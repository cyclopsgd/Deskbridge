---
phase: 23
plan: 01
subsystem: testing
tags: [nyquist, test-scaffold, bulk-operations, xunit-v3]
requires: [JsonConnectionStore.SaveBatch, ConnectionTreeViewModel, ConnectionModel]
provides: [BulkEditPersistenceTests, BulkEditViewModelTests, ConnectAllTests, DisconnectAllTests]
affects: [tests/Deskbridge.Tests]
tech-stack:
  added: []
  patterns: [temp-file-JsonConnectionStore, NSubstitute-VM-construction, skipped-behavior-pinning]
key-files:
  created:
    - tests/Deskbridge.Tests/Services/BulkEditPersistenceTests.cs
    - tests/Deskbridge.Tests/ViewModels/BulkEditViewModelTests.cs
    - tests/Deskbridge.Tests/ViewModels/ConnectAllTests.cs
    - tests/Deskbridge.Tests/ViewModels/DisconnectAllTests.cs
  modified: []
decisions:
  - "MTP filter syntax is --filter-query \"/*/*/ClassName/*\" — VSTest --filter is silently ignored (MTP0001 warning) and runs the full suite"
  - "ConnectAll/DisconnectAll BuildSut mirrors the CURRENT verified ConnectionTreeViewModel ctor; the IWindowStateService param noted in the plan interface is added by 23-03, documented in TODO blocks"
  - "BulkEditPersistenceTests authored with real bodies (existing SaveBatch); VM/command tests are skipped placeholders pinning behavior until 23-02/23-03 production types exist"
metrics:
  duration: 12min
  completed: 2026-05-31
---

# Phase 23 Plan 01: Bulk-Operations Nyquist Test Scaffold Summary

Wave 0 test scaffold for Phase 23 (Connect All / Disconnect All / Bulk Edit): four xUnit.v3 test files locking the behavior contract from 23-VALIDATION.md, with BulkEditPersistence green today and VM/command tests as documented skipped placeholders for plans 02/03.

## What Was Built

- **BulkEditPersistenceTests.cs** (BULK-03, 2 real passing tests): `SaveBatch_PersistsEditedConnections_SurvivesStoreReload` and `SaveBatch_SingleWrite_NotPerItemSave`. Mirror the `BulkDeleteTests` temp-file `JsonConnectionStore` setup (constructor) and reload assertion (`BulkDeleteTests.cs:126`). Pin the single-atomic-write floor that bulk edit relies on (IMP-04).
- **BulkEditViewModelTests.cs** (BULK-03, 9 skipped): one behavior-pinning method per VM row of the Per-Task Verification Map — shared/divergent field detection, `CanApply` gating, checked-only apply, Name/Password exclusion, Port (1–65535) + non-empty Hostname validation, GroupId apply. Compiles against existing `ConnectionModel` with `// TODO 23-02:` Arrange/Act/Assert bodies.
- **ConnectAllTests.cs** (BULK-01, 8 skipped): projected count = `ActiveCount + group.ConnectionCount`, threshold boundary (`== threshold` no warn / `threshold+1` warns), confirm gating on `ConfirmBeforeBulkOperations`, per-descendant `ConnectionRequestedEvent` publish, skip-already-open (`SwitchTo`).
- **DisconnectAllTests.cs** (BULK-02, 3 skipped): `CloseTabAsync` per active descendant, no close for inactive, `GroupHasActiveSessions` enable-state.

## Verification Results

- `dotnet build tests/Deskbridge.Tests/Deskbridge.Tests.csproj` → exit 0, 0 warnings, 0 errors.
- `--filter-query "/*/*/BulkEditPersistenceTests/*"` → 2 passed.
- `--filter-query "/*/*/BulkEditViewModelTests/*"` → 9 skipped (discovered).
- `--filter-query "/*/*/ConnectAllTests/*"` → 8 skipped (discovered).
- `--filter-query "/*/*/DisconnectAllTests/*"` → 3 skipped (discovered).
- All four classes discovered by the quick filter; total new tests = 2 passing + 20 skipped.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Quick-filter command syntax corrected for Microsoft.Testing.Platform**
- **Found during:** Task 1 verification.
- **Issue:** The plan/validation `dotnet test ... --filter "FullyQualifiedName~..."` uses VSTest syntax, which MTP silently ignores (emits `MTP0001` and runs the entire suite). It does not select the new classes.
- **Fix:** Used the MTP tree-node filter form `dotnet test ... -- --filter-query "/*/*/ClassName/*"`, which correctly selects each class. Recorded as a decision so 23-02/23-03 and `/gsd-verify-work` use the working form.
- **Files modified:** none (command-line only).
- **Commits:** cc424c4, 49787d8 (verification step).

**2. [Rule 3 - Blocking] ConnectionTreeViewModel ctor does not yet have IWindowStateService**
- **Found during:** Task 2.
- **Issue:** The plan `<interfaces>` block states 23-03 adds an `IWindowStateService` ctor param, but the current `ConnectionTreeViewModel` ctor (verified against `SwitchToExistingTabTests`) does not have it. Constructing the SUT with the future signature would not compile today.
- **Fix:** `BuildSut` mirrors the current verified 10-arg ctor; each file documents in a `// TODO 23-03:` block that 23-03 will add the `IWindowStateService` substitute and wire `settings.BulkOperations` (GdiWarningThreshold). All command tests are skipped, so the SUT is constructed but unused until 23-03.
- **Files modified:** ConnectAllTests.cs, DisconnectAllTests.cs.
- **Commit:** 49787d8.

## Deferred Issues

- **Pre-existing full-suite failure (out of scope):** A full-suite run during Task 1 verification showed `Failed: 1, Passed: 754, Skipped: 9` — consistent with the STATE.md baseline (753/754). The failure is in an unrelated existing test, not in any of the four new files (all 22 new tests are accounted for: 2 pass + 20 skip). Per the executor scope boundary, this pre-existing failure was not investigated or fixed. Plans 23-02/23-03 should run the full suite at their wave boundary to confirm it remains the known baseline failure.

## TDD Gate Compliance

Both task commits use the `test(...)` prefix (RED-phase scaffold). GREEN/REFACTOR for the VM and command tests are owned by plans 23-02 (BulkEditViewModel) and 23-03 (ConnectionTreeViewModel commands), which will replace the `// TODO` bodies and remove the `Skip` attributes. The BulkEditPersistence tests are already green against existing production code (store-level contract, no new production type required).

## Known Stubs

The 20 skipped test methods are intentional Wave 0 behavior-pinning placeholders (each body is `// TODO 23-XX:` + `Assert.True(true)`), not stubs in the production sense. They carry explicit `[Fact(Skip = "Wave N: implemented by 23-0X ...")]` and are resolved by plans 23-02/23-03. This is the planned Nyquist scaffold, not unfinished work.

## Self-Check: PASSED

- FOUND: tests/Deskbridge.Tests/Services/BulkEditPersistenceTests.cs
- FOUND: tests/Deskbridge.Tests/ViewModels/BulkEditViewModelTests.cs
- FOUND: tests/Deskbridge.Tests/ViewModels/ConnectAllTests.cs
- FOUND: tests/Deskbridge.Tests/ViewModels/DisconnectAllTests.cs
- FOUND commit: cc424c4 (Task 1 — BULK-03 scaffold)
- FOUND commit: 49787d8 (Task 2 — BULK-01/BULK-02 scaffold)
