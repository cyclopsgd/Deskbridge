---
phase: 03-connection-management
plan: 04
subsystem: tree-interactions
tags: [multi-select, drag-drop, context-menu, f2-rename, keyboard-shortcuts, editor-wiring]
dependency_graph:
  requires:
    - phase: 03-02
      provides: ConnectionTreeViewModel, TreeItemViewModel-hierarchy, ConnectionTreeControl-UserControl
    - phase: 03-03
      provides: ConnectionEditorViewModel, GroupEditorViewModel, ConnectionEditorDialog, GroupEditorDialog
  provides:
    - TreeViewMultiSelectBehavior
    - TreeViewDragDropBehavior
    - DropInsertionAdorner
    - FullCommandImplementations
    - CtrlN-global-shortcut
  affects: [App.xaml.cs-DI, MainWindowViewModel, MainWindow-InputBindings]
tech_stack:
  added: []
  patterns: [attached-behavior-pattern, adorner-visual-indicator, in-process-dataobject-dnd, code-behind-context-menu-assignment]
key_files:
  created:
    - src/Deskbridge/Behaviors/TreeViewMultiSelectBehavior.cs
    - src/Deskbridge/Behaviors/TreeViewDragDropBehavior.cs
  modified:
    - src/Deskbridge/Views/ConnectionTreeControl.xaml
    - src/Deskbridge/Views/ConnectionTreeControl.xaml.cs
    - src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs
    - src/Deskbridge/ViewModels/MainWindowViewModel.cs
    - src/Deskbridge/MainWindow.xaml
    - src/Deskbridge/App.xaml.cs
    - tests/Deskbridge.Tests/ViewModels/MainWindowViewModelTests.cs
decisions:
  - "ConnectionTreeViewModel registered as singleton (was transient) so MainWindowViewModel and ConnectionTreeControl share the same instance"
  - "Context menu assigned dynamically in code-behind PreviewMouseRightButtonDown instead of via XAML DataTrigger+converter (simpler, avoids type converter complexity)"
  - "Move to... submenu populated programmatically each time context menu opens via GetAvailableGroupsForMove()"
  - "GetDialogHostEx() used instead of deprecated GetDialogHost() per WPF-UI 4.2.0 API"
  - "ShowSimpleDialogAsync extension method requires Wpf.Ui.Extensions using"
  - "DuplicateConnection sets CredentialMode=Inherit (not copying original mode) per T-03-14 threat mitigation"
metrics:
  duration: 14min
  completed: pending-checkpoint
  tasks: 2
  files: 9
---

# Phase 03 Plan 04: Tree Interactions and Command Wiring Summary

Multi-select via Ctrl+Click/Shift+Click attached behavior, drag-drop with adorner visual indicators, 4 context menus (connection/group/multi-select/empty), F2 inline rename, all 11 ConnectionTreeViewModel commands implemented with editor dialog wiring, delete confirmation per UI-SPEC copywriting, Copy Hostname with snackbar, Duplicate with "(Copy)" suffix, MoveToGroup with immediate persistence, and Ctrl+N global shortcut.

## What Was Built

### Task 1: Behaviors, Context Menus, F2 Rename, Keyboard Shortcuts

**TreeViewMultiSelectBehavior** (`src/Deskbridge/Behaviors/TreeViewMultiSelectBehavior.cs`):
- Static class with `EnableMultiSelect` attached DependencyProperty
- Intercepts `PreviewMouseLeftButtonDown` on TreeView
- Plain click: deselect all, select clicked item, set PrimarySelectedItem
- Ctrl+Click: toggle clicked item's IsSelected, update SelectedItems collection
- Shift+Click: range selection using `GetFlatVisibleItems()` helper that walks tree in display order respecting expanded/collapsed groups
- Tracks `_lastClickedItem` for Shift+Click anchor (not updated on Shift+Click itself)
- Uses ViewModel's `IsSelected` property, not TreeViewItem.IsSelected

**TreeViewDragDropBehavior** (`src/Deskbridge/Behaviors/TreeViewDragDropBehavior.cs`):
- Static class with `EnableDragDrop` attached DependencyProperty
- State machine: Idle -> Pending (mouse down) -> Dragging (4px threshold) -> Drop/Cancel
- Uses `DataObject.SetData(string, List<TreeItemViewModel>)` for in-process DnD (no BinaryFormatter)
- Drop on group: calls MoveToGroupCommand with group's Id
- Drop on connection: moves to same group as target connection
- `DropInsertionAdorner`: renders group highlight (SubtleFillColorSecondaryBrush rectangle) or 2px insertion line (SystemAccentColorPrimaryBrush)
- Escape key clears visual indicators

