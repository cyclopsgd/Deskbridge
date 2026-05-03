# Phase 22 — Deferred Items (out-of-scope discoveries)

Issues discovered during Phase 22 execution that are NOT directly caused by
Phase 22 changes. Per executor scope-boundary rule: log here, do not fix.

## Plan 22-01

### Flaky: `CrashHandlerTests.OnUnobservedTask_LogsErrorAndSetsObserved`

**Discovered:** 2026-05-03 during Plan 22-01 Task 3 verification.
**Symptom:** Single test fails intermittently in full-suite runs (1 in ~3
runs); passes on the next run with no code change. Other 706 tests pass.
**Root cause (unconfirmed):** Test relies on GC finalizer pumping the
`TaskScheduler.UnobservedTaskException` event. Timing depends on the GC
heuristic deciding to run the finalizer thread, which is non-deterministic
under xUnit v3's parallel collection runner.
**Phase 22 attribution:** None. Test predates Phase 22; flake is in
`tests/Deskbridge.Tests/Logging/CrashHandlerTests.cs:160` and depends on
`JsonConnectionStore.Load` producing an unobserved task. Pre-existing.
**Suggested fix (future):** Force GC + `WaitForPendingFinalizers` BEFORE the
HaveCount assertion, or move the test to its own collection with parallel
execution disabled.

## Plan 22-02

### Env-dependent: `RdpHostControlSmokeTests.Gate2_IMsTscNonScriptable_PasswordSetSucceeds`

**Discovered:** 2026-05-03 during Plan 22-02 Task 2 verification.
**Symptom:** Test connects to a real RDP host and fails with
`discReason=1800` (server rejected auth) when the configured credentials
do not match a live target. Reproduces on the pre-Phase-22 baseline (verified
via `git stash` + isolated re-run) — failure is independent of the VM
refactor in this plan.
**Phase 22 attribution:** None. Test predates Phase 22; depends on a live
RDP environment and credentials supplied via the test runner's environment.
**Suggested fix (future):** Mark the smoke gate `[Trait("Category", "RequiresLiveRdp")]`
and exclude it from CI / default `dotnet test` runs, or seed it from a
hermetic test-RDP container.
