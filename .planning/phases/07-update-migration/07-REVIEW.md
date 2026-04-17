---
phase: 07-update-migration
reviewed: 2026-04-15T00:00:00Z
depth: standard
files_reviewed: 23
files_reviewed_list:
  - src/Deskbridge.Core/Interfaces/IUpdateService.cs
  - src/Deskbridge.Core/Services/UpdateService.cs
  - src/Deskbridge.Core/Interfaces/IConnectionImporter.cs
  - src/Deskbridge.Core/Models/ImportModels.cs
  - src/Deskbridge.Core/Services/MRemoteNGImporter.cs
  - src/Deskbridge.Core/Services/ConnectionExporter.cs
  - src/Deskbridge.Core/Services/CommandPaletteService.cs
  - src/Deskbridge.Core/Settings/AppSettings.cs
  - src/Deskbridge/ViewModels/ImportWizardViewModel.cs
  - src/Deskbridge/ViewModels/MainWindowViewModel.cs
  - src/Deskbridge/Dialogs/UpdateConfirmDialog.xaml
  - src/Deskbridge/Dialogs/UpdateConfirmDialog.xaml.cs
  - src/Deskbridge/Dialogs/ImportWizardDialog.xaml
  - src/Deskbridge/Dialogs/ImportWizardDialog.xaml.cs
  - src/Deskbridge/Converters/NullToBoolConverter.cs
  - src/Deskbridge/MainWindow.xaml
  - src/Deskbridge/MainWindow.xaml.cs
  - src/Deskbridge/App.xaml.cs
  - .github/workflows/build.yml
  - tests/Deskbridge.Tests/Update/UpdateServiceTests.cs
  - tests/Deskbridge.Tests/Import/MRemoteNGImporterTests.cs
  - tests/Deskbridge.Tests/Import/ConnectionExporterTests.cs
  - tests/Deskbridge.Tests/DiCompositionTests.cs
findings:
  critical: 2
  warning: 6
  info: 5
  total: 13
status: issues_found
---

# Phase 07: Code Review Report

**Reviewed:** 2026-04-15T00:00:00Z
**Depth:** standard
**Files Reviewed:** 23
**Status:** issues_found

## Summary

Phase 07 lands auto-update (Velopack wrapper), the mRemoteNG import wizard, and JSON/CSV export. Overall the codebase is in good shape — the XXE guard (`DtdProcessing.Prohibit`) is in place, the `BasedOn` style is present on both custom `ContentDialog` subclasses, `ContentDialogHost` is used correctly, and password data is not written to any output. Two critical issues were found: a thread-safety gap in `UpdateService` where `PendingVersion` and `_pendingUpdate` can diverge under concurrent calls, and a path-traversal / file-write risk in the export code-behind that resolves `IConnectionStore` from a nullable service locator without guarding the resulting file path. Six warnings cover logic gaps in the import duplicate-handling flow, a sync-over-async blocking call in `App.xaml.cs`, and a workflow flag correction.

---

## Critical Issues

### CR-01: Race condition — `PendingVersion` and `_pendingUpdate` can diverge under concurrent `CheckForUpdatesAsync` calls

**File:** `src/Deskbridge.Core/Services/UpdateService.cs:83-96`

**Issue:** `CheckForUpdatesAsync` is called from `App.OnStartup` via `Task.Run`, meaning it runs on a thread-pool thread. There is no synchronisation guard preventing two concurrent calls from running simultaneously. If two calls overlap, the first call sets `_pendingUpdate` (line 130) and the second overwrites it before the first sets `PendingVersion` (line 86). The result is that `PendingVersion` reflects version A while `_pendingUpdate` points to package B. When `ApplyUpdatesAndRestart` is called it passes `_pendingUpdate!` to Velopack, silently applying a different package than the one displayed to the user in the status bar badge. Additionally `_pendingUpdate` is a non-volatile field written on a thread-pool thread and read on the UI thread in `ApplyUpdatesInternalAndRestart`, which is a data race on the reference without a memory barrier.

