# Phase 18: Settings Infrastructure - Research

**Researched:** 2026-04-26
**Domain:** WPF-UI settings panel extension, AppSettings persistence, DI-driven configuration
**Confidence:** HIGH

## Summary

Phase 18 extends the existing settings slide-out panel with two new card sections (Bulk Operations and Uninstall) and wires a configurable GDI warning threshold into `TabHostManager` via DI, replacing a hardcoded constant. All patterns -- nullable sub-records on `AppSettings`, auto-save on change, `_suppressPersist` guard on load, JSON source generation in `AppSettingsContext` -- are already established across multiple prior phases (6, 9, 14). No new libraries, frameworks, or architectural patterns are introduced.

The primary technical risk is the `TabHostManager` constructor change: it currently takes no settings dependency, and two test files construct it directly. The snackbar warning message also hardcodes "15", which must be parameterized when the threshold becomes user-configurable. The secondary concern is forward-compatibility with Phase 24, where a headless Velopack uninstall hook must read `settings.json` via raw `JsonDocument` (no DI, no source-gen context) -- the camelCase JSON property path `uninstall.cleanUpOnUninstall` must be pinned now.

**Primary recommendation:** Follow the exact AppSettings sub-record pattern from Phase 9 (PropertiesPanelRecord) and Phase 14 (AppearanceRecord) for both new records. Inject `IWindowStateService` into `TabHostManager` constructor to read GDI threshold on demand.

<user_constraints>

## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Settings remain in the existing slide-out side panel (240px) -- no full-page or separate window. Extends the current StackPanel with card sections pattern.
- **D-02:** Section order top-to-bottom: Appearance, Security, Bulk Operations, Data, Uninstall. Most-used first, one-time toggles last.
- **D-03:** No "SETTINGS" header at top of panel -- sections start immediately. Icon rail already indicates Settings mode. Saves vertical space.
- **D-04:** "Confirm before bulk operations" -- single ToggleSwitch covering both Connect All and Disconnect All (default: on).
- **D-05:** GDI warning threshold -- WPF-UI NumberBox, range 5-30, default 15. Matches the auto-lock timeout NumberBox pattern in the Security section.
- **D-06:** TabHostManager reads GDI threshold from AppSettings via DI instead of using the hardcoded const. The const becomes just the default fallback.
- **D-07:** ToggleSwitch with inline muted description text below: "Removes connections, credentials, logs, and settings from %AppData% during uninstall. Default: data is preserved."
- **D-08:** Default value: off (preserve data). No InfoBar or modal warning -- inline description is sufficient.
- **D-09:** This phase adds the UI toggle and persists the preference only. Phase 24 implements the Velopack hook that reads settings.json to act on this preference.
- **D-10:** New `BulkOperationsRecord?` and `UninstallRecord?` added as nullable properties on `AppSettings`, null-coalesced to defaults on load. Same pattern as `PropertiesPanel?` and `Appearance?`. No SchemaVersion bump -- stays at 1.
- **D-11:** Auto-save on every change (each toggle/value triggers SaveAsync immediately). No Save button. Consistent with current Security section behavior.

### Claude's Discretion
- Exact description text wording for the uninstall toggle
- Whether Bulk Operations card needs a separator between the two controls
- ViewModel property naming for the new settings bindings

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope.

</user_constraints>

<phase_requirements>

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SET-01 | User sees a dedicated Settings page with categorized sections (Appearance, Security, Bulk Operations, Uninstall) | Existing settings StackPanel in MainWindow.xaml lines 217-300 provides the insertion point. BULK OPERATIONS card inserts between SECURITY and DATA sections; UNINSTALL card appends after DATA. All styles (SectionLabelStyle, CardContainerStyle, SeparatorStyle) already exist. |
| SET-02 | User can configure bulk operation preferences (confirm before bulk connect, GDI warning threshold) | New `BulkOperationsRecord` on `AppSettings` with `ConfirmBeforeBulkOperations` (bool, default true) and `GdiWarningThreshold` (int, default 15). ViewModel properties bound to ToggleSwitch + NumberBox. TabHostManager reads threshold via injected `IWindowStateService`. |
| SET-03 | User can toggle a preference to clean up application data on uninstall (default: preserve data) | New `UninstallRecord` on `AppSettings` with `CleanUpOnUninstall` (bool, default false). Phase 24 reads the persisted JSON value via `JsonDocument` in a headless context. |

