---
phase: 10-tree-view-polish
plan: 01
subsystem: ui
tags: [wpf, treeview, storyboard, animation, status-indicator, fluent]

# Dependency graph
requires:
  - phase: 09-quick-properties-panel
    provides: "_connectionStateMap event subscriptions, SelectedConnectionState, semantic brushes"
provides:
  - Per-item ConnectionState property on ConnectionTreeItemViewModel for tree status dots
  - Storyboard-based hover (150ms) and selection (80ms) animations on tree rows
  - Three visually distinct tree row states -- default, hovered, selected
affects: [10-02-PLAN, 12-general-polish]

# Tech tracking
tech-stack:
  added: []
  patterns: [Storyboard EnterActions/ExitActions for animated triggers, named SolidColorBrush for Color animation targets, StaticResource Color keys in Storyboard To values]

key-files:
  created: []
  modified:
    - src/Deskbridge/ViewModels/ConnectionTreeItemViewModel.cs
    - src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs
    - src/Deskbridge/Views/ConnectionTreeControl.xaml

key-decisions:
  - "StaticResource Color keys (not Brush keys) for Storyboard animation targets -- Freezable constraint requires StaticResource; dark-theme-only means no runtime theme switch risk"
  - "Named SolidColorBrush (RowBrush) as Border.Background child element instead of Background attribute -- enables ColorAnimation targeting"
  - "Selection animation 80ms (faster than hover 150ms) for snappy click response"

patterns-established:
  - "Pattern: Use SolidColorBrush child with x:Name for animatable backgrounds, not Background attribute"
  - "Pattern: Trigger ordering -- hover before selection in XAML; selection Storyboard overrides running hover animation"
  - "Pattern: Restore per-item state from cached map after BuildTree rebuild to prevent stale UI after RefreshTree"

requirements-completed: [TREE-01, TREE-02]

# Metrics
duration: 2min
completed: 2026-04-19
---

# Phase 10 Plan 01: Status Dots and Hover Transitions Summary

**Per-connection status dots (green/gray/amber/red) and 150ms Storyboard-animated hover/selection transitions on tree rows**

## Performance

- **Duration:** 2 min
- **Started:** 2026-04-19T15:36:51Z
- **Completed:** 2026-04-19T15:39:00Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Every connection in the tree shows an 8x8 status dot reflecting live connection state (Connected=green, Reconnecting=amber, Error=red, disconnected=gray)
- Tree row hover transitions use smooth 150ms ColorAnimation fade-in/out instead of instant background snap
- Three distinct visual states: default (transparent), hovered (SubtleFillColorSecondary), selected (SystemAccentColorSecondary + accent stripe)
- Status dots survive tree rebuilds via _connectionStateMap restoration in BuildTree

## Task Commits

Each task was committed atomically:

1. **Task 1: Add per-item ConnectionState property and propagation** - `ca8cb80` (feat)
2. **Task 2: Status dots in tree DataTemplate and animated hover transitions** - `8d888d8` (feat)

## Files Created/Modified
- `src/Deskbridge/ViewModels/ConnectionTreeItemViewModel.cs` - Added nullable TabState? ConnectionState property for per-item status dot binding
- `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` - Propagate state to tree item VMs in OnTabStateChanged/OnTabClosed/OnConnectionClosed; restore states after BuildTree
- `src/Deskbridge/Views/ConnectionTreeControl.xaml` - Added status dot Ellipse with DataTriggers in connection DataTemplate; replaced instant hover/selection triggers with Storyboard animations

## Decisions Made
- Used StaticResource Color keys (SubtleFillColorSecondary, SystemAccentColorSecondary) for Storyboard animation targets per Phase 8 convention and WPF Freezable constraint
- Selection animation duration 80ms (faster than hover 150ms) for snappy click response
- Named SolidColorBrush (RowBrush) pattern instead of Background attribute to enable ColorAnimation targeting
- Groups do NOT get status dots (TREE-01 requirement says "each connection", not "each item")

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Plan 10-02 (indentation guides) can proceed -- tree ControlTemplate is stable with the new animation infrastructure
- Phase 12 general polish can reference the Storyboard animation pattern established here

---
*Phase: 10-tree-view-polish*
*Completed: 2026-04-19*
