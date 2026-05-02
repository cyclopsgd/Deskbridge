# Phase 21: Performance Optimizations — Research

**Date:** 2026-04-30
**Status:** Complete
**Phase requirements:** PERF-01, PERF-02, PERF-03, PERF-05

## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** 250ms `DispatcherTimer` debounce on `SearchText`. No "searching..." indicator.
- **D-02:** Cancel-on-input. Clear-search bypasses debounce — restore tree synchronously.
- **D-03:** Use the established `DispatcherTimer` pattern (resize debounce in `MainWindow.xaml.cs:411`).
- **D-04:** Post-DI init: register `JsonConnectionStore` without loading; call `LoadAsync()` after `BuildServiceProvider()`, before `MainWindow.Show()`.
- **D-05:** Load runs on background thread (`Task.Run` wrapping I/O). No empty-tree state — window appears after load.
- **D-06:** Sync `Load()` retained for benchmarks/tests. `LoadAsync()` is a new method, not a replacement.
- **D-07/08:** Virtualization already enabled. Add `VirtualizingPanel.ScrollUnit="Pixel"` proactively.
- **D-09:** No further visual simplification unless benchmarks show stuttering at 500+.
- **D-10/11:** Inline parenthetical badge `(N)` next to group name, `TextFillColorSecondaryBrush`, between Name and Key icon.
- **D-12:** Recursive count via existing `GroupTreeItemViewModel.ConnectionCount`.

### Claude's Discretion
- Exact `DispatcherTimer` tick/reset wiring inside `ConnectionTreeViewModel`.
- Whether `LoadAsync()` is added as new method or `Load()` is refactored to call it. (Recommend: new method, keep `Load()` synchronous.)
- Exact await placement in `App.xaml.cs` startup sequence.
- Badge margin/font-size details — locked by UI-SPEC; planner copies verbatim.
- Caching `ConnectionCount` — recommend NO; recursive O(n) per group is negligible at 1000.
- Hide-when-zero rule — UI-SPEC locks: hide entirely.

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PERF-01 | Smooth tree scroll via `ScrollUnit="Pixel"` | Section 1 — XAML diff at line 344 |
| PERF-02 | 250ms `DispatcherTimer` debounce on `SearchText` | Section 2 — pattern from `MainWindow.xaml.cs:411` |
| PERF-03 | Async startup load via `LoadAsync()` after DI build | Sections 3, 5 — `protected override async void OnStartup` |
| PERF-05 | Inline `(N)` badge next to group name | Section 4 — UI-SPEC snippet, insertion point line ~379 |

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary | Rationale |
|------------|-------------|-----------|-----------|
| Tree virtualization scroll-unit | View (XAML) | — | Pure rendering attribute; WPF handles all layout |
| Search debounce timer | ViewModel | View (timer fires on UI thread) | Timer state belongs to VM that owns `SearchText`; `DispatcherTimer.Tick` already on UI thread |
| Async JSON load | Core service | App startup | I/O is core concern; orchestration in `App.OnStartup` |
| Group count badge | View (XAML binding) | ViewModel (existing `ConnectionCount`) | Pure binding; VM property already exists |

## 1. PERF-01 — Tree Scroll (`ScrollUnit="Pixel"`)

**Insertion point:** `src/Deskbridge/Views/ConnectionTreeControl.xaml`, inside the `<TreeView x:Name="ConnectionTree" ...>` opening tag (currently lines 339–354). Add one attribute alongside the existing virtualization attributes:

```xml
<TreeView x:Name="ConnectionTree"
          ...
          VirtualizingPanel.IsVirtualizing="True"
          VirtualizingPanel.VirtualizationMode="Recycling"
          VirtualizingPanel.ScrollUnit="Pixel"   <!-- NEW (PERF-01) -->
          ScrollViewer.CanContentScroll="True"
          ...>
```

**Behavior change:**
- Scroll wheel and trackpad gestures advance by **pixel** rather than per **item**. Smoother feel, especially for trackpad/precision-touchpad gestures.
- `BringIntoView`, focus visuals, hit-testing, item containers, recycling — all unaffected.
- Scrollbar thumb size remains proportional to virtual content; no visual chrome change.
- `FullRowTreeViewItemStyle` is unaffected (it operates on item containers, not the host panel scroll math).