**Fix:** Add a `SemaphoreSlim(1,1)` guard around the body of `CheckForUpdatesAsync` and keep both writes together atomically:

```csharp
private readonly SemaphoreSlim _checkLock = new(1, 1);

public async Task<bool> CheckForUpdatesAsync(CancellationToken ct = default)
{
    if (!IsInstalled)
    {
        Log.Warning("Update check skipped: not installed via Velopack");
        return false;
    }

    if (!await _checkLock.WaitAsync(0, ct))   // non-blocking: skip if already running
        return false;
    try
    {
        var version = await CheckForUpdatesInternalAsync(ct).ConfigureAwait(false);
        if (version is not null)
        {
            PendingVersion = version;
            _bus.Publish(new UpdateAvailableEvent(version));
            return true;
        }
        return false;
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Update check failed — will retry on next startup");
        return false;
    }
    finally
    {
        _checkLock.Release();
    }
}
```

---

### CR-02: Nullable service-locator pattern in export code-behind bypasses error feedback to the user

**File:** `src/Deskbridge/MainWindow.xaml.cs:684-686` (and line 712-714 for CSV)

**Issue:** Both `ExportJsonButton_Click` and `ExportCsvButton_Click` resolve `IConnectionStore` via a nullable service-locator (`((App)Application.Current).Services?.GetService(typeof(IConnectionStore)) as IConnectionStore`). When the cast fails (returns `null`), the method silently returns with no feedback to the user — the Save dialog has already closed, the file has been created/truncated on disk, but no data was written. On Windows, `File.WriteAllTextAsync` would have been called with the full path chosen by the user; in this pattern the early `return` after the null-check means the file stays empty (0 bytes) with no error shown. This is a silent data loss: the user believes their connections were exported but the file is blank.

The service-locator pattern is also architecturally wrong here: `IConnectionStore` was already injected into the service container and could be passed to `MainWindow` as a constructor parameter or used via the same pattern as the command palette.

**Fix:** Inject `IConnectionStore` as a constructor parameter alongside the other services:

```csharp
// In App.xaml.cs ConfigureServices, add to the MainWindow factory:
services.AddSingleton<MainWindow>(sp => new MainWindow(
    /* existing params */,
    sp.GetRequiredService<IConnectionStore>()));   // <-- add

// In MainWindow ctor:
private readonly IConnectionStore _connectionStore;

public MainWindow(/* existing params */, IConnectionStore connectionStore)
{
    // ...
    _connectionStore = connectionStore;
}

// Then in ExportJsonButton_Click:
var json = ConnectionExporter.ExportJson(_connectionStore.GetAll(), _connectionStore.GetGroups());
```

If keeping the service-locator approach in the interim, at minimum log and show a user-visible error on null:

```csharp
if (((App)Application.Current).Services?.GetService(typeof(IConnectionStore)) is not IConnectionStore store)
{
    Serilog.Log.Warning("ExportJson: IConnectionStore could not be resolved");
    return;  // already a silent no-op, but at least logged
}
```

The critical gap is the silent partial failure (dialog closed, file empty). A toast notification should be shown on any failure path.

---

## Warnings

### WR-01: `DownloadUpdatesAsync` missing `IsInstalled` guard — silently no-ops when called directly without a prior check

**File:** `src/Deskbridge.Core/Services/UpdateService.cs:100-106`

**Issue:** `DownloadUpdatesAsync` guards on `PendingVersion is null` but not on `!IsInstalled`. The interface doc says "Requires a prior successful `CheckForUpdatesAsync` call" but this is not enforced. A caller who bypasses the check path (e.g. a future test stub that sets `PendingVersion` directly via reflection, or a subclass override) could pass the guard and call `DownloadUpdatesInternalAsync` in a non-installed environment, which calls `_mgr.DownloadUpdatesAsync` with `_mgr` being null — throwing `NullReferenceException` inside the virtual override. The `_mgr is null` guard in `DownloadUpdatesInternalAsync` (line 140) catches the production path but not subclass overrides that forget to check.

**Fix:** Add the `IsInstalled` guard consistently:

