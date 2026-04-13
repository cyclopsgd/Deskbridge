using Deskbridge.Core.Models;

namespace Deskbridge.Core.Services;

/// <summary>
/// Request emitted by <see cref="ConnectionCoordinator"/> when a reconnect episode
/// begins. The UI layer ( <c>MainWindow</c>) subscribes to
/// <see cref="ConnectionCoordinator.ReconnectOverlayRequested"/> and bridges this
/// request to its <c>ReconnectOverlayViewModel</c> — Core never references the
/// WPF ViewModel type directly (D-10).
/// </summary>
public sealed record ReconnectUiRequest(ConnectionModel Connection, ReconnectOverlayHandle Handle);

/// <summary>
/// Protocol-agnostic bridge object passed through <see cref="ReconnectUiRequest"/>.
/// The coordinator pushes UI updates via the <see cref="UpdateAttempt"/>,
/// <see cref="SwitchToManual"/>, and <see cref="Close"/> action slots (set by the
/// MainWindow bridge); the ViewModel pushes user intents back via
/// <see cref="RaiseCancel"/>, <see cref="RaiseManualReconnect"/>, and
/// <see cref="RaiseManualClose"/>.
///
/// <para>Lives in Core so the <see cref="ConnectionCoordinator"/> can construct it
/// without referencing <c>Deskbridge</c> (the WPF exe project). Contains zero WPF
/// types.</para>
/// </summary>
public sealed class ReconnectOverlayHandle
{
    /// <summary>Set by MainWindow. Called per reconnect attempt with (attempt, delay).</summary>
    public Action<int, TimeSpan>? UpdateAttempt { get; set; }

    /// <summary>Set by MainWindow. Flips the overlay into "Connection lost / Reconnect / Close" mode.</summary>
    public Action? SwitchToManual { get; set; }

    /// <summary>Set by MainWindow. Removes the overlay from the visual tree and restores the WFH.</summary>
    public Action? Close { get; set; }

    /// <summary>Raised by the ViewModel when the user clicks Cancel during auto-retry.</summary>
    public event EventHandler? CancelRequested;

    /// <summary>Raised by the ViewModel when the user clicks Reconnect on the manual overlay.</summary>
    public event EventHandler? ManualReconnectRequested;

    /// <summary>Raised by the ViewModel when the user clicks Close on the manual overlay.</summary>
    public event EventHandler? ManualCloseRequested;

    public void RaiseCancel() => CancelRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseManualReconnect() => ManualReconnectRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseManualClose() => ManualCloseRequested?.Invoke(this, EventArgs.Empty);
}
