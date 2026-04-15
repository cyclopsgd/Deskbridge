using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Services;
using Deskbridge.Tests.Fixtures;

namespace Deskbridge.Tests.Security;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-02 / SEC-04 / Pitfall 5 Option A) behavioural tests
/// for <see cref="AppLockController"/>. All tests run on STA threads because
/// <see cref="Grid"/>, <see cref="UIElement"/>, <see cref="WindowsFormsHost"/>
/// all require a pumped WPF dispatcher + STA apartment.
///
/// <para>Tests that would require a real <see cref="Wpf.Ui.Controls.ContentDialog"/>
/// to render (e.g. the ShowAsync path) are covered by the UAT files, not unit tests.</para>
/// </summary>
[Collection("RDP-STA")]
public sealed class AppLockControllerTests
{
    private readonly StaCollectionFixture _fixture;
    public AppLockControllerTests(StaCollectionFixture fixture) => _fixture = fixture;

    // --------------------------------------------------------------------
    // Test 1 — LockAsync: flips IAppLockState, publishes AppLockedEvent,
    //          collapses every HostContainer child, writes audit record.
    // --------------------------------------------------------------------
    [Fact]
    public async Task LockAsync_FlipsState_CollapsesAllChildren_WritesAudit()
    {
        _ = _fixture;
        await StaRunnerAsync(async () =>
        {
            var harness = BuildHarness(out var deps);

            // Populate HostContainer with 2 children: one Visible, one Collapsed.
            var visibleHost = new WindowsFormsHost { Visibility = Visibility.Visible };
            var collapsedHost = new WindowsFormsHost { Visibility = Visibility.Collapsed };
            harness.Grid.Children.Add(visibleHost);
            harness.Grid.Children.Add(collapsedHost);

            // Pre-lock state
            deps.LockState.IsLocked.Should().BeFalse();

            // Act — but DO NOT await the dialog ShowAsync (it requires a real dialog host).
            // Instead, call the internal-scope state-flip logic via the Controller's public
            // LockAsync. The ShowLockOverlayAsync try/catch swallows the host failure, so
            // the state-flip + audit DO complete synchronously before ShowAsync runs.
            await harness.Controller.LockAsync(LockReason.Manual);

            deps.LockState.IsLocked.Should().BeTrue();
            visibleHost.Visibility.Should().Be(Visibility.Collapsed,
                "Pitfall 5 Option A: lock must collapse every WFH child regardless of prior state");
            collapsedHost.Visibility.Should().Be(Visibility.Collapsed);

            await deps.Audit.Received(1).LogAsync(
                Arg.Is<AuditRecord>(r =>
                    r.Type == AuditAction.AppLocked.ToString() &&
                    r.ErrorCode == LockReason.Manual.ToString()),
                Arg.Any<CancellationToken>());
        });
    }

    // --------------------------------------------------------------------
    // Test 2 — UnlockAsync restores per-child Visibility (NOT all-visible).
    // --------------------------------------------------------------------
    [Fact]
    public async Task UnlockAsync_RestoresPerChildVisibility_NotAllVisible()
    {
        _ = _fixture;
        await StaRunnerAsync(async () =>
        {
            var harness = BuildHarness(out var deps);

            var visibleHost = new WindowsFormsHost { Visibility = Visibility.Visible };
            var collapsedHost = new WindowsFormsHost { Visibility = Visibility.Collapsed };
            harness.Grid.Children.Add(visibleHost);
            harness.Grid.Children.Add(collapsedHost);

            await harness.Controller.LockAsync(LockReason.Manual);
            await harness.Controller.UnlockAsync();

            visibleHost.Visibility.Should().Be(Visibility.Visible, "prior-Visible restored");
            collapsedHost.Visibility.Should().Be(Visibility.Collapsed,
                "prior-Collapsed restored to Collapsed — NOT flipped to Visible");
            deps.LockState.IsLocked.Should().BeFalse();

            await deps.Audit.Received(1).LogAsync(
                Arg.Is<AuditRecord>(r => r.Type == AuditAction.AppUnlocked.ToString()),
                Arg.Any<CancellationToken>());
        });
    }

    // --------------------------------------------------------------------
    // Test 3 — D-18 idempotency: LockAsync called twice only produces one audit.
    // --------------------------------------------------------------------
    [Fact]
    public async Task LockAsync_Idempotent_WhenAlreadyLocked_WritesOnlyOneAudit()
    {
        _ = _fixture;
        await StaRunnerAsync(async () =>
        {
            var harness = BuildHarness(out var deps);
            harness.Grid.Children.Add(new WindowsFormsHost());

            await harness.Controller.LockAsync(LockReason.Manual);
            await harness.Controller.LockAsync(LockReason.Timeout);

            await deps.Audit.Received(1).LogAsync(
                Arg.Is<AuditRecord>(r => r.Type == AuditAction.AppLocked.ToString()),
                Arg.Any<CancellationToken>());
        });
    }

