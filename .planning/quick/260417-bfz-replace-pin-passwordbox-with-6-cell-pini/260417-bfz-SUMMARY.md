---
phase: quick
plan: 260417-bfz
subsystem: ui/lock-overlay
tags: [pin-input, lock-overlay, user-control, security]
dependency_graph:
  requires: [06-04]
  provides: [pin-input-control]
  affects: [lock-overlay-dialog]
tech_stack:
  added: []
  patterns: [custom-usercontrol, dependency-property, digit-masking, auto-advance]
key_files:
  created:
    - src/Deskbridge/Controls/PinInputControl.xaml
    - src/Deskbridge/Controls/PinInputControl.xaml.cs
    - tests/Deskbridge.Tests/Controls/PinInputControlTests.cs
  modified:
    - src/Deskbridge/Dialogs/LockOverlayDialog.xaml
    - src/Deskbridge/Dialogs/LockOverlayDialog.xaml.cs
decisions:
  - Standard WPF TextBox cells (not ui:TextBox) to avoid WPF-UI template overhead on single-char inputs
  - Bullet masking via private char[] backing array with U+2022 display (T-pin-01)
  - MultiDataTrigger for confirm field visibility (IsFirstRun AND IsPinMode conditions)
  - PinComplete auto-submit only in unlock mode (not first-run where confirm is needed)
metrics:
  duration: 5min
  completed: 2026-04-17
  tasks: 2
  files: 5
---

# Quick Task 260417-bfz: Replace PIN PasswordBox with 6-cell PinInputControl Summary

6-cell masked PIN input UserControl with auto-advance, backspace-back, paste-distribute, and digits-only filtering, integrated into LockOverlayDialog via Visibility DataTriggers

## Tasks Completed

| # | Task | Commit | Key Changes |
|---|------|--------|-------------|
| 1 | Create PinInputControl UserControl (TDD) | ab7f560, 5053296 | PinInputControl.xaml/.cs with Pin DP, 6-cell TextBox layout, bullet masking, static helpers; 10 unit tests |
| 2 | Swap PinInputControl into LockOverlayDialog | eee927f | Dual-control visibility toggling, confirm PIN field, Pitfall 8 TextBox guard, PinComplete auto-submit |

## Implementation Details

### PinInputControl (new)

- **XAML**: 6 named TextBox cells (Cell0-Cell5) in horizontal StackPanel, each 44x44px with CornerRadius=4 via custom ControlTemplate
- **Masking (T-pin-01)**: Real digits stored in private `char[6] _digits`; TextBox.Text only ever shows bullet character U+2022
- **Pin DP**: `string Pin` with `BindsTwoWayByDefault` and PropertyChangedCallback for external clear/set
- **Input handling**: PreviewTextInput filters non-digits, auto-advances on valid input; PreviewKeyDown handles Backspace (clear/move-back), Delete, Left/Right arrows
- **Paste (T-pin-02)**: DataObject.Pasting handler extracts clipboard text, filters via DistributeDigits (rejects any non-digit content), distributes across cells
- **PinComplete event**: Raised when all 6 cells filled, used for auto-submit in unlock mode
- **Static helpers**: `IsDigit`, `DistributeDigits`, `AssemblePin` are `internal static` for direct unit testing via InternalsVisibleTo

### LockOverlayDialog (modified)

- **Password field**: PasswordBox visible when IsPinMode=False (DataTrigger collapses on True); PinInputControl visible when IsPinMode=True
- **Confirm field**: PasswordBox visible when IsFirstRun=True AND IsPinMode=False (MultiDataTrigger); PinInputControl visible when IsFirstRun=True AND IsPinMode=True
- **Pitfall 8**: Dialog_PreviewKeyDown extended to handle `System.Windows.Controls.TextBox` (PIN cell type) in addition to PasswordBox types
- **Auto-submit**: PinField.PinComplete wired to execute UnlockCommand when not in first-run mode
- **Mode switching**: Clears both PasswordBox.Password and PinInputControl.Clear() on IsPinMode change
- **Focus routing**: OnLoaded and RequestFocusPassword route to PinField.FocusFirst() in PIN mode

## Deviations from Plan

None -- plan executed exactly as written.

## Verification Results

- `dotnet build src/Deskbridge/Deskbridge.csproj -c Release -warnaserror` -- 0 warnings, 0 errors
- `dotnet test tests/Deskbridge.Tests --no-restore` -- 502 tests passed (0 failed, 3 skipped)
- LockOverlayDialog_HasPitfall8EnterHandler source-grep test passes (Dialog_PreviewKeyDown + Key.Enter + PasswordBox + UnlockCommand + e.Handled present)
- All existing LockOverlayViewModelTests pass unchanged

## Self-Check: PASSED

- [x] src/Deskbridge/Controls/PinInputControl.xaml exists
- [x] src/Deskbridge/Controls/PinInputControl.xaml.cs exists
- [x] tests/Deskbridge.Tests/Controls/PinInputControlTests.cs exists
- [x] Commit ab7f560 exists (TDD RED)
- [x] Commit 5053296 exists (TDD GREEN)
- [x] Commit eee927f exists (Task 2)
