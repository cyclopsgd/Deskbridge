---
phase: 21
type: uat
status: passed
tester: George Denton
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

| Dimension | Sub-test                                              | Requirement   | Result | Notes                                                                                                  |
|-----------|-------------------------------------------------------|---------------|--------|--------------------------------------------------------------------------------------------------------|
| D1.1      | Badge visible on populated group                      | PERF-05       | PASS   | `(N)` rendered in subdued text immediately after group name on every populated group.                  |
| D1.2      | Badge hidden (Collapsed) on empty group               | PERF-05       | PASS   | No `(0)` glyph; key-icon position correctly slides leftward by the badge's would-be width.              |
| D1.3      | Recursive count matches sum of descendants            | PERF-05       | PASS   | Parent `(N)` matches the sum of all descendant connections across nested groups.                       |
| D2.1      | Rapid typing produces single trailing update          | PERF-02       | PASS   | Result list does not flicker per-keystroke; one trailing update ~250ms after last keystroke.           |
| D2.2      | Clear-search restores tree instantly                  | PERF-02       | PASS   | Tree restored on the same frame; no perceptible 250ms wait — synchronous bypass works.                  |
| D3.1      | No empty-tree flash on 3 cold starts                  | PERF-03       | PASS   | Window appeared with the tree already populated on all three cold-start launches.                       |
| D3.2      | Startup overhead ≤ 50ms (optional)                    | PERF-03 / P2  | SKIP   | Not measured this UAT cycle; instrumentation was not exercised. D3.1 functional gate satisfied.         |
| D4.1      | Mouse wheel pixel-smooth                              | PERF-01       | PASS   | Wheel scroll advances by pixel, smooth glide; no per-row stepping.                                      |
| D4.2      | Trackpad pixel-smooth                                 | PERF-01       | PASS   | Sub-row precision on precision touchpad; smooth.                                                        |
| D4.3      | No regression in hover/select/drag/menu               | PERF-01       | PASS   | Hover, selection, expand/collapse, multi-select Ctrl+click, drag-drop reorder, context menu all intact. |
| D5.1      | No unexpected stutter or errors                       | (cross)       | PASS   | No stutter on typical actions (open connection, type in search, expand group); no error toasts.         |

## Verdict

ALL PASS

## Logs reviewed

- `%AppData%/Deskbridge/logs/*.log` — no anomalies reported during the UAT session. No stack traces, no `Log.Fatal` entries from the new async OnStartup path, no debouncer or load-related warnings.

## Notes / Out-of-scope

The operator surfaced three **pre-existing** UI issues during this UAT pass that are NOT Phase 21 regressions and do NOT count as UAT failures. Each has been captured as a todo for follow-up:

1. **Server-name truncation at edge** — long server names truncate at the right edge of the tree row in some scenarios. Pre-existing layout sizing issue; surfaced again under the larger 500-connection dataset because more long names are visible at once. Not introduced by Phase 21 (no XAML changes affect column width or text trimming on the connection row template). _(Not yet captured as a separate todo — overlaps with the general text-size investigation; tracked here as a noted observation.)_
2. **PIN entry dialog edges clipped at startup** — the master-password / PIN entry dialog has slight clipping of its outer edges on app launch. Captured as: [`.planning/todos/pending/2026-05-02-pin-entry-dialog-edges-clipped-at-startup.md`](../../todos/pending/2026-05-02-pin-entry-dialog-edges-clipped-at-startup.md) (commit `6b29354`). Pre-existing; not touched by Phase 21.
3. **Text-size changer inconsistent in folder quick panel** — the app-level text-size setting does not propagate to the folder quick panel, which renders at a fixed (oversized) point size regardless of the user's selection. Captured as: [`.planning/todos/pending/2026-05-02-text-size-changer-inconsistent-in-folder-quick-panel.md`](../../todos/pending/2026-05-02-text-size-changer-inconsistent-in-folder-quick-panel.md) (commit `5f5808a`). Pre-existing; not touched by Phase 21.

These three items are tracked under "Pending Todos" in `STATE.md` for future polish work and are explicitly excluded from the Phase 21 UAT verdict per the operator's sign-off scope.
