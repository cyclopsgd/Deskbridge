---
phase: 22
plan: 04
subsystem: large-import-handling
tags: [stress-tests, pathological-tests, uat, imp-03, imp-05]
dependency_graph:
  requires:
    - "src/Deskbridge.Core/Services/MRemoteNGImportExecutor.cs (22-01)"
    - "src/Deskbridge.Core/Services/MRemoteNGXmlSerializer.cs (22-03)"
    - "tests/Deskbridge.Tests/Fixtures/large/*.xml (22-03)"
    - "src/Deskbridge.Core/Services/TestDataGenerator.cs (Phase 20)"
    - "src/Deskbridge.Core/Services/MRemoteNGImporter.cs (Phase 7)"
  provides:
    - "Stress baseline at 500/1000/5000 — OOM-free, wall-clock + GC mem-delta recorded per run"
    - "Pathological coverage for 4 D-10 edge cases (deep-nest, unicode, malformed-row, empty-groups)"
    - "22-UAT.md skipped record for v1.4 follow-up"
  affects:
    - "ROADMAP Phase 22 row — IMP-05 stress + pathological gate now closed; IMP-03 visual gate skipped (not blocked)"
tech-stack:
  added: []
  patterns:
    - "xUnit v3 [Theory]+[InlineData] size sweep with Stopwatch + GC.GetTotalMemory record-and-emit (D-12)"
    - "[Trait(\"Category\", \"stress\")] gating — runs on demand via dotnet test --filter \"Category=stress\""
    - "Fixture-stream loader via Path.Combine(AppContext.BaseDirectory, \"Fixtures\", \"large\", filename) (Pattern G)"
    - "Volatile guard flag captured by Progress<int> lambda (same race-mitigation pattern the 22-02 VM uses)"
key-files:
  created:
    - "tests/Deskbridge.Tests/Services/MRemoteNGImportStressTests.cs (122 lines, 4 tests)"
    - "tests/Deskbridge.Tests/Services/MRemoteNGImportPathologicalTests.cs (131 lines, 4 tests)"
    - ".planning/phases/22-large-import-handling/22-UAT.md (74 lines, status=skipped)"
  modified: []
decisions:
  - "Stress test's `reportedValues.Should().NotBeEmpty()` assertion was removed (Rule 1 deviation): with no captured SyncContext, Progress<T> schedules callbacks via ThreadPool.QueueUserWorkItem; the 22-01 PrepareAsync returns Task.FromResult, so the await resumes synchronously before pooled callbacks can drain. Per-row Progress.Report contract is already deterministically covered by MRemoteNGImportExecutorTests; this stress test's contract is the OOM gate (D-11) + record-and-emit telemetry (D-12) only."
  - "Stress test progress callback was race-guarded with a bool[1] volatile flag + ConcurrentBag<int> (Rule 1 deviation): without it, late-firing thread-pool callbacks raced past the test method's exit and threw ArgumentOutOfRangeException from List<T>.AddWithResize. Same pattern as the 22-02 VM's volatile guard against late Progress callbacks."
  - "Pathological tests use [Fact] per fixture (not [Theory]) because each fixture has different assertions — cleaner than dispatching by filename in a Theory."
  - "MalformedSingleRow_LoadsAsValidXml_TolerantParser documents PATTERNS.md correction #3: malformed-single-row.xml is structurally valid; the parser tolerates Port=\"abc\" via int.TryParse fallback to 3389; the executor's continue-and-collect path produces 100 ConnectionsToSave / 0 Failures. This is parser tolerance, not failure-path coverage."
  - "Manual UAT (Task 3) was skipped at developer request. UI-SPEC §Acceptance #1-#10 and VALIDATION.md §Manual-Only have no human sign-off for v1.3. Data-path coverage is complete and green; render-time visual verification is deferred to v1.4. 22-UAT.md frontmatter status=skipped; every dimension marked SKIP with rationale."
metrics:
  duration_minutes: 8
  completed_date: "2026-05-03"
  tasks: 3
  tasks_complete: 2
  tasks_skipped: 1
  commits: 3
  files_created: 3
  files_modified: 0
  tests_added: 8
  total_tests_after: 753
  total_tests_before: 745
---

# Plan 22-04 — Stress + Pathological + UAT (skipped)

## What landed

### Task 1 — Stress tests (commit `bb090c4`)

`tests/Deskbridge.Tests/Services/MRemoteNGImportStressTests.cs` exercises
`MRemoteNGImportExecutor.PrepareAsync` end-to-end via the full pipeline
`TestDataGenerator → MRemoteNGXmlSerializer → MRemoteNGImporter.ParseAsync →
MRemoteNGImportExecutor.PrepareAsync` at three sizes. Wall-clock + GC
memory delta are emitted via `TestContext.Current.TestOutputHelper.WriteLine`
per D-12 (record-and-emit, no hard timing gate). The OOM gate (D-11) is
the only failure mode.

