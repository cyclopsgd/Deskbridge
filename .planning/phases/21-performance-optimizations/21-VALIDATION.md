---
phase: 21
slug: performance-optimizations
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-02
---

# Phase 21 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution. Filled by gsd-planner during planning. See `21-RESEARCH.md` §"Validation Architecture" for the source contract.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit (existing in tests/Deskbridge.Core.Tests, tests/Deskbridge.Tests) + BenchmarkDotNet (tests/Deskbridge.Benchmarks, Phase 20) |
| **Config file** | none — frameworks already installed |
| **Quick run command** | `dotnet test tests/Deskbridge.Core.Tests --no-restore` |
| **Full suite command** | `dotnet test --no-restore` |
| **Estimated runtime** | ~30s for unit tests; benchmarks run separately on demand |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build` + targeted unit test (`dotnet test --filter "FullyQualifiedName~{TestClass}"`)
- **After every plan wave:** Run `dotnet test --no-restore` (full unit suite)
- **Before `/gsd-verify-work`:** Full suite green + benchmark regression check vs Phase 20 baseline
- **Max feedback latency:** 30 seconds for unit tests

---

## Per-Task Verification Map

> Filled by planner from PLAN.md acceptance_criteria. One row per atomic task.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD     | TBD  | TBD  | PERF-XX     | —          | N/A             | TBD       | TBD               | TBD         | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Deskbridge.Core.Tests/Services/JsonConnectionStoreAsyncLoadTests.cs` — covers PERF-03 LoadAsync contract
- [ ] `tests/Deskbridge.Tests/ViewModels/ConnectionTreeSearchDebounceTests.cs` — covers PERF-02 250ms debounce + clear-search bypass
- [ ] Existing `IConnectionStore` test fakes updated with `Task LoadAsync() => Task.CompletedTask` stub

*Performance regression validation reuses existing Phase 20 BenchmarkDotNet harness — no new framework install needed.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Smooth scroll feel after ScrollUnit=Pixel | PERF-01 | Subjective rendering quality not measurable in unit tests | Generate 1000-connection dataset via `TestDataGenerator`, launch Deskbridge, scroll the tree with both mouse wheel and trackpad. Confirm advancement is per-pixel (smooth) not per-row (stuttery). No visual regression in row hover/selection visuals. |
| No empty-tree flash on cold start | PERF-03 | Visual timing not unit-testable | With 500-connection dataset, cold-start Deskbridge 3 times. Confirm window appears with tree already populated — never empty. |
| Badge appears for non-empty groups, hidden for empty | PERF-05 | XAML rendering not unit-testable | Load dataset with at least one empty group and one populated group. Confirm "(N)" appears next to populated group name in secondary text color, completely absent (Collapsed) for empty group. |
| Async startup wall-time within budget | PERF-03 / P2 | Cold-start I/O timing | Add `Stopwatch` instrumentation in `OnStartup`. With N=500 dataset, log wall-time between `BuildServiceProvider()` and `MainWindow.Show()`. Compare against sync baseline (capture once before refactor) — overhead ≤ 50ms. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references (LoadAsync test class, debounce test class)
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] BenchmarkDotNet regression run against Phase 20 baseline before phase verification
- [ ] `nyquist_compliant: true` set in frontmatter after planner fills the verification map

**Approval:** pending
