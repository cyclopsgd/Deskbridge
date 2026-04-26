---
phase: 18-settings-infrastructure
plan: 03
subsystem: settings-ui
tags: [wpf-ui, xaml, mvvm, settings, observable-property, auto-save]

# Dependency graph
requires: [18-01]
provides:
  - BULK OPERATIONS settings section in slide-out panel (ToggleSwitch + ComboBox)
  - UNINSTALL settings section in slide-out panel (ToggleSwitch + hint text)
  - Auto-save persistence for bulk operations and uninstall settings
  - Apply-on-load and merge-on-close wiring for new settings
  - ScrollViewer for settings panel scrollability
  - Tiny text scale option in Appearance settings
  - Session limit warning ComboBox (Off/15/20/25/30) replacing NumberBox
affects: [19-savebatch, 23-bulk-ops-ux, 24-uninstall]

# Tech tracking
tech-stack:
  added: []
  patterns: [observable-property-persist-apply-snapshot, session-limit-option-combobox, scrollviewer-settings-panel]

key-files:
  created: []
  modified:
    - src/Deskbridge/ViewModels/MainWindowViewModel.cs
    - src/Deskbridge/MainWindow.xaml
    - src/Deskbridge/MainWindow.xaml.cs
    - src/Deskbridge.Core/Settings/AppSettings.cs
    - src/Deskbridge.Core/Services/TabHostManager.cs

key-decisions:
  - "Replaced NumberBox with ComboBox for session limit warning per user feedback — Off/15/20/25/30 options, 0 = Off sentinel"
  - "Added Tiny text scale (TextScale.Tiny = -4px offset) at end of enum to preserve existing numeric serialization values"
  - "Added ScrollViewer to settings panel with HorizontalScrollBarVisibility=Disabled for text wrapping"
  - "TabHostManager guards threshold > 0 before comparing — 0 means warnings disabled"
  - "Renamed GDI handle warning threshold to Session limit warning for user-friendly labeling"

# Execution
commits:
  - hash: 5b1a0c7
    message: "feat(18-03): add ViewModel properties for bulk operations and uninstall settings"
  - hash: cd7b92d
    message: "feat(18-03): add BULK OPERATIONS and UNINSTALL sections to settings panel XAML"
  - hash: 5dbbee2
    message: "fix(18-03): address visual checkpoint feedback"
  - hash: 709b2cb
    message: "fix(18-03): add TextWrapping to settings panel labels for large text sizes"

duration: ~20min
self-check: PASSED
---

## Summary

Added BULK OPERATIONS and UNINSTALL card sections to the settings slide-out panel with full auto-save persistence. User checkpoint feedback led to replacing the NumberBox with a ComboBox dropdown for session limit warning (Off/15/20/25/30), adding a ScrollViewer for panel scrollability, adding a "Tiny" text scale option to Appearance settings, and ensuring all labels wrap at larger text sizes.

## What was built

- **3 ObservableProperty declarations** on MainWindowViewModel: ConfirmBeforeBulkOperations (bool, default true), GdiWarningThreshold (int, default 15), CleanUpOnUninstall (bool, default false)
- **2 persist methods** (PersistBulkOperationsSettings, PersistUninstallSettings) using the established async-void fire-and-forget pattern
- **2 apply methods** (ApplyBulkOperationsSettings, ApplyUninstallSettings) with shared _suppressPersist guard
- **2 snapshot properties** (CurrentBulkOperationsSettings, CurrentUninstallSettings) for merge-on-close
- **SessionLimitOption record** and static options list for ComboBox binding
- **BULK OPERATIONS XAML section**: ToggleSwitch for confirm + ComboBox for session limit warning (Off/15/20/25/30)
- **UNINSTALL XAML section**: ToggleSwitch for cleanup + HintStyle description with TextWrapping
- **ScrollViewer** wrapping entire settings panel for scrollability
- **Tiny text scale** added to TextScale enum and ApplyTextScale (-4px offset)
- **TabHostManager guard** for threshold=0 (Off) — skips warning when disabled

## Deviations

| # | Type | Description | Justification |
|---|------|-------------|---------------|
| 1 | User feedback | Replaced NumberBox with ComboBox for session limit | User found NumberBox clunky, preferred dropdown with Off option |
| 2 | User feedback | Added ScrollViewer to settings panel | Content cut off when window minimized |
| 3 | User feedback | Added Tiny text scale option | User requested additional smaller text size |
| 4 | User feedback | Added TextWrapping to all settings labels | Text cut off at larger text sizes |
| 5 | User feedback | Renamed "GDI handle warning threshold" to "Session limit warning" | GDI is jargon, user-friendly label preferred |
| 6 | Scope expansion | TabHostManager threshold > 0 guard | Required for "Off" ComboBox option to work correctly |
