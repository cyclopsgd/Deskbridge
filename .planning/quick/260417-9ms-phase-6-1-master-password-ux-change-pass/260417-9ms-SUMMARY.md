---
phase: "6.1"
plan: 01
subsystem: security
tags: [master-password, pin, change-password, disable-password, settings-panel]
dependency_graph:
  requires: [phase-06-04]
  provides: [pin-auth-mode, change-password-dialog, disable-password-toggle]
  affects: [auth-schema, lock-overlay, settings-panel, lock-services]
tech_stack:
  added: []
  patterns: [cascading-no-op, runtime-guard-update, confirmation-dialog]
key_files:
  created:
    - src/Deskbridge/ViewModels/ChangePasswordViewModel.cs
    - src/Deskbridge/Dialogs/ChangePasswordDialog.xaml
    - src/Deskbridge/Dialogs/ChangePasswordDialog.xaml.cs
    - tests/Deskbridge.Tests/Security/ChangePasswordViewModelTests.cs
  modified:
    - src/Deskbridge.Core/Models/AuthFile.cs
    - src/Deskbridge.Core/Interfaces/IMasterPasswordService.cs
    - src/Deskbridge.Core/Services/MasterPasswordService.cs
    - src/Deskbridge.Core/Settings/AppSettings.cs
    - src/Deskbridge/ViewModels/LockOverlayViewModel.cs
    - src/Deskbridge/Dialogs/LockOverlayDialog.xaml
    - src/Deskbridge/Dialogs/LockOverlayDialog.xaml.cs
    - src/Deskbridge/ViewModels/MainWindowViewModel.cs
    - src/Deskbridge/MainWindow.xaml
    - src/Deskbridge/MainWindow.xaml.cs
    - src/Deskbridge/App.xaml.cs
    - src/Deskbridge/Services/AppLockController.cs
    - src/Deskbridge/Services/IdleLockService.cs
    - src/Deskbridge/Services/SessionLockService.cs
    - tests/Deskbridge.Tests/Security/MasterPasswordServiceTests.cs
    - tests/Deskbridge.Tests/Security/LockOverlayViewModelTests.cs
decisions:
  - "AuthMode stored as string ('password'/'pin') not enum -- backward-compatible JSON deserialization"
  - "PIN confirmation field kept in first-run (not omitted for brevity) -- consistency with password flow"
  - "ChangePasswordDialog uses OnButtonClick override pattern (WPF-UI 4.2.0 API) not PrimaryButtonClick event"
  - "AppLockController.RequireMasterPassword is mutable at runtime; IdleLockService/SessionLockService require restart"
  - "Disable toggle shows simple confirmation dialog (not inline password entry) -- security vs UX tradeoff"
metrics:
  duration: 13min
  completed: "2026-04-17T06:17:26Z"
  tasks: 3
  files: 20
---

# Phase 6.1 Plan 01: Master Password UX Enhancements Summary

PIN mode alternative, change password/PIN dialog, and disable-password toggle with cascading no-op across all lock services.

## Commits

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | PIN mode in auth schema + lock overlay | 4af2bdf | AuthFile.cs, IMasterPasswordService.cs, MasterPasswordService.cs, LockOverlayViewModel.cs, LockOverlayDialog.xaml |
| 2 | Change password/PIN dialog in Settings | aac96a4 | ChangePasswordViewModel.cs, ChangePasswordDialog.xaml/.cs, MainWindowViewModel.cs, MainWindow.xaml |
| 3 | Disable master password toggle + cascading no-op | f99573a | AppSettings.cs, AppLockController.cs, IdleLockService.cs, SessionLockService.cs, MainWindow.xaml/.cs |

## What Was Built

