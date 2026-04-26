# Phase 18: Settings Infrastructure - Pattern Map

**Mapped:** 2026-04-26
**Files analyzed:** 11 (modified/new)
**Analogs found:** 11 / 11

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/Deskbridge.Core/Settings/AppSettings.cs` | model | CRUD | Self: `AppearanceRecord` (lines 15-19), `PropertiesPanelRecord` (lines 96-101) | exact |
| `src/Deskbridge.Core/Settings/AppSettingsContext.cs` | config | CRUD | Self (lines 1-15) | exact |
| `src/Deskbridge.Core/Services/TabHostManager.cs` | service | event-driven | Self: ctor (lines 55-75), `GdiWarningThreshold` const (line 34), snackbar (lines 366-371) | exact |
| `src/Deskbridge/ViewModels/MainWindowViewModel.cs` | component | request-response | Self: `ApplySecuritySettings` (lines 509-524), `PersistSecuritySettings` (lines 542-556), `CurrentSecuritySettings` (line 527-528) | exact |
| `src/Deskbridge/MainWindow.xaml` | component | request-response | Self: SECURITY card section (lines 241-278) | exact |
| `src/Deskbridge/MainWindow.xaml.cs` | controller | CRUD | Self: `OnSourceInitialized` (lines 173-214), `TrySaveWindowState` (lines 316-360) | exact |
| `src/Deskbridge/App.xaml.cs` | config | CRUD | Self: TabHostManager DI registration (line 185) | exact |
| `tests/.../Tabs/TabHostManagerTests.cs` | test | request-response | Self: `BuildSut()` (lines 26-48) | exact |
| `tests/.../Tabs/TabHostManagerLruTests.cs` | test | request-response | Self: `BuildSut()` (lines 24-36) | exact |
| `tests/.../Settings/BulkOperationsSettingsTests.cs` | test | CRUD | `tests/.../Settings/PropertiesPanelSettingsTests.cs` (full file) | exact |
| `tests/.../Settings/UninstallSettingsTests.cs` | test | CRUD | `tests/.../Settings/PropertiesPanelSettingsTests.cs` (full file) | exact |

## Pattern Assignments

### `src/Deskbridge.Core/Settings/AppSettings.cs` (model, CRUD)

**Analog:** Self -- extend with two new records following established sub-record pattern.

**Record definition pattern** -- single-property record (`AppearanceRecord`, lines 15-19):
```csharp
public sealed record AppearanceRecord(
    TextScale TextScale = TextScale.Default)
{
    public static AppearanceRecord Default { get; } = new();
}
```

**Record definition pattern** -- multi-property record (`SecuritySettingsRecord`, lines 67-75):
```csharp
public sealed record SecuritySettingsRecord(
    int AutoLockTimeoutMinutes,
    bool LockOnMinimise,
    bool RequireMasterPassword = true)
{
    public static SecuritySettingsRecord Default { get; } =
        new(AutoLockTimeoutMinutes: 15, LockOnMinimise: false, RequireMasterPassword: true);
}
```

**Nullable property on AppSettings** (lines 29-36):
```csharp
public sealed record AppSettings(
    WindowStateRecord Window,
    SecuritySettingsRecord Security,
    UpdateSettingsRecord Update,
    PropertiesPanelRecord? PropertiesPanel = null,
    AppearanceRecord? Appearance = null,
    int SchemaVersion = 1)
{
    public AppSettings() : this(WindowStateRecord.Default, SecuritySettingsRecord.Default, UpdateSettingsRecord.Default) { }
}
```

New records `BulkOperationsRecord` and `UninstallRecord` follow the same sealed-record-with-static-Default pattern. New nullable properties append before `SchemaVersion` (which must stay last as a non-optional parameter would break the default ctor).

---

### `src/Deskbridge.Core/Settings/AppSettingsContext.cs` (config, CRUD)

**Analog:** Self (lines 1-15).

**Source generation context pattern** (full file):
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deskbridge.Core.Settings;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsContext : JsonSerializerContext { }
```

Add `[JsonSerializable(typeof(BulkOperationsRecord))]` and `[JsonSerializable(typeof(UninstallRecord))]` attributes. The `CamelCase` naming policy produces the JSON keys `bulkOperations.confirmBeforeBulkOperations`, `bulkOperations.gdiWarningThreshold`, and `uninstall.cleanUpOnUninstall` -- the Phase 24 Velopack hook depends on these exact paths.

