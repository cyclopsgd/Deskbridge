---
phase: 21
plan: 03
subsystem: core-services / app-startup
tags: [perf-03, async, startup, di, tdd]
requires:
  - 21-02
  - PERF-03
provides:
  - JsonConnectionStore.LoadAsync (Task.Run wrapper)
  - IConnectionStore.LoadAsync interface member
  - async App.OnStartup with awaited LoadAsync
affects:
  - src/Deskbridge.Core/Interfaces/IConnectionStore.cs
  - src/Deskbridge.Core/Services/JsonConnectionStore.cs
  - src/Deskbridge/App.xaml.cs
  - tests/Deskbridge.Tests/Services/JsonConnectionStoreAsyncLoadTests.cs (new)
  - tests/Deskbridge.Tests/Logging/SerilogConfigTests.cs (test fix for signature change)
tech-stack:
  added: []
  patterns:
    - "Task.Run dispatch shim for sync I/O wrappers"
    - "async void OnStartup with try/catch + Log.Fatal (WPF documented pattern)"
    - "Post-DI initialization step (Load moved out of factory lambda)"
key-files:
  created:
    - tests/Deskbridge.Tests/Services/JsonConnectionStoreAsyncLoadTests.cs
  modified:
    - src/Deskbridge.Core/Interfaces/IConnectionStore.cs
    - src/Deskbridge.Core/Services/JsonConnectionStore.cs
    - src/Deskbridge/App.xaml.cs
    - tests/Deskbridge.Tests/Logging/SerilogConfigTests.cs
decisions:
  - "Option A (Task.Run wrapping sync Load) chosen over native async I/O — smaller diff, no behavior duplication, equivalent threadpool dispatch"
  - "Sync Load() retained for benchmarks/tests per D-06 — not [Obsolete], not removed"
  - "Await placed AFTER BuildServiceProvider() and BEFORE MigrateFromTermsrv to preserve existing post-load order (D-04/D-05/D-06)"
  - "DI factory simplified to `sp => new JsonConnectionStore()` — load moves entirely out of DI graph construction"
  - "RED test 3 (LoadAsync_DispatchesViaTaskRun) verifies Task.Run dispatch via task.IsCompleted == false at the call site (synchronously-returned hot Task)"
metrics:
  duration: 4min
  completed: 2026-05-02
  tasks: 2
  files: 5
  commits: 2
---

# Phase 21 Plan 03: Async Startup Load (LoadAsync) Summary

PERF-03 — JsonConnectionStore now exposes `Task LoadAsync() => Task.Run(Load)` and App.OnStartup is `async void` with an awaited LoadAsync that runs after BuildServiceProvider and before MigrateFromTermsrv. File I/O + JSON parse no longer block the UI thread during DI graph construction. Sync `Load()` retained intact for benchmarks/tests per D-06.

## Implementation

### LoadAsync wrapper (Deskbridge.Core)

Added next to the existing sync `Load()` method:

```csharp
/// <summary>
/// Phase 21 (PERF-03): async load wrapper. Dispatches sync Load() to the threadpool
/// via Task.Run so file I/O + JSON parse run off the calling (UI) thread. Sync Load()
/// remains the canonical implementation; this is purely a dispatch shim per D-06.
/// </summary>
public Task LoadAsync() => Task.Run(Load);
```

`IConnectionStore` extended with `Task LoadAsync();` immediately after `void Load();` so the sync/async pair stays grouped. No XML doc on the interface member (matches existing convention).

### App.OnStartup async refactor

Three changes in `src/Deskbridge/App.xaml.cs`:

1. Signature: `protected override async void OnStartup(StartupEventArgs e)`.
2. New try/catch awaiting `connectionStore.LoadAsync()` between `BuildServiceProvider()` and the existing `MigrateFromTermsrv` block. `Log.Fatal` on exception (Serilog logger is configured at line 49 BEFORE the await, so this catch path is safe).
3. DI factory simplified to `services.AddSingleton<IConnectionStore>(sp => new JsonConnectionStore());` — no `Load()` call inside the factory.

### Order Invariant Preserved (D-04/D-05/D-06)

```
base.OnStartup
CrashHandler.InstallDispatcherHook  (line 41)
SerilogSetup.Configure              (line 49)
ApplicationThemeManager.Apply       (line 51)
ApplicationAccentColorManager.Apply (line 58)
BuildServiceProvider                (line 65)
==> await store.LoadAsync()         (line 75)  *** NEW ***
MigrateFromTermsrv                  (line 90)
ConnectionPipeline stages           (lines 94-104)
Eager singletons                    (lines 107-125)
MainWindow.Show                     (line 128)
AppLockController + lock startup    (lines 130-148)
```

The existing `CrashHandler.InstallDispatcherHook` continues to catch dispatcher-level exceptions outside the new try/catch — async-void caveat handled per RESEARCH.md §5.2.

### Test class structure

`tests/Deskbridge.Tests/Services/JsonConnectionStoreAsyncLoadTests.cs` (new): `public sealed class : IDisposable` with the standard temp-dir IDisposable scaffold copied from the sibling `JsonConnectionStoreTests.cs`. Three `[Fact]` methods:

