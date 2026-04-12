using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Events;

public record ConnectionRequestedEvent(ConnectionModel Connection);

/// <summary>
/// Published by <c>CreateHostStage</c> immediately after the protocol host is
/// constructed but BEFORE <c>ConnectStage</c> runs. Lets the coordinator mount the
/// host's view into the visual tree (WPF visual parent + layout pass) so the
/// AxHost HWND is realized in time for the synchronous <c>Connect()</c> call —
/// fixes the "called before host was added to the visual tree" siting-order bug
/// (see RDP-ACTIVEX-PITFALLS §1).
/// </summary>
public record HostCreatedEvent(ConnectionModel Connection, IProtocolHost Host);

public record ConnectionEstablishedEvent(ConnectionModel Connection, IProtocolHost Host);
public record ConnectionFailedEvent(ConnectionModel Connection, string Reason, Exception? Exception);
public record ConnectionClosedEvent(ConnectionModel Connection, DisconnectReason Reason);
public record ReconnectingEvent(ConnectionModel Connection, int Attempt, TimeSpan Delay);
public record CredentialRequestedEvent(ConnectionModel Connection);
public record ConnectionImportedEvent(int Count, string Source);
public record SessionHealthUpdateEvent(Guid ConnectionId, int LatencyMs, ConnectionQuality Quality);
