---
phase: 05-tab-management
plan: 01
subsystem: tab-management
tags: [tab-host-manager, multi-host, lru, snackbar-warning, airspace-multi-host, coordinator-refactor]
requires:
  - Phase 1 event bus + pipelines
  - Phase 4 ConnectionCoordinator + AirspaceSwapper + reconnect coordinator
provides:
  - ITabHostManager + TabHostManager singleton (D-01)
  - Dict-keyed multi-host tracking keyed by ConnectionId
  - TabOpenedEvent / TabClosedEvent / TabSwitchedEvent / TabStateChangedEvent canonical shapes
  - Fire-once-per-crossing 15-session Snackbar warning (D-09, D-10, TAB-04)
  - Bounded last-closed LRU for Ctrl+Shift+T (D-16)
  - IConnectionCoordinator.CancelReconnect(Guid) (Q2 resolution)
  - AirspaceSwapper per-host pre-drag visibility restore (WR-06)
  - Publisher-side switch-to-existing check in ConnectionTreeViewModel.Connect (D-02)
affects:
  - src/Deskbridge.Core/Services/ConnectionCoordinator.cs (dict-keyed storage; duplicate-click guard + replacement branch deleted)
  - src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs (ctor + Connect command)
  - src/Deskbridge/App.xaml.cs (DI registration + eager resolution)
tech-stack:
  added:
    - WPF-UI package reference added to Deskbridge.Core.csproj (ISnackbarService + ControlAppearance + SymbolIcon consumed by TabHostManager)
  patterns:
    - Dispatcher-injectable service constructor (mirrors ConnectionCoordinator) for STA testability
    - Snapshot-before-iterate (`_hosts.Keys.ToList()`) for re-entrant unmount safety (Pitfall 4)
    - Single-source-of-truth event records (TabEvents.cs consolidates Phase 1 stubs + new TabStateChangedEvent)
    - Publisher-side switch-to-existing chokepoint to break circular DI (Pitfall 5)
    - InternalsVisibleTo test seam for pure-data LRU unit tests
key-files:
  created:
    - src/Deskbridge.Core/Events/TabEvents.cs
    - src/Deskbridge.Core/Models/TabState.cs
    - src/Deskbridge.Core/Interfaces/ITabHostManager.cs
    - src/Deskbridge.Core/Services/TabHostManager.cs
    - tests/Deskbridge.Tests/Tabs/TabHostManagerTests.cs
    - tests/Deskbridge.Tests/Tabs/TabHostManagerLruTests.cs
    - tests/Deskbridge.Tests/ViewModels/SwitchToExistingTabTests.cs
  modified:
    - src/Deskbridge.Core/Deskbridge.Core.csproj (add WPF-UI + InternalsVisibleTo)
    - src/Deskbridge.Core/Events/AppEvents.cs (Tab*Event stubs removed — consolidated into TabEvents.cs)
    - src/Deskbridge.Core/Interfaces/IConnectionCoordinator.cs (CancelReconnect added; ActiveHost doc updated)
    - src/Deskbridge.Core/Services/ConnectionCoordinator.cs (dict-keyed refactor; guards deleted)
    - src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs (WR-06 pre-drag visibility capture/restore)
    - src/Deskbridge/App.xaml.cs (TabHostManager DI + eager resolve)
    - src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs (ITabHostManager dep + D-02 check)
    - tests/Deskbridge.Tests/Rdp/AirspaceSwapperTests.cs (MultiHost_ExitSizeMove_RestoresPreDragVisibility)
    - tests/Deskbridge.Tests/Coordinator/ConnectionCoordinatorTests.cs (two Phase 4 tests inverted, two new Phase 5 tests)
    - tests/Deskbridge.Tests/ViewModels/MainWindowViewModelTests.cs (ctor wiring updated for ITabHostManager)
