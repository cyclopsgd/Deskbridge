# Phase 15: Stability - Research

**Researched:** 2026-04-19
**Domain:** WPF performance, COM/ActiveX lifecycle, TreeView virtualization, bulk data operations
**Confidence:** HIGH

## Summary

Phase 15 addresses three distinct stability issues: a crash during bulk connection deletion (STAB-01), a black flash during rapid tab switching (STAB-02), and TreeView performance degradation at scale (STAB-05). Each issue has different root causes and requires different mitigation strategies.

The bulk delete crash (STAB-01) stems from repeated file I/O during iteration (each `Delete` and `DeleteGroup` call triggers `PersistAtomically` which serializes the entire connections.json file), combined with potential nested group orphaning that leaves credential store entries behind. The tab switch black flash (STAB-02) is an inherent WPF airspace limitation -- toggling `WindowsFormsHost.Visibility` between `Collapsed` and `Visible` causes the Win32 HWND to be recreated or repainted, producing a single-frame black flash visible during rapid switching. The TreeView virtualization issue (STAB-05) was intentionally disabled because two converters (`DepthToGuideLinesConverter` and `TreeViewItemIndentConverter`) walk the visual tree to compute depth, and the `FindContainerForItem` helper in `TreeViewMultiSelectBehavior` enumerates containers that may not exist when virtualized.

**Primary recommendation:** Batch the delete operations with a single persist at the end; add a brief dispatcher-priority delay between tab visibility flips to allow the Win32 compositor to settle; refactor the depth converters to use ViewModel-level depth instead of visual tree walking before enabling virtualization.

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| STAB-01 | User can delete multiple connections at once without the application crashing | Bulk delete analysis: repeated PersistAtomically, nested group cascade, credential cleanup identified |
| STAB-02 | User sees no black flash when switching between tabs rapidly | Airspace analysis: WFH Visibility toggle, Win32 HWND repaint timing, dispatcher coalescing strategies |
| STAB-05 | User experiences smooth scrolling and expand/collapse in the connection tree with 100+ connections | Virtualization analysis: depth converters, FindContainerForItem, VirtualizationMode.Recycling requirements |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- **COM/ActiveX**: Classic aximp.exe interop only -- no GeneratedComInterface, no Marshal.ReleaseComObject, site before configure
- **Airspace**: No WPF elements may overlap the RDP viewport (WinForms/ActiveX always renders on top)
- **Serialisation**: System.Text.Json only -- no XML config, no SQLite
- **Sessions**: Practical limit ~15-20 simultaneous RDP sessions (GDI handles)
- **UI Library**: WPF-UI (Fluent dark theme) -- all colours via DynamicResource tokens, BasedOn for style overrides
- **Credentials**: AdysTech.CredentialManager only

## Architecture Patterns

### STAB-01: Bulk Delete Crash Analysis

**Current code path** (`ConnectionTreeViewModel.DeleteSelectedAsync`, line 860):

1. Snapshots `SelectedItems.ToList()` -- correct, prevents collection-modified-during-iteration
2. Shows confirmation dialog with AirspaceSwapper -- correct
3. Iterates `itemsToDelete`, for each:
   - Connection: calls `_credentialService.DeleteForConnection(model)` then `_connectionStore.Delete(connItem.Id)`
   - Group: calls `_credentialService.DeleteForGroup(groupItem.Id)` then `_connectionStore.DeleteGroup(groupItem.Id)`
4. Clears selection, calls `RefreshTree()`

**Identified crash vectors:**

**Vector 1 -- Repeated file I/O per item (performance, potential IOException):**
Each `_connectionStore.Delete()` and `_connectionStore.DeleteGroup()` calls `PersistAtomically()` which does:
- `JsonSerializer.Serialize(_data, _jsonOptions)` -- serializes entire file
- `File.WriteAllText(tmpPath, json)` -- writes to .tmp
- `File.Move(tmpPath, _filePath, overwrite: true)` -- atomic rename

For 10 items, this produces 10 serialize + 10 file writes + 10 file moves. On slow storage or when antivirus scans the .tmp file, `File.Move` can throw `IOException` (file in use). No exception handling wraps the delete loop. [VERIFIED: codebase `JsonConnectionStore.cs` lines 88-140]

**Vector 2 -- Nested group children not deleted recursively:**
`DeleteGroup(Guid groupId)` only removes the specified group and orphans its direct child connections (sets `GroupId = null`). It does NOT:
- Delete nested sub-groups (a group inside the deleted group)
- Delete credentials for orphaned connections
- Recurse into child groups

