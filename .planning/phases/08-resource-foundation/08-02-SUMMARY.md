---
phase: 08-resource-foundation
plan: 02
subsystem: ui
tags: [wpf, xaml, resource-dictionary, spacing, typography, fluent-design]

# Dependency graph
requires:
  - phase: 08-resource-foundation/01
    provides: SpacingResources.xaml, TypographyStyles.xaml, CardAndPanelStyles.xaml shared dictionaries
provides:
  - MainWindow.xaml migrated to shared resource references (typography, spacing, container styles)
  - ConnectionTreeControl.xaml migrated with all off-grid spacing corrected to 4px grid
  - ToastStackControl.xaml using ToastContainerStyle and typography styles
  - ReconnectOverlay.xaml using typography styles and semantic spacing
  - Panel backgrounds differentiated with DeskbridgePanelBackgroundBrush (Layer 1)
affects: [08-resource-foundation/03, 09-tree-view-polish, 10-tab-bar-refinement, 11-quick-properties-overhaul]

# Tech tracking
tech-stack:
  added: []
  patterns: [StaticResource for Style/Thickness, DynamicResource for brushes, semantic fill layer hierarchy]

key-files:
  created: []
  modified:
    - src/Deskbridge/MainWindow.xaml
    - src/Deskbridge/Views/ConnectionTreeControl.xaml
    - src/Deskbridge/Controls/ToastStackControl.xaml
    - src/Deskbridge/Views/ReconnectOverlay.xaml

key-decisions:
  - "ReconnectOverlay uses ui:Card (WPF-UI control) not Border -- CardContainerStyle (Border-targeted) cannot be applied; ui:Card provides equivalent card styling natively"
  - "Empty-state 14px Tertiary text in MainWindow left inline -- no exact named style match (HintStyle is 12px, BodyMutedStyle is Secondary)"
  - "DATA section label Margin='0,16,0,4' left inline -- no exact semantic resource match (combines section top gap with label bottom gap)"

patterns-established:
  - "Named styles replace inline FontSize/Foreground/FontWeight triplets on TextBlock elements"
  - "CardContainerStyle replaces 5-6 inline card Border attributes with a single Style reference"
  - "SeparatorStyle replaces inline Height/Margin/Background separator Borders"
  - "DeskbridgePanelBackgroundBrush establishes Layer 1 fill on icon rail, slide-out panel, tab bar"

requirements-completed: [FOUND-01, FOUND-02, FOUND-03]

# Metrics
duration: 6min
completed: 2026-04-19
---

# Phase 08 Plan 02: Non-Dialog XAML Migration Summary

**Migrated MainWindow, ConnectionTreeControl, ToastStackControl, and ReconnectOverlay from inline styling to shared resource references with 14 off-grid spacing corrections**

## Performance

- **Duration:** 6 min
- **Started:** 2026-04-19T11:00:35Z
- **Completed:** 2026-04-19T11:07:00Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Replaced 37 inline style attributes in MainWindow.xaml with shared resource references (SectionLabelStyle, BodyStyle, SubtitleStyle, FieldLabelStyle, CardContainerStyle, SeparatorStyle, DeskbridgePanelBackgroundBrush, StatusBarContentMargin, PanelContentMargin, SectionLabelMargin, FormFieldSpacing)
- Corrected all 14 off-grid `Margin="0,3"` values to `Margin="0,4"` in ConnectionTreeControl's quick properties grid, plus `Padding="4,2"` to `Padding="4,4"` on group name TextBox
- Applied FieldLabelCompactStyle to 9 quick properties field labels, SectionLabelStyle to PROPERTIES header, PanelHeaderStyle to toggle strip, and BodyStyle/HintStyle to tree item templates
- Migrated ToastStackControl from 6 inline Border attributes to a single ToastContainerStyle reference, with CardTitleStyle and CaptionStyle for typography
- Applied CaptionStyle and CardTitleStyle to ReconnectOverlay with semantic spacing resources

## Task Commits

Each task was committed atomically:

1. **Task 1: Migrate MainWindow.xaml to shared resources** - `d70aa35` (feat)
2. **Task 2: Migrate ConnectionTreeControl, ToastStackControl, ReconnectOverlay** - `4c2f684` (feat)

## Files Created/Modified
- `src/Deskbridge/MainWindow.xaml` - Shell layout migrated: 5 SectionLabelStyle, 2 BodyStyle, 1 SubtitleStyle, 1 FieldLabelStyle, 2 CardContainerStyle, 2 SeparatorStyle, 3 DeskbridgePanelBackgroundBrush, semantic spacing resources
- `src/Deskbridge/Views/ConnectionTreeControl.xaml` - Tree + quick properties: 9 FieldLabelCompactStyle, 1 SectionLabelStyle, 1 PanelHeaderStyle, 1 BodyStyle, 1 HintStyle, all off-grid spacing corrected
- `src/Deskbridge/Controls/ToastStackControl.xaml` - Toast container: 1 ToastContainerStyle, 1 CardTitleStyle, 1 CaptionStyle
- `src/Deskbridge/Views/ReconnectOverlay.xaml` - Reconnect overlay: 1 CaptionStyle, 1 CardTitleStyle, 1 FormFieldLabelMargin, 1 FormFieldSpacing

## Decisions Made
- ReconnectOverlay uses `ui:Card` (a WPF-UI control), not a `Border`. The `CardContainerStyle` targets `Border` and cannot be applied to `ui:Card`. The `ui:Card` already provides equivalent card styling natively, so no change was needed.
- Empty-state text "Ctrl+N to create a connection" (14px Tertiary) was left inline because no named style exactly matches this combination (HintStyle is 12px Tertiary, BodyMutedStyle is 14px Secondary).
- DATA section label `Margin="0,16,0,4"` was left inline because it combines a 16px top gap (section separation from the card above) with a 4px bottom gap (label-to-content), and no single semantic resource matches this composite value.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] ReconnectOverlay CardContainerStyle not applicable**
- **Found during:** Task 2 (ReconnectOverlay migration)
- **Issue:** Plan acceptance criteria expected `StaticResource CardContainerStyle` in ReconnectOverlay.xaml, but the overlay uses `ui:Card` (a WPF-UI control), not a `Border`. Applying a Border-targeted Style to a ui:Card would cause a runtime type mismatch.
- **Fix:** Left `ui:Card` as-is since it already provides card-like styling (background, corner radius, padding, border). Applied typography styles (CaptionStyle, CardTitleStyle) and spacing resources (FormFieldLabelMargin, FormFieldSpacing) instead.
- **Files modified:** src/Deskbridge/Views/ReconnectOverlay.xaml
- **Verification:** Build succeeds with 0 errors, ui:Card renders identically

---

**Total deviations:** 1 auto-fixed (1 bug avoidance)
**Impact on plan:** Minimal -- ui:Card already provides equivalent visual treatment to CardContainerStyle. All other acceptance criteria met.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All four non-dialog XAML files are migrated to shared resources
- Plan 08-03 (dialog XAML migration) can proceed -- remaining dialog files (ConnectionEditorDialog, AuditLogDialog, CrashDialog, etc.) will follow the same migration patterns
- Panel backgrounds now use the semantic fill hierarchy (DeskbridgePanelBackgroundBrush for Layer 1)
- Phase 9 (tree view polish), Phase 10 (tab bar refinement), and Phase 11 (quick properties overhaul) can consume these shared styles

---
*Phase: 08-resource-foundation*
*Completed: 2026-04-19*