```csharp
public async Task DownloadUpdatesAsync(IProgress<int>? progress = null, CancellationToken ct = default)
{
    if (!IsInstalled || PendingVersion is null) return;
    // ...
}
```

---

### WR-02: Import duplicate handling has a logic gap — unresolved duplicates are silently auto-renamed without populating `DuplicateItems`

**File:** `src/Deskbridge/ViewModels/ImportWizardViewModel.cs:295-346`

**Issue:** The duplicate-resolution block in `ImportSelectedAsync` checks `DuplicateItems` for an existing resolution entry. If no resolution entry exists for a duplicate hostname, the code falls through to the "Auto-rename if no explicit resolution" block (line 339) and imports with ` (imported)` suffix — but `DuplicateItems` is never populated during this import pass. This means the step 3 UI never shows duplicate items for user review before the Import button is clicked. The user sees no indication that conflicts exist, the duplicates are silently auto-renamed, and the summary on step 4 shows `RenamedCount` which is surprising without context.

The intent appears to be: detect duplicates in a pre-pass, populate `DuplicateItems`, let the user resolve them (step 3b or embedded in the tree), then run the actual import. Currently there is no pre-pass — duplicates are only detected at import time when it is already too late for user resolution.

**Fix:** Add a pre-pass before the import loop to populate `DuplicateItems` and block progression if unresolved conflicts exist, or explicitly document in the UI that auto-rename is the policy and remove the dead resolution check code. At minimum, add a comment explaining the current auto-rename-always behavior so the intent is clear:

```csharp
// NOTE: DuplicateItems resolution UI is not yet wired. Current policy:
// duplicate hostnames are always auto-renamed with "(imported)" suffix.
// The DuplicateAction check below is dead code until a pre-pass populates DuplicateItems.
```

---

### WR-03: `PersistSecuritySettings` uses fire-and-forget `async void` — exceptions are silently swallowed at the call site

**File:** `src/Deskbridge/ViewModels/MainWindowViewModel.cs:447-461`

**Issue:** `PersistSecuritySettings` is declared `async void`. The method itself has a `try/catch` that logs failures, so the immediate risk is low. However, `async void` methods do not support `await` at call sites (`partial void OnAutoLockTimeoutMinutesChanged` and `OnLockOnMinimiseChanged` call it synchronously), meaning if the method throws before the first `await` (e.g. `_windowState` is null and the guard on line 449 is somehow bypassed), the exception propagates to the `SynchronizationContext` and becomes an unhandled exception that crashes the app. Additionally, rapid successive property changes (e.g. loading settings triggering multiple `Changed` callbacks) spawn multiple concurrent writes with no serialization.

**Fix:** This is an established pattern in WPF MVVM and the internal `try/catch` mitigates the crash risk, but the concurrent write issue is real. Use `CancellationTokenSource` debounce or delegate persistence to the existing `_windowState.SaveAsync` which should already serialize writes. At minimum, document the async-void choice explicitly:

```csharp
// async void is intentional here: this is a fire-and-forget ViewModel callback
// from an [ObservableProperty] Changed partial method (which must return void).
// The internal try/catch prevents unhandled exceptions from reaching SynchronizationContext.
// Rapid successive calls may overlap; IWindowStateService.SaveAsync must be idempotent.
private async void PersistSecuritySettings()
```

---

### WR-04: `App.xaml.cs` uses blocking `GetAwaiter().GetResult()` inside DI factory lambdas — risks deadlock on the UI thread

**File:** `src/Deskbridge/App.xaml.cs:223` (and lines 364, 370, 384)

**Issue:** Several singleton DI factory lambdas call `windowState.LoadAsync().GetAwaiter().GetResult()` synchronously. These lambdas execute during `_serviceProvider = services.BuildServiceProvider()` on line 65, which is called from `OnStartup` on the WPF UI (STA) thread. If `WindowStateService.LoadAsync` internally ever posts continuations back to the SynchronizationContext (e.g. if a future refactor adds a `ConfigureAwait(true)` path or uses `Dispatcher.InvokeAsync`), this would deadlock: the UI thread is blocked by `GetResult()` waiting for a continuation that can only run on the UI thread.