**Regression risk:** LOW. `ScrollUnit="Pixel"` is a documented WPF attribute that toggles `VirtualizingStackPanel`'s scroll math between item-based and pixel-based. No layout side effects. [VERIFIED via Microsoft docs — `VirtualizingPanel.ScrollUnit` enum on `VirtualizingStackPanel`].

**Validation:** Phase 20 `TreeBuildBenchmarks` BenchmarkDotNet baseline must show no measurable regression at N=500/1000. (BuildTree timing is independent of scroll-unit, but the harness exercises the same code path.) Manual smoke: scroll wheel feel, scrollbar drag responsiveness.

## 2. PERF-02 — Search Debounce (250ms `DispatcherTimer`)

**Reference pattern (`MainWindow.xaml.cs:407–418`):**
```csharp
private DispatcherTimer? _resizeTimer;

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

**Adapted pattern for `ConnectionTreeViewModel`:**

The debounce field lives on the **ViewModel**, not the view. `DispatcherTimer` is fine in a VM — it captures the calling-thread Dispatcher (the UI thread, since `OnSearchTextChanged` is invoked from a binding update). It does NOT couple the VM to a specific window or visual.

```csharp
// In ConnectionTreeViewModel (near line 495 where OnSearchTextChanged lives)
private DispatcherTimer? _searchDebounceTimer;
private const int SearchDebounceMs = 250;

partial void OnSearchTextChanged(string value)
{
    // Clear-search bypasses debounce (D-02)
    if (string.IsNullOrWhiteSpace(value))
    {
        _searchDebounceTimer?.Stop();
        ApplySearchFilter(value);
        return;
    }

    // Reset-on-input debounce
    _searchDebounceTimer ??= new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(SearchDebounceMs)
    };
    _searchDebounceTimer.Stop();
    _searchDebounceTimer.Tick -= OnSearchDebounceTick;
    _searchDebounceTimer.Tick += OnSearchDebounceTick;
    _searchDebounceTimer.Start();
}

private void OnSearchDebounceTick(object? sender, EventArgs e)
{
    _searchDebounceTimer?.Stop();
    ApplySearchFilter(SearchText);
}

