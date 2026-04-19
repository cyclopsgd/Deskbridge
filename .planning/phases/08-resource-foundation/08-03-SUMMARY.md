---
phase: 08-resource-foundation
plan: 03
subsystem: ui
tags: [wpf, xaml, resource-dictionary, typography-styles, spacing, refactor]

# Dependency graph
requires:
  - phase: 08-resource-foundation (plan 01)
    provides: Shared resource dictionaries (SpacingResources.xaml, TypographyStyles.xaml, CardAndPanelStyles.xaml)
provides:
  - All 9 dialog XAML files migrated from inline styles to shared resource references
  - 60 shared resource references replacing ~80 inline style occurrences
  - Complete Phase 8 migration across all 13 XAML surfaces (4 shell + 9 dialogs)
affects: [09-depth-hierarchy, 10-treeview-polish, 11-tab-bar-refinement, 12-general-polish]

# Tech tracking
tech-stack:
  added: []
  patterns: [StaticResource for Thickness/Style, DynamicResource for brushes, FieldLabelStyle for form field labels, FormFieldSpacing for field bottom margins, DialogContentMargin for dialog panel padding]

key-files:
  created: []
  modified:
    - src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml
    - src/Deskbridge/Dialogs/GroupEditorDialog.xaml
    - src/Deskbridge/Dialogs/CommandPaletteDialog.xaml
    - src/Deskbridge/Dialogs/CredentialPromptDialog.xaml
    - src/Deskbridge/Dialogs/ChangePasswordDialog.xaml
    - src/Deskbridge/Dialogs/LockOverlayDialog.xaml
    - src/Deskbridge/Dialogs/ImportWizardDialog.xaml
    - src/Deskbridge/Dialogs/CrashDialog.xaml
    - src/Deskbridge/Dialogs/UpdateConfirmDialog.xaml

key-decisions:
  - "ImportWizardDialog step headers left with inline FontWeight=SemiBold -- no named style matches the default inherited FontSize with SemiBold weight, applying CardTitleStyle would change visual size to 16px"
  - "ImportWizardDialog tree protocol tags (11px secondary non-bold) left inline -- SectionLabelStyle adds SemiBold weight not present in original"
  - "ChangePasswordDialog field labels with Margin=0,12,0,4 use FieldLabelStyle with explicit Margin override -- the 12px top spacing serves as inter-field gap and doesn't match any named resource"

patterns-established:
  - "Dialog field labels: Style={StaticResource FieldLabelStyle} replaces FontSize=14 + TextFillColorSecondaryBrush + Margin=0,0,0,4 triple"
  - "Dialog form fields: Margin={StaticResource FormFieldSpacing} replaces Margin=0,0,0,12"
  - "Dialog content panels: Margin={StaticResource DialogContentMargin} replaces Margin=16,12"
  - "Error messages: Style={StaticResource ErrorTextStyle} replaces FontSize=12 + DeskbridgeErrorBrush + TextWrapping=Wrap"

requirements-completed: [FOUND-01, FOUND-02]

# Metrics
duration: 5min
completed: 2026-04-19
---

# Phase 08 Plan 03: Dialog XAML Migration to Shared Resources Summary

**60 inline style occurrences across 9 dialog files replaced with shared FieldLabelStyle, FormFieldSpacing, typography styles, and semantic spacing resources**

## Performance

- **Duration:** 5 min
- **Started:** 2026-04-19T11:09:49Z
- **Completed:** 2026-04-19T11:15:12Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments
- Eliminated all inline FontSize="14" + TextFillColorSecondaryBrush + Margin="0,0,0,4" field label patterns from 9 dialog files (replaced with FieldLabelStyle)
- Replaced 60 inline style values with shared resource references across all dialog XAML files
- Completed Phase 8 resource migration across all 13 XAML surfaces (4 shell files in plan 02 + 9 dialogs in this plan)

## Task Commits

Each task was committed atomically:

1. **Task 1: Migrate ConnectionEditorDialog, GroupEditorDialog, CommandPaletteDialog, CredentialPromptDialog, ChangePasswordDialog** - `c45d8b8` (refactor)
2. **Task 2: Migrate LockOverlayDialog, ImportWizardDialog, CrashDialog, UpdateConfirmDialog** - `721c79c` (refactor)

## Files Created/Modified
- `src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml` - 23 shared resource refs (9 FieldLabelStyle, 9 FormFieldSpacing, 4 DialogContentMargin, 1 HintStyle)
- `src/Deskbridge/Dialogs/GroupEditorDialog.xaml` - 15 shared resource refs (5 FieldLabelStyle, 4 FormFieldSpacing, 1 SeparatorStyle, 1 CardTitleStyle, 2 CaptionStyle, 1 DialogContentMargin)
- `src/Deskbridge/Dialogs/CommandPaletteDialog.xaml` - 2 shared resource refs (1 BodyStyle, 1 CaptionStyle)
- `src/Deskbridge/Dialogs/CredentialPromptDialog.xaml` - 9 shared resource refs (3 FieldLabelStyle, 3 FormFieldSpacing, 1 CardTitleStyle, 1 SectionSpacing, 1 FormFieldLabelMargin)
- `src/Deskbridge/Dialogs/ChangePasswordDialog.xaml` - 4 shared resource refs (3 FieldLabelStyle, 1 ErrorTextStyle)
- `src/Deskbridge/Dialogs/LockOverlayDialog.xaml` - 3 shared resource refs (1 SubtitleStyle, 1 BodyMutedStyle, 1 ErrorTextStyle)
- `src/Deskbridge/Dialogs/ImportWizardDialog.xaml` - 2 shared resource refs (1 CaptionStyle, 1 BodyMutedStyle)
- `src/Deskbridge/Dialogs/CrashDialog.xaml` - 1 shared resource ref (1 BodyStyle)
- `src/Deskbridge/Dialogs/UpdateConfirmDialog.xaml` - 1 shared resource ref (1 BodyStyle)

## Decisions Made
- ImportWizardDialog step headers kept inline FontWeight="SemiBold" -- CardTitleStyle (16px) would change visual size from default (~12px). Only replace exact matches per plan rules.
- ImportWizardDialog tree protocol text (11px secondary) kept inline -- SectionLabelStyle adds SemiBold weight not in original.
- ChangePasswordDialog field labels use FieldLabelStyle with explicit Margin="0,12,0,4" override -- the compound margin (12px top gap + 4px bottom gap) doesn't match any named resource.

## Deviations from Plan

### Minor Acceptance Criteria Variance

**1. ImportWizardDialog step headers not converted to CardTitleStyle**
- **Reason:** Step headers have no explicit FontSize (inheriting default ~12px) with only FontWeight="SemiBold". CardTitleStyle is 16px SemiBold -- applying it would visually enlarge all step headers, violating the "refactor not redesign" principle.
- **Impact:** None on consistency -- these headers use a unique pattern not repeated elsewhere. The plan's critical rule "only replace exact matches" takes precedence.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 8 (Resource Foundation) is fully complete -- all 3 plans executed
- All 13 XAML files migrated to shared resource dictionaries
- Phases 9 (Depth/Hierarchy), 10 (TreeView Polish), 11 (Tab Bar Refinement) can proceed in parallel
- Phase 12 (General Polish) depends on 9, 10, 11 completing

## Self-Check: PASSED

- All 9 dialog files: FOUND
- SUMMARY.md: FOUND
- Commit c45d8b8: FOUND
- Commit 721c79c: FOUND

---
*Phase: 08-resource-foundation*
*Completed: 2026-04-19*
