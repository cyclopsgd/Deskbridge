# Phase 20: Performance Baselines - Research

**Researched:** 2026-04-27
**Domain:** BenchmarkDotNet performance infrastructure, test data generation, tree-building extraction
**Confidence:** HIGH

## Summary

This phase creates a BenchmarkDotNet project that measures seven core operations (BuildTree, Search, Load, Save, SaveBatch, DeleteBatch, GetByFilter) at enterprise scale (100/200/500/1000 connections). The work has three distinct components: (1) a deterministic test data generator in Deskbridge.Core, (2) extraction of pure tree-building logic from the ViewModel layer into Deskbridge.Core, and (3) the benchmark project itself with parameterized benchmarks and committed baseline results.

BenchmarkDotNet 0.15.8 is the current stable release, explicitly supports .NET 10 via `RuntimeMoniker.Net10`, and is forward-compatible with the project's `net10.0-windows` TFM via its .NET Standard 2.0 target. The project's existing `JsonConnectionStore` and `ConnectionQueryService` are already benchmarkable with minimal test seams -- only `BuildTree()` requires architectural extraction to remove UI dependencies.

**Primary recommendation:** Use BenchmarkDotNet 0.15.8 with `[Params]` for connection counts, `[GlobalSetup]` for read-only benchmarks, `[IterationSetup]` for mutating benchmarks, and JSON+CSV exporters. Extract tree-building into a static `ConnectionTreeBuilder` class with pure record-based output types.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- D-01: Enterprise-realistic data shape -- 3-level nesting (Region > Environment > Role), realistic hostnames like `srv-prod-web-001`, ~5-10 groups per level, connections distributed unevenly across groups (some groups 50+, some 3)
- D-02: Fixed seed for deterministic output -- same input count always produces identical dataset
- D-03: Extract core tree-building logic from `ConnectionTreeViewModel.BuildTree()` into a pure static/core method in `Deskbridge.Core` that the ViewModel calls. Benchmark the pure method directly -- no UI dependencies
- D-04: Seven operations benchmarked: BuildTree, Search, Load, Save, SaveBatch, DeleteBatch, GetByFilter
- D-05: Each operation benchmarked at 100, 200, 500, and 1000 connection counts (parameterized benchmarks)
- D-06: Use BenchmarkDotNet's built-in JSON/CSV exporters for result artifacts
- D-07: Git-track the initial baseline run as the reference point. `.gitignore` subsequent runs
- D-08: Test data generator lives in `Deskbridge.Core` -- reusable by both `Deskbridge.Benchmarks` and `Deskbridge.Tests`
- D-09: Benchmark project at `tests/Deskbridge.Benchmarks/`. Dev-only, not shipped

### Claude's Discretion
- Exact BenchmarkDotNet configuration (warmup, iteration count, exporters)
- Test data generator class name and API surface
- How to organize benchmark classes (one per operation vs grouped by component)
- Exact hostname/group naming patterns in the generator

### Deferred Ideas (OUT OF SCOPE)
None
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PERF-04 | Developer can run BenchmarkDotNet regression baselines for BuildTree, Search, Load/Save at 100/200/500/1000 connections | Full BenchmarkDotNet 0.15.8 configuration with parameterized benchmarks, deterministic data generator, and committed baseline strategy |
</phase_requirements>

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Test data generation | Deskbridge.Core | -- | Generator reused by tests and benchmarks; no UI dependency |
| Tree-building extraction | Deskbridge.Core | Deskbridge (ViewModel) | Pure logic in Core, ViewModel maps output to ObservableCollection |
| Benchmark execution | tests/Deskbridge.Benchmarks | Deskbridge.Core | Benchmark project references Core; exercises services directly |
| Baseline storage | Git (tracked files) | -- | Initial results committed; subsequent runs gitignored |

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| BenchmarkDotNet | 0.15.8 | Performance measurement framework | Industry standard for .NET micro-benchmarks; statistical rigor, parameterized benchmarks, built-in exporters [VERIFIED: NuGet registry] |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| BenchmarkDotNet.Diagnostics.Windows | 0.15.8 | ETW/GC diagnostics | Optional -- add if memory profiling needed beyond MemoryDiagnoser |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| BenchmarkDotNet | Manual Stopwatch loops | No statistical analysis, warmup handling, or reproducibility guarantees |
| BenchmarkDotNet | NBench | Less maintained, smaller community, fewer exporters |

