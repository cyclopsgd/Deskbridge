---
phase: 21
slug: performance-optimizations
status: approved
nyquist_compliant: true
wave_0_complete: true
created: 2026-05-02
updated: 2026-04-30
---

# Phase 21 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution. Filled by gsd-planner during planning. See `21-RESEARCH.md` §"Validation Architecture" for the source contract.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit (tests/Deskbridge.Tests; **note**: there is no tests/Deskbridge.Core.Tests project — Core service tests live in Deskbridge.Tests per PATTERNS.md) + BenchmarkDotNet (tests/Deskbridge.Benchmarks, Phase 20) |
| **Config file** | none — frameworks already installed |
| **Quick run command** | `dotnet test tests/Deskbridge.Tests --no-restore` |
| **Full suite command** | `dotnet test --no-restore` |
| **Estimated runtime** | ~30s for unit tests; BenchmarkDotNet ~3-8min on demand |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build` + targeted unit test (`dotnet test --filter "FullyQualifiedName~{TestClass}"`)
- **After every plan wave:** Run `dotnet test --no-restore` (full unit suite)
- **Before `/gsd-verify-work`:** Full suite green + BenchmarkDotNet regression vs Phase 20 baseline
- **Max feedback latency:** 30 seconds for unit tests

---

## Plan / Wave Map

| Plan | Wave | Depends on | Type | Files |
|------|------|------------|------|-------|
| 21-01 | 1 | — | execute | src/Deskbridge/Views/ConnectionTreeControl.xaml |
| 21-02 | 1 | — | tdd | IDebouncer + DispatcherTimerDebouncer + ConnectionTreeViewModel + App.xaml.cs (DI) + test files |
| 21-03 | 2 | 21-02 | tdd | IConnectionStore + JsonConnectionStore + App.xaml.cs (OnStartup async) + test file |
| 21-04 | 3 | 21-01, 21-02, 21-03 | execute (non-autonomous) | benchmark comparison + UAT report |

**Wave 1** = 21-01 (XAML polish) + 21-02 (search debounce TDD) — independent file sets, can run in parallel.
**Wave 2** = 21-03 (async load TDD) — sequenced after 21-02 because both touch `App.xaml.cs`.
**Wave 3** = 21-04 (validation) — sequenced after all implementation plans.

---

## Per-Task Verification Map

> Filled by planner from PLAN.md acceptance_criteria. One row per atomic task.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 21-01 / T1 (ScrollUnit) | 21-01 | 1 | PERF-01 | T-21-01 | N/A (rendering) | build smoke | `dotnet build src/Deskbridge/Deskbridge.csproj -c Debug` | src/Deskbridge/Views/ConnectionTreeControl.xaml | ⬜ pending |
| 21-01 / T2 (Badge) | 21-01 | 1 | PERF-05 | T-21-07 | N/A (rendering) | build smoke | `dotnet build src/Deskbridge/Deskbridge.csproj -c Debug` | src/Deskbridge/Views/ConnectionTreeControl.xaml | ⬜ pending |
| 21-02 / T1 (RED) | 21-02 | 1 | PERF-02 | T-21-02, T-21-03 | timing only | xUnit (RED build fail) | `dotnet build tests/Deskbridge.Tests/Deskbridge.Tests.csproj -c Debug` (must FAIL with CS1729/CS7036) | src/Deskbridge.Core/Interfaces/IDebouncer.cs, tests/Deskbridge.Tests/Fakes/FakeDebouncer.cs, tests/Deskbridge.Tests/ViewModels/ConnectionTreeSearchDebounceTests.cs | ⬜ pending |
| 21-02 / T2 (GREEN) | 21-02 | 1 | PERF-02 | T-21-02, T-21-03 | timing only | xUnit (4 tests pass) | `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj --filter "FullyQualifiedName~ConnectionTreeSearchDebounceTests" --no-restore` | src/Deskbridge/Services/DispatcherTimerDebouncer.cs, src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs, src/Deskbridge/App.xaml.cs | ⬜ pending |
| 21-03 / T1 (RED) | 21-03 | 2 | PERF-03 | T-21-04, T-21-05, T-21-06 | file I/O reuse, atomic field write, async-void try/catch | xUnit (RED build fail) | `dotnet build tests/Deskbridge.Tests/Deskbridge.Tests.csproj -c Debug` (must FAIL with CS0535) | src/Deskbridge.Core/Interfaces/IConnectionStore.cs, tests/Deskbridge.Tests/Services/JsonConnectionStoreAsyncLoadTests.cs | ⬜ pending |
| 21-03 / T2 (GREEN) | 21-03 | 2 | PERF-03 | T-21-04, T-21-05, T-21-06 | file I/O reuse, atomic field write, async-void try/catch | xUnit (3 tests pass) + bench build | `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj --filter "FullyQualifiedName~JsonConnectionStoreAsyncLoadTests" --no-restore` | src/Deskbridge.Core/Services/JsonConnectionStore.cs, src/Deskbridge/App.xaml.cs | ⬜ pending |
| 21-04 / T1 (Bench) | 21-04 | 3 | PERF-01, PERF-02, PERF-03, PERF-05 | T-21-08 | synthetic dataset only | BenchmarkDotNet | `dotnet run --project tests/Deskbridge.Benchmarks -c Release -- --filter "*TreeBuild*" --job short` | tests/Deskbridge.Benchmarks/results/phase-21-comparison.md | ⬜ pending |
| 21-04 / T2 (UAT) | 21-04 | 3 | PERF-01, PERF-02, PERF-03, PERF-05 | T-21-08 | synthetic dataset only | manual UAT (checkpoint) | (manual — see plan 21-04 Task 2) | .planning/phases/21-performance-optimizations/21-UAT.md | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

> Wave 0 = test scaffolding that must exist BEFORE production-code tasks. For Phase 21, the TDD plans (21-02, 21-03) bake the Wave 0 scaffolding into their RED tasks (Task 1 of each).

- [x] `tests/Deskbridge.Tests/Services/JsonConnectionStoreAsyncLoadTests.cs` — Plan 21-03 Task 1 (RED) creates this file with three failing tests; covers PERF-03 LoadAsync contract.
- [x] `tests/Deskbridge.Tests/ViewModels/ConnectionTreeSearchDebounceTests.cs` — Plan 21-02 Task 1 (RED) creates this file with four failing tests; covers PERF-02 250ms debounce + clear-search bypass.
- [x] `src/Deskbridge.Core/Interfaces/IDebouncer.cs` + `tests/Deskbridge.Tests/Fakes/FakeDebouncer.cs` — Plan 21-02 Task 1 introduces the testable abstraction (replaces the "test-fakes need LoadAsync stub" original Wave 0 item — for IConnectionStore, NSubstitute auto-stubs the new member; explicit `mock.LoadAsync().Returns(Task.CompletedTask)` is only required in tests that await it, which per PATTERNS.md is none of the existing tests).

*Performance regression validation reuses Phase 20 BenchmarkDotNet harness — no new framework install needed.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Plan Reference |
|----------|-------------|------------|----------------|
| Smooth scroll feel after ScrollUnit=Pixel | PERF-01 | Subjective rendering quality not measurable in unit tests | Plan 21-04 Task 2 D4.1 / D4.2 |
| No empty-tree flash on cold start | PERF-03 | Visual timing not unit-testable | Plan 21-04 Task 2 D3.1 |
| Badge appears for non-empty groups, hidden for empty | PERF-05 | XAML rendering not unit-testable | Plan 21-04 Task 2 D1.1 / D1.2 / D1.3 |
| Async startup wall-time within budget | PERF-03 / P2 | Cold-start I/O timing | Plan 21-04 Task 2 D3.2 (optional Stopwatch instrumentation) |
| Search debounce single-update + instant clear | PERF-02 | UI thread interaction timing not unit-testable beyond schedule-counter | Plan 21-04 Task 2 D2.1 / D2.2 |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies (every task in plans 21-01 through 21-04 has an `<automated>` block; manual UAT in 21-04 Task 2 is a sanctioned checkpoint with explicit verification steps)
- [x] Sampling continuity: no 3 consecutive tasks without automated verify (every task in Waves 1-2 has automated verify; Wave 3 Task 2 is a checkpoint)
- [x] Wave 0 covers all MISSING references (LoadAsync test class, debounce test class, IDebouncer interface)
- [x] No watch-mode flags (all `dotnet test` / `dotnet build` invocations are one-shot)
- [x] Feedback latency < 30s (unit tests target sub-30s per Phase 20 measurements)
- [x] BenchmarkDotNet regression run against Phase 20 baseline before phase verification (Plan 21-04 Task 1)
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** approved
