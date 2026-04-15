using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Settings;
using Deskbridge.Services;
using Deskbridge.Tests.Fixtures;

namespace Deskbridge.Tests.Security;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-03, Pattern 8, Pitfall 6) behavioural tests for
/// <see cref="IdleLockService"/>. All tests run on STA threads because
/// <see cref="System.Windows.Threading.DispatcherTimer"/> +
/// <see cref="System.Windows.Input.InputManager"/> + <see cref="WindowsFormsHost"/>
/// all require a pumped WPF dispatcher + STA apartment.
/// </summary>
[Collection("RDP-STA")]
public sealed class IdleLockServiceTests
{
    private readonly StaCollectionFixture _fixture;
    public IdleLockServiceTests(StaCollectionFixture fixture) => _fixture = fixture;

    // --------------------------------------------------------------------
    // Test 1 — Timer ticks → publishes AppLockedEvent(Timeout)
    // --------------------------------------------------------------------
    [Fact]
    public void OnTick_PublishesAppLockedEventWithTimeoutReason()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            using var svc = new IdleLockService(bus, SecuritySettingsRecord.Default);

            // Drive the timer to a very short interval + wait for Tick.
            svc.SetIntervalForTesting(TimeSpan.FromMilliseconds(50));

            PumpDispatcherFor(TimeSpan.FromMilliseconds(250));

            bus.Received(1).Publish(Arg.Is<AppLockedEvent>(e => e.Reason == LockReason.Timeout));
        });
    }

    // --------------------------------------------------------------------
    // Test 2 — Input from non-WFH source resets the timer (Stop + Start).
    //          We observe via HandleInputFromSource return value.
    // --------------------------------------------------------------------
    [Fact]
    public void HandleInputFromSource_NonWfh_ReturnsTrue_AndRestartsTimer()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            using var svc = new IdleLockService(bus, SecuritySettingsRecord.Default);

            var button = new System.Windows.Controls.Button();  // not inside a WindowsFormsHost

            var reset = svc.HandleInputFromSource(button);

            reset.Should().BeTrue();
            svc.IsTimerRunning.Should().BeTrue("timer restarts on every non-WFH input");
        });
    }

    // --------------------------------------------------------------------
    // Test 3 — Input from WFH-child source does NOT reset the timer.
    //          D-14 + Pitfall 6: typing into the RDP session is NOT
    //          Deskbridge activity.
    // --------------------------------------------------------------------
    [Fact]
    public void HandleInputFromSource_WfhChild_ReturnsFalse_DoesNotReset()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            using var svc = new IdleLockService(bus, SecuritySettingsRecord.Default);

            // A WindowsFormsHost with a child element underneath. FindAncestor<T>
            // walks the visual tree; we force the Parent by parenting under a
            // Grid that contains the WFH. The WFH's Child is a WinForms Control;
            // we use the WFH itself as the source — it IS-A WindowsFormsHost so
            // FindAncestor<WindowsFormsHost>(source) returns it.
            var wfh = new WindowsFormsHost();

            var reset = svc.HandleInputFromSource(wfh);

            reset.Should().BeFalse(
                "Pitfall 6: RDP-session input inside a WindowsFormsHost must not reset " +
                "the Deskbridge idle timer (D-14: Deskbridge activity only)");
        });
    }

    // --------------------------------------------------------------------
    // Test 4 — Dispose stops the timer AND returns false from subsequent input.
    // --------------------------------------------------------------------
    [Fact]
    public void Dispose_StopsTimer_AndBlocksSubsequentInput()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            var svc = new IdleLockService(bus, SecuritySettingsRecord.Default);
            svc.IsTimerRunning.Should().BeTrue();

            svc.Dispose();

            svc.IsTimerRunning.Should().BeFalse();
            svc.HandleInputFromSource(new System.Windows.Controls.Button()).Should().BeFalse(
                "post-Dispose input must be a no-op");
        });
    }

    // --------------------------------------------------------------------
    // Test 5 — Null source (unknown origin) counts as Deskbridge activity and
    //          resets the timer. Defense-in-depth: if we can't prove the input
    //          came from the RDP session, treat it as user activity.
    // --------------------------------------------------------------------
    [Fact]
    public void HandleInputFromSource_NullSource_TreatedAsDeskbridgeActivity()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            using var svc = new IdleLockService(bus, SecuritySettingsRecord.Default);

            svc.HandleInputFromSource(null).Should().BeTrue();
        });
    }

    // --------------------------------------------------------------------
    // Test 6 — Source-grep for the Pitfall 6 filter. KeyEventArgs / real
    //          PreProcessInputEventArgs construction is impractical, so
    //          we assert the handler body contains the right sentinels.
    // --------------------------------------------------------------------
    [Fact]
    public void IdleLockService_Source_ContainsPitfall6Filter()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var svcCs = System.IO.File.ReadAllText(System.IO.Path.Combine(solutionRoot,
            "src", "Deskbridge", "Services", "IdleLockService.cs"));

        svcCs.Should().Contain("FindAncestor<WindowsFormsHost>",
            "Pitfall 6: IdleLockService must filter input that came from inside a WindowsFormsHost");
        svcCs.Should().Contain("InputManager.Current.PreProcessInput",
            "Pattern 8: subscribe to the WPF input-manager pre-process pipeline");
        svcCs.Should().Contain("InputManager.Current.PreProcessInput -= ",
            "Dispose must unsubscribe the handler — static/singleton event otherwise leaks");
    }

    // ---------- helpers ----------

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var stopTimer = new System.Windows.Threading.DispatcherTimer(
            duration,
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            dispatcher);
        stopTimer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        stopTimer.Stop();
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
