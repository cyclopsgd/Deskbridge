# Phase 21: Performance Optimizations — Pattern Map

**Mapped:** 2026-04-30
**Files analyzed:** 8 (5 modified + 3 new)
**Analogs found:** 8 / 8 (100% — all patterns exist in-repo)

> Test project layout note: there is **no separate `tests/Deskbridge.Core.Tests/` project** in this repo. All Core service tests live in `tests/Deskbridge.Tests/` (e.g., `tests/Deskbridge.Tests/JsonConnectionStoreTests.cs`). The CONTEXT/RESEARCH paths that reference `tests/Deskbridge.Core.Tests/...` should be remapped to `tests/Deskbridge.Tests/...`. This is reflected below.

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/Deskbridge/Views/ConnectionTreeControl.xaml` (modify) | view (XAML template) | request-response (data binding) | self — adjacent template lines 339-385 | exact (in-place additions) |
| `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` (modify) | view-model | event-driven (text-change → debounce → filter) | `src/Deskbridge/MainWindow.xaml.cs` lines 411-418 (resize debounce) | role-different, data-flow exact |
| `src/Deskbridge.Core/Services/JsonConnectionStore.cs` (modify) | service | file-I/O (sync → async wrapper) | self — `Load()` body at lines 42-65 | exact (Task.Run wrap) |
| `src/Deskbridge.Core/Interfaces/IConnectionStore.cs` (modify) | interface | contract | self — existing method signatures lines 7-17 | exact |
| `src/Deskbridge/App.xaml.cs` (modify) | startup composition | request-response (DI build → load → show) | self — `OnStartup` lines 35-148 + factory lines 201-206 | exact (in-place async refactor) |
| Test fakes for `IConnectionStore` | test fake | stub | `tests/Deskbridge.Tests/ViewModels/ConnectionTreeStateTrackingTests.cs` lines 18-33 (NSubstitute pattern) | exact — all fakes are NSubstitute auto-stubs, **no manual fakes to update** |
| `tests/Deskbridge.Tests/Services/JsonConnectionStoreAsyncLoadTests.cs` (new) | xUnit test | file-I/O verification | `tests/Deskbridge.Tests/JsonConnectionStoreTests.cs` lines 1-52 | exact (xUnit + IDisposable temp dir) |
| `tests/Deskbridge.Tests/ViewModels/ConnectionTreeSearchDebounceTests.cs` (new) | xUnit test | event-driven (timer-driven) | `tests/Deskbridge.Tests/ViewModels/ConnectionTreeStateTrackingTests.cs` lines 1-50 (VM ctor + bus pattern) | exact-role |

---

## Pattern Assignments

### `src/Deskbridge/Views/ConnectionTreeControl.xaml` (view, request-response)

**Analog:** the file itself — both insertions go inside the existing `<TreeView>` element and the existing group `<HierarchicalDataTemplate>` `StackPanel`.

**PERF-01 insertion site — existing virtualization attributes** (lines 339-354):
```xml
<TreeView x:Name="ConnectionTree"
          ItemsSource="{Binding RootItems}"
          ItemContainerStyle="{StaticResource FullRowTreeViewItemStyle}"
          HorizontalContentAlignment="Stretch"
          ScrollViewer.HorizontalScrollBarVisibility="Disabled"
          VirtualizingPanel.IsVirtualizing="True"
          VirtualizingPanel.VirtualizationMode="Recycling"
          ScrollViewer.CanContentScroll="True"
          BorderThickness="0"
          Background="Transparent"
          Padding="0"
          behaviors:TreeViewMultiSelectBehavior.EnableMultiSelect="True"
          behaviors:TreeViewDragDropBehavior.EnableDragDrop="True"
          PreviewKeyDown="TreeView_PreviewKeyDown"
          PreviewMouseDoubleClick="TreeView_PreviewMouseDoubleClick"
          PreviewMouseRightButtonDown="TreeView_PreviewMouseRightButtonDown">