</phase_requirements>

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Settings UI (XAML sections) | Frontend (WPF MainWindow) | -- | Settings panel is MainWindow XAML + MainWindowViewModel bindings |
| Settings persistence | Core Services (WindowStateService) | -- | Atomic JSON load/save, established pattern |
| GDI threshold consumption | Core Services (TabHostManager) | -- | TabHostManager owns session counting and snackbar warnings |
| JSON serialization | Core (AppSettingsContext) | -- | Source-generated JSON context, single context per CLAUDE.md |
| Uninstall preference read | External (Phase 24 Velopack hook) | -- | Headless context reads settings.json via raw JsonDocument, not DI |

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF-UI | 4.2.0 | ToggleSwitch, NumberBox controls | Already in use for Security section controls [VERIFIED: existing MainWindow.xaml] |
| CommunityToolkit.Mvvm | 8.4.2 | [ObservableProperty] source gen for settings bindings | Already in use for all VM properties [VERIFIED: existing MainWindowViewModel.cs] |
| System.Text.Json | in-box (.NET 10) | AppSettings serialization via source-gen context | Already in use via AppSettingsContext [VERIFIED: existing AppSettingsContext.cs] |

### Supporting
No new libraries needed. All controls and patterns are established.

**Installation:** No new packages required.

## Architecture Patterns

### System Architecture Diagram

```
User changes toggle/NumberBox in Settings panel
        |
        v
MainWindowViewModel [ObservableProperty] setter fires
        |
        v
partial void On<Property>Changed() calls PersistBulkOperationsSettings() or PersistUninstallSettings()
        |
        v
IWindowStateService.LoadAsync() -> merge with `record with { }` -> SaveAsync()
        |
        v
settings.json on disk (%AppData%/Deskbridge/settings.json)
        ^
        |
TabHostManager reads threshold:
  IWindowStateService.LoadAsync() in ctor or on-demand
  _gdiThreshold field used in FireGdiWarningIfCrossingThreshold()
        ^
        |
Phase 24 (future): Velopack hook reads settings.json via JsonDocument
  Key path: uninstall.cleanUpOnUninstall
```

### Recommended Project Structure

No new files are created beyond record definitions. Changes touch existing files only:

```
src/
  Deskbridge.Core/
    Settings/
      AppSettings.cs         # Add BulkOperationsRecord, UninstallRecord, extend AppSettings record
      AppSettingsContext.cs   # Add [JsonSerializable] for new record types
    Services/
      TabHostManager.cs      # Add IWindowStateService ctor param, replace const with field
  Deskbridge/
    ViewModels/
      MainWindowViewModel.cs # Add properties + persist methods for new settings
    MainWindow.xaml           # Add BULK OPERATIONS and UNINSTALL card sections
    MainWindow.xaml.cs        # Apply new settings on load (OnSourceInitialized)
    App.xaml.cs               # Update TabHostManager DI registration
tests/
  Deskbridge.Tests/
    Settings/
      BulkOperationsSettingsTests.cs  # NEW: round-trip, defaults, null handling
      UninstallSettingsTests.cs       # NEW: round-trip, defaults, null handling
    Tabs/
      TabHostManagerTests.cs          # UPDATE: new ctor signature
      TabHostManagerLruTests.cs       # UPDATE: new ctor signature
```

### Pattern 1: Nullable Sub-Record on AppSettings
**What:** Add nullable property to `AppSettings` record, null-coalesce to `Default` on load.
**When to use:** Every new settings section follows this pattern for backward compatibility with existing `settings.json` files.
**Example:**
```csharp
// Source: existing AppSettings.cs pattern (PropertiesPanelRecord, AppearanceRecord)
public sealed record BulkOperationsRecord(
    bool ConfirmBeforeBulkOperations = true,
    int GdiWarningThreshold = 15)
{
    public static BulkOperationsRecord Default { get; } = new();
}

public sealed record UninstallRecord(
    bool CleanUpOnUninstall = false)
{
    public static UninstallRecord Default { get; } = new();
}

// On AppSettings:
public sealed record AppSettings(
    WindowStateRecord Window,
    SecuritySettingsRecord Security,
    UpdateSettingsRecord Update,
    PropertiesPanelRecord? PropertiesPanel = null,
    AppearanceRecord? Appearance = null,
    BulkOperationsRecord? BulkOperations = null,
    UninstallRecord? Uninstall = null,
    int SchemaVersion = 1)
{
    public AppSettings() : this(WindowStateRecord.Default, SecuritySettingsRecord.Default, UpdateSettingsRecord.Default) { }
}
```

