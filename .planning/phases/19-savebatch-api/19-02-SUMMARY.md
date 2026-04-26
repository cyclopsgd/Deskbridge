---
phase: 19-savebatch-api
plan: 02
subsystem: data
tags: [savebatch, import, event-bus, batch-persistence]

# Dependency graph
requires:
  - phase: 19-01
    provides: "SaveBatch method on IConnectionStore and ConnectionDataChangedEvent record"
provides:
  - "Import wizard uses single SaveBatch call instead of per-item Save"
  - "ConnectionTreeViewModel auto-refreshes on ConnectionDataChangedEvent"
affects: [22-large-import-handling, 23-bulk-operations-ux]

# Tech tracking
tech-stack:
  added: []
  patterns: ["batch-collect-then-persist: accumulate models in lists, call SaveBatch once at end"]

key-files:
  created: []
  modified:
    - "src/Deskbridge/ViewModels/ImportWizardViewModel.cs"
    - "src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs"
    - "tests/Deskbridge.Tests/Import/ImportWizardViewModelTests.cs"

key-decisions:
  - "ConnectionDataChangedEvent published before ConnectionImportedEvent to ensure tree refresh before toast notification"

patterns-established:
  - "Batch-collect-then-persist: accumulate models in lists during loop, call SaveBatch once after loop completes"
  - "Caller-publishes event: ImportWizardViewModel publishes ConnectionDataChangedEvent after SaveBatch (not the store)"

requirements-completed: [IMP-04]

# Metrics
duration: 7min
completed: 2026-04-26
---

# Phase 19 Plan 02: Consumer Migration Summary

**Import wizard migrated from 4 per-item Save calls to single SaveBatch, tree auto-refreshes via ConnectionDataChangedEvent subscription**

## Performance

- **Duration:** 7 min
- **Started:** 2026-04-26T12:26:12Z
- **Completed:** 2026-04-26T12:33:12Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Replaced all 4 `_store.Save()` and 1 `_store.SaveGroup()` calls in ImportWizardViewModel with list accumulation + single `_store.SaveBatch()` call
- Wired ConnectionTreeViewModel to subscribe to ConnectionDataChangedEvent and auto-refresh tree on UI thread
- Published ConnectionDataChangedEvent after SaveBatch to notify all subscribers of bulk data mutation
- Preserved existing ConnectionImportedEvent for toast notifications (no behavior change)

## Task Commits

Each task was committed atomically:

1. **Task 1: Migrate ImportWizardViewModel to use SaveBatch** - `bab13df` (feat)
2. **Task 2: Wire ConnectionTreeViewModel to refresh on ConnectionDataChangedEvent** - `3484c94` (feat)

## Files Created/Modified
- `src/Deskbridge/ViewModels/ImportWizardViewModel.cs` - Batch-collecting import loop with SaveBatch, ConnectionDataChangedEvent publish
- `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` - Event subscription and OnDataChanged handler for tree refresh
- `tests/Deskbridge.Tests/Import/ImportWizardViewModelTests.cs` - Updated Tests 9 and 10 to assert SaveBatch instead of per-item Save

## Decisions Made
- ConnectionDataChangedEvent is published before ConnectionImportedEvent so the tree refreshes before the toast notification appears
- Followed caller-publishes pattern as established in Plan 01 (store does not auto-publish events)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Updated test assertions from Save to SaveBatch**
- **Found during:** Task 1 (ImportWizardViewModel migration)
- **Issue:** Tests 9 and 10 in ImportWizardViewModelTests.cs asserted `store.Received(3).Save()` and captured via `store.When(s => s.Save(...))` -- both broke after removing per-item Save calls
- **Fix:** Test 9 now asserts `store.Received(1).SaveBatch()` with 3 connections; Test 10 now captures via `store.When(s => s.SaveBatch(...))` callback
- **Files modified:** tests/Deskbridge.Tests/Import/ImportWizardViewModelTests.cs
- **Verification:** All 14 ImportWizard tests pass, full suite 635 passed
- **Committed in:** bab13df (part of Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug fix)
**Impact on plan:** Test update necessary for correctness after removing Save calls. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- SaveBatch API complete end-to-end: data layer (Plan 01) + consumer migration (Plan 02)
- Phase 22 (Large Import Handling) can now add progress bar UI with confidence that batch persistence works
- Phase 23 (Bulk Operations UX) can use SaveBatch for multi-select bulk edit
- ConnectionDataChangedEvent pattern ready for any future bulk mutation callers

---
*Phase: 19-savebatch-api*
*Completed: 2026-04-26*
