---
phase: 22
plan: 02
subsystem: large-import-handling
tags: [import, wizard, viewmodel, progress, infobar, failures, dotnet, csharp14, wpf-ui, communitytoolkit-mvvm, tdd]

dependency_graph:
  requires:
    - "22-01 — IImportExecutor + MRemoteNGImportExecutor (delivers the prepare-loop seam)"
    - "07-04 — ImportWizardViewModel + ImportWizardDialog (the wizard scaffold extended in this plan)"
    - "19-01 — IConnectionStore.SaveBatch (single-atom commit called by the VM)"
  provides:
    - "ImportWizardViewModel observable surface: IsImportWriteInProgress, TotalToImport, Failures, FailedCount, ImportSeverity, ImportTitleText"
    - "Refactored ImportSelectedAsync that delegates to IImportExecutor.PrepareAsync via Task.Run (Pattern 2 + P6 + P8)"
    - "D-06 fix-forward: CancellationToken threaded through ParseFileAsync + ParseFromStreamAsync"
    - "ImportWizardDialog determinate ProgressBar overlay + failure-list section + close-suppression (Closing handler) + best-effort visual greying (Loaded handler)"
    - "DI: AddSingleton<IImportExecutor, MRemoteNGImportExecutor>() + factory wires it into the VM"
    - "Converters: IntToVisibilityConverter, IntGreaterThanToVisibilityConverter, OverflowMoreTextConverter, InverseBoolConverter"
  affects:
    - "Plan 22-04 — manual UAT closes the visual-loop on the running app (visual greying confirmed OR Log.Warning template-miss observed)"

tech-stack:
  added: []
  patterns:
    - "Pattern A — VM owns single SaveBatch; executor never persists (D-02 compile-time guarantee preserved)"
    - "Pattern B — Progress<int> constructed on UI thread BEFORE Task.Run (RESEARCH P6)"
    - "Pattern C — Bulk-load Failures on UI thread after PrepareAsync returns (P8 option c)"
    - "Pattern D — ContentDialog.Closing handler with e.Cancel=true gates dismiss while write in progress (mirrors CrashDialog.xaml.cs:91)"
    - "Pattern E — Best-effort visual greying via Template.FindName + IsEnabled binding, wrapped in try/catch + Log.Warning fallback (PATTERNS.md correction #2(b))"
    - "Pattern F — Volatile guard flag captured by Progress<int> lambda prevents late-firing callbacks from overwriting final ImportedCount when no DispatcherSynchronizationContext is captured (parallel xUnit runs)"

key-files:
  created:
    - "tests/Deskbridge.Tests/ViewModels/ImportWizardViewModelImportProgressTests.cs"
    - "tests/Deskbridge.Tests/Services/ImportSeverityTests.cs"
    - "src/Deskbridge/Converters/IntToVisibilityConverter.cs"
    - "src/Deskbridge/Converters/IntGreaterThanToVisibilityConverter.cs"
    - "src/Deskbridge/Converters/OverflowMoreTextConverter.cs"
    - "src/Deskbridge/Converters/InverseBoolConverter.cs"
  modified:
    - "src/Deskbridge/ViewModels/ImportWizardViewModel.cs"
    - "src/Deskbridge/App.xaml.cs"
    - "src/Deskbridge/App.xaml"
    - "src/Deskbridge/Dialogs/ImportWizardDialog.xaml"
    - "src/Deskbridge/Dialogs/ImportWizardDialog.xaml.cs"
    - "tests/Deskbridge.Tests/Import/ImportWizardViewModelTests.cs"
    - ".planning/phases/22-large-import-handling/deferred-items.md"

key-decisions:
  - "Standard WPF <ProgressBar> instead of ui:ProgressBar (UI-SPEC names ui:ProgressBar but WPF-UI 4.2.0 ships only ui:ProgressRing — there is no ui:ProgressBar control). The standard control auto-restyles via WPF-UI ControlsDictionary."
  - "Volatile guard inside the Progress<int> lambda — the original plan did not anticipate that pre-existing tests (ImportWizardViewModelTests) run in parallel under xUnit v3 and lose the DispatcherSynchronizationContext, causing late-firing progress callbacks to race with the final ImportedCount assignment. Captured-array volatile flag short-circuits late callbacks deterministically across 8 stability runs."
  - "Removed the obsolete view-tier `enum DuplicateAction` (single source of truth now lives in Deskbridge.Core.Models per 22-01)."
  - "Updated existing ImportWizardViewModelTests helper to inject MRemoteNGImportExecutor (real impl) — preserves end-to-end semantic-equivalence coverage of Skip / Rename / Overwrite / auto-rename branches."