### Pattern 2: Auto-Save ViewModel Binding
**What:** `[ObservableProperty]` with `partial void On<Prop>Changed()` calling async-void persist method that loads current settings, merges with `record with { }`, and saves.
**When to use:** Every settings toggle/input that should persist immediately.
**Example:**
```csharp
// Source: existing MainWindowViewModel.cs PersistSecuritySettings pattern
[ObservableProperty]
public partial bool ConfirmBeforeBulkOperations { get; set; } = true;

[ObservableProperty]
public partial int GdiWarningThreshold { get; set; } = 15;

partial void OnConfirmBeforeBulkOperationsChanged(bool value) => PersistBulkOperationsSettings();
partial void OnGdiWarningThresholdChanged(int value) => PersistBulkOperationsSettings();

private async void PersistBulkOperationsSettings()
{
    if (_suppressPersist) return;
    if (_windowState is null) return;
    try
    {
        var current = await _windowState.LoadAsync().ConfigureAwait(false);
        var updated = current with { BulkOperations = new BulkOperationsRecord(
            ConfirmBeforeBulkOperations, GdiWarningThreshold) };
        await _windowState.SaveAsync(updated).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to persist bulk operations settings");
    }
}
```

### Pattern 3: Apply Settings on Load (suppress-persist guard)
**What:** On `OnSourceInitialized`, load settings from disk and apply to VM properties with `_suppressPersist = true` to prevent round-trip save.
**Example:**
```csharp
// Source: existing ApplySecuritySettings / ApplyAppearanceSettings pattern
public void ApplyBulkOperationsSettings(BulkOperationsRecord bulk)
{
    ArgumentNullException.ThrowIfNull(bulk);
    _suppressPersist = true;
    try
    {
        ConfirmBeforeBulkOperations = bulk.ConfirmBeforeBulkOperations;
        GdiWarningThreshold = bulk.GdiWarningThreshold;
    }
    finally
    {
        _suppressPersist = false;
    }
}
```

### Pattern 4: TabHostManager Settings Injection
**What:** Inject `IWindowStateService` into TabHostManager to read GDI threshold at construction time, replacing the `public const`.
**Example:**
```csharp
// TabHostManager constructor addition
private readonly int _gdiWarningThreshold;

public TabHostManager(
    IEventBus bus,
    IConnectionCoordinator coordinator,
    IDisconnectPipeline disconnect,
    ISnackbarService snackbar,
    ILogger<TabHostManager> logger,
    IWindowStateService? windowState = null,  // nullable for test compat
    Dispatcher? dispatcher = null)
{
    // ... existing assignments ...
    var settings = windowState?.LoadAsync().GetAwaiter().GetResult();
    var bulk = settings?.BulkOperations ?? BulkOperationsRecord.Default;
    _gdiWarningThreshold = bulk.GdiWarningThreshold;
}
```

### Anti-Patterns to Avoid
- **Separate persist method per property:** Don't create `PersistConfirmBeforeBulkOperations()` and `PersistGdiWarningThreshold()` separately. Both are in the same `BulkOperationsRecord`, so a single `PersistBulkOperationsSettings()` handles both (matches the existing pattern where `PersistSecuritySettings()` covers all three security props).
- **Loading settings on every threshold check:** Don't call `LoadAsync()` inside `FireGdiWarningIfCrossingThreshold()`. Read the value once at construction. Mid-session changes take effect on next app restart (or next TabHostManager construction).
- **Adding a SchemaVersion bump:** D-10 explicitly says no bump. Nullable properties with defaults handle backward compatibility.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Number input with range clamping | Custom TextBox + validation | WPF-UI NumberBox (Minimum/Maximum) | Built-in clamping, spinner buttons, focus revert on empty [VERIFIED: already used for AutoLockTimeoutMinutes] |
| Toggle switch | Custom checkbox styling | WPF-UI ToggleSwitch | Fluent Design, consistent with existing Security toggles [VERIFIED: existing MainWindow.xaml] |
| JSON serialization for records | Manual string building | System.Text.Json source-gen (AppSettingsContext) | AOT-safe, camelCase by default [VERIFIED: existing AppSettingsContext.cs] |
| Atomic file write | Direct File.WriteAllText | WindowStateService tmp-file-rename | Crash-safe, established pattern [VERIFIED: existing WindowStateService.cs] |

