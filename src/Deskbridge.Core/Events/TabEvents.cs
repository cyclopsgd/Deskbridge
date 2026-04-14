using Deskbridge.Core.Models;

namespace Deskbridge.Core.Events;

/// <summary>
/// Published by <c>ITabHostManager</c> (Phase 5) when a tab is opened —
/// i.e. when the coordinator's <c>HostMounted</c> event fires and the new host is
/// registered in the tab dictionary. TAB-05. Carries the full <see cref="ConnectionModel"/>
/// so subscribers render the tab label / tooltip without a separate store lookup
/// (hotfix 2026-04-14: store lookups were intermittently returning null, producing
/// "(unknown)" tab titles with spinning ProgressRings).
/// </summary>
public record TabOpenedEvent(Guid ConnectionId, ConnectionModel Connection);

/// <summary>
/// Published by <c>ITabHostManager</c> (Phase 5) when a tab is closed —
/// i.e. after the disconnect pipeline completes and the host is removed from the tab
/// dictionary. TAB-05.
/// </summary>
public record TabClosedEvent(Guid ConnectionId);

/// <summary>
/// Published by <c>ITabHostManager</c> (Phase 5) when the active tab
/// changes. <paramref name="PreviousId"/> is <c>null</c> on the first tab open.
/// <paramref name="ActiveId"/> is <c>Guid.Empty</c> when the last tab closes (no
/// active tab). Subscribers (status bar / Phase 6 observability) treat
/// <c>Guid.Empty</c> as "no active tab".
/// </summary>
public record TabSwitchedEvent(Guid? PreviousId, Guid ActiveId);

/// <summary>
/// Published by <c>ITabHostManager</c> (Phase 5) when a tab's lifecycle
/// state changes (Connecting/Connected/Reconnecting/Error). Drives the D-12
/// per-tab indicators (ProgressRing / amber dot / red dot).
/// </summary>
public record TabStateChangedEvent(Guid ConnectionId, TabState State);