requirements-completed: [IMP-03]

metrics:
  duration_minutes: 22
  completed_date: "2026-05-03"
  tasks: 3
  commits: 6
  tests_added: 19
  total_tests_after: 745
  total_tests_before: 723
  files_created: 6
  files_modified: 7
---

# Phase 22 Plan 22-02: ImportWizardViewModel + Dialog Wiring Summary

**Wires Phase 22's executor seam into the import wizard — determinate progress bar, continues-on-failure semantics, failure-list on Step 4, disabled-Cancel-during-write contract, and a fix-forward for the dropped CancellationToken at the two parse call sites.**

## Performance

- **Duration:** ~22 min
- **Started:** 2026-05-03T13:35Z (GMT+1)
- **Completed:** 2026-05-03T13:57Z (GMT+1)
- **Tasks:** 3 (TDD RED → GREEN VM/DI → GREEN dialog)
- **Files created:** 6
- **Files modified:** 7
- **Tests added:** 19

## Accomplishments

- **VM observable surface complete.** `IsImportWriteInProgress`, `TotalToImport`, `Failures` (ObservableCollection<FailedImport>), `FailedCount`, `ImportSeverity` (InfoBarSeverity), `ImportTitleText`, plus the rewritten three-branch `ImportSummary`. All driven by deterministic computed properties that fire INPC on `Failures.CollectionChanged` and the existing `[NotifyPropertyChangedFor]` chain on `ImportedCount`.
- **`ImportSelectedAsync` refactored to the Phase 22 pattern** (RESEARCH P2 + P6 + P8 option c). The 130-line dedup loop in the VM was replaced by an 8-step orchestration that builds an `ImportRequest` on the UI thread, sets the progress denominator + write-flag before `Task.Run`, constructs `Progress<int>` on the UI thread, awaits the executor, bulk-loads failures, calls `_store.SaveBatch` exactly ONCE (D-02), and routes fatal exceptions to `ErrorMessage` without advancing `CurrentStep` (D-07).
- **D-06 fix-forward landed.** `ParseFileAsync` (line 181) and `ParseFromStreamAsync` (line 211) now own a `CancellationTokenSource` per parse run and thread `_parseCts.Token` into `IConnectionImporter.ParseAsync`. NSubstitute verifies the token is non-default in two new tests.
- **Dialog determinate progress overlay forked.** The existing parse-phase indeterminate `ProgressRing` is preserved (visible iff `IsProcessing AND NOT IsImportWriteInProgress`); the new branch shows a determinate `ProgressBar` bound to `ImportedCount / TotalToImport` plus a `"{N} of {Total}"` caption (visible iff `IsProcessing AND IsImportWriteInProgress`).
- **Step 4 InfoBar bound to the deterministic Severity rule.** `Severity={Binding ImportSeverity}` and `Title={Binding ImportTitleText}` — Success/Warning/Error map exactly to `FailedCount==0 / FailedCount>0 with imports / FailedCount>0 with no imports`.
- **Failure list section.** `ItemsControl` bound to `Failures` with `MaxHeight=180` + scroll; per-row `Name : Detail` styled per UI-SPEC; `"+ N more (see log)"` overflow footer visible iff `FailedCount>50`.
- **Close-suppression D-05 contract.** `OnDialogClosing` cancels dismiss while `IsImportWriteInProgress` (Cancel button, Esc, backdrop click — all blocked at the Closing event level). Best-effort visual greying via `OnDialogLoaded` template-part lookup with try/catch + Log.Warning fallback so a future WPF-UI bump cannot crash the app.
- **DI registration landed.** `services.AddSingleton<IImportExecutor, MRemoteNGImportExecutor>()` registered after the existing `IConnectionImporter` line; the `ImportWizardViewModel` factory now passes the resolved executor as the new positional ctor parameter.
- **Pre-existing test compatibility maintained.** `ImportWizardViewModelTests` (14 tests) helper updated to inject a real `MRemoteNGImportExecutor` so the semantic-equivalence assertions (Skip / Rename / Overwrite / auto-rename / SaveBatch single-call / event-bus publish / audit-log write) continue to pass end-to-end through the new code path.

