---
phase: 12-general-polish-sweep
plan: 02
subsystem: ui
tags: [wpf, xaml, gradient-brush, hover-animation, context-menu, fluent-design]

requires:
  - phase: 10-tree-view-polish
    provides: Named SolidColorBrush + ColorAnimation hover pattern (RowBrush, SubtleFillColorSecondary)
  - phase: 11-tab-bar-refinement
    provides: Tab hover animation pattern with 150ms timing
  - phase: 12-general-polish-sweep/01
    provides: Upgraded empty states in viewport and tree
provides:
  - PanelEdgeGradientVertical and PanelEdgeGradientHorizontal reusable gradient brush resources
  - 150ms hover animation on icon rail buttons matching tree/tab pattern
  - AnimatedMenuItemStyle for consistent context menu hover highlight
  - CardAndPanelStyles.xaml resource dictionary
affects: []

tech-stack:
  added: []
  patterns:
    - "Wrapping Border with named SolidColorBrush + EventTrigger for hover animation on WPF-UI buttons"
    - "Named MenuItem style with ItemContainerStyle for ContextMenu hover consistency"
    - "LinearGradientBrush as BorderBrush for subtle panel edge elevation"

key-files:
  created:
    - src/Deskbridge/Resources/CardAndPanelStyles.xaml
  modified:
    - src/Deskbridge/App.xaml
    - src/Deskbridge/MainWindow.xaml
    - src/Deskbridge/Views/ConnectionTreeControl.xaml

key-decisions:
  - "Created CardAndPanelStyles.xaml as new resource dictionary (file did not exist despite plan reference)"
  - "Used wrapping Border + EventTrigger approach for icon rail hover to avoid fighting WPF-UI internal button template"
  - "Applied AnimatedMenuItemStyle to all 5 ContextMenu instances (plan specified 3, added MultiSelect and EmptyArea for full consistency)"

patterns-established:
  - "Wrapping Border with named SolidColorBrush for hover animation on WPF-UI controls that resist Style-level Background overrides"
  - "Application-scope named MenuItem style applied via ItemContainerStyle on ContextMenu instances"

requirements-completed: [POLISH-02, POLISH-03]

duration: 3min
completed: 2026-04-19
---

# Phase 12 Plan 02: Gradient Borders and Hover Transitions Summary

**Gradient border brushes on all 4 panel edges with 150ms hover fade on icon rail buttons and consistent context menu highlight via AnimatedMenuItemStyle**

## Performance

- **Duration:** 3 min
- **Started:** 2026-04-19T19:47:08Z
- **Completed:** 2026-04-19T19:50:23Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Gradient border brushes (PanelEdgeGradientVertical/Horizontal) fade from visible at top/left to transparent at 30%, applied to icon rail, slide-out panel, tab bar, and properties panel edges
- Icon rail Settings and Connections buttons wrapped in Border with named SolidColorBrush and 150ms ColorAnimation hover matching the Phase 10/11 tree and tab pattern
- AnimatedMenuItemStyle with IsHighlighted trigger applied to all 5 ContextMenu instances for consistent hover highlight color

## Task Commits

Each task was committed atomically:

1. **Task 1: Add gradient border brush resources and apply to all panel edges** - `d24e192` (feat)
2. **Task 2: Add 150ms hover transitions to icon rail buttons and context menu items** - `41b1e74` (feat)

## Files Created/Modified
- `src/Deskbridge/Resources/CardAndPanelStyles.xaml` - New resource dictionary with PanelEdgeGradientVertical and PanelEdgeGradientHorizontal LinearGradientBrush resources
- `src/Deskbridge/App.xaml` - Merged CardAndPanelStyles.xaml dictionary; added AnimatedMenuItemStyle with IsHighlighted trigger
- `src/Deskbridge/MainWindow.xaml` - Gradient borders on icon rail/slide-out/tab bar; hover animation Borders wrapping icon rail buttons; ItemContainerStyle on tab ContextMenu
- `src/Deskbridge/Views/ConnectionTreeControl.xaml` - Gradient border on properties panel; ItemContainerStyle on all 4 ContextMenu resources

## Decisions Made
- Created `CardAndPanelStyles.xaml` as a new resource dictionary since it did not exist in the codebase despite being referenced in the plan context; registered it in App.xaml MergedDictionaries
- Used wrapping Border + EventTrigger (MouseEnter/MouseLeave) approach for icon rail button hover animation to avoid fighting WPF-UI's internal ui:Button ControlTemplate hover state
- Applied AnimatedMenuItemStyle to all 5 ContextMenu instances (ConnectionContextMenu, GroupContextMenu, MultiSelectContextMenu, EmptyAreaContextMenu, tab bar ContextMenu) for complete consistency -- plan only specified 3 but Rule 2 applies for visual consistency

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Created missing CardAndPanelStyles.xaml resource dictionary**
- **Found during:** Task 1
- **Issue:** Plan references `src/Deskbridge/Resources/CardAndPanelStyles.xaml` as existing file, but neither the file nor the Resources directory existed
- **Fix:** Created the Resources directory and CardAndPanelStyles.xaml with gradient brush resources; registered in App.xaml MergedDictionaries
- **Files modified:** src/Deskbridge/Resources/CardAndPanelStyles.xaml (created), src/Deskbridge/App.xaml
- **Verification:** Build succeeds, gradient brushes resolve correctly
- **Committed in:** d24e192

**2. [Rule 2 - Missing Critical] Applied AnimatedMenuItemStyle to MultiSelect and EmptyArea context menus**
- **Found during:** Task 2
- **Issue:** Plan specified applying ItemContainerStyle to 3 context menus (ConnectionContextMenu, GroupContextMenu, tab bar). Two additional context menus (MultiSelectContextMenu, EmptyAreaContextMenu) would have inconsistent hover behavior.
- **Fix:** Applied AnimatedMenuItemStyle to all 5 ContextMenu instances
- **Files modified:** src/Deskbridge/Views/ConnectionTreeControl.xaml
- **Verification:** All ContextMenu elements have ItemContainerStyle attribute
- **Committed in:** 41b1e74

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 missing critical)
**Impact on plan:** Both fixes necessary for correctness. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 12 plan 02 complete. All POLISH-02 and POLISH-03 requirements satisfied.
- Gradient brush resources available for reuse in future panel additions.
- AnimatedMenuItemStyle pattern available for any new ContextMenu instances.

## Self-Check: PASSED

All files exist, all commits verified.

---
*Phase: 12-general-polish-sweep*
*Completed: 2026-04-19*