1. `Save_ThenLoadAsync_RoundtripsConnection` — Save via existing sync `Save`, construct a fresh store, `await LoadAsync`, assert the connection round-trips.
2. `LoadAsync_MissingFile_StartsWithEmptyStore` — Construct against a non-existent path, `await LoadAsync`, assert empty + no exception (delegates to sync `Load`'s missing-file fallback).
3. `LoadAsync_DispatchesViaTaskRun` — Capture the Task synchronously after invocation, assert `task.IsCompleted == false`. This is the proof of off-thread dispatch: `Task.Run` schedules to the threadpool, so the synchronously-returned hot Task has not completed at the call site even for trivial files. Then `await task` and assert the data is loaded.

## TDD Commits

- **RED** (`5776423`): `test(21-03): add failing LoadAsync tests for PERF-03 (RED)` — interface change + 3 [Fact] methods. Build fails with CS0535: `JsonConnectionStore` does not implement `IConnectionStore.LoadAsync`.
- **GREEN** (`a797205`): `feat(21-03): implement LoadAsync + async OnStartup for off-thread JSON load (GREEN)` — impl + DI factory simplification + async OnStartup + Serilog test signature fix. All 3 new tests pass; full suite 682/682 (3 env-gated RDP smoke skipped).

## TDD Gate Compliance

- RED gate commit (`test(21-03): … RED`): present at `5776423`.
- GREEN gate commit (`feat(21-03): … GREEN`): present at `a797205`, after RED.
- No REFACTOR commit needed — implementation is a one-line `Task.Run(Load)` and a small structural insert; nothing to clean up.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated SerilogConfigTests source-string match for new OnStartup signature**

- **Found during:** Task 2 GREEN — full test suite run.
- **Issue:** `SerilogConfigTests.App_OnStartup_InstallsDispatcherHookBeforeShowingMainWindow` reads `App.xaml.cs` source and runs `IndexOf("protected override void OnStartup", ...)`. Changing the signature to `async void` made that lookup return -1, which then propagated to `IndexOf(value, startIndex: -1, ...)` and threw `ArgumentOutOfRangeException`.
- **Fix:** Prefer-async lookup with sync fallback in the test:
  ```csharp
  var onStartupIdx = appCs.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
  if (onStartupIdx < 0) onStartupIdx = appCs.IndexOf("protected override void OnStartup", StringComparison.Ordinal);
  ```
  This keeps the test green for both legacy and new signatures and preserves its original intent (dispatcher-hook-before-Show ordering invariant).
- **Files modified:** `tests/Deskbridge.Tests/Logging/SerilogConfigTests.cs`
- **Commit:** `a797205` (bundled with GREEN)

### Auth Gates

None.

### Pre-existing flake observed (not fixed — out of scope)

The first full-suite run showed `CrashHandlerTests.OnUnobservedTask_LogsErrorAndSetsObserved` failing with 4 events in the in-memory sink instead of 1. Inspection of timestamps showed the extra events were from concurrent tests (`UpdateService.CheckForUpdatesAsync`, `ContentDialogHost is null`) leaking through the static `Log.Logger`. The test re-ran clean on the second invocation — pre-existing race in shared-static-logger test setup, not caused by this plan's changes. Not fixed; out of scope per the executor's scope-boundary rule.

## Verification

- `dotnet test … --filter-method "Deskbridge.Tests.Services.JsonConnectionStoreAsyncLoadTests.*"` → **3/3 Passed**.
- `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj` (full suite) → **682 Passed, 0 Failed, 3 Skipped**.
- `dotnet build src/Deskbridge/Deskbridge.csproj -c Debug` → **0 Warnings, 0 Errors**.
- `dotnet build tests/Deskbridge.Benchmarks/Deskbridge.Benchmarks.csproj -c Release` → **0 Warnings, 0 Errors** (D-06 invariant: sync `Load` still callable).
- `git log --oneline -3`:
  ```
  a797205 feat(21-03): implement LoadAsync + async OnStartup for off-thread JSON load (GREEN)
  5776423 test(21-03): add failing LoadAsync tests for PERF-03 (RED)
  f7a1b86 docs(state): record Phase 21 planning complete (4 plans)
  ```

## Self-Check: PASSED

- FOUND: `tests/Deskbridge.Tests/Services/JsonConnectionStoreAsyncLoadTests.cs`
- FOUND: `src/Deskbridge.Core/Interfaces/IConnectionStore.cs` (modified — `Task LoadAsync()` at line 18)
- FOUND: `src/Deskbridge.Core/Services/JsonConnectionStore.cs` (modified — `public Task LoadAsync() => Task.Run(Load);` at line 72)
- FOUND: `src/Deskbridge/App.xaml.cs` (modified — `async void OnStartup` at line 35; await at line 75; simplified factory at line 218)
- FOUND: `tests/Deskbridge.Tests/Logging/SerilogConfigTests.cs` (modified — async-signature support around line 76)
- FOUND commit: `5776423` (RED)
- FOUND commit: `a797205` (GREEN)