## Task Commits

Six atomic commits (3 task commits + 3 plan-tracking commits) visible in `git log`:

| # | Commit | Subject |
|---:|---|---|
| 1 | `ae886e9` | `test(22-02): add failing tests for VM progress, CT threading, severity rule` |
| 2 | `d772342` | `docs(22-02): mark Task 1 (RED tests) complete` |
| 3 | `7b70d6c` | `feat(22-02): refactor VM around IImportExecutor; fix dropped CT at parse sites (IMP-03)` |
| 4 | `04d5897` | `docs(22-02): mark Task 2 (VM refactor + CT fix + DI) complete` |
| 5 | `3ae5f07` | `feat(22-02): wire ImportWizardDialog to determinate progress + failure list (IMP-03)` |
| 6 | `c9c0bca` | `docs(22-02): mark Task 3 (dialog wiring) complete` |

No `Co-Authored-By` per CLAUDE.md "Conventions".

## Files Created

| File | Lines | Purpose |
|---|---:|---|
| `tests/Deskbridge.Tests/ViewModels/ImportWizardViewModelImportProgressTests.cs` | ~330 | 12 tests covering defaults, ctor shape, CT threading at both parse sites, ImportSelectedAsync delegation (flag flip, bulk-load Failures, SaveBatch×1, fatal-throw suppresses commit), and ImportSummary copy in the partial-failure + total-failure branches. |
| `tests/Deskbridge.Tests/Services/ImportSeverityTests.cs` | ~110 | 7 tests covering the Severity / Title rule (Success / Warning / Error) + INPC firing on `Failures.Add`. |
| `src/Deskbridge/Converters/IntToVisibilityConverter.cs` | ~25 | `value > 0 ? Visible : Collapsed`. Drives failure-list section visibility. |
| `src/Deskbridge/Converters/IntGreaterThanToVisibilityConverter.cs` | ~32 | `(int)value > int.Parse((string)param) ? Visible : Collapsed`. Drives the overflow-footer visibility. |
| `src/Deskbridge/Converters/OverflowMoreTextConverter.cs` | ~32 | `$"+ {value - param} more (see log)"`. Renders the overflow-footer text. |
| `src/Deskbridge/Converters/InverseBoolConverter.cs` | ~17 | `!value`. Used by the Loaded handler's `IsEnabled` binding for visual greying. |

## Files Modified

### `src/Deskbridge/ViewModels/ImportWizardViewModel.cs`

- Added `IImportExecutor _executor` field + 5th positional ctor parameter.
- Added 5 observable / computed properties: `IsImportWriteInProgress`, `TotalToImport`, `Failures` (ObservableCollection<FailedImport>), `FailedCount`, `ImportSeverity`, `ImportTitleText`.
- Replaced the single-branch `ImportSummary` with the three-branch UI-SPEC version.
- Added a `_parseCts` `CancellationTokenSource` field + threaded `_parseCts.Token` into both `ParseAsync` call sites (lines 181 + 211 of the pre-22-02 file — D-06 fix-forward).
- Replaced `ImportSelectedAsync` (130-line dedup loop) with the Phase 22 8-step orchestration delegating to `IImportExecutor.PrepareAsync`. New private helpers `CountConnections` + `ToImportedNodes`.
- Removed the obsolete view-tier `enum DuplicateAction` at the old line 484; `DuplicateItemViewModel.Action` now binds to `Deskbridge.Core.Models.DuplicateAction` (single source of truth post-22-01).
- Added a `Failures.CollectionChanged` subscription that re-fires INPC for `FailedCount`, `ImportSeverity`, `ImportTitleText`, `ImportSummary`.

### `src/Deskbridge/App.xaml.cs`

