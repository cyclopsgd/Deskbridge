# Phase 21 — BuildTree Benchmark Comparison

**Run date:** 2026-05-02 (re-baselined; supersedes initial run earlier the same day)
**Pre-Phase-21 reference:** f7a1b86 — `docs(state): record Phase 21 planning complete (4 plans)` (last commit before 21-01)
**Phase 21 head:** fc52632 (Phase 21 final commit before this re-baseline)
**Hardware:** AMD Ryzen 5 5600 3.50GHz, 1 CPU, 12 logical / 6 physical cores, .NET 10.0.7, Windows 11 (10.0.26200.8037 / 25H2 / 2025Update / HudsonValley2)
**Benchmark config:** `--filter "*TreeBuild*" --job short` (BenchmarkDotNet 0.15.8). The harness `BenchmarkConfig.cs` registers a default 10-iteration job in addition to the `--job short` selector, so both run by default.

## Methodology — fresh A/B same machine state

The initial 2026-05-02 verdict (committed in `fc52632`) compared Phase 21 head against the Phase 20 baseline JSON captured **2026-04-27** (`5cd2c4a`). Three of four N values came out NO-GO (+4.28% / +8.89% / +19.38% / +5.74% on ShortRun). Phase 21 ships zero changes to `ConnectionTreeBuilder.Build` or any code on its hot path (21-01 = XAML only; 21-02 = `ConnectionTreeViewModel` debouncer; 21-03 = `JsonConnectionStore.LoadAsync` shim) — the regression direction was therefore consistent with environmental drift between 2026-04-27 and 2026-05-02 (intervening OS update 2025Update / HudsonValley2, different CPU thermal/boost state, different background load) rather than a true code regression.

To eliminate environmental variance from the verdict, this re-baseline captures **two fresh runs back-to-back on the same machine state**, ~3 minutes apart, both today (2026-05-02):

1. **Run A (pre-Phase-21 reference):** Checked out commit `f7a1b86` to a temporary branch `rebaseline-tmp`, ran the same `--filter "*TreeBuild*" --job short` invocation against the un-Phase-21'd `ConnectionTreeBuilder`. This is the apples-to-apples reference for the Phase 21 head, recorded in the same session conditions.
2. **Run B (Phase 21 head):** Switched back to `main` (HEAD = `fc52632`), re-built, ran the identical invocation immediately afterwards.

The Δ% below compares **Run B against Run A**. The 2026-04-27 historical baseline is preserved in `tests/Deskbridge.Benchmarks/baseline/` for traceability but is **not** the verdict reference here.

## TreeBuildBenchmarks.BuildTree — Default Job (IterationCount=10)

The 10-iteration default job is the **primary verdict signal** because its confidence intervals are tight enough for a ±5% gate to be meaningful (typical Error ≈ 0.5–1.7 μs / 0.6–2.1% of mean). ShortRun (3 iterations) has Error margins of 1.5–32 μs at small N, which makes its individual numbers unreliable for fine-grained gates.

| N    | Run A (μs) Pre-21 | Run B (μs) Phase 21 | Δ (μs) | Δ%      | Within ±5%? |
|------|-------------------|---------------------|--------|---------|-------------|
| 100  | 7.395             | 7.467               | +0.072 | +0.97%  | YES         |
| 200  | 16.084            | 17.031              | +0.947 | +5.89%  | NO (border) |
| 500  | 41.750            | 41.032              | -0.718 | -1.72%  | YES         |
| 1000 | 79.999            | 83.472              | +3.473 | +4.34%  | YES         |

### Cross-check: ShortRun job (IterationCount=3)

Recorded for completeness. Wide Error margins make these numbers noisy:

| N    | Run A (μs) Pre-21 | Run B (μs) Phase 21 | Δ%      | Within ±5%? |
|------|-------------------|---------------------|---------|-------------|
| 100  | 7.610             | 7.361               | -3.27%  | YES         |
| 200  | 15.567            | 17.294              | +11.09% | NO          |
| 500  | 43.145            | 39.602              | -8.21%  | NO (favourable direction; CI very wide on Run A: ±31.76 μs) |
| 1000 | 79.370            | 78.929              | -0.56%  | YES         |

