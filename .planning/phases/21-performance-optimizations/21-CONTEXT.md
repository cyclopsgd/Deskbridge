# Phase 21: Performance Optimizations - Context

**Gathered:** 2026-04-28
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
- **D-03:** Use DispatcherTimer pattern (already established in codebase for resize debounce in MainWindow.xaml.cs).

### Startup Loading (PERF-03)
- **D-04:** Post-DI initialization step: register JsonConnectionStore without loading during DI, then call `LoadAsync()` after `BuildServiceProvider()` completes but before MainWindow is shown.
- **D-05:** Load runs on background thread (Task.Run wrapping file I/O), tree populates once data is ready. No visible empty tree state — window shows after load completes.
- **D-06:** Synchronous `Load()` remains for backward compatibility (benchmarks, tests). `LoadAsync()` is a new method, not a replacement.

### Tree Scroll Performance (PERF-01)
- **D-07:** Virtualization is already enabled (VirtualizingPanel.IsVirtualizing + Recycling mode). Validate it handles 500+ without additional optimization.
- **D-08:** Add `VirtualizingPanel.ScrollUnit="Pixel"` proactively for smoother scroll feel (free improvement, especially with trackpad/smooth scroll wheel).
- **D-09:** If benchmarks show stuttering at 500+, simplify row visual complexity as needed — but don't pre-optimize beyond ScrollUnit.

### Group Count Badges (PERF-05)
- **D-10:** Inline parenthetical display — show count as separate TextBlock next to group name: "Production (23)". Standard explorer pattern, minimal visual weight.
- **D-11:** Use `TextFillColorSecondaryBrush` for subdued styling. Positioned between group name TextBlock and key icon in the existing StackPanel.
- **D-12:** Recursive total count (all descendants, not just direct children). The existing `GroupTreeItemViewModel.ConnectionCount` property already computes this recursively.

### Claude's Discretion
- Exact DispatcherTimer tick handler implementation details
- Whether LoadAsync is added as a new method or Load() is refactored to call it
- How to wire async load into App.xaml.cs startup sequence (exact await placement)
- Badge TextBlock margin/font-size details within the 28px row height
- Whether ConnectionCount needs caching or if recursive counting is fast enough at 500+
- Whether to show "(0)" for empty groups or hide the count when zero

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
- `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` — BuildTree(), OnSearchTextChanged() at line 495, tree filtering
- `src/Deskbridge/ViewModels/GroupTreeItemViewModel.cs` — ConnectionCount recursive property at line 20
- `src/Deskbridge.Core/Services/JsonConnectionStore.cs` — Load() at line 42, needs LoadAsync() added
- `src/Deskbridge.Core/Services/ConnectionTreeBuilder.cs` — Pure tree builder (Phase 20)
- `src/Deskbridge/Views/ConnectionTreeControl.xaml` — TreeView virtualization at lines 341-342, group template at lines 354-383
- `src/Deskbridge/App.xaml.cs` — Startup DI registration at lines 201-206 where sync Load() is called
- `src/Deskbridge/MainWindow.xaml.cs` — DispatcherTimer resize debounce pattern at line 414 (reference for search debounce)

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
- `DispatcherTimer` pattern — already used for resize debounce (500ms) in MainWindow.xaml.cs

### Established Patterns
- `[ObservableProperty]` + `partial void OnXChanged()` — used for SearchText change handler
- `DispatcherTimer` with reset-on-input — resize handler resets timer on each SizeChanged event
- Synchronous `JsonConnectionStore(filePath)` constructor + `Load()` — current startup pattern
- `TextFillColorSecondaryBrush` — used throughout for subdued/muted text elements

### Integration Points
- `App.xaml.cs` line 201-206 — where JsonConnectionStore is constructed and Load() called (needs async refactor)
- `ConnectionTreeViewModel.OnSearchTextChanged()` line 495 — where debounce logic wraps existing filter
- `GroupTreeItemViewModel` — ConnectionCount already computed, just needs XAML binding
- `ConnectionTreeControl.xaml` line 356 — group template StackPanel where badge TextBlock is added
- `ConnectionTreeControl.xaml` line 341-342 — where ScrollUnit="Pixel" is added

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
*Context gathered: 2026-04-28*