    // --------------------------------------------------------------------
    // Test 4 — UnlockAsync is a no-op when already unlocked.
    // --------------------------------------------------------------------
    [Fact]
    public async Task UnlockAsync_Idempotent_WhenAlreadyUnlocked_DoesNothing()
    {
        _ = _fixture;
        await StaRunnerAsync(async () =>
        {
            var harness = BuildHarness(out var deps);

            await harness.Controller.UnlockAsync();

            deps.LockState.IsLocked.Should().BeFalse();
            await deps.Audit.DidNotReceiveWithAnyArgs().LogAsync(default!, default);
        });
    }

    // --------------------------------------------------------------------
    // Test 5 — Bus subscription routes AppLockedEvent through LockAsync.
    //          IdleLockService + SessionLockService both publish that event;
    //          this test confirms the controller subscribes in its ctor.
    // --------------------------------------------------------------------
    [Fact]
    public async Task Constructor_SubscribesToAppLockedEvent_ViaEventBus()
    {
        _ = _fixture;
        await StaRunnerAsync(async () =>
        {
            var harness = BuildHarness(out var deps);
            harness.Grid.Children.Add(new WindowsFormsHost());

            // Publish on the bus — the controller ctor subscribed to AppLockedEvent.
            deps.Bus.Publish(new AppLockedEvent(LockReason.Timeout));

            // The handler kicks off LockAsync asynchronously (via _ = LockAsync).
            // Pump the dispatcher to let the continuation run.
            PumpDispatcher();

            // Wait up to 500ms for the state flip — the ShowAsync path yields
            // internally before returning, and the state flip happens BEFORE
            // the yield, but to be safe we let the whole method complete.
            for (int i = 0; i < 20 && !deps.LockState.IsLocked; i++)
            {
                await Task.Delay(25);
                PumpDispatcher();
            }

            deps.LockState.IsLocked.Should().BeTrue("the controller subscribes to the bus in its ctor");
        });
    }

    // --------------------------------------------------------------------
    // Test 6 — Source-grep for Pitfall 5 Option A mitigation + bus subscription.
    // --------------------------------------------------------------------
    [Fact]
    public void AppLockController_Source_ContainsPitfall5Mitigation()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var svcCs = System.IO.File.ReadAllText(System.IO.Path.Combine(solutionRoot,
            "src", "Deskbridge", "Services", "AppLockController.cs"));

        svcCs.Should().Contain("_preLockVisibility",
            "Pitfall 5 Option A: snapshot every child's Visibility into a dict");
        svcCs.Should().Contain("Visibility.Collapsed",
            "Pitfall 5 Option A: collapse every WFH child before showing the overlay");
        svcCs.Should().Contain("_bus.Subscribe<AppLockedEvent>",
            "Bus fan-in: IdleLockService + SessionLockService both publish AppLockedEvent");
    }

    // --------------------------------------------------------------------
    // helpers
    // --------------------------------------------------------------------

    private sealed class TestHostContainerProvider(Grid grid) : IHostContainerProvider
    {
        public System.Windows.Controls.Panel HostContainer { get; } = grid;
    }

    private sealed record Harness(Grid Grid, AppLockController Controller);

    private sealed class Deps
    {
        public required IAppLockState LockState;
        public required IEventBus Bus;
        public required Wpf.Ui.IContentDialogService DialogService;
        public required IAuditLogger Audit;
        public required IMasterPasswordService MasterPassword;
    }

    private static Harness BuildHarness(out Deps deps)
    {
        var grid = new Grid();

        // Real IAppLockState — the controller reads IsLocked for idempotency.
        var lockState = new Deskbridge.Core.Services.AppLockState();

        // Real EventBus — the bus subscription in the ctor must actually deliver.
        var bus = new Deskbridge.Core.Services.EventBus();

        var dialogSvc = Substitute.For<Wpf.Ui.IContentDialogService>();
        var audit = Substitute.For<IAuditLogger>();
        var masterPw = Substitute.For<IMasterPasswordService>();
        masterPw.IsMasterPasswordSet().Returns(true);  // unlock-mode dialog

        deps = new Deps
        {
            LockState = lockState,
            Bus = bus,
            DialogService = dialogSvc,
            Audit = audit,
            MasterPassword = masterPw,
        };

        // Factory returns a null dialog — ShowAsync path will fail inside
        // ShowLockOverlayAsync, which is wrapped in try/catch + Log.Error so the
        // state-flip and audit paths complete before the dialog logic runs.
        Func<Deskbridge.Dialogs.LockOverlayDialog> dialogFactory = () =>
            throw new InvalidOperationException("test harness — dialog show path not exercised");

        var provider = new TestHostContainerProvider(grid);
        var controller = new AppLockController(
            lockState, bus, dialogSvc, audit, dialogFactory, provider, masterPw);

        return new Harness(grid, controller);
    }

    private static async Task StaRunnerAsync(Func<Task> body)
    {
        StaRunner.RunAsync(body);
        await Task.CompletedTask;
    }

    private static void PumpDispatcher()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static string FindSolutionRoot(string startPath)
    {
        var dir = new System.IO.DirectoryInfo(startPath);
        while (dir is not null)
        {
            if (dir.GetFiles("Deskbridge.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate Deskbridge.sln from {startPath}");
    }
}
