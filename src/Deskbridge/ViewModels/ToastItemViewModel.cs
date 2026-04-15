using Wpf.Ui.Controls;

namespace Deskbridge.ViewModels;

/// <summary>
/// Phase 6 Plan 06-02 (NOTF-01 / D-06): single toast entry in the
/// <see cref="ToastStackViewModel"/>. Immutable per-toast state plus a bindable
/// <see cref="IsPaused"/> flag and a <see cref="DismissCommand"/> that raises
/// <see cref="DismissRequested"/> — the stack VM subscribes to that event and
/// removes the item (keeping the per-item DispatcherTimer cleanup in one place).
/// </summary>
public partial class ToastItemViewModel : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Monotonic counter assigned by <see cref="ToastStackViewModel.Push"/>. Used
    /// for ordering even when two toasts land in the same <see cref="DateTime.UtcNow"/>
    /// tick (millisecond resolution on some systems is coarser than Push cadence).
    /// </summary>
    public long Sequence { get; init; }

    public ControlAppearance Appearance { get; init; }
    public SymbolRegular Icon { get; init; }
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";

    /// <summary>
    /// <c>null</c> = sticky (never auto-dismisses — only explicit user dismiss or
    /// 4th-push eviction). Non-null = auto-dismiss after this TimeSpan elapses
    /// (paused while any toast in the stack is hovered, D-07).
    /// </summary>
    public TimeSpan? Duration { get; init; }

    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    /// <summary>Raised by <see cref="DismissCommand"/>. <see cref="ToastStackViewModel"/> subscribes to remove the item.</summary>
    public event EventHandler? DismissRequested;

    [RelayCommand]
    private void Dismiss() => DismissRequested?.Invoke(this, EventArgs.Empty);
}