## Common Pitfalls

### Pitfall 1: AppSettingsContext Missing New Record Types
**What goes wrong:** `JsonSerializer.Serialize/Deserialize` silently produces empty objects or throws when new record types aren't registered in the source-gen context.
**Why it happens:** `[JsonSerializable(typeof(AppSettings))]` alone may not discover nested types if they're nullable properties added later.
**How to avoid:** Add `[JsonSerializable(typeof(BulkOperationsRecord))]` and `[JsonSerializable(typeof(UninstallRecord))]` to `AppSettingsContext`. The existing context only has `[JsonSerializable(typeof(AppSettings))]` -- verify that the source generator walks the record tree. Test with a round-trip test.
**Warning signs:** Null properties after deserialization of a JSON file that contains the values.

### Pitfall 2: Snackbar Message Hardcodes "15"
**What goes wrong:** User changes GDI threshold to 20, but warning still says "15 active sessions reached."
**Why it happens:** The snackbar message at TabHostManager.cs:368 is a string literal.
**How to avoid:** Parameterize: `$"{_gdiWarningThreshold} active sessions reached -- performance may degrade beyond this point."`
**Warning signs:** Contradiction between NumberBox value and snackbar message text.

### Pitfall 3: Equality Check `_hosts.Count == GdiWarningThreshold` With Dynamic Threshold
**What goes wrong:** If user changes threshold mid-session from 15 to 20, the `==` check at the old threshold has already fired/not-fired, and the new threshold may never trigger.
**Why it happens:** TabHostManager reads the threshold once at construction. The `==` check is exact-match, not `>=`.
**How to avoid:** Accept that the threshold is read once at startup. The `==` check is intentional (fire-once-per-crossing, not fire-on-every-add-above-threshold). Document that mid-session changes apply on next restart. This is consistent with D-06 which says "reads from AppSettings via DI" without specifying real-time reactivity.
**Warning signs:** None -- this is by design. The planner should note this as expected behavior in task descriptions.

### Pitfall 4: Suppress-Persist Flag Shared Across Settings Groups
**What goes wrong:** `_suppressPersist` is a single `bool` on MainWindowViewModel. If `ApplyBulkOperationsSettings()` and `ApplySecuritySettings()` are called sequentially in `OnSourceInitialized`, the flag correctly guards both. But if called concurrently (impossible in current code, both are on UI thread), the flag could leak.
**Why it happens:** Single boolean for all settings groups.
**How to avoid:** Keep the sequential call pattern in OnSourceInitialized. No change needed to the flag -- it works because all Apply methods run on the UI thread in sequence.
**Warning signs:** Settings saving immediately on app load (check Serilog output for unexpected persist calls).

### Pitfall 5: Uninstall Description Text Discrepancy
**What goes wrong:** CONTEXT.md D-07 includes "Default: data is preserved" in the description text. The UI-SPEC drops this sentence, reasoning that the default off-state makes preservation implicit.
**Why it happens:** Claude's Discretion covers "exact description text wording."
**How to avoid:** Follow the UI-SPEC wording (shorter, no "Default:" prefix) since CONTEXT.md explicitly grants discretion on wording. The planner should reference the UI-SPEC Copywriting Contract for exact text.
**Warning signs:** None -- just a documentation delta to be aware of.

### Pitfall 6: Forward-Compatibility JSON Key Path for Phase 24
**What goes wrong:** Phase 24's Velopack uninstall hook reads `settings.json` via raw `JsonDocument` (no DI, no source-gen). If the camelCase key path drifts between phases, the hook silently reads null and defaults to "preserve data."
**Why it happens:** Source-gen uses `JsonKnownNamingPolicy.CamelCase`, producing `uninstall.cleanUpOnUninstall`. Phase 24 must match this exactly.
**How to avoid:** Pin the JSON property path now: `settings.json` will contain `"uninstall": { "cleanUpOnUninstall": true/false }`. Document this in research (here) and in the `UninstallRecord` doc comment.
**Warning signs:** Phase 24 reads the value and always gets `false` despite user enabling the toggle.