**Installation:**
```bash
dotnet add tests/Deskbridge.Benchmarks/Deskbridge.Benchmarks.csproj package BenchmarkDotNet --version 0.15.8
```

**Version verification:** BenchmarkDotNet 0.15.8 published 2025-11-30. Targets .NET Standard 2.0 + .NET 6.0 + .NET 8.0. Forward-compatible with .NET 10 (explicit `RuntimeMoniker.Net10` support added in 0.15.0 release). [VERIFIED: NuGet registry]

## Architecture Patterns

### System Architecture Diagram

```
[Test Data Generator]          [Benchmark Project]
  (Deskbridge.Core)              (Deskbridge.Benchmarks)
        |                               |
        | Generate(count, seed)         | [Params(100,200,500,1000)]
        v                               v
  (connections, groups)          [GlobalSetup / IterationSetup]
        |                               |
        +-------------------------------+
        |
        v
  +---[Operations Under Test]---+
  |                             |
  | ConnectionTreeBuilder.Build |  <-- extracted from ViewModel
  | ConnectionQueryService.Search |
  | ConnectionQueryService.GetByFilter |
  | JsonConnectionStore.Load    |
  | JsonConnectionStore.Save    |
  | JsonConnectionStore.SaveBatch |
  | JsonConnectionStore.DeleteBatch |
  |                             |
  +-----------------------------+
        |
        v
  [BenchmarkDotNet.Artifacts/]
  - results-*.json (gitignored)
  - results-*.csv  (gitignored)

  [baseline/]
  - initial-results.json (git-tracked)
```

### Files Modified by This Phase

This phase is NOT purely greenfield. In addition to new files, it modifies existing code:
- **`src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs`** -- `BuildTree()` refactored to delegate to `ConnectionTreeBuilder.Build()` and map results to ViewModels. The `SortSiblings()` and `AssignDepths()` internal helpers move to the builder.
- **`tests/Deskbridge.Tests/ViewModels/TreeDepthTests.cs`** -- 6 calls to `ConnectionTreeViewModel.AssignDepths()` via `InternalsVisibleTo`. These tests must be rewritten to test `ConnectionTreeBuilder` directly (the public Core API) since `AssignDepths` moves out of the ViewModel. [VERIFIED: grep found 6 calls in TreeDepthTests.cs]

### Recommended Project Structure
```
tests/
+-- Deskbridge.Benchmarks/
|   +-- Deskbridge.Benchmarks.csproj
|   +-- Program.cs                    # BenchmarkSwitcher entry point
|   +-- Config/
|   |   +-- BenchmarkConfig.cs        # Custom ManualConfig
|   +-- Benchmarks/
|   |   +-- TreeBuildBenchmarks.cs    # BuildTree parameterized
|   |   +-- SearchBenchmarks.cs       # Search parameterized
|   |   +-- StoreBenchmarks.cs        # Load, Save, SaveBatch, DeleteBatch
|   |   +-- FilterBenchmarks.cs       # GetByFilter parameterized
|   +-- baseline/
|   |   +-- .gitkeep                  # Initial results committed here
|   +-- .gitignore                    # Ignore BenchmarkDotNet.Artifacts/
+-- Deskbridge.Tests/
|   +-- (existing)
+-- uat/
    +-- (existing)

src/
+-- Deskbridge.Core/
|   +-- Services/
|   |   +-- ConnectionTreeBuilder.cs  # NEW: pure tree-building logic
|   |   +-- TestDataGenerator.cs      # NEW: deterministic data generation
|   +-- Models/
|       +-- TreeNode.cs               # NEW: pure tree result types
+-- Deskbridge/
    +-- ViewModels/
        +-- ConnectionTreeViewModel.cs  # MODIFIED: BuildTree() delegates to builder
```

