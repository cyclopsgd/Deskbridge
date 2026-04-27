# Phase 21: Performance Optimizations - Context

**Gathered:** 2026-04-27
**Status:** Ready for planning

<domain>
## Phase Boundary

Optimize Deskbridge for enterprise-scale connection lists (500+). Four requirements: smooth tree scrolling (PERF-01), debounced search (PERF-02), async startup load (PERF-03), and group connection count badges (PERF-05). All optimizations validated against Phase 20 baselines.

</domain>

<decisions>
## Implementation Decisions

### Search Debounce (PERF-02)
- **D-01:** Standard debounce on SearchText changes — ~250-300ms delay before filtering fires. No "searching..." indicator needed; the debounce is short enough that the result appears naturally.
- **D-02:** Cancel any in-progress filter when new input arrives. Clear search restores full tree immediately (no debounce on clear).

### Startup Loading (PERF-03)
- **D-03:** Load connections.json on a background thread via Task.Run, then marshal results to UI thread. No splash screen or skeleton — just ensure the UI doesn't freeze. The tree populates once loading completes.
- **D-04:** JsonConnectionStore gets an async Load path. The synchronous Load() remains for backward compatibility (benchmarks, tests).

### Tree Scroll Performance (PERF-01)
- **D-05:** Virtualization is already enabled (VirtualizingPanel.IsVirtualizing + Recycling mode). Validate it handles 500+ without additional optimization. If benchmarks show issues, simplify row visual complexity as needed — but don't pre-optimize.

### Group Count Badges (PERF-05)
- **D-06:** Show connection count inline in group name display in the tree — e.g., "Production (23)". Use the existing GroupTreeItemViewModel.ConnectionCount computed property. Style as subdued text next to group name.

### Claude's Discretion
- Exact debounce timer implementation (DispatcherTimer, CancellationTokenSource, or Rx-style)
- Whether to add LoadAsync as a new method or refactor Load() to be async
- How to wire async load into App startup sequence
- Badge text formatting and styling details
- Whether ConnectionCount needs caching or if recursive counting is fast enough at 500+

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Architecture & Patterns
- `docs/REFERENCE.md` — Architecture, DI registrations, constraints
- `docs/WPF-UI-PITFALLS.md` — WPF-UI silent failures (read before any UI changes)
- `docs/WPF-TREEVIEW-PATTERNS.md` — TreeView multi-select, row-level selection visuals

### Phase 20 Artifacts (baseline context)
- `.planning/phases/20-performance-baselines/20-01-SUMMARY.md` — ConnectionTreeBuilder extraction details
- `.planning/phases/20-performance-baselines/20-02-SUMMARY.md` — TestDataGenerator details
- `.planning/phases/20-performance-baselines/20-03-SUMMARY.md` — Benchmark project details

### Key Source Files
- `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` — BuildTree(), OnSearchTextChanged(), tree filtering
- `src/Deskbridge/ViewModels/GroupTreeItemViewModel.cs` — ConnectionCount computed property
- `src/Deskbridge.Core/Services/JsonConnectionStore.cs` — Load() to make async
- `src/Deskbridge.Core/Services/ConnectionTreeBuilder.cs` — Pure tree builder (Phase 20)
- `src/Deskbridge/Views/ConnectionTreeControl.xaml` — TreeView with virtualization settings
- `src/Deskbridge/App.xaml.cs` — Startup sequence where Load() is called

### Benchmarks
- `tests/Deskbridge.Benchmarks/` — Baseline results for regression comparison

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ConnectionTreeBuilder.Build()` — pure static method, already extracted in Phase 20
- `GroupTreeItemViewModel.ConnectionCount` — recursive count property, already exists
- `VirtualizingPanel` settings — already configured on TreeView in ConnectionTreeControl.xaml
- `TestDataGenerator.Generate()` — can produce 500/1000 connection datasets for validation

### Established Patterns
- `[ObservableProperty]` + `partial void OnXChanged()` — used for SearchText change handler
- `DispatcherTimer` — used elsewhere in the app (MainWindow, ToastStack, IdleLockService)
- Synchronous `JsonConnectionStore(filePath)` constructor + `Load()` — current startup pattern

### Integration Points
- `App.xaml.cs` line ~203 — where JsonConnectionStore is constructed and Load() called
- `ConnectionTreeViewModel.OnSearchTextChanged()` — where debounce logic goes
- `GroupTreeItemViewModel` — where badge count is computed
- `ConnectionTreeControl.xaml` — where badge display needs to be added to tree item template

</code_context>

<specifics>
## Specific Ideas

No specific requirements — standard performance optimization approaches. Validate against Phase 20 baselines.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 21-performance-optimizations*
*Context gathered: 2026-04-27*