// Existing OnSearchTextChanged body extracted into ApplySearchFilter:
private void ApplySearchFilter(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        RootItems = new ObservableCollection<TreeItemViewModel>(_fullTree);
        return;
    }
    var searchLower = value.ToLowerInvariant();
    var flatMatches = new ObservableCollection<TreeItemViewModel>();
    FlattenAndFilter(_fullTree, searchLower, flatMatches);
    RootItems = flatMatches;
}
```

**Key points:**
- `Stop()` then `Start()` — the documented way to reset a `DispatcherTimer`. Setting `Interval` does NOT reset the countdown.
- `Tick -= ; Tick += ;` mirrors the resize pattern — defends against duplicate handler attachment if someone constructs the timer eagerly later. Safe even on first wire-up (`-=` on a non-attached handler is a no-op).
- Tick handler **stops the timer first**, then runs the filter. This makes the timer a true one-shot per quiet-period.
- Clear path runs `ApplySearchFilter` synchronously with no `Start()` — instant restore as required by D-02 / UI-SPEC.

**Disposal:** `ConnectionTreeViewModel` does not currently implement `IDisposable`. `DispatcherTimer` does not implement `IDisposable` either; when stopped, it deregisters from the dispatcher's timer list and is GC-eligible along with its owning VM. **No cleanup required.** If the VM lives for the entire app lifetime (it does — it's a ViewModel for the persistent ConnectionTreeControl), the timer's lifecycle simply matches the app's. [VERIFIED: `DispatcherTimer` is not `IDisposable` per .NET API surface; lifetime tied to dispatcher's internal weak-reference timer queue when stopped.]

**Edge case (UI thread affinity):** `OnSearchTextChanged` is invoked on the dispatcher thread because the binding source is a TextBox `UpdateSourceTrigger=PropertyChanged`. `DispatcherTimer` constructed without an explicit `Dispatcher` argument captures `Dispatcher.CurrentDispatcher`. Both happen on UI thread. Safe.

## 3. PERF-03 — Async Startup Load

**Strategy: add `LoadAsync()` to `JsonConnectionStore`, refactor App startup, retain sync `Load()`.**

### 3.1 `JsonConnectionStore.LoadAsync()` (Deskbridge.Core)

Two viable implementations:

**Option A (recommended): `Task.Run` wrapping existing sync `Load()`.** Simplest, lowest-risk change. `Load()` does ~1 file read + JSON parse, both CPU-bound work. Wrapping in `Task.Run` moves it to the threadpool; the JSON parse is what takes meaningful time at 1000 connections (Phase 20 `Load` benchmark already captured baseline).

```csharp
public Task LoadAsync() => Task.Run(Load);
```

**Option B: native async I/O.** Use `File.ReadAllTextAsync` + `JsonSerializer.DeserializeAsync` from a `FileStream`. More idiomatic but adds two API surface changes for marginal benefit (the deserialize step dominates, and it's CPU-bound regardless).

```csharp
public async Task LoadAsync()
{
    if (!File.Exists(_filePath)) { _data = new ConnectionsFile(); return; }
    try
    {
        await using var fs = File.OpenRead(_filePath);
        _data = await JsonSerializer.DeserializeAsync<ConnectionsFile>(fs, _jsonOptions)
                ?? new ConnectionsFile();
        if (_data.Version > 1)
            Log.Warning("connections.json version {Version} is newer than supported (1)", _data.Version);
    }
    catch (JsonException ex)
    {
        Log.Error(ex, "Failed to load connections.json, starting with empty collection");
        _data = new ConnectionsFile();
    }
}
```

**Recommendation: Option A.** Smaller diff, no behavior duplication, equivalent threadpool dispatch behavior. Phase 20 baselines confirm `Load` is fast even at 1000 connections — the win is "off UI thread", not raw throughput.

**Add to `IConnectionStore` interface:** `Task LoadAsync();` — and implement in any test fakes.

### 3.2 Sync `Load()` retained

D-06 explicitly retains `Load()` for `StoreBenchmarks` in `tests/Deskbridge.Benchmarks/` and for any unit tests that prefer synchronous setup. No deprecation warning, no `[Obsolete]`. Both methods coexist.

## 4. PERF-05 — Group Count Badge

### 4.1 XAML insertion (verbatim from UI-SPEC)

**Location:** `src/Deskbridge/Views/ConnectionTreeControl.xaml`, inside the `<HierarchicalDataTemplate DataType="{x:Type vm:GroupTreeItemViewModel}">` `StackPanel`. Insert **between** the Name `TextBlock` (line 375) and the Key `ui:SymbolIcon` (line 380).

```xml
<TextBlock Text="{Binding Name}"
           FontWeight="SemiBold" FontSize="14"
           Foreground="{DynamicResource TextFillColorPrimaryBrush}"
           VerticalAlignment="Center" />

<!-- PERF-05: recursive connection count badge -->
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

<ui:SymbolIcon Symbol="Key24" FontSize="12"
               Foreground="{DynamicResource TextFillColorSecondaryBrush}"
               Margin="4,0,0,0"
               VerticalAlignment="Center"
               Visibility="{Binding HasCredentials, Converter={StaticResource BoolToVisibility}}" />
