---
status: partial
phase: 23-bulk-operations-ux
source: [23-VERIFICATION.md]
started: 2026-06-01T07:25:00Z
updated: 2026-06-01T07:25:00Z
---

## Current Test

[awaiting human testing]

## Tests

### 1. Connect All dialog z-order over active RDP viewport
expected: With ≥1 active RDP session and threshold exceeded (GdiWarningThreshold low, ConfirmBeforeBulkOperations=true), right-click a group → Connect All. The BulkConnectConfirmDialog appears ON TOP of the active RDP tab (airspace z-order correct); Warning InfoBar + amber Warning24 icon render; clicking Connect All begins opening RDP tabs; cancel does nothing.
result: [pending]

### 2. Connect All threshold boundary in practice
expected: Projected count (active + group connection count) == GdiWarningThreshold → connects immediately, no dialog. Projected == threshold+1 with ConfirmBeforeBulkOperations=true → confirm dialog appears.
result: [pending]

### 3. Disconnect All enable-state (greyed vs active)
expected: Group with zero active sessions → Disconnect All menu item greyed/disabled. Group with ≥1 active session → item enabled; clicking it closes all active RDP tabs in the group.
result: [pending]

### 4. BulkEditDialog visual rendering
expected: Select ≥2 connections → right-click → Edit… opens a 3-column (Auto/72/*) grid with one row per editable field (Hostname, Port, Credential, Username, Domain, Group — NO Name row); per-field checkboxes; divergent fields show "Multiple values" placeholder; Apply disabled until ≥1 checkbox checked; ComboBoxes render via ItemTemplate (no "- - -" glyph).
result: [pending]

### 5. BulkEditDialog Apply path — single atomic write + tree refresh
expected: After Apply, the connection tree refreshes to reflect edited field values; selection clears; only one JSON file write occurs (no duplicate Save calls).
result: [pending]

### 6. BulkEditDialog validation — invalid port / empty hostname
expected: Clicking Apply with an enabled Port field set to "0"/"abc", or an empty enabled Hostname, keeps the dialog open and shows the validation error text.
result: [pending]

## Summary

total: 6
passed: 0
issues: 0
pending: 6
skipped: 0
blocked: 0

## Gaps