The file is small (< 1 KB per the comment) so the risk in practice is low today, but the pattern is fragile as a coding convention.

**Fix:** Move the settings load to a dedicated synchronous helper that clearly owns the sync-over-async contract and is called once, then passed to the factories:

```csharp
// In OnStartup, before services.BuildServiceProvider():
var windowStateService = new WindowStateService(); // temp instance for startup
var startupSettings = windowStateService.LoadAsync().GetAwaiter().GetResult();

// Pass startupSettings directly to factories:
services.AddSingleton<IUpdateService>(_ =>
    new UpdateService(bus, "https://github.com/...", startupSettings.Update?.UseBetaChannel ?? false));
```

---

### WR-05: GitHub Actions `build` job publishes with `PublishSingleFile=true` but `release` job does not — Velopack receives a multi-file layout

**File:** `.github/workflows/build.yml:88-94`

**Issue:** The `build` job (lines 43-44) publishes with `-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`, producing a single executable. The `release` job (lines 87-94) publishes without `PublishSingleFile=true`, producing a multi-file layout in the `publish/` directory. Velopack's `vpk pack` (line 108-113) then packs this multi-file output. This is correct for Velopack — `vpk pack` expects a publish directory, not a single file. However, the `build` job's single-file output is uploaded as the `Deskbridge-win-x64-*` artifact (line 47-52), which differs structurally from what Velopack distributes to end users via the release job. Users who download the GitHub artifact from a CI run get a different binary format than users who install via Velopack. This is a consistency issue: both should use the same publish flags.

Additionally, the `release` job does not re-run the test suite before packaging — it only publishes and packs. A tag push that skips the `build` job's test gate could ship a broken release.

**Fix:** Align the publish flags and add a test gate to the release job:

```yaml
# release job: add test step before Pack
- name: Test
  run: dotnet test Deskbridge.sln --configuration Release --filter "Category!=UAT&Category!=Slow"

# And add PublishSingleFile to match the build job (or remove it from build job):
- name: Publish
  run: >
    dotnet publish src/Deskbridge/Deskbridge.csproj
    --configuration Release
    --runtime win-x64
    --self-contained true
    -p:Version=${{ steps.version.outputs.version }}
    -p:PublishSingleFile=true
    -p:IncludeNativeLibrariesForSelfExtract=true
    --output publish
```

---

### WR-06: `SanitizeName` in `MRemoteNGImporter` allows tab and newline characters through — these render incorrectly in WPF TextBlock

**File:** `src/Deskbridge.Core/Services/MRemoteNGImporter.cs:119-125`

**Issue:** The sanitizer explicitly allows `\t` (tab), `\n` (newline), and `\r` (carriage return) through the control-character filter. Connection names are displayed in WPF `TextBlock` elements (the tree view and import wizard preview). WPF `TextBlock` does not interpret tab as whitespace expansion — it renders as a variable-width gap that can misalign the entire tree row. Newlines in a name collapse to a thin gap in single-line TextBlock contexts, causing the name to appear truncated. For the import wizard `TreeView` specifically (`ImportWizardDialog.xaml:66`), a name with `\n` would make the `StackPanel` row overflow its intended height.

The comment says "except tab and newline" which implies an intent to preserve them, but there is no downstream consumer that renders these characters correctly.

**Fix:** Remove the exceptions and strip all control characters below `0x20`:

```csharp
// Skip all control characters (< 0x20), no exceptions
if (c < '\x20')
    continue;
```

---

## Info

### IN-01: `ImportWizardViewModel` — `ImportProtocolExtensions.ToProtocol` is a no-op identity method

**File:** `src/Deskbridge/ViewModels/ImportWizardViewModel.cs:521`

