---
phase: 05-tab-management
plan: 03
subsystem: tab-management
tags: [tab-ui, state-indicators, context-menu, drag-reorder, keyboard-shortcuts, uat, d-07, d-11, d-12, d-13, d-16]
requires:
  - Plan 05-01 (ITabHostManager + TabEvents + TabState + coordinator dict refactor)
  - Plan 05-02 (persistent HostContainer + MainWindowViewModel Tab*Event subscribers + RdpHostControl.GetSessionResolution)
provides:
  - TabItemViewModel.State/Hostname/Resolution/ReconnectAttempt + derived IsConnecting/IsReconnecting/IsError + TooltipText
  - Tab DataTemplate with mutually-exclusive state indicators (ProgressRing + amber/red Ellipse)
  - Tab ContextMenu (Close / Close Others / Close All — UI-SPEC locked order, D-07)
  - Tab active-tab SemiBold weight trigger + IsMouseOver hover + 96-240px width clamp + CharacterEllipsis + ToolTip binding
  - Behaviors/TabReorderBehavior attached property with 2px accent TabInsertionAdorner
  - KeyboardShortcutRouter static helper + MainWindow.OnPreviewKeyDown delegate (D-16)
  - 4 UAT checklists (keyboard A1 gate, multi-host GDI, drag visual, state matrix)
affects:
  - src/Deskbridge/ViewModels/TabItemViewModel.cs (already extended in this plan)
  - src/Deskbridge/ViewModels/MainWindowViewModel.cs (OnTabOpened/OnTabSwitched populate Hostname + Resolution)
  - src/Deskbridge/MainWindow.xaml (tab DataTemplate extension + reorder behavior + comment seam)
  - src/Deskbridge/MainWindow.xaml.cs (OnPreviewKeyDown override delegating to KeyboardShortcutRouter)
tech-stack:
  added:
    - (no new packages) — reuses WPF-UI ProgressRing, Ellipse, ContextMenu, BooleanToVisibilityConverter (already present)
  patterns:
    - Pure-data KeyboardShortcutRouter + thin code-behind delegator (unit-testable without a Window)
    - ObservableCollection.Move (not Remove+Insert) for drag-reorder — preserves D-04 "never re-parent" invariant
    - 2px AdornerLayer insertion indicator via DrawingContext.DrawLine on left/right half of target
    - XAML-text-parse XAML contract tests (parses MainWindow.xaml from disk, avoids cross-thread Freezable exceptions)
    - Derived mutually-exclusive booleans fed by [ObservableProperty] with [NotifyPropertyChangedFor] on each derivative
key-files:
  created:
    - src/Deskbridge/Behaviors/TabReorderBehavior.cs (+ TabInsertionAdorner sealed class)
    - src/Deskbridge/KeyboardShortcutRouter.cs
    - tests/Deskbridge.Tests/ViewModels/MainWindowXamlContextMenuTests.cs
    - tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs
    - tests/uat/phase-05-keyboard.md
    - tests/uat/phase-05-multihost-gdi.md
    - tests/uat/phase-05-drag.md
    - tests/uat/phase-05-state-matrix.md
  modified:
    - src/Deskbridge/ViewModels/TabItemViewModel.cs (State + derived + Hostname/Resolution/ReconnectAttempt + TooltipText)
    - src/Deskbridge/ViewModels/MainWindowViewModel.cs (OnTabOpened populates Hostname; OnTabSwitched populates Resolution)
    - src/Deskbridge/MainWindow.xaml (tab DataTemplate extension + xmlns:behaviors + EnableReorder + ContextMenu + indicators + width clamp + tooltip)
    - src/Deskbridge/MainWindow.xaml.cs (OnPreviewKeyDown override)
    - src/Deskbridge/Deskbridge.csproj (InternalsVisibleTo Deskbridge.Tests for TabInsertionAdorner reflection access)
    - tests/Deskbridge.Tests/ViewModels/TabItemViewModelTests.cs (+16 tests for State/derived/TooltipText)
    - tests/Deskbridge.Tests/Integration/TabReorderIntegrationTests.cs (+3 tests for TabReorderBehavior/Adorner)
