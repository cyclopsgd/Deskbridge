---
phase: 21-performance-optimizations
plan: 01
subsystem: ui
tags: [wpf, wpf-ui, treeview, virtualization, xaml, fluent]

# Dependency graph
requires:
  - phase: 20-performance-baselines
    provides: GroupTreeItemViewModel.ConnectionCount recursive getter (already present), benchmark baselines for regression check in plan 21-04
provides:
  - Per-pixel scrolling on the connection tree (PERF-01)
  - Inline (N) recursive count badge on every non-empty group row (PERF-05)
affects: [21-02-search-debounce, 21-03-async-load, 21-04-performance-validation, 23-bulk-operations-ux]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "VirtualizingPanel.ScrollUnit=Pixel attached property for smooth WPF tree scrolling"
    - "Inline DataTrigger Visibility collapse on a TextBlock keyed off an int VM property (no converter)"

key-files:
  created: []
  modified:
    - src/Deskbridge/Views/ConnectionTreeControl.xaml

key-decisions:
  - "Reused existing GroupTreeItemViewModel.ConnectionCount (recursive getter) — no caching, no converter, no VM change"
  - "Hide-when-zero implemented via inline DataTrigger rather than IValueConverter (UI-SPEC-locked)"
  - "Grouped all three VirtualizingPanel.* attributes together for readability"

patterns-established:
  - "Pixel-unit virtualization on TreeView: combine IsVirtualizing=True + VirtualizationMode=Recycling + ScrollUnit=Pixel"
  - "Recursive count badge on group rows: 12px Regular, TextFillColorSecondaryBrush, 4px left margin, VerticalAlignment=Center, DataTrigger collapse on 0"

requirements-completed: [PERF-01, PERF-05]

# Metrics
duration: 4min
completed: 2026-05-02
---

# Phase 21 Plan 01: Tree Scroll + Group Count Badge Summary

**Two surgical XAML edits in ConnectionTreeControl.xaml — `VirtualizingPanel.ScrollUnit="Pixel"` for smooth wheel/trackpad scrolling (PERF-01) and an inline recursive `(N)` count badge on group rows that hides when empty (PERF-05).**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-05-02T08:23:00Z (approx)
- **Completed:** 2026-05-02T08:27:32Z
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments
- Added `VirtualizingPanel.ScrollUnit="Pixel"` to the ConnectionTree opening tag — wheel and trackpad gestures now advance by pixel rather than per row, producing smoother scroll feel especially on high-resolution displays and trackpads.
- Inserted recursive count badge `TextBlock` between the group `Name` TextBlock and `Key24` SymbolIcon in the group `HierarchicalDataTemplate`. Reuses `GroupTreeItemViewModel.ConnectionCount` (already recursive across descendants), formatted as `({0})` with `TextFillColorSecondaryBrush`, `FontSize=12`, `FontWeight=Regular`, `Margin=4,0,0,0`, `VerticalAlignment=Center`.
- Hide-when-zero rule implemented via inline `DataTrigger Binding={Binding ConnectionCount} Value=0 → Visibility=Collapsed` — no converter required.
- Pure XAML changes: no code-behind, no ViewModel, no resource, no converter, no namespace additions.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add `VirtualizingPanel.ScrollUnit="Pixel"` to ConnectionTree opening tag** — `0f3a39d` (feat)
2. **Task 2: Insert recursive count badge in group HierarchicalDataTemplate** — `5df2ab7` (feat)

**Plan metadata:** (this commit) — docs(21-01): complete plan

## Files Created/Modified
- `src/Deskbridge/Views/ConnectionTreeControl.xaml` — added `VirtualizingPanel.ScrollUnit="Pixel"` (line 347) and a new `TextBlock` count badge inside the group `HierarchicalDataTemplate StackPanel` (lines 382–401). Two phase-tag comments at lines 339 and 382.

## Decisions Made
- **Followed the UI-SPEC-locked badge XAML verbatim** (FontSize=12, FontWeight=Regular, TextFillColorSecondaryBrush, Margin=4,0,0,0, VerticalAlignment=Center, inline DataTrigger collapse-on-0). No deviations from the visual contract.
- **No caching of `ConnectionCount`.** The CONTEXT permits caching but at expected enterprise scale (≤1000 connections, mostly leaf nodes) the recursive `O(n)` traversal is negligible. Will revisit only if 21-04 benchmarks flag a regression.
- **Inline DataTrigger over IValueConverter.** UI-SPEC suggests both are acceptable; the inline trigger keeps the change zero-dependency and consistent with this template's existing approach for `IsExpanded → Folder24/FolderOpen24` (sibling element 12 lines above already uses the same DataTrigger pattern).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None. Both tasks built clean on first attempt (`dotnet build src/Deskbridge/Deskbridge.csproj -c Debug` exited 0 with 0 warnings, 0 errors after each task). All acceptance-criteria grep checks passed.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- **Plan 21-02 (search debounce)** — independent of this plan; can start immediately. Will introduce a `DispatcherTimer` field on `ConnectionTreeViewModel` per PATTERNS.md.
- **Plan 21-03 (async startup load)** — independent of this plan; can start immediately. Will add `JsonConnectionStore.LoadAsync()` and refactor `App.OnStartup` to async-void.
- **Plan 21-04 (performance regression validation)** — depends on 21-01/02/03 complete. Will validate that `VirtualizingPanel.ScrollUnit="Pixel"` does not regress `BuildTree` benchmarks at N=500/1000 vs Phase 20 baselines, plus manual UAT on tree scroll smoothness and badge visibility on populated/empty groups.

No blockers or concerns. The two changes are inert until the user opens the tree; both are pure rendering paths.

## Self-Check: PASSED

- File `src/Deskbridge/Views/ConnectionTreeControl.xaml` exists and contains:
  - `VirtualizingPanel.ScrollUnit="Pixel"` on line 347 (between `VirtualizationMode="Recycling"` and `ScrollViewer.CanContentScroll="True"`).
  - Phase-tag comment `Phase 21 (PERF-01)` on line 339.
  - Badge `TextBlock Text="{Binding ConnectionCount, StringFormat='({0})'}"` on line 385 (between Name TextBlock at 377 and Key SymbolIcon at 403).
  - Phase-tag comment `Phase 21 (PERF-05)` on line 382.
  - Inline `<DataTrigger Binding="{Binding ConnectionCount}" Value="0">` collapsing badge when count == 0.
- Commits exist in `git log`: `0f3a39d` (Task 1), `5df2ab7` (Task 2).
- Build clean: `dotnet build src/Deskbridge/Deskbridge.csproj -c Debug` exited 0 with 0 warnings.

---
*Phase: 21-performance-optimizations*
*Completed: 2026-05-02*
