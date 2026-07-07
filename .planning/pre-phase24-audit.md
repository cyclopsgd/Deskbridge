# Pre-Phase-24 Audit — Confirmed Findings (2026-07-04)

> **STATUS: RESOLVED (2026-07-07).** All 30 findings fixed across four sequentially
> reviewed batches. Each batch was spec-reviewed and quality-reviewed before commit; a
> multi-agent `/code-review` over the full diff surfaced 5 follow-up findings, all fixed.
> Plan: `docs/plans/2026-07-05-pre-phase24-audit-fixes.md`. See the resolution map at the
> bottom of this file.

Read-only audit, four lanes, every finding adversarially verified by an independent
refuter agent before inclusion. 36 raised → 30 confirmed → listed here. Severities are
the *verified* severities (several were revised down from the original claim).

Fix order recommendation: C1+C2 together → W1 → A1 → A2/A3/A4/A5 → U1 → remaining U batch.
C1 and C2 interact — a naive fix to one worsens the other (see notes).

---

## Lane C — COM/ActiveX lifecycle

### C1 (HIGH) — Disconnect-before-dispose safety net is dead code
`src/Deskbridge.Protocols.Rdp/RdpHostControl.cs:272`
`Dispose()` sets `_disposed = true` (line 272) before calling
`DisconnectAsync().GetAwaiter().GetResult()` (line 280), but `DisconnectAsync` starts with
`if (_disposed || _rdp is null) return;` (line 236) — the in-Dispose disconnect is always a
no-op. Confirmed dispose-while-connected paths: ConnectStage 30s timeout →
ConnectionFailedEvent → `ConnectionCoordinator.OnConnectionFailed` (line 335) disposes the
host mid-handshake with no Disconnect ever issued (doc §3 forbidden sequence; can hang the
STA/UI thread). Also TabHostManager.RunDisconnectAsync no-model fallback (TabHostManager.cs:247).
**Fix caution:** do NOT just remove the `_disposed` guard — `GetResult()` on the dispatcher
thread blocks the queue, so the wait loop's `Task.Delay` continuations would never run and
Dispose-while-connected would deadlock instead. Either make the pipeline the sole owner of
disconnect (Dispose hard-requires Connected==0, with logging assert), or use a
message-pumping wait (DispatcherFrame) for the in-Dispose fallback. Consider extracting a
`DisconnectCoreAsync` not gated on `_disposed`.

### C2 (HIGH) — Host disposed synchronously inside the ActiveX control's own OnDisconnected event
`src/Deskbridge.Core/Services/ConnectionCoordinator.cs:446` (also :426 Logoff branch)
Chain is fully synchronous on the STA thread: mstscax OnDisconnected → AxHost sink →
`RdpHostControl.cs:423` `DisconnectedAfterConnect?.Invoke` → coordinator handler
(CheckAccess()==true so no deferral) → `host.Dispose()` — unparents the control, destroys
its HWND, `Marshal.FinalReleaseComObject` on the OCX, disposes AxHost/WFH, all while the
mstscax event-dispatch frame is on the native stack. Use-after-free/AV pattern; the
`AccessViolationException` catch at RdpHostControl.cs:361 is inert on .NET 10 (CSEs
uncatchable) so a hit is a process crash.
**Fix:** defer only `host.Dispose()` (lines 426 and 446) out of the event frame via
`_dispatcher.BeginInvoke`/`InvokeAsync`. Dict removal, overlay request, unsubscribe can stay
synchronous.

### C3 (LOW) — DisconnectStage timeout abandons polling task → unobserved NRE on nulled `_rdp`
`src/Deskbridge.Protocols.Rdp/RdpHostControl.cs:256` + `DisconnectStage.cs:32-41`
Stage's 30s cts starts before the host's internal 30s deadline, so on a hung disconnect the
stage timeout always wins; Dispose then nulls `_rdp` (line 369) and the abandoned loop
dereferences `_rdp.Connected` → NRE in an unobserved task (CrashHandler observes it; impact
is a misleading error log). Also `Connected` reads at 245/256/262 are unguarded unlike
`IsConnected` (71-75).
**Fix:** snapshot `_rdp` to a local, guard Connected reads like IsConnected, exit loop when
`_disposed`; in DisconnectStage observe or cancel the abandoned task; stagger stage timeout
above the host-internal 30s.