## Code Examples

### XAML: Bulk Operations Card Section
```xml
<!-- Source: pattern from existing SECURITY section in MainWindow.xaml lines 241-278 -->
<TextBlock Text="BULK OPERATIONS"
           Style="{StaticResource SectionLabelStyle}"
           Margin="0,16,0,4" />
<Border Style="{StaticResource CardContainerStyle}">
    <StackPanel>
        <TextBlock Text="Confirm before bulk operations"
                   Style="{StaticResource BodyStyle}" />
        <ui:ToggleSwitch IsChecked="{Binding ConfirmBeforeBulkOperations}"
                         Margin="0,4,0,12" />

        <!-- Separator between controls -->
        <Border Style="{StaticResource SeparatorStyle}" />

        <TextBlock Text="GDI handle warning threshold"
                   Style="{StaticResource FieldLabelStyle}" />
        <ui:NumberBox Minimum="5" Maximum="30"
                      Value="{Binding GdiWarningThreshold, UpdateSourceTrigger=LostFocus}"
                      Margin="{StaticResource FormFieldSpacing}" />
    </StackPanel>
</Border>
```

### XAML: Uninstall Card Section
```xml
<!-- Source: pattern from existing SECURITY section + HintStyle from TypographyStyles.xaml -->
<TextBlock Text="UNINSTALL"
           Style="{StaticResource SectionLabelStyle}"
           Margin="0,16,0,4" />
<Border Style="{StaticResource CardContainerStyle}">
    <StackPanel>
        <TextBlock Text="Clean up application data on uninstall"
                   Style="{StaticResource BodyStyle}" />
        <ui:ToggleSwitch IsChecked="{Binding CleanUpOnUninstall}"
                         Margin="0,4,0,4" />
        <TextBlock Text="Removes connections, credentials, logs, and settings from %AppData% during uninstall."
                   Style="{StaticResource HintStyle}"
                   TextWrapping="Wrap" />
    </StackPanel>
</Border>
```

### AppSettingsContext Update
```csharp
// Source: existing AppSettingsContext.cs
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(BulkOperationsRecord))]
[JsonSerializable(typeof(UninstallRecord))]
internal partial class AppSettingsContext : JsonSerializerContext { }
```

