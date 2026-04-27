---
phase: 20-performance-baselines
reviewed: 2026-04-27T18:45:00Z
depth: standard
files_reviewed: 16
files_reviewed_list:
  - src/Deskbridge.Core/Models/TreeNode.cs
  - src/Deskbridge.Core/Services/ConnectionTreeBuilder.cs
  - tests/Deskbridge.Tests/Services/ConnectionTreeBuilderTests.cs
  - src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs
  - tests/Deskbridge.Tests/ViewModels/TreeDepthTests.cs
  - src/Deskbridge.Core/Services/TestDataGenerator.cs
  - tests/Deskbridge.Tests/Services/TestDataGeneratorTests.cs
  - tests/Deskbridge.Benchmarks/Deskbridge.Benchmarks.csproj
  - tests/Deskbridge.Benchmarks/Program.cs
  - tests/Deskbridge.Benchmarks/Config/BenchmarkConfig.cs
  - tests/Deskbridge.Benchmarks/Benchmarks/TreeBuildBenchmarks.cs
  - tests/Deskbridge.Benchmarks/Benchmarks/SearchBenchmarks.cs
  - tests/Deskbridge.Benchmarks/Benchmarks/QueryBenchmarks.cs
  - tests/Deskbridge.Benchmarks/Benchmarks/StoreBenchmarks.cs
  - Deskbridge.sln
  - Directory.Packages.props
findings:
  critical: 0
  warning: 3
  info: 2
  total: 5
status: issues_found
---

# Phase 20: Code Review Report

**Reviewed:** 2026-04-27T18:45:00Z
**Depth:** standard
**Files Reviewed:** 16
**Status:** issues_found

## Summary

Phase 20 introduces a benchmark harness (Deskbridge.Benchmarks), a deterministic test data generator (TestDataGenerator), and extracts the pure tree-building logic from ConnectionTreeViewModel into the static ConnectionTreeBuilder class with new TreeNode record types. Unit tests cover tree depth, sorting, and cycle detection. The solution file and Directory.Packages.props are updated to include BenchmarkDotNet 0.15.8.

Overall the code is well-structured. Three warnings were found: a potential infinite loop in TestDataGenerator's surplus-removal logic for small connection counts, a search benchmark that measures unintended code paths due to a hostname pattern mismatch, and a silently swallowed exception in ConnectionTreeViewModel. Two info-level items flag dead code.

## Warnings

### WR-01: TestDataGenerator surplus-removal loop can infinite-loop for small connectionCount

**File:** `src/Deskbridge.Core/Services/TestDataGenerator.cs:126-138`
**Issue:** When `connectionCount < leafGroups.Count`, every allocation is `Math.Max(1, ...)` = 1, so `allocated >= leafGroups.Count > connectionCount` making `diff < 0`. The surplus-removal loop (lines 129-137) only decrements allocations where `allocations[i] > 1`, but every allocation is exactly 1, so the condition is never met. The index `i` wraps around via `if (i < 0) i = leafGroups.Count - 1` but `diff` never changes, producing an infinite loop. This is reachable for any `connectionCount` below the leaf group count (e.g., `connectionCount = 1` with 3 leaf groups).
**Fix:** Add a guard to break when no allocation can be reduced:
```csharp
else if (diff < 0)
{
    // Remove excess from groups with more than 1 connection
    int passes = 0;
    for (int i = leafGroups.Count - 1; diff < 0; i--)
    {
        if (i < 0)
        {
            // Completed one full pass without progress -- all allocations are 1
            if (passes == 0) break;
            i = leafGroups.Count - 1;
            passes = 0;
        }
        if (allocations[i] > 1)
        {
            allocations[i]--;
            diff++;
            passes++;
        }
    }
}
```
Alternatively, clamp the minimum `connectionCount` at the top of `Generate()`:
```csharp
connectionCount = Math.Max(connectionCount, leafGroups.Count);
```

### WR-02: SearchBenchmarks search term never matches via Contains -- measures wrong code path

**File:** `tests/Deskbridge.Benchmarks/Benchmarks/SearchBenchmarks.cs:27`
**Issue:** The benchmark searches for `"srv-prod-web"` but TestDataGenerator produces hostnames in the pattern `srv-{region}-{env}-{role}-NNN` (e.g. `srv-useast-prod-web-001`). The region segment always separates `srv-` from `prod`, so `String.Contains("srv-prod-web")` is false for every connection. All matches fall through to the subsequence path in `ConnectionQueryService.CalculateScore` (lines 88-94), which is the slowest matching strategy. The benchmark measures worst-case subsequence matching for 100% of connections rather than the typical `Contains` hit path that real user searches exercise.
**Fix:** Use a search term that matches the actual hostname pattern:
```csharp
[Benchmark]
public IReadOnlyList<ConnectionModel> Search()
{
    // Matches via Contains on hostnames like "srv-useast-prod-web-001"
    return _queryService.Search("prod-web");
}
```

### WR-03: Empty catch block silently swallows credential deletion failure

**File:** `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs:165`
**Issue:** `ClearQuickPassword` wraps `_credentialService.DeleteForConnection(model)` in a bare `try { ... } catch { }` with no logging. Credential store failures (e.g. Windows Credential Manager access denied) are silently swallowed. Compare `SaveQuickPassword` at line 617-620 which correctly logs the same category of failure.
**Fix:**
```csharp
try { _credentialService.DeleteForConnection(model); }
catch (Exception ex)
{
    Serilog.Log.Error(ex, "Failed to delete credentials for connection {ConnectionId} via quick-properties", model.Id);
}
```

## Info

### IN-01: Unused rng parameter in ComputeLevelCounts

**File:** `src/Deskbridge.Core/Services/TestDataGenerator.cs:213-214`
**Issue:** `ComputeLevelCounts` accepts a `Random rng` parameter but never uses it. The method is purely deterministic (brute-force iteration over small ranges). The parameter was likely intended for future randomization but currently adds noise to the signature.
**Fix:** Remove the `rng` parameter from the method signature and the call site at line 28:
```csharp
private static (int Regions, int EnvsPerRegion, int RolesPerEnv) ComputeLevelCounts(
    int targetTotalGroups)
```

### IN-02: Dead branch -- region name disambiguation is unreachable

**File:** `src/Deskbridge.Core/Services/TestDataGenerator.cs:41-42`
**Issue:** The condition `if (r >= RegionNames.Length)` appends a numeric suffix to avoid duplicate region names, but `ComputeLevelCounts` constrains `regionCount` to `Math.Min(5, RegionNames.Length)` (line 224), and `RegionNames.Length` is 5. The loop runs `r` from 0 to `regionCount - 1`, so `r` is always < 5 = `RegionNames.Length`. The disambiguation branch is unreachable dead code.
**Fix:** Either remove the branch (if region count will never exceed the array size) or add a comment explaining it's a defensive guard for future array changes.

---

_Reviewed: 2026-04-27T18:45:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