### C4 (LOW) — IMsTscNonScriptable cast failure bypasses sanitized catch; password not cleared on throw paths
`src/Deskbridge.Protocols.Rdp/RdpHostControl.cs:186-204`
`as IMsTscNonScriptable ?? throw new InvalidOperationException(...)` — IOE is not in the
`COMException or InvalidCastException or NullReferenceException` filter (ICE/NRE arms are
dead code), so all doc §4 cast-failure modes skip the ErrorOccurred notification and the
sanitized log. `context.ResolvedPassword = null` (line 194) runs only on success. (Verified:
no actual credential leak — coordinator catch-all still sanitizes; context doesn't reach the
bus.)
**Fix:** clear ResolvedPassword in a `finally` covering the whole password write; add IOE to
the filter (or throw InvalidCastException).

### C5 (LOW) — Dead `AccessViolationException` arm in dispose catch filter
`src/Deskbridge.Protocols.Rdp/RdpHostControl.cs:362`
CSEs are uncatchable on .NET Core+; the arm is unreachable and gives false confidence.
**Fix:** drop it (also in Prototype/RdpSmokeHost.cs:279 and the doc sample when next revised)
or comment as aspirational-only.

### C6 (LOW) — SmartSizing fallback is an unguarded COM call inside a catch block
`src/Deskbridge.Protocols.Rdp/RdpHostControl.cs:474-482`
`_rdp.AdvancedSettings9.SmartSizing = true;` inside `catch (COMException)` can itself throw
(sibling catches don't apply); escapes to OnResizeSettled (no try/catch) → CrashDialog.
`_loginComplete` is never reset on disconnect so the line-457 guard doesn't close the window.
**Fix:** wrap the fallback in its own try/catch matching the IsConnected defensive style.

---

## Lane A — Airspace / WinForms host / DPI

### A1 (HIGH) — Toasts and GDI-limit snackbar invisible behind the live RDP viewport
`src/Deskbridge/MainWindow.xaml:642-650` + `Controls/ToastStackControl.xaml:6-8`
ToastStackControl + SnackbarPresenter are in-window WPF visuals spanning rows 0-2, anchored
bottom-right over the WFH region. No SnapshotAndHideAll on any toast/snackbar path (only
ContentDialog flows). Background-tab failure toasts, reconnect/update/import toasts, and the
15-session GDI snackbar (fires exactly when the 15th WFH becomes visible) are occluded in
the app's dominant runtime state → NOTF-01..03 silently non-functional.
**Fix options:** (a) constrain toasts to regions never covered by the WFH (side panel column
/ status bar), (b) host toasts in a separate top-level transparent popup window tracking the
main window (Airhack pattern, doc §), (c) snapshot-swap active WFH while a toast shows.
Decide placement before implementing.

### A2 (MEDIUM) — WM_ENTERSIZEMOVE captures collapsed background hosts into the shared snapshot
`src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs:246-271`
Loop lacks the `Visibility.Collapsed` skip that SnapshotAndHideAll has (119-120); every host
overwrites the single shared ViewportSnapshot Image, last-enumerated wins → drag-freeze
frame can show a background session. Self-corrects on release.
**Fix:** mirror the Collapsed guard in the WM_ENTERSIZEMOVE branch.

### A3 (MEDIUM) — Session-drop path leaks AirspaceSwapper registrations; breaks single-visible-WFH invariant
`src/Deskbridge/MainWindow.xaml.cs:497` + `ConnectionCoordinator.cs:437-452`
Suppressed-HostUnmounted path (`_suppressedHost` is write-only) means UnregisterHost never
runs for a dropped host: one stale `_hosts` entry leaks per drop (iterated every drag), and
after successful reconnect the disposed orphan WFH coexists in HostContainer with the fresh
one — SetActiveHostVisibility (563-570) sets ALL Tag matches Visible. (Refuted sub-claim:
failed attempts do NOT accumulate — TabHostManager publishes TabClosedEvent which cleans
HostContainer. Unmanaged resources already freed; leak is managed graph + invariant break.)
**Fix:** on the suppressed path (or ReconnectOverlayRequested) remove the dead WFH from
HostContainer and call UnregisterHost; have re-mount/TabClosed paths purge pre-existing
same-Tag WFHs.

### A4 (MEDIUM) — SnapshotAndHideAll/RestoreAll not reentrant — nested dialog scopes expose all background sessions
`src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs:113, 142-162`
Nested SnapshotAndHideAll overwrites `_preDialogVisibility` with the all-Collapsed state;
inner RestoreAll nulls it; outer RestoreAll's null-fallback sets EVERY host Visible (stacked
live RDP surfaces until next tab switch). Overlap realistic: async update-confirm dialog,
credential prompt during reconnect vs any open dialog — 13 call sites, no shared gate.
**Fix:** depth counter — snapshot on 0→1 transition only, restore on 1→0; inner scopes no-op.