```
Add **`VirtualizingPanel.ScrollUnit="Pixel"`** as a sibling attribute alongside `IsVirtualizing` / `VirtualizationMode` (group all three `VirtualizingPanel.*` attributes together for readability). Do NOT modify any other attribute on this element.

**PERF-05 insertion site — group HierarchicalDataTemplate StackPanel** (lines 357-385):
```xml
<HierarchicalDataTemplate DataType="{x:Type vm:GroupTreeItemViewModel}"
                          ItemsSource="{Binding Children}">
    <StackPanel Orientation="Horizontal" Height="28" VerticalAlignment="Center">
        <ui:SymbolIcon FontSize="16" Margin="0,0,4,0"
                       Foreground="{DynamicResource TextFillColorPrimaryBrush}"
                       VerticalAlignment="Center">
            <ui:SymbolIcon.Style>
                <Style TargetType="ui:SymbolIcon">
                    <Setter Property="Symbol" Value="Folder24" />
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding IsExpanded}" Value="True">
                            <Setter Property="Symbol" Value="FolderOpen24" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </ui:SymbolIcon.Style>
        </ui:SymbolIcon>

        <TextBlock Text="{Binding Name}"
                   FontWeight="SemiBold" FontSize="14"
                   Foreground="{DynamicResource TextFillColorPrimaryBrush}"
                   VerticalAlignment="Center" />

        <!-- INSERT BADGE TEXTBLOCK HERE (between Name and KeyIcon) -->

        <ui:SymbolIcon Symbol="Key24" FontSize="12"
                       Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                       Margin="4,0,0,0"
                       VerticalAlignment="Center"
                       Visibility="{Binding HasCredentials, Converter={StaticResource BoolToVisibility}}" />
    </StackPanel>
</HierarchicalDataTemplate>
```

**Badge XAML to insert** — copy verbatim from `21-UI-SPEC.md` lines 142-157 (locked):
```xml
<TextBlock Text="{Binding ConnectionCount, StringFormat='({0})'}"
           FontSize="12"
           FontWeight="Regular"
           Foreground="{DynamicResource TextFillColorSecondaryBrush}"
           Margin="4,0,0,0"
           VerticalAlignment="Center">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Visible" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding ConnectionCount}" Value="0">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
            </Style.Triggers>
        </TextBlock.Style>
    </TextBlock>
</TextBlock>
```

**Pattern conventions to preserve:**
- Existing `Foreground="{DynamicResource TextFillColorSecondaryBrush}"` matches the sibling `ui:SymbolIcon Symbol="Key24"` (line 381) — same secondary tier.
- Existing `Margin="4,0,0,0"` 4px-gap rhythm — same value as the key icon.
- Existing `VerticalAlignment="Center"` on every child of the 28px-Height StackPanel — must keep.

---

### `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` (view-model, event-driven debounce)

**Analog:** `src/Deskbridge/MainWindow.xaml.cs` lines 411-418 (resize debounce — proven, identical reset-on-input pattern).

**Imports pattern** — `MainWindow.xaml.cs` line 5 already imports `System.Windows.Threading`:
```csharp
using System.Windows.Threading;
```
The VM file does NOT yet import `System.Windows.Threading`. Add it to the existing `using` block at the top of `ConnectionTreeViewModel.cs` (currently lines 1-12). `DispatcherTimer` lives there.

**DispatcherTimer field declaration shape** (`MainWindow.xaml.cs` lines 64-68):
```csharp
/// <summary>
/// Phase 16 (STAB-03): debounce timer for dynamic resolution updates on window resize.
/// T-16-03: 500ms debounce prevents flooding UpdateSessionDisplaySettings.
/// </summary>
private DispatcherTimer? _resizeTimer;
```
Note: nullable, lazily constructed inside the change handler. **No constructor initialisation, no `IDisposable` plumbing.**

**Reset-on-input + Tick-rebind pattern** (`MainWindow.xaml.cs` lines 411-418) — copy verbatim, only the interval and handler names change:
```csharp
private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
{
    _resizeTimer?.Stop();
    _resizeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
    _resizeTimer.Tick -= OnResizeSettled;
    _resizeTimer.Tick += OnResizeSettled;
    _resizeTimer.Start();
}

private void OnResizeSettled(object? sender, EventArgs e)
{
    _resizeTimer?.Stop();
    // ... do work ...
}
```

The five-step shape **must be reproduced exactly**: `?.Stop()` → `??= new DispatcherTimer { Interval = ... }` → `Tick -=` → `Tick +=` → `Start()`. Tick handler **stops first**, then runs work.

**Existing `OnSearchTextChanged` (lines 493-509) to wrap** — current sync body that becomes `ApplySearchFilter`:
```csharp
// --- Search filter ---

