# Phase 18: Settings Infrastructure - Context

**Gathered:** 2026-04-25
**Status:** Ready for planning

<domain>
## Phase Boundary

Dedicated settings page with categorized sections (Appearance, Security, Bulk Operations, Data, Uninstall), replacing the current minimal settings panel. Adds bulk operation preferences and uninstall cleanup toggle. Does NOT implement the Velopack uninstall hook itself (Phase 24) or the bulk operation commands (Phase 23).

</domain>

<decisions>
## Implementation Decisions

### Settings Page Layout
- **D-01:** Settings remain in the existing slide-out side panel (240px) — no full-page or separate window. Extends the current StackPanel with card sections pattern.
- **D-02:** Section order top-to-bottom: Appearance, Security, Bulk Operations, Data, Uninstall. Most-used first, one-time toggles last.
- **D-03:** No "SETTINGS" header at top of panel — sections start immediately. Icon rail already indicates Settings mode. Saves vertical space.

### Bulk Operation Settings
- **D-04:** "Confirm before bulk operations" — single ToggleSwitch covering both Connect All and Disconnect All (default: on).
- **D-05:** GDI warning threshold — WPF-UI NumberBox, range 5–30, default 15. Matches the auto-lock timeout NumberBox pattern in the Security section.
- **D-06:** TabHostManager reads GDI threshold from AppSettings via DI instead of using the hardcoded const. The const becomes just the default fallback.

### Uninstall Cleanup
- **D-07:** ToggleSwitch with inline muted description text below: "Removes connections, credentials, logs, and settings from %AppData% during uninstall. Default: data is preserved."
- **D-08:** Default value: off (preserve data). No InfoBar or modal warning — inline description is sufficient.
- **D-09:** This phase adds the UI toggle and persists the preference only. Phase 24 implements the Velopack hook that reads `settings.json` to act on this preference.

### Settings Persistence
- **D-10:** New `BulkOperationsRecord?` and `UninstallRecord?` added as nullable properties on `AppSettings`, null-coalesced to defaults on load. Same pattern as `PropertiesPanel?` and `Appearance?`. No SchemaVersion bump — stays at 1.
- **D-11:** Auto-save on every change (each toggle/value triggers `SaveAsync` immediately). No Save button. Consistent with current Security section behavior.

### Claude's Discretion
- Exact description text wording for the uninstall toggle
- Whether Bulk Operations card needs a separator between the two controls
- ViewModel property naming for the new settings bindings

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Architecture & Constraints
- `docs/REFERENCE.md` — Architecture, DI registrations, constraints, feature spec
- `docs/DESIGN.md` — WPF-UI patterns, control usage, colour tokens, layout reference

### WPF-UI Patterns
- `docs/WPF-UI-PITFALLS.md` — 8 categories of silent WPF-UI failures (read before any XAML work)

### Design System
- `.claude/skills/deskbridge-design/` — Windows 11 Fluent tokens, spacing grid, component specs, visual states

### Existing Settings Code
- `src/Deskbridge.Core/Settings/AppSettings.cs` — Current AppSettings record with all sub-records
- `src/Deskbridge.Core/Settings/AppSettingsContext.cs` — JSON source generation context (must add new types)
- `src/Deskbridge.Core/Services/WindowStateService.cs` — Atomic load/save of settings.json
- `src/Deskbridge/MainWindow.xaml` lines 217-300 — Current settings panel StackPanel (Appearance, Security, Data sections)
- `src/Deskbridge.Core/Services/TabHostManager.cs` line 34 — Hardcoded `GdiWarningThreshold = 15` const to replace with settings-driven value

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AppSettings` record pattern — nullable sub-records with defaults, backward-compatible
- `AppSettingsContext` — JSON source generation, needs new record types added
- `WindowStateService` — atomic load/save with tmp-file-rename pattern
- `CardContainerStyle` — existing card border style for settings sections
- `SectionLabelStyle` — existing label style for section headers (e.g., "APPEARANCE", "SECURITY")
- `SeparatorStyle` — existing border style for in-card separators
- `FieldLabelStyle`, `BodyStyle` — existing typography styles
- `WPF-UI NumberBox` — already used for auto-lock timeout, reuse for GDI threshold
- `WPF-UI ToggleSwitch` — already used for RequireMasterPassword and LockOnMinimise

### Established Patterns
- Settings sections use `TextBlock` with `SectionLabelStyle` as header, then `Border` with `CardContainerStyle` wrapping a `StackPanel`
- Settings bindings go through `MainWindowViewModel` properties that proxy to `AppSettings` sub-records
- Auto-save: ViewModel property setters call `SaveSettingsAsync()` on change
- Nullable sub-records null-coalesced on load: `settings.PropertiesPanel ?? PropertiesPanelRecord.Default`

### Integration Points
- `MainWindowViewModel` — needs new properties for BulkOperations and Uninstall settings
- `MainWindow.xaml` — settings StackPanel needs Bulk Operations and Uninstall sections added between Data and end
- `TabHostManager` — needs DI injection of settings to read GDI threshold instead of const
- `App.xaml.cs` — DI registration if TabHostManager constructor changes
- `AppSettingsContext` — must remain the single JSON context (no separate contexts)

</code_context>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. Follow existing settings panel patterns exactly.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 18-settings-infrastructure*
*Context gathered: 2026-04-25*