### TrySaveWindowState Update
```csharp
// Source: existing MainWindow.xaml.cs TrySaveWindowState pattern (lines 316-360)
// Add bulk operations and uninstall to the merged settings:
var bulkOps = vm?.CurrentBulkOperationsSettings
    ?? _loadedSettings.BulkOperations
    ?? BulkOperationsRecord.Default;

var uninstall = vm?.CurrentUninstallSettings
    ?? _loadedSettings.Uninstall
    ?? UninstallRecord.Default;

var updated = _loadedSettings with
{
    Window = new WindowStateRecord(x, y, w, h, isMaximized, sidebarOpen, sidebarWidth),
    Security = security,
    PropertiesPanel = propertiesPanel,
    Appearance = appearance,
    BulkOperations = bulkOps,
    Uninstall = uninstall,
};
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Hardcoded `GdiWarningThreshold = 15` const | Settings-driven via `IWindowStateService` DI | Phase 18 | User can tune 5-30; TabHostManager reads on construction |
| Three settings sections (Appearance, Security, Data) | Five sections (+ Bulk Operations, Uninstall) | Phase 18 | Section order per D-02; DATA moves from position 3 to 4 |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | AppSettingsContext source-gen walks the record tree for nullable properties without explicit per-type registration | Pitfall 1 | Serialization silently drops new properties. Mitigated by adding explicit [JsonSerializable] attributes as belt-and-suspenders. |
| A2 | WPF-UI NumberBox clamps out-of-range values on LostFocus without custom validation code | Code Examples | NumberBox might allow out-of-range entry; would need manual clamping in VM setter. Mitigated by existing auto-lock timeout NumberBox working correctly with same pattern. |

## Open Questions

1. **Mid-session GDI threshold changes**
   - What we know: TabHostManager reads threshold once at construction (startup). D-06 says "reads from AppSettings via DI."
   - What's unclear: Whether user expects the threshold change to take effect immediately or on restart.
   - Recommendation: Document as "restart required" behavior. Injecting reactive settings adds complexity with no clear user need -- the threshold is a rarely-changed preference.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit.v3 + FluentAssertions + NSubstitute |
| Config file | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| Quick run command | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~Settings" -x` |
| Full suite command | `dotnet test tests/Deskbridge.Tests` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SET-01 | Settings panel has 5 categorized sections | manual-only | Visual inspection of XAML structure | N/A -- XAML layout |
| SET-02a | BulkOperationsRecord defaults (confirm=true, threshold=15) | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~BulkOperationsSettings" -x` | Wave 0 |
| SET-02b | BulkOperationsRecord round-trip via JSON | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~BulkOperationsSettings" -x` | Wave 0 |
| SET-02c | Missing bulkOperations key deserializes to null | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~BulkOperationsSettings" -x` | Wave 0 |
| SET-02d | TabHostManager uses injected threshold | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~TabHostManager" -x` | Needs update |
| SET-03a | UninstallRecord defaults (cleanUp=false) | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~UninstallSettings" -x` | Wave 0 |
| SET-03b | UninstallRecord round-trip via JSON | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~UninstallSettings" -x` | Wave 0 |
| SET-03c | Missing uninstall key deserializes to null | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~UninstallSettings" -x` | Wave 0 |
| SET-03d | JSON key path is `uninstall.cleanUpOnUninstall` (Phase 24 compat) | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~UninstallSettings" -x` | Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~Settings" -x`
- **Per wave merge:** `dotnet test tests/Deskbridge.Tests`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/Deskbridge.Tests/Settings/BulkOperationsSettingsTests.cs` -- covers SET-02a/b/c (defaults, round-trip, null handling)
- [ ] `tests/Deskbridge.Tests/Settings/UninstallSettingsTests.cs` -- covers SET-03a/b/c/d (defaults, round-trip, null handling, JSON key path)
- [ ] Update `tests/Deskbridge.Tests/Tabs/TabHostManagerTests.cs` -- `BuildSut()` needs new ctor param
- [ ] Update `tests/Deskbridge.Tests/Tabs/TabHostManagerLruTests.cs` -- `BuildSut()` needs new ctor param

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | Not applicable -- settings panel does not handle auth |
| V3 Session Management | no | Not applicable |
| V4 Access Control | no | Settings panel accessible to any authenticated user (behind existing master password lock) |
| V5 Input Validation | yes | WPF-UI NumberBox Minimum=5/Maximum=30 clamps range; ToggleSwitches are binary |
| V6 Cryptography | no | No secrets stored by this phase -- preferences only |

### Known Threat Patterns for WPF Settings

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Settings file tampering | Tampering | %AppData% is user-scoped; same trust boundary as current settings.json |
| Uninstall toggle social engineering | Information Disclosure | Default is OFF (preserve data); inline description explains consequences |

## Integration Points Summary

This table consolidates every file that must change, for the planner to size tasks accurately.

| File | Change Type | What Changes |
|------|-------------|-------------|
| `src/Deskbridge.Core/Settings/AppSettings.cs` | Add records + extend AppSettings | `BulkOperationsRecord`, `UninstallRecord`, new nullable props on `AppSettings` |
| `src/Deskbridge.Core/Settings/AppSettingsContext.cs` | Add attributes | `[JsonSerializable(typeof(BulkOperationsRecord))]`, `[JsonSerializable(typeof(UninstallRecord))]` |
| `src/Deskbridge.Core/Services/TabHostManager.cs` | Ctor change + field | Add `IWindowStateService?` param, `_gdiWarningThreshold` field, replace const usage, parameterize snackbar message |
| `src/Deskbridge/ViewModels/MainWindowViewModel.cs` | Add properties + methods | 3 new `[ObservableProperty]`, 2 persist methods, 2 apply methods, 2 snapshot properties |
| `src/Deskbridge/MainWindow.xaml` | Add XAML sections | Bulk Operations card + Uninstall card; reorder DATA to position 4 |
| `src/Deskbridge/MainWindow.xaml.cs` | OnSourceInitialized + TrySaveWindowState | Apply new settings on load; merge new settings on close |
| `src/Deskbridge/App.xaml.cs` | DI registration | Pass `IWindowStateService` to `TabHostManager` ctor |
| `tests/.../Tabs/TabHostManagerTests.cs` | Update ctor calls | Add `windowState` parameter to `BuildSut()` |
| `tests/.../Tabs/TabHostManagerLruTests.cs` | Update ctor calls | Add `windowState` parameter to `BuildSut()` |
| `tests/.../Settings/BulkOperationsSettingsTests.cs` | NEW | Defaults, round-trip, null handling |
| `tests/.../Settings/UninstallSettingsTests.cs` | NEW | Defaults, round-trip, null handling, JSON key path |

