---
phase: 07-update-migration
plan: 01
subsystem: update
tags: [velopack, auto-update, status-bar, dialog]
dependency_graph:
  requires: []
  provides: [IUpdateService, UpdateService, UpdateConfirmDialog, UpdateSettingsRecord]
  affects: [MainWindowViewModel, MainWindow, AppSettings, App.xaml.cs]
tech_stack:
  added: []
  patterns: [virtual-seam-testing, event-bus-subscription, dialog-callback]
key_files:
  created:
    - src/Deskbridge.Core/Interfaces/IUpdateService.cs
    - src/Deskbridge.Core/Services/UpdateService.cs
    - src/Deskbridge/Dialogs/UpdateConfirmDialog.xaml
    - src/Deskbridge/Dialogs/UpdateConfirmDialog.xaml.cs
    - tests/Deskbridge.Tests/Update/UpdateServiceTests.cs
  modified:
    - src/Deskbridge.Core/Deskbridge.Core.csproj
    - src/Deskbridge.Core/Settings/AppSettings.cs
    - src/Deskbridge/App.xaml.cs
    - src/Deskbridge/MainWindow.xaml
    - src/Deskbridge/MainWindow.xaml.cs
    - src/Deskbridge/ViewModels/MainWindowViewModel.cs
    - tests/Deskbridge.Tests/DiCompositionTests.cs
    - tests/Deskbridge.Tests/Notifications/WindowStateServiceTests.cs
decisions:
  - Velopack NuGet added to Deskbridge.Core (not just exe) so UpdateService lives alongside other Core services
  - Protected constructor + virtual seams on UpdateService for testability without VelopackApp.Build() runtime
  - WPF-UI ProgressRing used as indeterminate spinner (no Value property) with separate percentage TextBlock
  - UpdateService public property on MainWindowViewModel for MainWindow restart flow (avoids reflection)
  - Source-grep DiComposition tests for IUpdateService (real UpdateManager requires VelopackApp.Build())
metrics:
  duration: 14min
  completed: 2026-04-17
  tasks: 3
  files: 13
---

# Phase 07 Plan 01: Auto-Update Service Layer Summary

Velopack UpdateManager wrapper with dev-mode guard, status bar badge, and restart confirmation dialog protecting active RDP sessions.

## What Was Built

### Task 1: IUpdateService + UpdateService + AppSettings Extension + Tests
- **IUpdateService** interface with `CheckForUpdatesAsync`, `DownloadUpdatesAsync`, `ApplyUpdatesAndRestart`, `IsInstalled`, `PendingVersion`
- **UpdateService** wraps Velopack `UpdateManager` with `GithubSource` configured for `cyclopsgd/Deskbridge` repo
  - Dev-mode guard: `IsInstalled` returns false when not installed via Velopack; all operations log warning and return
  - Publishes `UpdateAvailableEvent` on event bus when update found
  - `ExplicitChannel` set to `"beta"` or `"stable"` based on `UpdateSettingsRecord.UseBetaChannel`
  - Exception handling: `CheckForUpdatesAsync` catches all exceptions and returns false (T-07-02 mitigation)
  - Protected constructor + virtual seams (`CheckForUpdatesInternalAsync`, `DownloadUpdatesInternalAsync`, `ApplyUpdatesInternalAndRestart`) for test subclassing without Velopack runtime
- **UpdateSettingsRecord** added to AppSettings as third positional parameter with `UseBetaChannel` toggle (default: false)
- **Velopack** NuGet added to `Deskbridge.Core.csproj`
- **7 unit tests** via `TestableUpdateService` subclass: dev-mode guard, event publication, exception resilience, progress reporting, no-op on missing pending update
- Existing `WindowStateServiceTests` updated for 3-parameter `AppSettings` constructor