```

Final left-to-right order in the group row: `[FolderIcon] [Name] [Badge] [KeyIcon]`. All `VerticalAlignment="Center"` inside the locked 28px row. 4px gap rhythm preserved.

### 4.2 `ConnectionCount` INPC behavior — important caveat

`GroupTreeItemViewModel.ConnectionCount` is a **plain computed getter** (line 20 of `GroupTreeItemViewModel.cs`):

```csharp
public int ConnectionCount => CountConnections(Children);
```

It is **not** a `[ObservableProperty]` and does not raise `PropertyChanged`. The binding evaluates once when the row is realized, then **does not update on its own** when `Children` mutates.

**Why this is acceptable for this phase:**
- `ConnectionTreeViewModel.BuildTree()` rebuilds `RootItems` (a fresh `ObservableCollection<TreeItemViewModel>`) on every add/remove/import. This causes WPF to throw away the existing item containers and re-realize them, re-running the binding. The badge updates correctly on every tree mutation that goes through the established path.
- Drag-drop / re-parenting also flows through `BuildTree`, so the same applies.

**Risk:** if a future code path mutates `Children` in place without rebuilding the tree, the badge will go stale. **Mitigation:** document this constraint in the plan; do not introduce `[ObservableProperty]` here (would require subscribing to `Children.CollectionChanged` and recursive child events — out of scope for this phase). UI-SPEC's "do not cache `ConnectionCount`" rule and "tree rebuild absorbs the update" pattern is the intended design.

**No converter needed.** The inline `DataTrigger` handles hide-when-zero per UI-SPEC.

## 5. App.xaml.cs Wiring Sketch

### 5.1 DI registration change (line 200–206)

**Current:**
```csharp
services.AddSingleton<IConnectionStore>(sp =>
{
    var store = new JsonConnectionStore();
    store.Load();              // <-- sync I/O on UI thread during BuildServiceProvider
    return store;
});
```

**New (no Load in factory):**
```csharp
services.AddSingleton<IConnectionStore>(sp => new JsonConnectionStore());
```

The factory just constructs. Loading is moved out of DI graph construction.

### 5.2 `OnStartup` becomes `async void`

Currently `protected override void OnStartup(StartupEventArgs e)` (line 35). Change to `async void` — this is the WPF-standard escape hatch for awaiting in framework lifecycle entry points. Wrap the new await in try/catch so any startup failure is logged via Serilog before propagating (Serilog logger is configured at line 49, before the await would happen).

**Diff at line 65 (just after `BuildServiceProvider()`):**

```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    // ... existing CrashHandler + Serilog setup + theme apply (lines 41–61) ...

    var services = new ServiceCollection();
    ConfigureServices(services);
    _serviceProvider = services.BuildServiceProvider();

    // PERF-03: load connections asynchronously before showing MainWindow.
    // Background thread for file I/O + JSON parse; resumes on UI thread for any
    // post-load work (the `Load`/`LoadAsync` method mutates _data, which is then
    // read by other services — fine because all subsequent reads happen on UI thread).
    try
    {
        var store = _serviceProvider.GetRequiredService<IConnectionStore>();
        await store.LoadAsync();
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to load connections during startup");
        // Existing connections-load failure path inside Load() already logs and falls
        // back to empty store; this catch is for unexpected outer failures only.
    }

    // Existing post-load steps (Credential Guard migration, pipeline wiring) follow here
    // — they previously ran AFTER the sync Load() inside the factory; now they run AFTER
    // the awaited LoadAsync(). Order preserved.
    var credService = _serviceProvider.GetRequiredService<ICredentialService>();
    if (credService is WindowsCredentialService winCredService)
    {
        var store = _serviceProvider.GetRequiredService<IConnectionStore>();
        winCredService.MigrateFromTermsrv(store);
    }
    // ... rest of OnStartup unchanged (pipeline wiring, MainWindow construction + Show) ...
}
```

**Order invariant:** `LoadAsync()` MUST complete before `MigrateFromTermsrv` is called (the migrator iterates `store.GetAll()`), and before `MainWindow` is constructed (its DataContext / ConnectionTreeViewModel reads from the store). The `await` on line after `BuildServiceProvider()` enforces this.

**Async-void caveat:** Any exception inside `OnStartup` after the first `await` propagates to the synchronization context, not to a calling caller. The try/catch around `LoadAsync()` covers the new code path. The existing `CrashHandler.InstallDispatcherHook` (line 41) catches dispatcher-level unhandled exceptions for the rest of the method. This is the documented WPF pattern for async startup.

## 6. Threading & Disposal

| Concern | Resolution |
|---------|------------|
| `DispatcherTimer.Tick` thread | UI thread (always). Filter mutates `RootItems` (`ObservableCollection`) safely on UI thread. |
| `Task.Run(Load)` — store mutation thread | Background thread sets `_data` field. Single field assignment is atomic for reference types in .NET. |
| Reading `_data` after `await` | Continuation resumes on UI thread (default `SynchronizationContext`). All consumers (`GetAll`, `GetById`, …) read on UI thread. No race. |
| Concurrent `Load`+`Save` during startup | Not possible — `Save` is invoked from UI commands which only fire after `MainWindow.Show()`, which only happens after `await store.LoadAsync()` completes. |
| `DispatcherTimer` GC | Stopped timer is unregistered from dispatcher queue; GC-eligible with VM. Not `IDisposable`. No leak. |
| `ConnectionTreeViewModel` lifetime | App-lifetime singleton (per existing DI). Timer lifetime matches. |

## 7. Risk Register

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ScrollUnit="Pixel"` regression | LOW | Pure rendering attribute; Phase 20 BuildTree benchmark guards against any unexpected layout cost |
| Async startup unhandled exception | MEDIUM | try/catch around `await store.LoadAsync()` with Serilog `Log.Fatal`; existing `CrashHandler.InstallDispatcherHook` catches anything outside the catch |
| Search debounce reset bug | LOW | Pattern is identical to proven resize-debounce in `MainWindow.xaml.cs:411` |
| Badge stale on Children mutation without rebuild | LOW | All current mutation paths flow through `BuildTree`; document constraint in plan |
| Order-of-operations regression in OnStartup | MEDIUM | `MigrateFromTermsrv` and pipeline wiring must remain AFTER `await store.LoadAsync()`; planner's task list must show this explicitly |
| `IConnectionStore` interface breaks consumers | LOW | New `LoadAsync()` method addition; existing implementations (test fakes) need a one-liner stub |

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit (existing test projects: `tests/Deskbridge.Core.Tests`, `tests/Deskbridge.Tests`) + BenchmarkDotNet 0.15.8 (`tests/Deskbridge.Benchmarks`) |
| Config file | per-project `*.csproj`; benchmarks via `BenchmarkConfig.cs` |
| Quick run command | `dotnet test --filter "FullyQualifiedName~ConnectionTreeViewModel"` |
| Full suite command | `dotnet test` (all 656 tests) + `dotnet run --project tests/Deskbridge.Benchmarks -c Release -- --filter "*TreeBuild*"` for perf gate |
| Phase 20 baselines | `tests/Deskbridge.Benchmarks/baseline/*.json` |

