# Phase 5: Tab Management - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-13
**Phase:** 05-tab-management
**Areas discussed:** Tab ↔ host coordination, Tab close semantics, 15+ GDI warning UX, Tab state & background reconnect (incl. label, state indicators, background drops, status bar, extra shortcuts)

---

## Gray-Area Selection

| Option | Description | Selected |
|--------|-------------|----------|
| Tab ↔ host coordination | Multi-host refactor of ConnectionCoordinator, dup-click policy, where state lives | ✓ |
| Tab close semantics | Disconnect on close, confirmation, context menu, shutdown teardown | ✓ |
| 15+ GDI warning UX | Modal vs non-blocking, trigger rules, hard cap | ✓ |
| Tab state & bg reconnect | Label format, state indicators, bg-drop UX, status bar, extras | ✓ |

**User's choice:** All four selected (multi-select).

---

## Tab ↔ Host Coordination

### Q1: Where should the multi-host tab state live?

| Option | Description | Selected |
|--------|-------------|----------|
| New TabHostManager | New singleton service in Deskbridge.Core owning Dictionary<Guid, IProtocolHost>; clean separation of concerns | ✓ |
| Extend ConnectionCoordinator | Refactor _active from tuple to Dictionary; fewer new classes but concerns mix | |
| Live in MainWindowViewModel | UI layer owns dict; simplest wiring but breaks Phase 4 D-10 boundary | |

**User's choice:** New TabHostManager (Recommended).

### Q2: Double-clicking a connection already open — what happens?

| Option | Description | Selected |
|--------|-------------|----------|
| Switch to existing tab | TAB-01 literal, publishes TabSwitched only, deletes rapid-click guard | ✓ |
| Open a second tab | Allows multiple sessions to same host; breaks TAB-01 literal | |
| Offer choice via InfoBar | UX branch for both cases; adds friction | |

**User's choice:** Switch to existing tab (Recommended).

### Q3: How do tabs reference their host?

| Option | Description | Selected |
|--------|-------------|----------|
| Manager holds dict, tab holds Guid | Matches MVVM hygiene; ViewModel tests don't need IProtocolHost mocks | ✓ |
| TabItemViewModel holds IProtocolHost directly | Simpler lookup; leaks Core abstraction into ViewModel | |

**User's choice:** Manager holds the dict, tab holds only Guid (Recommended).

### Q4: Confirm D-12 "never re-parent" / Visibility toggle for all WFHs?

| Option | Description | Selected |
|--------|-------------|----------|
| Yes — persistent container | All WFHs added once, Visibility toggle on tab switch, confirms Phase 4 D-12 | ✓ |
| Re-parent only active host | Keep Phase 4 swap pattern; rejected by WINFORMS-HOST-AIRSPACE.md | |

**User's choice:** Yes — persistent container, Visibility toggle (Recommended).

---

## Tab Close Semantics

### Q1: Closing a tab with a live RDP session — what happens?

| Option | Description | Selected |
|--------|-------------|----------|
| Instant close, no confirm | Matches enterprise power-user expectation | ✓ |
| Confirm if more than 1 tab live | Safety for close-all / window-close only | |
| Always confirm on live close | Safest but punishes the common case | |

**User's choice:** Instant close, no confirm (Recommended).

### Q2: Right-click tab context menu — which actions?

| Option | Description | Selected |
|--------|-------------|----------|
| Close | Explicit menu item alongside X / middle-click / Ctrl+W | ✓ |
| Close Others | Keep this tab, close the rest | ✓ |
| Close All | Close every tab | ✓ |
| Duplicate | Open second session to same host; conflicts with D-02 | |

**User's choice:** Close + Close Others + Close All (all Recommended); Duplicate rejected.

### Q3: App window close — how aggressive is the multi-host teardown?

| Option | Description | Selected |
|--------|-------------|----------|
| Sequential via DisconnectPipeline | Matches Phase 4 pattern, preserves disposal ordering | ✓ |
| Parallel disconnects | Faster but nullified by STA marshal; risky | |
| Skip disconnect, just Dispose | Fastest; leaks server-side session state, skips audit | |

**User's choice:** Sequential, each via DisconnectPipeline (Recommended).

---

## 15+ GDI Warning UX

### Q1: How should the 15+ warning appear?

| Option | Description | Selected |
|--------|-------------|----------|
| Snackbar / InfoBar, non-blocking | Toast via existing ISnackbarService; connection still opens | ✓ |
| ContentDialog modal | Blocking; re-prompt repetition problem | |
| Status bar badge only | Most subtle, easily missed | |

**User's choice:** Snackbar / InfoBar, non-blocking (Recommended).

### Q2: When does the warning fire?