**Context Menus** (ConnectionTreeControl.xaml):
- Connection context menu: Connect, Edit, Rename, Duplicate, Copy Hostname, Move to..., New Connection, New Group, Delete (10 items with separators per D-07)
- Group context menu: Edit, Rename, New Connection, New Group, Move to..., Delete (7 items)
- Multi-select context menu: "[N] items selected" disabled header, Move to..., Delete
- Empty area context menu: New Connection, New Group
- Delete menu item uses DeskbridgeErrorBrush foreground
- Move to... submenu populated dynamically with depth-indented groups and (Root) option

**F2 Rename** (ConnectionTreeControl.xaml.cs):
- PreviewKeyDown handler: F2 triggers RenameItem command (sets IsRenaming=true)
- RenameTextBox_Loaded: auto-focus and select-all when TextBox becomes visible
- RenameTextBox_KeyDown: Enter commits rename, Escape restores original name
- RenameTextBox_LostFocus: commits rename on focus loss
- Original name tracked for Escape cancel

**Keyboard Shortcuts** (ConnectionTreeControl.xaml.cs):
- F2: inline rename
- Delete: delete selected items with confirmation
- Enter: connect (stub) on connection, toggle expand/collapse on group
- Ctrl+C: copy hostname of selected connection
- Escape: deselect all
- Double-click: open editor on connection, toggle expand/collapse on group

### Task 2: Command Implementations and Ctrl+N Shortcut

**ConnectionTreeViewModel Commands** (all 11 stubs replaced with implementations):
- `NewConnectionAsync()`: resolves ConnectionEditorViewModel via IServiceProvider, initializes, shows ConnectionEditorDialog via GetDialogHostEx(), saves and refreshes tree on Primary result
- `NewGroupAsync()`: same pattern with GroupEditorViewModel + GroupEditorDialog
- `EditItemAsync(TreeItemViewModel)`: loads existing model from IConnectionStore, opens correct editor based on item type (connection vs group)
- `DeleteSelectedAsync()`: snapshots SelectedItems (Pitfall 7), builds confirmation message per UI-SPEC copywriting contract (single connection/group/multi), shows SimpleContentDialogCreateOptions, deletes via IConnectionStore, cleans up credentials via ICredentialService
- `RenameItem(TreeItemViewModel)`: sets IsRenaming=true (TextBox appears via DataTemplate)
- `CopyHostname(ConnectionTreeItemViewModel)`: Clipboard.SetText + ISnackbarService show "Hostname copied to clipboard" for 2 seconds
- `DuplicateConnection(ConnectionTreeItemViewModel)`: creates copy with new Guid, "(Copy)" suffix, CredentialMode=Inherit (T-03-14: no credential copy), saves and refreshes
- `MoveToGroup(Guid?)`: updates GroupId/ParentGroupId on all selected items, persists immediately, refreshes tree
- `ToggleQuickProperties()`: unchanged
- `Connect()`: stub for Phase 4/5

**Injected Services** (ConnectionTreeViewModel constructor):
- Added: IContentDialogService, ISnackbarService, IServiceProvider
- DI registration: ConnectionTreeViewModel changed from Transient to Singleton (shared by MainWindowViewModel and ConnectionTreeControl)

**Ctrl+N Global Shortcut** (MainWindow.xaml):
- Added KeyBinding: `Gesture="Ctrl+N" Command="{Binding ConnectionTree.NewConnectionCommand}"`
- MainWindowViewModel now exposes `ConnectionTree` property (ConnectionTreeViewModel), injected via constructor

**Test Fix** (MainWindowViewModelTests.cs):
- Updated to pass ConnectionTreeViewModel (with mocked dependencies) to MainWindowViewModel constructor
- All 83 tests passing

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] IDE0019 pattern matching errors treated as errors**
- **Found during:** Task 1
- **Issue:** TreatWarningsAsErrors=true promotes IDE0019 (use pattern matching over as+null check) to errors. All `as Type` + null check patterns fail to compile.
- **Fix:** Changed all `var x = obj as Type; if (x is null)` to `if (obj is not Type x)` pattern matching across all 3 new/modified files
- **Files modified:** TreeViewMultiSelectBehavior.cs, TreeViewDragDropBehavior.cs, ConnectionTreeControl.xaml.cs