If the user selects a parent group and its child group in the same multi-select, the parent deletes first, orphaning the child group's connections. Then when the child group deletes, its `GroupId` match finds nothing (already orphaned). This is actually safe but leaves orphaned sub-groups if only the parent was selected. [VERIFIED: codebase `JsonConnectionStore.cs` lines 114-125]

**Vector 3 -- CredentialManager exceptions during bulk delete:**
`DeleteForConnection` catches all exceptions (swallows). `DeleteForGroup` also catches all exceptions (swallows). These are safe. However, `_connectionStore.GetById(connItem.Id)` returns the model from the in-memory list. If a group was deleted earlier in the loop and its `DeleteGroup` orphaned the connection (set `GroupId = null` but didn't remove it), the `GetById` still finds the connection. This path is safe. [VERIFIED: codebase `WindowsCredentialService.cs` lines 55-66, 99-110]

**Vector 4 -- Active tab connections deleted without disconnect:**
The delete loop does NOT check whether a connection has an active RDP session open. Deleting the connection data while a tab is still connected would leave a zombie tab with no backing model. When that tab later tries to reference its connection ID, it may crash. [ASSUMED -- needs verification against TabHostManager behavior]

**Recommended fix strategy:**
1. Wrap the entire delete loop in a try/catch
2. Batch all deletions before calling PersistAtomically once (add a `DeleteBatch` method to IConnectionStore)
3. Before deleting, check if any items have active sessions and close them first (or warn the user)
4. Handle nested group deletion recursively -- collect all descendant groups and connections before deleting

### STAB-02: Tab Switch Black Screen Analysis

**Current code path** (`MainWindow.SetActiveHostVisibility`, line 448):

```csharp
private void SetActiveHostVisibility(Guid activeId)
{
    foreach (var child in HostContainer.Children)
    {
        if (child is WindowsFormsHost wfh)
        {
            var isActive = wfh.Tag is Guid id && id == activeId;
            wfh.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            wfh.IsEnabled = isActive;
        }
    }
}
```

**Root cause of black flash:**

The Win32 window manager needs to repaint the child HWND when its parent container changes size or visibility. When `Visibility` goes from `Collapsed` to `Visible`, WPF must:
1. Add the WFH back to the layout tree (Collapsed removes from layout)
2. Call `MeasureOverride` and `ArrangeOverride` on the WFH
3. The WFH creates/shows the Win32 child HWND
4. The child HWND (AxMsRdpClient) receives `WM_PAINT`
5. The RDP ActiveX control repaints its last frame

Steps 1-4 happen within a single WPF layout pass, but step 5 is asynchronous -- the Win32 child HWND needs a separate paint cycle to display its content. Between steps 4 and 5, the HWND is visible but has not yet repainted, showing either black (default window background) or the previous content. [CITED: https://github.com/dotnet/wpf/issues/5892, https://learn.microsoft.com/en-us/archive/msdn-technet-forums/957d58bb-95f4-4bf0-9f87-aef55b564af8]

**Why rapid switching makes it worse:**
Each tab switch publishes `TabSwitchedEvent` -> `Dispatcher.Invoke(() => SetActiveHostVisibility(evt.ActiveId))`. With rapid clicks, multiple `Dispatcher.Invoke` calls queue up. Each one triggers a full layout pass + Win32 repaint cycle. The WPF dispatcher processes them sequentially but the Win32 repaints lag behind, causing visible black frames. [VERIFIED: codebase `MainWindow.xaml.cs` lines 433-436]

**Mitigation strategies (ranked by effectiveness):**

1. **Bitmap snapshot during switch (most reliable, highest complexity):** Before hiding the active WFH, capture its current frame via `PrintWindow` (AirspaceSwapper already has `CaptureHwnd`). Show the snapshot `Image` overlay. Set new WFH to `Visible`. Wait for a dispatcher callback at `Render` priority. Then hide the snapshot. This eliminates the black frame entirely because the snapshot covers the gap. The AirspaceSwapper infrastructure already supports this pattern for dialog overlay -- extend it for tab switching.

2. **Dispatcher priority coalescing (moderate reliability):** Instead of `Dispatcher.Invoke`, use `Dispatcher.InvokeAsync` at `DispatcherPriority.Input` and cancel any pending switch when a new one arrives. This coalesces rapid clicks into a single visibility flip, reducing the number of black frames.

3. **Delay-then-hide (simple, partially effective):** After making the new WFH Visible, delay hiding the old WFH by one or two dispatcher frames (`DispatcherPriority.Loaded` or `DispatcherPriority.Render`). The old WFH stays visible briefly alongside the new one, but since they occupy the same space, the user sees the new one paint over the old one before the old one disappears. The risk is two HWNDs fighting for the same pixel space (z-order battle), but since only the new one has IsEnabled=true, input goes to the right place.

### STAB-05: TreeView Virtualization Analysis

**Current state:**
`VirtualizingPanel.IsVirtualizing="False"` is explicitly set on the TreeView in `ConnectionTreeControl.xaml` (line 341). This means ALL TreeViewItem containers are created upfront for every item in the tree, regardless of visibility.

**Why virtualization was disabled:**

Three code patterns break with virtualization enabled:

**Pattern 1 -- Depth converters walk the visual tree:**
Both `DepthToGuideLinesConverter` and `TreeViewItemIndentConverter` receive the `TreeViewItem` via `{Binding RelativeSource={RelativeSource TemplatedParent}}` and walk `VisualTreeHelper.GetParent()` upward to count ancestor `TreeViewItem` nodes. With `VirtualizationMode.Recycling`, when a container is recycled from depth 3 to depth 1, the visual tree parent chain changes. However, the binding source is `{RelativeSource TemplatedParent}` (the TreeViewItem itself) -- this binding resolves once when the template is applied and does NOT re-evaluate when the container is recycled to a different position. The converter returns stale depth values. [VERIFIED: codebase `DepthToGuideLinesConverter.cs`, `TreeViewItemIndentConverter.cs`]

**Fix:** Replace visual-tree-walking converters with a ViewModel-level `Depth` property on `TreeItemViewModel`. Compute depth during `BuildTree()` from the group nesting hierarchy. Bind the indent margin and guide lines to this property. This makes the binding dependent on `DataContext` changes (which DO fire on recycling) rather than template application.

**Pattern 2 -- FindContainerForItem in multi-select behavior:**
`TreeViewMultiSelectBehavior.FindContainerForItem` (line 276) calls `ItemContainerGenerator.ContainerFromItem(item)` recursively. With virtualization, off-screen items return `null`. However, this method is ONLY used in the `Dispatcher.InvokeAsync` callback that suppresses the native TreeView highlight (line 131). If `ContainerFromItem` returns null, the native highlight just won't get cleared for that item -- a minor visual glitch, not a crash. [VERIFIED: codebase `TreeViewMultiSelectBehavior.cs` lines 276-289]

**Fix:** Add a null guard in the dispatcher callback. If `FindContainerForItem` returns null, skip the native highlight suppression. The ViewModel-level `IsSelected` property already drives the actual visual selection state through DataTriggers, so this is purely cosmetic cleanup.

**Pattern 3 -- GetFlatVisibleItems walks the model tree (SAFE):**
`TreeViewMultiSelectBehavior.GetFlatVisibleItems` (line 235) walks `viewModel.RootItems` recursively. This operates on the ViewModel model tree, NOT the visual tree. It is completely unaffected by virtualization because it never touches containers or the visual tree. [VERIFIED: codebase `TreeViewMultiSelectBehavior.cs` lines 235-249]

**Required XAML changes:**
```xml
<TreeView x:Name="ConnectionTree"
          VirtualizingPanel.IsVirtualizing="True"
          VirtualizingStackPanel.VirtualizationMode="Recycling"
          ScrollViewer.CanContentScroll="True"
          ... />
```

`ScrollViewer.CanContentScroll="True"` is critical -- without it, WPF uses pixel-based scrolling which defeats virtualization. The default for TreeView is `True`, but it may have been overridden. [CITED: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-improve-the-performance-of-a-treeview]

**Known WPF TreeView virtualization bugs:**
- dotnet/wpf#11331: App hangs when any item in ItemsSource is null with virtualization + IsExpanded. Deskbridge does not have null items in the tree, so this is not a risk. [CITED: https://github.com/dotnet/wpf/issues/11331]
- dotnet/wpf#1962: TreeView scroll freeze. Relates to very deep trees (>20 levels) and mouse wheel scrolling. Deskbridge trees are typically 2-4 levels deep. Low risk. [CITED: https://github.com/dotnet/wpf/issues/1962]
- dotnet/wpf#7321: TreeView virtualization broken under .NET 7. Fixed in .NET 8+. Not a risk on .NET 10. [CITED: https://github.com/dotnet/wpf/issues/7321]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Bitmap capture of Win32 HWND | Custom GDI capture | Existing `AirspaceSwapper.CaptureHwnd` | Already implemented, handles GDI resource cleanup, tested in production |
| Debounced dispatcher invocation | Manual timer + CancellationToken | `Dispatcher.InvokeAsync` with `DispatcherPriority` | WPF dispatcher already has priority-based coalescing built in |
| TreeView depth calculation | Visual tree walking at render time | ViewModel `Depth` property computed at build time | Immune to virtualization container recycling, cheaper per-render |

## Common Pitfalls

### Pitfall 1: PersistAtomically per-item in bulk operations
**What goes wrong:** Each Delete/DeleteGroup call serializes the entire JSON file and writes it to disk. For N items, this is N file writes. On slow storage, antivirus-scanned paths, or with concurrent file access, IOException can occur.
**Why it happens:** `JsonConnectionStore` was designed for single-item operations, not batch operations.
**How to avoid:** Add a `BeginBatch`/`EndBatch` mechanism or a `DeleteBatch(IEnumerable<Guid>)` method that defers `PersistAtomically` until all mutations are complete.
**Warning signs:** Application freezes during bulk delete, IOException in logs, connections.json.tmp file left behind.

### Pitfall 2: Deleting connections with active RDP sessions
**What goes wrong:** The delete code removes the connection from the data store but does not close its active RDP tab. The tab still references the deleted connection ID. Subsequent operations on the tab (status bar update, reconnect, properties lookup) may throw NullReferenceException.
**Why it happens:** The delete path and the tab management path are decoupled -- delete operates on data, tabs operate on live sessions.
**How to avoid:** Before deleting, check `_tabHostManager.TryGet(connItem.Id, out _)` and close active sessions first. Or, at minimum, check if the connection is open and include it in the confirmation message.
**Warning signs:** Zombie tabs that show stale data, crashes when clicking a tab after its connection was deleted.

### Pitfall 3: TreeView virtualization breaks visual-tree-dependent code
**What goes wrong:** Converters that walk `VisualTreeHelper.GetParent()` return stale depth values when containers are recycled. Items display at wrong indentation levels or with wrong guide lines.
**Why it happens:** `{Binding RelativeSource={RelativeSource TemplatedParent}}` resolves once when the template is applied. Container recycling reuses the template without re-applying it.
**How to avoid:** Move depth computation to the ViewModel. Bind to `DataContext.Depth` instead of walking the visual tree.
**Warning signs:** Indent guides at wrong positions, items appearing at wrong depth after scrolling.

### Pitfall 4: ScrollViewer.CanContentScroll must be True for virtualization
**What goes wrong:** Setting `VirtualizingPanel.IsVirtualizing="True"` has no effect if `ScrollViewer.CanContentScroll` is `False`. WPF falls back to pixel-based scrolling which creates all containers upfront.
**Why it happens:** Pixel-based scrolling needs to know the total height of all items, which requires all containers to exist.
**How to avoid:** Explicitly set `ScrollViewer.CanContentScroll="True"` on the TreeView. Verify scrolling is item-based (items snap to boundaries) rather than smooth pixel scrolling.
**Warning signs:** Memory usage stays high with virtualization "enabled", all containers are generated at startup.

### Pitfall 5: Two WFH controls visible simultaneously during tab switch
**What goes wrong:** If the fix for black flash involves showing the new WFH before hiding the old one, two Win32 child HWNDs exist in the same pixel region. The Win32 window manager picks the one with higher z-order (typically the one added later to the visual tree). The user may see the old session's frame above the new one for a frame.
**Why it happens:** Win32 airspace constraint -- each pixel belongs to exactly one HWND.
**How to avoid:** Use the bitmap snapshot approach: capture old WFH to Image, hide old WFH, show new WFH, wait one render pass, then hide snapshot Image.
**Warning signs:** Brief flicker showing the wrong session content, z-order battles.

## Code Examples

### STAB-01: Batch delete with single persist

```csharp
// Source: Proposed pattern based on existing JsonConnectionStore.cs
// Add to IConnectionStore:
void DeleteBatch(IEnumerable<Guid> connectionIds, IEnumerable<Guid> groupIds);

// Implementation in JsonConnectionStore:
public void DeleteBatch(IEnumerable<Guid> connectionIds, IEnumerable<Guid> groupIds)
{
    foreach (var groupId in groupIds)
    {
        _data.Groups.RemoveAll(g => g.Id == groupId);
        foreach (var conn in _data.Connections.Where(c => c.GroupId == groupId))
            conn.GroupId = null;
    }

    var idSet = connectionIds.ToHashSet();
    _data.Connections.RemoveAll(c => idSet.Contains(c.Id));

    PersistAtomically(); // Single write at the end
}
```

### STAB-02: Snapshot-based tab switch

```csharp
// Source: Pattern extending existing AirspaceSwapper.CaptureHwnd
private void SetActiveHostVisibility(Guid activeId)
{
    WindowsFormsHost? outgoing = null;
    WindowsFormsHost? incoming = null;

    // Identify outgoing (currently visible) and incoming (target) hosts
    foreach (var child in HostContainer.Children)
    {
        if (child is not WindowsFormsHost wfh) continue;
        if (wfh.Tag is not Guid id) continue;

        if (id == activeId)
            incoming = wfh;
        else if (wfh.Visibility == Visibility.Visible)
            outgoing = wfh;
    }

    if (incoming is null) return;
    if (outgoing == incoming) return;

    // Capture outgoing frame as bitmap overlay (covers the black gap)
    if (outgoing is not null && _airspace is not null)
    {
        // AirspaceSwapper already has CaptureHwnd and overlay Image per host
        // Leverage existing infrastructure to show snapshot
    }

    // Show incoming, hide all others
    foreach (var child in HostContainer.Children)
    {
        if (child is WindowsFormsHost wfh)
        {
            var isActive = wfh.Tag is Guid id && id == activeId;
            wfh.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            wfh.IsEnabled = isActive;
        }
    }

    // Hide snapshot after render pass
    Dispatcher.InvokeAsync(() =>
    {
        // Clear snapshot overlay after the incoming WFH has painted
    }, DispatcherPriority.Loaded);
}
```

### STAB-05: ViewModel-level depth property

```csharp
// Source: Proposed pattern to replace visual-tree-walking converters
// Add to TreeItemViewModel base class:
public int Depth { get; set; }

// Compute during BuildTree():
private void AssignDepths(ObservableCollection<TreeItemViewModel> items, int depth)
{
    foreach (var item in items)
    {
        item.Depth = depth;
        if (item is GroupTreeItemViewModel group)
            AssignDepths(group.Children, depth + 1);
    }
}

// New converter (replaces visual tree walking):
public sealed class DepthToIndentConverter : IValueConverter
{
    public double IndentSize { get; set; } = 19.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int depth) return new Thickness(0);
        return new Thickness(IndentSize * depth, 0, 0, 0);
    }
    // ...
}

// XAML binding (replaces RelativeSource TemplatedParent):
// <Grid Margin="{Binding DataContext.Depth,
//     RelativeSource={RelativeSource TemplatedParent},
//     Converter={StaticResource DepthToIndent}}" />
```

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit.v3 |
| Config file | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| Quick run command | `dotnet test tests/Deskbridge.Tests --filter "Category=Stability" -x` |
| Full suite command | `dotnet test tests/Deskbridge.Tests` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| STAB-01 | Bulk delete 10+ connections without crash | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~BulkDelete" -x` | No -- Wave 0 |
| STAB-01 | DeleteBatch writes file once | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~DeleteBatch" -x` | No -- Wave 0 |
| STAB-01 | Active session closed before delete | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~ActiveSessionDelete" -x` | No -- Wave 0 |
| STAB-02 | Tab switch rapid fire no exception | manual-only | Visual inspection with 5+ tabs | N/A |
| STAB-05 | TreeItemViewModel.Depth computed correctly | unit | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~TreeDepth" -x` | No -- Wave 0 |
| STAB-05 | Virtualization enabled without selection bugs | manual-only | Visual inspection with 100+ items | N/A |

### Sampling Rate
- **Per task commit:** `dotnet test tests/Deskbridge.Tests --filter "Category=Stability" -x`
- **Per wave merge:** `dotnet test tests/Deskbridge.Tests`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/Deskbridge.Tests/Services/BulkDeleteTests.cs` -- covers STAB-01 batch delete
- [ ] `tests/Deskbridge.Tests/ViewModels/TreeDepthTests.cs` -- covers STAB-05 depth computation

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Active tab connections are not checked before deletion and may produce zombie tabs | STAB-01 Vector 4 | If TabHostManager already handles this gracefully (e.g., event-driven cleanup), the fix is unnecessary |
| A2 | The bitmap snapshot approach for tab switching eliminates the black flash entirely | STAB-02 Mitigation 1 | If PrintWindow fails on the RDP control (some servers block it), the snapshot will be empty/black -- need fallback |
| A3 | DepthToGuideLinesConverter binding does not re-evaluate on container recycling | STAB-05 Pattern 1 | If WPF does re-evaluate RelativeSource TemplatedParent bindings on recycling, the converter is already safe |

## Open Questions

1. **Does deleting a connection with an active tab cause a crash?**
   - What we know: Delete removes from store, RefreshTree rebuilds, but TabHostManager still holds a reference to the host by connection ID
   - What's unclear: Does TabHostManager handle missing connections gracefully? Does the tab simply stay open with stale data?
   - Recommendation: Test manually -- delete a connection that has an active RDP tab and observe behavior

2. **Does PrintWindow work reliably on AxMsRdpClient during active session?**
   - What we know: AirspaceSwapper.CaptureHwnd already uses PrintWindow for drag/resize snapshots. This works.
   - What's unclear: Is the captured frame up-to-date (not a stale DirectX surface)? Some RDP servers use hardware-accelerated rendering that PrintWindow cannot capture.
   - Recommendation: Test with actual RDP sessions across different server types (Windows Server 2019/2022, xrdp)

3. **What is the practical item count threshold where non-virtualized TreeView becomes sluggish?**
   - What we know: Requirement says 100+ connections. Each item generates a TreeViewItem with custom ControlTemplate including ItemsControl for guide lines, ToggleButton, ContentPresenter.
   - What's unclear: Exact threshold. 100 items with 3-level nesting is 100+ containers. Is that enough to cause visible lag?
   - Recommendation: Profile BuildTree and initial render time with 100, 200, 500 item test datasets

## Sources

### Primary (HIGH confidence)
- Codebase: `ConnectionTreeViewModel.cs` DeleteSelectedAsync (lines 860-944) -- bulk delete code path
- Codebase: `JsonConnectionStore.cs` Delete/DeleteGroup (lines 88-125) -- per-item PersistAtomically
- Codebase: `MainWindow.xaml.cs` SetActiveHostVisibility (lines 448-471) -- tab switch visibility
- Codebase: `AirspaceSwapper.cs` -- existing bitmap capture infrastructure
- Codebase: `TreeViewMultiSelectBehavior.cs` -- multi-select implementation, FindContainerForItem
- Codebase: `DepthToGuideLinesConverter.cs`, `TreeViewItemIndentConverter.cs` -- visual tree walking converters
- Codebase: `ConnectionTreeControl.xaml` line 341 -- VirtualizingPanel.IsVirtualizing="False"
- [Microsoft Learn: How to Improve Performance of a TreeView](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-improve-the-performance-of-a-treeview) -- official virtualization guidance

### Secondary (MEDIUM confidence)
- [dotnet/wpf#5892](https://github.com/dotnet/wpf/issues/5892) -- WindowsFormsHost flicker during resize
- [dotnet/wpf#11331](https://github.com/dotnet/wpf/issues/11331) -- TreeView hang with null items + virtualization
- [dotnet/wpf#7321](https://github.com/dotnet/wpf/issues/7321) -- TreeView virtualization broken .NET 7 (fixed .NET 8+)
- [dotnet/wpf#10044](https://github.com/dotnet/wpf/issues/10044) -- WindowsFormsHost rendering with fluent themes
- [MSDN Forums: WindowsFormsHost Flicker on Visibility Change](https://learn.microsoft.com/en-us/archive/msdn-technet-forums/957d58bb-95f4-4bf0-9f87-aef55b564af8)

### Tertiary (LOW confidence)
- [dotnet/wpf#1962](https://github.com/dotnet/wpf/issues/1962) -- TreeView virtualization scroll freeze (deep trees)

## Metadata

**Confidence breakdown:**
- STAB-01 (Bulk delete): HIGH -- code path fully traced, crash vectors identified from source
- STAB-02 (Black flash): HIGH -- root cause understood (WPF airspace limitation), mitigation pattern exists in codebase
- STAB-05 (Virtualization): HIGH -- all three breaking patterns identified, fix strategy clear
- Test architecture: MEDIUM -- tests are unit-level for data operations; UI visual tests require manual verification

**Research date:** 2026-04-19
**Valid until:** 2026-05-19 (stable domain, no external dependency changes expected)
