using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Services;
using Deskbridge.Tests.Fixtures;
using Microsoft.Win32;

namespace Deskbridge.Tests.Security;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-04, Pattern 9, Pitfall 1 + Pitfall 7) behavioural
/// tests for <see cref="SessionLockService"/>. The real
/// <see cref="SystemEvents.SessionSwitch"/> path requires a Windows lock screen
/// (covered by the UAT file); these tests exercise the reason-matching +
/// Dispatcher-marshal seam via the <c>internal HandleSessionSwitch</c>.
///
/// <para>All tests in this collection run on STA threads because the service
/// marshals via <see cref="System.Windows.Threading.Dispatcher"/> which the
/// WPF <see cref="System.Windows.Application"/> owns on the UI thread.</para>
/// </summary>
[Collection("RDP-STA")]
public sealed class SessionLockServiceTests
{
    private readonly StaCollectionFixture _fixture;
    public SessionLockServiceTests(StaCollectionFixture fixture) => _fixture = fixture;

    // --------------------------------------------------------------------
    // Test 1 — SessionLock publishes AppLockedEvent(SessionSwitch) via dispatcher
    // --------------------------------------------------------------------
    [Fact]
    public void SessionLock_PublishesAppLockedEventWithSessionSwitchReason()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            using var svc = new SessionLockService(bus);

            svc.HandleSessionSwitch(SessionSwitchReason.SessionLock);
            PumpDispatcher();

            bus.Received(1).Publish(Arg.Is<AppLockedEvent>(e => e.Reason == LockReason.SessionSwitch));
        });
    }

    // --------------------------------------------------------------------
    // Test 2 — ConsoleDisconnect also triggers lock
    // --------------------------------------------------------------------
    [Fact]
    public void ConsoleDisconnect_TriggersLock()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            using var svc = new SessionLockService(bus);

            svc.HandleSessionSwitch(SessionSwitchReason.ConsoleDisconnect);
            PumpDispatcher();

            bus.Received(1).Publish(Arg.Is<AppLockedEvent>(e => e.Reason == LockReason.SessionSwitch));
        });
    }

    // --------------------------------------------------------------------
    // Test 3 — RemoteDisconnect also triggers lock
    // --------------------------------------------------------------------
    [Fact]
    public void RemoteDisconnect_TriggersLock()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            using var svc = new SessionLockService(bus);

            svc.HandleSessionSwitch(SessionSwitchReason.RemoteDisconnect);
            PumpDispatcher();

            bus.Received(1).Publish(Arg.Is<AppLockedEvent>(e => e.Reason == LockReason.SessionSwitch));
        });
    }

    // --------------------------------------------------------------------
    // Test 4 — Non-lock reasons do NOT publish
    // --------------------------------------------------------------------
    [Theory]
    [InlineData(SessionSwitchReason.SessionUnlock)]
    [InlineData(SessionSwitchReason.ConsoleConnect)]
    [InlineData(SessionSwitchReason.RemoteConnect)]
    [InlineData(SessionSwitchReason.SessionLogon)]
    [InlineData(SessionSwitchReason.SessionLogoff)]
    [InlineData(SessionSwitchReason.SessionRemoteControl)]
    public void NonLockReason_DoesNotPublish(SessionSwitchReason reason)
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            using var svc = new SessionLockService(bus);

            svc.HandleSessionSwitch(reason);
            PumpDispatcher();

            bus.DidNotReceiveWithAnyArgs().Publish(Arg.Any<AppLockedEvent>());
        });
    }

    // --------------------------------------------------------------------
    // Test 5 — Post-Dispose handler does NOT publish even when the underlying
    //          static event would have routed through. We simulate by calling
    //          HandleSessionSwitch AFTER Dispose; the guard flag suppresses.
    // --------------------------------------------------------------------
    [Fact]
    public void Dispose_HandlerIsIdempotent_AndGuardsFutureCalls()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            var svc = new SessionLockService(bus);

            svc.Dispose();
            svc.HandleSessionSwitch(SessionSwitchReason.SessionLock);
            PumpDispatcher();

            bus.DidNotReceiveWithAnyArgs().Publish(Arg.Any<AppLockedEvent>());
        });
    }

    // --------------------------------------------------------------------
    // Test 6 — Source grep for the mandatory patterns. The SystemEvents
    //          attach + detach + BeginInvoke marshal must all be present;
    //          a regression that drops the detach would leak handlers per
    //          learn.microsoft.com guidance (Pitfall 1).
    // --------------------------------------------------------------------
    [Fact]
    public void SessionLockService_Source_ContainsPattern9_Invariants()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var svcCs = System.IO.File.ReadAllText(System.IO.Path.Combine(solutionRoot,
            "src", "Deskbridge", "Services", "SessionLockService.cs"));

        svcCs.Should().Contain("SystemEvents.SessionSwitch += _handler",
            "Pattern 9: subscribe via strong-ref field so the delegate isn't GC'd");
        svcCs.Should().Contain("SystemEvents.SessionSwitch -= _handler",
            "Pattern 9: Dispose MUST unsubscribe — static-event invocation list leak otherwise");
        svcCs.Should().Contain("BeginInvoke",
            "Pitfall 7: SessionSwitch fires on a non-UI thread; must marshal to UI dispatcher");
    }

    // ---------- helpers ----------

    private static void PumpDispatcher()
    {
        // Pump until the dispatcher queue drains so BeginInvoke continuations run.
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