### Pattern 1: Extracted Tree Builder (D-03)

**What:** Pure static tree-building logic extracted from `ConnectionTreeViewModel.BuildTree()` into `Deskbridge.Core`.
**When to use:** Any time tree structure needs to be computed without UI dependencies.

The current `BuildTree()` method has three UI entanglements:
1. Creates `GroupTreeItemViewModel` / `ConnectionTreeItemViewModel` (ViewModel types in UI project)
2. Calls `_credentialService.HasGroupCredentials()` (sets `HasCredentials` on group VM)
3. Returns `ObservableCollection<TreeItemViewModel>`

The extracted version uses pure data types:

```csharp
// Source: Designed from existing BuildTree() logic at ConnectionTreeViewModel.cs:419-556
// In Deskbridge.Core/Models/TreeNode.cs
namespace Deskbridge.Core.Models;

public abstract record TreeNode(Guid Id, string Name, int SortOrder, int Depth);

public sealed record GroupNode(
    Guid Id,
    string Name,
    int SortOrder,
    int Depth,
    Guid? ParentGroupId,
    IReadOnlyList<TreeNode> Children) : TreeNode(Id, Name, SortOrder, Depth);

public sealed record ConnectionNode(
    Guid Id,
    string Name,
    int SortOrder,
    int Depth,
    Guid? GroupId,
    string Hostname) : TreeNode(Id, Name, SortOrder, Depth);
```

```csharp
// In Deskbridge.Core/Services/ConnectionTreeBuilder.cs
namespace Deskbridge.Core.Services;

public static class ConnectionTreeBuilder
{
    /// <summary>
    /// Builds a sorted, nested tree from flat connection and group lists.
    /// Pure function: no UI dependencies, no credential lookups.
    /// Cycle detection promotes cyclic groups to root level.
    /// </summary>
    public static IReadOnlyList<TreeNode> Build(
        IReadOnlyList<ConnectionModel> connections,
        IReadOnlyList<ConnectionGroup> groups)
    {
        // 1. Build group lookup
        // 2. Detect cycles (same algorithm as existing)
        // 3. Nest groups into parents
        // 4. Place connections into groups or root
        // 5. Sort siblings by SortOrder then Name
        // 6. Assign depth values
        // Return immutable tree
    }
}
```

The ViewModel then becomes a thin mapper:
```csharp
// ConnectionTreeViewModel.BuildTree() after extraction:
var tree = ConnectionTreeBuilder.Build(connections, groups);
var rootItems = MapToViewModels(tree); // Maps TreeNode -> *TreeItemViewModel, applies HasCredentials
```

**Existing test migration:** `tests/Deskbridge.Tests/ViewModels/TreeDepthTests.cs` contains 6 calls to `ConnectionTreeViewModel.AssignDepths()` exposed via `InternalsVisibleTo("Deskbridge.Tests")`. When `AssignDepths` and sort logic move to `ConnectionTreeBuilder`, these tests MUST be rewritten to test the new builder's public API directly. The planner should include this as an explicit task within the tree-extraction wave, not defer it.

### Pattern 2: Deterministic Test Data Generator

**What:** Seeded random generator producing enterprise-realistic connection datasets.
**When to use:** Benchmarks and large-scale test scenarios.

```csharp
// In Deskbridge.Core/Services/TestDataGenerator.cs
namespace Deskbridge.Core.Services;

public static class TestDataGenerator
{
    /// <summary>
    /// Generates a deterministic dataset of connections and groups.
    /// Each connectionCount value with the same seed produces identical output.
    /// Group count scales as ~connectionCount / 10.
    /// </summary>
    public static (IReadOnlyList<ConnectionModel> Connections, IReadOnlyList<ConnectionGroup> Groups)
        Generate(int connectionCount, int seed = 42);
}
```