| Option | Description | Selected |
|--------|-------------|----------|
| Once on crossing 14 → 15 | No repeat until count drops below and rises again | ✓ |
| Every open once count ≥ 15 | Noisy | |
| Escalate at 15, 20, 25... | No requirement signal; complexity without benefit | |

**User's choice:** Once on crossing the 15 threshold (Recommended).

### Q3: Hard ceiling on connection count?

| Option | Description | Selected |
|--------|-------------|----------|
| No hard cap, soft warning only | Machine-specific GDI ceilings vary too much | ✓ |
| Hard cap at 25 | Arbitrary; would need to be configurable | |

**User's choice:** No hard cap, soft warning only (Recommended).

---

## Tab State & Background Reconnect

### Q1: What does the tab label show?

| Option | Description | Selected |
|--------|-------------|----------|
| Connection display name only | ConnectionModel.Name; hostname in status bar + tooltip | ✓ |
| Hostname only | FQDN; user-chosen names hidden | |
| Name (hostname) | Combined; worst horizontal space usage | |

**User's choice:** Connection display name only (Recommended).

### Q2: Per-tab state indicators — which states show markers?

| Option | Description | Selected |
|--------|-------------|----------|
| Connecting spinner | ProgressRing next to title during ConnectStage | ✓ |
| Reconnecting badge | Amber dot during RdpReconnectCoordinator loop | ✓ |
| Error dot | Red dot for manual-reconnect state | ✓ |
| Drag-to-reorder | Enterprise ask; adds drag-drop plumbing | ✓ |

**User's choice:** All four (Recommended three + Drag-to-reorder). Drag-reorder is now in scope.

### Q3: When an inactive tab's session drops — what should the UI do?

| Option | Description | Selected |
|--------|-------------|----------|
| Indicator on tab badge, overlay when activated | No focus yank; auto-retry runs in background | ✓ |
| Auto-switch focus | Impossible to miss but intrusive | |
| Snackbar + badge | Overlaps Phase 6 NOTF-02 scope | |

**User's choice:** Indicator on tab badge only, overlay when activated (Recommended).

### Q4: Status bar binding to active tab — in Phase 5 or deferred?

| Option | Description | Selected |
|--------|-------------|----------|
| In Phase 5, wire to active tab | Natural seam; all metrics attempted | ✓ |
| Phase 5 hostname only, metrics Phase 6 | Cleaner phase boundary | |
| Defer entirely to Phase 6 | Hardcoded until Phase 6 observability | |

**User's choice:** In Phase 5, wire to active tab (Recommended).

*(Follow-up clarified: hostname + state + resolution in Phase 5; latency/quality stubbed for Phase 6.)*

---

## Follow-up: Extra Shortcuts & Status Bar Metrics

### Q1: Beyond TAB-03, which extra shortcuts go in Phase 5?

| Option | Description | Selected |
|--------|-------------|----------|
| Ctrl+1..Ctrl+9 (jump to tab N) | Chrome/VS Code convention; Ctrl+9 = last | ✓ |
| Ctrl+F4 (close tab alias) | VS/Word tradition; trivial | ✓ |
| Ctrl+Shift+T (reopen last closed) | Browser convention; bounded LRU | ✓ |
| Stop at TAB-03, defer rest | Phase 6 bundles with command palette | |

**User's choice:** All three shortcuts selected. Reopen-last-closed is in Phase 5 scope.

### Q2: Status bar metrics depth in Phase 5?

| Option | Description | Selected |
|--------|-------------|----------|
| Hostname + state + resolution, stub latency/quality | From IMsRdpClient.DesktopWidth/Height; probes Phase 6 | ✓ |
| All four metrics | Full REFERENCE spec; needs DispatcherTimer probe | |
| Hostname + state only | Minimum viable; resolution deferred | |

**User's choice:** Hostname + state + resolution, stub latency/quality (Recommended).

---

## Claude's Discretion

- Exact class/interface naming for TabHostManager
- Internal structure of the persistent HostContainer (Grid vs custom HostsPanel)
- Drag-reorder implementation detail (reuse Phase 3 behavior or custom)
- LRU size for Ctrl+Shift+T (recommended 10)
- Reconnecting badge visual (dot vs ring vs ProgressRing)
- Tab tooltip content
- TabSwitchedEvent signature detail — follow REFERENCE.md `(Guid? PreviousId, Guid ActiveId)`

## Deferred Ideas (noted during discussion)

- **Phase 6:** Snackbar on bg drops, latency/quality probes, command palette tab search, configurable hard cap, audit stage for tab events, global shortcut service
- **v1.1:** multi-window tear-off, order persistence, duplicate-session-to-same-host, LRU persistence across restarts
- **Rejected explicitly:** confirm dialog on close, parallel shutdown disconnects, auto-switch to dropped tab, host dict in MainWindowViewModel
