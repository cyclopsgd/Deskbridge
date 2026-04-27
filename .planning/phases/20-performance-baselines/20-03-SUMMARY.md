---
phase: 20-performance-baselines
plan: 03
subsystem: testing
tags: [benchmarkdotnet, performance-baselines, tree-builder, connection-store, query-service]

# Dependency graph
requires:
  - phase: 20-performance-baselines
    provides: "ConnectionTreeBuilder.Build() static method and TestDataGenerator.Generate() for benchmark data"
provides:
  - "Deskbridge.Benchmarks project with BenchmarkDotNet 0.15.8"
  - "4 benchmark classes covering 7 operations (8 benchmark methods)"
  - "Baseline JSON reports committed for regression comparison"
  - "DeskbridgeBenchmarkConfig with MemoryDiagnoser and JSON/CSV/Markdown exporters"
affects: [21-performance-optimization]

# Tech tracking
tech-stack:
  added: [BenchmarkDotNet 0.15.8]
  patterns: [parameterized-benchmarks, IterationSetup-for-mutating-ops, ManualConfig-with-exporters]

key-files:
  created:
    - tests/Deskbridge.Benchmarks/Deskbridge.Benchmarks.csproj
    - tests/Deskbridge.Benchmarks/Program.cs
    - tests/Deskbridge.Benchmarks/Config/BenchmarkConfig.cs
    - tests/Deskbridge.Benchmarks/Benchmarks/TreeBuildBenchmarks.cs
    - tests/Deskbridge.Benchmarks/Benchmarks/SearchBenchmarks.cs
    - tests/Deskbridge.Benchmarks/Benchmarks/QueryBenchmarks.cs
    - tests/Deskbridge.Benchmarks/Benchmarks/StoreBenchmarks.cs
    - tests/Deskbridge.Benchmarks/.gitignore
  modified:
    - Deskbridge.sln
    - Directory.Packages.props
    - .gitignore

key-decisions:
  - "BenchmarkDotNet version managed via Central Package Management (Directory.Packages.props)"
  - "Used --job short for baseline capture to ensure reliable completion within CI-friendly time budget"
  - "StoreBenchmarks uses IterationSetup per-target to reset file state for mutating operations"
  - "QueryBenchmarks uses non-text filters (Tag, Protocol) to isolate GetByFilter from Search cost"

patterns-established:
  - "IterationSetup(Target=...) for disk-mutating benchmarks that need fresh state each iteration"
  - "GlobalSetup for read-only benchmarks using TestDataGenerator deterministic data"
  - "Baseline JSON in tracked baseline/ directory; BenchmarkDotNet.Artifacts/ gitignored"

requirements-completed: [PERF-04]

# Metrics
duration: 12min
completed: 2026-04-27
---

# Phase 20 Plan 03: BenchmarkDotNet Baseline Suite Summary

**BenchmarkDotNet project measuring BuildTree, Search, GetByFilter, Load, Save, SaveBatch, and DeleteBatch at 100/200/500/1000 connections with committed baseline JSON**

## Performance

- **Duration:** 12 min
- **Started:** 2026-04-27T18:44:49Z
- **Completed:** 2026-04-27T18:57:00Z
- **Tasks:** 2
- **Files modified:** 11

## Accomplishments
- Deskbridge.Benchmarks project scaffolded with BenchmarkDotNet 0.15.8, registered in Deskbridge.sln under tests folder
- 4 benchmark classes (TreeBuild, Search, Query, Store) covering all 7 operations with 8 benchmark methods
- Each benchmark parameterized at 100, 200, 500, and 1000 connections using TestDataGenerator
- Real baseline results captured and committed (4 JSON report files) for future regression comparison
- DeskbridgeBenchmarkConfig with MemoryDiagnoser, JSON/CSV/Markdown exporters, and JitOptimizationsValidator

## Baseline Results Summary

| Operation | 100 conns | 1000 conns | Trend |
|-----------|-----------|------------|-------|
| BuildTree | ~7.4 us | ~79.4 us | Linear |
| Search | measured | measured | Captured |
| GetByFilter_Tag | measured | measured | Captured |
| GetByFilter_Protocol | measured | measured | Captured |
| Load | measured | measured | Captured |
| Save | measured | measured | Captured |
| SaveBatch | measured | measured | Captured |
| DeleteBatch | measured | measured | Captured |

## Task Commits

Each task was committed atomically:

1. **Task 1: Scaffold benchmark project, add to solution, verify build + dry run** - `f4cd937` (chore)
2. **Task 2: Create all benchmark classes and run baseline** - `5cd2c4a` (feat)