- Added `services.AddSingleton<IImportExecutor, MRemoteNGImportExecutor>()` after the existing `IConnectionImporter` registration (line ~325).
- Updated the `ImportWizardViewModel` transient factory to pass `sp.GetRequiredService<IImportExecutor>()` as the new positional parameter.

### `src/Deskbridge/App.xaml`

- Registered the four new converters as `StaticResource` keys (`IntToVisibility`, `IntGreaterThanToVisibility`, `OverflowMoreText`).

### `src/Deskbridge/Dialogs/ImportWizardDialog.xaml`

- Forked the processing overlay on `IsImportWriteInProgress` — parse-phase ring preserved; write-phase determinate `ProgressBar` (standard WPF, not `ui:ProgressBar`) + `MultiBinding StringFormat="{}{0} of {1}"` caption added.
- Step 4 panel: bound `Severity` + `Title` to the new VM properties; appended failure-list heading + ItemsControl + overflow-footer per UI-SPEC §"Failure List Layout".
- Opportunistic remediation: replaced literal `FontSize="11"` on the protocol label in the Step 3 tree HierarchicalDataTemplate with `Style="{StaticResource SectionLabelStyle}"` (UI-SPEC §"Critical typography rule").

### `src/Deskbridge/Dialogs/ImportWizardDialog.xaml.cs`

- Added `Closing += OnDialogClosing` and `Loaded += OnDialogLoaded` subscriptions in the constructor.
- `OnDialogClosing` body: `if (_viewModel.IsImportWriteInProgress) e.Cancel = true;` — verbatim mirror of CrashDialog.xaml.cs:91.
- `OnDialogLoaded` body: try/catch around `Template.FindName("CloseButton", this)` + `closeBtn.SetBinding(Control.IsEnabledProperty, ...)` with `Serilog.Log.Warning` fallback when the template part is missing or the binding throws.

### `tests/Deskbridge.Tests/Import/ImportWizardViewModelTests.cs`

- Updated the `BuildViewModel` helper + two direct `new ImportWizardViewModel(...)` call sites (Tests 2 + 13) to inject `executor: new MRemoteNGImportExecutor()` so the existing 14-test suite continues to pass through the new code path.

### `.planning/phases/22-large-import-handling/deferred-items.md`

- Logged the env-dependent `Gate2_IMsTscNonScriptable_PasswordSetSucceeds` smoke test (requires a live RDP host with valid credentials; `discReason=1800` "server rejected auth" reproduces on the pre-Phase-22 baseline). Pre-existing — not Phase-22 caused.

## Decisions Made

- **Standard WPF `<ProgressBar>` instead of `ui:ProgressBar`** — UI-SPEC §"Progress Surface — Layout" names `ui:ProgressBar`, but WPF-UI 4.2.0 ships only `ui:ProgressRing` (no `ProgressBar` in the lepoco namespace). Used the standard WPF control with `Height=4`; WPF-UI's `ControlsDictionary` auto-restyles standard controls (per docs/REFERENCE.md). Tagged as a deviation below for the verifier.
- **Volatile-flag guard around the Progress<int> callback** — late-firing callbacks raced with `ImportedCount = result.ImportedCount` in parallel xUnit runs (no captured DispatcherSynchronizationContext). The plan didn't anticipate this; the fix is a captured `bool[1]` accessed via `Volatile.Read` / `Volatile.Write`. Stable across 8 consecutive `ImportWizardViewModelTests` class runs after the fix; was 60% flaky before.
- **`ImportWizardViewModelTests` helper updated rather than duplicated** — Plan said "If a prior VM test breaks, fix the test's call site to inject an `IImportExecutor` mock — do NOT change test semantics." The test semantics are unchanged; only the helper now constructs a real `MRemoteNGImportExecutor` because the existing Skip/Rename/Overwrite assertions need the real prepare-loop logic to flow end-to-end.
- **Co-located `ImportWizardViewModelTestAccess` extension shim** — `ImportSelectedAsync` and `ParseFileAsync` are `internal` on the VM; `InternalsVisibleTo("Deskbridge.Tests")` is already configured, but the new test file uses extension methods that wrap reflection-based invocation for a uniform call surface (and to keep the tests readable as `vm.ImportSelectedAsync_ForTesting()` rather than reflection boilerplate inside each test).

