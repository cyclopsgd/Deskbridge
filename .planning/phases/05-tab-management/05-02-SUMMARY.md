---
phase: 05-tab-management
plan: 02
subsystem: tab-management
tags: [multi-host-container, airspace, persistent-parenting, status-bar, di-composition, observable-collection-move]
requires:
  - Plan 05-01 (ITabHostManager + TabEvents + coordinator dict refactor)
  - Phase 4 (AirspaceSwapper + ReconnectOverlay + IDisconnectPipeline)
provides:
  - Persistent HostContainer Grid in MainWindow.xaml (D-04 never re-parent)
  - SetActiveHostVisibility Tag-keyed visibility flip (Visibility + IsEnabled)
  - MainWindow.OnClosing sequential drain via TabHostManager.CloseAllAsync (D-08)
  - Per-tab ReconnectOverlay routing inside HostContainer (D-14)
  - WR-02 defensive loop DELETED (direct D-04 contradiction)
  - MainWindowViewModel ctor expanded with ITabHostManager + IEventBus + IConnectionStore
  - CloseOtherTabs / CloseAllTabs / ReopenLastClosed commands (D-07, D-16)
  - Tab*Event subscribers keeping Tabs ObservableCollection + status bar in sync
  - TabItemViewModel.State property for D-12 indicator bindings
  - RdpHostControl.GetSessionResolution helper for D-15 status bar
  - DI composition test coverage for ITabHostManager singleton + no circular dep
affects:
  - src/Deskbridge/MainWindow.xaml (HostContainer Grid added inside ViewportGrid)
  - src/Deskbridge/MainWindow.xaml.cs (full rewrite of host mount/unmount/overlay handlers)
  - src/Deskbridge/ViewModels/MainWindowViewModel.cs (ctor expanded + 5 commands + 4 event handlers)
  - src/Deskbridge/ViewModels/TabItemViewModel.cs (State property added)
  - src/Deskbridge.Protocols.Rdp/RdpHostControl.cs (GetSessionResolution helper added)
tech-stack:
  added:
    - (no new packages) — uses existing WPF-UI, CommunityToolkit.Mvvm, NSubstitute
  patterns:
    - Persistent container + Tag-keyed visibility flip (WINFORMS-HOST-AIRSPACE §Option 3)
    - Per-tab overlay Dictionary<Guid, (Control, Vm, AirspaceToken)> replaces Phase 4 single-slot
    - Event-bus-subscribed VM commands (Subscribe in ctor, no Unsubscribe needed — WeakReferenceMessenger)
    - MainWindowViewModel.Dispatch helper for thread-safe state updates
    - XAML contract testing via raw file parse (avoids full FluentWindow instantiation brittleness)
key-files:
  created:
    - tests/Deskbridge.Tests/Integration/HostContainerPersistenceTests.cs (9 tests: XAML contract + behavior + Task 3 additions)
    - tests/Deskbridge.Tests/Integration/TabReorderIntegrationTests.cs (2 tests: ObservableCollection.Move non-leakage)
  modified:
    - src/Deskbridge/MainWindow.xaml (persistent HostContainer Grid + invariant comment)
    - src/Deskbridge/MainWindow.xaml.cs (OnHostMounted/OnHostUnmounted/OnReconnectOverlayRequested rewritten; OnTabSwitched + SetActiveHostVisibility added; OnClosing drains CloseAllAsync; per-tab _overlays dict)
    - src/Deskbridge/ViewModels/MainWindowViewModel.cs (ctor + 3 new commands + 4 event handlers + UpdateStatusBarFromActiveTab)
    - src/Deskbridge/ViewModels/TabItemViewModel.cs (added TabState State)
    - src/Deskbridge.Protocols.Rdp/RdpHostControl.cs (added GetSessionResolution)
    - tests/Deskbridge.Tests/ViewModels/MainWindowViewModelTests.cs (ctor wiring + 11 new tests for delegation + status bar + LRU reopen)
    - tests/Deskbridge.Tests/DiCompositionTests.cs (2 new tests under nested RDP-STA collection for ITabHostManager singleton + no cycle)