## Files Created/Modified
- `tests/Deskbridge.Benchmarks/Deskbridge.Benchmarks.csproj` - Exe project with BenchmarkDotNet and Deskbridge.Core reference
- `tests/Deskbridge.Benchmarks/Program.cs` - BenchmarkSwitcher entry point
- `tests/Deskbridge.Benchmarks/Config/BenchmarkConfig.cs` - ManualConfig with MemoryDiagnoser, exporters, JitOptimizationsValidator
- `tests/Deskbridge.Benchmarks/Benchmarks/TreeBuildBenchmarks.cs` - ConnectionTreeBuilder.Build benchmark
- `tests/Deskbridge.Benchmarks/Benchmarks/SearchBenchmarks.cs` - ConnectionQueryService.Search benchmark
- `tests/Deskbridge.Benchmarks/Benchmarks/QueryBenchmarks.cs` - GetByFilter with Tag and Protocol filters
- `tests/Deskbridge.Benchmarks/Benchmarks/StoreBenchmarks.cs` - Load/Save/SaveBatch/DeleteBatch with IterationSetup
- `tests/Deskbridge.Benchmarks/.gitignore` - Excludes BenchmarkDotNet.Artifacts/
- `tests/Deskbridge.Benchmarks/baseline/*.json` - 4 baseline report files
- `Deskbridge.sln` - Added Deskbridge.Benchmarks under tests folder
- `Directory.Packages.props` - Added BenchmarkDotNet 0.15.8 to CPM
- `.gitignore` - Added root-level BenchmarkDotNet.Artifacts/ exclusion

## Decisions Made
- BenchmarkDotNet version centralized in Directory.Packages.props (CPM) rather than inline in csproj -- consistent with project convention
- Used `--job short` for baseline capture per research guidance: produces real statistical measurements with acceptable confidence intervals, completes reliably within time budget
- StoreBenchmarks uses targeted `[IterationSetup(Target = nameof(...))]` to reset file state for each mutating operation, preventing cross-contamination between Load/Save/SaveBatch/DeleteBatch iterations
- QueryBenchmarks filters by Tag and Protocol (not SearchText) to isolate GetByFilter filtering cost from Search string-matching cost

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added BenchmarkDotNet to Central Package Management**
- **Found during:** Task 1 (build step)
- **Issue:** Project uses Central Package Management (Directory.Packages.props) -- specifying Version in csproj caused NU1008 error
- **Fix:** Added `<PackageVersion Include="BenchmarkDotNet" Version="0.15.8" />` to Directory.Packages.props, removed Version from csproj PackageReference
- **Files modified:** Directory.Packages.props, Deskbridge.Benchmarks.csproj
- **Verification:** `dotnet build tests/Deskbridge.Benchmarks -c Release` exits 0
- **Committed in:** f4cd937 (Task 1 commit)

**2. [Rule 3 - Blocking] Added System.IO using to StoreBenchmarks**
- **Found during:** Task 2 (build step after writing benchmark classes)
- **Issue:** Path and Directory types not in scope -- WPF implicit usings don't include System.IO
- **Fix:** Added `using System.IO;` to StoreBenchmarks.cs
- **Files modified:** tests/Deskbridge.Benchmarks/Benchmarks/StoreBenchmarks.cs
- **Verification:** Build succeeds with 0 errors
- **Committed in:** 5cd2c4a (Task 2 commit)

**3. [Rule 2 - Missing Critical] Added BenchmarkDotNet.Artifacts/ to root .gitignore**
- **Found during:** Task 2 (after benchmark run)
- **Issue:** BenchmarkDotNet creates artifacts at the working directory root (repo root when using dotnet run), not in the project directory. Only the project-level .gitignore covered it.
- **Fix:** Added `BenchmarkDotNet.Artifacts/` entry to root .gitignore
- **Files modified:** .gitignore
- **Verification:** Root artifacts directory excluded from git tracking
- **Committed in:** 5cd2c4a (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (2 blocking, 1 missing critical)
**Impact on plan:** All fixes necessary for build and correctness. No scope creep.

## Issues Encountered
None beyond the auto-fixed items above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Baseline suite complete -- developer runs `dotnet run --project tests/Deskbridge.Benchmarks -c Release -- --filter '*'`
- 4 baseline JSON reports committed for future regression comparison via BenchmarkDotNet's `--baseline` flag
- Phase 21 optimization work can measure improvements against these baselines
- All 656 existing tests pass; solution builds cleanly

---
*Phase: 20-performance-baselines*
*Completed: 2026-04-27*
