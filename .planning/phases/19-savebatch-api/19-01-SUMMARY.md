---
phase: 19-savebatch-api
plan: 01
subsystem: database
tags: [json-persistence, batch-write, atomic-io, upsert, tdd]

# Dependency graph
requires:
  - phase: 03-connection-management
    provides: IConnectionStore interface and JsonConnectionStore with PersistAtomically pattern
provides:
  - SaveBatch method on IConnectionStore for bulk upsert with single atomic file write
  - ConnectionDataChangedEvent marker record for post-batch UI refresh
affects: [19-02-savebatch-api, 22-large-import-handling, 23-bulk-operations-ux]

# Tech tracking
tech-stack:
  added: []
  patterns: [batch-upsert-by-id, groups-before-connections-ordering, caller-publishes-event]

key-files:
  created:
    - tests/Deskbridge.Tests/Services/SaveBatchTests.cs
  modified:
    - src/Deskbridge.Core/Interfaces/IConnectionStore.cs
    - src/Deskbridge.Core/Services/JsonConnectionStore.cs
    - src/Deskbridge.Core/Events/ConnectionEvents.cs

key-decisions:
  - "SaveBatch accepts both connections and groups for symmetry with DeleteBatch and to fully eliminate write amplification"
  - "UpdatedAt set only on update path to preserve meaningful GetRecent ordering"
  - "Event publishing responsibility remains with caller (not in store) to avoid coupling data layer to event bus"

patterns-established:
  - "Batch upsert: groups processed before connections to prevent transient orphans"
  - "Caller-publishes pattern: SaveBatch callers must publish ConnectionDataChangedEvent after call"

requirements-completed: []

# Metrics
duration: 6min
completed: 2026-04-26
---

# Phase 19 Plan 01: SaveBatch API Summary

**SaveBatch API on IConnectionStore with groups-first upsert ordering, single PersistAtomically call, and ConnectionDataChangedEvent marker record**

## Performance

- **Duration:** 6 min
- **Started:** 2026-04-26T12:14:39Z
- **Completed:** 2026-04-26T12:21:00Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- SaveBatch method added to IConnectionStore interface and implemented in JsonConnectionStore
- Batch upsert persists N connections and M groups in a single atomic file write (one PersistAtomically call)
- ConnectionDataChangedEvent marker record defined for callers to publish after batch mutations
- 7 comprehensive unit tests covering batch insert, upsert, UpdatedAt semantics, group ordering, empty input, round-trip persistence, and atomic write cleanup

## TDD Gate Compliance

- RED gate: `b59ac13` - `test(19-01): add failing SaveBatch tests (RED)` -- 7 tests fail with NotImplementedException
- GREEN gate: `ff9c56c` - `feat(19-01): implement SaveBatch API on JsonConnectionStore` -- all 7 tests pass, full suite green (635 passed)
- REFACTOR gate: not needed -- implementation is minimal and follows existing patterns exactly

## Task Commits

Each task was committed atomically:

1. **Task 1: Define contracts and write failing tests (RED)** - `b59ac13` (test)
2. **Task 2: Implement SaveBatch (GREEN)** - `ff9c56c` (feat)

## Files Created/Modified
- `src/Deskbridge.Core/Interfaces/IConnectionStore.cs` - Added SaveBatch signature after DeleteBatch
- `src/Deskbridge.Core/Services/JsonConnectionStore.cs` - SaveBatch implementation with groups-first ordering and single PersistAtomically
- `src/Deskbridge.Core/Events/ConnectionEvents.cs` - Added ConnectionDataChangedEvent marker record
- `tests/Deskbridge.Tests/Services/SaveBatchTests.cs` - 7 unit tests covering all SaveBatch behaviors

## Decisions Made
- SaveBatch accepts both connections and groups (not connections-only) for symmetry with DeleteBatch and to fully eliminate write amplification during import
- UpdatedAt is set only on the update path (existing item found by Id), not on inserts, to preserve meaningful ordering for GetRecent queries
- Event publishing stays in the caller, matching existing patterns (ImportWizardViewModel publishes ConnectionImportedEvent); JsonConnectionStore has no IEventBus dependency

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed timing-sensitive test for UpdatedAt semantics**
- **Found during:** Task 2 (GREEN phase)
- **Issue:** Test 3 (SaveBatch_SetsUpdatedAtOnUpdatePath_NotOnInsert) constructed the insert ConnectionModel after the pivot timestamp, so its default UpdatedAt was after the pivot, causing the "inserted items should keep their original UpdatedAt" assertion to fail
- **Fix:** Moved ConnectionModel construction for both update and insert objects to before the pivot timestamp, so their default UpdatedAt values are earlier than the pivot
- **Files modified:** tests/Deskbridge.Tests/Services/SaveBatchTests.cs
- **Verification:** All 7 SaveBatch tests pass, full suite green
- **Committed in:** ff9c56c (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Test timing fix was necessary for correctness. No scope creep.

## Issues Encountered
None beyond the test timing fix documented above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- SaveBatch API is ready for Plan 19-02 to migrate the import wizard from per-item Save to single SaveBatch call
- ConnectionDataChangedEvent is defined and ready for ConnectionTreeViewModel subscription
- Full test suite green with no regressions

## Self-Check: PASSED

All files exist, all commits found, all content verified (SaveBatch in interface, SaveBatch in implementation, ConnectionDataChangedEvent record, 7 [Fact] methods).

---
*Phase: 19-savebatch-api*
*Completed: 2026-04-26*