---

### `src/Deskbridge.Core/Services/TabHostManager.cs` (service, event-driven)

**Analog:** Self -- in-place modification of constructor and threshold usage.

**Current const** (line 34):
```csharp
public const int GdiWarningThreshold = 15;
```

**Constructor signature** (lines 55-62):
```csharp
public TabHostManager(
    IEventBus bus,
    IConnectionCoordinator coordinator,
    IDisconnectPipeline disconnect,
    ISnackbarService snackbar,
    ILogger<TabHostManager> logger,
    Dispatcher? dispatcher = null)
```

**Snackbar warning with hardcoded "15"** (lines 366-371):
```csharp
_snackbar.Show(
    "Approaching session limit",
    "15 active sessions reached — performance may degrade beyond this point.",
    ControlAppearance.Caution,
    new SymbolIcon { Symbol = SymbolRegular.Warning24 },
    TimeSpan.FromSeconds(6));
```

**Threshold usage in comparison** (line 361):
```csharp
if (!_warned15 && _hosts.Count == GdiWarningThreshold)
```

**Re-arm on close** (line 205):
```csharp
if (_hosts.Count < GdiWarningThreshold) _warned15 = false;
```

Changes: (1) Rename const to `private const int DefaultGdiWarningThreshold = 15`. (2) Add `private readonly int _gdiWarningThreshold` field. (3) Append `IWindowStateService? windowState = null` AFTER `Dispatcher? dispatcher = null` in ctor. (4) Read threshold from settings in ctor body. (5) Replace all `GdiWarningThreshold` references with `_gdiWarningThreshold`. (6) Parameterize snackbar message string.

---

### `src/Deskbridge/ViewModels/MainWindowViewModel.cs` (component, request-response)

**Analog:** Self -- clone Security settings pattern for BulkOperations and Uninstall.

**Imports block** (lines 1-8):
```csharp
using System.Collections.ObjectModel;
using System.Windows;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Settings;
using Deskbridge.Models;
using Serilog;
```

**ObservableProperty declarations** (lines 480-488):
```csharp
[ObservableProperty]
public partial int AutoLockTimeoutMinutes { get; set; } = 15;

[ObservableProperty]
public partial bool LockOnMinimise { get; set; }
```

**OnChanged -> Persist hook** (line 539):
```csharp
partial void OnAutoLockTimeoutMinutesChanged(int value) => PersistSecuritySettings();
partial void OnLockOnMinimiseChanged(bool value) => PersistSecuritySettings();
```

**Persist method pattern** (lines 542-556):
```csharp
private async void PersistSecuritySettings()
{
    if (_suppressPersist) return;
    if (_windowState is null) return;
    try
    {
        var current = await _windowState.LoadAsync().ConfigureAwait(false);
        var updated = current with { Security = CurrentSecuritySettings };
        await _windowState.SaveAsync(updated).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to persist security settings");
    }
}
```

**Apply method pattern** (lines 509-524):
```csharp
public void ApplySecuritySettings(SecuritySettingsRecord security)
{
    ArgumentNullException.ThrowIfNull(security);
    _suppressPersist = true;
    try
    {
        AutoLockTimeoutMinutes = security.AutoLockTimeoutMinutes;
        LockOnMinimise = security.LockOnMinimise;
        RequireMasterPassword = security.RequireMasterPassword;
        IsMasterPasswordConfigured = _masterPassword?.IsMasterPasswordSet() ?? false;
    }
    finally
    {
        _suppressPersist = false;
    }
}
```

**Snapshot property pattern** (lines 527-528):
```csharp
public SecuritySettingsRecord CurrentSecuritySettings =>
    new(AutoLockTimeoutMinutes: AutoLockTimeoutMinutes, LockOnMinimise: LockOnMinimise, RequireMasterPassword: RequireMasterPassword);
```

Clone all three patterns (Apply, Persist, Current snapshot) for both `BulkOperationsRecord` and `UninstallRecord`. The `_suppressPersist` flag is shared across all settings groups (single bool, line 537).

---

### `src/Deskbridge/MainWindow.xaml` (component, request-response)