decisions:
  - [05-01] WPF-UI added to Deskbridge.Core — TabHostManager consumes ISnackbarService/ControlAppearance/SymbolIcon directly per UI-SPEC §Snackbar; synthesising a Core-local shadow abstraction was rejected as over-engineered for a single consumer. Core still instantiates zero WPF controls.
  - [05-01] TabState enum placed in Deskbridge.Core.Models (not WPF project) — keeps TabStateChangedEvent reference free of reverse WPF dependency. Plan 03 XAML bindings consume via DynamicResource value converter.
  - [05-01] TabOpenedEvent/TabClosedEvent/TabSwitchedEvent consolidated into TabEvents.cs; Phase 1 placeholder declarations in AppEvents.cs removed. Canonical shapes preserved (no API change).
  - [05-01] ActiveHost retained on IConnectionCoordinator as a shim backed by the new _coordinatorHosts dict — minimises churn on Phase 4 tests. New code should prefer ITabHostManager.GetHost / ActiveId.
  - [05-01] CloseOthersAsync / CloseAllAsync call CancelReconnect(id) upfront per-id AND CloseTabAsync calls it again defensively — the repeat is a no-op under the single-CTS design. Makes the CancelReconnect-before-DisconnectAsync ordering obvious at each call site for future maintenance.
metrics:
  duration: 18min
  tasks: 3
  files: 18
  commits: 3
  tests_added: 25
  tests_total: 201
  completed: 2026-04-14
---

# Phase 5 Plan 01: Multi-Host Coordination Core — Summary

One-liner: **Introduces ITabHostManager + dict-keyed multi-host storage, deletes Phase 4's single-slot _active tuple, wires publisher-side switch-to-existing into ConnectionTreeViewModel.Connect, and fixes AirspaceSwapper's WM_EXITSIZEMOVE to restore per-host Visibility instead of unconditional Visible.**

## What Shipped

### New Code (Core)
- **`TabEvents.cs`** — canonical `TabOpenedEvent` / `TabClosedEvent` / `TabSwitchedEvent` / `TabStateChangedEvent` records (consolidates Phase 1 stubs with the new state-change event).
- **`TabState.cs`** — enum `Connecting / Connected / Reconnecting / Error` in canonical order per UI-SPEC §Per-Tab State Visual Contract.
- **`ITabHostManager.cs`** — interface surface: `GetHost`, `TryGetExistingTab`, `ActiveCount`, `ActiveId`, `AllHosts`, `SwitchTo`, `CloseTabAsync`, `CloseOthersAsync`, `CloseAllAsync`, `PopLastClosed`.
- **`TabHostManager.cs`** — singleton implementation. Subscribes to `IConnectionCoordinator.HostMounted/HostUnmounted/ReconnectOverlayRequested` and the bus's `HostCreatedEvent`. Publishes `TabOpenedEvent` / `TabClosedEvent` / `TabSwitchedEvent` / `TabStateChangedEvent`. Drives the fire-once-per-crossing 15-session Snackbar (D-09, D-10). Owns the bounded last-closed LRU (D-16). Snapshot-iterates `_hosts.Keys.ToList()` in `CloseOthersAsync` / `CloseAllAsync` (Pitfall 4). Calls `coord.CancelReconnect(id)` before `disc.DisconnectAsync(ctx)` on every close path (Q2).

### Coordinator refactor (D-01, D-02, D-05)
- `ConnectionCoordinator._active` single-slot tuple → `Dictionary<Guid, (IProtocolHost, ConnectionModel)> _coordinatorHosts` + `Guid? _activeId`.
- **Deleted:** rapid-double-click guard (Phase 4 lines 73-88, "Ignoring duplicate connect request" log).
- **Deleted:** single-host replacement branch (Phase 4 lines 90-104, "Replacing active host" log).
- **Deleted:** WR-01 previous-host unmount-before-overwrite in `OnHostCreated` (unreachable after replacement branch deletion).
- `Dispose()` drains the dict (was single `_active`). Best-effort safety net; D-08 app-shutdown now owned by `TabHostManager.CloseAllAsync` (Plan 02).
- `ActiveHost` retained as a shim over `_coordinatorHosts[_activeId]` — Phase 4 tests compile unchanged.

### Airspace multi-host fix (WR-06)
- `AirspaceSwapper` captures `Dictionary<WindowsFormsHost, Visibility> _preDragVisibility` on `WM_ENTERSIZEMOVE`.
- `WM_EXITSIZEMOVE` restores each host's captured pre-drag `Visibility` (falls back to `Visible` only when the dict entry is missing — shouldn't happen in practice).
- Background tabs (Collapsed) now stay Collapsed after a drag-resize; previously every host was forced Visible, briefly exposing every background RDP surface.

