# Phase 20: Performance Baselines - Context

**Gathered:** 2026-04-27
**Status:** Ready for planning

<domain>
## Phase Boundary

BenchmarkDotNet project with regression baselines for tree building, search, and serialization performance at enterprise scale (100/200/500/1000 connections). Provides a deterministic test data generator and committed baseline results for detecting regressions. Does NOT optimize any operations — Phase 21 uses these baselines to guide optimization work.

</domain>

<decisions>
## Implementation Decisions

### Test Data Shape
- **D-01:** Enterprise-realistic data shape — 3-level nesting (e.g., Region > Environment > Role), realistic hostnames like `srv-prod-web-001`, ~5-10 groups per level, connections distributed unevenly across groups (some groups 50+, some 3).
- **D-02:** Fixed seed for deterministic output — same input count always produces identical dataset. Benchmarks are reproducible across machines and runs.

### Benchmark Scope
- **D-03:** Extract core tree-building logic from `ConnectionTreeViewModel.BuildTree()` into a pure static/core method in `Deskbridge.Core` that the ViewModel calls. Benchmark the pure method directly — no UI dependencies (no `ICredentialService`, no `ObservableCollection`).
- **D-04:** Seven operations benchmarked total: BuildTree, Search, Load, Save (the four from PERF-04), plus SaveBatch, DeleteBatch, and GetByFilter.
- **D-05:** Each operation benchmarked at 100, 200, 500, and 1000 connection counts (parameterized benchmarks).

### Baseline Storage
- **D-06:** Use BenchmarkDotNet's built-in JSON/CSV exporters for result artifacts. Results land in `BenchmarkDotNet.Artifacts/`.
- **D-07:** Git-track the initial baseline run as the reference point. `.gitignore` subsequent runs. Developer compares locally against the committed baseline.

### Project Structure
- **D-08:** Test data generator lives in `Deskbridge.Core` — reusable by both `Deskbridge.Benchmarks` and `Deskbridge.Tests` for large-data test scenarios.
- **D-09:** Benchmark project at `tests/Deskbridge.Benchmarks/`. Dev-only, not shipped. Alongside existing `Deskbridge.Tests` project.

### Claude's Discretion
- Exact BenchmarkDotNet configuration (warmup, iteration count, exporters)
- Test data generator class name and API surface
- How to organize benchmark classes (one per operation vs grouped by component)
- Exact hostname/group naming patterns in the generator

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Architecture & Models
- `docs/REFERENCE.md` — Architecture, DI registrations, constraints
- `src/Deskbridge.Core/Models/ConnectionModel.cs` — Connection model (14 properties to generate)
- `src/Deskbridge.Core/Models/ConnectionGroup.cs` — Group model (4 properties, supports nesting via ParentGroupId)

### Operations to Benchmark
- `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` lines 419-488 — `BuildTree()` method to extract core logic from
- `src/Deskbridge.Core/Services/ConnectionQueryService.cs` — `Search()` with fuzzy scoring + `GetByFilter()`
- `src/Deskbridge.Core/Services/JsonConnectionStore.cs` — `Load()`, `Save()`, `SaveBatch()`, `DeleteBatch()`, `PersistAtomically()`

### Existing Project Structure
- `src/Deskbridge.Core/Deskbridge.Core.csproj` — Core project (generator will live here)
- `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` — Existing test project (benchmarks project sits alongside)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ConnectionModel` and `ConnectionGroup` — well-defined models with simple properties, straightforward to generate at scale
- `JsonConnectionStore` — self-contained store with file path constructor, can be instantiated in benchmarks with temp paths
- `ConnectionQueryService` — can be constructed with `IEnumerable<ConnectionModel>` directly (no DI needed)
- `IConnectionStore` interface — benchmark can use `JsonConnectionStore` directly or stub the interface

### Established Patterns
- `ConnectionQueryService(IConnectionStore store)` constructor — benchmarks can inject a pre-loaded store
- `JsonConnectionStore(string filePath)` constructor — benchmarks can point at temp files
- Atomic write via tmp-file-rename in `PersistAtomically()` — important to capture real I/O cost

### Integration Points
- `BuildTree()` core logic needs to be extracted from `ConnectionTreeViewModel` into a method in `Deskbridge.Core` — this is a code change, not just a new project
- Test data generator in `Deskbridge.Core` will be available to `Deskbridge.Tests` for future large-scale integration tests
- `Deskbridge.Benchmarks` references `Deskbridge.Core` (and possibly `Deskbridge` main project if benchmarking ViewModel path)

</code_context>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. Follow BenchmarkDotNet conventions.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 20-performance-baselines*
*Context gathered: 2026-04-27*