**Analog:** Self -- SECURITY card section (lines 241-278).

**Section label + card container pattern** (lines 241-244):
```xml
<TextBlock Text="SECURITY"
           Style="{StaticResource SectionLabelStyle}"
           Margin="0,16,0,4" />
<Border Style="{StaticResource CardContainerStyle}">
    <StackPanel>
```

**ToggleSwitch binding pattern** (lines 247-250):
```xml
<TextBlock Text="Require password/PIN"
           Style="{StaticResource BodyStyle}" />
<ui:ToggleSwitch IsChecked="{Binding RequireMasterPassword}"
                 Margin="0,4,0,12" />
```

**Separator pattern** (lines 252-253):
```xml
<!-- Separator -->
<Border Style="{StaticResource SeparatorStyle}" />
```

**NumberBox binding pattern** (lines 258-261):
```xml
<TextBlock Text="Auto-lock timeout"
           Style="{StaticResource FieldLabelStyle}" />
<ui:NumberBox Minimum="1" Maximum="1440"
              Value="{Binding AutoLockTimeoutMinutes, UpdateSourceTrigger=LostFocus}"
              Margin="{StaticResource FormFieldSpacing}" />
```

BULK OPERATIONS section inserts between SECURITY (line 278 closing `</Border>`) and DATA (line 281). UNINSTALL section appends after DATA (line 299 closing `</Border>`), before the `</StackPanel>` at line 300. Section order per D-02: Appearance, Security, Bulk Operations, Data, Uninstall.

---

### `src/Deskbridge/MainWindow.xaml.cs` (controller, CRUD)

**Analog:** Self -- OnSourceInitialized and TrySaveWindowState.

**Apply on load pattern** (lines 195-204):
```csharp
if (DataContext is ViewModels.MainWindowViewModel vm)
{
    vm.ApplySecuritySettings(_loadedSettings.Security);

    // Phase 14 (UX-02): apply persisted text scale
    var appearance = _loadedSettings.Appearance ?? AppearanceRecord.Default;
    vm.ApplyAppearanceSettings(appearance);
    vm.SetTextScaleCallback(ApplyTextScale);
    ApplyTextScale(appearance.TextScale);
}
```

Add `vm.ApplyBulkOperationsSettings(_loadedSettings.BulkOperations ?? BulkOperationsRecord.Default)` and `vm.ApplyUninstallSettings(_loadedSettings.Uninstall ?? UninstallRecord.Default)` after the appearance apply.

**Merge on close pattern** (lines 330-354):
```csharp
var security = vm?.CurrentSecuritySettings ?? _loadedSettings.Security;

var treeVm = _connectionTreeControl?.DataContext as ViewModels.ConnectionTreeViewModel;
var propertiesPanel = treeVm?.GetPropertiesPanelSettings()
    ?? _loadedSettings.PropertiesPanel
    ?? PropertiesPanelRecord.Default;

var appearance = vm?.CurrentAppearanceSettings
    ?? _loadedSettings.Appearance
    ?? AppearanceRecord.Default;

var updated = _loadedSettings with
{
    Window = new WindowStateRecord(x, y, w, h, isMaximized, sidebarOpen, sidebarWidth),
    Security = security,
    PropertiesPanel = propertiesPanel,
    Appearance = appearance,
};
```

Add `BulkOperations` and `Uninstall` to the `with` expression following the same `vm?.CurrentXxxSettings ?? _loadedSettings.Xxx ?? XxxRecord.Default` pattern.

---

### `src/Deskbridge/App.xaml.cs` (config, CRUD)

**Analog:** Self -- TabHostManager DI registration (line 185).

**Current registration** (lines 182-185):
```csharp
// Phase 5 (D-01): multi-host tab manager. Singleton. Subscribes to the coordinator's
// Host events in its ctor; resolve eagerly after build-service-provider so the
// subscriptions land before the first ConnectionRequestedEvent.
services.AddSingleton<ITabHostManager, TabHostManager>();
```

This uses the default DI constructor resolution. After adding `IWindowStateService? windowState = null` as the last optional parameter on TabHostManager's ctor, DI will automatically resolve `IWindowStateService` (already registered as a singleton). No explicit lambda factory needed -- DI resolves optional parameters when a registration exists.

---

