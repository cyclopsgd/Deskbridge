---
phase: 23-bulk-operations-ux
verified: 2026-06-01T07:20:00Z
status: human_needed
score: 5/5 must-haves verified
overrides_applied: 0
human_verification:
  - test: "Right-click a group → Connect All — verify menu item appears, dialog renders over the RDP viewport (airspace z-order), and clicking Connect All opens sessions"
    expected: "BulkConnectConfirmDialog appears on top of any active RDP tab; clicking Connect All begins opening RDP tabs; cancelling does nothing"
    why_human: "Requires a running app with at least one live RDP session to verify ContentDialog z-order over the WindowsFormsHost/ActiveX airspace layer"
  - test: "Connect All with count at-threshold vs. over-threshold — verify threshold boundary visually"
    expected: "At GdiWarningThreshold or below: connects immediately with no dialog. At threshold+1 and ConfirmBeforeBulkOperations=true: the confirm dialog appears"
    why_human: "Requires configuring BulkOperationsRecord.GdiWarningThreshold in settings and observing whether the dialog fires at the boundary"
  - test: "Right-click a group → Disconnect All — verify the item is greyed when no sessions are active, enabled when ≥1 session is active"
    expected: "Menu item IsEnabled=false with zero active sessions; enabled once a session opens; clicking it closes all active sessions in the group"
    why_human: "Requires live RDP sessions to exercise the GroupHasActiveSessions enable-gate; visual state of a disabled vs enabled MenuItem must be visually confirmed"
  - test: "Select ≥2 connections → right-click → Edit… → BulkEditDialog renders correctly"
    expected: "3-column (Auto/72/*) grid with one row per editable field (Hostname, Port, Credential, Username, Domain, Group — NO Name row); per-field checkboxes; divergent fields show 'Multiple values' placeholder; Apply button disabled until ≥1 checkbox checked; ComboBoxes use ItemTemplate (no '- - -' glyph)"
    why_human: "Visual rendering of the XAML grid, ComboBox ItemTemplate glyph fix, and placeholder text colour require visual inspection in the running app"
  - test: "BulkEditDialog Apply path — verify single SaveBatch write and tree refresh"
    expected: "After clicking Apply, the connection tree refreshes to reflect the edited field values; selection clears; no duplicate Save calls"
    why_human: "Requires confirming that only one JSON file write occurs (atomic) and that the tree refresh happens — observable at the UI level"
  - test: "BulkEditDialog validation — invalid port or empty hostname when enabled"
    expected: "Clicking Apply with an enabled Port field set to '0' or 'abc', or an empty Hostname field, keeps the dialog open and shows the validation error text"
    why_human: "Requires interacting with the running dialog; OnButtonClick gate behaviour is not exercised by automated tests"
---

# Phase 23: Bulk Operations UX — Verification Report