Recorded telemetry from a Release run:

```
rows=500   elapsed-ms=4   mem-delta-kb=380    failures=0  imported=500
rows=1000  elapsed-ms=4   mem-delta-kb=810    failures=0  imported=1000
rows=5000  elapsed-ms=19  mem-delta-kb=3719   failures=0  imported=5000
```

OOM-free at all three sizes. Memory deltas track linearly with row count
(~0.74-0.81 KB/row). The plus-one `[Fact]
PrepareAsync_5000_NoUiThreadAffinityRequired` confirms the executor runs
entirely off any UI thread (no Application.Current / Dispatcher access).

### Task 2 — Pathological tests (commit `28cfc1c`)

`tests/Deskbridge.Tests/Services/MRemoteNGImportPathologicalTests.cs`
covers the four D-10 fixtures committed in 22-03 with one `[Fact]` per
fixture:

- `DeepNest7Levels_ParsesWithoutStackOverflow` — walks 7 nested
  Container nodes and confirms the leaf Connection at `deepleaf.local`.
  No stack overflow; structure preserved.
- `UnicodeMixed_PreservesUnicodeNames` — confirms Cyrillic, CJK, and
  emoji ranges survive `SanitizeName` round-trip per Phase 7 T-07-10
  semantics.
- `MalformedSingleRow_LoadsAsValidXml_TolerantParser` — drives the
  executor through 100 connections (99 valid + 1 with `Port="abc"`).
  Parser falls back to 3389; executor produces 100 `ConnectionsToSave`
  and 0 `Failures`. Documents parser tolerance per PATTERNS.md
  correction #3.
- `LargeEmptyGroups_ProducesZeroConnections` — drives the executor
  through 50 empty Containers; produces 0 `ConnectionsToSave`, 50
  `GroupsToSave`, `ImportedCount=0`. Per UI-SPEC severity rule, Step 4
  would still render Severity=Success at this outcome.

All 4 pass.

### Task 3 — Manual UAT (commit `6d3444c`)

**Skipped at developer request.** `22-UAT.md` is committed with
`status: skipped` and every dimension marked `SKIP` with rationale. The
file documents three v1.4 follow-ups:

1. Run the full UAT before v1.4 release.
2. Consider WinAppDriver / FlaUI automation for the ProgressBar fill
   behavior to remove the human gate.
3. Add stricter post-parse validation in v1.4 so the Step 4 failure-list
   surface (currently Hot-Reload-only in v1.3) gets production coverage.

## What is NOT verified

UI-SPEC §Acceptance #1-#10 and VALIDATION.md §Manual-Only have no
human sign-off for v1.3:

- Determinate ProgressBar fill behavior (D1.2, D1.3)
- Cancel button visual greying during write phase (D2.4 — runtime guard
  IS covered by `ImportWizardViewModelImportProgressTests`)
- InfoBar severity color rendering (D3.1)
- Failure list overflow footer (D4.1, D4.2 — Hot-Reload-only path)
- UI thread responsiveness during 5000-row write (D5.1, D5.2)
- Process-level memory ceiling under load (D5.3 — only GC delta is recorded)

Each is documented in `22-UAT.md` with a SKIP rationale.

## Test outcomes

- Phase 22-04 net new tests: **8** (4 stress + 4 pathological).
- Project total: 745 → 753 (+8).
- Phase-22-attributable failures: 0.
- Sole suite failure: `Gate2_IMsTscNonScriptable_PasswordSetSucceeds`
  (env-dependent RDP smoke; documented in
  `.planning/phases/22-large-import-handling/deferred-items.md` from
  22-02; pre-Phase-22 baseline; not Phase-22 caused).

## Commits

| Hash | Subject |
|------|---------|
| `bb090c4` | `test(22-04): add stress tests at 500/1000/5000 (IMP-05, D-09)` |
| `28cfc1c` | `test(22-04): add pathological fixture coverage (D-10)` |
| `6d3444c` | `test(22-04): record Phase 22 manual UAT results (skipped)` |

## Follow-ups for v1.4

1. **Run the full UAT.** Generate `%TEMP%\uat-1000.xml` and
   `%TEMP%\uat-5000.xml`, drive the wizard through the 7-dimension
   checklist in `22-UAT.md`, flip `status` to `passed` or `failed`.
2. **Automate visual gates** with WinAppDriver / FlaUI so the ProgressBar
   fill behavior, Cancel disablement, and InfoBar copy land in CI rather
   than UAT.
3. **Tighten post-parse validation** so the Step 4 failure-list surface
   has a non-Hot-Reload code path (today the only in-app trigger is
   parser-tolerated rows, which the executor still treats as success).
4. **Record process-level memory** under a 5000-row import so the IMP-05
   ceiling has a hard number — current evidence is only GC delta from
   the unit-test stress run (3.7 MB).
