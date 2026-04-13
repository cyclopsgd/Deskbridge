---
phase: 04-rdp-integration
fixed_at: 2026-04-12T00:00:00Z
review_path: .planning/phases/04-rdp-integration/04-REVIEW.md
iteration: 1
findings_in_scope: 5
fixed: 5
skipped: 0
status: all_fixed
---

# Phase 4: Code Review Fix Report

**Fixed at:** 2026-04-12
**Source review:** `.planning/phases/04-rdp-integration/04-REVIEW.md`
**Iteration:** 1

**Summary:**
- Findings in scope: 5 (critical=0, warning=5; info=8 deferred — `--all` not passed)
- Fixed: 5
- Skipped: 0
- Final build: `dotnet build Deskbridge.sln -c Debug` → **0 warnings, 0 errors**
- Final tests: `dotnet test tests/Deskbridge.Tests --no-build` → **176 passed, 0 failed, 3 skipped**

## Fixed Issues

### WR-01: Replace-active-host race can leave stale WindowsFormsHost mounted

**Files modified:** `src/Deskbridge.Core/Services/ConnectionCoordinator.cs`
**Commit:** `a808510`
**Applied fix:** In `OnHostCreated`, when `_active` is populated with a different host, unsubscribe `DisconnectedAfterConnect` and raise `HostUnmounted` for the previous host before reassigning `_active` to the incoming host. Follows the non-awaiting fix suggested in REVIEW.md (preserves fire-and-forget semantics in `OnConnectionRequested`). Existing tests pass because they do not exercise the replace-path through `HostCreatedEvent` (the duplicate-suppression test uses the same model, the replacement test drives through `ConnectionRequestedEvent`, not a race on two `HostCreatedEvent` hits).

### WR-02: MainWindow.OnHostMounted does not guard viewport ordering on rapid swap

**Files modified:** `src/Deskbridge/MainWindow.xaml.cs`
**Commit:** `66d9555`
**Applied fix:** Walk `ViewportGrid.Children` backwards and remove any existing `WindowsFormsHost` before adding the incoming host. Defence-in-depth against WR-01 as well as any post-failure lingering WFH. Used the fully-qualified `System.Windows.Forms.Integration.WindowsFormsHost` type to avoid adding a `using` (matches existing code style further down the file).

### WR-03: AirspaceSwapper.RestoreVisibilityToken promotes Collapsed-before to Visible-after

**Files modified:** `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs`
**Commit:** `5f89716`
**Applied fix:** Changed `RestoreVisibilityToken.Dispose` to `_host.Visibility = _previous` unconditionally (removed the `_previous == Collapsed ? Visible : _previous` coercion). Existing `HideWithoutSnapshot_Returns_DisposableToken_ThatRestoresVisibility` test still passes because it captures `Visibility = Visible` before calling `HideWithoutSnapshot` — the token restores to Visible faithfully. The semantic change only affects the edge case where the caller had Collapsed already (drag mid-flight, D-12 single-host policy), which the old code silently flipped to Visible.

### WR-04: RdpSmokeHost.ErrorOccurred payloads embed COM exception message

**Files modified:** `src/Deskbridge.Protocols.Rdp/Prototype/RdpSmokeHost.cs`
**Commit:** `d8aa327`
**Applied fix:** Replaced all five `ex.Message` embeds (lines 132, 138, 144, 159, 164) with `$"{ex.GetType().Name} HResult=0x{ex.HResult:X8}"` format. Matches the sanitization pattern in `RdpHostControl.ConnectAsync`. Lines 308-309 (discReason embed) left as-is because REVIEW.md explicitly flagged those as safe-by-design. Gate 2 smoke tests still compile because they subscribe via `event EventHandler<string>` — the payload shape is unchanged.

### WR-05: ConnectionCoordinator.RunConnectSafely / RunDisconnectSafely include ex.Message for non-sensitive exceptions

**Files modified:** `src/Deskbridge.Core/Services/ConnectionCoordinator.cs`
**Commit:** `0a328a4`
**Applied fix:** Dropped `ex.Message` unconditionally from both safe wrappers; log `ExceptionType` + `HResult:X8` only. Matches `ConnectStage`'s catch filters and the T-04-EXC / RDP-ACTIVEX-PITFALLS §3 contract. Chose the user-preferred option (drop Message unconditionally) rather than the narrower allow-list invert, to avoid the risk of missing another derived exception type later. No tests asserted on the `Message=` log field (verified via grep on `Message=`, `safeMessage`, `Hunter2` in tests) so no test churn.

## Skipped Issues

None.

## Info Findings Deferred

The following Info-severity findings were not addressed because `fix_scope` was `critical_warning` (the `--all` flag was not passed):

- IN-01: Dead field `_suppressedHost` in ConnectionCoordinator
- IN-02: RdpHostControl.Dispose re-unsubscribes an already-torn-down handler
- IN-03: `[diag]` Info-level log line in RdpHostControl should be Debug
- IN-04: DisconnectReasonClassifier.Describe swallows all exceptions silently
- IN-05: WindowsCredentialService.DeleteForConnection swallows all exceptions silently
- IN-06: ConnectionCoordinator never unsubscribes from `ReconnectOverlayHandle.CancelRequested`
- IN-07: EnumToVisibilityConverter.ConvertBack throws NotSupportedException
- IN-08: RdpReconnectCoordinator.RunAsync lacks `await notifyAttempt` error handling

All remain in REVIEW.md for a future `--all` pass or manual cleanup in the next phase.

## Final State

- Phase 4 warnings cleared: 5/5
- Build: 0 warnings, 0 errors (TreatWarningsAsErrors enforced)
- Tests: 176 passed / 3 skipped / 0 failed (no change from pre-fix baseline)
- Git log (fixer commits only):
  - `a808510` — fix(04): WR-01 hand off stale host in OnHostCreated before replacing
  - `66d9555` — fix(04): WR-02 remove stale WindowsFormsHost children before mounting
  - `5f89716` — fix(04): WR-03 restore captured Visibility, not hardcoded Visible
  - `d8aa327` — fix(04): WR-04 sanitize RdpSmokeHost.ErrorOccurred payloads
  - `0a328a4` — fix(04): WR-05 drop ex.Message unconditionally in safe wrappers

---

_Fixed: 2026-04-12_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
