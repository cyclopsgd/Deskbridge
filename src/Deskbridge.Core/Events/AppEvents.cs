using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Events;

public record NotificationEvent(string Title, string Message, NotificationLevel Level);
public record TabOpenedEvent(Guid ConnectionId);
public record TabClosedEvent(Guid ConnectionId);
public record TabSwitchedEvent(Guid? PreviousId, Guid ActiveId);
public record UpdateAvailableEvent(string Version);
public record AppLockedEvent(LockReason Reason);
public record AppUnlockedEvent();