### A5 (MEDIUM) — No DPI-change handling; cross-monitor drag leaves stale resolution with SmartSizing off
`src/Deskbridge/MainWindow.xaml.cs:411-441` + `RdpConnectionConfigurator.cs:61-63`
PerMonitorV2 manifest; zero DpiChanged/WM_DPICHANGED handlers in src; only trigger is
ViewportGrid.SizeChanged (doesn't reliably fire on pure cross-monitor drag since logical size
stays constant); SmartSizing=false on the viewport-matched path so no bitmap fallback.
Doc mandates a manual DPI handler.
**Fix:** handle Window.DpiChanged (or WM_DPICHANGED in existing WndProc): recompute physical
pixels + dpiPercent, call UpdateResolution on the active host (reuse debounce), fall back to
SmartSizing=true when dynamic resize unsupported; invalidate in-flight drag snapshots on DPI
change.

### A6 (LOW) — AssertDispatcher is a tautology
`src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs:331-337`
`Dispatcher.CurrentDispatcher.CheckAccess()` is always true — dead guard on all 8 public
members. Most misuse still trips WPF VerifyAccess; UnregisterHost would silently corrupt
`_hosts` cross-thread.
**Fix:** capture the UI dispatcher in the ctor and assert against it (see
RdpHostControl.AssertSta pattern).

---

## Lane W — Correctness / wiring (Phase 23 bulk ops)

### W1 (HIGH) — Bulk-edit failure violates the all-or-nothing "No changes were saved" contract
`src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs:1231` + `BulkEditViewModel.ApplyToModels:205` + `JsonConnectionStore.GetById:76`
ApplyToModels mutates LIVE store objects (GetById returns backing-list references, no clone)
BEFORE SaveBatch. On persistence failure: dialog says "No changes were saved", but in-memory
models are mutated, no rollback exists (comment at 1244 falsely claims "nothing persisted"),
and the next unrelated Save serializes the whole list → silently flushes the failed edit to
disk. Violates T-23-08.
**Fix:** apply edits to clones and commit into the store only on successful SaveBatch; or
snapshot original field values and restore in the catch. In-memory state must match the
"nothing saved" message after failure.

### W2 (LOW) — Connect All GDI projection double-counts open sessions ("This will open 0 sessions")
`src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs:1111, 1117, 1128`
`projected = ActiveCount + group.ConnectionCount` counts open group members twice; dialog
headline uses `toOpen`. Fails safe (over-warns) but can show a nonsensical zero count.
**Fix:** compute `toOpen` first, `projected = ActiveCount + toOpen`, skip dialog when
`toOpen == 0`.

### W3 (LOW) — Multi-select "Edit…" enabled for <2-connection selections, silently no-ops
`src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs:1189-1207` + `ConnectionTreeControl.xaml:99-101`
No CanExecute; selecting two groups (or group+connection) shows an enabled item that does
nothing.
**Fix:** CanExecute predicate (`SelectedItems.OfType<ConnectionTreeItemViewModel>().Count() >= 2`)
or a snackbar explaining the requirement.

---

## Lane U — UI design + spacing

### U1 (HIGH) — Selected tree row loses accent background after hover exit
`src/Deskbridge/Views/ConnectionTreeControl.xaml:280-289`
HoverExit storyboard lacks `FillBehavior="Stop"` (MainWindow's three equivalents all have
it); held Transparent permanently masks the SelectEnter accent fill. Reproduces on nearly
every selection; only the 2px stripe survives.
**Fix:** add `FillBehavior="Stop"`; optionally gate the hover trigger on IsSelected=False.

### U2 (MEDIUM) — Group name field: stock TextBox with Height="24" (Pitfall §8a) and inconsistent with sibling card
`src/Deskbridge/Views/ConnectionTreeControl.xaml:869-874`
Below the ~28px restyled-template minimum → clipping; Name row jumps height/chrome between
group and connection selection.
**Fix:** replace with `<ui:TextBox ... FontSize="14" Margin="0,4"/>` matching the connection
card; drop Height, Padding, manual Foreground.