## Pinned JSON Contract (Phase 24 Compatibility)

The Velopack uninstall hook (Phase 24) will read `settings.json` via `JsonDocument` in a headless context. The following JSON structure is the contract:

```json
{
  "uninstall": {
    "cleanUpOnUninstall": false
  },
  "bulkOperations": {
    "confirmBeforeBulkOperations": true,
    "gdiWarningThreshold": 15
  },
  "schemaVersion": 1
}
```

Property names are camelCase per `AppSettingsContext`'s `JsonKnownNamingPolicy.CamelCase` setting. Phase 24 reads: `root.GetProperty("uninstall").GetProperty("cleanUpOnUninstall").GetBoolean()`. This path must not change.

## Sources

### Primary (HIGH confidence)
- `src/Deskbridge.Core/Settings/AppSettings.cs` -- existing record pattern with nullable sub-records
- `src/Deskbridge.Core/Settings/AppSettingsContext.cs` -- JSON source generation context
- `src/Deskbridge.Core/Services/WindowStateService.cs` -- atomic load/save pattern
- `src/Deskbridge.Core/Services/TabHostManager.cs` -- GdiWarningThreshold const at line 34, snackbar at line 366-368
- `src/Deskbridge/ViewModels/MainWindowViewModel.cs` -- PersistSecuritySettings pattern (lines 542-556), ApplySecuritySettings (lines 509-524), _suppressPersist guard (line 537)
- `src/Deskbridge/MainWindow.xaml` -- settings StackPanel (lines 217-300)
- `src/Deskbridge/MainWindow.xaml.cs` -- TrySaveWindowState (lines 316-360), OnSourceInitialized (lines 173-222)
- `src/Deskbridge/App.xaml.cs` -- TabHostManager DI registration (line 185)
- `src/Deskbridge/Resources/CardAndPanelStyles.xaml` -- CardContainerStyle, SeparatorStyle
- `src/Deskbridge/Resources/TypographyStyles.xaml` -- SectionLabelStyle, BodyStyle, FieldLabelStyle, HintStyle
- `src/Deskbridge/Resources/SpacingResources.xaml` -- FormFieldSpacing, SectionLabelMargin, PanelContentMargin
- `tests/Deskbridge.Tests/Settings/PropertiesPanelSettingsTests.cs` -- test pattern for nullable record round-trips
- `tests/Deskbridge.Tests/Notifications/WindowStateServiceTests.cs` -- test pattern for settings persistence
- `tests/Deskbridge.Tests/Tabs/TabHostManagerTests.cs` -- BuildSut() at line 26 (will need update)
- `tests/Deskbridge.Tests/Tabs/TabHostManagerLruTests.cs` -- BuildSut() at line 24 (will need update)

### Secondary (MEDIUM confidence)
- `.planning/phases/18-settings-infrastructure/18-UI-SPEC.md` -- UI design contract, copywriting, spacing
- `.planning/phases/18-settings-infrastructure/18-CONTEXT.md` -- locked decisions D-01 through D-11
- `.planning/STATE.md` -- "Velopack uninstall hook cannot show UI -- headless context, must read settings.json via JsonDocument"

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no new libraries; all controls and patterns verified in existing codebase
- Architecture: HIGH -- follows established nullable sub-record + auto-save + suppress-persist patterns exactly
- Pitfalls: HIGH -- all identified from direct codebase inspection (hardcoded "15" in snackbar, test ctor signatures, JSON key paths)

**Research date:** 2026-04-26
**Valid until:** 2026-05-26 (stable patterns, no external dependency changes expected)