**2. [Rule 1 - Bug] GetDialogHost() deprecated in WPF-UI 4.2.0**
- **Found during:** Task 2
- **Issue:** `IContentDialogService.GetDialogHost()` marked obsolete with CS0618 error: "Use GetDialogHostEx() instead". Return type changed from ContentPresenter to ContentDialogHost.
- **Fix:** Changed to `GetDialogHostEx()` (returns ContentDialogHost directly)
- **Files modified:** ConnectionTreeViewModel.cs

**3. [Rule 1 - Bug] ShowSimpleDialogAsync is an extension method**
- **Found during:** Task 2
- **Issue:** `ShowSimpleDialogAsync` is in `Wpf.Ui.Extensions.ContentDialogServiceExtensions`, not on `IContentDialogService` interface directly. CS1061 error.
- **Fix:** Added `using Wpf.Ui.Extensions;`
- **Files modified:** ConnectionTreeViewModel.cs

**4. [Rule 2 - Critical] ConnectionTreeViewModel needed singleton registration**
- **Found during:** Task 2
- **Issue:** Both MainWindowViewModel (for Ctrl+N) and ConnectionTreeControl need the same ConnectionTreeViewModel instance. Transient registration would create separate instances.
- **Fix:** Changed DI registration from AddTransient to AddSingleton
- **Files modified:** App.xaml.cs

**5. [Rule 1 - Bug] MainWindowViewModelTests broke after constructor change**
- **Found during:** Task 2
- **Issue:** Tests used parameterless `new MainWindowViewModel()` but constructor now requires ConnectionTreeViewModel.
- **Fix:** Updated test to construct ConnectionTreeViewModel with NSubstitute mocks and pass to MainWindowViewModel
- **Files modified:** MainWindowViewModelTests.cs

## Threat Mitigations Applied

| Threat ID | Status | Implementation |
|-----------|--------|----------------|
| T-03-14 | Mitigated | DuplicateConnection sets CredentialMode=Inherit, does NOT copy credentials from original |
| T-03-15 | Mitigated | DeleteGroup shows confirmation with connection count; DeleteGroup in IConnectionStore orphans connections (GroupId=null), does not delete them |

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build Deskbridge.sln` | 0 errors, 0 warnings |
| `dotnet test tests/Deskbridge.Tests/` | 83 passed, 0 failed, 0 skipped |
| TreeViewMultiSelectBehavior | Static class, EnableMultiSelect attached property, Ctrl/Shift/plain click |
| Shift+Click range | GetFlatVisibleItems walks tree in display order |
| TreeViewDragDropBehavior | Static class, 4px threshold, DataObject in-process, DropInsertionAdorner |
| Connection context menu | 10 items per D-07 with correct icons and separators |
| Group context menu | 7 items per D-07 |
| Multi-select context menu | Count header + Move to + Delete |
| Delete uses DeskbridgeErrorBrush | Foreground="{DynamicResource DeskbridgeErrorBrush}" |
| F2 rename | IsRenaming=true, Enter/Escape/LostFocus handling |
| All 11 commands implemented | NewConnection, NewGroup, EditItem, DeleteSelected, Rename, CopyHostname, Duplicate, MoveToGroup, Connect, ToggleQuickProperties |
| Ctrl+N shortcut | KeyBinding in MainWindow.xaml |
| Credential cleanup on delete | DeleteForConnection/DeleteForGroup called before store delete |

## Checkpoint Pending

Task 3 (checkpoint:human-verify) is pending. Tasks 1 and 2 are complete. The checkpoint requires visual verification of the complete connection management feature end-to-end including:
- TreeView with groups/connections, context menus, drag-drop, F2 rename
- Multi-select with Ctrl+Click/Shift+Click
- Editor dialogs from context menu and double-click
- Delete confirmation per UI-SPEC copywriting
- Copy Hostname with snackbar
- Duplicate with "(Copy)" suffix
- Credential inheritance indicators
- Ctrl+N global shortcut
- Data persistence across app restart

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 | 8629df4 | feat(03-04): add multi-select, drag-drop behaviors, context menus, F2 rename, and keyboard shortcuts |
| 2 | 5ea85bf | feat(03-04): implement all tree commands, wire editor dialogs, add Ctrl+N shortcut |

## Self-Check: PASSED

All 9 created/modified files verified on disk. Both commit hashes (8629df4, 5ea85bf) found in git log.

---
*Phase: 03-connection-management*
*Completed: pending checkpoint verification*
