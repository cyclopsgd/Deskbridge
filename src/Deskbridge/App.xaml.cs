using System.IO;
using System.Windows.Media;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Logging;
using Deskbridge.Core.Models;
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

        // Credential Guard fix: migrate legacy TERMSRV/* entries to DESKBRIDGE/CONN/* targets.
        // One-time idempotent migration -- skips connections that already have new-format credentials.
        // Must run after IConnectionStore.Load() (which happens in the factory lambda above)
        // and before any connection attempts.
        var credService = _serviceProvider.GetRequiredService<ICredentialService>();
        if (credService is WindowsCredentialService winCredService)
        {
            var store = _serviceProvider.GetRequiredService<IConnectionStore>();
            winCredService.MigrateFromTermsrv(store);
        }

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

        // Phase 6 Plan 06-04 (SEC-03 / SEC-04): eager-resolve the lock-trigger
        // services so their subscriptions (InputManager.PreProcessInput,
        // SystemEvents.SessionSwitch) land BEFORE the user can interact with the
        // window. Without eager resolution these singletons only construct on
        // first lazy request — AppLockController's bus subscription would work,
        // but the trigger services would miss every pre-first-request event.
        _ = _serviceProvider.GetRequiredService<IdleLockService>();
        _ = _serviceProvider.GetRequiredService<SessionLockService>();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // Phase 6 Plan 06-04 (SEC-02): resolve AppLockController AFTER Show() because
        // the controller takes IHostContainerProvider (the MainWindow), and because
        // it needs the visual tree realized before capturing children on the first
        // EnsureLockedOnStartupAsync call. Controller subscribes to AppLockedEvent
        // on the bus in its ctor, so resolving here also activates the fan-in.
        var lockController = _serviceProvider.GetRequiredService<AppLockController>();

        // Phase 6.1: wire the LockController reference on MainWindow so the
        // RequireMasterPassword toggle can update it at runtime.
        mainWindow.LockController = lockController;

        // Phase 7 Plan 07-04 (MIG-02): wire the import wizard factory on MainWindow
        // so the settings panel import button can open the wizard.
        mainWindow.SetImportWizardFactory(_serviceProvider.GetRequiredService<Func<ImportWizardDialog>>());

        // Startup lock — fire-and-forget so OnStartup returns and the dispatcher
        // can render the overlay. EnsureLockedOnStartupAsync handles both the
        // returning-user (unlock mode) and first-run (setup mode) flows by
        // reading IMasterPasswordService.IsMasterPasswordSet inside the VM.
        _ = lockController.EnsureLockedOnStartupAsync();

        // Phase 7 Plan 07-01 (UPD-01): silent update check on startup.
        // Runs on a background thread; UpdateAvailableEvent marshals to UI
        // via the event bus subscription in MainWindowViewModel.
        var updateService = _serviceProvider.GetRequiredService<IUpdateService>();
        if (updateService.IsInstalled)
        {
            _ = Task.Run(() => updateService.CheckForUpdatesAsync());
        }
        else
        {
            Log.Information("Update check skipped: app not installed via Velopack (dev mode)");
        }
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

        // Credential prompt dialog service — CredentialMode.Prompt shows a ContentDialog
        // for one-time credentials (not persisted to Credential Manager).
        services.AddSingleton<ICredentialPromptService, CredentialPromptService>();

        // Phase 6 Plan 06-02 (NOTF-04): window + security settings persistence.
        services.AddSingleton<IWindowStateService, WindowStateService>();

        // ---- Phase 7 Plan 07-01: auto-update (UPD-01 / UPD-02 / UPD-03) ----
        services.AddSingleton<IUpdateService>(sp =>
        {
            var bus = sp.GetRequiredService<IEventBus>();
            var windowState = sp.GetRequiredService<IWindowStateService>();
            var appSettings = windowState.LoadAsync().GetAwaiter().GetResult();
            var useBeta = appSettings.Update?.UseBetaChannel ?? false;
            return new UpdateService(bus, "https://github.com/cyclopsgd/Deskbridge", useBeta);
        });

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
        // Phase 7 Plan 07-04: adds import + export commands via optional parameters.
        services.AddSingleton<ICommandPaletteService>(sp =>
        {
            var main = sp.GetRequiredService<MainWindowViewModel>();
            var tree = sp.GetRequiredService<ConnectionTreeViewModel>();
            var connStore = sp.GetRequiredService<IConnectionStore>();
            var importerFactory = sp.GetRequiredService<Func<ImportWizardDialog>>();
            var contentDialog = sp.GetRequiredService<IContentDialogService>();
            return new CommandPaletteService(
                newConnection: () => tree.NewConnectionCommand.ExecuteAsync(null),
                openSettings: () =>
                {
                    main.ActivePanelMode = PanelMode.Settings;
                    return Task.CompletedTask;
                },
                disconnectAll: () => main.DisconnectAllCommand.ExecuteAsync(null),
                quickConnect: () => main.QuickConnectCommand.ExecuteAsync(null),
                importConnections: async () =>
                {
                    var dialog = importerFactory();
                    await dialog.ShowAsync();
                },
                exportJson: async () =>
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "JSON Files (*.json)|*.json",
                        FileName = "deskbridge-connections.json"
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        var json = ConnectionExporter.ExportJson(connStore.GetAll(), connStore.GetGroups());
                        await File.WriteAllTextAsync(dlg.FileName, json);
                    }
                },
                exportCsv: async () =>
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "CSV Files (*.csv)|*.csv",
                        FileName = "deskbridge-connections.csv"
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        var csv = ConnectionExporter.ExportCsv(connStore.GetAll(), connStore.GetGroups());
                        await File.WriteAllTextAsync(dlg.FileName, csv);
                    }
                });
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

        // ---- Phase 7 Plan 07-04: import wizard + export (MIG-02 / MIG-05) ----
        //
        // IConnectionImporter: all importers registered (multi-source architecture).
        // Phase 7 ships MRemoteNGImporter only; future importers add registrations here.
        services.AddSingleton<IConnectionImporter, MRemoteNGImporter>();

        // ImportWizardViewModel: transient — fresh per wizard session so
        // step state and preview items are clean each open.
        services.AddTransient<ImportWizardViewModel>(sp =>
            new ImportWizardViewModel(
                sp.GetServices<IConnectionImporter>().ToList(),
                sp.GetRequiredService<IConnectionStore>(),
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<IAuditLogger>()));

        // ImportWizardDialog: transient — one dialog per import session.
        services.AddTransient<ImportWizardDialog>(sp =>
            new ImportWizardDialog(
                sp.GetRequiredService<MainWindow>().FindName("RootContentDialog") as Wpf.Ui.Controls.ContentDialogHost
                    ?? throw new InvalidOperationException("RootContentDialog host not found"),
                sp.GetRequiredService<ImportWizardViewModel>()));

        // Factory so command palette + settings panel can open the import wizard
        // without holding a reference to IServiceProvider.
        services.AddTransient<Func<ImportWizardDialog>>(sp =>
            () => sp.GetRequiredService<ImportWizardDialog>());

        // ---- Phase 6 Plan 06-04: app security (SEC-01..SEC-05) ----
        //
        // IMasterPasswordService singleton: single writer to auth.json. The service
        // takes a directory path — we resolve the %AppData%/Deskbridge root once.
        services.AddSingleton<IMasterPasswordService>(_ =>
            new MasterPasswordService(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Deskbridge")));

        // LockOverlayViewModel + LockOverlayDialog are transient — a fresh VM +
        // dialog per lock event so the password field starts blank and IsFirstRun
        // is recomputed from IMasterPasswordService.IsMasterPasswordSet() each time.
        services.AddTransient<LockOverlayViewModel>();
        services.AddTransient<LockOverlayDialog>();
        services.AddTransient<Func<LockOverlayDialog>>(sp =>
            () => sp.GetRequiredService<LockOverlayDialog>());

        // Idle-timer + session-switch services are singletons — they own a
        // DispatcherTimer + InputManager subscription + SystemEvents subscription
        // that must live for the process lifetime. App.OnStartup eager-resolves both
        // so the subscriptions land before the user can interact with the window.
        //
        // NOTE: SecuritySettingsRecord is passed by VALUE — changes made through the
        // settings panel at runtime do NOT automatically update the IdleLockService
        // interval. A restart picks up the new interval. A future plan could add a
        // "settings changed" bus event to hot-reload without restart.
        services.AddSingleton<IdleLockService>(sp =>
        {
            var bus = sp.GetRequiredService<IEventBus>();
            var windowState = sp.GetRequiredService<IWindowStateService>();
            // Load synchronously at ctor time — this service is eager-resolved from
            // OnStartup on the UI thread so the sync-over-async is acceptable (< 1 KB
            // read from %AppData%).
            var settings = windowState.LoadAsync().GetAwaiter().GetResult();
            return new IdleLockService(bus, settings.Security);
        });
        services.AddSingleton<SessionLockService>(sp =>
        {
            var windowState = sp.GetRequiredService<IWindowStateService>();
            var settings = windowState.LoadAsync().GetAwaiter().GetResult();
            return new SessionLockService(
                sp.GetRequiredService<IEventBus>(),
                requireMasterPassword: settings.Security.RequireMasterPassword);
        });

        // AppLockController: singleton factory that takes the MainWindow (as
        // IHostContainerProvider) — resolved at service-build time, so the
        // controller sees the already-constructed window. The controller
        // subscribes to AppLockedEvent in its ctor; ALL lock triggers (bus
        // events + Ctrl+L direct call + startup) fan in here.
        services.AddSingleton<AppLockController>(sp =>
        {
            var windowState = sp.GetRequiredService<IWindowStateService>();
            var settings = windowState.LoadAsync().GetAwaiter().GetResult();
            return new AppLockController(
                sp.GetRequiredService<IAppLockState>(),
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<IContentDialogService>(),
                sp.GetRequiredService<IAuditLogger>(),
                sp.GetRequiredService<Func<LockOverlayDialog>>(),
                sp.GetRequiredService<MainWindow>(),
                sp.GetRequiredService<IMasterPasswordService>(),
                requireMasterPassword: settings.Security.RequireMasterPassword);
        });

        // ViewModels
        //
        // MainWindowViewModel: factory registration so Plan 06-04's optional
        // IWindowStateService ctor param is resolved (DI's default binder ignores
        // optional ref-type params with a null default). AppLockController is NOT
        // passed to the VM to avoid a DI cycle (controller takes MainWindow as
        // IHostContainerProvider which takes this VM as DataContext). The LockApp
        // command publishes on the bus — AppLockController subscribes.
        services.AddSingleton<ViewModels.MainWindowViewModel>(sp => new ViewModels.MainWindowViewModel(
            sp.GetRequiredService<ViewModels.ConnectionTreeViewModel>(),
            sp.GetRequiredService<ITabHostManager>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<IConnectionStore>(),
            sp.GetRequiredService<ViewModels.ToastStackViewModel>(),
            windowState: sp.GetRequiredService<IWindowStateService>(),
            masterPassword: sp.GetRequiredService<IMasterPasswordService>(),
            updateService: sp.GetRequiredService<IUpdateService>()));
        // Phase 6.1: change password/PIN dialog
        services.AddTransient<ViewModels.ChangePasswordViewModel>();
        services.AddTransient<Dialogs.ChangePasswordDialog>();
        services.AddTransient<Func<Dialogs.ChangePasswordDialog>>(sp =>
            () => sp.GetRequiredService<Dialogs.ChangePasswordDialog>());

        services.AddSingleton<ViewModels.ConnectionTreeViewModel>();
        services.AddTransient<ViewModels.ConnectionEditorViewModel>();
        services.AddTransient<ViewModels.GroupEditorViewModel>();

        // Views
        services.AddSingleton<Views.ConnectionTreeControl>();
        services.AddTransient<Dialogs.ConnectionEditorDialog>();
        services.AddTransient<Dialogs.GroupEditorDialog>();
        services.AddSingleton<MainWindow>(sp => new MainWindow(
            sp.GetRequiredService<ViewModels.MainWindowViewModel>(),
            sp.GetRequiredService<ISnackbarService>(),
            sp.GetRequiredService<IContentDialogService>(),
            sp.GetRequiredService<Views.ConnectionTreeControl>(),
            sp.GetRequiredService<IConnectionCoordinator>(),
            sp.GetRequiredService<AirspaceSwapper>(),
            sp.GetRequiredService<ITabHostManager>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<IWindowStateService>(),
            sp.GetRequiredService<IAppLockState>(),
            sp.GetRequiredService<Func<CommandPaletteDialog>>(),
            sp.GetRequiredService<Func<ChangePasswordDialog>>(),
            sp.GetRequiredService<IMasterPasswordService>()));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