Key design decisions:
- **Independent datasets per N** -- N=100 is NOT a subset of N=1000. Each (count, seed) pair produces its own deterministic dataset. This matches BenchmarkDotNet `[Params]` semantics cleanly.
- **Group count scales as `~connectionCount / 10`** -- 100 connections => ~10 groups, 1000 => ~100 groups.
- **3-level nesting** -- Regions (3-5), Environments per region (2-4), Roles per environment (1-3).
- **Uneven distribution** -- Some leaf groups get 50+ connections, some get 2-3. Achieved via weighted random selection.
- **Realistic hostnames** -- Pattern: `srv-{region}-{env}-{role}-{NNN}` (e.g., `srv-prod-web-001`).

### Pattern 3: Parameterized Benchmarks with Correct Setup

**What:** BenchmarkDotNet parameterized benchmarks using appropriate setup strategies per operation type.
**When to use:** All benchmarks in this project.

**Critical distinction -- read-only vs mutating operations:**

| Operation | Mutates State? | Setup Strategy |
|-----------|---------------|----------------|
| BuildTree | No | `[GlobalSetup]` -- generate data once per param value |
| Search | No | `[GlobalSetup]` -- generate data once per param value |
| GetByFilter | No | `[GlobalSetup]` -- generate data once per param value |
| Load | No (reads file) | `[GlobalSetup]` -- write JSON file once; fresh store per benchmark call |
| Save | Yes | `[IterationSetup]` -- reseed store state before each iteration |
| SaveBatch | Yes | `[IterationSetup]` -- reseed store state before each iteration |
| DeleteBatch | Yes | `[IterationSetup]` -- reseed store state before each iteration |

**Critical: Benchmark method must only contain the operation being measured.** For mutating benchmarks, `[IterationSetup]` creates and loads a fresh store instance stored as a class field. The `[Benchmark]` method then calls only the single operation on that pre-loaded store. Do NOT create a new store or call `Load()` inside the benchmark method -- that conflates construction/loading cost with the operation cost.

```csharp
// Source: BenchmarkDotNet docs - setup-and-cleanup.md + parameterization.md
[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[CsvExporter]
public class StoreBenchmarks
{
    [Params(100, 200, 500, 1000)]
    public int ConnectionCount { get; set; }

    private string _tempDir = null!;
    private string _filePath = null!;
    private IReadOnlyList<ConnectionModel> _connections = null!;
    private IReadOnlyList<ConnectionGroup> _groups = null!;
    private JsonConnectionStore _store = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"deskbridge-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "connections.json");

        var (connections, groups) = TestDataGenerator.Generate(ConnectionCount);
        _connections = connections;
        _groups = groups;
    }

    [IterationSetup(Target = nameof(Save))]
    public void SetupForSave()
    {
        // Create fresh store seeded with full dataset
        _store = new JsonConnectionStore(_filePath);
        _store.Load();
        _store.SaveBatch(_connections, _groups);
    }

    [Benchmark]
    public void Save()
    {
        // Only the Save operation is timed -- store is pre-loaded in IterationSetup
        _store.Save(_connections[0]);
    }

    [IterationSetup(Target = nameof(DeleteBatch))]
    public void SetupForDelete()
    {
        // Create fresh store seeded with full dataset
        _store = new JsonConnectionStore(_filePath);
        _store.Load();
        _store.SaveBatch(_connections, _groups);
    }

    [Benchmark]
    public void DeleteBatch()
    {
        // Only the DeleteBatch operation is timed
        var idsToDelete = _connections.Take(ConnectionCount / 10).Select(c => c.Id);
        _store.DeleteBatch(idsToDelete, []);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
```

### Pattern 4: BenchmarkSwitcher Entry Point

```csharp
// Source: BenchmarkDotNet docs - how-to-run.md
// In Program.cs
using BenchmarkDotNet.Running;

// BenchmarkSwitcher allows running specific classes or all via CLI args
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// Run all:    dotnet run -c Release -- --filter '*'
// Run one:    dotnet run -c Release -- --filter '*TreeBuild*'
// List:       dotnet run -c Release -- --list flat
```

