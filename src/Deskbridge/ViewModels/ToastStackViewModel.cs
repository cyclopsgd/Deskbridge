using System.Collections.ObjectModel;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace Deskbridge.ViewModels;

/// <summary>
/// Phase 6 Plan 06-02 (NOTF-01 / D-07 / Q1 Option B): custom ItemsControl stack
/// replacing WPF-UI's single-snackbar FIFO SnackbarPresenter.
///
/// <para>Invariants enforced here:</para>
/// <list type="bullet">
/// <item>Max 3 visible items — a 4th push evicts the oldest (lowest <see cref="ToastItemViewModel.Sequence"/>).</item>
/// <item><see cref="Items"/> are maintained newest-at-index-0 so the natural reading order is top-to-bottom.</item>
/// <item>Each item with a non-null <see cref="ToastItemViewModel.Duration"/> owns a <see cref="DispatcherTimer"/> —
/// <see cref="Pause"/> stops all timers; <see cref="Resume"/> restarts them (hover-pause, D-07).</item>
/// <item>Explicit dismiss via <see cref="ToastItemViewModel.DismissCommand"/> removes immediately regardless of sticky state.</item>
/// </list>
/// </summary>
public sealed class ToastStackViewModel
{
    private const int MaxVisible = 3;

    private long _sequence;
    private readonly Dictionary<Guid, DispatcherTimer> _timers = new();
    private bool _paused;

    public ObservableCollection<ToastItemViewModel> Items { get; } = new();

    /// <summary>
    /// Push a new toast. Evicts the oldest if the stack is already at
    /// <c>MaxVisible</c> (even sticky items — D-07 explicit: the 4th push always
    /// evicts). Starts the auto-dismiss timer when <paramref name="duration"/> is
    /// non-null (subject to the current <see cref="Pause"/>/<see cref="Resume"/> state).
    /// </summary>
    public ToastItemViewModel Push(
        string title,
        string message,
        ControlAppearance appearance,
        SymbolRegular icon,
        TimeSpan? duration)
    {
        var item = new ToastItemViewModel
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Appearance = appearance,
            Icon = icon,
            Title = title,
            Message = message,
            Duration = duration,
        };
        item.DismissRequested += (_, _) => Remove(item);

        // D-07: eviction happens BEFORE insert so the new item always fits.
        // Oldest = highest index (we keep newest at index 0).
        while (Items.Count >= MaxVisible)
        {
            Remove(Items[Items.Count - 1]);
        }

        Items.Insert(0, item);  // Newest-on-top invariant.

        if (duration.HasValue)
        {
            StartTimer(item, duration.Value);
        }

        return item;
    }

    private void StartTimer(ToastItemViewModel item, TimeSpan duration)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher
                          ?? Dispatcher.CurrentDispatcher;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _timers.Remove(item.Id);
            Remove(item);
        };
        _timers[item.Id] = timer;
        if (!_paused)
        {
            timer.Start();
        }
        else
        {
            item.IsPaused = true;
        }
    }

    private void Remove(ToastItemViewModel item)
    {
        if (_timers.TryGetValue(item.Id, out var timer))
        {
            timer.Stop();
            _timers.Remove(item.Id);
        }
        Items.Remove(item);
    }

    /// <summary>
    /// Pause all auto-dismiss timers. Invoked by the code-behind on <c>MouseEnter</c>
    /// over the <see cref="Deskbridge.Controls.ToastStackControl"/>. Idempotent.
    /// </summary>
    public void Pause()
    {
        _paused = true;
        foreach (var t in _timers.Values) t.Stop();
        foreach (var i in Items) i.IsPaused = true;
    }

    /// <summary>
    /// Resume all auto-dismiss timers. Invoked on <c>MouseLeave</c>. Idempotent.
    /// </summary>
    public void Resume()
    {
        _paused = false;
        foreach (var t in _timers.Values) t.Start();
        foreach (var i in Items) i.IsPaused = false;
    }
}