### U3 (MEDIUM) — Custom toasts lost the design-mandated drop shadow
`src/Deskbridge/Resources/CardAndPanelStyles.xaml:45-53`
Design system's single elevation rule (toasts+dialogs get shadows) lost in Phase 6 Snackbar→
ToastStackControl migration; zero DropShadowEffect in src.
**Fix:** add `DropShadowEffect BlurRadius=8 ShadowDepth=2 Direction=270 Opacity=0.2` to
ToastContainerStyle (on the Border, not a text panel).

### U4 (MEDIUM) — BulkConnectConfirmDialog duplicates "Many active sessions" heading + warning icon
`src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml:22-29 vs 37-41`
Custom title row (off-ramp 20px Warning24 glyph) duplicates the InfoBar's identical title and
built-in icon.
**Fix:** delete the custom StackPanel row; let the InfoBar carry icon+title+message.

### U5 (MEDIUM) — Dialog content gutters: three conventions across eleven dialogs
`ImportWizardDialog.xaml:20` (none) / `CredentialPromptDialog.xaml:20`,
`ChangePasswordDialog.xaml:23`, `UpdateConfirmDialog.xaml:19` (hardcoded 0,8) vs
`DialogContentMargin` token (16,12) used by ConnectionEditor/BulkEdit/GroupEditor/BulkConnectConfirm.
**Fix:** DialogContentMargin on the root content element of every ContentDialog; retire the
ad-hoc 0,8 margins.

### U6 (MEDIUM) — PIN cells have no keyboard-focus visual
`src/Deskbridge/Controls/PinInputControl.xaml:24-39`
Full template replacement, zero triggers; only the caret indicates the focused cell in the
lock-screen flow.
**Fix:** ControlTemplate.Triggers — IsKeyboardFocused → BorderBrush AccentFillColorDefaultBrush
(or 2px bottom accent underline); optional IsMouseOver → ControlFillColorSecondaryBrush.

### U7 (MEDIUM) — In-dialog section headers use three treatments
`BulkEditDialog.xaml:23` (SectionLabel UPPERCASE) / `GroupEditorDialog.xaml:49` (CardTitle) /
`ImportWizardDialog.xaml:23,35,58` (inline SemiBold + trailing colons; bypasses text-scaling;
step 4 of the same wizard uses CardTitle).
**Fix:** standardize on CardTitleStyle for in-dialog section headers; restyle ImportWizard
steps 1-3, drop trailing colons; decide UPPERCASE convention for BulkEdit or convert.

### U8 (LOW) — ASCII "..." vs mandated "…" in five labels
`ConnectionTreeControl.xaml:40,85,102` ("Move to..."), `MainWindow.xaml:320`
("Import Connections..."), `ImportWizardDialog.xaml:42` ("Browse...").
**CAUTION:** `ConnectionTreeControl.xaml.cs:315` matches `headerStr == "Move to..."` exactly —
update in the same change or the Move-to submenu silently breaks.

### U9 (LOW) — Settings section labels hardcode 0,16,0,4 (four of five bypass the token)
`MainWindow.xaml:245,287,317,338` vs `:225` (token).
**Fix:** add `SectionLabelMarginBetween` (0,16,0,4) to SpacingResources and reference it.

### U10 (LOW) — Six chrome-size tokens defined, zero consumers
`SpacingResources.xaml:26-31` (RowHeight/TabBarHeight/TitleBarHeight/IconRailWidth/PanelWidth/
StatusBarHeight) — all chrome sizes are duplicated literals in MainWindow/ConnectionTreeControl.
**Fix:** wire the literals to the tokens where feasible, or delete the token block so there is
one authority.

### U11 (LOW) — CredentialPromptDialog Domain field misuses label-margin token on an input
`CredentialPromptDialog.xaml:45-47` — trailing field carries FormFieldLabelMargin (0,0,0,4);
sibling dialogs' trailing fields carry none.
**Fix:** remove the margin.

### U12 (LOW) — ChangePasswordDialog spaces fields via label top-margin overrides
`ChangePasswordDialog.xaml:40,56` — Margin="0,12,0,4" overrides FieldLabelStyle's token;
app-wide pattern is FormFieldSpacing (0,0,0,12) on the preceding input.
**Fix:** add FormFieldSpacing to CurrentField/NewField PasswordBoxes, delete the two label
overrides (pixel-identical result).

### U13 (LOW) — Toast severity icons render uncolored
`ToastStackControl.xaml:26-30` — Symbol bound, Foreground never set; ToastItemViewModel
already carries Appearance (Info/Caution/Danger) but the template drops it. Design kit also
shows a 3px severity-colored left border.
**Fix:** bind icon Foreground to severity brushes (DeskbridgeSuccess/Warning/ErrorBrush,
TextFillColorSecondaryBrush for info); optionally the left border accent.

