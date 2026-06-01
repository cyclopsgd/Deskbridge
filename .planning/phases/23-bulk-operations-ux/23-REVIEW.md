---
phase: 23-bulk-operations-ux
reviewed: 2026-06-01T00:00:00Z
depth: standard
files_reviewed: 12
files_reviewed_list:
  - src/Deskbridge/ViewModels/BulkEditViewModel.cs
  - src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs
  - src/Deskbridge/Dialogs/BulkEditDialog.xaml
  - src/Deskbridge/Dialogs/BulkEditDialog.xaml.cs
  - src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml
  - src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml.cs
  - src/Deskbridge/Views/ConnectionTreeControl.xaml
  - src/Deskbridge/Views/ConnectionTreeControl.xaml.cs
  - src/Deskbridge/App.xaml.cs
  - tests/Deskbridge.Tests/ViewModels/BulkEditViewModelTests.cs
  - tests/Deskbridge.Tests/ViewModels/ConnectAllTests.cs
  - tests/Deskbridge.Tests/ViewModels/DisconnectAllTests.cs
findings:
  critical: 0
  warning: 4
  info: 5
  total: 9
status: resolved
resolved_note: "WR-01..WR-04 + IN-03/IN-04/IN-05 fixed in commits ab92f0d, 9e6f334, 39a0ae9, 9c43475 (2026-06-01). IN-01/IN-02 no change required. Build clean, 22 bulk tests green. WR-01 veto-on-save-failure behavior to confirm in human UAT."
---

# Phase 23: Code Review Report

**Reviewed:** 2026-06-01
**Depth:** standard
**Files Reviewed:** 12
**Status:** issues_found

## Summary

Reviewed the Phase 23 bulk-operations surface: Connect All / Disconnect All / Bulk
Edit. The security and threat claims hold up well — the most important ones are
genuinely mitigated by the code:

- **T-23-04 (no Name / no password write):** verified. `BulkEditViewModel` exposes no
  Name or password field, injects no `ICredentialService`, and `ApplyToModels` writes
  only Hostname/Port/CredentialMode/Username/Domain/GroupId. `ConnectionModel` has no
  password property. The reflection assertion in `BulkEditViewModelTests` pins this.
- **T-23-03 (validation gate):** `Validate()` is pure and no-throw, rejecting
  out-of-range ports and empty hostnames on enabled fields; `OnButtonClick` blocks the
  Primary action and keeps the dialog open on failure.
- **T-23-06 / T-23-08 (atomic all-or-nothing persistence):** the apply path uses a
  single `SaveBatch` call, never a per-item loop, inside a try/catch.
- **T-23-07 (info-disclosure in logs):** the catch logs only the connection count, no
  field values or hostnames.

The findings below are correctness/robustness issues, not threat-mitigation gaps. The
most consequential is WR-01: on a SaveBatch failure the error InfoBar is told to open on
a dialog that has **already closed**, so the user gets no visible error and the
selection is left in an inconsistent state — undermining the *observable* half of the
T-23-08 all-or-nothing contract.

## Warnings

### WR-01: SaveBatch-failure InfoBar is shown on an already-closed dialog

