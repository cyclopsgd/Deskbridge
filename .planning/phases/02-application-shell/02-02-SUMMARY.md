---
phase: 02-application-shell
plan: 02
subsystem: ui
tags: [wpf, wpf-ui, xaml, fluent-design, layout, icon-rail, panel, tab-bar, status-bar, viewport]

# Dependency graph
requires:
  - phase: 02-application-shell
    plan: 01
    provides: MainWindowViewModel with panel toggle, tab management, status bar state; PanelMode enum; TabItemViewModel; custom brushes; BoolToVisibility converter; ISnackbarService and IContentDialogService DI registrations
provides:
  - Complete VS Code-style shell layout in MainWindow.xaml (icon rail, slide-out panel, tab bar, viewport, status bar)
  - SnackbarPresenter and ContentDialogHost pre-wired for future phases
  - Code-behind with ISnackbarService and IContentDialogService constructor injection
  - Ctrl+W keyboard shortcut for closing active tab
affects: [03-connection-management, 04-rdp-integration, 05-tab-management, 06-cross-cutting]

# Tech tracking
tech-stack:
  added: []
  patterns: [vs-code-grid-layout, auto-column-panel-visibility, content-control-data-triggers, custom-itemscontrol-tab-bar]

key-files:
  created: []
  modified:
    - src/Deskbridge/MainWindow.xaml
    - src/Deskbridge/MainWindow.xaml.cs

key-decisions:
  - "Use ui:ContentDialogHost instead of ContentPresenter for dialog host (ContentPresenter variant deprecated in WPF-UI 4.2.0)"
  - "DockPanel.Dock=Right on StatusSecondary TextBlock placed before StatusText to achieve left/right DockPanel layout"

patterns-established:
  - "VS Code grid layout: root 3 rows (Auto title bar, * content, 22 status bar), content 3 columns (36 icon rail, Auto panel, * editor)"
  - "Panel visibility via Auto column: fixed Width=240 on Border inside Width=Auto ColumnDefinition, Visibility bound to IsPanelVisible with BoolToVisibility converter"
  - "Icon rail active indicator: DataTrigger on IsXxxActive sets 2px left BorderBrush to SystemAccentColorPrimaryBrush"
  - "Panel content switching: ContentControl with DataTriggers on ActivePanelMode to swap content per panel type"
  - "Tab bar: ItemsControl with horizontal StackPanel ItemsPanel, DataTemplate with accent top border, MiddleClick MouseBinding for close"

requirements-completed: [SHEL-03, SHEL-04, SHEL-05, SHEL-06, SHEL-07]

# Metrics
duration: 4min
completed: 2026-04-11
---

# Phase 2 Plan 02: Shell Layout Summary

**VS Code-style XAML shell with 36px icon rail, 240px slide-out panel, 30px tab bar, viewport with empty-state branding, and 22px accent status bar -- all bound to ViewModel state machine**

## Performance

- **Duration:** 4 min
- **Started:** 2026-04-11T15:31:29Z
- **Completed:** 2026-04-11T15:35:15Z
- **Tasks:** 1 of 2 (Task 2 is checkpoint:human-verify -- pending)
- **Files modified:** 2

## Accomplishments
- Complete VS Code-style shell layout replacing Phase 1 placeholder content
- Icon rail with PlugConnected24, Search24, Settings24 icons, 2px accent left border on active icon via DataTriggers
- 240px slide-out panel with instant visibility toggle (Auto column collapse), panel header and content switching via ContentControl DataTriggers
- Custom tab bar with ItemsControl, horizontal scroll overflow, active accent top border, middle-click close, and Dismiss16 close button
- 22px status bar with SystemAccentColorPrimaryBrush background and TextOnAccentFillColorPrimary text
- Viewport with centered empty-state branding ("Deskbridge" / "Ctrl+N to create a connection"), bound to HasNoTabs
- SnackbarPresenter and ContentDialogHost as last root Grid children for overlay Z-order
- ISnackbarService and IContentDialogService wired in code-behind constructor
- Ctrl+W keyboard shortcut bound to CloseTabCommand with ActiveTab parameter

## Task Commits

Each task was committed atomically:

1. **Task 1: Replace MainWindow.xaml placeholder with full VS Code-style shell layout** - `d9d669e` (feat)

**Checkpoint pending:** Task 2 (visual verification) awaits human review.

## Files Created/Modified
- `src/Deskbridge/MainWindow.xaml` - Complete shell layout with all 5 regions, keyboard shortcuts, overlay hosts
- `src/Deskbridge/MainWindow.xaml.cs` - Code-behind with ISnackbarService and IContentDialogService DI wiring

## Decisions Made
- Used `ui:ContentDialogHost` instead of `ContentPresenter` for dialog host because WPF-UI 4.2.0 marks `SetDialogHost(ContentPresenter)` as obsolete with CS0618 error. The new `ContentDialogHost` control provides better modal features.
- Placed `DockPanel.Dock="Right"` TextBlock before the non-docked TextBlock in the status bar DockPanel, following WPF DockPanel convention where docked items must precede fill items.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Use ContentDialogHost instead of ContentPresenter for dialog host**
- **Found during:** Task 1 (build verification)
- **Issue:** Plan specified `<ContentPresenter x:Name="RootContentDialog" />` but WPF-UI 4.2.0 marks `IContentDialogService.SetDialogHost(ContentPresenter)` as obsolete (CS0618 error, not just warning). Build fails.
- **Fix:** Changed XAML to `<ui:ContentDialogHost x:Name="RootContentDialog" />` which uses the non-deprecated `SetDialogHost(ContentDialogHost)` overload
- **Files modified:** src/Deskbridge/MainWindow.xaml
- **Verification:** `dotnet build` succeeds with 0 errors, 0 warnings
- **Committed in:** d9d669e (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug fix)
**Impact on plan:** Necessary for build success. WPF-UI 4.2.0 treats the deprecated API as error-level, not warning. No scope creep.

## Issues Encountered
- .NET 10 SDK "Question build" feature (MSB3492) caused initial build failure with stale AssemblyInfoInputs.cache. Resolved with `--no-incremental` flag. Known issue from Plan 01.

## User Setup Required

None - no external service configuration required.

## Checkpoint Pending

**Task 2 (checkpoint:human-verify)** requires visual verification of the complete application shell. The user must:

1. Launch the app: `cd C:/Users/cyclo/projects/Deskbridge && dotnet run --project src/Deskbridge/`
2. Verify all 5 layout regions are visible (icon rail, panel, tab bar, viewport, status bar)
3. Test icon rail toggle behavior (open/close/switch panels)
4. Verify accent color on status bar and active indicators
5. Check window resize behavior and snap layouts

## Next Phase Readiness
- All layout regions are in place and bound to the ViewModel state machine from Plan 01
- SnackbarPresenter and ContentDialogHost are wired and ready for Phases 3+ and 6
- Tab bar ItemsControl ready for real tab data when connection management ships in Phase 3
- Panel content areas are placeholder TextBlocks ready to be replaced with UserControls in Phase 3

## Self-Check: PASSED

- All 2 modified files verified present on disk (MainWindow.xaml, MainWindow.xaml.cs)
- Task 1 commit verified in git log (d9d669e)
- Build: 0 errors, 0 warnings
- Tests: 59 passed, 0 failed, 0 skipped
- Task 2 checkpoint:human-verify pending user action

---
*Phase: 02-application-shell*
*Completed: 2026-04-11 (Task 1 only; Task 2 checkpoint pending)*