### U14 (LOW) — ReconnectOverlay scrim hardcodes #99000000 (60%) vs documented 40% dim
`ReconnectOverlay.xaml:10`.
**Fix:** shared `DeskbridgeScrimBrush` (#66000000) in CardAndPanelStyles, reference here.

### U15 (LOW) — Tree filter box Height="28" should be MinHeight (Pitfall §8a)
`ConnectionTreeControl.xaml:342-349`.
**Fix:** `MinHeight="28"`.

---

## Unverified (verifier hit session limit — check before fixing)

- **U16?** PinInputControl hardcodes `FontFamily="Segoe UI"` instead of inheriting Segoe UI
  Variable (`PinInputControl.xaml`). Plausible; confirm against TypographyStyles/design skill
  before changing.

## Refuted (do NOT "fix" these)

- Marshal.FinalReleaseComObject in RdpHostControl.Dispose — sanctioned by
  WINFORMS-HOST-AIRSPACE.md + TECHNICAL-DEEP-DIVE.md §5.3 (ban targets per-object
  ReleaseComObject). Optional: reconcile RDP-ACTIVEX-PITFALLS §3 wording.
- TabHostManager.Dispose not disposing hosts — ConnectionCoordinator.Dispose D-08 backstop
  covers it; ownership is deliberate (D-04/D-10).
- Rapid-tab-switch premature snapshot clear — dispatcher priority ordering makes it unreachable.
- ConnectionEditor hostname error binding to CanSave — exactly equivalent as coded (latent
  drift note only).
- Panel-edge gradient borders — POLISH-03 requirement, user-ratified (commit fc75d3c).
  Optional: update deskbridge-design README/SKILL to bless the gradient edges.

---

## Resolution map (2026-07-07)

Base `063eb28`. Each batch: implement → independent spec review → code-quality review →
fixups → build (0 warnings) + tests → commit. Then multi-agent `/code-review` over the full
diff → 5 verified follow-up findings → fixed in the review commit.

| Findings | Commit | Message |
| --- | --- | --- |
| C1–C6 (COM lifecycle) | `de1d91e` | `fix(rdp): restructure disconnect/dispose ownership; defer dispose out of COM event frames` |
| W1–W3 (bulk-ops correctness) | `d4239c1` | `fix(bulk-ops): make bulk edit all-or-nothing; fix Connect All projection and Edit CanExecute` |
| A1–A6 (airspace/DPI) | `898f480` | `fix(airspace): popup-hosted toasts, reentrant snapshot scopes, DPI-change handling, stale-host purge` |
| U1–U16 (UI polish) | `0885f67` | `style(ui): audit consistency pass — tokens, dialog gutters, toast elevation, tree row visuals` |
| Code-review follow-ups | `4085ace` | `fix(review): sanitize DPI-change COM log, dialog-scope host invariant, dispose hardening` |

Plan doc committed at `457cc08`.

**A1 placement decision:** toasts hosted in a top-level `Popup` HWND (option b), not
region-constrained (option a) — no uncovered window region can hold the toast stack, and the
side panel collapses in the app's dominant state. Doc-sanctioned per WINFORMS-HOST-AIRSPACE §Popups.

**U16 (was unverified):** the `FontFamily="Segoe UI"` hardcode in PinInputControl was the app's
only font-family declaration and redundant (TypographyStyles sets no face; WPF-UI default is
already Segoe UI) → removed to inherit.

### Deliberately deferred (not fixed; recorded so they aren't lost)

- `PanelHeaderStyle` in `CardAndPanelStyles.xaml` — an unused style with a hardcoded `Height=28`
  duplicating the new `RowHeight` token. Correct disposition is deletion, which is outside the
  UI-consistency lane's scope. Follow-up: delete it.
- `HeaderHeight = 28` C# constant in `ConnectionTreeControl.xaml.cs` mirrors the `RowHeight` XAML
  token. A C# consumer reading a XAML resource is contortion; left with a clarifying comment.
- `AppLockController.RestoreHostVisibility` — pre-existing "defensive skip" comment that doesn't
  match its code. Noted during the A3 review; out of this audit's scope.
- Live-RDP smoke tests (`RdpHostControlSmokeTests.Gate2…`) require a reachable RDP host; the test
  VM has since been removed, so these are expected to fail locally until a host is available. Not
  a code issue — the automated `Gate1` GDI-leak test does run and passed.