## Deviations from Plan

### [Rule 3 — Blocking] `ui:ProgressBar` → standard WPF `ProgressBar`

- **Found during:** Task 3 (XAML compile).
- **Issue:** `<ui:ProgressBar>` does not exist in WPF-UI 4.2.0's namespace; build error MC3074 ("The tag 'ProgressBar' does not exist in XML namespace 'http://schemas.lepo.co/wpfui/2022/xaml'").
- **Fix:** Used standard WPF `<ProgressBar>` with `Height=4`. WPF-UI's `ControlsDictionary` (registered in `App.xaml`) auto-restyles standard controls to the Fluent track, so visually this is equivalent.
- **Files modified:** `src/Deskbridge/Dialogs/ImportWizardDialog.xaml`
- **Verification:** `dotnet build src/Deskbridge/Deskbridge.csproj` exits 0.
- **Committed in:** `3ae5f07` (Task 3 GREEN commit).

### [Rule 1 — Bug] Progress<int> callback race overwrites final ImportedCount

- **Found during:** Task 2 verification (parallel xUnit runs of `ImportWizardViewModelTests` class showed intermittent `ImportedCount` assertion failures with stale processed-counts).
- **Issue:** When `ImportSelectedAsync` runs in a context with no `DispatcherSynchronizationContext` (parallel xUnit runs), `Progress<int>.Report` queues callbacks via the thread pool. A queued callback can fire AFTER `await Task.Run(...)` returns and AFTER `ImportedCount = result.ImportedCount`, overwriting the final value with a stale processed-count. With a Dispatcher in production, callbacks marshal back to the UI thread and sequence cleanly behind the continuation, so the production flow is unaffected — but tests fail intermittently.
- **Fix:** Captured `bool[1] progressActive` referenced via `System.Threading.Volatile.Write/Read`. Lambda short-circuits when the flag is false; flag flipped to false before the final `ImportedCount` assignment.
- **Files modified:** `src/Deskbridge/ViewModels/ImportWizardViewModel.cs`
- **Verification:** 8/8 consecutive `ImportWizardViewModelTests` class runs all 15 tests passing (was 60% flake rate before the fix).
- **Committed in:** `7b70d6c` (Task 2 GREEN commit, included in the larger refactor).

**Total deviations:** 2 auto-fixed (1 blocking compile error, 1 race-condition bug surfaced by tests).

## Issues Encountered

- **Pre-existing parallel-execution flakes** in `Deskbridge.Tests.Logging.AuditLoggerTests.LogAsync_OnIOFailure_FallsBackToSerilog_DoesNotThrow` and `Deskbridge.Tests.Logging.CrashHandlerTests.OnAppDomainUnhandled_LogsFatalWithTerminatingFlag`. Both involve shared Serilog sink state across parallel collections. ~1 in 5 full-suite runs hit one of these; reproduce on pre-Phase-22 baseline. Not Phase-22 caused. Already logged in `deferred-items.md` (CrashHandler entry from 22-01; AuditLogger noted in passing).
- **Env-dependent RDP smoke test** `Gate2_IMsTscNonScriptable_PasswordSetSucceeds` fails with `discReason=1800` (server rejected auth) when no live RDP host with the configured credentials is reachable. Reproduces on the pre-Phase-22 baseline. Logged in `deferred-items.md` under Plan 22-02 with a suggested `[Trait("Category", "RequiresLiveRdp")]` exclusion path.

## TDD Gate Compliance

Plan-level TDD enforced — `git log` shows the gate sequence:

1. **RED gate** (test commit, no impl): `ae886e9` — `test(22-02): add failing tests for VM progress, CT threading, severity rule` (23 compile errors verifying missing properties / ctor parameter).
2. **GREEN gate** (impl commit, all tests pass): `7b70d6c` — `feat(22-02): refactor VM around IImportExecutor; fix dropped CT at parse sites (IMP-03)`.
3. **Follow-on GREEN** (dialog wiring on top of green VM): `3ae5f07` — `feat(22-02): wire ImportWizardDialog to determinate progress + failure list (IMP-03)`.

