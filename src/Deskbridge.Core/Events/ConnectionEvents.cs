using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Events;

public record ConnectionRequestedEvent(ConnectionModel Connection);
public record ConnectionEstablishedEvent(ConnectionModel Connection, IProtocolHost Host);
public record ConnectionFailedEvent(ConnectionModel Connection, string Reason, Exception? Exception);
public record ConnectionClosedEvent(ConnectionModel Connection, DisconnectReason Reason);
public record ReconnectingEvent(ConnectionModel Connection, int Attempt, TimeSpan Delay);
public record CredentialRequestedEvent(ConnectionModel Connection);
public record ConnectionImportedEvent(int Count, string Source);
public record SessionHealthUpdateEvent(Guid ConnectionId, int LatencyMs, ConnectionQuality Quality);