### Anti-Patterns to Avoid
- **Benchmarking in Debug mode:** BenchmarkDotNet requires Release configuration. Running in Debug produces misleading results and BDN warns/aborts.
- **Including setup work in benchmark body:** Mutating benchmarks must do `new + Load + seed` in `[IterationSetup]`, NOT in the `[Benchmark]` method. Otherwise you measure construction + loading + the actual operation, making results useless for isolating operation cost.
- **Shared mutable state across iterations:** Save/Delete benchmarks MUST reseed state via `[IterationSetup]`, not reuse modified state from previous iteration.
- **Benchmarking ViewModel directly:** The ViewModel depends on `ICredentialService`, `ObservableCollection`, and WPF binding infrastructure. Benchmark the extracted pure logic only.
- **Non-deterministic data:** Using `Random()` without a fixed seed makes results incomparable across runs. Always pass explicit seed.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Statistical benchmarking | Manual Stopwatch loops | BenchmarkDotNet | Handles warmup, outlier removal, GC collection between runs, statistical significance |
| Result export/comparison | Custom CSV formatting | BenchmarkDotNet JSON/CSV exporters | Standard format, tooling ecosystem (benchview, etc.) |
| Memory measurement | `GC.GetTotalMemory()` | `[MemoryDiagnoser]` | Tracks per-benchmark allocations accurately including Gen0/1/2 collections |
| Deterministic random | Custom PRNG | `System.Random(seed)` | Well-tested, sufficient for data generation, deterministic with seed |

**Key insight:** BenchmarkDotNet's value is statistical rigor -- it handles warmup iterations, process isolation (forks a new process per benchmark), GC stabilization, and outlier detection. A hand-rolled timing loop misses all of these, producing numbers that look precise but aren't reproducible.

## Common Pitfalls

### Pitfall 1: IterationSetup vs GlobalSetup Confusion
**What goes wrong:** Using `[GlobalSetup]` for mutating benchmarks means iteration N+1 measures a different data shape than iteration 1 (e.g., after Delete, there are fewer items).
**Why it happens:** GlobalSetup runs once per parameter value; IterationSetup runs before each measured invocation.
**How to avoid:** Use `[IterationSetup(Target = nameof(MethodName))]` for any benchmark that modifies in-memory or on-disk state.
**Warning signs:** Results that drift or have unusually high variance within a single parameter value.

### Pitfall 2: File I/O Benchmark Variance
**What goes wrong:** Disk benchmarks show high variance due to OS file caching, antivirus scanning, or other disk activity.
**Why it happens:** `PersistAtomically()` does real file writes; OS buffers/caches affect timing.
**How to avoid:** Accept moderate variance for I/O benchmarks (relative comparisons still valid). Use `[MinIterationCount(15)]` and `[MaxIterationCount(30)]` to get sufficient samples. Run on SSD.
**Warning signs:** Standard deviation > 20% of mean for file operations.

