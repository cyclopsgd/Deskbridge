using Deskbridge.Core.Services;

namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Singleton event-bus bridge: subscribes to <see cref="Events.ConnectionRequestedEvent"/>,
/// marshals to STA per D-11, and runs <see cref="IConnectionPipeline"/>. MainWindow
/// subscribes to <see cref="HostMounted"/>/<see cref="HostUnmounted"/> to add/remove
/// the host's WindowsFormsHost from the viewport (D-12 single live host in Phase 4).
/// </summary>
public interface IConnectionCoordinator
{
    /// <summary>The currently active host, or <c>null</c> if no connection is live.</summary>
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
}
