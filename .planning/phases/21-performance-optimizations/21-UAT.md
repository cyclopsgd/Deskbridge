---
phase: 21
type: uat
status: in-progress
tester: TBD
test_date: 2026-05-02
dataset: 500 connections (TestDataGenerator deterministic seed)
---

# Phase 21 — Manual UAT

This document captures the manual UAT for Phase 21 performance & UX optimizations. Phase 21 has shipped:

- **21-01:** `VirtualizingPanel.ScrollUnit="Pixel"` on the TreeView (PERF-01) + inline `(N)` count badge between group name and key icon, hidden when zero (PERF-05)
- **21-02:** 250ms `IDebouncer`-injected search debounce in `ConnectionTreeViewModel` (PERF-02)
- **21-03:** `LoadAsync()` on `JsonConnectionStore` + `async void OnStartup` awaiting load before `MainWindow.Show()` (PERF-03)

Task 1 of plan 21-04 has confirmed BuildTree benchmarks are within ±5% of the pre-Phase-21 reference on the primary 10-iteration metric (commit `3ac0c08`, supersedes initial NO-GO in `fc52632` which was environmental variance).

This document is the structured manual UAT for the user-perceptible behaviors that automated tests cannot cover.

## Setup

1. Generate a 500-connection deterministic dataset using Phase 20's `TestDataGenerator`. One viable approach: write a one-off C# console snippet that instantiates `TestDataGenerator.Generate(500)` and serializes the result to `%APPDATA%/Deskbridge/connections.json`. **Back up any existing connections.json before overwriting.**
2. Verify the dataset includes at least ONE empty group (no descendants) and at least ONE deeply nested group (≥ 2 levels) — required for D1.2 and D1.3.
3. Launch Deskbridge.

See `21-04-PLAN.md` Task 2 `<how-to-verify>` for full step-by-step instructions per dimension.

## Results

| Dimension | Sub-test                                              | Requirement   | Result          | Notes |
|-----------|-------------------------------------------------------|---------------|-----------------|-------|
| D1.1      | Badge visible on populated group                      | PERF-05       | PASS/FAIL       |       |
| D1.2      | Badge hidden (Collapsed) on empty group               | PERF-05       | PASS/FAIL       |       |
| D1.3      | Recursive count matches sum of descendants            | PERF-05       | PASS/FAIL       |       |
| D2.1      | Rapid typing produces single trailing update          | PERF-02       | PASS/FAIL       |       |
| D2.2      | Clear-search restores tree instantly                  | PERF-02       | PASS/FAIL       |       |
| D3.1      | No empty-tree flash on 3 cold starts                  | PERF-03       | PASS/FAIL       |       |
| D3.2      | Startup overhead ≤ 50ms (optional)                    | PERF-03 / P2  | PASS/FAIL/SKIP  |       |
| D4.1      | Mouse wheel pixel-smooth                              | PERF-01       | PASS/FAIL       |       |
| D4.2      | Trackpad pixel-smooth                                 | PERF-01       | PASS/FAIL       |       |
| D4.3      | No regression in hover/select/drag/menu               | PERF-01       | PASS/FAIL       |       |
| D5.1      | No unexpected stutter or errors                       | (cross)       | PASS/FAIL       |       |

## Verdict

{ALL PASS / FAIL — list failed dimensions}

## Logs reviewed

- `%AppData%/Deskbridge/logs/{file}` — {clean / contains expected entries / contains anomaly}
