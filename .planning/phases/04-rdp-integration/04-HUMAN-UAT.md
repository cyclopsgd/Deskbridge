---
status: partial
phase: 04-rdp-integration
source: ["04-VERIFICATION.md"]
started: 2026-04-13T21:00:00Z
updated: 2026-04-13T21:00:00Z
---

## Current Test

[awaiting Windows VM session]

## Tests

### 1. Drop-and-reconnect happy path (Plan 04-03 Task 8 step 1)
expected: Active session, network drop on VM, overlay appears within ~2s showing "Reconnecting... attempt 1" with progress ring + Cancel. Attempts proceed at 2s, 4s, 8s, 16s, 30s. Restore network → next attempt succeeds, overlay disappears, RDP view returns.
result: [pending]

### 2. Cancel during auto-retry (Plan 04-03 Task 8 step 2)
expected: Drop session → click Cancel on overlay → overlay disappears, Serilog logs `ConnectionClosedEvent` with `Reason=UserInitiated`, app remains responsive.
result: [pending]

### 3. 20-attempt cap (D-05) live transition (Plan 04-03 Task 8 step 4)
expected: Keep network down across 20 attempts (~10 min). After attempt 20, overlay switches to manual "Connection lost" with Reconnect + Close buttons.
result: [pending]

### 4. Manual Reconnect button live (Plan 04-03 Task 8 step 5)
expected: On manual overlay (from step 3 or auth failure), click Reconnect → pipeline runs fresh → session reconnects (given network restored).
result: [pending]

### 5. Manual Close button live (Plan 04-03 Task 8 step 6)
expected: On manual overlay, click Close → `ConnectionClosedEvent` fires → overlay closes → viewport returns to empty state.
result: [pending]

### 6. GDI handle stability over 5 reconnect cycles (Plan 04-03 Task 8 step 8)
expected: Task Manager > Details > GDI Objects for `Deskbridge.exe`. Run 5 drop-reconnect cycles. Count stable within +/- 50 over baseline; growth >50 per cycle indicates a leak.
result: [pending]

### 7. Window close while reconnecting (Plan 04-03 Task 8 step 9)
expected: Start a drop-reconnect cycle; before it succeeds, close MainWindow. App exits cleanly — no hung process in Task Manager, no crash dialog.
result: [pending]

## Summary

total: 7
passed: 0
issues: 0
pending: 7
skipped: 0
blocked: 0

## Gaps

[none — all items pending live execution against Windows VM]