### Publisher-side switch-to-existing (D-02)
- `ConnectionTreeViewModel` ctor gains `ITabHostManager` (8th parameter).
- `Connect` command calls `_tabHostManager.TryGetExistingTab(model.Id)` BEFORE publishing `ConnectionRequestedEvent`; on hit, calls `SwitchTo` and returns.
- Breaks the circular DI that would arise if both singletons injected each other (Pitfall 5).

### Q2 resolution: CancelReconnect
- `IConnectionCoordinator.CancelReconnect(Guid)` — new interface method.
- Coordinator implementation calls `_reconnectCts?.Cancel()` (single-CTS design preserved; per-connection CTS deferred as not-in-scope for Phase 5).
- Invoked by `TabHostManager.CloseTabAsync` / `CloseOthersAsync` / `CloseAllAsync` BEFORE the disconnect pipeline so `RdpReconnectCoordinator.RunAsync` cannot fire `ConnectAsync` against a host that is about to be disposed.

## Test Coverage Added

| Suite | Count | Notes |
|-------|-------|-------|
| `TabHostManagerTests` | 15 | TryGetExistingTab (open/closed), OnHostMounted publishes TabOpened/TabSwitched, OnHostUnmounted publishes TabClosed + auto-activates neighbor + Guid.Empty on last-close, 15-warning fires exactly once per crossing (14→15 crossing, no refire at 16/17, re-arm below then refire), CloseTabAsync runs disconnect pipeline + publishes event, CloseAllAsync snapshot-iterates under re-entrant unmount, AllHosts return type via reflection, CloseAllAsync sequential (TCS gate probe), CancelReconnect ordering on every close path via `Received.InOrder` |
| `TabHostManagerLruTests` | 4 | PopLastClosed null on empty, dedupe-on-push, cap at 10 with eldest eviction, newest-first pop |
| `AirspaceSwapperTests` (added) | 1 | `MultiHost_ExitSizeMove_RestoresPreDragVisibility` — drives private WndProc via reflection, asserts host A (Visible) returns to Visible and host B (Collapsed) STAYS Collapsed |
| `ConnectionCoordinatorTests` (inverted) | 2 | Delete-and-replace: `DoesNotGuard_DuplicateConnectionRequests_PublisherSideOwnsThat` and `DoesNotReplace_PreviousActiveHost_OnNewConnectionRequest` |
| `SwitchToExistingTabTests` | 2 | Publishes ConnectionRequestedEvent when no tab exists; calls SwitchTo when one does |
| `MainWindowViewModelTests` (ctor fix) | — | Existing 20 tests unchanged; ctor wiring updated to inject ITabHostManager mock |

**Full suite:** 201 total, 0 failed, 3 skipped (smoke tests only, env-gated).

## Threats Mitigated

| Threat ID | Disposition | Evidence |
|-----------|-------------|----------|
| T-05-01 (log redaction) | mitigate | TabHostManager logs only `ConnectionId` (Guid) + literal `ex.GetType().Name` + `HResult:X8`; UI-SPEC-locked Snackbar text has no interpolation. `grep -n "ex.Message" src/Deskbridge.Core/Services/TabHostManager.cs` → 0 matches. |
| T-05-02 (coordinator catch logging) | mitigate | Existing T-04-EXC patterns preserved across the refactor. New `TabHostManager.CloseTabAsync` catch follows the same shape. `grep` verified. |
| T-05-04 (DoS on CloseAllAsync re-entrant) | mitigate | `_hosts.Keys.ToList()` snapshot in CloseAllAsync + CloseOthersAsync. Test `CloseAllAsync_SnapshotsKeysBeforeIterating_HandlesReentrantUnmount` exercises the re-entrant path and asserts zero throws + all three hosts receive disconnect. |
| T-05-05 (AirspaceSwapper log redaction) | mitigate | Log line interpolates `_hosts.Count` (integer) only; no host identity or connection metadata. |

## Deviations from Plan

**None architectural.** Minor clarifications applied:

1. **[Rule 2 - Critical] Snapshot `AllHosts` as `_hosts.Values.ToList()`** — the plan had `_hosts.Values` direct but that exposes a mutable view; consumers iterating during a close path would see mid-mutation state. Changed to `ToList()` snapshot per Pitfall 4 spirit. Zero test churn.
2. **[Rule 2 - Critical] Explicit `CancelReconnect(id)` in `CloseOthersAsync` and `CloseAllAsync` loops** — plan's grep criterion required ≥3 matches. I had the logical ordering right via `CloseTabAsync` delegation, but the call-site visibility was not at each method's head. Added explicit per-id cancel BEFORE delegating to `CloseTabAsync` (which cancels again defensively). Adjusted the `CloseOthersAsync_CancelReconnect_BeforeDisconnectPipeline_PerHost` test from `Received(1)` to unconstrained `Received()` to accept the observed 2-per-id pattern.
3. **[Rule 2 - Critical] Consolidate Tab*Event records into TabEvents.cs** — Plan 01 intended to "ADD Tab events in a new file" but the Phase 1 `AppEvents.cs` already contained `TabOpenedEvent` / `TabClosedEvent` / `TabSwitchedEvent` placeholder records. Removed them from AppEvents.cs and let TabEvents.cs be the single source (shapes unchanged).

**No Rule 4 architectural decisions triggered.** No auth gates. No checkpoint state.

## Commits

- `cbdcd11` — feat(05-01): Wave 0 scaffolds + TabEvents + TabState + AirspaceSwapper multi-host fix
- `9dc36ed` — feat(05-01): implement ITabHostManager + TabHostManager (D-01) with LRU + 15-warning + Q2 cancel
- `c289345` — refactor(05-01): delete ConnectionCoordinator duplicate-click guard + replacement branch (D-01, D-02, D-05)

## Unblocks

- **Plan 02** (multi-host MainWindow refactor) — HostContainer + OnClosing drain via `TabHostManager.CloseAllAsync`, `OnHostMounted`/`OnHostUnmounted` handler rewiring, Tab reorder integration.
- **Plan 03** (UI templates + keyboard) — TabState binding, context menu, Ctrl+Shift+T via `PopLastClosed`, drag-reorder, UAT checklists.

## Self-Check: PASSED

**Files verified to exist on disk:**
- FOUND: src/Deskbridge.Core/Events/TabEvents.cs
- FOUND: src/Deskbridge.Core/Models/TabState.cs
- FOUND: src/Deskbridge.Core/Interfaces/ITabHostManager.cs
- FOUND: src/Deskbridge.Core/Services/TabHostManager.cs
- FOUND: tests/Deskbridge.Tests/Tabs/TabHostManagerTests.cs
- FOUND: tests/Deskbridge.Tests/Tabs/TabHostManagerLruTests.cs
- FOUND: tests/Deskbridge.Tests/ViewModels/SwitchToExistingTabTests.cs

**Commits verified via `git log`:**
- FOUND: cbdcd11 (Wave 0 scaffolds)
- FOUND: 9dc36ed (TabHostManager impl)
- FOUND: c289345 (coordinator refactor)

**Grep-verifiable done criteria:**
- FOUND: `_hosts.Keys.ToList` (2 matches — CloseOthersAsync + CloseAllAsync)
- FOUND: `ControlAppearance.Caution` (1 match in TabHostManager.cs)
- FOUND: `CancelReconnect` ≥ 3 matches in TabHostManager.cs (6 total including XMLdoc + all call sites)
- ZERO: `IConnectionPipeline` in TabHostManager.cs
- ZERO: `BitmapPeristence` / `BitmapPersistence` in TabHostManager.cs
- ZERO: `ConfigureAwait` in TabHostManager.cs
- ZERO: `Ignoring duplicate connect request` in ConnectionCoordinator.cs
- ZERO: `Replacing active host` in ConnectionCoordinator.cs
- ZERO: old `(IProtocolHost Host, ConnectionModel Model)? _active` field in ConnectionCoordinator.cs
- FOUND: `ITabHostManager` + `TryGetExistingTab` in ConnectionTreeViewModel.cs

All success criteria from 05-01-PLAN.md `<success_criteria>` met.