**File:** `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs:1223-1241`
**Issue:** In `EditSelectedAsync`, `ApplyToModels` + `SaveBatch` run **after**
`await dialog.ShowAsync()` returns. A `ContentDialogResult.Primary` result means the
user's Primary click already ran `OnButtonClick` → `base.OnButtonClick(Primary)`, which
dismisses the dialog. By the time `SaveBatch` throws and `dialog.ShowSaveError(...)` sets
`SaveErrorInfoBar.IsOpen = true`, the dialog is no longer hosted/visible, so the InfoBar
never renders. The user sees the dialog vanish with no error and no saved changes
(silent failure). Additionally, `SelectedItems.Clear()` / `PrimarySelectedItem = null`
in the success branch are skipped on failure, but the dialog has still closed — so the
"keep dialog open on error" intent of T-23-08 is not actually achieved.
**Fix:** Perform the save *inside* the dialog's Primary handler so the close can be
vetoed (mirror the `Validate()` pattern in `BulkEditDialog.OnButtonClick`), or pass a
save callback the dialog invokes before calling `base.OnButtonClick`. Sketch:
```csharp
// BulkEditDialog: run the save inside the button handler and cancel close on failure
protected override void OnButtonClick(ContentDialogButton button)
{
    if (button == ContentDialogButton.Primary)
    {
        if (!_viewModel.Validate()) { /* show validation, return */ return; }
        if (_onApply is not null && !_onApply())   // returns false on SaveBatch failure
        {
            ShowSaveError(_viewModel.SelectedCount, _viewModel.SelectedCount);
            return; // do NOT call base → dialog stays open with the InfoBar visible
        }
    }
    base.OnButtonClick(button);
}
```
The VM then passes `() => { try { SaveBatch(...); ... return true; } catch { log; return false; } }`.

### WR-02: Connect All confirmation count includes already-open sessions, contradicting the projection logic

**File:** `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs:1127`,
`src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml.cs:22-24`
**Issue:** The dialog body says *"This will open {sessionCount} sessions"* using
`conns.Count` (every descendant connection). But the connect loop (lines 1143-1157)
switches to already-open tabs instead of opening them, so the number of sessions
actually *opened* is `conns.Count` minus those already open. When a group is partially
connected the warning overstates the new GDI cost. The projection math
(`ActiveCount + group.ConnectionCount`) is itself an over-estimate for the same reason,
but the user-facing sentence is the visible discrepancy.
**Fix:** Compute the count of connections that will actually be newly opened and pass
that to the dialog:
```csharp
var toOpen = conns.Count(c => !_tabHostManager.TryGetExistingTab(c.Id, out _));
var dialog = new BulkConnectConfirmDialog(host, toOpen, bulk.GdiWarningThreshold);
```
If the over-estimate is intentional (conservative GDI warning), at minimum reword the
body so it doesn't claim it "will open" a count that includes already-open sessions.

### WR-03: `ConnectAllAsync` re-reads settings on every invocation off the UI dispatcher without cancellation

**File:** `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs:1107`
**Issue:** `await _windowState.LoadAsync()` runs before the `_isDialogOpen` guard is set
for the connect path. `ConnectAllAsync` checks `_isDialogOpen` at entry (line 1099) but
does not set it until line 1121 (inside the warn branch). Two rapid Connect All invocations
on different groups can both pass the entry guard and both proceed to publish
`ConnectionRequestedEvent`s concurrently, doubling the projected GDI load the warning was
meant to bound. The `DisconnectAllAsync` path (line 1165) has no `_isDialogOpen` guard at
all, which is acceptable since it only closes tabs, but Connect All can open them.
**Fix:** Set the re-entrancy guard for the whole connect operation, not just the
confirmation sub-block:
```csharp
if (group is null || _isDialogOpen) return;
_isDialogOpen = true;
try
{
    // ... projection, optional confirm (drop the inner _isDialogOpen set/reset), connect loop
}
finally { _isDialogOpen = false; }
```
Note the confirm branch currently sets `_isDialogOpen = false` in its own `finally`
(line 1137) *before* the connect loop runs, so even the existing partial guard is
released too early.

### WR-04: `BulkConnectConfirmDialog` duplicates the warning text in two places that can drift

**File:** `src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml:33-46`,
`src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml.cs:22-24`
**Issue:** The performance-warning phrasing appears twice with *different* wording: the
code-behind `BodyText` says *"{threshold}+ active sessions may degrade performance"* while
the InfoBar `Message` says *"Opening this many sessions may degrade performance."* Both
render simultaneously in the same dialog, producing redundant, slightly inconsistent copy.
The XAML comment claims the InfoBar "carries the canonical phrasing verbatim" but the body
text uses a different sentence.
**Fix:** Show the dynamic sentence (count + threshold) in one place only. Either drop the
static InfoBar `Message` and keep the parameterized `BodyText`, or move the full sentence
into the InfoBar and remove `BodyText`. Avoid two visible copies of the same warning.

