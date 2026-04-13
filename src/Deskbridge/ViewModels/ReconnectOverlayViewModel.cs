namespace Deskbridge.ViewModels;

/// <summary>
/// Reconnect overlay rendering mode. <see cref="Auto"/> shows the progress ring
/// + "Reconnecting... attempt N" text + Cancel button while the
/// <see cref="Deskbridge.Core.Services.RdpReconnectCoordinator"/> loop runs;
/// <see cref="Manual"/> shows "Connection lost" + Reconnect/Close buttons after
/// the D-05 cap or after a D-06 auth/licensing skip.
/// </summary>
public enum ReconnectMode
{
    Auto,
    Manual,
}

/// <summary>
/// ViewModel for <c>ReconnectOverlay.xaml</c>. Bridges
/// <see cref="Deskbridge.Core.Services.RdpReconnectCoordinator"/> notifications to
/// the overlay UI (D-07) and emits <see cref="Cancelled"/>, <see cref="ReconnectRequested"/>,
/// and <see cref="CloseRequested"/> for the MainWindow bridge to forward to the
/// <c>ReconnectOverlayHandle</c> held by <c>ConnectionCoordinator</c>.
///
/// <para><b>Phase 6 note:</b> typography, spacing, and error-state accent are
/// intentionally minimal here per the plan's &lt;ui_deferral&gt;. The ui-phase 4 pass
/// in Phase 6 refines without touching this shape.</para>
/// </summary>
public partial class ReconnectOverlayViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AttemptText))]
    public partial ReconnectMode Mode { get; set; } = ReconnectMode.Auto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AttemptText))]
    public partial int Attempt { get; set; } = 0;

    [ObservableProperty]
    public partial TimeSpan Delay { get; set; } = TimeSpan.Zero;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AttemptText))]
    public partial string Message { get; set; } = "Reconnecting...";

    [ObservableProperty]
    public partial string ConnectionName { get; set; } = "";

    /// <summary>
    /// Combined display text. In <see cref="ReconnectMode.Auto"/> it includes the
    /// attempt counter; in <see cref="ReconnectMode.Manual"/> it collapses to the
    /// <see cref="Message"/> field ("Connection lost" after cap / auth skip).
    /// </summary>
    public string AttemptText => Mode == ReconnectMode.Auto
        ? $"Reconnecting... attempt {Attempt}"
        : Message;

    // --- Wiring hooks the MainWindow bridge subscribes to ---
    // Using EventHandler? (not EventHandler<T>) matches the plan shape exactly.

    public event EventHandler? Cancelled;
    public event EventHandler? ReconnectRequested;
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Reconnect() => ReconnectRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// D-05 cap + D-06 auth-skip entry point. Flips the overlay into the
    /// "Connection lost — Reconnect / Close" manual state.
    /// </summary>
    public void SwitchToManual()
    {
        Message = "Connection lost";
        Mode = ReconnectMode.Manual;
    }

    /// <summary>
    /// Called per reconnect attempt with the 1-based attempt number and the delay
    /// that is about to elapse. Drives the spinner + counter display.
    /// </summary>
    public void Update(int attempt, TimeSpan delay)
    {
        Attempt = attempt;
        Delay = delay;
    }
}
