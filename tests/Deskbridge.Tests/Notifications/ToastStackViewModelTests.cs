using System.Windows.Threading;
using Deskbridge.Tests.Fixtures;
using Deskbridge.ViewModels;
using Wpf.Ui.Controls;

namespace Deskbridge.Tests.Notifications;

/// <summary>
/// Phase 6 Plan 06-02 (NOTF-01 / D-07) coverage for <see cref="ToastStackViewModel"/>.
/// Tests run on the STA collection because <see cref="DispatcherTimer"/> requires a
/// running dispatcher, and the timer-based tests pump frames via
/// <see cref="AdvanceDispatcher"/>.
/// </summary>
[Collection("RDP-STA")]
public sealed class ToastStackViewModelTests
{
    private readonly StaCollectionFixture _fixture;

    public ToastStackViewModelTests(StaCollectionFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Pump the current thread's Dispatcher for <paramref name="delay"/> so
    /// <see cref="DispatcherTimer"/> ticks can fire within a test.
    /// </summary>
    private static void AdvanceDispatcher(TimeSpan delay)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = delay,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    // ------------------------------------------------------------------
    // Test 1 — Push adds newest at index 0
    // ------------------------------------------------------------------
    [Fact]
    public void Push_PlacesNewestAtIndexZero()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var stack = new ToastStackViewModel();

            var a = stack.Push("A", "a-msg", ControlAppearance.Info, SymbolRegular.Info24, null);
            var b = stack.Push("B", "b-msg", ControlAppearance.Info, SymbolRegular.Info24, null);
            var c = stack.Push("C", "c-msg", ControlAppearance.Info, SymbolRegular.Info24, null);

            stack.Items.Count.Should().Be(3);
            stack.Items[0].Should().BeSameAs(c);
            stack.Items[1].Should().BeSameAs(b);
            stack.Items[2].Should().BeSameAs(a);
        });
    }

    // ------------------------------------------------------------------
    // Test 2 — Cap at 3; fourth push evicts oldest (FIFO by Sequence)
    // ------------------------------------------------------------------
    [Fact]
    public void Push_FourthPush_EvictsOldest()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var stack = new ToastStackViewModel();

            var a = stack.Push("A", "", ControlAppearance.Info, SymbolRegular.Info24, null);
            var b = stack.Push("B", "", ControlAppearance.Info, SymbolRegular.Info24, null);
            var c = stack.Push("C", "", ControlAppearance.Info, SymbolRegular.Info24, null);
            var d = stack.Push("D", "", ControlAppearance.Info, SymbolRegular.Info24, null);

            stack.Items.Count.Should().Be(3);
            stack.Items[0].Should().BeSameAs(d);
            stack.Items[1].Should().BeSameAs(c);
            stack.Items[2].Should().BeSameAs(b);
            stack.Items.Should().NotContain(a);
        });
    }

    // ------------------------------------------------------------------
    // Test 3 — Auto-dismiss removes the toast after Duration elapses
    // ------------------------------------------------------------------
    [Fact]
    public void Push_WithDuration_AutoDismisses()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var stack = new ToastStackViewModel();
            var item = stack.Push(
                "Connected", "to srv", ControlAppearance.Info, SymbolRegular.Info24,
                TimeSpan.FromMilliseconds(100));

            stack.Items.Should().Contain(item);

            AdvanceDispatcher(TimeSpan.FromMilliseconds(200));

            stack.Items.Should().NotContain(item);
        });
    }

    // ------------------------------------------------------------------
    // Test 4 — Sticky toast (Duration == null) stays past normal auto-dismiss window
    // ------------------------------------------------------------------
    [Fact]
    public void Push_Sticky_StaysIndefinitely()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var stack = new ToastStackViewModel();
            var item = stack.Push("Reconnecting", "attempt 1/20",
                ControlAppearance.Caution, SymbolRegular.ArrowClockwise24, duration: null);

            AdvanceDispatcher(TimeSpan.FromMilliseconds(250));

            stack.Items.Should().Contain(item);
        });
    }

    // ------------------------------------------------------------------
    // Test 5 — Explicit DismissCommand removes a sticky toast
    // ------------------------------------------------------------------
    [Fact]
    public void DismissCommand_RemovesStickyToast()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var stack = new ToastStackViewModel();
            var item = stack.Push("Connection failed", "bad host",
                ControlAppearance.Danger, SymbolRegular.ErrorCircle24, duration: null);

            item.DismissCommand.Execute(null);

            stack.Items.Should().NotContain(item);
        });
    }

    // ------------------------------------------------------------------
    // Test 6 — Pause/Resume freezes and unfreezes auto-dismiss timers
    // ------------------------------------------------------------------
    [Fact]
    public void PauseResume_StopsAndRestartsAutoDismiss()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var stack = new ToastStackViewModel();
            var a = stack.Push("A", "", ControlAppearance.Info, SymbolRegular.Info24,
                TimeSpan.FromMilliseconds(100));
            var b = stack.Push("B", "", ControlAppearance.Info, SymbolRegular.Info24,
                TimeSpan.FromMilliseconds(100));

            stack.Pause();
            AdvanceDispatcher(TimeSpan.FromMilliseconds(250));

            // Both still present — timers paused.
            stack.Items.Should().Contain(a);
            stack.Items.Should().Contain(b);
            a.IsPaused.Should().BeTrue();
            b.IsPaused.Should().BeTrue();

            stack.Resume();
            AdvanceDispatcher(TimeSpan.FromMilliseconds(250));

            stack.Items.Should().NotContain(a);
            stack.Items.Should().NotContain(b);
        });
    }

    // ------------------------------------------------------------------
    // Test 7 — 4th push evicts oldest even when all 3 are sticky
    // ------------------------------------------------------------------
    [Fact]
    public void Push_FourthPush_EvictsOldest_EvenWhenAllSticky()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var stack = new ToastStackViewModel();
            var a = stack.Push("A", "", ControlAppearance.Info, SymbolRegular.Info24, null);
            var b = stack.Push("B", "", ControlAppearance.Info, SymbolRegular.Info24, null);
            var c = stack.Push("C", "", ControlAppearance.Info, SymbolRegular.Info24, null);
            var d = stack.Push("D", "", ControlAppearance.Info, SymbolRegular.Info24, null);

            stack.Items.Count.Should().Be(3);
            stack.Items.Should().NotContain(a);
            stack.Items[0].Should().BeSameAs(d);
        });
    }

    // ------------------------------------------------------------------
    // Test 8 — Sequence is monotonic even for rapid-succession pushes
    // ------------------------------------------------------------------
    [Fact]
    public void Push_AssignsMonotonicSequence()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var stack = new ToastStackViewModel();
            var items = new List<ToastItemViewModel>();
            for (var i = 0; i < 10; i++)
            {
                items.Add(stack.Push($"T{i}", "", ControlAppearance.Info, SymbolRegular.Info24, null));
            }

            for (var i = 1; i < items.Count; i++)
            {
                items[i].Sequence.Should().BeGreaterThan(items[i - 1].Sequence,
                    "Sequence must be strictly monotonic even under rapid pushes");
            }
        });
    }
}