### Functional Dimensions
- **D1: Group count badge**
  - Renders `(N)` next to non-empty groups in the configured 12pt secondary-brush style.
  - **Hidden** when `ConnectionCount == 0` (no `(0)` artifact).
  - Badge value matches recursive descendant count (verify on nested groups).
- **D2: Search debounce**
  - Single-keystroke triggers exactly one filter execution 250ms later.
  - Five keystrokes within 200ms produce exactly **one** filter execution (trailing).
  - Clearing search (text becomes empty/whitespace) restores tree synchronously, no 250ms wait.
- **D3: Async startup load**
  - `MainWindow` does not appear until `LoadAsync()` resolves.
  - No empty-tree flash.
  - Connection count visible at first paint matches JSON file content.
  - Cold-start wall-time within 50ms of pre-change sync startup at N=500.
- **D4: ScrollUnit=Pixel**
  - Mouse-wheel scroll advances by pixel rather than per-row (manual smoke).
  - No layout regression — group expand/collapse, multi-select, drag-drop, context menu still function.

### Performance Dimensions
- **P1:** `TreeBuildBenchmarks.BuildTree` p95 within ±5% of Phase 20 baseline at N=100, 200, 500, 1000.
- **P2:** Async startup wall-time at N=500 ≤ existing sync startup wall-time + 50ms (acceptable threadpool dispatch overhead). Measured via `Stopwatch` in `App.OnStartup` around the `await`.
- **P3:** Search debounce produces exactly one `ApplySearchFilter` call per 250ms quiet period (enforced via unit test counter).

### Sampling Plan (Nyquist)
- **BenchmarkDotNet (P1):** ≥10 iterations per N already configured in `BenchmarkConfig.cs` with `--job short`. Re-run after each PERF change; compare via `--baseline`.
- **Async startup (P2):** 3 manual cold-start measurements at N=500, instrument with `Stopwatch` logging in `OnStartup` (Serilog Information).
- **Search debounce (P3):** unit test in `Deskbridge.Tests` simulating 5 `SearchText` setter calls within 200ms then awaiting 300ms — assert internal filter call counter == 1. Add `internal` counter or use `IObservable`-style hook for test access.
- **Functional manual smoke:** N=500 dataset via `TestDataGenerator`; load, verify badge counts at root + nested groups, type+clear in search, scroll wheel feel.

