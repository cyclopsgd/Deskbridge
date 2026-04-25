# Phase 18: Settings Infrastructure - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-25
**Phase:** 18-settings-infrastructure
**Areas discussed:** Settings page layout, Bulk operation settings, Uninstall cleanup UX, Settings persistence

---

## Settings Page Layout

| Option | Description | Selected |
|--------|-------------|----------|
| Keep in side panel | Add new sections to existing scrollable StackPanel in slide-out panel. Consistent with current pattern, no layout refactor needed | ✓ |
| Full-page settings view | Settings icon switches to a full-width page replacing the viewport area. More room but requires new layout infrastructure | |
| Separate settings window | Settings opens as a standalone dialog window. Clean separation but adds window management complexity | |

**User's choice:** Keep in side panel
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| Appearance, Security, Bulk Ops, Data, Uninstall | Most-used first, one-time toggles last | ✓ |
| Appearance, Bulk Ops, Data, Security, Uninstall | Group daily-use above admin settings | |
| You decide | Claude picks ordering | |

**User's choice:** Appearance, Security, Bulk Ops, Data, Uninstall
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| No header — sections start immediately | Matches current pattern, icon rail indicates mode | ✓ |
| Add a 'Settings' header | Explicit title at top | |

**User's choice:** No header
**Notes:** None

---

## Bulk Operation Settings

| Option | Description | Selected |
|--------|-------------|----------|
| NumberBox with min/max | WPF-UI NumberBox, range 5-30, default 15. Matches auto-lock timeout pattern | ✓ |
| Slider with labels | Slider 5-30 with tick marks. More visual but more space | |
| Preset dropdown | Named presets: Conservative/Default/High/Maximum. Less flexible | |

**User's choice:** NumberBox with min/max
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| Both connect and disconnect | Single toggle: "Confirm before bulk operations". Covers both | ✓ |
| Connect All only | Only gate the connect action | |
| Separate toggles | Two toggles for granular control | |

**User's choice:** Both connect and disconnect
**Notes:** None

---

## Uninstall Cleanup UX

| Option | Description | Selected |
|--------|-------------|----------|
| Toggle with inline description | ToggleSwitch with muted description. Simple, matches Security section pattern | ✓ |
| Toggle with InfoBar warning | ToggleSwitch plus orange InfoBar when enabled. More visible but heavier | |
| You decide | Claude picks warning level | |

**User's choice:** Toggle with inline description
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| Settings UI only | This phase adds toggle and persists preference. Phase 24 implements Velopack hook | ✓ |
| Settings UI + hook | Implement both here. Phase 24 becomes unnecessary | |

**User's choice:** Settings UI only
**Notes:** Clean separation — toggle needs to exist before the hook can read it

---

## Settings Persistence

| Option | Description | Selected |
|--------|-------------|----------|
| Add nullable properties, keep SchemaVersion=1 | Same pattern as PropertiesPanel? and Appearance?. No migration needed | ✓ |
| Add required properties, bump to SchemaVersion=2 | Non-nullable, requires migration logic. More explicit | |
| You decide | Claude picks based on pattern | |

**User's choice:** Nullable properties, SchemaVersion=1
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| TabHostManager reads from AppSettings via DI | Inject settings, const becomes default fallback | ✓ |
| Shared config, TabHostManager reads on next check | Less coupling but threshold may lag | |
| You decide | Claude picks wiring | |

**User's choice:** AppSettings via DI
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| Auto-save on change | Each toggle/value triggers SaveAsync immediately. Matches current Security behavior | ✓ |
| Save button at bottom | Batch changes, persist on click. More traditional | |

**User's choice:** Auto-save on change
**Notes:** None

---

## Claude's Discretion

- Exact description text wording for the uninstall toggle
- Whether Bulk Operations card needs a separator between controls
- ViewModel property naming for new settings bindings

## Deferred Ideas

None — discussion stayed within phase scope