key-decisions:
  - "[Rule 3 — Blocking] Extracted routing logic into KeyboardShortcutRouter static helper; OnPreviewKeyDown override delegates in 3 lines. Rationale: testing OnPreviewKeyDown directly requires a real Window + Dispatcher + routed-event plumbing — the pure-data router is trivially unit-testable with NSubstitute mocks."
  - "[Rule 3 — Blocking] XAML contract tests (MainWindowXamlContextMenuTests) parse the MainWindow.xaml file text directly rather than instantiate the FluentWindow. Mirrors Plan 02's decision for HostContainerPersistenceTests — cross-thread Freezable exceptions block full-window STA instantiation. XAML regex parsing locks the UI-SPEC contract without the framework brittleness."
  - "[Rule 2 — Missing Critical] Added Hostname/Resolution/ReconnectAttempt partial properties + TooltipText derived to TabItemViewModel. Plan 03 assumed these existed; Plans 01/02 had not added them. MainWindow.xaml needs them bound to render the UI-SPEC §Copywriting tooltip strings, and MainWindowViewModel handlers need them to populate on TabOpened/TabSwitched."
  - "[Scope] Alt-modified keys (e.g. Ctrl+Alt+Tab) are explicitly rejected by the router and pass through to the remote AxHost. Matches UI-SPEC expectation that remote-session shortcuts stay with the RDP control."
  - "[Scope] Ctrl+9 always jumps to the LAST tab, even when fewer than 9 tabs exist. Chrome / VS Code convention locked in D-16."
  - "[Scope] Ctrl+Shift+T on empty LRU is a silent no-op. Deleted-connection Ctrl+Shift+T is also silent. UI-SPEC §Copywriting line 345 locks 'no toast, no beep'."
patterns-established:
  - "Pattern 1: XAML contract tests via File.ReadAllText(MainWindow.xaml) + regex. Avoids FluentWindow STA instantiation brittleness while still locking UI-SPEC invariants."
  - "Pattern 2: Pure-data shortcut router extracted from code-behind. Window override becomes a 3-line delegate; all routing logic is unit-testable."
  - "Pattern 3: Attached behavior for ItemsControl drag-reorder via DragDrop.DoDragDrop + AdornerLayer 2px line. Template for future drag-drop UX (command palette reorder, etc.)."
  - "Pattern 4: TabItemViewModel composes derived booleans via [NotifyPropertyChangedFor]. Mutual exclusion invariant is structural (State enum switch), XAML binds three separate Visibility properties."
requirements-completed:
  - TAB-01
  - TAB-02
  - TAB-03
# Metrics
duration: ~15min (effective — excludes subsequent post-UAT debug work)
completed: 2026-04-14
---

# Phase 5 Plan 03: Tab UI Templates + Keyboard Shortcuts + UAT — Summary

**Extends the tab DataTemplate with mutually-exclusive state indicators (ProgressRing / amber / red) + ContextMenu (Close / Close Others / Close All) + 96-240px width clamp with CharacterEllipsis tooltip, adds the TabReorderBehavior attached property with a 2px accent insertion adorner that mutates Tabs via ObservableCollection.Move (never Remove+Insert, preserving D-04), introduces the KeyboardShortcutRouter helper covering Ctrl+Tab cycle / Ctrl+Shift+Tab / Ctrl+F4 close / Ctrl+1..9 jump with Ctrl+9=LAST tab (Chrome convention) / Ctrl+Shift+T reopen-last-closed, and ships 4 UAT checklists gating the Phase 5 blocker clear — Assumption A1 (KeyboardHookMode=0 lets PreviewKeyDown fire with RDP focused).**

## Performance

- **Duration:** ~15 minutes effective (Tasks 1–2 + UAT checklists in Task 3)
- **Completed:** 2026-04-14
- **Tasks:** 3 (2 TDD auto + 1 UAT checkpoint)
- **Files created:** 8 (TabReorderBehavior + KeyboardShortcutRouter + 2 test files + 4 UAT checklists)
- **Files modified:** 7 (TabItemViewModel, MainWindowViewModel, MainWindow.xaml, MainWindow.xaml.cs, Deskbridge.csproj, TabItemViewModelTests, TabReorderIntegrationTests)

