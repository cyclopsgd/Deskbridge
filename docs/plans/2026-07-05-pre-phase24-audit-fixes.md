# Pre-Phase-24 Audit Fixes — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Batches run SEQUENTIALLY in the working tree — NO git worktree isolation (worktrees fail on this Windows setup). Fan out subagents within a batch only for files that don't overlap.

**Goal:** Fix all 30 confirmed findings from `.planning/pre-phase24-audit.md` (C1-C6, A1-A6, W1-W3, U1-U15) in four sequentially committed batches, respecting the audit's fix cautions and its "Refuted — do NOT fix" list.

**Architecture:** Batch 1 restructures the RDP disconnect/dispose ownership model (see §Ownership Model below) before touching individual findings. Batch 2 makes bulk-edit persistence transactional. Batch 3 fixes airspace/DPI defects, including moving toasts into a top-level Popup HWND. Batch 4 is a UI-token/consistency pass judged against the deskbridge-design skill.

**Tech Stack:** .NET 10 / C# 14, WPF + WPF-UI 4.2, AxMSTSCLib classic interop, xUnit (`tests/Deskbridge.Tests`).

**Verification per batch:** `dotnet build Deskbridge.sln` then `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj`. Commit each batch separately, conventional message, **no Co-Authored-By**.

**Mandatory reading per batch (subagents must read before editing):**
- Batch 1: `docs/RDP-ACTIVEX-PITFALLS.md` §3/§4/§6, `docs/WINFORMS-HOST-AIRSPACE.md` (disposal + leak sections)
- Batch 3: `docs/WINFORMS-HOST-AIRSPACE.md` (full), `docs/RDP-ACTIVEX-PITFALLS.md` §1/§3
- Batch 4: `docs/WPF-UI-PITFALLS.md`, `docs/WPF-TREEVIEW-PATTERNS.md`, `.claude/skills/deskbridge-design/` (SKILL.md + README.md), `docs/DESIGN.md`

**Do NOT fix (refuted list, audit lines 271-282):** `Marshal.FinalReleaseComObject` in RdpHostControl.Dispose (sanctioned); TabHostManager.Dispose not disposing hosts (deliberate); rapid-tab-switch snapshot clear; ConnectionEditor hostname CanSave binding; panel-edge gradient borders (user-ratified POLISH-03).

---

## Batch 1 — COM lifecycle (C1-C6) — model: fable

### The disconnect/dispose ownership model (design for C1+C2, decided up front)

**Principles:**

1. **Disconnect is owned by async callers** (the disconnect pipeline or a coordinator cleanup helper) — never by `Dispose()`. `Dispose()` must never block-wait on an `async` disconnect (`GetResult()` on the dispatcher thread starves the `Task.Delay` polling continuations → deadlock; this is why the current in-Dispose `DisconnectAsync().GetAwaiter().GetResult()` being dead code (C1) must NOT be "revived" by removing the `_disposed` guard).
2. **`Dispose()` is terminal and expects `Connected == 0`.** If called while still connected, it logs an ERROR (the "logging assert" from the audit), then runs a bounded **message-pumping** safety net: set `_userInitiatedClose = true`, best-effort synchronous `_rdp.Disconnect()`, then `Dispatcher.PushFrame` with a `DispatcherTimer` (100 ms tick) that ends the frame when `IsConnected == false` or a 10 s deadline passes. Then teardown proceeds regardless. The pump keeps COM events flowing so `OnDisconnected` can actually fire (RDP-ACTIVEX-PITFALLS §3: "must pump messages").
3. **`Dispose()` never runs inside a COM event frame** (C2). Coordinator paths reached synchronously from mstscax's `OnDisconnected` defer *only* `host.Dispose()` via `_dispatcher.BeginInvoke`. Dict removal, event unsubscribe, `HostUnmounted`, overlay request all stay synchronous. Because `OnDisconnected` has already fired on these paths, `Connected == 0` when the deferred Dispose runs → the safety net in (2) stays idle → no pumping, no reentrancy. **This is the C1/C2 interaction:** C2's deferral guarantees the disposal paths that fire most often never hit C1's fallback.
4. **Dispose-while-connected call sites are fixed to disconnect first** (C1 confirmed paths):
   - `ConnectionCoordinator.OnConnectionFailed` (ConnectStage timeout → mid-handshake dispose): keep dict removal/unsubscribe/`HostUnmounted` synchronous, then replace the direct `host.Dispose()` with `_ = DisposeHostSafelyAsync(host)` — a private helper that `await host.DisconnectAsync()` (already internally bounded at 30 s) inside try/catch, then `host.Dispose()`, all on the dispatcher.
   - `TabHostManager.RunDisconnectAsync` no-model fallback (line ~247): `await host.DisconnectAsync()` in try/catch before `host.Dispose()`.
   - `OnHostCreated` duplicate-host branch: never connected, `Connected == 0` — leave as-is.
   - `ConnectionCoordinator.Dispose()` app-shutdown loop still calls `_disconnect.DisconnectAsync(...).GetResult()` — pre-existing, not an audit finding, dict is empty in practice. Out of scope; do not touch.

