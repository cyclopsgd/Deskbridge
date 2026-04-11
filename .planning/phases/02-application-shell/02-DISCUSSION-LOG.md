# Phase 2: Application Shell - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-11
**Phase:** 2-Application Shell
**Areas discussed:** Layout approach, Icon rail behavior, Tab bar design, Empty viewport

---

## Layout Approach

| Option | Description | Selected |
|--------|-------------|----------|
| Custom Grid (Recommended) | Manual Grid columns/rows matching DESIGN.md's VS Code-style layout | ✓ |
| NavigationView Left mode | WPF-UI's NavigationView with Left pane mode | |
| You decide | Claude picks | |

**User's choice:** Custom Grid

### Follow-up: Panel width

| Option | Description | Selected |
|--------|-------------|----------|
| Fixed 240px | Simpler, consistent, matches REFERENCE.md spec | ✓ |
| Resizable with drag | GridSplitter, more flexible | |

### Follow-up: Animation

| Option | Description | Selected |
|--------|-------------|----------|
| Snap instantly | Immediate show/hide via Visibility toggle | ✓ |
| Slide animation | Smooth width transition (150-200ms) | |

---

## Icon Rail Behavior

| Option | Description | Selected |
|--------|-------------|----------|
| Toggle same panel | VS Code pattern: click opens, same click closes, different click switches | ✓ |
| Always open | Click always opens, separate close button | |
| You decide | Claude picks | |

### Icons

| Option | Description | Selected |
|--------|-------------|----------|
| Exactly those 3 | Connections (top), Search (top), Settings (bottom) | ✓ |
| Add more | Additional icons | |

---

## Tab Bar Design

| Option | Description | Selected |
|--------|-------------|----------|
| Custom tab bar control | Hand-built ItemsControl, full control over behavior | ✓ |
| WPF-UI TabControl | Auto-restyled, less control | |
| You decide | Claude picks | |

### Empty tabs

| Option | Description | Selected |
|--------|-------------|----------|
| Hidden entirely | Collapses to 0px when empty | |
| Always visible | 30px always visible, consistent layout | ✓ |
| You decide | Claude picks | |

---

## Empty Viewport

| Option | Description | Selected |
|--------|-------------|----------|
| Subtle branding | Centered name/logo with muted text and shortcut hint | ✓ |
| Getting started | Action buttons: New Connection, Import, Settings | |
| Just empty | Dark background, nothing | |
| You decide | Claude picks | |

---

## Claude's Discretion

- Icon choices from WPF-UI SymbolIcon library
- Panel content layout per mode (placeholder)
- Status bar content layout
- Active icon visual indicator

## Deferred Ideas

None