### `tests/.../Tabs/TabHostManagerTests.cs` (test, request-response)

**Analog:** Self -- `BuildSut()` (lines 26-48).

**Current ctor call** (lines 45-48):
```csharp
var sut = new TabHostManager(
    bus, coord, disc, snack,
    NullLogger<TabHostManager>.Instance,
    Dispatcher.CurrentDispatcher);
```

Positional args through `Dispatcher.CurrentDispatcher` remain valid. The new `IWindowStateService? windowState = null` parameter defaults to null. No changes required to existing test callers unless a test needs to verify threshold behavior.

---

### `tests/.../Tabs/TabHostManagerLruTests.cs` (test, request-response)

**Analog:** Self -- `BuildSut()` (lines 24-36).

**Current ctor call** (lines 32-35):
```csharp
return new TabHostManager(
    bus, coord, disc, snack,
    NullLogger<TabHostManager>.Instance,
    Dispatcher.CurrentDispatcher);
```

Same as above -- positional args unchanged, `windowState` defaults to null. LRU tests don't touch the threshold.

---

### `tests/.../Settings/BulkOperationsSettingsTests.cs` (test, CRUD) -- NEW

**Analog:** `tests/Deskbridge.Tests/Settings/PropertiesPanelSettingsTests.cs` (full file).

**Test class structure** (lines 1-4):
```csharp
using System.Text.Json;
using Deskbridge.Core.Settings;

namespace Deskbridge.Tests.Settings;
```

**Default values test** (lines 8-14):
```csharp
[Fact]
public void PropertiesPanelRecord_DefaultValues_BothExpanded()
{
    var record = PropertiesPanelRecord.Default;
    record.IsConnectionCardExpanded.Should().BeTrue();
    record.IsCredentialsCardExpanded.Should().BeTrue();
}
```

**Null on AppSettings default ctor** (lines 16-23):
```csharp
[Fact]
public void AppSettings_Default_PropertiesPanel_IsNull()
{
    var settings = new AppSettings();
    settings.PropertiesPanel.Should().BeNull();
}
```

**Round-trip via source-gen context** (lines 25-46):
```csharp
[Theory]
[InlineData(true, true)]
[InlineData(false, false)]
[InlineData(true, false)]
[InlineData(false, true)]
public void PropertiesPanelRecord_Roundtrip_PreservesState(
    bool connExpanded, bool credExpanded)
{
    var original = new AppSettings(
        WindowStateRecord.Default,
        SecuritySettingsRecord.Default,
        UpdateSettingsRecord.Default,
        new PropertiesPanelRecord(connExpanded, credExpanded));

    var json = JsonSerializer.Serialize(original, AppSettingsContext.Default.AppSettings);
    var deserialized = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);

    deserialized.Should().NotBeNull();
    deserialized!.PropertiesPanel.Should().NotBeNull();
    deserialized.PropertiesPanel!.IsConnectionCardExpanded.Should().Be(connExpanded);
    deserialized.PropertiesPanel.IsCredentialsCardExpanded.Should().Be(credExpanded);
}
```

**Missing key backward-compat test** (lines 48-65):
```csharp
[Fact]
public void AppSettings_Deserialize_MissingPropertiesPanel_ReturnsNull()
{
    var json = """
    {
      "window": { "x": 100, "y": 100, "width": 1200, "height": 800, "isMaximized": false, "sidebarOpen": true, "sidebarWidth": 240 },
      "security": { "autoLockTimeoutMinutes": 15, "lockOnMinimise": false, "requireMasterPassword": true },
      "update": { "useBetaChannel": false },
      "schemaVersion": 1
    }
    """;
    var settings = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);

    settings.Should().NotBeNull();
    settings!.PropertiesPanel.Should().BeNull();
}
```

Clone all four test shapes for `BulkOperationsRecord`. Adapt the `[Theory]` InlineData to cover `(bool confirmBulk, int threshold)` combinations.

---

### `tests/.../Settings/UninstallSettingsTests.cs` (test, CRUD) -- NEW

**Analog:** `tests/Deskbridge.Tests/Settings/PropertiesPanelSettingsTests.cs` (full file) + one additional test.

Same four test shapes as BulkOperationsSettingsTests above, adapted for `UninstallRecord(bool CleanUpOnUninstall)`. One additional test unique to this file:

**Phase 24 JSON key path contract test** (no existing analog -- new pattern):
```csharp
// Verify raw JSON key path matches Phase 24's JsonDocument.GetProperty("uninstall").GetProperty("cleanUpOnUninstall")
[Fact]
public void UninstallRecord_JsonKeyPath_MatchesPhase24Contract()
{
    var settings = new AppSettings(
        WindowStateRecord.Default,
        SecuritySettingsRecord.Default,
        UpdateSettingsRecord.Default,
        Uninstall: new UninstallRecord(CleanUpOnUninstall: true));

    var json = JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings);
    using var doc = JsonDocument.Parse(json);

    doc.RootElement.GetProperty("uninstall")
       .GetProperty("cleanUpOnUninstall")
       .GetBoolean()
       .Should().BeTrue();
}
```

---

## Shared Patterns

### Auto-Save Persistence (suppress-persist guard)
**Source:** `src/Deskbridge/ViewModels/MainWindowViewModel.cs` lines 537, 542-556
**Apply to:** All new ViewModel settings properties (BulkOperations and Uninstall)

The `_suppressPersist` bool (line 537) is a shared guard across ALL settings groups. Apply/Persist/Current-snapshot is a three-part contract:
1. `Apply<Group>Settings(<Record>)` -- sets VM properties inside `_suppressPersist = true` block
2. `Persist<Group>Settings()` -- async void, Load -> `with { }` -> Save, guarded by `_suppressPersist`
3. `Current<Group>Settings` -- read-only snapshot property for OnClosing merge

### Nullable Sub-Record with Default
**Source:** `src/Deskbridge.Core/Settings/AppSettings.cs` lines 15-19, 96-101
**Apply to:** `BulkOperationsRecord`, `UninstallRecord`

Every new settings section uses `sealed record` with defaulted ctor params and a `static Default` property. Added to `AppSettings` as a nullable optional parameter, null-coalesced on load.

### Card Section XAML Layout
**Source:** `src/Deskbridge/MainWindow.xaml` lines 241-278
**Apply to:** BULK OPERATIONS and UNINSTALL XAML sections

Pattern: `TextBlock` (SectionLabelStyle, Margin "0,16,0,4") -> `Border` (CardContainerStyle) -> `StackPanel` -> controls. ToggleSwitch uses `BodyStyle` label + `Margin="0,4,0,12"`. NumberBox uses `FieldLabelStyle` label + `FormFieldSpacing` margin. Separator via `Border` with `SeparatorStyle`.

### Settings Apply-on-Load
**Source:** `src/Deskbridge/MainWindow.xaml.cs` lines 195-204
**Apply to:** New BulkOperations and Uninstall settings

In `OnSourceInitialized`, after `_loadedSettings` is hydrated: `vm.Apply<Group>Settings(_loadedSettings.<Group> ?? <Group>Record.Default)`.

### Settings Merge-on-Close
**Source:** `src/Deskbridge/MainWindow.xaml.cs` lines 330-354
**Apply to:** New BulkOperations and Uninstall settings

In `TrySaveWindowState`, capture: `vm?.Current<Group>Settings ?? _loadedSettings.<Group> ?? <Group>Record.Default`. Add to the `_loadedSettings with { ... }` expression.

### Test Structure for Settings Round-Trips
**Source:** `tests/Deskbridge.Tests/Settings/PropertiesPanelSettingsTests.cs` (full file)
**Apply to:** `BulkOperationsSettingsTests.cs`, `UninstallSettingsTests.cs`

Four-test pattern: (1) default values, (2) null on default AppSettings ctor, (3) round-trip via `AppSettingsContext.Default.AppSettings`, (4) missing key backward-compat with raw JSON string.

## No Analog Found

No files lack analogs. All 11 files have exact matches from existing codebase patterns.

## Metadata

**Analog search scope:** `src/Deskbridge.Core/Settings/`, `src/Deskbridge.Core/Services/`, `src/Deskbridge/ViewModels/`, `src/Deskbridge/`, `src/Deskbridge/App.xaml.cs`, `tests/Deskbridge.Tests/Settings/`, `tests/Deskbridge.Tests/Tabs/`
**Files scanned:** 11 target files, 6 unique analog source files
**Pattern extraction date:** 2026-04-26