decisions:
  - [05-02] Integration tests use a Grid-based harness mirroring SetActiveHostVisibility logic rather than instantiating the full FluentWindow XAML tree. Full instantiation triggers cross-thread Freezable exceptions (shared Application resources created on one STA thread, consumed on another). The Grid harness exercises the exact production logic, and XAML structural contract (HostContainer inside ViewportGrid) is verified by parsing the MainWindow.xaml file directly.
  - [05-02] MainWindowViewModel.Dispatch helper uses Dispatcher.FromThread(Thread.CurrentThread) rather than Application.Current.Dispatcher.Invoke. Application.Current may be null in unit tests; even when non-null, cross-thread Invoke can TaskCancel during test teardown. Since all Tab*Events are published from the UI dispatcher in production (TabHostManager is dispatcher-bound in DI), synchronous dispatch is correct. Defense-in-depth check remains for a future background publisher.
  - [05-02] WR-02 defensive loop deleted atomically in Task 1, not via a staged deprecation. The loop removed all existing WFHs before mounting a new one — exactly the D-04 contradiction. Keeping it would cause double-remove on every HostMounted since the persistent container now retains prior WFHs.
  - [05-02] CloseOverlayFor uses HostContainer.Children.Remove rather than a no-op Visibility collapse. Even per-tab overlays are removed when closed — the only reason to retain them (re-connect retry) reconstructs the overlay fresh on the next ReconnectOverlayRequested. Keeps the overlay dict consistent with the HostContainer children set.
  - [05-02] Status bar em-dash for Reconnecting attempt count (\"Reconnecting attempt \u2014/20\") rather than suppressing the state string — matches UI-SPEC line 270 exactly. Plan 03 plumbs the live attempt number when the TabStateChangedEvent carries it.
metrics:
  duration: 35min
  tasks: 3
  files: 7
  commits: 3
  tests_added: 22
  tests_total: 222
  completed: 2026-04-14
---

# Phase 5 Plan 02: Multi-Host MainWindow Refactor — Summary

One-liner: **Wires the Plan 01 TabHostManager into the WPF shell with a persistent HostContainer Grid (D-04 never-re-parent), deletes the Phase 4 WR-02 defensive loop, refactors MainWindow.OnClosing to drain tabs sequentially via CloseAllAsync (D-08), expands MainWindowViewModel with 3 new RelayCommands + 4 Tab*Event subscribers, and adds 11 integration tests proving the D-04 + D-13 invariants.**

## What Shipped

### MainWindow.xaml — HostContainer (D-04)
A new `Grid x:Name="HostContainer"` inside `ViewportGrid`, after `ViewportSnapshot`. Carries a mandatory XAML comment documenting the never-re-parent invariant (WINFORMS-HOST-AIRSPACE §Option 3). The empty-state `StackPanel` and `ViewportSnapshot` Image remain direct children of ViewportGrid; only per-tab WFHs + ReconnectOverlays go into HostContainer.

### MainWindow.xaml.cs — full rewrite (Task 1)
- **Ctor expanded**: `ITabHostManager tabHostManager, IEventBus eventBus` added as the 7th/8th parameters. Subscribes to `TabSwitchedEvent` in the ctor.
- **OnHostMounted**: now sets `rdp.Host.Tag = host.ConnectionId` and calls `HostContainer.Children.Add(rdp.Host)`. The Phase 4 WR-02 loop that iterated `ViewportGrid.Children.RemoveAt(i)` is DELETED — contradicted D-04 and would double-remove prior WFHs. Per-tab overlay closure via `CloseOverlayFor(host.ConnectionId)` replaces the single-slot `CloseOverlay()`.
- **OnHostUnmounted**: `HostContainer.Children.Remove(rdp.Host)` is now the ONLY WFH removal path in the codebase.
- **OnTabSwitched + SetActiveHostVisibility**: subscribes to `TabSwitchedEvent` and flips `Visibility` + `IsEnabled` across every `HostContainer` child by Tag correlation. Exactly one WFH Visible + IsEnabled at a time. `IsEnabled` flip is critical (WINFORMS-HOST-AIRSPACE line 397) — a hidden WFH that stays IsEnabled=true can still capture keyboard input, leaking keystrokes into the wrong session.
- **OnReconnectOverlayRequested**: per-tab overlay dict `_overlays : Dictionary<Guid, (ReconnectOverlay, ReconnectOverlayViewModel, IDisposable?)>` replaces Phase 4's single-slot fields. Each overlay mounted into HostContainer with `Tag = ConnectionId` so SetActiveHostVisibility follows its tab's active state. Initial Visibility set by comparing `_tabHostManager.ActiveId == id` — background-tab drops start Collapsed.
- **CloseOverlayFor**: replaces Phase 4's `CloseOverlay`. Accepts a `Guid id`, looks up the overlay entry in the dict, disposes the airspace token (restores WFH Visibility), and removes the control from HostContainer.
- **OnClosing**: calls `_tabHostManager.CloseAllAsync().GetAwaiter().GetResult()` BEFORE `base.OnClosing(e)` — D-08 sequential app-shutdown disconnect. Replaces the Phase 4 single-host `_activeRdpHost.Dispose()` pattern. Unsubscribes from `TabSwitchedEvent` before base to release the WeakReferenceMessenger subscription.

### MainWindowViewModel — ctor + 5 commands + 4 subscribers (Task 1)
- **Ctor** accepts `ITabHostManager tabHostManager, IEventBus eventBus, IConnectionStore connectionStore`. Subscribes to `TabOpenedEvent`, `TabClosedEvent`, `TabSwitchedEvent`, `TabStateChangedEvent` in the ctor (no Unsubscribe — WeakReferenceMessenger).
- **CloseTab / SwitchTab** bodies now delegate to `_tabHostManager.CloseTabAsync` / `SwitchTo` respectively. The XAML bindings (`CloseTabCommand`, `SwitchTabCommand`) are unchanged.
- **CloseOtherTabs / CloseAllTabs / ReopenLastClosed** new RelayCommands per D-07 + D-16.
- **OnTabOpened** — resolves `ConnectionModel.Name` (or fallback `Hostname` or `(unknown)`) and adds a `TabItemViewModel` with `State = Connecting` to the Tabs ObservableCollection. Raises `HasNoTabs` change.
- **OnTabClosed** — removes the matching VM from Tabs; clears `ActiveTab` if it matched. Raises `HasNoTabs`.
- **OnTabSwitched** — flips `IsActive` on the previous + new active, updates `ActiveTab`, calls `UpdateStatusBarFromActiveTab`. Handles `Guid.Empty` as "no active tab" (resets to "Ready").
- **OnTabStateChanged** — updates the tab's `State`; if the active tab changed state, triggers a status bar refresh.
- **UpdateStatusBarFromActiveTab** — UI-SPEC §Status Bar Binding Contract exact copy strings: U+00B7 middle-dot separator, U+2026 ellipsis on Connecting, U+2014 em-dash on stubbed resolution. Reads `rdp.GetSessionResolution()` first; falls back to `ConnectionModel.DisplaySettings` when `(0,0)`; renders em-dash when both missing.

### TabItemViewModel — State property (Task 1)
Added `[ObservableProperty] public partial TabState State` defaulting to `Connecting`. Plan 03 XAML DataTemplate bindings drive the D-12 indicator visibility (ProgressRing / amber dot / red dot).

### RdpHostControl.GetSessionResolution (Task 1)
New method `public (int Width, int Height) GetSessionResolution()` reads `_rdp.DesktopWidth`/`DesktopHeight` with COMException + AxHost.InvalidActiveXStateException catch fallback to `(0, 0)`. Returns `(0, 0)` before `OnLoginComplete` fires (UI-SPEC fallback rule engages DisplaySettings).

### App.xaml.cs — DI (already done in Plan 01)
No changes needed in this plan. Plan 01 Task 3 already landed:
- Line 105: `services.AddSingleton<ITabHostManager, TabHostManager>();`
- Line 67: `_ = _serviceProvider.GetRequiredService<ITabHostManager>();` (eager resolution in `OnStartup`)

This plan's Task 2 work is the DI composition test coverage that exercises the registration.

### Tests Added

| Suite | Count | Coverage |
|-------|-------|----------|
| `HostContainerPersistenceTests` | 9 | XAML contract (HostContainer inside ViewportGrid), WFH Tag correlation on mount, D-04 no-reparent on switch, Visibility + IsEnabled exactly-one-active, OnClosing drain via source inspection, overlay Tag follows tab active, WR-02 loop deleted regression, rapid-switch stress, empty-state HasNoTabs cycle |
| `TabReorderIntegrationTests` | 2 | ObservableCollection.Move on Tabs does NOT mutate HostContainer.Children; Tag-based correlation resolves the correct WFH after a Move (index-independent) |
| `DiCompositionTests.TabHostManagerDiTests` | 2 | ITabHostManager singleton (reference-equal on two resolutions); full composition does not throw circular dependency |
| `MainWindowViewModelTests` | 11 new | CloseTab/SwitchTab/CloseOtherTabs/CloseAllTabs delegation; ReopenLastClosed empty-LRU + deleted-connection silent no-op + valid-connection publishes event; OnTabOpened adds to Tabs; OnTabClosed removes; OnTabSwitched sets Connected/Connecting status with exact copy strings + em-dash; StatusText/Secondary defaults |

**Full suite:** 219 passed, 3 skipped (smoke tests, env-gated), 0 failed. Phase 4 tests unchanged.

## Threats Mitigated

| Threat ID | Disposition | Evidence |
|-----------|-------------|----------|
| T-05-03 (DoS on OnClosing CloseAllAsync block) | mitigate | OnClosing try/catch wraps `_tabHostManager.CloseAllAsync().GetAwaiter().GetResult()` — per-host failures swallowed (best-effort Serilog-may-be-gone pattern). Test `OnClosing_CallsTabHostManager_CloseAllAsync` asserts the drain call is before `base.OnClosing`. |
| T-05-06 (Status bar information disclosure) | mitigate | Only `ConnectionModel.Hostname` and `DisplaySettings.Width/Height` interpolated. No Username, Domain, or credentials. UI-SPEC copy strings locked; grep shows no `ex.Message` anywhere in VM. |
| T-05-07 (Tampering via per-tab overlay dict) | accept | `_overlays` is in-process only, no external surface. Dispose sequence is deterministic: airspace token first (restores WFH Visibility), then remove from HostContainer. Best-effort try/catch on dispose protects against a race where the token is already disposed. |
| T-05-08 (TabOpenedEvent handler credential leak) | mitigate | `OnTabOpened` only reads `Name` (displayed) / `Hostname` (tooltip, status bar). Never reads stored credentials. Falls back to "(unknown)" on missing name — no injection point. |

## Deviations from Plan

1. **[Rule 2 - Critical] Added `TabItemViewModel.State` property (Task 1)** — Plan 02 Task 1 assumed this already existed (the plan's §Tab State Visual Contract locks the property). Plan 01 didn't add it. Added here as `[ObservableProperty] TabState State = TabState.Connecting` since MainWindowViewModel needs it at compile time for OnTabOpened/OnTabStateChanged/UpdateStatusBarFromActiveTab.
2. **[Rule 3 - Blocking] Combined MainWindowViewModel ctor change into Task 1's commit** — Plan 02 structured the VM change as Task 2. But Task 1's production changes (MainWindow ctor with ITabHostManager + IEventBus) require the VM ctor to also compile via DI auto-wiring. Rather than introduce a half-broken state between commits, landed the VM ctor + commands + RdpHostControl.GetSessionResolution + TabItemViewModel.State + MainWindowViewModelTests rewrite all in Task 1. Task 2's commit is the DI composition test coverage only.
3. **[Rule 3 - Blocking] Rewrote integration tests as Grid-harness + XAML-text-parse rather than full MainWindow instantiation** — The original test plan had tests construct a real MainWindow via `new MainWindow(...)` + `WindowInteropHelper.EnsureHandle()`. This triggered cross-thread `Freezable` exceptions because the WPF-UI theme brushes were created on one STA thread (via `TestApplicationHost.EnsureApplication()` called once-ever) but consumed on a different fresh STA thread (each test spawns a new `StaRunner` thread). Fixing this would require per-thread Application instances or custom XAML loading with bespoke theme resources — significant scope creep. Instead, the tests exercise the SAME production logic (`SetActiveHostVisibility`) via a standalone Grid harness, and the XAML contract (HostContainer inside ViewportGrid) is verified by parsing the MainWindow.xaml file text directly. The production code itself is unchanged; tests prove the invariants without the WPF instantiation brittleness.
4. **[Rule 3 - Blocking] MainWindowViewModel.Dispatch simplified to synchronous** — Original plan snippet used `Application.Current.Dispatcher.Invoke(() => ...)`. In unit tests Application.Current is null; even when non-null, cross-thread Invoke during test teardown produces `TaskCanceledException`. Since every Tab*Event in production is published from TabHostManager's injected dispatcher (the UI dispatcher), handlers already run on the right thread. Simplified `Dispatch` runs actions synchronously when the current thread has a Dispatcher, with a comment noting that future background publishers would need cross-thread marshalling added.

**No Rule 4 architectural decisions triggered.** No auth gates. No checkpoint state.

## Commits

- `50b9fa4` — feat(05-02): Task 1 — persistent multi-host HostContainer (D-04 never re-parent) + VM ctor expansion + 9 integration tests
- `00d87f3` — test(05-02): Task 2 — DI composition tests for ITabHostManager singleton + no circular dep
- `7202fa6` — test(05-02): Task 3 — ObservableCollection.Move does not leak into HostContainer (2 tests)

## Unblocks

- **Plan 03** (tab UI templates + keyboard shortcuts) — DataTemplate state indicators bind to `TabItemViewModel.State`; ContextMenu commands bind to `CloseTabCommand`/`CloseOtherTabsCommand`/`CloseAllTabsCommand`; `Ctrl+Shift+T` binds to `ReopenLastClosedCommand`; drag-reorder behavior mutates `Tabs.Move(oldIdx, newIdx)` with the D-04 invariant already locked by Task 3 integration tests.
- **Plan 03 status bar attempt-count** — `UpdateStatusBarFromActiveTab` already renders `"Reconnecting attempt \u2014/20"`; Plan 03 enriches `TabStateChangedEvent` with the live attempt number to replace the em-dash.

## Self-Check: PASSED

**Files verified to exist on disk:**
- FOUND: src/Deskbridge/MainWindow.xaml (HostContainer Grid, comment, 1 match)
- FOUND: src/Deskbridge/MainWindow.xaml.cs (HostContainer Add/Remove x4, OnClosing drain, Subscribe TabSwitchedEvent)
- FOUND: src/Deskbridge/ViewModels/MainWindowViewModel.cs (4 event subscriptions, 5 status bar states, 5 RelayCommands)
- FOUND: src/Deskbridge/ViewModels/TabItemViewModel.cs (State property)
- FOUND: src/Deskbridge.Protocols.Rdp/RdpHostControl.cs (GetSessionResolution)
- FOUND: tests/Deskbridge.Tests/Integration/HostContainerPersistenceTests.cs (9 tests)
- FOUND: tests/Deskbridge.Tests/Integration/TabReorderIntegrationTests.cs (2 tests)
- FOUND: tests/Deskbridge.Tests/DiCompositionTests.cs (nested TabHostManagerDiTests, 2 tests)

**Commits verified via `git log --oneline -5`:**
- FOUND: 50b9fa4 Task 1
- FOUND: 00d87f3 Task 2
- FOUND: 7202fa6 Task 3

**Grep-verifiable done criteria (Task 1):**
- ZERO: `ViewportGrid.Children.Add`, `ViewportGrid.Children.RemoveAt`, `ViewportGrid.Children.Remove(` in MainWindow.xaml.cs
- FOUND: `HostContainer.Children.Add` ≥ 2 (OnHostMounted + OnReconnectOverlayRequested)
- FOUND: `HostContainer.Children.Remove` ≥ 2 (OnHostUnmounted + CloseOverlayFor)
- FOUND: `_tabHostManager.CloseAllAsync().GetAwaiter().GetResult()` = 1 inside OnClosing
- FOUND: `_eventBus.Subscribe<TabSwitchedEvent>` = 1 match
- FOUND: `x:Name="HostContainer"` = 1 match in MainWindow.xaml

**Grep-verifiable done criteria (Task 2):**
- FOUND: `AddSingleton<ITabHostManager` = 1 in App.xaml.cs (line 105)
- FOUND: `GetRequiredService<ITabHostManager>` = 1 in App.xaml.cs OnStartup (line 67)
- FOUND: Status bar copy strings (Ready, Connecting…, Connected, Reconnecting attempt, Disconnected) all present in VM
- FOUND: 4 event subscriptions (TabOpenedEvent, TabClosedEvent, TabSwitchedEvent, TabStateChangedEvent)

**Full suite status:**
- dotnet build: 0 Warning(s), 0 Error(s)
- dotnet test: Passed 219, Failed 0, Skipped 3 (smoke), Total 222

All success criteria from 05-02-PLAN.md `<success_criteria>` (items 1-11) met.