### Task 2: Status Bar Update Badge + ViewModel Integration
- **MainWindowViewModel** extended with:
  - `UpdateAvailable`, `UpdateVersion`, `DownloadProgress`, `IsDownloading` observable properties
  - `UpdateAvailableEvent` subscription in constructor (marshals to UI via `Dispatch`)
  - `CheckForUpdatesCommand` for startup-triggered check
  - `ApplyUpdateCommand` downloads with `IProgress<int>`, then invokes confirmation callback
  - `DismissUpdateCommand` hides badge without applying
  - `SetUpdateConfirmation` callback and `UpdateService` public property
- **MainWindow.xaml** status bar updated with:
  - `ArrowDownload24` icon + version text badge (DockPanel.Dock="Right", BoolToVisibility converter)
  - Indeterminate `ProgressRing` + percentage text during download
  - Badge tooltip: "Update v{0} available -- click to download"

### Task 3: UpdateConfirmDialog + DI Registration + Startup Check
- **UpdateConfirmDialog** (ContentDialog subclass):
  - "Restart Now" primary button / "Later" close button
  - InfoBar with Severity="Caution" warning: "Active sessions will be disconnected"
  - BasedOn style for proper WPF-UI theming (Pitfall 1)
- **DI registration** in App.xaml.cs:
  - `IUpdateService` as singleton factory loading `UpdateSettingsRecord` from settings.json
  - `IUpdateService` injected into `MainWindowViewModel` factory
- **Startup check**: `Task.Run(() => updateService.CheckForUpdatesAsync())` after `mainWindow.Show()`
- **Confirmation wiring**: `MainWindow.xaml.cs` sets `SetUpdateConfirmation` callback to show dialog; on Primary result calls `ApplyUpdatesAndRestart()`
- **DiCompositionTests**: 2 source-grep tests verify DI registration and startup ordering

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] VelopackApp.Build() required for UpdateManager constructor**
- **Found during:** Task 1 TDD RED phase
- **Issue:** Velopack `UpdateManager` constructor throws `InvalidOperationException: No VelopackLocator has been set` when `VelopackApp.Build()` hasn't been called (test environment)
- **Fix:** Added protected constructor `UpdateService(IEventBus bus)` that skips UpdateManager creation; `TestableUpdateService` uses this constructor. Production constructor retains full UpdateManager setup.
- **Files modified:** `UpdateService.cs`, `UpdateServiceTests.cs`
- **Commit:** 8a10508

**2. [Rule 1 - Bug] WPF-UI ProgressRing has no Value property**
- **Found during:** Task 2 build
- **Issue:** Plan specified `<ui:ProgressRing Value="{Binding DownloadProgress}"` but WPF-UI 4.2.0 ProgressRing does not expose a `Value` property (MC3072 XAML parse error)
- **Fix:** Changed to `IsIndeterminate="True"` spinner with percentage shown in adjacent TextBlock. Download progress is still tracked via DownloadProgress property for the text display.
- **Files modified:** `MainWindow.xaml`
- **Commit:** d08ee5a

**3. [Rule 3 - Blocking] AppSettings constructor breaking change**
- **Found during:** Task 1
- **Issue:** Adding `UpdateSettingsRecord` as third positional parameter to `AppSettings` record broke existing 2-argument constructor calls in `WindowStateServiceTests`
- **Fix:** Updated test call sites to pass `UpdateSettingsRecord.Default` as third argument. Parameterless `new AppSettings()` constructor and `with` expressions were unaffected.
- **Files modified:** `WindowStateServiceTests.cs`
- **Commit:** 8a10508

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 | 8a10508 | IUpdateService + UpdateService + UpdateSettingsRecord + tests |
| 2 | d08ee5a | Status bar update badge + MainWindowViewModel integration |
| 3 | 7cf1501 | UpdateConfirmDialog + DI registration + startup check |

## Test Results

- **Total:** 508 passed, 0 failed, 3 skipped (RDP smoke tests)
- **New tests:** 9 (7 UpdateServiceTests + 2 DiCompositionTests)
- All pre-existing tests continue to pass

## Self-Check: PASSED

All 5 created files verified on disk. All 3 commit hashes verified in git log.