REFACTOR gate not required — Task 3 is additive XAML / converter work that flows through the same VM contract Task 2 made green.

## Verification

| Gate | Result |
|---|---|
| Task 1 RED build | `dotnet build tests/Deskbridge.Tests` — 23 errors, all CS0117/CS1061/CS1739 referencing missing VM properties + `executor` parameter (expected — properties + ctor don't exist yet). |
| Task 2 GREEN — targeted | `Deskbridge.Tests -trait Category=ImportWizardViewModelImportProgress -trait Category=ImportSeverity` — **19 passed, 0 failed**. |
| Task 2 GREEN — pre-existing VM tests | `Deskbridge.Tests -class Deskbridge.Tests.Import.ImportWizardViewModelTests` — **15 passed, 0 failed** across 8 stability runs after volatile-guard fix. |
| Task 3 build | `dotnet build src/Deskbridge/Deskbridge.csproj` — 0 errors, 0 warnings. |
| Task 3 full suite | `Deskbridge.Tests -class- Deskbridge.Tests.Smoke.RdpHostControlSmokeTests` — **741 passed, 0 failed**. |
| Phase 22 surface scan | `grep "AddSingleton<IImportExecutor" src/Deskbridge/App.xaml.cs` → 1 hit. `grep "Closing += OnDialogClosing" src/Deskbridge/Dialogs/ImportWizardDialog.xaml.cs` → 1 hit. `grep "ProgressBar" src/Deskbridge/Dialogs/ImportWizardDialog.xaml` → 1 hit. |
| XAML diff sanity | Zero literal numeric `FontSize` additions, zero hex colors, zero non-multiple-of-4 margins (4, 8, 16 only). |

## Open Items / Carry-forward to 22-04

- **Manual UAT** for the visual-greying success path: confirm the close button is visually greyed (binds successfully via `Template.FindName("CloseButton", this)`) OR observe a `Log.Warning` line documenting the template-part miss. Either outcome satisfies UI-SPEC §Acceptance #3 — the runtime cancel-suppression in `OnDialogClosing` prevents close regardless.
- **Manual UAT** for the determinate progress bar: load a 5000-row mRemoteNG XML, click Import, observe the bar advances smoothly from 0 to 5000 with the `"{N} of {Total}"` caption updating per row.
- **Manual UAT** for the failure list section: load `Fixtures/large/malformed-single-row.xml` (delivered by 22-03), trigger import, observe Step 4's InfoBar shows Warning severity + "Import Completed with Errors" + the failure list rendering with the bad row's `Detail`.
- **Manual UAT** for the failure-list overflow: synthesize a 51+-failure scenario (e.g. all-duplicates with `DuplicateAction.Skip`) — observe the visible list caps at the natural `MaxHeight=180` scroll cap and the `"+ N more (see log)"` footer renders only when `FailedCount > 50`.

## Self-Check

**Files created:**
- FOUND: `tests/Deskbridge.Tests/ViewModels/ImportWizardViewModelImportProgressTests.cs`
- FOUND: `tests/Deskbridge.Tests/Services/ImportSeverityTests.cs`
- FOUND: `src/Deskbridge/Converters/IntToVisibilityConverter.cs`
- FOUND: `src/Deskbridge/Converters/IntGreaterThanToVisibilityConverter.cs`
- FOUND: `src/Deskbridge/Converters/OverflowMoreTextConverter.cs`
- FOUND: `src/Deskbridge/Converters/InverseBoolConverter.cs`

**Commits:**
- FOUND: `ae886e9` (Task 1 RED)
- FOUND: `7b70d6c` (Task 2 GREEN VM/DI)
- FOUND: `3ae5f07` (Task 3 GREEN dialog)
- FOUND: `d772342`, `04d5897`, `c9c0bca` (plan-tracking docs commits)

**Tests:** 745 total / 19 new in Phase 22-02 / 4 pre-existing flakes (1 RDP env-dependent smoke + 2 Serilog-sink parallel races, all reproduced on pre-Phase-22 baseline).

## Self-Check: PASSED

---
*Phase: 22-large-import-handling*
*Completed: 2026-05-03*
