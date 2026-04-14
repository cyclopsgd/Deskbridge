using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Events;

public record NotificationEvent(string Title, string Message, NotificationLevel Level);

// TabOpenedEvent / TabClosedEvent / TabSwitchedEvent moved to TabEvents.cs in Phase 5
// (alongside the new TabStateChangedEvent) so every tab-lifecycle event record lives in
// one file and shares docs. The canonical shapes are unchanged.

public record UpdateAvailableEvent(string Version);
public record AppLockedEvent(LockReason Reason);
public record AppUnlockedEvent();
