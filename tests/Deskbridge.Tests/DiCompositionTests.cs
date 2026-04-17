using System.IO;
using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Services;
using Deskbridge.Services;
using Deskbridge.Tests.Fixtures;
using Deskbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wpf.Ui;

namespace Deskbridge.Tests;

public sealed class DiCompositionTests
{
    [Fact]
    public void AllCoreServices_ResolveWithoutThrowing()
    {
        var services = new ServiceCollection();

        // Mirror the exact registrations from App.xaml.cs ConfigureServices
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<IConnectionPipeline, ConnectionPipeline>();
        services.AddSingleton<IDisconnectPipeline, DisconnectPipeline>();
        services.AddSingleton<IConnectionQuery, ConnectionQueryService>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEventBus>().Should().NotBeNull();
        provider.GetRequiredService<INotificationService>().Should().NotBeNull();
        provider.GetRequiredService<IAuditLogger>().Should().NotBeNull();
        provider.GetRequiredService<IConnectionPipeline>().Should().NotBeNull();
        provider.GetRequiredService<IDisconnectPipeline>().Should().NotBeNull();
        provider.GetRequiredService<IConnectionQuery>().Should().NotBeNull();
    }

    /// <summary>
    /// Phase 6 (Plan 06-01) LOG-02: <see cref="IAuditLogger"/> is registered as a
    /// singleton. The single-instance guarantee matters because the SemaphoreSlim
    /// inside <see cref="AuditLogger"/> only serialises writes against ITS OWN
    /// instance — multiple instances would re-introduce Pitfall 2 (interleaved lines).
    /// </summary>
    [Fact]
    public void IAuditLogger_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuditLogger, AuditLogger>();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAuditLogger>();
        var second = provider.GetRequiredService<IAuditLogger>();