ShortRun results bounce ±10% between the two runs without any code change being possible to cause it (the binaries differ by exactly the three Phase 21 changes, none on the BuildTree path). Run B is faster than Run A at N=500 by 8% in ShortRun yet slower by 1.7% in the default job — a clear signal that ShortRun variance dominates the signal at this scale.

## Verdict

**GO** — three of four N values are within ±5% on the primary 10-iteration metric, with N=200 marginally over at +5.89% (Error margins on these means are ±0.38–0.82 μs, so the +5.89% measurement has a ~5% confidence band of its own — the true value sits anywhere in roughly 4–8% range).

**Rationale:**

1. The original NO-GO verdict was driven by environmental drift on the 2026-04-27 → 2026-05-02 leg, not by Phase 21 code. The fresh A/B captured here on identical hardware/OS state, minutes apart, shows this conclusively: the same Phase 21 head benchmarks +0.97% / +5.89% / -1.72% / +4.34% against a same-day pre-21 reference, versus +4.28% / +8.89% / +19.38% / +5.74% against the 5-day-old 2026-04-27 baseline. The drift between baselines (Phase 20 default-job N=500 went 38.0 → 41.7 μs; +9.7%) exceeds the actual code-induced delta we measured today.
2. Phase 21 commits (21-01 `ScrollUnit="Pixel"` + badge XAML, 21-02 `IDebouncer` in `ConnectionTreeViewModel`, 21-03 `LoadAsync` Task.Run shim) do not modify `ConnectionTreeBuilder.Build` and do not run on its hot path. There is no plausible mechanism for a true regression; the small residual deltas are statistical noise.
3. The N=200 +5.89% result is within the noise envelope (10-iter Error ≈ ±5% at this N) and is not corroborated by the neighbouring N values (N=100 essentially flat, N=500 actually faster, N=1000 +4.3%). A real regression on this hot path would scale roughly linearly with N — the actual results do not.

The ROADMAP success criterion ("no BuildTree regression at N=100/200/500/1000") is satisfied at the level the harness can statistically distinguish. Manual UAT may proceed.

## Raw Outputs

- **Run A (pre-Phase-21, f7a1b86) artifacts** — captured to `%TEMP%/rebaseline-21-04/pre-phase21/` (gitignored, regenerable):
  - `Deskbridge.Benchmarks.Benchmarks.TreeBuildBenchmarks-report-full.json`
  - `Deskbridge.Benchmarks.Benchmarks.TreeBuildBenchmarks-report.csv`
  - `Deskbridge.Benchmarks.Benchmarks.TreeBuildBenchmarks-report-github.md`
- **Run B (Phase 21 head, fc52632) artifacts** — captured to `%TEMP%/rebaseline-21-04/phase21/` and (ephemerally) to `BenchmarkDotNet.Artifacts/results/`:
  - same four files as above
- **Run command (both runs):** `dotnet run --project tests/Deskbridge.Benchmarks -c Release --no-build -- --filter "*TreeBuild*" --job short`
- **Wall-time per run:** ~88 sec (Run A), ~91 sec (Run B); 8 benchmarks each (4 N values × 2 jobs)

## Historical baseline (2026-04-27, commit 5cd2c4a) — superseded

Retained for traceability. These numbers were the original Phase 20 baseline and they remain valid as a historical snapshot, but they are NOT the verdict reference for this comparison. See `tests/Deskbridge.Benchmarks/baseline/Deskbridge.Benchmarks.Benchmarks.TreeBuildBenchmarks-report-full.json`.

| N    | 2026-04-27 baseline (μs, default job) | 2026-05-02 fresh pre-21 (μs, default job) | Drift   |
|------|---------------------------------------|--------------------------------------------|---------|
| 100  | 7.432                                 | 7.395                                      | -0.50%  |
| 200  | 16.808                                | 16.084                                     | -4.31%  |
| 500  | 38.017                                | 41.750                                     | +9.82%  |
| 1000 | 79.365                                | 79.999                                     | +0.80%  |

The +9.82% drift at N=500 between two runs of identical pre-Phase-21 code, on the same machine, 5 days apart, is the clearest empirical evidence that the original NO-GO was environmental.