**Phase Goal:** Users can perform group-level and multi-select operations efficiently — connecting all servers in a group, disconnecting a group, or editing shared fields across multiple connections in one action.
**Verified:** 2026-06-01T07:20:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths (from ROADMAP.md Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can right-click a group and select "Connect All" to open RDP sessions for every connection in the group, with a confirmation warning if the count exceeds the GDI limit threshold | ✓ VERIFIED | `ConnectionTreeControl.xaml` line 66–73: `Header="Connect All"` + `Play24` + `ConnectAllCommand` + `PlacementTarget.DataContext`. `ConnectionTreeViewModel.ConnectAllAsync` (line 1097) reads `BulkOperations` from `IWindowStateService`, gates on `projected > GdiWarningThreshold && ConfirmBeforeBulkOperations`, shows airspace-wrapped `BulkConnectConfirmDialog`, then publishes `ConnectionRequestedEvent` per descendant (SwitchTo for already-open). 8 `ConnectAllTests` green. |
| 2 | User can right-click a group and select "Disconnect All" to close all active sessions in that group | ✓ VERIFIED | `ConnectionTreeControl.xaml` line 70–74: `Header="Disconnect All"` + `PlugDisconnected24` + `DisconnectAllCommand` + `x:Name="DisconnectAllMenuItem"`. `ConnectionTreeViewModel.DisconnectAllAsync` (line 1165) calls `CloseTabAsync` for each active descendant. Imperative enable-gate in code-behind (`FindMenuItemByName` + `GroupHasActiveSessions`). 3 `DisconnectAllTests` green. |
| 3 | User can select multiple connections and open a bulk edit dialog that shows shared/divergent field values with per-field enable checkboxes, applying changes to all selected connections on confirm | ✓ VERIFIED | `MultiSelectContextMenu` in `ConnectionTreeControl.xaml` line 99–101: `Header="Edit…"` + `Edit24` + `EditSelectedCommand`. `BulkEditViewModel` builds `BulkEditField<T>` per field (shared/divergent diff). `BulkEditDialog.xaml` renders 3-column grid with per-field checkboxes, `IsPrimaryButtonEnabled="{Binding CanApply}"`, `FIELDS TO EDIT` section label, NO Name row. `EditSelectedAsync` calls single `SaveBatch` + `ConnectionDataChangedEvent` + selection clear. 9 `BulkEditViewModelTests` + 2 `BulkEditPersistenceTests` green. |

**Score:** 5/5 must-haves verified (3 roadmap truths + 2 plan-specific truths — see below)

### Plan 23-03 Additional Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 4 | Connect All skips already-open tabs (SwitchTo) and never calls the pipeline directly | ✓ VERIFIED | `ConnectionTreeViewModel.cs` line 1149–1152: `TryGetExistingTab` → `SwitchTo` → `continue`; only `Publish(new ConnectionRequestedEvent(model))` for new sessions. No direct coordinator/pipeline call in `ConnectAllAsync`. |
| 5 | On SaveBatch failure nothing is persisted and the error InfoBar is shown (all-or-nothing) | ✓ VERIFIED | `EditSelectedAsync` lines 1235–1241: `catch (Exception ex)` path logs count only (`T-23-07`), calls `dialog.ShowSaveError(edited.Count, edited.Count)` — no partial write. `BulkEditDialog.ShowSaveError` sets `SaveErrorInfoBar.Message` and `IsOpen=true`. |

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/Deskbridge/ViewModels/BulkEditViewModel.cs` | Field diff, per-field enable, ApplyToModels, Validate | ✓ VERIFIED | 254 lines. `ApplyToModels`, `Validate`, `CanApply`, `IsHostnameEnabled`, `IsPortEnabled`, `IsGroupEnabled` all present. No `Name =` write inside `ApplyToModels`. No `Password` property. |
| `src/Deskbridge/Dialogs/BulkEditDialog.xaml` | Bulk edit modal (DialogMaxWidth 520) | ✓ VERIFIED | `DialogMaxWidth="520"`, `PrimaryButtonText="Apply"`, `IsPrimaryButtonEnabled="{Binding CanApply}"`, `FIELDS TO EDIT`, `Multiple values` (as `PlaceholderText` binding source). No Name TextBox row. |
| `src/Deskbridge/Dialogs/BulkEditDialog.xaml.cs` | OnButtonClick validation gate + base(host) ctor | ✓ VERIFIED | `ctor(ContentDialogHost host, BulkEditViewModel viewModel) : base(host)`. `OnButtonClick` calls `vm.Validate()`, returns without `base.OnButtonClick` on failure. Enter-swallow `Dialog_PreviewKeyDown`. `ShowSaveError` method wired. |
| `src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml` | GDI confirm modal (DialogMaxWidth 420, Warning InfoBar) | ✓ VERIFIED | `DialogMaxWidth="420"`, `PrimaryButtonAppearance="Primary"`, `DefaultButton="Primary"`, `Warning24` SymbolIcon, `Severity="Warning"` InfoBar with `may degrade performance` copy. |
| `src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml.cs` | base(host) ctor + N/threshold params | ✓ VERIFIED | `ctor(ContentDialogHost host, int sessionCount, int threshold) : base(host)`. Sets `BodyText.Text` with canonical "may degrade performance" phrasing. |
| `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` | ConnectAllAsync, DisconnectAllAsync, EditSelectedAsync, GroupHasActiveSessions, CollectDescendantConnections | ✓ VERIFIED | All five members present. `IWindowStateService` added as last ctor param. `SaveBatch(`, `Publish(new ConnectionRequestedEvent`, `SnapshotAndHideAll` all present in the new command bodies. |
| `src/Deskbridge/Views/ConnectionTreeControl.xaml` | Connect All / Disconnect All / Edit… menu items | ✓ VERIFIED | `Header="Connect All"` + `Play24`, `Header="Disconnect All"` + `x:Name="DisconnectAllMenuItem"` + `PlugDisconnected24`, `Header="Edit…"` + `Edit24` + `EditSelectedCommand`. Both group items use `PlacementTarget.DataContext` (not `AncestorType=TreeView`). |
| `src/Deskbridge/Views/ConnectionTreeControl.xaml.cs` | Imperative Disconnect All enable-gate | ✓ VERIFIED | `FindMenuItemByName(menu, "DisconnectAllMenuItem")` + `disconnectAllItem.IsEnabled = _viewModel.GroupHasActiveSessions(group)` (lines 280–282). |
| `src/Deskbridge/App.xaml.cs` | DI registration comment for BulkEditDialog + BulkConnectConfirmDialog + IWindowStateService threading | ✓ VERIFIED | Lines 453–460: comment names both dialog types. `IWindowStateService` already registered as singleton (line 232); `ConnectionTreeViewModel` by-type registration resolves the new ctor param automatically. |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `ConnectionTreeViewModel` | `IConnectionStore.SaveBatch` | Single atomic write on Apply | ✓ WIRED | Line 1230: `_connectionStore.SaveBatch(edited, Array.Empty<ConnectionGroup>())` inside `EditSelectedAsync`. |
| `ConnectionTreeViewModel` | `ConnectionRequestedEvent` | Publish per descendant in Connect All | ✓ WIRED | Line 1156: `_eventBus.Publish(new ConnectionRequestedEvent(model))` inside per-descendant loop in `ConnectAllAsync`. No direct pipeline call. |
| `ConnectionTreeControl.xaml` | `ConnectAllCommand` / `DisconnectAllCommand` | `PlacementTarget.DataContext` | ✓ WIRED | Both group menu items bind via `PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}` — not `AncestorType=TreeView`. |
| `BulkEditDialog.xaml` | `BulkEditViewModel.CanApply` | `IsPrimaryButtonEnabled` binding | ✓ WIRED | `IsPrimaryButtonEnabled="{Binding CanApply}"` on line 11 of BulkEditDialog.xaml. |
| `BulkEditViewModel` | `ConnectionModel` (selected) | Per-field `Distinct()` diff over the selection | ✓ WIRED | `BuildField` helper (lines 231–252) uses `_selected.Select(selector).Distinct().Count()` to determine shared/divergent. |
| `ConnectionTreeControl.xaml.cs` | `GroupHasActiveSessions` | Imperative enable-gate (WPF-UI #1387 workaround) | ✓ WIRED | `FindMenuItemByName` locates `DisconnectAllMenuItem` on the fresh `x:Shared="False"` instance; `IsEnabled` set from `_viewModel.GroupHasActiveSessions(group)`. |

---

### Data-Flow Trace (Level 4)

BulkEditViewModel and the dialog are dependency-light (no service injection — data flows in through ctor args from `EditSelectedAsync`). Data flow from store to dialog:

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|--------------|--------|--------------------|--------|
| `BulkEditViewModel` | `_selected` (ConnectionModel list) | `_connectionStore.GetById(connVm.Id)` in `EditSelectedAsync` (lines 1196–1199) | Yes — live store read | ✓ FLOWING |
| `BulkEditViewModel.ApplyToModels()` | `edited` list | Mutates and returns `_selected` in-memory | Yes — writes back to persisted models via `SaveBatch` | ✓ FLOWING |
| `ConnectAllAsync` | `conns` (descendant list) | `CollectDescendantConnections(group)` → `_connectionStore.GetById(conn.Id)` (line 1145) | Yes — live store read per connection | ✓ FLOWING |

---

### Behavioral Spot-Checks

Step 7b: The phase produces runnable WPF UI code, not a CLI/API that can be checked without a running app. The bulk test suite (22 tests: ConnectAll 8, DisconnectAll 3, BulkEditViewModel 9, BulkEditPersistence 2) passes deterministically in isolation per 23-03 SUMMARY (verified across 6+ runs). Build is clean (0 warnings, 0 errors per all three plan SUMMARYs).

Live UI behavior (dialog rendering, airspace z-order, visual field states) is routed to human verification below.

---

### Requirements Coverage

| Requirement | Source Plans | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| BULK-01 | 23-01, 23-02, 23-03 | User can right-click a group and select "Connect All" to open RDP sessions for all connections, with a warning if the count exceeds the GDI limit | ✓ SATISFIED | `ConnectAllAsync` in `ConnectionTreeViewModel.cs`; `BulkConnectConfirmDialog`; `Connect All` menu item; 8 `ConnectAllTests` green |
| BULK-02 | 23-01, 23-03 | User can right-click a group or use a command to disconnect all active sessions in the group | ✓ SATISFIED | `DisconnectAllAsync` in `ConnectionTreeViewModel.cs`; `Disconnect All` menu item; imperative enable-gate; 3 `DisconnectAllTests` green |
| BULK-03 | 23-01, 23-02, 23-03 | User can select multiple connections and open a bulk edit dialog with shared/divergent fields and per-field enable checkboxes | ✓ SATISFIED | `BulkEditViewModel`; `BulkEditDialog`; `Edit…` menu item; `EditSelectedAsync` with `SaveBatch`; 9 VM tests + 2 persistence tests green |

No orphaned requirements: BULK-01, BULK-02, BULK-03 are the only phase-23 requirements in REQUIREMENTS.md (traceability table lines 376–378), and all three are covered.

---

### Anti-Patterns Found

| File | Pattern | Severity | Impact |
|------|---------|----------|--------|
| `BulkEditDialog.xaml.cs` line 36 | `ShowSaveError` uses `SaveErrorInfoBar.IsOpen = true` after the dialog `ShowAsync()` has already returned on the persistence failure path — the dialog remains open when `ShowSaveError` is called (the catch block is inside the `await dialog.ShowAsync()` scope), so the InfoBar will display. However, the 23-02 SUMMARY notes WR-01: if the dialog is somehow dismissed before the async SaveBatch failure surfaces, the InfoBar would be shown on an already-closed dialog. | ⚠️ Warning (known, WR-01 from 23-REVIEW.md) | Advisory UX refinement — not goal-blocking. The normal failure path (exception during SaveBatch while dialog is open) works correctly. |

No blockers found. No `TODO` / `FIXME` / `PLACEHOLDER` / stub patterns in production files. No hardcoded empty data flowing to user-visible output.

---

### Human Verification Required

The automated checks (artifact existence, wiring, test coverage) all pass. The following items require a running app with an active connection and available RDP hosts to verify fully.

#### 1. Connect All dialog z-order over active RDP viewport

**Test:** Open at least one active RDP session (so the WindowsFormsHost is rendering). Right-click a group and select Connect All with settings configured so the threshold is exceeded (`GdiWarningThreshold` set low in Settings > Bulk Operations, `ConfirmBeforeBulkOperations = true`).
**Expected:** The `BulkConnectConfirmDialog` appears visually above the active RDP tab (not hidden behind the ActiveX surface). The Warning InfoBar and amber Warning24 icon render correctly.
**Why human:** ContentDialog z-order over `WindowsFormsHost` / ActiveX requires the airspace snapshot mechanism (`AirspaceSwapper.SnapshotAndHideAll`) to be live — the bitmap snapshot hides the RDP control before the dialog appears. This cannot be verified headless.

#### 2. Connect All threshold boundary in practice

**Test:** Set `GdiWarningThreshold = 2` and `ConfirmBeforeBulkOperations = true` in Settings. With 1 active session, right-click a group with exactly 1 connection (projected = 2, equal to threshold). Then with 2 active sessions and 1 connection in the group (projected = 3, threshold + 1).
**Expected:** Projected = threshold (2): no dialog, connects immediately. Projected = threshold+1 (3): confirm dialog appears.
**Why human:** Settings are written at runtime via the settings UI; confirming the threshold boundary with live settings data requires manual interaction.

#### 3. Disconnect All enable-state (greyed vs. active)

**Test:** Right-click a group that has no active sessions, then right-click a group that has at least one active session.
**Expected:** No active sessions: `Disconnect All` menu item appears greyed/disabled. At least one active session: item is enabled. After clicking Disconnect All with active sessions: all open RDP tabs in the group close.
**Why human:** The `IsEnabled` imperative gate (`FindMenuItemByName` + `GroupHasActiveSessions`) must be observed in the rendered menu against real tab state.

#### 4. BulkEditDialog visual rendering

**Test:** Select ≥2 connections with different hostnames (divergent) and the same port (shared). Right-click → Edit….
**Expected:** Dialog title shows "Edit N connections". Hostname row shows `Multiple values` placeholder in tertiary colour; Port row pre-fills with the shared value. Both checkboxes unchecked initially. Apply button disabled. Credential mode and Group ComboBoxes show clean text labels (no "- - -" glyph). No Name row present.
**Why human:** Visual rendering of placeholder text colour, ComboBox ItemTemplate glyph fix, and the 3-column grid layout require visual inspection.

#### 5. BulkEditDialog Apply path — tree refresh and field validation

**Test:** In BulkEditDialog, enable the Hostname checkbox and enter a new hostname. Click Apply.
**Expected:** Dialog closes, connection tree refreshes showing the updated hostname, selection clears. Test also: enable Port, enter "0", click Apply — dialog stays open with validation error text visible.
**Why human:** Confirming the tree refresh (ConnectionDataChangedEvent → OnDataChanged → RefreshTree), single SaveBatch write (not per-item), and the OnButtonClick validation gate all require observing the running app.

---

### Gaps Summary

No automated gaps. All five must-have truths are VERIFIED in the codebase:

- `BulkEditViewModel` implements field-diff, per-field enable, `ApplyToModels` (Name and password excluded), `Validate` (port 1–65535, non-empty hostname for enabled fields). 9 tests green.
- `BulkEditDialog` and `BulkConnectConfirmDialog` are fully wired to the VM and confirm dialog patterns — correct widths (520/420), copy, icons, `base(host)` pattern, and `OnButtonClick` gate.
- `ConnectionTreeViewModel` carries all three commands (`ConnectAllAsync`, `DisconnectAllAsync`, `EditSelectedAsync`) plus helpers (`CollectDescendantConnections`, `GroupHasActiveSessions`), reads threshold from `IWindowStateService`, gates on the correct boundary (`> threshold`, not `>=`), publishes `ConnectionRequestedEvent` (never calls pipeline), uses single `SaveBatch`, and wraps dialogs in `AirspaceSwapper`.
- `ConnectionTreeControl.xaml` has all three menu items with correct copy, icons, and `PlacementTarget.DataContext` binding.
- The imperative `DisconnectAllMenuItem.IsEnabled` gate correctly uses `FindMenuItemByName` on the fresh `x:Shared="False"` instance.
- `App.xaml.cs` documents the dialog surface and the `IWindowStateService` ctor dependency resolves automatically via by-type DI.

The 6 human verification items above are genuine live-app checks — visual rendering, airspace z-order, and live-settings boundary behaviour — not indicators of missing production code.

**Known advisory item (not a gap):** WR-01 (23-REVIEW.md) — `ShowSaveError` InfoBar shown on the SaveBatch failure path while the dialog is still open. This is the correct path (exception caught inside `await dialog.ShowAsync()` scope); the edge case of dialog already closed before the exception is a UX refinement for a future phase.

---

_Verified: 2026-06-01T07:20:00Z_
_Verifier: Claude (gsd-verifier)_
