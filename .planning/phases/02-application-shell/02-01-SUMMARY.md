---
phase: 02-application-shell
plan: 01
subsystem: ui
tags: [wpf, wpf-ui, mvvm, communitytoolkit, viewmodel, panel-toggle, tab-management]

# Dependency graph
requires:
  - phase: 01-foundation
    provides: Solution scaffold, DI composition root, MainWindowViewModel stub, GlobalUsings
provides:
  - PanelMode enum (None, Connections, Search, Settings)
  - TabItemViewModel with observable Title, IsActive, ConnectionId
  - Extended MainWindowViewModel with panel toggle state machine, tab management, status bar
  - Custom brushes (DeskbridgeErrorBrush, DeskbridgeSuccessBrush) in App.xaml
  - BooleanToVisibilityConverter in App.xaml
  - Custom accent #007ACC via ApplicationAccentColorManager
  - ISnackbarService and IContentDialogService DI registrations
  - Unit tests for panel toggle, tab operations, and TabItemViewModel
affects: [02-application-shell-plan-02, 03-connection-management, 06-cross-cutting]

# Tech tracking
tech-stack:
  added: []
  patterns: [panel-toggle-state-machine, observable-collection-tab-management, accent-color-override]

key-files:
  created:
    - src/Deskbridge/Models/PanelMode.cs
    - src/Deskbridge/ViewModels/TabItemViewModel.cs
    - tests/Deskbridge.Tests/ViewModels/MainWindowViewModelTests.cs
    - tests/Deskbridge.Tests/ViewModels/TabItemViewModelTests.cs
  modified:
    - src/Deskbridge/ViewModels/MainWindowViewModel.cs
    - src/Deskbridge/App.xaml
    - src/Deskbridge/App.xaml.cs

key-decisions:
  - "ObservableProperty partial properties use public access modifier for cross-class and test accessibility"
  - "ApplicationAccentColorManager.Apply called after ApplicationThemeManager.Apply to override system accent"

patterns-established:
  - "Panel toggle state machine: single PanelMode enum with NotifyPropertyChangedFor on computed bool properties"
  - "Tab management: ObservableCollection with explicit OnPropertyChanged for HasNoTabs after mutations"
  - "Custom semantic brushes defined in App.xaml after MergedDictionaries for DynamicResource consumption"

requirements-completed: [SHEL-01, SHEL-02, SHEL-08]

# Metrics
duration: 8min
completed: 2026-04-11
---

# Phase 2 Plan 01: ViewModel/Model/Resource Layer Summary

**Panel toggle state machine, tab management ViewModel, custom accent #007ACC, and WPF-UI service DI registrations with full unit test coverage**

## Performance

- **Duration:** 8 min
- **Started:** 2026-04-11T15:19:39Z
- **Completed:** 2026-04-11T15:27:59Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments
- PanelMode enum and VS Code-style toggle state machine with computed IsPanelVisible, IsConnectionsActive, IsSearchActive, IsSettingsActive properties
- TabItemViewModel with observable Title/IsActive and Guid ConnectionId for per-tab state
- Extended MainWindowViewModel with Tabs collection, ActiveTab, CloseTab/SwitchTab commands, StatusText/StatusSecondary defaults
- Custom brushes (DeskbridgeErrorBrush #F44747, DeskbridgeSuccessBrush #89D185) and BoolToVisibility converter in App.xaml
- Brand accent #007ACC applied via ApplicationAccentColorManager after theme
- ISnackbarService and IContentDialogService registered in DI for future phases
- 24 new unit tests covering all panel toggle permutations, tab operations, and TabItemViewModel properties (59 total suite)

## Task Commits

Each task was committed atomically:

1. **Task 1: PanelMode enum, TabItemViewModel, MainWindowViewModel extension** - TDD task
   - RED: `857bfbb` (test) - failing tests for panel toggle, tab management, TabItemViewModel
   - GREEN: `38e4c66` (feat) - implement PanelMode, TabItemViewModel, extended MainWindowViewModel
2. **Task 2: Custom brushes, accent colour, WPF-UI services** - `acd28f1` (feat)

## Files Created/Modified
- `src/Deskbridge/Models/PanelMode.cs` - Enum: None, Connections, Search, Settings
- `src/Deskbridge/ViewModels/TabItemViewModel.cs` - Per-tab observable data model
- `src/Deskbridge/ViewModels/MainWindowViewModel.cs` - Extended with panel/tab/status state and commands
- `src/Deskbridge/App.xaml` - Custom brushes and BoolToVisibility converter
- `src/Deskbridge/App.xaml.cs` - Accent colour application and WPF-UI service DI registrations
- `tests/Deskbridge.Tests/ViewModels/MainWindowViewModelTests.cs` - 18 tests for panel toggle and tab management
- `tests/Deskbridge.Tests/ViewModels/TabItemViewModelTests.cs` - 6 tests for TabItemViewModel properties

## Decisions Made
- Used `public partial` (not `private partial`) for [ObservableProperty] declarations that need cross-class access from tests and other ViewModels. CommunityToolkit.Mvvm 8.4+ partial property access modifier defines the generated property's visibility.
- ApplicationAccentColorManager.Apply placed after ApplicationThemeManager.Apply per RESEARCH.md Pitfall 5 to ensure #007ACC overrides system accent.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Changed private partial to public partial on ObservableProperty declarations**
- **Found during:** Task 1 (GREEN phase build)
- **Issue:** Plan specified `private partial` for [ObservableProperty] properties but CommunityToolkit.Mvvm 8.4+ partial properties use the declared access modifier as the generated property's visibility. `private` made IsActive inaccessible from MainWindowViewModel and tests.
- **Fix:** Changed all [ObservableProperty] partial properties to `public partial` in MainWindowViewModel and TabItemViewModel
- **Files modified:** src/Deskbridge/ViewModels/MainWindowViewModel.cs, src/Deskbridge/ViewModels/TabItemViewModel.cs
- **Verification:** Build succeeds, all 59 tests pass
- **Committed in:** 38e4c66 (Task 1 GREEN commit)

---

**Total deviations:** 1 auto-fixed (1 bug fix)
**Impact on plan:** Necessary for correctness. The plan's code snippets used `private partial` following the Phase 1 Title property pattern, but that pattern only works when property access is limited to XAML binding (which uses reflection). C# code access requires public visibility.

## Issues Encountered
- .NET 10 SDK "Question build" feature (MSB3492) caused intermittent build failures when AssemblyInfoInputs.cache files became stale. Workaround: delete obj directories or use `--no-incremental` flag. Not a code issue.
- Running Deskbridge process (PID 29412) locked DLLs during initial build. Killed process to unblock.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All ViewModel contracts are in place for Plan 02 (XAML layout) to bind against
- Panel toggle state machine fully tested and ready for icon rail Command bindings
- Tab management ready for ItemsControl ItemsSource binding
- Custom brushes and accent colour available as DynamicResource
- ISnackbarService/IContentDialogService registered, ready for SnackbarPresenter/ContentPresenter wiring in Plan 02

## Self-Check: PASSED

- All 8 files verified present on disk
- All 3 task commits verified in git log (857bfbb, 38e4c66, acd28f1)
- Build: 0 errors, 0 warnings
- Tests: 59 passed, 0 failed, 0 skipped

---
*Phase: 02-application-shell*
*Completed: 2026-04-11*