### Test Data
- `TestDataGenerator.Generate(100|200|500|1000)` from Phase 20 (deterministic seed, already in solution).

### Pass/Fail Gates
- **Performance:** all P1/P2/P3 thresholds met; otherwise BLOCK and revisit.
- **Functional:** all D1–D4 verified manually + via tests; otherwise BLOCK.
- **Regression:** existing 656 tests must remain green; `dotnet build` zero warnings.

### Wave 0 Gaps
- [ ] Add `Task LoadAsync()` to `IConnectionStore` interface — any existing test fake (`tests/Deskbridge.Core.Tests/Fakes/`) needs a stub returning `Task.CompletedTask`.
- [ ] New unit test file `tests/Deskbridge.Tests/ViewModels/ConnectionTreeViewModelDebounceTests.cs` for D2 / P3 coverage.
- [ ] Optional: `Stopwatch` instrumentation hook in `App.OnStartup` for P2 measurement (can be removed after gate is verified).

## Threat Model Notes

No new attack surface.

- **Async file I/O trust boundary** is identical to sync — same path, same parser, same fallback to empty store on `JsonException`. No user-supplied file path crosses any new boundary.
- **DispatcherTimer** does not introduce any external input or resource handle.
- **Group count binding** reads VM-internal computed property; no new data egress, no new attacker-influenced format string (`StringFormat='({0})'` consumes a managed `int`, not user text).
- **Async-void OnStartup** does not change the unhandled-exception story in any way Serilog/CrashHandler does not already cover.

ASVS categories: V5 (Input Validation) and V6 (Cryptography) — neither is touched by this phase. Existing JSON parse already validates at structural level; no new untrusted inputs introduced.

## Sources

### Primary (HIGH confidence)
- `src/Deskbridge/MainWindow.xaml.cs:407–418` — established `DispatcherTimer` debounce pattern.
- `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs:495–509` — current sync `OnSearchTextChanged` body to wrap in debounce.
- `src/Deskbridge.Core/Services/JsonConnectionStore.cs:42–65` — `Load()` to wrap in `LoadAsync()`.
- `src/Deskbridge/App.xaml.cs:35,63–76,200–206` — current sync startup flow.
- `src/Deskbridge/ViewModels/GroupTreeItemViewModel.cs:20` — existing recursive `ConnectionCount`.
- `src/Deskbridge/Views/ConnectionTreeControl.xaml:339–386` — TreeView attributes + group `HierarchicalDataTemplate` insertion site.
- `.planning/phases/21-performance-optimizations/21-UI-SPEC.md` — visual + interaction-timing contract (LOCKED).
- `.planning/phases/21-performance-optimizations/21-CONTEXT.md` — decisions D-01 through D-12 (LOCKED).
- `.planning/phases/20-performance-baselines/20-03-SUMMARY.md` — BenchmarkDotNet baseline harness reference.

### Secondary (MEDIUM confidence)
- WPF `VirtualizingPanel.ScrollUnit` enum — Microsoft Learn `System.Windows.Controls.ScrollUnit` documentation.
- `DispatcherTimer` lifecycle — Microsoft Learn `System.Windows.Threading.DispatcherTimer` (not `IDisposable`, weak-reference timer queue).

### Confidence Breakdown
| Area | Level | Reason |
|------|-------|--------|
| PERF-01 XAML | HIGH | Single-attribute change, documented WPF behavior |
| PERF-02 pattern | HIGH | Mirrors proven in-repo pattern |
| PERF-03 wiring | MEDIUM | `async void OnStartup` is standard but exception path needs careful planning |
| PERF-05 binding | HIGH | XAML locked by UI-SPEC; computed property already exists |
| Validation gates | HIGH | Phase 20 baselines + existing test infrastructure cover all dimensions |

## RESEARCH COMPLETE
