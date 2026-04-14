using Deskbridge.Core.Services;

namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Singleton event-bus bridge: subscribes to <see cref="Events.ConnectionRequestedEvent"/>,
/// marshals to STA per D-11, and runs <see cref="IConnectionPipeline"/>. MainWindow
/// subscribes to <see cref="HostMounted"/>/<see cref="HostUnmounted"/> to add/remove
/// the host's WindowsFormsHost from the viewport.
///
/// <para>Phase 5 (multi-host): <see cref="ITabHostManager"/> is the source of truth for
/// "which connections are open"; <see cref="ActiveHost"/> is retained as a convenience
/// shim returning the currently-focused host (drawn from <see cref="ITabHostManager.GetHost"/>
/// of the manager's <c>ActiveId</c>).</para>
/// </summary>
public interface IConnectionCoordinator
{
    /// <summary>
    /// The currently active host, or <c>null</c> if no connection is live.
    /// Phase 5: backed by the tab host manager; prefer querying <see cref="ITabHostManager"/>
    /// directly where possible.
    /// </summary>
    IProtocolHost? ActiveHost { get; }

    /// <summary>Raised when a host is successfully established; MainWindow mounts the WFH.</summary>
    event EventHandler<IProtocolHost>? HostMounted;

    /// <summary>Raised when the active host is disconnected; MainWindow unmounts the WFH.</summary>
    event EventHandler<IProtocolHost>? HostUnmounted;

    /// <summary>
    /// Raised when a post-connect disconnect triggers a reconnect episode (Plan 04-03).
    /// MainWindow creates a <c>ReconnectOverlayViewModel</c>, bridges it to the
    /// supplied <see cref="ReconnectOverlayHandle"/>, and mounts the overlay via
    /// <see cref="Deskbridge.Protocols.Rdp.AirspaceSwapper.HideWithoutSnapshot"/>.
    /// </summary>
    event EventHandler<ReconnectUiRequest>? ReconnectOverlayRequested;

    /// <summary>
    /// Cancel any in-flight auto-reconnect backoff loop for the given connection.
    /// Must be called by <see cref="ITabHostManager"/>'s close paths BEFORE
    /// <see cref="IDisconnectPipeline.DisconnectAsync"/> to prevent <c>RdpReconnectCoordinator</c>
    /// from firing <c>ConnectAsync</c> against a host that is about to be disposed.
    /// No-op if no reconnect loop is currently running. Q2 resolution —
    /// see <c>05-RESEARCH.md §Open Questions</c>.
    /// </summary>
    void CancelReconnect(Guid connectionId);
}
