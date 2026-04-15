---
status: partial
phase: 06-cross-cutting-features
source: [06-VERIFICATION.md]
started: 2026-04-15T20:30:00Z
updated: 2026-04-15T20:30:00Z
---

## Current Test

[awaiting human testing — 4 items across 3 UAT checklists in tests/uat/]

## Tests

### 1. SEC-02 — Lock overlay airspace (no RDP pixel bleed-through)
expected: Opening an RDP session, then pressing Ctrl+L, should hide the RDP viewport entirely behind the opaque lock overlay. No pixels of the remote desktop should leak around or through the lock card.
source: tests/uat/phase-06-security.md §Airspace
result: [pending]

### 2. SEC-03 — Idle timer filters RDP-session input
expected: With idle timeout set (e.g. 5 min), typing inside an active RDP session should NOT reset Deskbridge's own idle timer. The app should lock after 5 minutes of no Deskbridge-chrome input, even while the user is actively working inside the RDP viewport.
source: tests/uat/phase-06-auto-lock.md §Idle filter
result: [pending]

### 3. SEC-04 — Real Windows SessionSwitch auto-locks the app
expected: Pressing Win+L to lock Windows (real SystemEvents.SessionSwitch, not a synthetic test) should cause Deskbridge to auto-lock. Unlocking Windows should leave Deskbridge locked — user must enter master password to get back in.
source: tests/uat/phase-06-auto-lock.md §SessionSwitch
result: [pending]

### 4. LOG-04 — Crash dialog Copy Details + Restart physical round-trip
expected: Trigger an unhandled exception (dev-only hook or the test crash button if added). Crash dialog appears with details. "Copy Details" puts the full exception+stack on the clipboard (verify by pasting into Notepad). "Restart" closes the app and relaunches it.
source: tests/uat/phase-06-crash.md
result: [pending]

## Summary

total: 4
passed: 0
issues: 0
pending: 4
skipped: 0
blocked: 0

## Gaps