### Task 1: PIN Mode
- AuthFile record extended with `AuthMode` field (default `"password"`, backward-compatible with existing installs)
- IMasterPasswordService: `SetMasterPassword(string, string)`, `GetAuthMode()`, `DeleteAuthFile()`
- Lock overlay shows Password/PIN radio selector in first-run mode, reads stored mode in unlock mode
- PIN validation: exactly 6 digits; password validation: 8+ chars (unchanged)
- MaxLength DataTrigger on PasswordBox: 6 for PIN, unlimited for password
- Mode switch clears fields and refocuses (code-behind PropertyChanged handler)

### Task 2: Change Password/PIN Dialog
- ChangePasswordViewModel: validates current password, validates new per mode, writes new hash
- ChangePasswordDialog: 3 PasswordBox fields with PIN MaxLength DataTriggers, Pitfall 8 Enter handler
- Uses WPF-UI 4.2.0 `OnButtonClick` override (not `PrimaryButtonClick` event which doesn't exist)
- Settings panel: "Change Password"/"Change PIN" button below lock-on-minimise toggle
- DI: transient VM + dialog + factory, MainWindow factory registration with all params

### Task 3: Disable Toggle
- `SecuritySettingsRecord.RequireMasterPassword` (default `true`, backward-compatible)
- Settings panel: "Require password/PIN" toggle above auto-lock controls
- Auto-lock timeout, lock-on-minimise, and Change Password button wrapped in `IsEnabled` binding
- Toggle OFF: confirmation dialog, then `DeleteAuthFile()` + update AppLockController at runtime
- Toggle ON (no password): triggers first-run lock overlay via `AppLockedEvent`
- `AppLockController.LockAsync`: guard returns early when disabled
- `IdleLockService`: skips timer start and InputManager subscription when disabled
- `SessionLockService`: skips SystemEvents.SessionSwitch subscription when disabled
- Ctrl+L effectively no-op via controller guard (no router change needed)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] ChangePasswordDialog PrimaryButtonClick API mismatch**
- **Found during:** Task 2
- **Issue:** Plan specified `PrimaryButtonClick` event and `args.Cancel` which don't exist in WPF-UI 4.2.0
- **Fix:** Used `OnButtonClick` virtual override pattern (same as ConnectionEditorDialog) -- not calling `base.OnButtonClick` prevents close on validation failure
- **Files modified:** src/Deskbridge/Dialogs/ChangePasswordDialog.xaml.cs

**2. [Rule 3 - Blocking] Missing Wpf.Ui.Extensions using for ShowSimpleDialogAsync**
- **Found during:** Task 3
- **Issue:** `ShowSimpleDialogAsync` is an extension method requiring `using Wpf.Ui.Extensions`
- **Fix:** Added the using directive to MainWindow.xaml.cs
- **Files modified:** src/Deskbridge/MainWindow.xaml.cs

**3. [Rule 1 - Bug] Test assertions for SetMasterPassword overload**
- **Found during:** Task 1
- **Issue:** Existing tests 5, 6, 7 in LockOverlayViewModelTests checked `DidNotReceiveWithAnyArgs().SetMasterPassword(default!)` which only matched the one-param overload. Test 7 asserted `SetMasterPassword("goodpassword")` but the code now calls the two-param version.
- **Fix:** Updated test 5/6 to `DidNotReceiveWithAnyArgs().SetMasterPassword(default!, default!)` and test 7 to `Received(1).SetMasterPassword("goodpassword", "password")`
- **Files modified:** tests/Deskbridge.Tests/Security/LockOverlayViewModelTests.cs

## Test Results

- **Total tests:** 480 passed, 0 failed, 3 skipped
- **New tests added:** 19 (6 MasterPasswordService + 5 LockOverlayViewModel + 8 ChangePasswordViewModel)
- All existing tests continue to pass

## Self-Check: PASSED

- All 4 created files exist on disk
- All 3 task commits verified in git log (4af2bdf, aac96a4, f99573a)
- Build: 0 warnings, 0 errors
- Tests: 480 passed, 0 failed, 3 skipped