partial void OnSearchTextChanged(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        // Restore hierarchical view
        RootItems = new ObservableCollection<TreeItemViewModel>(_fullTree);
        return;
    }

    // Flatten tree and filter by name/hostname match
    var searchLower = value.ToLowerInvariant();
    var flatMatches = new ObservableCollection<TreeItemViewModel>();
    FlattenAndFilter(_fullTree, searchLower, flatMatches);
    RootItems = flatMatches;
}
```
Refactor: rename body to `private void ApplySearchFilter(string value)` (extract whole-cloth, identical signature except access modifier). The new `OnSearchTextChanged` becomes the debounce gate per RESEARCH.md section 2 (clear-search synchronous bypass + reset-on-input timer).

**[ObservableProperty] declaration that drives the partial** (line 264-265, do **not** modify):
```csharp
[ObservableProperty]
public partial string SearchText { get; set; } = string.Empty;
```
This is the source-generator pattern — `OnSearchTextChanged(string value)` is a `partial void` that the generator wires automatically. Keep the declaration as-is; only modify the partial method body.

**Pattern conventions to preserve:**
- VM is a singleton (`services.AddSingleton<ViewModels.ConnectionTreeViewModel>` in `App.xaml.cs:423`) — timer lifetime matches the app, no `IDisposable` needed (matches `MainWindow._resizeTimer` lifetime story).
- Comment style for new field: triple-slash XML doc with phase tag — mirror lines 64-68 of `MainWindow.xaml.cs` (`/// <summary> Phase 21 (PERF-02): ... </summary>`).

---

### `src/Deskbridge.Core/Services/JsonConnectionStore.cs` (service, file-I/O)

**Analog:** the existing sync `Load()` method at lines 42-65 within the same file.

**Existing sync `Load()` to wrap** (lines 42-65):
```csharp
public void Load()
{
    if (!File.Exists(_filePath))
    {
        _data = new ConnectionsFile();
        return;
    }

    try
    {
        var json = File.ReadAllText(_filePath, System.Text.Encoding.UTF8);
        _data = JsonSerializer.Deserialize<ConnectionsFile>(json, _jsonOptions) ?? new ConnectionsFile();
        // Version check for future migrations
        if (_data.Version > 1)
        {
            Log.Warning("connections.json version {Version} is newer than supported (1). Data may be incomplete", _data.Version);
        }
    }
    catch (JsonException ex)
    {
        Log.Error(ex, "Failed to load connections.json, starting with empty collection");
        _data = new ConnectionsFile();
    }
}
```

**LoadAsync pattern (Option A, recommended by RESEARCH.md §3.1)** — minimal wrapper, retains sync semantics for benchmarks:
```csharp
public Task LoadAsync() => Task.Run(Load);
```
Place immediately after the existing `Load()` method (before `GetAll()` at line 67). One-line implementation; no try/catch needed (sync `Load` already swallows `JsonException` and falls back to empty `ConnectionsFile`).

**Pattern conventions to preserve:**
- Class is `sealed`, fields are `readonly` where possible — no changes.
- `_data` is a single reference-type field; assignment from `Task.Run` thread is atomic in .NET.
- `Log.Warning` / `Log.Error` use Serilog static logger (already imported at line 6).

---

### `src/Deskbridge.Core/Interfaces/IConnectionStore.cs` (interface, contract)

**Analog:** the file itself — existing method signatures (lines 7-17).

**Existing interface (full file, lines 1-18)**:
```csharp
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

public interface IConnectionStore
{
    IReadOnlyList<ConnectionModel> GetAll();
    ConnectionModel? GetById(Guid id);
    void Save(ConnectionModel connection);
    void Delete(Guid id);
    IReadOnlyList<ConnectionGroup> GetGroups();
    ConnectionGroup? GetGroupById(Guid id);
    void SaveGroup(ConnectionGroup group);
    void DeleteGroup(Guid groupId);
    void DeleteBatch(IEnumerable<Guid> connectionIds, IEnumerable<Guid> groupIds);
    void SaveBatch(IEnumerable<ConnectionModel> connections, IEnumerable<ConnectionGroup> groups);
    void Load();
}
```