### Pitfall 3: Directory.Build.props Inheritance
**What goes wrong:** Benchmark project inherits `UseWPF=true` and `TreatWarningsAsErrors=true` from Directory.Build.props. BenchmarkDotNet may generate temporary code with warnings.
**Why it happens:** The project sits under the solution root and inherits shared build properties.
**How to avoid:** Keep `TreatWarningsAsErrors` inherited (it's fine -- BDN-generated code is warning-free). Do NOT try to override `TargetFramework` to `net10.0` (without `-windows`) because Deskbridge.Core transitively depends on WPF-UI which requires the windows TFM.
**Warning signs:** Build failures referencing WPF-UI or Wpf.Ui namespace.

### Pitfall 4: GetByFilter Overlaps with Search
**What goes wrong:** `GetByFilter()` internally calls `Search()` when `filter.SearchText` is set. Benchmarking GetByFilter with a SearchText measures Search performance, not filter-specific logic.
**Why it happens:** The implementation delegates to Search for the text-matching portion.
**How to avoid:** Benchmark GetByFilter with non-text filters (Tag, Protocol, GroupId) to measure its independent cost. Optionally also benchmark with SearchText to capture the combined path, but label it clearly.
**Warning signs:** GetByFilter results being identical to Search results.

### Pitfall 5: Forgetting to Add Project to Solution
**What goes wrong:** Project builds standalone but IDE doesn't show it; `dotnet build` at solution level skips it.
**Why it happens:** Creating a `.csproj` doesn't automatically register it in `Deskbridge.sln`.
**How to avoid:** Run `dotnet sln add tests/Deskbridge.Benchmarks/Deskbridge.Benchmarks.csproj` after creating the project.
**Warning signs:** "Project not found" errors when running from solution context.

### Pitfall 6: Existing Test Breakage from Tree Extraction
**What goes wrong:** `tests/Deskbridge.Tests/ViewModels/TreeDepthTests.cs` has 6 calls to `ConnectionTreeViewModel.AssignDepths()` via `InternalsVisibleTo`. After extraction, `AssignDepths` no longer exists on the ViewModel.
**Why it happens:** D-03 moves tree-building logic (including depth assignment and sorting) from the ViewModel to `ConnectionTreeBuilder` in Core.
**How to avoid:** Rewrite `TreeDepthTests.cs` to test `ConnectionTreeBuilder` directly using its public API. Do this in the same wave as the extraction, not deferred.
**Warning signs:** `Deskbridge.Tests` compilation failure after tree extraction -- `AssignDepths` method not found.

## Code Examples

### BenchmarkDotNet Project File (.csproj)

```xml
<!-- Source: BenchmarkDotNet docs + project conventions -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.15.8" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Deskbridge.Core\Deskbridge.Core.csproj" />
  </ItemGroup>
</Project>
```

Note: `TargetFramework`, `UseWPF`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` all inherited from Directory.Build.props. `OutputType=Exe` is required by BenchmarkDotNet (process forking).

### Custom Config with Appropriate Exporters

```csharp
// Source: BenchmarkDotNet docs - configs/jobs.md
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;

namespace Deskbridge.Benchmarks.Config;

public class DeskbridgeBenchmarkConfig : ManualConfig
{
    public DeskbridgeBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(3)
            .WithIterationCount(10)
            .WithId("Default"));

        AddDiagnoser(MemoryDiagnoser.Default);

        AddExporter(JsonExporter.Full);
        AddExporter(CsvExporter.Default);
        AddExporter(MarkdownExporter.GitHub);

        AddValidator(JitOptimizationsValidator.FailOnError);
    }
}
```

### Read-Only Benchmark (BuildTree)

```csharp
// Source: BenchmarkDotNet docs + project architecture
using BenchmarkDotNet.Attributes;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Benchmarks.Benchmarks;

[Config(typeof(DeskbridgeBenchmarkConfig))]
public class TreeBuildBenchmarks
{
    [Params(100, 200, 500, 1000)]
    public int ConnectionCount { get; set; }

    private IReadOnlyList<ConnectionModel> _connections = null!;
    private IReadOnlyList<ConnectionGroup> _groups = null!;

    [GlobalSetup]
    public void Setup()
    {
        var (connections, groups) = TestDataGenerator.Generate(ConnectionCount);
        _connections = connections;
        _groups = groups;
    }

    [Benchmark]
    public IReadOnlyList<TreeNode> BuildTree()
    {
        return ConnectionTreeBuilder.Build(_connections, _groups);
    }
}
```

### Mutating Benchmark (Store Operations)

```csharp
using BenchmarkDotNet.Attributes;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Benchmarks.Benchmarks;

[Config(typeof(DeskbridgeBenchmarkConfig))]
public class StoreBenchmarks
{
    [Params(100, 200, 500, 1000)]
    public int ConnectionCount { get; set; }

    private string _tempDir = null!;
    private string _filePath = null!;
    private IReadOnlyList<ConnectionModel> _connections = null!;
    private IReadOnlyList<ConnectionGroup> _groups = null!;
    private JsonConnectionStore _store = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"deskbridge-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "connections.json");

