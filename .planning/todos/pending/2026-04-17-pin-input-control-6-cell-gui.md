---
created: 2026-04-17T06:10:00Z
title: Replace PIN PasswordBox with 6-cell PinInputControl
area: ui
files:
  - src/Deskbridge/Dialogs/LockOverlayDialog.xaml
  - src/Deskbridge/Controls/PinInputControl.xaml
  - src/Deskbridge/Controls/PinInputControl.xaml.cs
---

## Problem

PIN mode currently uses a single PasswordBox with MaxLength=6. User wants the "nice PIN GUI" — 6 separate digit cells in a horizontal row, like Windows Hello PIN or phone lock screens.

## Solution

Create a custom `PinInputControl` UserControl:
- 6 individual single-character TextBox cells in a horizontal StackPanel
- Each cell: fixed width (~40px), centered text, large font, border, monospace
- Auto-advance to next cell on digit keystroke
- Backspace goes back to previous cell and clears it
- Paste support: distribute 6-digit paste across all cells
- InputScope=Number or PreviewTextInput filter for digits only
- Exposes a `Pin` dependency property (string, 6 chars) for binding to VM
- Focus ring on current cell via FocusVisualStyle
- Masked display (dots) like PasswordBox — or show digits briefly then mask (configurable)

Swap into LockOverlayDialog.xaml via DataTrigger on IsPinMode — show PinInputControl when PIN, show PasswordBox when password. Backend (PBKDF2, VM, auth.json) stays identical.

Run as `/gsd-quick` — purely visual, ~20-30 min.