**Add a single new line** alongside the existing `void Load();` (line 17) — keep the sync `Load()` for tests/benchmarks (D-06):
```csharp
Task LoadAsync();
```
Requires adding `using System.Threading.Tasks;` to the using block (or the implicit-usings .NET 10 default already covers `Task`; verify by inspecting the project's `<ImplicitUsings>` setting in the csproj — if `enable`, no using needed).

**Pattern conventions to preserve:**
- No XML doc comments on existing interface members — keep consistent (no doc comment on `LoadAsync` either).
- Method order: place `Task LoadAsync();` immediately after `void Load();` so sync/async pair is grouped.

---

### `src/Deskbridge/App.xaml.cs` (startup composition)

**Analog:** the file itself — `OnStartup` body at lines 35-148, DI factory at lines 201-206.

**Current sync DI factory** (lines 201-206) — to be simplified:
```csharp
services.AddSingleton<IConnectionStore>(sp =>
{
    var store = new JsonConnectionStore();
    store.Load();
    return store;
});
```
**Replace with** (load moves out of DI factory per D-04):
```csharp
services.AddSingleton<IConnectionStore>(sp => new JsonConnectionStore());
```

**Current sync `OnStartup` signature** (line 35):
```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
```
**Change to** (`async void` is the documented WPF lifecycle escape hatch):
```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
```

**Insertion site for `await store.LoadAsync()`** — between `BuildServiceProvider()` (line 65) and the existing `MigrateFromTermsrv` block (lines 67-76):
```csharp
var services = new ServiceCollection();
ConfigureServices(services);
_serviceProvider = services.BuildServiceProvider();

// PERF-03 (Phase 21): load connections async on background thread before
// MainWindow is shown. Sync Load() retained on JsonConnectionStore for
// benchmarks and tests; this path is the production startup load.
try
{
    var store = _serviceProvider.GetRequiredService<IConnectionStore>();
    await store.LoadAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Failed to load connections during startup");
}

// Credential Guard fix: migrate legacy TERMSRV/* entries to DESKBRIDGE/CONN/* targets.
// One-time idempotent migration -- skips connections that already have new-format credentials.
// Must run after IConnectionStore.LoadAsync() (which previously happened in the factory lambda above)
// and before any connection attempts.
var credService = _serviceProvider.GetRequiredService<ICredentialService>();
if (credService is WindowsCredentialService winCredService)
{
    var store = _serviceProvider.GetRequiredService<IConnectionStore>();
    winCredService.MigrateFromTermsrv(store);
}
```

**Order invariant to preserve** (RESEARCH.md §5.2):
1. `BuildServiceProvider()` — synchronous (line 65)
2. `await store.LoadAsync()` — NEW (after line 65, before line 67)
3. `MigrateFromTermsrv` — must read populated store (lines 67-76)
4. Pipeline stage wiring (lines 78-89)
5. Eager singleton resolution (lines 91-110)
6. `mainWindow.Show()` — must be last visible step (line 113)

**Pattern conventions to preserve:**
- Existing `CrashHandler.InstallDispatcherHook` at line 41 catches anything outside the new try/catch — do not reorder.
- Existing Serilog logger configured at lines 44-49 BEFORE the new `await` — `Log.Fatal` in catch is safe.
- All other registrations (`MasterPasswordService`, `IdleLockService`, etc.) — untouched.

---

### Test fakes for `IConnectionStore`

**Analog:** `tests/Deskbridge.Tests/ViewModels/ConnectionTreeStateTrackingTests.cs` lines 18-33.

**Pattern in use:** the test suite uses `NSubstitute.Substitute.For<IConnectionStore>()` to auto-stub the interface. NSubstitute returns `default` for any method not explicitly configured, which means **adding `Task LoadAsync()` to the interface produces a stub that returns `null` by default — but `Task` return values default to `Task.CompletedTask` only with `.Returns(Task.CompletedTask)`** explicit setup. Auto-stubbed `Task` returns can throw `NullReferenceException` on `await`.

**Existing fake construction pattern** (verbatim, lines 18-33):
```csharp
public ConnectionTreeStateTrackingTests()
{
    var connectionStore = Substitute.For<IConnectionStore>();
    var connectionQuery = Substitute.For<IConnectionQuery>();
    var credentialService = Substitute.For<ICredentialService>();
    var contentDialogService = Substitute.For<IContentDialogService>();
    var snackbarService = Substitute.For<ISnackbarService>();
    var serviceProvider = Substitute.For<IServiceProvider>();
    _bus = new EventBus();
    var tabHostManager = Substitute.For<ITabHostManager>();

    _sut = new ConnectionTreeViewModel(
        connectionStore, connectionQuery, credentialService,
        contentDialogService, snackbarService, serviceProvider,
        _bus, tabHostManager, new AirspaceSwapper());
}
```

**Action for planner:** `Substitute.For<IConnectionStore>()` does NOT need any new manual fake class. NSubstitute regenerates the proxy automatically when `LoadAsync` is added to the interface. **However**, any test that *awaits* `connectionStore.LoadAsync()` MUST explicitly configure: `connectionStore.LoadAsync().Returns(Task.CompletedTask);` to avoid `NullReferenceException`. No other test paths await `LoadAsync` (the VM tests don't trigger startup load), so this concern is contained to the new `JsonConnectionStoreAsyncLoadTests`.

A search for any **manual** `IConnectionStore` fake class (`class FakeConnectionStore` / `class TestConnectionStore` / `: IConnectionStore`) is recommended during execution; if none found (likely — all tests use NSubstitute), no fake updates are needed beyond the rule above.

---

### `tests/Deskbridge.Tests/Services/JsonConnectionStoreAsyncLoadTests.cs` (NEW, file-I/O xUnit test)

**Analog:** `tests/Deskbridge.Tests/JsonConnectionStoreTests.cs` lines 1-52 (ctor + temp-dir IDisposable + Save_ThenLoad_RoundtripsConnection).

**xUnit test scaffolding shape (verbatim from JsonConnectionStoreTests.cs lines 1-52)**:
```csharp
using System.IO;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests;

public sealed class JsonConnectionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;
    private readonly JsonConnectionStore _store;

    public JsonConnectionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DeskbridgeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "connections.json");
        _store = new JsonConnectionStore(_filePath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Save_ThenLoad_RoundtripsConnection()
    {
        var conn = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "TestServer",
            Hostname = "server01.local",
            Port = 3389,
            Protocol = Protocol.Rdp
        };

        _store.Save(conn);

        // Create a new store instance and load from disk
        var store2 = new JsonConnectionStore(_filePath);
        store2.Load();

        var loaded = store2.GetAll();
        loaded.Should().HaveCount(1);
        loaded[0].Id.Should().Be(conn.Id);
        ...
    }
}
```

**Adapt for async (rename to `JsonConnectionStoreAsyncLoadTests`, place under `tests/Deskbridge.Tests/Services/` to match the SaveBatch/BulkDelete sibling style):**
- Reuse the temp-dir ctor + `IDisposable.Dispose` verbatim.
- Replace `[Fact] public void Save_ThenLoad_...` with `[Fact] public async Task Save_ThenLoadAsync_RoundtripsConnection()`.
- Body: identical setup, but call `await store2.LoadAsync()` instead of `store2.Load()`.
- Add a second `[Fact] public async Task LoadAsync_MissingFile_StartsWithEmptyStore()` that does NOT seed-save first — asserts `_store.GetAll().Should().BeEmpty()` after `await _store.LoadAsync()`.
- Add a third `[Fact] public async Task LoadAsync_RunsOnBackgroundThread()` that captures `Thread.CurrentThread.ManagedThreadId` before `await` and inside the awaited continuation; asserts `Task.Run` dispatched off the calling thread (or use `Environment.CurrentManagedThreadId` comparison; non-test-runner-thread proves background dispatch).

**Pattern conventions to preserve:**
- File header `using` order: System.IO first, then Deskbridge.Core.* — exactly per analog.
- Class is `sealed`, implements `IDisposable` for temp-dir cleanup.
- `xUnit.v3` is the configured framework (per csproj line 13). Use `[Fact]` and `Should()` (FluentAssertions, line 14).
- No `[Trait]` / `[Collection]` attributes — sibling tests don't use them.

---

### `tests/Deskbridge.Tests/ViewModels/ConnectionTreeSearchDebounceTests.cs` (NEW, event-driven xUnit test)

**Analog:** `tests/Deskbridge.Tests/ViewModels/ConnectionTreeStateTrackingTests.cs` lines 1-50 (VM instantiation + bus pattern).

**xUnit test scaffolding shape (verbatim from ConnectionTreeStateTrackingTests.cs lines 1-50)**:
```csharp
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using Deskbridge.Core.Settings;
using Deskbridge.Protocols.Rdp;
using Deskbridge.ViewModels;
using NSubstitute;
using Wpf.Ui;

namespace Deskbridge.Tests.ViewModels;

public class ConnectionTreeStateTrackingTests
{
    private readonly ConnectionTreeViewModel _sut;
    private readonly IEventBus _bus;

    public ConnectionTreeStateTrackingTests()
    {
        var connectionStore = Substitute.For<IConnectionStore>();
        var connectionQuery = Substitute.For<IConnectionQuery>();
        var credentialService = Substitute.For<ICredentialService>();
        var contentDialogService = Substitute.For<IContentDialogService>();
        var snackbarService = Substitute.For<ISnackbarService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        _bus = new EventBus();
        var tabHostManager = Substitute.For<ITabHostManager>();

        _sut = new ConnectionTreeViewModel(
            connectionStore, connectionQuery, credentialService,
            contentDialogService, snackbarService, serviceProvider,
            _bus, tabHostManager, new AirspaceSwapper());
    }

    [Fact]
    public void SelectedConnectionState_IsNull_WhenNothingSelected()
    {
        _sut.SelectedConnectionState.Should().BeNull();
    }
    ...
}
```

**Adapt for debounce tests:**
- Reuse the entire ctor block verbatim (rename class to `ConnectionTreeSearchDebounceTests`).
- Add tests asserting:
  - **Single keystroke fires once after 250ms.** Set `_sut.SearchText = "a"`. Wait < 250ms — assert no filter ran (a counter or `RootItems` shape stays at full tree). Wait additional time so total ≥ 300ms — assert filter ran exactly once.
  - **Five keystrokes within 200ms fire once.** Loop set `SearchText` to "a","ab","abc","abcd","abcde" with no waits between. Wait 300ms. Assert filter ran exactly once.
  - **Clear-search bypasses debounce.** With debounce pending (`SearchText = "abc"`), set `SearchText = ""`. Assert `RootItems` restored synchronously on the same dispatcher tick.
- **DispatcherTimer + xUnit.v3 caveat:** `DispatcherTimer` requires a running `Dispatcher`. Test classes do NOT auto-pump a dispatcher. Two viable approaches the planner can choose:
  1. Run on STA thread + manual `Dispatcher.PushFrame` to pump until timer fires (heavyweight; not used elsewhere in this repo per grep results).
  2. **Inject a debounce abstraction.** Refactor the VM to accept an `IDebouncer` (or similar `Action<TimeSpan, Action>` delegate) with a `DispatcherTimer`-backed default in production and a synchronous test-double in tests. This is cleaner and keeps the test deterministic. The planner should pick this approach unless time pressure forces the dispatcher-pump route.
- Use `Substitute.For<>` for the `IConnectionStore`-passed-to-VM (no need to seed it; tests target SearchText flow, not store flow).

**Pattern conventions to preserve:**
- File header `using` order matches sibling tests verbatim (Deskbridge.Core.* first, then NSubstitute, then Wpf.Ui).
- Test class is **NOT** `sealed`, **NOT** `IDisposable` (matches sibling — the VM is GC'd, no explicit cleanup needed).
- Use `Should()` (FluentAssertions). Use `[Fact]`. Test method names follow `MethodOrEvent_Expected_Condition` shape (matches sibling lines 38, 44, 56, 69, 87).

---

## Shared Patterns

### Phase-tagged comment style on new class members
**Source:** `src/Deskbridge/MainWindow.xaml.cs` lines 64-68 + `src/Deskbridge/App.xaml.cs` lines 67-70 (multiple call sites)
**Apply to:** every new field/method introduced this phase
```csharp
/// <summary>
/// Phase 21 (PERF-XX): <one-line description>.
/// </summary>
```
Or the `// Phase 21 (PERF-XX): ...` single-line form for inline comments. The repo consistently tags new code with the phase number + the requirement ID — every code reviewer searches for these tags during regression triage. `git grep "Phase 21"` should locate all PERF-01/02/03/05 changes.

### DispatcherTimer reset-on-input pattern (5-step shape)
**Source:** `src/Deskbridge/MainWindow.xaml.cs` lines 411-418
**Apply to:** `ConnectionTreeViewModel` search debounce (PERF-02)
```csharp
_timer?.Stop();
_timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(N) };
_timer.Tick -= OnTick;
_timer.Tick += OnTick;
_timer.Start();
```
The `Tick -= ; Tick += ;` defends against duplicate-handler bugs (idempotent: `-=` on non-attached handler is a no-op). The Tick handler stops the timer first, then runs work — mandatory to make the timer one-shot per quiet period.

### Singleton ViewModel + non-disposable timer
**Source:** `src/Deskbridge/App.xaml.cs:423` (`AddSingleton<ConnectionTreeViewModel>`) + `MainWindow.xaml.cs:68`
**Apply to:** debounce timer field on `ConnectionTreeViewModel`
The VM lives for the application's process lifetime; `DispatcherTimer` is not `IDisposable` and is GC-eligible only when the VM is. No `Dispose()` plumbing needed (precedent: `MainWindow._resizeTimer` is never explicitly disposed).

### Async-void OnStartup + try/catch + Log.Fatal
**Source:** `src/Deskbridge/App.xaml.cs:35` (current sync) + RESEARCH.md §5.2 (target async shape)
**Apply to:** `App.OnStartup`
Standard WPF pattern: `protected override async void OnStartup(StartupEventArgs e)`. Wrap each `await` in try/catch and log via `Log.Fatal` (Serilog logger configured at line 49, BEFORE any await). `CrashHandler.InstallDispatcherHook` (line 41) catches the rest of the dispatcher.

### Atomic field assignment for cross-thread reads
**Source:** `src/Deskbridge.Core/Services/JsonConnectionStore.cs:13` (`private ConnectionsFile _data = new();`)
**Apply to:** `LoadAsync` background-thread `_data` write
Single reference-type field assignment is atomic in .NET. `Task.Run(Load)` mutates `_data` on a threadpool thread; the awaiter reads `_data` on the UI thread after the await resumes. No `lock` needed — the await-continuation provides the happens-before edge for the UI thread (RESEARCH.md §6).

### NSubstitute auto-fakes for `IConnectionStore`
**Source:** `tests/Deskbridge.Tests/ViewModels/ConnectionTreeStateTrackingTests.cs:20`
**Apply to:** all test classes that take `IConnectionStore`
`Substitute.For<IConnectionStore>()` regenerates a proxy automatically when interface members change. The only manual update is **for tests that await new async methods** — must add `mock.LoadAsync().Returns(Task.CompletedTask);` to avoid awaiting a `null` Task.

---

## No Analog Found

None — every change in this phase has at least a role-match analog in the existing codebase. This is unsurprising: the phase is a polish/perf phase that explicitly mirrors established patterns (resize debounce → search debounce, sync Load → async Load).

---

## Metadata

**Analog search scope:** `src/Deskbridge/`, `src/Deskbridge.Core/`, `src/Deskbridge.Protocols.Rdp/`, `tests/Deskbridge.Tests/`, `tests/Deskbridge.Benchmarks/`
**Files scanned:** 8 directly read for excerpts; ~12 located via Grep/Glob (test fakes, interface, view-model header, etc.)
**Pattern extraction date:** 2026-04-30

**Notable adjustments from upstream specs:**
- Test files placed under `tests/Deskbridge.Tests/` (not `tests/Deskbridge.Core.Tests/` — that project does not exist in the repo).
- `JsonConnectionStoreAsyncLoadTests.cs` placed under `tests/Deskbridge.Tests/Services/` to match the `SaveBatchTests.cs` / `BulkDeleteTests.cs` sibling layout, even though `JsonConnectionStoreTests.cs` itself sits at the test-project root.

## PATTERN MAPPING COMPLETE