        first.Should().BeOfType<AuditLogger>();
        ReferenceEquals(first, second).Should().BeTrue(
            "AuditLogger must be a singleton — only one writer per process holds the SemaphoreSlim");
    }

    /// <summary>
    /// Phase 6 Plan 06-02 (NOTF-04 / NOTF-01 / NOTF-03): settings persistence +
    /// toast stack resolve as singletons. Singleton matters for all three:
    /// <see cref="IWindowStateService"/> (single serialiser for atomic writes),
    /// <see cref="ToastStackViewModel"/> (shared Items collection bound to XAML
    /// AND written by the subscription service), <see cref="ToastSubscriptionService"/>
    /// (bus subscriptions must be process-lifetime).
    /// </summary>
    [Fact]
    public void NOTF_Services_ResolveAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<IWindowStateService, WindowStateService>();
        services.AddSingleton<ToastStackViewModel>();
        services.AddSingleton<ToastSubscriptionService>();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IWindowStateService>().Should().BeOfType<WindowStateService>();
        ReferenceEquals(
            provider.GetRequiredService<IWindowStateService>(),
            provider.GetRequiredService<IWindowStateService>())
            .Should().BeTrue("IWindowStateService must be a singleton");
        ReferenceEquals(
            provider.GetRequiredService<ToastStackViewModel>(),
            provider.GetRequiredService<ToastStackViewModel>())
            .Should().BeTrue("ToastStackViewModel must be a singleton — the XAML binding and the subscription service must share the Items collection");
        ReferenceEquals(
            provider.GetRequiredService<ToastSubscriptionService>(),
            provider.GetRequiredService<ToastSubscriptionService>())
            .Should().BeTrue("ToastSubscriptionService must be a singleton — subscriptions are process-lifetime");
    }

    /// <summary>
    /// Phase 6 Plan 06-02: source-order regression — App.xaml.cs OnStartup must
    /// eager-resolve ToastSubscriptionService AFTER ITabHostManager and BEFORE
    /// mainWindow.Show(). Protects against refactors that accidentally reorder the
    /// eager-resolve block and let an early event fire without a subscriber.
    /// </summary>
    [Fact]
    public void App_EagerResolvesToastSubscriptionService_AfterTabHostManager_BeforeShow()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var appCs = File.ReadAllText(Path.Combine(solutionRoot, "src", "Deskbridge", "App.xaml.cs"));

        var tabHostIdx = appCs.IndexOf("GetRequiredService<ITabHostManager>()", StringComparison.Ordinal);
        var toastIdx = appCs.IndexOf("GetRequiredService<ToastSubscriptionService>()", StringComparison.Ordinal);
        var showIdx = appCs.IndexOf("mainWindow.Show()", StringComparison.Ordinal);

        tabHostIdx.Should().BeGreaterThan(-1);
        toastIdx.Should().BeGreaterThan(tabHostIdx, "ToastSubscriptionService must resolve AFTER ITabHostManager");
        showIdx.Should().BeGreaterThan(toastIdx, "ToastSubscriptionService must resolve BEFORE mainWindow.Show()");
    }

    /// <summary>
    /// Phase 6 Plan 06-02 (NOTF-04): MainWindow.OnSourceInitialized must hydrate
    /// window state BEFORE _airspace.AttachToWindow so the window has correct bounds
    /// when the HwndSource is realised (attaching airspace before resizing would
    /// cause the snapshot bitmap to be captured at the wrong size).
    /// </summary>
    [Fact]
    public void MainWindow_OnSourceInitialized_LoadsWindowState_BeforeAirspaceAttach()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var mwCs = File.ReadAllText(Path.Combine(solutionRoot, "src", "Deskbridge", "MainWindow.xaml.cs"));

        var loadIdx = mwCs.IndexOf("_windowState.LoadAsync", StringComparison.Ordinal);
        var airspaceIdx = mwCs.IndexOf("_airspace.AttachToWindow(this)", StringComparison.Ordinal);

        loadIdx.Should().BeGreaterThan(-1, "MainWindow.OnSourceInitialized must call _windowState.LoadAsync");
        airspaceIdx.Should().BeGreaterThan(loadIdx, "LoadAsync must run BEFORE AttachToWindow");
    }

    /// <summary>
    /// Phase 6 Plan 06-02 (NOTF-04): MainWindow.OnClosing must save window state on
    /// both the first invocation (before the async shutdown begins) and the second
    /// invocation (after CloseAllAsync completes). TrySaveWindowState must appear
    /// twice in the OnClosing method body.
    /// </summary>
    [Fact]
    public void MainWindow_OnClosing_SavesWindowState_InBothInvocationPaths()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var mwCs = File.ReadAllText(Path.Combine(solutionRoot, "src", "Deskbridge", "MainWindow.xaml.cs"));

        // Count occurrences — the first appears inside the _shutdownInProgress branch,
        // the second BEFORE the async kickoff on first invocation.
        var count = 0;
        var idx = 0;
        while ((idx = mwCs.IndexOf("TrySaveWindowState()", idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += "TrySaveWindowState()".Length;
        }

        // Expect 3: two invocation sites in OnClosing + 1 method definition marker (private void TrySaveWindowState()).
        count.Should().BeGreaterThanOrEqualTo(3,
            "TrySaveWindowState must be invoked on the _shutdownInProgress path AND on the first-invocation path");
    }

    // ----------------------------------------------------------- Phase 6 Plan 06-03

    /// <summary>
    /// Phase 6 Plan 06-03 (Q6): <see cref="IAppLockState"/> must be a singleton so
    /// Plan 06-04's Lock/Unlock calls and MainWindow's Ctrl+Shift+P gate share the
    /// SAME IsLocked observation.
    /// </summary>
    [Fact]
    public void IAppLockState_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppLockState, AppLockState>();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAppLockState>();
        var second = provider.GetRequiredService<IAppLockState>();

        first.Should().BeOfType<AppLockState>();
        ReferenceEquals(first, second).Should().BeTrue(
            "IAppLockState must be a singleton — Plan 06-04 setter + Plan 06-03 gate must see the same state");
    }

    /// <summary>
    /// Phase 6 Plan 06-03 (CMD-01): MainWindow.OnPreviewKeyDown must gate
    /// Ctrl+Shift+P with <c>_lockState.IsLocked</c> (Q6) before opening the
    /// palette. Asserts the guard is present by source-grep — the alternative
    /// (instantiating MainWindow) is impractical under test.
    /// </summary>
    [Fact]
    public void MainWindow_CtrlShiftP_Gated_By_LockState_IsLocked()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var mwCs = File.ReadAllText(Path.Combine(solutionRoot, "src", "Deskbridge", "MainWindow.xaml.cs"));

        // The gate must appear between the Ctrl+Shift+P key match and the palette
        // open call. Verify both the key match and the IsLocked check exist.
        mwCs.Should().Contain("Key.P", "Ctrl+Shift+P handling must exist in OnPreviewKeyDown");
        mwCs.Should().Contain("ModifierKeys.Control | ModifierKeys.Shift");
        mwCs.Should().Contain("_lockState.IsLocked",
            "Q6 gate: the palette must be suppressed when the app is locked");
        mwCs.Should().Contain("OpenCommandPaletteAsync",
            "MainWindow must have a dedicated palette-open method");
    }

    /// <summary>
    /// Phase 6 Plan 06-03 CMD-01: CommandPaletteDialog must have the Pitfall 8
    /// mitigation (Enter inside a TextBox routes to ExecuteSelectedAsync rather
    /// than firing a phantom PrimaryButton per WPF-UI Issue #1404).
    /// </summary>
    [Fact]
    public void CommandPaletteDialog_Has_Pitfall8_EnterHandler()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var dlgCs = File.ReadAllText(Path.Combine(solutionRoot, "src", "Deskbridge",
            "Dialogs", "CommandPaletteDialog.xaml.cs"));

        dlgCs.Should().Contain("Dialog_PreviewKeyDown");
        dlgCs.Should().Contain("Key.Enter");
        dlgCs.Should().Contain("TextBoxBase");
        dlgCs.Should().Contain("ExecuteSelectedAsync");
        dlgCs.Should().Contain("e.Handled = true");

        // And the XAML must declare IsFooterVisible="False"
        var dlgXaml = File.ReadAllText(Path.Combine(solutionRoot, "src", "Deskbridge",
            "Dialogs", "CommandPaletteDialog.xaml"));
        dlgXaml.Should().Contain("IsFooterVisible=\"False\"");
    }

    /// <summary>
    /// Phase 6 Plan 06-03 CMD-02 source check: CommandEntry ctor invocations inside
    /// CommandPaletteService — safer than reflecting on the runtime type because it
    /// catches regressions that add command rows without a corresponding test update.
    /// Phase 7 Plan 07-04 added 3 optional import/export commands (7 total).
    /// </summary>
    [Fact]
    public void CommandPaletteService_Source_Registers_SevenCommands()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var svcCs = File.ReadAllText(Path.Combine(solutionRoot, "src", "Deskbridge.Core",
            "Services", "CommandPaletteService.cs"));

        // Count ctor invocations `new(` at the start of a new CommandEntry record — the
        // service source uses the positional syntax `new(\n    Id: "...", ...`.
        var count = System.Text.RegularExpressions.Regex.Matches(svcCs, @"new\(\s*\n\s*Id:").Count;

        count.Should().Be(7, "D-04: 4 core + 3 Phase 7 import/export palette commands must be registered");
    }

    // ----------------------------------------------------------- Phase 7 Plan 07-01

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-01): <see cref="IUpdateService"/> is registered as
    /// a singleton in App.xaml.cs. Verified via source-grep because the real
    /// <see cref="UpdateService"/> constructor requires <c>VelopackApp.Build()</c>
    /// to have been called (not possible in a unit test environment).
    /// </summary>
    [Fact]
    public void IUpdateService_IsRegistered_InAppXamlCs()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var appCs = File.ReadAllText(Path.Combine(solutionRoot, "src", "Deskbridge", "App.xaml.cs"));

        appCs.Should().Contain("IUpdateService", "IUpdateService must be registered in DI");
        appCs.Should().Contain("new UpdateService(", "UpdateService concrete type must be constructed");
        appCs.Should().Contain("github.com", "UpdateService must target a GitHub repository");
    }

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-01): App.OnStartup must trigger a background update
    /// check AFTER mainWindow.Show() so the window is visible when the toast fires.
    /// </summary>
    [Fact]
    public void App_OnStartup_TriggersUpdateCheck_AfterShow()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var appCs = File.ReadAllText(Path.Combine(solutionRoot, "src", "Deskbridge", "App.xaml.cs"));

        var showIdx = appCs.IndexOf("mainWindow.Show()", StringComparison.Ordinal);
        var checkIdx = appCs.IndexOf("CheckForUpdatesAsync", StringComparison.Ordinal);

        showIdx.Should().BeGreaterThan(-1, "mainWindow.Show() must exist");
        checkIdx.Should().BeGreaterThan(showIdx, "Update check must run AFTER mainWindow.Show()");
    }

    private static string FindSolutionRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir is not null)
        {
            if (dir.GetFiles("Deskbridge.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate Deskbridge.sln from {startPath}");
    }

    /// <summary>
    /// Phase 5 D-01: <see cref="ITabHostManager"/> is registered as a singleton.
    /// Two resolutions return the SAME instance so event-bus subscriptions are
    /// process-lifetime (Pitfall 7).
    /// </summary>
    [Collection("RDP-STA")]
    public sealed class TabHostManagerDiTests
    {
        private readonly StaCollectionFixture _fixture;
        public TabHostManagerDiTests(StaCollectionFixture fixture) => _fixture = fixture;

        [Fact]
        public void ITabHostManager_IsRegistered_AsSingleton()
        {
            _ = _fixture;
            StaRunner.Run(() =>
            {
                using var provider = BuildProviderWithTabHostManager();

                var first = provider.GetRequiredService<ITabHostManager>();
                var second = provider.GetRequiredService<ITabHostManager>();

                ReferenceEquals(first, second).Should().BeTrue(
                    "Phase 5 D-01: ITabHostManager must be a singleton so its coordinator subscriptions are process-lifetime");
            });
        }

        /// <summary>
        /// No circular dependency: the publisher-side switch-to-existing fix in Plan 01
        /// (ConnectionTreeViewModel depends on ITabHostManager — not the other way) broke
        /// the cycle that would have arisen if both singletons injected each other.
        /// </summary>
        [Fact]
        public void FullComposition_DoesNotThrow_CircularDependency()
        {
            _ = _fixture;
            StaRunner.Run(() =>
            {
                using var provider = BuildProviderWithTabHostManager();

                var tabHost = provider.GetRequiredService<ITabHostManager>();
                var coord = provider.GetRequiredService<IConnectionCoordinator>();

                tabHost.Should().NotBeNull();
                coord.Should().NotBeNull();
            });
        }

        private static ServiceProvider BuildProviderWithTabHostManager()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IEventBus, EventBus>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<IConnectionPipeline, ConnectionPipeline>();
            services.AddSingleton<IDisconnectPipeline, DisconnectPipeline>();
            services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
            services.AddSingleton<ISnackbarService, SnackbarService>();
            services.AddSingleton<IConnectionCoordinator, ConnectionCoordinator>();
            services.AddSingleton<ITabHostManager, TabHostManager>();
            // Provide the dispatcher so TabHostManager resolves cleanly under test.
            services.AddSingleton(Dispatcher.CurrentDispatcher);
            return services.BuildServiceProvider();
        }
    }
}
