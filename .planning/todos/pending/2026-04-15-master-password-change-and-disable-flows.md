---
created: 2026-04-15T19:51:31.669Z
title: Phase 6.1 — master password change + disable flows
area: security
files:
  - src/Deskbridge.Core/Interfaces/IMasterPasswordService.cs
  - src/Deskbridge.Core/Services/MasterPasswordService.cs
  - src/Deskbridge.Core/Settings/AppSettings.cs
  - src/Deskbridge/ViewModels/LockOverlayViewModel.cs
  - src/Deskbridge/ViewModels/MainWindowViewModel.cs
  - src/Deskbridge/MainWindow.xaml
  - src/Deskbridge/App.xaml.cs
---

## Problem

Phase 6 Plan 04 shipped `IMasterPasswordService.SetMasterPassword` / `VerifyMasterPassword` / `IsMasterPasswordSet` and the lock-overlay first-run + unlock flows, but there is no UX for:

1. **Change password** — users cannot rotate their master password from within the app. The service mechanically supports it (`SetMasterPassword` overwrites `auth.json`) but there is no Settings entry, no dialog, no command, and no audit-log entry for a rotation event.

2. **Disable master password** — the app always requires a master password at launch. There is no way to opt out. Enterprise / single-user scenarios where the user wants the Deskbridge UI to launch straight to Connections have no path.

Both are UX gaps rather than missing security primitives. Phase 6 requirements (SEC-01..05) are fully met as specified; this is follow-up surface.

## Solution

### (1) Change password — ~2-3 hours

- Add Settings-panel entry under the existing SECURITY section: "Change master password" (ui:Button or hyperlink).
- Click opens a new `ChangePasswordDialog` (ContentDialog): three PasswordBoxes — Current / New / Confirm New.
- VM: `ChangePasswordViewModel(IMasterPasswordService)` with `ChangeCommand` that:
  1. `VerifyMasterPassword(current)` — if false, show "Incorrect current password" and clear Current field.
  2. Validate New.Length >= 8 (same rule as first-run) and New == Confirm.
  3. `SetMasterPassword(new)` — overwrites auth.json atomically (already implemented).
  4. Publish `AppLockedEvent(Manual)` or a new `MasterPasswordChangedEvent` so the audit log records the rotation.
  5. Dismiss dialog.
- Tests: 6 cases (wrong current / short new / mismatch / success / cancel mid-flow / audit entry present).

### (2) Disable password — ~half day

- `AppSettings.SecuritySettings`: add `public bool RequireMasterPassword { get; init; } = true;` (default TRUE, so existing users are unaffected).
- Settings panel: add `ui:ToggleSwitch` bound to `MainWindowViewModel.RequireMasterPassword` under the SECURITY section, below the auto-lock controls.
- Toggle behaviour:
  - ON→OFF: show a confirmation dialog explaining "Without a master password, Deskbridge's UI state (connections, settings, audit log) is unprotected at rest." Require current master password to confirm. On success: set flag to false AND delete `auth.json` (otherwise IsMasterPasswordSet still returns true — confusing state).
  - OFF→ON: if auth.json exists already → just flip flag. If not → immediately trigger the first-run SetPassword overlay.
- Startup flow change in App.xaml.cs `EnsureLockedOnStartupAsync`:
  - If `settings.Security.RequireMasterPassword == false` → skip the lock overlay, go straight to MainWindow interactive state.
  - Else → existing behaviour (lock overlay in setup or unlock mode).
- Runtime lock triggers (Ctrl+L, idle, SessionSwitch, minimise) should all become no-ops when `RequireMasterPassword == false` — they should NOT silently lock a password-less app (that would hang the user out of their own session with no unlock path).
- Tests: 8 cases (default-on / toggle-off-requires-password / toggle-off-deletes-auth-json / toggle-on-triggers-setup / startup-skipped-when-off / Ctrl+L-noop-when-off / idle-timer-noop-when-off / SessionSwitch-noop-when-off).

### Design coordination with Phase 7 (encrypted credential store)

If Phase 7 ships an encrypted credential store keyed off the master password (see separate backlog item for Defender/Credential-Guard workaround), disabling the master password becomes load-bearing:

- Option A: refuse to disable while encrypted creds exist (simplest — user must delete creds first).
- Option B: re-encrypt creds with a machine-local DPAPI key when disabling (silent migration, weaker security).
- Option C: let users choose via a "This will decrypt your saved RDP passwords to machine-protected storage" dialog.

Decide this BEFORE shipping disable so the UI can message correctly. Most likely Option A for v1.

### Entry point

Run `/gsd-insert-phase 6.1 "master password change + disable flows"` when ready to promote this todo into an active phase plan. Estimated one plan (3 tasks: change-password dialog, disable toggle + startup wiring, tests + UAT).

## Related

- Phase 6 REVIEW.md (7 warnings — v1.1 hardening candidates, separate from this UX gap).
- Future Phase 7 backlog: encrypted credential store (Defender / Credential Guard workaround for work machines).
- Phase 4.1 backlog: RDP credential injection bug ("falls back to Windows account" at work — likely `Domain` / `ClearTextPassword` / `DisableCredentialsDelegation` ordering in AxMsRdpClient9 integration).
