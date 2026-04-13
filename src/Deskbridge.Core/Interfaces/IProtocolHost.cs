using Deskbridge.Core.Pipeline;

namespace Deskbridge.Core.Interfaces;

public interface IProtocolHost : IDisposable
{
    Guid ConnectionId { get; }
    bool IsConnected { get; }
    Task ConnectAsync(ConnectionContext context);
    Task DisconnectAsync();
    event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Raised when a session that was fully established (login complete) subsequently
    /// disconnects — i.e. the "drop" notification Plan 04-03's reconnect coordinator
    /// subscribes to. The payload is the protocol-specific disconnect-reason code
    /// (for RDP, <c>IMsTscAxEvents_OnDisconnectedEvent.discReason</c>) which the
    /// coordinator feeds to <see cref="Services.DisconnectReasonClassifier.Classify"/>.
    ///
    /// <para>Distinct from <see cref="ErrorOccurred"/>: that surfaces sanitized error
    /// strings for logging; this drives the reconnect state machine. Abstraction
    /// chosen so <c>ConnectionCoordinator</c> in Core can subscribe without taking
    /// a reverse dependency on <c>Deskbridge.Protocols.Rdp</c> (Plan 04-03 Task 2.1
    /// deviation under Rule 3 — the plan's sample code assumed the reverse ref).</para>
    /// </summary>
    event EventHandler<int>? DisconnectedAfterConnect;
}