        var (connections, groups) = TestDataGenerator.Generate(ConnectionCount);
        _connections = connections;
        _groups = groups;
    }

    // --- Load: read-only, uses pre-written file ---

    [IterationSetup(Target = nameof(Load))]
    public void SetupForLoad()
    {
        // Write file with full dataset; Load benchmark reads it
        var seedStore = new JsonConnectionStore(_filePath);
        seedStore.Load();
        seedStore.SaveBatch(_connections, _groups);
    }

    [Benchmark]
    public void Load()
    {
        // Only Load() is timed -- file was pre-written in IterationSetup
        var store = new JsonConnectionStore(_filePath);
        store.Load();
    }

    // --- Save: mutating, single connection upsert ---

    [IterationSetup(Target = nameof(Save))]
    public void SetupForSave()
    {
        _store = new JsonConnectionStore(_filePath);
        _store.Load();
        _store.SaveBatch(_connections, _groups);
    }

    [Benchmark]
    public void Save()
    {
        // Only Save() is timed -- store is pre-loaded
        _store.Save(_connections[0]);
    }

    // --- SaveBatch: mutating, full batch write ---

    [IterationSetup(Target = nameof(SaveBatch))]
    public void SetupForSaveBatch()
    {
        _store = new JsonConnectionStore(_filePath);
        _store.Load();
    }

    [Benchmark]
    public void SaveBatch()
    {
        _store.SaveBatch(_connections, _groups);
    }

    // --- DeleteBatch: mutating, 10% deletion ---

    [IterationSetup(Target = nameof(DeleteBatch))]
    public void SetupForDelete()
    {
        _store = new JsonConnectionStore(_filePath);
        _store.Load();
        _store.SaveBatch(_connections, _groups);
    }

    [Benchmark]
    public void DeleteBatch()
    {
        var idsToDelete = _connections.Take(ConnectionCount / 10).Select(c => c.Id);
        _store.DeleteBatch(idsToDelete, []);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
```

### .gitignore for Benchmark Project

```gitignore
# BenchmarkDotNet artifacts (generated on each run)
BenchmarkDotNet.Artifacts/

# Keep baseline/ tracked (committed initial results)
!baseline/
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| BenchmarkDotNet 0.13.x | BenchmarkDotNet 0.15.8 | Nov 2025 | .NET 10 RuntimeMoniker, improved memory diagnoser, better parameterization |
| `[SimpleJob]` for quick runs | `[ShortRunJob]` or custom config | 0.14+ | More control over iteration counts vs statistical accuracy |
| Manual result comparison | JSON export + diff | Long-standing | Machine-readable baselines enable CI regression detection |

**Deprecated/outdated:**
- BenchmarkDotNet 0.13.x: Does not support .NET 10 RuntimeMoniker. Use 0.15.8.
- `IParam` interface for complex parameters: Deprecated since 0.11.0. Use `ToString()` override on parameter objects instead.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | BenchmarkDotNet 0.15.8 runs cleanly on `net10.0-windows` with `UseWPF=true` inherited from Directory.Build.props | Standard Stack | Build failure -- would need to override TFM or isolate from Directory.Build.props. Planner should add a verification step (build + dry-run a hello-world benchmark) as the first task. |
| A2 | `Directory.Delete(_tempDir, recursive: true)` in GlobalCleanup is sufficient (no file locking from BDN forked processes) | Code Examples | Temp files leak on disk -- harmless but messy |

## Open Questions

1. **Benchmark class organization**
   - What we know: Seven operations across two services (store + query/tree)
   - What's unclear: Whether to group by component (StoreBenchmarks, QueryBenchmarks, TreeBenchmarks) or one class per operation
   - Recommendation: Group by component -- 3-4 classes is manageable, reduces boilerplate. StoreBenchmarks (Load/Save/SaveBatch/DeleteBatch), SearchBenchmarks (Search), FilterBenchmarks (GetByFilter), TreeBuildBenchmarks (BuildTree).

2. **Baseline comparison workflow**
   - What we know: Initial results committed to `baseline/` directory
   - What's unclear: Whether to build an automated comparison script or rely on manual diff
   - Recommendation: Manual comparison for Phase 20 (developer reads the committed JSON, compares visually or with `diff`). Automated regression detection is a Phase 21 concern.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | Build + Run | Yes | 10.0.203 | -- |
| BenchmarkDotNet 0.15.8 | Benchmark framework | Yes (NuGet) | 0.15.8 | -- |
| SSD storage | Reliable I/O benchmarks | Likely | -- | Accept higher variance on HDD |

**Missing dependencies with no fallback:** None.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit.v3 (existing in Deskbridge.Tests) |
| Config file | `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` |
| Quick run command | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~TestDataGenerator" -c Release --no-build` |
| Full suite command | `dotnet test tests/Deskbridge.Tests -c Release` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PERF-04a | TestDataGenerator produces deterministic output (same seed = same data) | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~TestDataGenerator" -c Release` | No -- Wave 0 |
| PERF-04b | TestDataGenerator produces correct count at all sizes | unit | Same as above | No -- Wave 0 |
| PERF-04c | TestDataGenerator produces 3-level nesting | unit | Same as above | No -- Wave 0 |
| PERF-04d | ConnectionTreeBuilder.Build produces correct tree structure | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~TreeBuilder" -c Release` | No -- Wave 0 |
| PERF-04e | ConnectionTreeBuilder.Build detects cycles | unit | Same as above | No -- Wave 0 |
| PERF-04f | Benchmark project builds and runs | smoke | `dotnet run --project tests/Deskbridge.Benchmarks -c Release -- --filter '*TreeBuild*' --job dry` | No -- Wave 0 |
| PERF-04g | Existing TreeDepthTests pass after migration to builder API | regression | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~TreeDepth" -c Release` | Yes -- needs rewrite |

### Sampling Rate
- **Per task commit:** `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~TestDataGenerator OR FullyQualifiedName~TreeBuilder OR FullyQualifiedName~TreeDepth" -c Release`
- **Per wave merge:** `dotnet test tests/Deskbridge.Tests -c Release`
- **Phase gate:** Full test suite green + benchmark dry-run completes without error

### Wave 0 Gaps
- [ ] `tests/Deskbridge.Tests/Services/TestDataGeneratorTests.cs` -- covers PERF-04a/b/c
- [ ] `tests/Deskbridge.Tests/Services/ConnectionTreeBuilderTests.cs` -- covers PERF-04d/e
- [ ] Rewrite `tests/Deskbridge.Tests/ViewModels/TreeDepthTests.cs` -- covers PERF-04g (test ConnectionTreeBuilder public API instead of VM internals)
- [ ] Benchmark project scaffold (`tests/Deskbridge.Benchmarks/`) -- covers PERF-04f

## Security Domain

This phase has no security surface -- it creates dev-only benchmark infrastructure that does not handle credentials, user input, or network communication. No ASVS categories apply.

## Sources

### Primary (HIGH confidence)
- [Context7: /dotnet/benchmarkdotnet] -- Parameterized benchmarks, setup/cleanup, exporters, runner patterns
- [NuGet: BenchmarkDotNet 0.15.8](https://www.nuget.org/packages/benchmarkdotnet/) -- Version, publish date, TFMs confirmed

### Secondary (MEDIUM confidence)
- [BenchmarkDotNet in .NET 10 blog post](https://jkrussell.dev/blog/benchmarkdotnet-dotnet-10-benchmarks/) -- Confirms .NET 10 usage patterns
- [GitHub: dotnet/BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) -- RuntimeMoniker.Net10 support in 0.15.0+

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- BenchmarkDotNet 0.15.8 verified on NuGet, .NET 10 support confirmed in release notes
- Architecture: HIGH -- Extraction pattern directly informed by reading existing BuildTree() code (lines 419-556)
- Pitfalls: HIGH -- Derived from understanding actual code (mutating store methods, Directory.Build.props inheritance, existing test dependencies)

**Research date:** 2026-04-27
**Valid until:** 2026-05-27 (stable domain, unlikely to change)