### Task 1.1 — C2: defer `host.Dispose()` out of the COM event frame

**Files:** Modify `src/Deskbridge.Core/Services/ConnectionCoordinator.cs` (lines ~426 and ~446).

In `OnDisconnectedAfterConnect`, both the Logoff branch and the reconnect-suppress branch: replace the synchronous `try { host.Dispose(); } catch ...` with

```csharp
_dispatcher.BeginInvoke(() =>
{
    try { host.Dispose(); }
    catch (Exception ex)
    {
        _logger.LogError(
            "Deferred host dispose threw: {ExceptionType} HResult={HResult:X8}",
            ex.GetType().Name, ex.HResult);
    }
});
```

Keep everything else in the handler synchronous and in its current order. Add a comment citing C2 + RDP-ACTIVEX-PITFALLS §3 (never destroy the OCX while its event-dispatch frame is on the native stack).

### Task 1.2 — C1: ownership model in `RdpHostControl.Dispose` + call-site fixes

**Files:** Modify `src/Deskbridge.Protocols.Rdp/RdpHostControl.cs` (Dispose, ~269-380), `src/Deskbridge.Core/Services/ConnectionCoordinator.cs` (OnConnectionFailed), `src/Deskbridge.Core/Services/TabHostManager.cs` (RunDisconnectAsync no-model branch).

In `RdpHostControl.Dispose()`:
- Remove the dead `DisconnectAsync().GetAwaiter().GetResult()` call.
- After the `AssertSta()` call, insert the safety net per Ownership Model §2:

```csharp
// Ownership model (audit C1): disconnect is owned by the pipeline. Dispose expects
// Connected==0; reaching this fallback means a caller skipped disconnect.
if (IsConnected)
{
    _logger.LogError(
        "Dispose called while still connected for {ConnectionId} — callers must disconnect first. " +
        "Entering bounded message-pumping fallback.", ConnectionId);
    _userInitiatedClose = true;
    try { _rdp!.Disconnect(); }
    catch (Exception ex) when (ex is COMException or AxHost.InvalidActiveXStateException)
    {
        _logger.LogDebug("Fallback Disconnect threw: {ExceptionType} HResult={HResult:X8}",
            ex.GetType().Name, ex.HResult);
    }
    PumpUntilDisconnected(TimeSpan.FromSeconds(10));
    if (IsConnected)
        _logger.LogWarning("Pumped-wait fallback timed out after 10s — force disposing {ConnectionId}", ConnectionId);
}
```

with

```csharp
private static void PumpUntilDisconnectedCore(Func<bool> stillConnected, TimeSpan timeout) { ... }
private void PumpUntilDisconnected(TimeSpan timeout)
{
    var frame = new DispatcherFrame();
    var deadline = DateTime.UtcNow + timeout;
    var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal,
        (_, _) => { if (!IsConnected || DateTime.UtcNow >= deadline) frame.Continue = false; },
        System.Windows.Threading.Dispatcher.CurrentDispatcher);
    timer.Start();
    try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
    finally { timer.Stop(); }
}
```

(Adjust namespaces/usings as needed; `IsConnected` is already defensively guarded.)

- `ConnectionCoordinator.OnConnectionFailed`: replace direct `host.Dispose()` with `_ = DisposeHostSafelyAsync(host);` where:

```csharp
private async Task DisposeHostSafelyAsync(IProtocolHost host)
{
    try { await host.DisconnectAsync(); }
    catch (Exception ex)
    {
        _logger.LogDebug("Disconnect before dispose threw: {ExceptionType} HResult={HResult:X8}",
            ex.GetType().Name, ex.HResult);
    }
    try { host.Dispose(); }
    catch (Exception ex)
    {
        _logger.LogDebug("Dispose after failed connect threw: {ExceptionType} HResult={HResult:X8}",
            ex.GetType().Name, ex.HResult);
    }
}
```

