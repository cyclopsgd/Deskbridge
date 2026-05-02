using System.Windows.Threading;
using Deskbridge.Core.Interfaces;

namespace Deskbridge.Services;

/// <summary>
/// Phase 21 (PERF-02): DispatcherTimer-backed debouncer for SearchText filtering.
/// 250ms interval mirrors the resize-debounce pattern in MainWindow.xaml.cs:411-418.
/// </summary>
internal sealed class DispatcherTimerDebouncer : IDebouncer
{
    private const int DebounceMs = 250;

    private DispatcherTimer? _timer;
    private Action? _pending;

    public void Schedule(Action action)
    {
        _pending = action;
        _timer?.Stop();
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Cancel()
    {
        _timer?.Stop();
        _pending = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        var a = _pending;
        _pending = null;
        a?.Invoke();
    }
}
