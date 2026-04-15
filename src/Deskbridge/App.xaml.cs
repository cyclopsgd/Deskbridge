using System.IO;
using System.Windows.Media;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Logging;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;
using Deskbridge.Core.Services;
using Deskbridge.Dialogs;
using Deskbridge.Models;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Services;
using Deskbridge.ViewModels;
using Microsoft.Extensions.Logging;
using Serilog;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Deskbridge;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    /// <summary>
    /// Phase 6 Plan 06-04 (LOG-04): exposes the DI container so
    /// <see cref="CrashHandler.TryShowCrashDialog"/> can resolve
    /// <see cref="IContentDialogService"/> without taking a static dependency
    /// on the service collection. Nullable because a crash during App ctor
    /// would fire BEFORE the provider is built.
    /// </summary>
    internal IServiceProvider? Services => _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // LOG-04 / Pattern 4: attach Dispatcher hook now that Application.Current is valid.
        // AppDomain + UnobservedTask hooks were installed in Program.Main before App ctor.
        CrashHandler.InstallDispatcherHook(this);

        // --- LOG-01 + LOG-05 Serilog config (replaces Phase 4 baseline) ---
        // Dispose any previous logger (Velopack / earlier startup may have created one).
        (Log.Logger as IDisposable)?.Dispose();
        var logRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Deskbridge", "logs");
        Log.Logger = SerilogSetup.Configure(logRoot).CreateLogger();

        ApplicationThemeManager.Apply(
            ApplicationTheme.Dark,
            WindowBackdropType.Mica,
            updateAccent: true
        );

        // Override system accent with Deskbridge brand colour #007ACC (per SHEL-08)
        ApplicationAccentColorManager.Apply(
            Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC),
            ApplicationTheme.Dark
        );

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Wire stages into pipelines (must happen after container build)
        var connectPipeline = _serviceProvider.GetRequiredService<IConnectionPipeline>();
        foreach (var stage in _serviceProvider.GetServices<IConnectionPipelineStage>())
        {
            connectPipeline.AddStage(stage);
        }

        var disconnectPipeline = _serviceProvider.GetRequiredService<IDisconnectPipeline>();
        foreach (var stage in _serviceProvider.GetServices<IDisconnectPipelineStage>())
        {
            disconnectPipeline.AddStage(stage);
        }

        // Resolve ConnectionCoordinator eagerly so it subscribes to the event bus
        _ = _serviceProvider.GetRequiredService<IConnectionCoordinator>();

        // Phase 5 (D-01): resolve TabHostManager eagerly so its HostMounted/HostUnmounted
        // subscriptions exist BEFORE the first ConnectionRequestedEvent fires.
        _ = _serviceProvider.GetRequiredService<ITabHostManager>();

        // Phase 6 Plan 06-02 (NOTF-01 / NOTF-03): eager-resolve ToastSubscriptionService
        // so its 6 bus subscriptions land BEFORE the first ConnectionRequestedEvent can
        // fire. Mirrors the ITabHostManager pattern above.
        _ = _serviceProvider.GetRequiredService<ToastSubscriptionService>();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<INotificationService, NotificationService>();
        // LOG-02 / LOG-03 audit logger — singleton (one writer per process so the
        // SemaphoreSlim guards monthly rotation across all bus consumers).
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<IConnectionPipeline, ConnectionPipeline>();
        services.AddSingleton<IDisconnectPipeline, DisconnectPipeline>();

        // Microsoft.Extensions.Logging backing (Serilog adapter) — Phase 4 baseline
        services.AddLogging(b => b.AddSerilog(dispose: false));

        // ---- Phase 4: RDP integration ----
        // Pipeline stages (connect)
        services.AddSingleton<IConnectionPipelineStage, ResolveCredentialsStage>();
        services.AddSingleton<IConnectionPipelineStage, CreateHostStage>();
        services.AddSingleton<IConnectionPipelineStage, ConnectStage>();
        services.AddSingleton<IConnectionPipelineStage, UpdateRecentsStage>();

        // Pipeline stages (disconnect)
        services.AddSingleton<IDisconnectPipelineStage, DisconnectStage>();
        services.AddSingleton<IDisconnectPipelineStage, DisposeStage>();
        services.AddSingleton<IDisconnectPipelineStage, PublishClosedEventStage>();

        // Protocol host factory + RDP impl
        services.AddSingleton<IProtocolHostFactory, RdpProtocolHostFactory>();

        // Connection coordinator (event-bus bridge — D-11 STA marshal + multi-host in Phase 5)
        services.AddSingleton<IConnectionCoordinator, ConnectionCoordinator>();

        // Phase 5 (D-01): multi-host tab manager. Singleton. Subscribes to the coordinator's
        // Host events in its ctor; resolve eagerly after build-service-provider so the
        // subscriptions land before the first ConnectionRequestedEvent.
        services.AddSingleton<ITabHostManager, TabHostManager>();

        // Reconnect coordinator (Plan 04-03 — D-03 backoff + D-05 cap). Injected into
        // ConnectionCoordinator via optional ctor parameter (DI resolves the concrete class).
        services.AddSingleton<RdpReconnectCoordinator>();

        // Reconnect overlay ViewModel is transient — one instance per reconnect episode,
        // constructed by MainWindow.OnReconnectOverlayRequested directly rather than DI
        // (needs per-request ConnectionName). Registration retained for future DI consumers.
        services.AddTransient<ViewModels.ReconnectOverlayViewModel>();

        // Airspace swapper (WM_ENTERSIZEMOVE bitmap-swap per D-13 + D-14)
        services.AddSingleton<AirspaceSwapper>();
        // ---- end Phase 4 ----

        // Connection persistence and credentials
        services.AddSingleton<IConnectionStore>(sp =>
        {
            var store = new JsonConnectionStore();
            store.Load();
            return store;
        });
        services.AddSingleton<ICredentialService, WindowsCredentialService>();
        services.AddSingleton<IConnectionQuery>(sp =>
            new ConnectionQueryService(sp.GetRequiredService<IConnectionStore>()));

        // WPF-UI services (for Phases 3+ and 6)
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        // Phase 6 Plan 06-02 (NOTF-04): window + security settings persistence.
        services.AddSingleton<IWindowStateService, WindowStateService>();

        // Phase 6 Plan 06-02 (NOTF-01 / NOTF-03): custom toast stack (Q1 Option B).
        // ToastStackViewModel and ToastSubscriptionService MUST be singletons so the
        // MainWindow.DataContext binding and ToastSubscriptionService push into the
        // SAME ObservableCollection — transient scope would silently swallow every
        // subscription-side push.
        services.AddSingleton<ToastStackViewModel>();
        services.AddSingleton<ToastSubscriptionService>();

        // Phase 6 Plan 06-03 (CMD-01 / CMD-02 / Q6): palette scaffolding.
        // IAppLockState singleton (Plan 06-04 flips IsLocked on startup).
        services.AddSingleton<IAppLockState, AppLockState>();

        // ICommandPaletteService singleton — command closures delegate to the
        // MainWindowViewModel / ConnectionTreeViewModel singletons so the palette
        // entries invoke the SAME commands as the global keyboard shortcuts.
        services.AddSingleton<ICommandPaletteService>(sp =>
        {
            var main = sp.GetRequiredService<MainWindowViewModel>();
            var tree = sp.GetRequiredService<ConnectionTreeViewModel>();
            return new CommandPaletteService(
                newConnection: () => tree.NewConnectionCommand.ExecuteAsync(null),
                openSettings: () =>
                {
                    main.ActivePanelMode = PanelMode.Settings;
                    return Task.CompletedTask;
                },
                disconnectAll: () => main.DisconnectAllCommand.ExecuteAsync(null),
                quickConnect: () => main.QuickConnectCommand.ExecuteAsync(null));
        });

        // CommandPaletteViewModel + CommandPaletteDialog are transient — one
        // instance per palette session (Ctrl+Shift+P creates a fresh VM each time
        // so SearchText starts blank and Items re-populates from current state).
        services.AddTransient<CommandPaletteViewModel>();
        services.AddTransient<CommandPaletteDialog>();

        // Factory so MainWindow can request a fresh dialog each open without
        // holding a reference to IServiceProvider directly.
        services.AddTransient<Func<CommandPaletteDialog>>(sp =>
            () => sp.GetRequiredService<CommandPaletteDialog>());

        // ViewModels
        services.AddSingleton<ViewModels.MainWindowViewModel>();
        services.AddSingleton<ViewModels.ConnectionTreeViewModel>();
        services.AddTransient<ViewModels.ConnectionEditorViewModel>();
        services.AddTransient<ViewModels.GroupEditorViewModel>();

        // Views
        services.AddSingleton<Views.ConnectionTreeControl>();
        services.AddTransient<Dialogs.ConnectionEditorDialog>();
        services.AddTransient<Dialogs.GroupEditorDialog>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
