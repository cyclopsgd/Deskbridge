---
phase: 20-performance-baselines
plan: 02
subsystem: testing
tags: [benchmarks, test-data, determinism, seed-based-generation]

# Dependency graph
requires:
  - phase: 01-foundation
    provides: Core project structure, ConnectionModel, ConnectionGroup models
provides:
  - "TestDataGenerator.Generate(count, seed) static method for deterministic enterprise-realistic datasets"
  - "Unit tests verifying determinism, count accuracy, nesting depth, distribution, hostname patterns"
affects: [20-performance-baselines]

# Tech tracking
tech-stack:
  added: []
  patterns: [deterministic-guid-generation, seed-folded-with-count, weighted-random-distribution]

key-files:
  created:
    - src/Deskbridge.Core/Services/TestDataGenerator.cs
    - tests/Deskbridge.Tests/Services/TestDataGeneratorTests.cs
  modified: []

key-decisions:
  - "Folded connectionCount into seed via HashCode.Combine to ensure N=100 is not a subset of N=1000"
  - "Removed per-level jitter from group generation to keep total group count predictably within ~connectionCount/10"
  - "Used SequenceEqual instead of FluentAssertions NotBeEquivalentTo for Guid list comparison to avoid O(n^2) performance"

patterns-established:
  - "Deterministic Guid generation: Random.NextBytes into new Guid(byte[]) -- never Guid.NewGuid() in test data"
  - "Deterministic dates: fixed base date with Random.Next offsets -- never DateTime.UtcNow in test data"
  - "ComputeLevelCounts: brute-force search of R/E/Ro combos minimizing distance to target group count"

requirements-completed: [PERF-04]

# Metrics
duration: 8min
completed: 2026-04-27
---

# Phase 20 Plan 02: Test Data Generator Summary

**Deterministic test data generator producing enterprise-realistic 3-level connection datasets at configurable sizes with seed-based reproducibility**

## Performance

- **Duration:** 8 min
- **Started:** 2026-04-27T18:33:29Z
- **Completed:** 2026-04-27T18:41:53Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- TestDataGenerator.Generate(count, seed) produces exactly N connections with ~N/10 groups in a 3-level hierarchy (Region > Environment > Role)
- Same (count, seed) pair always produces identical output; different seeds produce different datasets
- Uneven weighted distribution across leaf groups ensures realistic data shape (max group >= 3x min group)
- All hostnames follow `srv-{region}-{env}-{role}-{NNN}` pattern with single-word region/env/role tokens
- 9 comprehensive unit tests covering determinism, count accuracy, nesting depth, distribution, hostnames, GroupId validity, and property completeness

## Task Commits

Each task was committed atomically:

1. **Task 1: Write TestDataGeneratorTests (RED phase)** - `b3e7d95` (test)
2. **Task 2: Implement TestDataGenerator (GREEN phase)** - `33edb80` (feat)

## Files Created/Modified
- `src/Deskbridge.Core/Services/TestDataGenerator.cs` - Static generator with Generate(count, seed) producing deterministic ConnectionModel/ConnectionGroup datasets
- `tests/Deskbridge.Tests/Services/TestDataGeneratorTests.cs` - 9 unit tests verifying determinism, counts, nesting, distribution, hostnames, GroupIds, properties

## Decisions Made
- Folded connectionCount into seed via `HashCode.Combine(seed, connectionCount)` so each (count, seed) pair is independent (N=100 is NOT a subset of N=1000)
- Used single-word region tokens (`useast`, `uswest`, etc.) instead of hyphenated (`us-east`) to match hostname regex pattern `srv-[a-z]+-[a-z]+-[a-z]+-\d{3}`
- Removed random jitter from env/role counts per level to keep total group count predictably within the ~connectionCount/10 target range

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed FluentAssertions NotBeEquivalentTo performance on large Guid collections**
- **Found during:** Task 2 (GREEN phase test run)
- **Issue:** `NotBeEquivalentTo` on 100-element Guid lists caused O(n^2) comparison taking 2+ minutes
- **Fix:** Changed to `SequenceEqual(...).Should().BeFalse()` for fast linear comparison
- **Files modified:** tests/Deskbridge.Tests/Services/TestDataGeneratorTests.cs
- **Verification:** Test completes in <1 second
- **Committed in:** 33edb80 (Task 2 commit)

**2. [Rule 1 - Bug] Fixed ComputeLevelCounts search range excluding E=1**
- **Found during:** Task 2 (GREEN phase test run)
- **Issue:** Environment count search started at E=2, preventing small group targets (e.g., 10 for 100 connections)
- **Fix:** Changed loop start from `e = 2` to `e = 1` and removed per-level jitter that could push counts outside expected range
- **Files modified:** src/Deskbridge.Core/Services/TestDataGenerator.cs
- **Verification:** Generate(100) produces 9 groups (within 8-15 range); Generate(1000) produces ~100 groups
- **Committed in:** 33edb80 (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (2 bugs)
**Impact on plan:** Both fixes necessary for test correctness. No scope creep.

## Issues Encountered
None beyond the auto-fixed items above.

## User Setup Required
None - no external service configuration required.

## TDD Gate Compliance
- RED gate: `b3e7d95` (test commit with failing build -- TestDataGenerator class did not exist)
- GREEN gate: `33edb80` (feat commit with all 9 tests passing)
- REFACTOR gate: Not needed -- implementation was clean on first pass

## Next Phase Readiness
- TestDataGenerator is ready for use by Deskbridge.Benchmarks project (Plan 03)
- Generator lives in Deskbridge.Core, reusable by both benchmark and test projects
- All 9 tests pass in Release configuration

---
*Phase: 20-performance-baselines*
*Completed: 2026-04-27*
