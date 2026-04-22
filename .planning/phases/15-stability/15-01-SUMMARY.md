---
phase: 15-stability
plan: 01
subsystem: core/tree
tags: [stability, bulk-delete, virtualization, performance]
dependency_graph:
  requires: []
  provides: [DeleteBatch, TreeItemViewModel.Depth, virtualized-treeview]
  affects: [ConnectionTreeControl, ConnectionTreeViewModel, JsonConnectionStore]
tech_stack:
  added: []
  patterns: [batch-persist, viewmodel-depth-over-visual-tree-walk, treeview-virtualization-recycling]
key_files:
  created:
    - tests/Deskbridge.Tests/Services/BulkDeleteTests.cs
    - tests/Deskbridge.Tests/ViewModels/TreeDepthTests.cs
  modified:
    - src/Deskbridge.Core/Interfaces/IConnectionStore.cs
    - src/Deskbridge.Core/Services/JsonConnectionStore.cs
    - src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs
    - src/Deskbridge/ViewModels/TreeItemViewModel.cs
    - src/Deskbridge/Converters/DepthToGuideLinesConverter.cs
    - src/Deskbridge/Converters/TreeViewItemIndentConverter.cs
    - src/Deskbridge/Views/ConnectionTreeControl.xaml
decisions:
  - "Made AssignDepths internal static (not private) to allow direct unit testing via InternalsVisibleTo"
  - "Used int Depth as plain property (not ObservableProperty) since it is set once during BuildTree, not reactive"
  - "Kept existing null guard pattern in TreeViewMultiSelectBehavior (already correct, no changes needed)"
metrics:
  duration: 7m15s
  completed: 2026-04-22T20:20:58Z
  tasks_completed: 3
  tasks_total: 3
  tests_added: 10
  tests_passing: 10
---

# Phase 15 Plan 01: Bulk Delete Crash Fix and TreeView Virtualization Summary

Batch delete with single PersistAtomically call, active-session-aware deletion, and ViewModel-level depth for virtualization-safe TreeView rendering.

## What Changed

### STAB-01: Bulk Delete Crash Fix

**Problem:** Deleting 10+ connections called `PersistAtomically()` N times (one per item), causing I/O exhaustion on slow storage. Active RDP sessions were not closed before their connection data was deleted, leaving zombie tabs.

**Solution:**
1. Added `DeleteBatch(IEnumerable<Guid> connectionIds, IEnumerable<Guid> groupIds)` to `IConnectionStore` and implemented in `JsonConnectionStore` -- groups deleted first (orphaning child connections), then connections removed, single `PersistAtomically()` at the end.
2. Refactored `DeleteSelectedAsync` to close active RDP tabs before deleting data -- iterates selected items, checks `TryGetExistingTab`, calls `CloseTabAsync` for each active session, including recursive descent into group children.
3. Wrapped entire delete block in try/catch to prevent unhandled exceptions from crashing the application.

### STAB-05: TreeView Virtualization

**Problem:** TreeView had `VirtualizingPanel.IsVirtualizing="False"` because two converters (`DepthToGuideLinesConverter`, `TreeViewItemIndentConverter`) walked `VisualTreeHelper.GetParent()` to compute depth. With virtualization, recycled containers return stale parent chains.

**Solution:**
1. Added `int Depth` property to `TreeItemViewModel` base class.
2. Added `AssignDepths(items, depth)` to `ConnectionTreeViewModel`, called after `SortSiblings` during `BuildTree()`.
3. Refactored both converters to accept `int` instead of `TreeViewItem` -- no more visual tree walking.
4. Updated XAML bindings from `{Binding Converter=..., RelativeSource={RelativeSource TemplatedParent}}` to `{Binding DataContext.Depth, RelativeSource={RelativeSource TemplatedParent}, Converter=...}`.
5. Enabled virtualization: `IsVirtualizing="True"`, `VirtualizationMode="Recycling"`, `CanContentScroll="True"`.

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | ad4b1f2 | test(15-01): add failing tests for DeleteBatch bulk delete |
| 2 | ce6c9ae | feat(15-01): implement DeleteBatch for single-persist bulk delete |
| 3 | 3f92ba3 | feat(15-01): refactor DeleteSelectedAsync to use DeleteBatch and close active sessions |
| 4 | c6d9548 | test(15-01): add failing tests for TreeItemViewModel Depth property |
| 5 | 0002636 | feat(15-01): add ViewModel Depth property, refactor converters, enable virtualization |

## Test Results

| Suite | Tests | Status |
|-------|-------|--------|
| BulkDeleteTests | 5 | All passing |
| TreeDepthTests | 5 | All passing |
| Full solution build | - | 0 errors, 0 warnings |

## Deviations from Plan

None -- plan executed exactly as written.

## Decisions Made

1. **AssignDepths as internal static:** Made the method `internal static` instead of `private` so unit tests can call it directly via `InternalsVisibleTo` (already configured in Deskbridge.csproj). This avoids reflection hacks and follows the existing pattern used by `TabHostManager` tests in the codebase.

2. **Depth as plain property:** Used `public int Depth { get; set; }` instead of `[ObservableProperty]` because depth is set once during `BuildTree()` and does not change reactively. The XAML binding fires correctly because `DataContext` changes on container recycling trigger re-evaluation of the binding path.

3. **No changes to TreeViewMultiSelectBehavior:** The existing null guard on `FindContainerForItem` (line 136: `container is not null && container.IsSelected`) was already correct for virtualized containers. No other call sites lacked null guards.

## Self-Check: PASSED

All 10 key files verified present on disk. All 5 commit hashes verified in git log.