## Info

### IN-01: Divergent Port field renders an empty diff even when one model has the default port 0

**File:** `src/Deskbridge/ViewModels/BulkEditViewModel.cs:89-93`
**Issue:** The Port field projects a shared value of `0` to `string.Empty`. That is fine
for the shared case, but the `blankValue` for the divergent case is also `string.Empty`,
so a divergent Port and an all-zero shared Port both present an empty textbox with no
placeholder distinction (shared-0 has empty placeholder; divergent has "Multiple values").
Minor UX ambiguity only; behavior is otherwise correct.
**Fix:** No change required; note for UX polish if blank-port semantics ever matter.

### IN-02: `BulkEditField<T>.OnValueChanged` does not re-raise `CanApply`

**File:** `src/Deskbridge/ViewModels/BulkEditViewModel.cs:41-51`
**Issue:** `CanApply` depends only on the per-field `IsEnabled` flags, which correctly
notify via `_onEnabledChanged`. Editing a field's `Value` without toggling its checkbox
does not affect `CanApply`, so this is correct by design. Flagged only to confirm the
asymmetry is intentional (it is) — no Value→CanApply coupling exists or is needed.
**Fix:** None.

### IN-03: `GroupDisplayItem.Id` is `Guid?` but populated from non-nullable group ids

**File:** `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs:1210-1212`
**Issue:** `GetAvailableGroupsForMove()` returns `(Guid Id, ...)` (non-null), wrapped into
`GroupDisplayItem(g.Id, ...)` where `Id` is `Guid?`. The Group ComboBox binds
`SelectedValuePath="Id"` against `GroupField.Value` (`Guid?`). This works, but the
available-group list never contains a "(Root)" / null entry, so the bulk-edit Group field
can only *assign* a group, never clear a connection's group back to root — unlike the
context-menu "Move to..." which has an explicit `(Root)` item.
**Fix:** If clearing-to-root via bulk edit is desired, prepend a `GroupDisplayItem(null, "(Root)", 0)`.
Otherwise document that bulk Group edit is assign-only.

### IN-04: `ShowSaveError` parameters are always equal (`edited.Count, edited.Count`)

**File:** `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs:1240`,
`src/Deskbridge/Dialogs/BulkEditDialog.xaml.cs:33-38`
**Issue:** `ShowSaveError(int failedCount, int totalCount)` is only ever called with
`(edited.Count, edited.Count)`, producing "N of N connections could not be updated."
Because `SaveBatch` is atomic, partial failure is impossible, so the two-parameter API is
vestigial. Not a bug, but the signature implies partial-failure semantics that cannot occur.
**Fix:** Collapse to `ShowSaveError(int count)` to reflect the all-or-nothing reality, or
keep both params and document why they are always equal.

### IN-05: `App.xaml.cs` Phase 23 DI comment registers dialogs that are constructed inline

**File:** `src/Deskbridge/App.xaml.cs:453-460`
**Issue:** The Phase 23 comment block describes bulk dialogs being "constructed inline" by
the VM, but the only registration that actually changed in this block is `MainWindow`
(its existing factory). `BulkEditDialog` / `BulkConnectConfirmDialog` are not registered
in DI at all (confirmed — they are `new`'d in the VM). The comment claims "these
registrations document the surface and allow the dialogs to be resolved via DI in
tests/tooling," which is misleading since no such registrations exist.
**Fix:** Either add the documented `services.AddTransient<BulkEditDialog>()` /
`BulkConnectConfirmDialog` registrations, or trim the comment to match reality (inline
construction only).

---

_Reviewed: 2026-06-01_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