**Issue:** `ToProtocol(this Protocol protocol) => protocol` is an identity extension that converts `Protocol` to `Protocol`. This exists because `ImportTreeItemViewModel.Protocol` is typed as `Protocol` (the same enum as `ConnectionModel.Protocol`), so the conversion is trivially the same type. The method adds noise and may confuse readers into thinking there is a type distinction between the two. If the import model ever introduces its own `ImportProtocol` enum (to distinguish SSH1 from SSH2, for instance), this extension method would be the right seam, but as written it is dead code.

**Fix:** Remove `ToProtocol` and use the `Protocol` property directly:

```csharp
Protocol = item.Protocol,  // instead of item.Protocol.ToProtocol()
```

---

### IN-02: `DuplicateAction` default is `Rename` but the auto-rename fallback path doesn't check `DuplicateItems` is empty

**File:** `src/Deskbridge/ViewModels/ImportWizardViewModel.cs:480`

**Issue:** `DuplicateItemViewModel.Action` defaults to `DuplicateAction.Rename`. The import loop checks for `DuplicateAction.Skip`, `Rename`, and `Overwrite` explicitly. However, since `DuplicateItems` is never populated (see WR-02), the default value is irrelevant to the actual behavior. This creates a false impression that the default action matters.

**Fix:** Address as part of the WR-02 fix. Once `DuplicateItems` is populated via a pre-pass, the default `Rename` will be the correct safe default.

---

### IN-03: `CommandPaletteService` registers `export-json` and `export-csv` with identical icons

**File:** `src/Deskbridge.Core/Services/CommandPaletteService.cs:93-109`

**Issue:** Both `export-json` and `export-csv` commands use `SymbolRegular.ArrowExportLtr24` as their icon. In the command palette, the two entries are visually identical aside from the title text. This is a minor usability issue for keyboard-first workflows where icons provide quick visual differentiation.

**Fix:** Use `SymbolRegular.DocumentArrowRight24` (or similar) for one of the export commands to distinguish them. This is a cosmetic preference only; the current icons are functional.

---

### IN-04: `UpdateConfirmDialog.xaml` uses `Severity="Caution"` which is not a valid `InfoBarSeverity` value in WPF-UI 4.x

**File:** `src/Deskbridge/Dialogs/UpdateConfirmDialog.xaml:29`

**Issue:** The `InfoBar` on line 29 sets `Severity="Caution"`. WPF-UI 4.x `InfoBarSeverity` has values `Informational`, `Success`, `Warning`, and `Error`. `Caution` is not a valid enum member. At runtime this will throw a `FormatException` or `XamlParseException` when the dialog is opened. The intended severity is almost certainly `Warning`.

**Fix:**
```xml
<ui:InfoBar
    IsOpen="True" IsClosable="False"
    Severity="Warning" Margin="0,12,0,0"
    Title="Active sessions will be disconnected"
    Message="All open RDP sessions will be closed when the application restarts." />
```

---

### IN-05: `ImportWizardDialog.xaml` — `BoolToVisibility` converter is referenced via `StaticResource` but is not defined in the dialog's own resources

**File:** `src/Deskbridge/Dialogs/ImportWizardDialog.xaml:22-23` (and throughout)

**Issue:** The XAML uses `{StaticResource BoolToVisibility}` and `{StaticResource NullToBool}` without declaring them in `ImportWizardDialog.Resources` or a merged dictionary in the file. These converters must be defined in the `App.xaml` merged resource dictionary (or `MainWindow.xaml` resources) for the `StaticResource` lookup to succeed. If the dialog is ever opened from a context where those resources are not in scope (e.g. a test harness or a different host window), the lookup will throw `XamlParseException` at parse time. The dependency on application-level resources is implicit and fragile.

**Fix:** Add explicit resource declarations in the dialog's own `Resources` block, or document the requirement clearly:

```xml
<ui:ContentDialog.Resources>
    <Style BasedOn="{StaticResource {x:Type ui:ContentDialog}}"
           TargetType="{x:Type local:ImportWizardDialog}" />
    <converters:BooleanToVisibilityConverter x:Key="BoolToVisibility" />
    <converters:NullToBoolConverter x:Key="NullToBool" />
</ui:ContentDialog.Resources>
```

---

_Reviewed: 2026-04-15T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