- `TabHostManager.RunDisconnectAsync` no-model fallback: `await host.DisconnectAsync()` (try/catch, log) before the existing `host.Dispose()`.

**Tests:** run existing suite; RdpHostControl itself has no STA test harness for the pump path — verify by inspection + existing ErrorIsolation/coordinator tests still green. Add/adjust a coordinator unit test if one asserts synchronous dispose on the logoff path (dispatcher-deferred now — tests using a test dispatcher may need to pump/flush).

### Task 1.3 — C3: disconnect polling loop hardening + stage timeout stagger

**Files:** Modify `src/Deskbridge.Protocols.Rdp/RdpHostControl.cs` (DisconnectAsync), `src/Deskbridge.Core/Pipeline/Stages/DisconnectStage.cs`.

- `DisconnectAsync`: snapshot `var rdp = _rdp;` at entry and use the local throughout; wrap every `rdp.Connected` read in the same try/catch pattern as `IsConnected` (treat `InvalidActiveXStateException`/`COMException` as "disconnected", exit loop); add `|| _disposed` to the loop exit condition.
- `DisconnectStage`: default timeout 30 s → **35 s** (stagger above the host-internal 30 s deadline so the host's own timeout wins and the polling loop completes). On the timeout branch, observe the abandoned task: `_ = disconnectTask.ContinueWith(t => _logger.LogDebug("Abandoned disconnect task faulted: {ExceptionType}", t.Exception?.GetBaseException().GetType().Name), TaskContinuationOptions.OnlyOnFaulted);`
- Check `tests/Deskbridge.Tests` for DisconnectStage timeout tests that assert 30 s and update.

### Task 1.4 — C4: password clear in `finally` + IOE in catch filter

**Files:** Modify `src/Deskbridge.Protocols.Rdp/RdpHostControl.cs` (~186-204).

Restructure the password block so `context.ResolvedPassword = null` runs in a `finally` covering the whole cast + write; add `InvalidOperationException` to the catch filter (`ex is COMException or InvalidCastException or NullReferenceException or InvalidOperationException`) so the doc §4 cast-failure IOE hits the sanitized log + `ErrorOccurred` before rethrow.

### Task 1.5 — C5: drop dead `AccessViolationException` catch arms

**Files:** Modify `src/Deskbridge.Protocols.Rdp/RdpHostControl.cs` (~362), `Prototype/RdpSmokeHost.cs` (~279, if the file exists in-repo).

Remove `AccessViolationException` from the filters (CSEs are uncatchable on .NET Core+); leave `InvalidComObjectException or COMException`. Add a one-line comment noting AVs are uncatchable and will crash the process by design.

### Task 1.6 — C6: guard the SmartSizing fallback; reset `_loginComplete`

**Files:** Modify `src/Deskbridge.Protocols.Rdp/RdpHostControl.cs` (~474-482, disconnect handlers).

Wrap `_rdp.AdvancedSettings9.SmartSizing = true;` in its own try/catch (`COMException`, `AxHost.InvalidActiveXStateException`, `NullReferenceException` → log warning, sanitized). Also set `_loginComplete = false` in `OnDisconnectedDuringConnect` and `OnDisconnectedAfterConnectHandler` so the `UpdateResolution` gate closes after disconnect.

### Batch 1 completion

`dotnet build Deskbridge.sln` → `dotnet test` → commit: `fix(rdp): restructure disconnect/dispose ownership; defer dispose out of COM event frames (C1-C6)`.

---

## Batch 2 — Correctness W1-W3 — model: opus

### Task 2.1 — W1: transactional bulk edit (clone-and-commit + store-level rollback)

**Files:** Modify `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` (~1229-1247), `src/Deskbridge/ViewModels/BulkEditViewModel.cs` (ApplyToModels), `src/Deskbridge.Core/Services/JsonConnectionStore.cs` (SaveBatch), `src/Deskbridge.Core/Models` (ConnectionModel — add clone support if absent). Test: `tests/Deskbridge.Tests` (locate existing BulkEdit / JsonConnectionStore test files).

Two layers, both required for the "No changes were saved" contract:

1. **VM layer (clone-and-commit):** `ApplyToModels` currently mutates live store references (`GetById` returns backing-list objects). In `EditSelectedAsync`'s `SaveCallback`, apply edits to **clones**: clone each selected model (add a `ConnectionModel.Clone()` — memberwise copy is sufficient if all properties are value types/strings; check for collection properties and deep-copy those), pass clones to `vm.ApplyToModels(clones)`, then `SaveBatch(clones, ...)`. On success, `SaveBatch`'s upsert-by-Id replaces the store's list entries with the clones (same semantics as existing `Save()`); on failure the live models were never touched. Fix the false comment at ~1244.
2. **Store layer (memory matches disk):** `JsonConnectionStore.SaveBatch` currently mutates `_data` BEFORE `PersistAtomically()`; if the write throws, in-memory state has the edits and the next unrelated `Save` flushes them to disk. Make SaveBatch commit-on-success: apply the upserts to *copies* of the Groups/Connections lists, persist the new state, and only then swap the lists into `_data` (or snapshot both lists at entry and restore them in a catch that rethrows). Check how `PersistAtomically` serializes (`_data` field vs parameter) and restructure accordingly.

**TDD (write these FIRST, watch them fail):**
- Store test: `SaveBatch_WhenPersistFails_LeavesInMemoryStateUnchanged` — force persist failure (e.g. store pointed at an invalid/locked path, or seam), assert `GetById` returns pre-edit values and a subsequent successful `Save` of an unrelated model does not flush the failed batch.
- VM/flow test: `BulkEdit_SaveBatchThrows_ModelsUnchanged` — stub `IConnectionStore.SaveBatch` to throw, run the save callback path, assert the models returned by `GetById` are untouched (mirror however existing bulk-edit tests drive the callback).

### Task 2.2 — W2: Connect All projection double-count

**Files:** Modify `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` (~1110-1143).

Hoist `toOpen` above the dialog branch; `projected = _tabHostManager.ActiveCount + toOpen`; if `toOpen == 0`, skip the dialog entirely (still run the switch-to loop). Update/add a unit test for the boundary (`toOpen == 0` → no dialog; open group members not double-counted).

### Task 2.3 — W3: multi-select "Edit…" CanExecute

**Files:** Modify `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` (EditSelected command), `src/Deskbridge/Views/ConnectionTreeControl.xaml` if needed.

`[RelayCommand(CanExecute = nameof(CanEditSelected))]` with `private bool CanEditSelected() => SelectedItems.OfType<ConnectionTreeItemViewModel>().Count() >= 2;` and call `EditSelectedCommand.NotifyCanExecuteChanged()` wherever selection changes (find the SelectedItems change hook — likely a CollectionChanged subscription or the multi-select behavior callback). Verify the menu item greys out (MenuItem Command binding disables automatically). Unit test: CanExecute false for 1 connection / group+connection, true for 2 connections.

### Batch 2 completion

Build → test → commit: `fix(bulk-ops): make bulk edit all-or-nothing; fix Connect All projection and Edit CanExecute (W1-W3)`.

---

## Batch 3 — Airspace / DPI (A1-A6) — model: fable

### A1 placement decision (decided, with reasoning)

**Chosen: option (b) — host toasts (and the snackbar presenter) in a separate top-level transparent Popup tracking the main window.** Option (a) is infeasible: the only regions never covered by the WFH are the 32 px title bar, 30 px tab bar, 22 px status bar, 36 px icon rail, and the *collapsible* side panel — none can hold a 3-deep toast stack, and the side panel disappears when closed (toasts would vanish in exactly the dominant runtime state again). Option (c) freezes the live session bitmap on every toast — unacceptable for an RDP manager. WINFORMS-HOST-AIRSPACE.md §Popups explicitly blesses the mechanism: WPF `Popup` creates its own top-level HWND that renders above any WindowsFormsHost. Constraint: toast visuals must be fully opaque cards (no acrylic/blur over the WFH region — doc caveat).

### Task 3.1 — A1: toast/snackbar Popup host

**Files:** Modify `src/Deskbridge/MainWindow.xaml` (~638-650), `src/Deskbridge/MainWindow.xaml.cs`, possibly `src/Deskbridge/Controls/ToastStackControl.xaml`.

- Replace the in-window `ToastStackControl` + `SnackbarPresenter` placement with a `<Popup x:Name="OverlayPopup" AllowsTransparency="True" Placement="Bottom" StaysOpen="True" Focusable="False" ...>` whose child is a transparent panel containing `SnackbarPresenter` and `ToastStackControl` (same DataContext bindings). `ContentDialogHost` stays in-window (dialog flows already use SnapshotAndHideAll).
- Position bottom-right over the viewport with the existing toast margins: compute placement against `ViewportGrid` (PlacementTarget) with `Placement="Custom"` + `CustomPopupPlacementCallback`, or Placement offsets recomputed in code-behind.
- Reposition on `LocationChanged`/`SizeChanged` (standard trick: `popup.HorizontalOffset += 1; popup.HorizontalOffset -= 1;`). Open when the window is loaded and visible; close/hide when minimized (`StateChanged`).
- Keep `IsOpen` true permanently while the window is visible (an empty toast stack renders nothing), or bind IsOpen to "has content" — implementer's choice, simplest robust wins.
- Focus: `Focusable=False` on the popup; verify toast action buttons still clickable and the popup never steals activation from the RDP session (no `WS_EX_NOACTIVATE` needed for WPF Popup default, but verify by manual reasoning; document behavior in code comment).
- Note in a comment: toasts now render above ContentDialog scrims (top-level HWND) — accepted trade-off.

### Task 3.2 — A2: Collapsed skip in WM_ENTERSIZEMOVE

**Files:** Modify `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs` (~254).

Mirror SnapshotAndHideAll's guard inside the ENTERSIZEMOVE loop: `if (host.Visibility == Visibility.Collapsed) continue;` (visibility snapshot dictionary still records all hosts — keep that above the loop).

### Task 3.3 — A4: reentrant Snapshot/Restore depth counter

**Files:** Modify `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs` (SnapshotAndHideAll/RestoreAll).

Add `private int _dialogDepth;`. `SnapshotAndHideAll`: `if (++_dialogDepth > 1) return;` before doing work. `RestoreAll`: `if (_dialogDepth == 0) { log warning; return; } if (--_dialogDepth > 0) return;` then restore. `_inDialogMode` = `_dialogDepth > 0`. Unit-testable without STA COM: add a test if AirspaceSwapper has existing tests (check `tests/`).

### Task 3.4 — A6: real dispatcher assert

**Files:** Modify `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs` (~331-337, ctor).

Capture `_dispatcher = Dispatcher.CurrentDispatcher;` in the ctor; `AssertDispatcher()` becomes instance method checking `_dispatcher.CheckAccess()` (pattern: RdpHostControl.AssertSta). Update all call sites (it's currently static).

### Task 3.5 — A3: purge stale hosts on the suppressed-unmount path

**Files:** Modify `src/Deskbridge/MainWindow.xaml.cs` (~497, OnHostMounted/OnHostUnmounted/ReconnectOverlayRequested handlers, SetActiveHostVisibility ~563-570).

- On the reconnect-overlay path (where `_suppressedHost` made HostUnmounted skip cleanup): remove the dead WFH from `HostContainer` and call `_airspace.UnregisterHost(wfh)`. Hook wherever MainWindow observes `ReconnectOverlayRequested` (read the handler to find the right seam — the host's WFH must be captured *before* the coordinator's deferred Dispose nulls `Host`; note interaction with Batch 1's deferred dispose: BeginInvoke ordering means MainWindow's synchronous handler still sees a live `Host` — verify and comment).
- In OnHostMounted (or TabClosed cleanup): before parenting a fresh WFH, purge any existing `HostContainer` children with the same `Tag` (ConnectionId), unregistering each from the AirspaceSwapper.
- Check `ConnectionCoordinator._suppressedHost` — after this fix it may become fully write-only dead state; if so remove the field (coordinate with Batch 1's edits already in the file).

### Task 3.6 — A5: DPI-change handling

**Files:** Modify `src/Deskbridge/MainWindow.xaml.cs` (~411-441 debounce area), possibly `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs`.

- Handle `Window.DpiChanged`: recompute physical pixel size + dpiPercent for the active host (reuse the existing ViewportGrid measurement + debounce used by SizeChanged — read that code first and share the code path), call `UpdateResolution` on the active host. `UpdateResolution` already falls back to SmartSizing when unsupported.
- Invalidate in-flight drag snapshots on DPI change: if `AirspaceSwapper` is mid-drag (`_inSizeMove`), clear/recapture overlay sources (add a small `InvalidateSnapshots()` method; stale-DPI bitmap otherwise renders at wrong scale until EXITSIZEMOVE).

### Batch 3 completion

Build → test → commit: `fix(airspace): popup-hosted toasts, reentrant snapshot scopes, DPI-change handling, stale-host purge (A1-A6)`.

---

## Batch 4 — UI polish (U1-U15, U16 check) — model: fable

Judge every change against `.claude/skills/deskbridge-design` + `docs/DESIGN.md`. All are XAML-only unless noted.

### Task 4.1 — U1 first (isolated one-attribute fix)

`src/Deskbridge/Views/ConnectionTreeControl.xaml` ~280-289: add `FillBehavior="Stop"` to the HoverExit storyboard (matches MainWindow's three equivalents). Optionally gate the hover trigger on `IsSelected=False` per `docs/WPF-TREEVIEW-PATTERNS.md` row-visual guidance.

### Task 4.2 — consistency pass (U2-U15) — may fan out across non-overlapping files

| ID | File | Fix |
|----|------|-----|
| U2 | `Views/ConnectionTreeControl.xaml:869-874` | Group name field → `<ui:TextBox ... FontSize="14" Margin="0,4"/>`; drop Height/Padding/manual Foreground (Pitfall §8a) |
| U3 | `Resources/CardAndPanelStyles.xaml:45-53` | ToastContainerStyle Border: `DropShadowEffect BlurRadius=8 ShadowDepth=2 Direction=270 Opacity=0.2` |
| U4 | `Dialogs/BulkConnectConfirmDialog.xaml:22-29` | Delete custom title StackPanel row; InfoBar carries icon+title+message |
| U5 | `ImportWizardDialog.xaml:20`, `CredentialPromptDialog.xaml:20`, `ChangePasswordDialog.xaml:23`, `UpdateConfirmDialog.xaml:19` | `Margin="{StaticResource DialogContentMargin}"` on root content element; retire ad-hoc 0,8 |
| U6 | `Controls/PinInputControl.xaml:24-39` | ControlTemplate.Triggers: IsKeyboardFocused → BorderBrush `AccentFillColorDefaultBrush`; optional IsMouseOver → `ControlFillColorSecondaryBrush` |
| U7 | `BulkEditDialog.xaml:23`, `GroupEditorDialog.xaml:49`, `ImportWizardDialog.xaml:23,35,58` | Standardize on CardTitleStyle for in-dialog section headers; restyle ImportWizard steps 1-3, drop trailing colons; convert BulkEdit UPPERCASE label |
| U8 | `ConnectionTreeControl.xaml:40,85,102`, `MainWindow.xaml:320`, `ImportWizardDialog.xaml:42` | "..." → "…". **CAUTION: update the `headerStr == "Move to..."` match in `ConnectionTreeControl.xaml.cs:315` in the SAME edit** |
| U9 | `MainWindow.xaml:245,287,317,338` | Add `SectionLabelMarginBetween` (0,16,0,4) token to SpacingResources; reference it |
| U10 | `SpacingResources.xaml:26-31` | Wire chrome-size literals (RowHeight/TabBarHeight/TitleBarHeight/IconRailWidth/PanelWidth/StatusBarHeight) to tokens where feasible; delete any token that can't be wired (one authority) |
| U11 | `CredentialPromptDialog.xaml:45-47` | Remove FormFieldLabelMargin from the trailing Domain input |
| U12 | `ChangePasswordDialog.xaml:40,56` | FormFieldSpacing on CurrentField/NewField PasswordBoxes; delete the two label top-margin overrides (pixel-identical) |
| U13 | `ToastStackControl.xaml:26-30` | Bind severity icon Foreground (DeskbridgeSuccess/Warning/ErrorBrush; TextFillColorSecondaryBrush for info); optional 3px severity left border per design kit |
| U14 | `ReconnectOverlay.xaml:10` | Shared `DeskbridgeScrimBrush` (#66000000) in CardAndPanelStyles; reference it |
| U15 | `ConnectionTreeControl.xaml:342-349` | `Height="28"` → `MinHeight="28"` |

**U16 (unverified):** check `PinInputControl.xaml` `FontFamily="Segoe UI"` against TypographyStyles/design skill. If the app standard is Segoe UI Variable, remove the hardcode (inherit); if PIN cells intentionally use a specific face for digits, leave with a comment. Decide from evidence, report the decision.

Note U13/U3 touch toast XAML that Task 3.1 relocated — batch 4 runs after batch 3, re-read the moved XAML first.

### Batch 4 completion

Build → test → commit: `style(ui): audit consistency pass — tokens, dialog gutters, toast elevation, tree row visuals (U1-U16)`.

---

## Final phase

1. `/code-review` on the full diff (all four commits); fix anything it confirms (amend nothing — new fixup commits per batch area).
2. superpowers:verification-before-completion — build + full test run, evidence before claims.
3. Summary to user: finding ID → commit map, plus deliberately deferred items with reasons.
