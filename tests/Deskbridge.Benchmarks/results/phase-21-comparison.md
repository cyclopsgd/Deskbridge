# Phase 21 — BuildTree Benchmark Comparison

**Run date:** 2026-05-02
**Phase 20 baseline:** 5cd2c4a (committed in `tests/Deskbridge.Benchmarks/baseline/Deskbridge.Benchmarks.Benchmarks.TreeBuildBenchmarks-report-full.json`, ShortRun job)
**Phase 21 head:** 3880dfe
**Hardware:** AMD Ryzen 5 5600 3.50GHz, 1 CPU, 12 logical / 6 physical cores, .NET 10.0.7, Windows 11 (10.0.26200.8037)
**Benchmark config:** `--filter "*TreeBuild*" --job short` (BenchmarkDotNet 0.15.8, ShortRun: IterationCount=3, LaunchCount=1, WarmupCount=3)

## TreeBuildBenchmarks.BuildTree

ShortRun job (matches `--job short` invocation; baseline JSON contains both default and ShortRun rows — ShortRun used here because the run uses the same harness profile):

| N    | Phase 20 Mean (μs) | Phase 21 Mean (μs) | Δ (μs)  | Δ%      | Within ±5%? |
|------|--------------------|--------------------|---------|---------|-------------|
| 100  | 7.735              | 8.066              | +0.331  | +4.28%  | YES         |
| 200  | 15.529             | 16.910             | +1.381  | +8.89%  | NO          |
| 500  | 37.898             | 45.241             | +7.343  | +19.38% | NO          |
| 1000 | 79.477             | 84.040             | +4.563  | +5.74%  | NO          |

### Cross-check: default job (IterationCount=10)

For corroboration, the 10-iteration job rows from both runs (also captured because `BenchmarkConfig.cs` registers a default job in addition to the `--job short` selector) show the same direction:

| N    | Phase 20 Mean (μs) | Phase 21 Mean (μs) | Δ%      |
|------|--------------------|--------------------|---------|
| 100  | 7.432              | 8.034              | +8.10%  |
| 200  | 16.808             | 18.795             | +11.82% |
| 500  | 38.017             | 43.629             | +14.76% |
| 1000 | 79.365             | 86.181             | +8.59%  |

Both jobs agree the regression is real, not a ShortRun-only artefact.

## Verdict

NO-GO — N=200 regressed by +8.89%, N=500 regressed by +19.38%, N=1000 regressed by +5.74%. Only N=100 is within ±5%.

**Likely cause analysis (must verify):** Phase 21 changes (per `21-RESEARCH.md`) explicitly do NOT modify `ConnectionTreeBuilder.Build()` or any code on its hot path:

- 21-01 added `VirtualizingPanel.ScrollUnit="Pixel"` (XAML only) and inline badge XAML — neither touches the builder.
- 21-02 introduced `IDebouncer` in `ConnectionTreeViewModel` — separate class from the builder.
- 21-03 added `JsonConnectionStore.LoadAsync()` as a `Task.Run(Load)` shim — does not change `ConnectionTreeBuilder`.

The benchmark target (`ConnectionTreeBuilder.Build`) appears unchanged on this branch versus Phase 20 head. This points strongly to environmental variance between the 2026-04-27 baseline run and the 2026-05-02 re-run on the same machine (different background load, CPU thermal/boost state, OS update 2025Update/HudsonValley2 was applied between runs, etc.) rather than a true Phase 21 code regression.

That said, we cannot ship the perf gate as PASS while three of four N values are outside ±5%. Operator decision required: (a) treat as environmental and re-baseline before continuing, (b) accept the regression as noise and proceed to UAT anyway, or (c) bisect Phase 21 commits to confirm no hot-path regression slipped in.

## Raw Outputs

- Phase 20 baseline source: `tests/Deskbridge.Benchmarks/baseline/Deskbridge.Benchmarks.Benchmarks.TreeBuildBenchmarks-report-full.json`
- Phase 21 run artifacts (gitignored, regenerable): `BenchmarkDotNet.Artifacts/results/Deskbridge.Benchmarks.Benchmarks.TreeBuildBenchmarks-report-full.json` (and `.csv`, `-report-github.md`, `.html` siblings)
- Run command: `dotnet run --project tests/Deskbridge.Benchmarks -c Release --no-build -- --filter "*TreeBuild*" --job short`
- Run wall-time: 87.86 sec (8 benchmarks executed: 4 N values × 2 jobs)