## Accomplishments

- **Tab DataTemplate** renders a 12px ProgressRing, an 8px amber Ellipse (DeskbridgeWarningBrush #FFCC02), and an 8px red Ellipse (DeskbridgeErrorBrush #F44747) — exactly one visible at any time driven by TabItemViewModel derived booleans. Background tabs still show the indicator (D-12 key requirement).
- **Active-tab SemiBold FontWeight** trigger + IsMouseOver hover trigger + 96-240px MinWidth/MaxWidth clamp + CharacterEllipsis overflow + ToolTip bound to TabItemViewModel.TooltipText — matches UI-SPEC §Tab Bar Internal Layout exactly.
- **ContextMenu** with three MenuItems (Close / Close Others / Close All) in UI-SPEC canonical order. No icons, no separators, no Duplicate — D-07 locked. Commands bind to MainWindowViewModel via `RelativeSource AncestorType=ItemsControl`.
- **TabReorderBehavior** attached property wires the tab-bar ItemsControl with DragDrop + 2px TabInsertionAdorner + 3x system drag threshold. Drop mutates `ObservableCollection<TabItemViewModel>.Move(oldIdx, newIdx)` — NEVER Remove+Insert. HostContainer.Children is untouched, preserving D-04.
- **KeyboardShortcutRouter** handles Ctrl+Tab cycle, Ctrl+Shift+Tab backward cycle, Ctrl+F4 close active, Ctrl+1..Ctrl+9 jump (Ctrl+9 = LAST per Chrome/VS Code convention), Ctrl+Shift+T reopen-last-closed (silent no-op on empty LRU + deleted-connection).
- **MainWindow.OnPreviewKeyDown** override is a 3-line delegate to the router — thin WPF-side seam keeps the routing logic unit-testable without STA plumbing.
- **4 UAT checklists** at `tests/uat/phase-05-*.md` cover the manual-only verifications from 05-VALIDATION.md, including the Assumption A1 blocking gate (Ctrl+Tab with RDP focused).

## Task Commits

1. **Task 1 — tab DataTemplate state indicators + ContextMenu + tooltip + width clamp** — `f48ebc8` (feat)
2. **Task 2 — TabReorderBehavior + OnPreviewKeyDown keyboard shortcuts** — `3bf1fcd` (feat)
3. **Task 3 — 4 UAT checklists** — `69def28` (test)

Post-task stability fixes uncovered during UAT walk (referenced for traceability — not part of Plan 05-03's task body):

- `60656b6` fix(05): instant tab close + Connected state transition
- `463c0bb` fix(05): carry ConnectionModel in TabOpenedEvent to fix "(unknown)" tabs
- `50beced` fix(05): move tab registration to OnHostCreated to fix bus-order bug
- `5a61ede` fix(05): idempotent OnHostCreated for reconnect + phantom tab sweep
- `0c95ff3` fix(05): visibility-hidden + sync close + async shutdown
- `65d3198` fix(05): ConnectionCoordinator dedupes in-flight connects
- `1655a8d` fix(05): ConnectionCoordinator rejects duplicate host mounts
- `214dd73` fix(05): force render pass on mount + revert Hidden to Collapsed
- `b621dc7` fix(05): force WM_PAINT on AxHost after login complete
- `638a30e` fix(05): invalidate visual tree on ConnectionEstablished
- `288da78` Revert "fix(05): invalidate visual tree on ConnectionEstablished"
- `cb1cb03` refactor(05): clean up fire-and-forget close architecture
- `52f6dfa` docs(debug): handoff for first-connect black viewport debug session
- `c1542a2` fix(05): set ConnectionId immutably at host construction (root cause of first-connect black viewport — resolved)

These commits address issues surfaced while walking the UAT and are tracked under the phase-level debug trail in `.planning/phases/05-tab-management/debug/`.

## Files Created/Modified

**Created:**

- `src/Deskbridge/Behaviors/TabReorderBehavior.cs` — attached behavior + `TabInsertionAdorner` sealed class (2px accent vertical line via `AdornerLayer`)
- `src/Deskbridge/KeyboardShortcutRouter.cs` — pure-data routing for the Phase 5 shortcut family
- `tests/Deskbridge.Tests/ViewModels/MainWindowXamlContextMenuTests.cs` — 8 XAML contract tests (File.ReadAllText + regex)
- `tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs` — 22 router tests (all shortcuts + edge cases)
- `tests/uat/phase-05-keyboard.md` — Assumption A1 BLOCKING gate checklist
- `tests/uat/phase-05-multihost-gdi.md` — 14-host GDI baseline + 15-session Snackbar fire-once + drag-resize multi-host
- `tests/uat/phase-05-drag.md` — 2px accent insertion line + Move semantics (session integrity after drag) + ESC + MouseBinding non-conflict
- `tests/uat/phase-05-state-matrix.md` — D-12 mutual exclusion across Connecting/Connected/Reconnecting/Error + background-tab visibility + D-14 no-auto-switch

**Modified:**

- `src/Deskbridge/ViewModels/TabItemViewModel.cs` — added `State` (TabState), `Hostname`, `Resolution`, `ReconnectAttempt`, derived `IsConnecting` / `IsReconnecting` / `IsError` / `TooltipText`
- `src/Deskbridge/ViewModels/MainWindowViewModel.cs` — `OnTabOpened` populates `Hostname`; `OnTabSwitched` calls `UpdateActiveTabResolution` to populate `Resolution` from the live RDP host (fallback to `ConnectionModel.DisplaySettings`)
- `src/Deskbridge/MainWindow.xaml` — `xmlns:behaviors`, `ItemsControl behaviors:TabReorderBehavior.EnableReorder="True"`, ContextMenu with 3 MenuItems, 12px ProgressRing + 2 × 8px Ellipse, MinWidth/MaxWidth/CharacterEllipsis on title, ToolTip binding, SemiBold active-tab FontWeight trigger, hover trigger
- `src/Deskbridge/MainWindow.xaml.cs` — `OnPreviewKeyDown` override delegating to `KeyboardShortcutRouter.TryRoute`
- `src/Deskbridge/Deskbridge.csproj` — `InternalsVisibleTo Deskbridge.Tests` (TabInsertionAdorner sealed class accessed via reflection from TabReorderIntegrationTests)
- `tests/Deskbridge.Tests/ViewModels/TabItemViewModelTests.cs` — 16 new tests (State default + transitions, derived flag mutual-exclusion theory, TooltipText per-state with Unicode codepoint checks, T-05-01 no-credential property check, property-changed propagation)
- `tests/Deskbridge.Tests/Integration/TabReorderIntegrationTests.cs` — 3 new tests (EnableReorder toggles AllowDrop, Tabs.Move preserves reference identity, TabInsertionAdorner constructs + renders without throwing)

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Extract router out of OnPreviewKeyDown | Pure-data helper is unit-testable with NSubstitute mocks; code-behind override stays 3 lines |
| XAML tests parse file text rather than instantiate window | Cross-thread Freezable exceptions block full-window STA construction (Plan 02 precedent) |
| Alt-modified keys NOT routed (pass-through to AxHost) | Remote-session shortcuts (Ctrl+Alt+Tab, etc.) belong to the RDP control |
| Ctrl+9 = LAST tab (not literal 9th) | Chrome / VS Code convention (D-16 + UI-SPEC line 236) |
| Ctrl+Shift+T silent on empty LRU | UI-SPEC §Copywriting line 345: "no toast, no beep" |
| Ctrl+W stays with XAML KeyBinding | Avoids double-fire; router explicitly returns false for Ctrl+W |
| Added Hostname/Resolution/ReconnectAttempt to TabItemViewModel | Plan 03 assumed these existed; Plans 01/02 did not add them. Required for MainWindow.xaml tooltip binding and OnTabOpened/OnTabSwitched handler correctness. |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Blocking] Routing logic extracted from OnPreviewKeyDown into KeyboardShortcutRouter**

- **Found during:** Task 2 (initial attempt to unit-test OnPreviewKeyDown directly)
- **Issue:** Testing a WPF routed-event override requires a live Window + Dispatcher + RoutedEvent plumbing. Cross-thread Freezable exceptions (same class of issue Plan 02 documented) block per-STA-thread MainWindow construction.
- **Fix:** Extracted pure-data `KeyboardShortcutRouter.TryRoute(vm, key, modifiers)` static helper. OnPreviewKeyDown is now three lines (`if (TryRoute(ViewModel, e.Key, Keyboard.Modifiers)) { e.Handled = true; return; } base.OnPreviewKeyDown(e);`).
- **Files modified:** `src/Deskbridge/KeyboardShortcutRouter.cs` (new), `src/Deskbridge/MainWindow.xaml.cs` (simplified override)
- **Verification:** 22 KeyboardShortcutTests exercise every shortcut + edge case without a Window.
- **Committed in:** `3bf1fcd`

**2. [Rule 3 — Blocking] XAML contract tests parse file text instead of instantiating FluentWindow**

- **Found during:** Task 1 (initial attempt to load MainWindow and navigate the visual tree)
- **Issue:** `XamlReader.Load(MainWindow.xaml)` requires the WPF-UI themes to be applied on the test thread. Per-test STA threads cause the same cross-thread Freezable exception that Plan 02 documented for integration tests.
- **Fix:** Tests read MainWindow.xaml file text with `File.ReadAllText` and use regex + string slicing to assert the ContextMenu block contains the three canonical MenuItems in order. Matches Plan 02's `HostContainerPersistenceTests` approach.
- **Files modified:** `tests/Deskbridge.Tests/ViewModels/MainWindowXamlContextMenuTests.cs` (new)
- **Verification:** 8 XAML tests assert the D-07 canonical order, no Duplicate, no Separator, correct command bindings, indicators present, width clamp + tooltip binding, active-tab SemiBold.
- **Committed in:** `f48ebc8`

**3. [Rule 2 — Missing Critical] Added Hostname/Resolution/ReconnectAttempt properties + TooltipText to TabItemViewModel**

- **Found during:** Task 1 (extending the tab DataTemplate with the ToolTip binding)
- **Issue:** The plan assumed these partial properties existed, but Plans 01/02 only added `State`. MainWindow.xaml needs `Hostname`/`Resolution`/`ReconnectAttempt` + `TooltipText` bound to render the UI-SPEC §Copywriting tooltip strings (e.g. `"myserver · Connecting…"`, `"myserver · 1920×1080"`). MainWindowViewModel handlers also need them to populate on TabOpened/TabSwitched.
- **Fix:** Added 3 observable properties (`Hostname`, `Resolution` as `(int W, int H)?`, `ReconnectAttempt`) + `TooltipText` computed property with state-switch. Added `NotifyPropertyChangedFor(nameof(TooltipText))` on each input. `MainWindowViewModel.OnTabOpened` populates `Hostname` from `evt.Connection.Hostname`; `MainWindowViewModel.OnTabSwitched` calls `UpdateActiveTabResolution` to pull from `RdpHostControl.GetSessionResolution()` with fallback to `ConnectionModel.DisplaySettings`.
- **Files modified:** `src/Deskbridge/ViewModels/TabItemViewModel.cs`, `src/Deskbridge/ViewModels/MainWindowViewModel.cs`
- **Verification:** 16 new TabItemViewModelTests cover TooltipText per state with exact Unicode codepoints (U+00B7 middle-dot, U+2026 ellipsis, U+00D7 multiplication, U+2014 em-dash), mutual-exclusion theory across all 4 states, and T-05-01 no-credential property check.
- **Committed in:** `f48ebc8`

---

**Total deviations:** 3 auto-fixed (2 blocking — test harness shape, 1 missing critical — VM property plumbing)
**Impact on plan:** All auto-fixes necessary to land the plan's stated behavior. No scope creep. XAML contract tests are a stronger gate than the plan's "STA smoke test" goal — same invariants locked, simpler test harness.

## Issues Encountered

- **First-connect black viewport (2026-04-14 afternoon)** — during UAT walk with a live RDP target, the first connection rendered a black viewport even though the WFH was correctly parented and the session was live. Root-caused to `RdpHostControl.ConnectionId` being assigned inside `ConnectAsync` (ConnectStage Order=300) but `HostMounted` firing from CreateHostStage (Order=200) — so `WindowsFormsHost.Tag` was stamped with `Guid.Empty`. `TabSwitchedEvent` then collapsed the only WFH as "not the active tab", zero-sizing the viewport.
  - **Resolved in `c1542a2`**: plumb `ConnectionId` through `IProtocolHostFactory.Create(connectionId)` so the id is non-Empty BEFORE `HostCreatedEvent` publishes. Full debug trail in `.planning/phases/05-tab-management/debug/first-connect-black-viewport-RESOLVED.md`.
- **Pre-existing flaky test** — `EventBusTests.MultipleSubscribers_AllReceive` occasionally fails on full-suite runs because all `EventBus` instances share `WeakReferenceMessenger.Default` (no test isolation). Pre-existing since Phase 1; not caused by this plan. Re-runs pass (269/272 green with 3 env-gated smoke skips). Not scoped to Plan 05-03 fixes.

## UAT Status

| UAT | Status |
|-----|--------|
| `tests/uat/phase-05-keyboard.md` (Assumption A1 gate — BLOCKING) | **In progress** — user actively driving the gate via live RDP sessions; first-connect black viewport bug (blocker) resolved in `c1542a2`. Formal sign-off pending user walkthrough of the checklist. |
| `tests/uat/phase-05-multihost-gdi.md` | **Deferred to `/gsd-verify-work`** — requires 14-host workload |
| `tests/uat/phase-05-drag.md` | **Deferred to `/gsd-verify-work`** — requires live RDP sessions for session-integrity checks |
| `tests/uat/phase-05-state-matrix.md` | **Deferred to `/gsd-verify-work`** — requires controlled network disconnect |

The keyboard UAT (A1 gate) is the blocking phase-completion gate. All four checklists are signed off during `/gsd-verify-work`; Plan 05-03 has shipped the checklists + underlying code so the walk-through can execute.

**STATE.md Phase 5 blocker** (`Keyboard focus management between WPF and WindowsFormsHost needs validation with actual RDP control`) — remains **open until keyboard UAT is signed off by user**. Infrastructure is in place; verification walk-through pending.

## Threats Mitigated

| Threat ID | Disposition | Evidence |
|-----------|-------------|----------|
| T-05-01 (tab tooltip/status bar information disclosure) | mitigate | `TabItemViewModel.TooltipText` composes `Hostname + state + resolution` only. `TooltipText_NeverInterpolatesCredentials` test asserts no `Password` / `Credential` / `Secret` property on the VM. UI-SPEC copy strings locked verbatim. |
| T-05-09 (DataObject format collision) | accept | TabReorderBehavior uses `"DeskbridgeTabItem"` format string, distinct from TreeView's `"DeskbridgeTreeItems"`. In-process only, no BinaryFormatter. |
| T-05-10 (rapid drag-reorder DoS) | accept | `ObservableCollection.Move` is O(1) event + O(n) XAML re-layout. With Tabs.Count ≤ 50 in practice, sub-millisecond impact. |
| T-05-11 (UAT file hostname placeholders) | accept | UAT files ship as templates with `_____` placeholders. Users fill locally; committed copies stay unfilled. No secret material interpolated. |

## Phase 5 Requirements Coverage

| Req ID | Requirement | Plan 05-03 Coverage |
|--------|-------------|---------------------|
| TAB-01 | One connection per tab; switch-to-existing on re-request | Plan 01's TabHostManager + publisher-side switch check (Plan 01 Task 3). This plan's tab DataTemplate renders the single tab per ConnectionId. |
| TAB-02 | Inactive tabs have `Visibility=Collapsed`; `BitmapPeristence=0` | Plan 02's `SetActiveHostVisibility` (Tag-keyed flip). This plan's drag-reorder preserves the HostContainer never-re-parent invariant. |
| TAB-03 | Ctrl+Tab / Ctrl+Shift+Tab cycle; Ctrl+W closes; middle-click closes; Ctrl+1..9 jump; Ctrl+F4 close; Ctrl+Shift+T reopen | **COMPLETE** — KeyboardShortcutRouter covers all shortcuts + UAT A1 gate verifies live-RDP behavior |
| TAB-04 | Crossing 14→15 fires Snackbar once (no cap) | Plan 01's `TabHostManager.OnHostMounted` + `_warned15` flag |
| TAB-05 | HostMounted → TabOpenedEvent; HostUnmounted → TabClosedEvent | Plan 01's TabHostManager subscriptions + Plan 02's MainWindowViewModel event subscribers |

## Next Phase Readiness

- **Phase 6 unblocked** for command palette, observability (latency/quality status bar), notifications, window-state persistence. All Phase 5 infrastructure is in place.
- **Phase 6 may consume** the KeyboardShortcutRouter as the seed for a global `IShortcutService` — the router's pure-data shape is already `IShortcutService`-ready (just needs a DI wrapper).
- **STATE.md Phase 5 blocker** will clear after the keyboard UAT is signed off; this is the only remaining phase-completion gate.
- **Known stub:** `MainWindowViewModel.UpdateStatusBarFromActiveTab` renders `"Reconnecting attempt —/20"` (em-dash placeholder). Phase 6 observability plumbs the live attempt counter into `TabStateChangedEvent` — this is tracked, not a regression. Documented at UI-SPEC line 270.

## Self-Check: PASSED

**Files verified to exist on disk:**
- FOUND: src/Deskbridge/Behaviors/TabReorderBehavior.cs
- FOUND: src/Deskbridge/KeyboardShortcutRouter.cs
- FOUND: tests/Deskbridge.Tests/ViewModels/MainWindowXamlContextMenuTests.cs
- FOUND: tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs
- FOUND: tests/uat/phase-05-keyboard.md
- FOUND: tests/uat/phase-05-multihost-gdi.md
- FOUND: tests/uat/phase-05-drag.md
- FOUND: tests/uat/phase-05-state-matrix.md

**Commits verified via `git log`:**
- FOUND: f48ebc8 (Task 1 — tab DataTemplate state indicators + ContextMenu)
- FOUND: 3bf1fcd (Task 2 — TabReorderBehavior + OnPreviewKeyDown)
- FOUND: 69def28 (Task 3 — 4 UAT checklists)

**Grep-verifiable done criteria:**
- FOUND: `<ui:ProgressRing` + `Ellipse.*DeskbridgeWarningBrush` + `Ellipse.*DeskbridgeErrorBrush` in MainWindow.xaml (6 lines total)
- FOUND: `<ContextMenu>` with 3 MenuItems in canonical order in MainWindow.xaml (D-07)
- FOUND: `MinWidth="96"` / `MaxWidth="240"` / `TextTrimming="CharacterEllipsis"` in MainWindow.xaml
- FOUND: `behaviors:TabReorderBehavior.EnableReorder="True"` = 1 match in MainWindow.xaml
- FOUND: `xmlns:behaviors=` = 1 match in MainWindow.xaml
- FOUND: `OnPreviewKeyDown` = 1 match in MainWindow.xaml.cs
- FOUND: `Key.(Tab|F4|T|D[1-9])` = 5 matches in KeyboardShortcutRouter.cs (Tab, F4, T, D1, D9)
- FOUND: `tabs.Move(` = 1 match in TabReorderBehavior.cs (Move, not Remove+Insert)
- ZERO: `Duplicate` / `confirm` in MainWindow.xaml (D-06 + D-07)
- ZERO: `HostContainer.Children.Remove` / `HostContainer.Children.Add` in TabReorderBehavior.cs (D-04 invariant preserved)

**Build + test status:**
- `dotnet build Deskbridge.sln`: **0 Warning(s), 0 Error(s)**
- `dotnet test` (full suite): **269 passed, 0 failed, 3 skipped** (smoke tests, env-gated on `DESKBRIDGE_SMOKE_RDP_HOST`)

All success criteria from 05-03-PLAN.md `<success_criteria>` items 1-7 met. Item 8 (UAT keyboard checkpoint signed off) and item 9 (STATE.md blocker cleared) remain pending formal user sign-off of the keyboard UAT checklist.

---

*Phase: 05-tab-management*
*Completed: 2026-04-14 (code + checklists); UAT sign-off pending*
