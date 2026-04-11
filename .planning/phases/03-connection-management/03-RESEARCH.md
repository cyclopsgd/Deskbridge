# Phase 3: Connection Management - Research

**Researched:** 2026-04-11
**Domain:** WPF data management (persistence, TreeView, dialogs, credential storage)
**Confidence:** HIGH

## Summary

Phase 3 implements the full connection management layer: JSON persistence with atomic writes, a TreeView with multi-select, drag-drop, context menus, and F2 inline rename, a quick properties panel for inline editing, ContentDialog-based editors for connections and groups, and credential storage via AdysTech.CredentialManager. This is the largest and most UI-intensive phase so far, touching every layer from Core services through to XAML views.

The primary technical challenges are: (1) WPF TreeView does not support multi-select natively, requiring ViewModel-based selection tracking with attached behaviors, (2) drag-drop within a TreeView requires careful state management and must avoid BinaryFormatter (removed in .NET 9+) for DataObject serialization, (3) WPF-UI ContentDialog with a TabControl inside requires the custom dialog pattern (extend `ui:ContentDialog`, call `ShowAsync` on a ContentDialogHost), and (4) AdysTech.CredentialManager uses `CredentialType.Generic` for TERMSRV targets, not DomainPassword.

**Primary recommendation:** Build the phase in layers -- persistence first (JsonConnectionStore + ICredentialService), then tree ViewModel + basic rendering, then interactions (drag-drop, multi-select, context menu, F2 rename), then editor dialogs, then quick properties panel. Wire ConnectionQueryService to IConnectionStore early (CR-01 fix) so all subsequent work has a live data source.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Flat arrays structure -- two top-level arrays: `connections` and `groups`. Connections reference groups via GroupId. IConnectionQuery and IConnectionStore already work with flat models from Phase 1.
- **D-02:** Schema versioning from day one -- top-level `"version": 1` in connections.json. Future migrations check version and transform.
- **D-03:** Atomic writes via temp-file-then-rename pattern. Write to `connections.json.tmp`, then `File.Move(overwrite: true)`. Crash during write leaves the old file intact.
- **D-04:** Standard WPF TreeView auto-styled by WPF-UI ControlsDictionary. HierarchicalDataTemplate for groups, DataTemplate for connections.
- **D-05:** Both drag-drop AND context menu "Move to..." for moving connections between groups. Drag-drop with visual drop indicator. Context menu as alternative.
- **D-06:** Multi-select via Ctrl+click (individual) and Shift+click (range). Enables bulk delete, move, export.
- **D-07:** Context menu items: Connect, Edit, Delete, Rename (F2), Move to... (submenu), New Connection, New Group, Copy Hostname, Duplicate. Separators between action groups.
- **D-08:** Collapsible quick properties panel below the tree in the Connections slide-out panel. Shows key fields of the selected connection (hostname, port, username, group, credential mode) for inline editing. Like mRemoteNG's properties pane.
- **D-09:** When a group is selected, the quick properties panel shows group-specific fields (name, credential username/domain, connection count).
- **D-10:** WPF-UI ContentDialog (modal overlay inside main window) for full connection editing. Uses IContentDialogService from Phase 2.
- **D-11:** 4 tabs as specified in REFERENCE.md: General (hostname, port, name, group), Credentials (mode selector + "inherited from" InfoBar + disabled fields), Display (resolution, color depth, smart sizing), Notes (free text).
- **D-12:** Group editor uses the SAME dual approach: inline quick edit in properties panel, plus right-click "Edit" opens a full ContentDialog with group name, credentials (username, domain, password), and count of connections that will inherit.
- **D-13:** Credential inheritance indicator: WPF-UI InfoBar at top of Credentials tab: "Credentials inherited from [Group Name]". Username/password fields shown but disabled/grayed. Switch CredentialMode to "Own" to enable fields.
- **D-14:** Full AdysTech.CredentialManager implementation in Phase 3 (not a stub). Credentials saved to Windows Credential Manager immediately when user sets them. TERMSRV/<hostname> for connections, DESKBRIDGE/GROUP/<guid> for groups.
- **D-15:** Prompt mode shows password dialog at connect time (Phase 4/5 pipeline scope). Phase 3 only stores the CredentialMode; the actual prompt is deferred to ResolveCredentialsStage.

### Claude's Discretion
- Dev path for connections.json: Claude picks what's most practical (likely %AppData% always for consistency, with easy reset via delete)
- Quick properties panel layout -- which fields exactly, collapsible mechanism, height
- Drag-drop visual indicator style (line between items vs highlight target group)
- Multi-select implementation pattern (custom SelectionMode or manual tracking)
- Search filter behavior in the tree (type-to-filter or separate search box bound to IConnectionQuery)

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| CONN-01 | Connection model with Id, Name, Hostname, Port, Username, Domain, Protocol, GroupId, Notes, Tags, CredentialMode, DisplaySettings, timestamps | Model already exists in ConnectionModel.cs. Verified complete. |
| CONN-02 | Connection groups with Id, Name, ParentGroupId, SortOrder supporting arbitrary nesting | Model already exists in ConnectionGroup.cs. Need SaveGroup/DeleteGroup methods on IConnectionStore. |
| CONN-03 | JSON persistence at %AppData%/Deskbridge/connections.json with atomic writes | JsonConnectionStore implementation using System.Text.Json + File.Move atomic pattern. See Architecture Patterns. |
| CONN-04 | Connection tree in slide-out panel with context menu, drag-drop reorder, F2 rename, and search filter | TreeView with HierarchicalDataTemplate, ViewModel-based multi-select, custom drag-drop behavior. See Architecture Patterns. |
| CONN-05 | Connection editor modal dialog with tabs: General, Credentials, Display, Notes | Custom ContentDialog extending ui:ContentDialog with TabControl inside. See Code Examples. |
| CONN-06 | Group editor for setting group-level credentials with inheritance count indicator | Custom ContentDialog with single-panel layout (no tabs). Uses ICredentialService.StoreForGroup. |
| CONN-07 | Credential storage via AdysTech.CredentialManager (TERMSRV for connections, DESKBRIDGE/GROUP for groups) | AdysTech.CredentialManager 3.1.0 with CredentialType.Generic. See Standard Stack and Code Examples. |
| CONN-08 | Credential inheritance resolves recursively up the group tree (Inherit/Own/Prompt modes) | ICredentialService.ResolveInherited walks group chain via IConnectionStore.GetGroupById. |
| CONN-09 | Connection editor shows "inherited from: [group name]" indicator when CredentialMode is Inherit | WPF-UI InfoBar with Severity="Informational" inside Credentials tab. See Code Examples. |
| CONN-10 | Groups display key icon when they have credentials set | HasCredentials property on GroupTreeItemViewModel, triggers Key24 icon overlay via DataTrigger. |
</phase_requirements>

## Standard Stack

### Core (Phase 3 additions)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| AdysTech.CredentialManager | 3.1.0 | Windows Credential Manager wrapper | P/Invoke wrapper for CredWrite/CredRead/CredDelete. Targets .NET 8 + netstandard2.0 (forward-compatible with .NET 10). Published Feb 2026, BinaryFormatter fallback removed. [VERIFIED: NuGet registry and GitHub] |
| System.Text.Json | (in-box .NET 10) | JSON serialization for connections.json | Source generator support, no external dependency. JsonSerializerOptions with WriteIndented for human-readable config. [VERIFIED: in-box with .NET 10] |

### Already Present (from Phase 1/2)

| Library | Version | Purpose |
|---------|---------|---------|
| WPF-UI | 4.2.0 | Fluent Design controls (ContentDialog, InfoBar, TreeView auto-styling) |
| CommunityToolkit.Mvvm | 8.4.2 | [ObservableProperty], [RelayCommand], ObservableValidator, WeakReferenceMessenger |
| Microsoft.Extensions.DependencyInjection | 10.0.x | DI container |

### New NuGet References Required

AdysTech.CredentialManager is NOT currently in Directory.Packages.props or any csproj. Phase 3 must add:

**Directory.Packages.props:**
```xml
<PackageVersion Include="AdysTech.CredentialManager" Version="3.1.*" />
```

**Deskbridge.Core.csproj** (the credential service lives in Core):
```xml
<PackageReference Include="AdysTech.CredentialManager" />
```

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| ViewModel-based multi-select | ygoe/MultiSelectTreeView NuGet | Archived (May 2022), read-only repo, won't receive WPF-UI styling updates. Manual ViewModel tracking is more work but fully styled. [VERIFIED: GitHub repo archived] |
| AdysTech.CredentialManager | Raw P/Invoke to CredWrite/CredRead | Unnecessary duplication; AdysTech wraps the same APIs cleanly. [CITED: REFERENCE.md] |
| System.Text.Json manual | SQLite via Microsoft.Data.Sqlite | Out-of-scope per project constraints (JSON only, no SQLite). [CITED: REFERENCE.md] |

**Installation (Phase 3 additions):**
```bash
dotnet add src/Deskbridge.Core/Deskbridge.Core.csproj package AdysTech.CredentialManager
```
Note: With Central Package Management, add the PackageVersion to Directory.Packages.props and a bare PackageReference to the csproj.

## Architecture Patterns

### Recommended Project Structure (new files in Phase 3)

```
src/
├── Deskbridge.Core/
│   ├── Interfaces/
│   │   ├── IConnectionStore.cs         # EXISTS -- needs SaveGroup/DeleteGroup additions
│   │   └── ICredentialService.cs       # NEW
│   ├── Services/
│   │   ├── ConnectionQueryService.cs   # EXISTS -- fix CR-01 (inject IConnectionStore)
│   │   ├── JsonConnectionStore.cs      # NEW
│   │   └── WindowsCredentialService.cs # NEW
│   └── Models/
│       ├── ConnectionModel.cs          # EXISTS (complete)
│       ├── ConnectionGroup.cs          # EXISTS (complete)
│       └── Enums.cs                    # EXISTS (CredentialMode already defined)
├── Deskbridge/
│   ├── Views/
│   │   ├── ConnectionTreeControl.xaml  # NEW -- UserControl for tree + search + quick props
│   │   └── ConnectionTreeControl.xaml.cs
│   ├── Dialogs/
│   │   ├── ConnectionEditorDialog.xaml # NEW -- Custom ContentDialog with TabControl
│   │   ├── ConnectionEditorDialog.xaml.cs
│   │   ├── GroupEditorDialog.xaml      # NEW -- Custom ContentDialog for group editing
│   │   └── GroupEditorDialog.xaml.cs
│   ├── ViewModels/
│   │   ├── ConnectionTreeViewModel.cs  # NEW
│   │   ├── TreeItemViewModel.cs        # NEW (base + derived classes)
│   │   ├── ConnectionEditorViewModel.cs # NEW
│   │   └── GroupEditorViewModel.cs     # NEW
│   ├── Behaviors/
│   │   ├── TreeViewMultiSelectBehavior.cs  # NEW -- attached behavior for multi-select
│   │   └── TreeViewDragDropBehavior.cs     # NEW -- attached behavior for drag-drop
│   └── Converters/
│       └── InverseBoolToVisibilityConverter.cs # NEW (if not using DataTriggers everywhere)
└── tests/
    └── Deskbridge.Tests/
        ├── JsonConnectionStoreTests.cs         # NEW
        ├── WindowsCredentialServiceTests.cs    # NEW (integration tests)
        ├── CredentialInheritanceTests.cs        # NEW
        └── ConnectionQueryServiceStoreTests.cs  # NEW (CR-01 fix verification)
```

### Pattern 1: JsonConnectionStore (Flat JSON + Atomic Write)

**What:** Implements IConnectionStore with flat JSON arrays and atomic write via temp+rename.
**When to use:** All connection/group CRUD operations.

```csharp
// Source: REFERENCE.md + D-01, D-02, D-03
// [VERIFIED: File.Move overwrite:true is atomic for same-directory rename on NTFS]
public sealed class JsonConnectionStore : IConnectionStore
{
    private readonly string _filePath;
    private ConnectionsFile _data = new();

    // JSON schema with version field (D-02)
    private sealed class ConnectionsFile
    {
        public int Version { get; set; } = 1;
        public List<ConnectionModel> Connections { get; set; } = [];
        public List<ConnectionGroup> Groups { get; set; } = [];
    }

    public void Save(ConnectionModel connection)
    {
        // Upsert into _data.Connections by Id
        // Then call PersistAtomically()
    }

    private void PersistAtomically()
    {
        var tmpPath = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(_data, _jsonOptions);
        File.WriteAllText(tmpPath, json, Encoding.UTF8);
        File.Move(tmpPath, _filePath, overwrite: true); // Atomic on NTFS same-dir
    }

    public void Load()
    {
        if (!File.Exists(_filePath)) return;
        var json = File.ReadAllText(_filePath, Encoding.UTF8);
        _data = JsonSerializer.Deserialize<ConnectionsFile>(json, _jsonOptions) ?? new();
        // Check _data.Version for future migration
    }
}
```

**Critical notes:**
- `File.Move(src, dest, overwrite: true)` on NTFS same-directory is atomic for the rename operation itself. [VERIFIED: Microsoft docs confirm same-directory rename is atomic on NTFS]
- Write to `.tmp` in the same directory as the target file (not %TEMP%) to guarantee same-volume operation.
- Use `JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }` for human-readable config.
- Consider using `JsonSerializerContext` (source generator) for AOT compatibility, though not required for v1.

### Pattern 2: IConnectionStore Extension for Groups

**What:** The existing IConnectionStore interface needs additional group mutation methods.
**Current interface:**
```csharp
public interface IConnectionStore
{
    IReadOnlyList<ConnectionModel> GetAll();
    ConnectionModel? GetById(Guid id);
    void Save(ConnectionModel connection);
    void Delete(Guid id);
    IReadOnlyList<ConnectionGroup> GetGroups();
    ConnectionGroup? GetGroupById(Guid id);
}
```

**Methods to add:**
```csharp
// Add to IConnectionStore:
void SaveGroup(ConnectionGroup group);
void DeleteGroup(Guid groupId);  // Also deletes or orphans child connections
```

### Pattern 3: ViewModel-Based Multi-Select for TreeView (D-06)

**What:** Track selection in the ViewModel since WPF TreeView only supports single SelectedItem.
**When to use:** All tree interaction code.

Standard WPF TreeView binds `TreeViewItem.IsSelected` but only maintains one selected item. Multi-select requires:

1. An `IsSelected` property on each `TreeItemViewModel`
2. An attached behavior on the TreeView that intercepts mouse events
3. Logic for Ctrl+Click (toggle), Shift+Click (range), plain Click (single)

```csharp
// Attached behavior approach (simplified) [ASSUMED]
public static class TreeViewMultiSelectBehavior
{
    // Attached property: EnableMultiSelect (bool)
    // On TreeViewItem PreviewMouseLeftButtonDown:
    //   - If Ctrl held: toggle item.IsSelected
    //   - If Shift held: select range from _lastClickedItem to current
    //   - Otherwise: deselect all, select current
    // Maintain _lastClickedItem for Shift+Click range calculation
}
```

**Key challenge:** Shift+Click range selection requires a flat ordered list of visible items. The ViewModel must provide a method to enumerate all visible items in display order (respecting expand/collapse state) to compute the range.

### Pattern 4: TreeView Drag-Drop (D-05)

**What:** In-process drag-drop within the same TreeView for reordering connections between groups.
**When to use:** Moving connections/groups in the tree.

**BinaryFormatter consideration:** Since BinaryFormatter is removed in .NET 9+, drag-drop with custom types in `DataObject` must avoid BinaryFormatter serialization. For in-process drag-drop (same TreeView), this is not a problem -- pass the ViewModel reference directly via `DataObject.SetData(typeof(TreeItemViewModel), item)` since no cross-process serialization occurs. [VERIFIED: Microsoft docs confirm BinaryFormatter only needed for cross-process DnD]

```csharp
// Drag-drop state machine (from UI-SPEC):
// [Idle] -> mouse down on selected -> [Pending] (record start)
// [Pending] -> mouse move > 4px -> [Dragging] (DoDragDrop)
// [Pending] -> mouse up -> [Idle] (was click)
// [Dragging] -> drop on group -> move items, persist
// [Dragging] -> Escape -> cancel
```

**Visual indicators:**
- Drop ON group: highlight target group background with `SubtleFillColorSecondaryBrush`
- Drop BETWEEN items: 2px horizontal accent line showing insertion position
- Use `AdornerLayer` for the insertion line indicator

### Pattern 5: Custom ContentDialog for Editor (D-10)

**What:** Extend `ui:ContentDialog` for connection and group editors.
**When to use:** Full connection editing with tabs.

WPF-UI ContentDialog is shown via `IContentDialogService` or by creating a custom dialog that accepts the `ContentDialogHost` and calls `ShowAsync()`. The project already has `ContentDialogHost` registered in MainWindow.xaml and `IContentDialogService` in DI from Phase 2.

```csharp
// Custom dialog pattern [VERIFIED: WPF-UI GitHub discussions #726, #848]
public partial class ConnectionEditorDialog : ContentDialog
{
    public ConnectionEditorDialog(ContentPresenter dialogHost)
        : base(dialogHost)
    {
        InitializeComponent();
    }
}
```

**Important:** In WPF-UI 4.2.0, `ContentDialogHost` is the recommended host element (ContentPresenter is deprecated for this purpose). The existing MainWindow.xaml already uses `ui:ContentDialogHost`. The custom dialog constructor should accept `ContentDialogHost` if that constructor overload exists, otherwise use `IContentDialogService.GetDialogHost()`. [VERIFIED: Phase 2 code uses `contentDialogService.SetDialogHost(RootContentDialog)` where RootContentDialog is a ContentDialogHost]

### Pattern 6: F2 Inline Rename

**What:** Replace TextBlock with TextBox on F2 keypress for inline name editing.
**When to use:** Renaming connections and groups in the tree.

Use a DataTrigger bound to `IsRenaming` property on the TreeItemViewModel:

```xml
<!-- Inside HierarchicalDataTemplate / DataTemplate -->
<Grid>
    <TextBlock Text="{Binding Name}"
               Visibility="{Binding IsRenaming, Converter={StaticResource InverseBoolToVisibility}}" />
    <ui:TextBox Text="{Binding Name, UpdateSourceTrigger=LostFocus}"
                Visibility="{Binding IsRenaming, Converter={StaticResource BoolToVisibility}}"
                Height="24" Padding="4,2"
                ui:FocusExtension.IsFocused="{Binding IsRenaming}" />
</Grid>
```

When `IsRenaming` becomes true, the TextBox appears with focus. Enter commits (via KeyDown handler), Escape cancels (restores original name), LostFocus commits. [ASSUMED: FocusExtension may need custom implementation for auto-focus]

### Pattern 7: CR-01 Fix (ConnectionQueryService)

**What:** Fix the CR-01 bug where ConnectionQueryService uses an empty in-memory list.
**Root cause:** `ConnectionQueryService()` parameterless constructor initializes `_connections = []`.
**Fix:** Inject `IConnectionStore` and delegate to it.

```csharp
public sealed class ConnectionQueryService : IConnectionQuery
{
    private readonly IConnectionStore _store;

    public ConnectionQueryService(IConnectionStore store)
    {
        _store = store;
    }

    public IReadOnlyList<ConnectionModel> GetAll() => _store.GetAll();

    public IReadOnlyList<ConnectionModel> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAll();

        var normalizedQuery = query.Trim().ToLowerInvariant();
        return _store.GetAll()
            .Select(c => new { Connection = c, Score = CalculateScore(c, normalizedQuery) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Connection)
            .ToList();
    }
    // ... remaining methods delegate to _store.GetAll() instead of _connections
}
```

**Test impact:** Existing `ConnectionQueryTests` use the `ConnectionQueryService(IEnumerable<ConnectionModel>)` constructor. This constructor can be kept for test compatibility, or tests can be updated to use a mock `IConnectionStore`.

### Anti-Patterns to Avoid

- **Never store passwords in connections.json:** JSON stores `CredentialMode` (Inherit/Own/Prompt) but NEVER the actual password. Passwords go to Windows Credential Manager only. [CITED: REFERENCE.md constraint #11]
- **Never use `SecureString`:** Deprecated by Microsoft (DE0001). Use plain `string` for in-memory credential handling. [CITED: REFERENCE.md "Do NOT Use" section]
- **Never use BinaryFormatter for drag-drop DataObject:** Removed in .NET 9+. Pass ViewModel references directly for in-process DnD. [VERIFIED: Microsoft migration guide]
- **Never hand-roll multi-select by overriding TreeView SelectionMode:** WPF TreeView has no SelectionMode property. Use ViewModel tracking + attached behavior.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Credential storage | Custom P/Invoke to CredWrite/CredRead | AdysTech.CredentialManager 3.1.0 | Handles blob size limits, Unicode, error codes. Edge cases documented in prior research. [CITED: PITFALLS.md Pitfall 14] |
| JSON serialization | Custom file format parser | System.Text.Json with source generators | In-box, performant, handles null/optional fields correctly |
| Atomic file writes | Custom Win32 ReplaceFile interop | File.Move(overwrite: true) in same directory | .NET API wraps MoveFileEx. Same-directory rename is atomic on NTFS. |
| Observable collections | Manual PropertyChanged wiring | CommunityToolkit.Mvvm ObservableObject | Source generators handle all boilerplate |
| Dialog overlay/dimming | Custom popup window | WPF-UI ContentDialog + ContentDialogHost | Handles overlay, dimming, focus trapping, keyboard navigation |
| Form validation | Manual field checking | CommunityToolkit.Mvvm ObservableValidator with DataAnnotations | [Required], [Range], etc. with `ValidateAllProperties()` |

**Key insight:** This phase has a lot of UI surface area but most of it is standard WPF patterns auto-styled by WPF-UI. The main custom work is multi-select tracking and drag-drop behavior -- everything else uses existing controls and libraries.

## Common Pitfalls

### Pitfall 1: WPF TreeView SelectedItem Binding is One-Way-To-Source Only

**What goes wrong:** Developers try to set `TreeView.SelectedItem` from code/ViewModel and it doesn't work.
**Why it happens:** WPF TreeView.SelectedItem is read-only in XAML. You can only read it, not set it.
**How to avoid:** Use `TreeViewItem.IsSelected` via ItemContainerStyle binding. For programmatic selection, set the ViewModel's `IsSelected` property and let the binding propagate.
**Warning signs:** Selected item doesn't update when navigating programmatically.

### Pitfall 2: ContentDialog Cannot Be Shown While Another Is Open

**What goes wrong:** Attempting to show a delete confirmation dialog while an editor dialog is open throws an exception.
**Why it happens:** WPF-UI ContentDialog uses a single ContentDialogHost. Only one dialog can be active at a time.
**How to avoid:** Close the editor dialog before showing delete confirmation, or handle delete within the editor dialog itself using validation/buttons.
**Warning signs:** `InvalidOperationException` about dialog already being shown.

### Pitfall 3: File.Move Not Atomic Across Volumes

**What goes wrong:** Writing to %TEMP% and moving to %AppData% is not atomic if they're on different drives.
**Why it happens:** Cross-volume "move" is actually copy+delete, not rename.
**How to avoid:** Write the .tmp file in the SAME directory as connections.json (not %TEMP%). [VERIFIED: OS documentation]
**Warning signs:** Corrupted connections.json after crash during save.

### Pitfall 4: AdysTech CredentialManager CredentialType Must Be Generic for TERMSRV

**What goes wrong:** Saving credentials with `CredentialType.DomainPassword` causes Windows Credential Guard to block RDP credential usage.
**Why it happens:** Windows Credential Guard intercepts DomainPassword credentials. Generic credentials bypass this.
**How to avoid:** Always use `CredentialType.Generic` (the default) for both TERMSRV and DESKBRIDGE/GROUP targets. [VERIFIED: multiple community reports and Microsoft Q&A]
**Warning signs:** RDP connections fail with "credentials did not work" even though credentials are stored.

### Pitfall 5: PasswordBox Cannot Be Data-Bound in WPF

**What goes wrong:** `PasswordBox.Password` is not a DependencyProperty and cannot be bound in XAML.
**Why it happens:** Security design decision by Microsoft to prevent passwords from being stored in plain text in ViewModel properties.
**How to avoid:** Handle PasswordBox in code-behind: pass the PasswordBox.Password to the ViewModel's save method directly, or use an attached behavior. Do NOT create a bindable password property (defeats the security purpose). [VERIFIED: standard WPF limitation]
**Warning signs:** Binding error in output window, password field always empty.

### Pitfall 6: TreeView Virtualization Breaks Multi-Select Range Calculation

**What goes wrong:** Shift+Click range selection selects wrong items or crashes.
**Why it happens:** With UI virtualization enabled, TreeViewItems that aren't visible may not have containers, making it impossible to enumerate them.
**How to avoid:** For a tree with < 1000 items (typical connection count), disable virtualization: `VirtualizingPanel.IsVirtualizing="False"`. Or compute ranges in the ViewModel using the flat data model, not the visual tree.
**Warning signs:** IndexOutOfRange exceptions or incorrect selection ranges when tree is partially scrolled.

### Pitfall 7: ObservableCollection Modifications During Iteration

**What goes wrong:** Modifying the tree (delete, move) while iterating over selected items causes `InvalidOperationException`.
**Why it happens:** ObservableCollection raises CollectionChanged on every modification.
**How to avoid:** Copy `SelectedItems.ToList()` before performing bulk operations (delete, move).
**Warning signs:** Exception during multi-delete or multi-move.

### Pitfall 8: ConnectionQueryService Test Constructor vs DI Constructor

**What goes wrong:** After fixing CR-01, the existing `ConnectionQueryService(IEnumerable<ConnectionModel>)` constructor is orphaned but tests still use it.
**Why it happens:** The test constructor was a workaround for the missing IConnectionStore dependency.
**How to avoid:** Either (a) keep both constructors (DI uses IConnectionStore, tests use the list overload) or (b) update tests to mock IConnectionStore. Option (a) is simpler and non-breaking.
**Warning signs:** Tests fail after refactoring if only the IConnectionStore constructor is kept.

## Code Examples

### AdysTech.CredentialManager Usage

```csharp
// Source: AdysTech.CredentialManager GitHub README + API analysis
// [VERIFIED: GitHub source code for method signatures]

using AdysTech.CredentialManager;
using System.Net;

public sealed class WindowsCredentialService : ICredentialService
{
    // Connection-specific credentials (D-14): TERMSRV/<hostname>
    public NetworkCredential? GetForConnection(ConnectionModel connection)
    {
        var target = $"TERMSRV/{connection.Hostname}";
        return CredentialManager.GetCredentials(target, CredentialType.Generic);
    }

    public void StoreForConnection(ConnectionModel connection, string username, string? domain, string password)
    {
        var target = $"TERMSRV/{connection.Hostname}";
        var cred = new NetworkCredential(username, password, domain ?? string.Empty);
        CredentialManager.SaveCredentials(target, cred, CredentialType.Generic);
    }

    public void DeleteForConnection(ConnectionModel connection)
    {
        var target = $"TERMSRV/{connection.Hostname}";
        try { CredentialManager.RemoveCredentials(target, CredentialType.Generic); }
        catch { /* Credential may not exist -- swallow */ }
    }

    // Group-level credentials (D-14): DESKBRIDGE/GROUP/<guid>
    public NetworkCredential? GetForGroup(Guid groupId)
    {
        var target = $"DESKBRIDGE/GROUP/{groupId}";
        return CredentialManager.GetCredentials(target, CredentialType.Generic);
    }

    public void StoreForGroup(Guid groupId, string username, string? domain, string password)
    {
        var target = $"DESKBRIDGE/GROUP/{groupId}";
        var cred = new NetworkCredential(username, password, domain ?? string.Empty);
        CredentialManager.SaveCredentials(target, cred, CredentialType.Generic);
    }

    public void DeleteForGroup(Guid groupId)
    {
        var target = $"DESKBRIDGE/GROUP/{groupId}";
        try { CredentialManager.RemoveCredentials(target, CredentialType.Generic); }
        catch { /* Credential may not exist -- swallow */ }
    }

    // Credential inheritance resolution (CONN-08)
    public NetworkCredential? ResolveInherited(ConnectionModel connection, IConnectionStore store)
    {
        var groupId = connection.GroupId;
        while (groupId.HasValue)
        {
            var cred = GetForGroup(groupId.Value);
            if (cred is not null)
                return cred;

            var group = store.GetGroupById(groupId.Value);
            groupId = group?.ParentGroupId;
        }
        return null; // No credentials found in group chain
    }
}
```

### Custom ContentDialog with TabControl (D-10, D-11)

```xml
<!-- ConnectionEditorDialog.xaml -->
<!-- Source: WPF-UI GitHub discussions #726, #848 + DESIGN.md section 3 -->
<!-- [VERIFIED: ContentDialog pattern from WPF-UI docs] -->
<ui:ContentDialog x:Class="Deskbridge.Dialogs.ConnectionEditorDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    Title="{Binding DialogTitle}"
    DialogMaxWidth="560"
    PrimaryButtonText="Save"
    CloseButtonText="Cancel"
    PrimaryButtonAppearance="Primary"
    DefaultButton="Primary">

    <!-- IMPORTANT: Style BasedOn for WPF-UI theming -->
    <ui:ContentDialog.Resources>
        <Style BasedOn="{StaticResource {x:Type ui:ContentDialog}}"
               TargetType="{x:Type local:ConnectionEditorDialog}" />
    </ui:ContentDialog.Resources>

    <!-- Content: TabControl with 4 tabs -->
    <TabControl SelectedIndex="0">
        <TabItem Header="General">
            <StackPanel Margin="16,12">
                <TextBlock Text="Name" FontSize="14"
                    Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,4" />
                <ui:TextBox Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}"
                    PlaceholderText="Connection name" Margin="0,0,0,12" />

                <TextBlock Text="Hostname" FontSize="14"
                    Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,4" />
                <ui:TextBox Text="{Binding Hostname, UpdateSourceTrigger=PropertyChanged}"
                    PlaceholderText="hostname or IP" Margin="0,0,0,12" />

                <TextBlock Text="Port" FontSize="14"
                    Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,4" />
                <ui:TextBox Text="{Binding Port, UpdateSourceTrigger=PropertyChanged}"
                    PlaceholderText="3389" Margin="0,0,0,12" />

                <TextBlock Text="Group" FontSize="14"
                    Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,4" />
                <ComboBox ItemsSource="{Binding AvailableGroups}"
                    SelectedValue="{Binding GroupId}"
                    SelectedValuePath="Id"
                    DisplayMemberPath="DisplayName" />
            </StackPanel>
        </TabItem>

        <TabItem Header="Credentials">
            <StackPanel Margin="16,12">
                <TextBlock Text="Credential mode" FontSize="14"
                    Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,4" />
                <ComboBox SelectedValue="{Binding CredentialMode}"
                    SelectedValuePath="Tag" Margin="0,0,0,12">
                    <ComboBoxItem Content="Inherit from parent group" Tag="{x:Static models:CredentialMode.Inherit}" />
                    <ComboBoxItem Content="Use own credentials" Tag="{x:Static models:CredentialMode.Own}" />
                    <ComboBoxItem Content="Prompt at connection time" Tag="{x:Static models:CredentialMode.Prompt}" />
                </ComboBox>

                <!-- InfoBar for Inherit mode (D-13, CONN-09) -->
                <ui:InfoBar Title="Inherited credentials"
                    Message="{Binding InheritedFromMessage}"
                    Severity="Informational"
                    IsOpen="{Binding IsInheritInfoBarVisible, Mode=OneWay}"
                    IsClosable="False"
                    Margin="0,0,0,12" />

                <!-- InfoBar for Prompt mode -->
                <ui:InfoBar Title="Prompt mode"
                    Message="You will be asked for credentials each time you connect."
                    Severity="Warning"
                    IsOpen="{Binding IsPromptInfoBarVisible, Mode=OneWay}"
                    IsClosable="False"
                    Margin="0,0,0,12" />

                <!-- Credential fields (visible when not Prompt, enabled when Own) -->
                <StackPanel Visibility="{Binding IsCredentialFieldsVisible, Converter={StaticResource BoolToVisibility}}">
                    <TextBlock Text="Username" FontSize="14"
                        Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,4" />
                    <ui:TextBox Text="{Binding Username}"
                        PlaceholderText="username"
                        IsEnabled="{Binding IsCredentialFieldsEnabled}" Margin="0,0,0,12" />

                    <TextBlock Text="Domain" FontSize="14"
                        Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,4" />
                    <ui:TextBox Text="{Binding Domain}"
                        PlaceholderText="domain (optional)"
                        IsEnabled="{Binding IsCredentialFieldsEnabled}" Margin="0,0,0,12" />

                    <TextBlock Text="Password" FontSize="14"
                        Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,4" />
                    <PasswordBox x:Name="PasswordBox"
                        IsEnabled="{Binding IsCredentialFieldsEnabled}" />
                </StackPanel>
            </StackPanel>
        </TabItem>

        <TabItem Header="Display">
            <!-- Resolution + Smart Sizing -->
        </TabItem>

        <TabItem Header="Notes">
            <TextBox Text="{Binding Notes}" AcceptsReturn="True"
                TextWrapping="Wrap" MinHeight="120" Margin="16,12"
                VerticalScrollBarVisibility="Auto" />
        </TabItem>
    </TabControl>
</ui:ContentDialog>
```

### ConnectionEditorDialog Code-Behind

```csharp
// Source: WPF-UI ContentDialog pattern
// [VERIFIED: WPF-UI GitHub discussions + API docs]
public partial class ConnectionEditorDialog : ContentDialog
{
    private readonly ConnectionEditorViewModel _viewModel;

    public ConnectionEditorDialog(
        ContentDialogHost dialogHost,
        ConnectionEditorViewModel viewModel)
        : base(dialogHost)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    // Override to handle save with password from PasswordBox
    protected override void OnButtonClick(ContentDialogButton button)
    {
        if (button == ContentDialogButton.Primary)
        {
            // Pass PasswordBox.Password to ViewModel for credential storage
            _viewModel.SetPassword(PasswordBox.Password);
            if (!_viewModel.Validate())
            {
                // Prevent dialog from closing on validation failure
                return;
            }
        }
        base.OnButtonClick(button);
    }
}
```

### ICredentialService Interface Definition

```csharp
// Source: REFERENCE.md ICredentialService specification
// [CITED: REFERENCE.md Architecture section]
using System.Net;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

public interface ICredentialService
{
    // Connection-specific credentials (TERMSRV/<hostname>)
    NetworkCredential? GetForConnection(ConnectionModel connection);
    void StoreForConnection(ConnectionModel connection, string username, string? domain, string password);
    void DeleteForConnection(ConnectionModel connection);

    // Group-level credentials (DESKBRIDGE/GROUP/<guid>)
    NetworkCredential? GetForGroup(Guid groupId);
    void StoreForGroup(Guid groupId, string username, string? domain, string password);
    void DeleteForGroup(Guid groupId);

    // Inheritance resolution -- walks up the group tree
    NetworkCredential? ResolveInherited(ConnectionModel connection, IConnectionStore connectionStore);
}
```

### TreeView with HierarchicalDataTemplate (D-04)

```xml
<!-- Source: DESIGN.md section 3 + WPF-UI auto-styling -->
<!-- [VERIFIED: DESIGN.md confirms standard TreeView auto-styled by ControlsDictionary] -->
<TreeView ItemsSource="{Binding RootItems}"
          VirtualizingPanel.IsVirtualizing="False">
    <TreeView.Resources>
        <!-- Group template (HierarchicalDataTemplate for nesting) -->
        <HierarchicalDataTemplate DataType="{x:Type vm:GroupTreeItemViewModel}"
                                  ItemsSource="{Binding Children}">
            <StackPanel Orientation="Horizontal" Height="28" Margin="4,2">
                <ui:SymbolIcon Symbol="Folder24" FontSize="16" Margin="0,0,4,0" />
                <!-- TextBlock / TextBox toggle for F2 rename -->
                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="14"
                           VerticalAlignment="Center" />
            </StackPanel>
        </HierarchicalDataTemplate>

        <!-- Connection template (flat DataTemplate) -->
        <DataTemplate DataType="{x:Type vm:ConnectionTreeItemViewModel}">
            <StackPanel Orientation="Horizontal" Height="28" Margin="4,2">
                <ui:SymbolIcon Symbol="Desktop24" FontSize="16" Margin="0,0,4,0" />
                <TextBlock Text="{Binding Name}" FontSize="14"
                           VerticalAlignment="Center" />
            </StackPanel>
        </DataTemplate>
    </TreeView.Resources>

    <!-- ItemContainerStyle for selection binding -->
    <TreeView.ItemContainerStyle>
        <Style TargetType="TreeViewItem" BasedOn="{StaticResource {x:Type TreeViewItem}}">
            <Setter Property="IsSelected" Value="{Binding IsSelected, Mode=TwoWay}" />
            <Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}" />
        </Style>
    </TreeView.ItemContainerStyle>
</TreeView>
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| BinaryFormatter for DnD DataObject | JSON serialization or direct ViewModel refs | .NET 9 (Nov 2024) | Must use in-process ViewModel references for drag-drop, not serialized DataObject with custom types |
| ContentPresenter for dialog host | ContentDialogHost element | WPF-UI 4.2.0 (Jan 2026) | Use `ui:ContentDialogHost` instead of `ContentPresenter` in MainWindow.xaml (already done in Phase 2) |
| SecureString for credentials | Plain string + Windows Credential Manager | .NET 7+ (Microsoft DE0001) | SecureString is deprecated; use plain strings in memory, store via OS credential store |
| MultiSelectTreeView NuGet | ViewModel-based selection tracking | Repo archived May 2022 | No maintained third-party multi-select TreeView for WPF; must implement via ViewModel |

**Deprecated/outdated:**
- `ContentDialog(ContentPresenter)` constructor is deprecated in WPF-UI 4.2.0 -- use `ContentDialog(ContentDialogHost)` [VERIFIED: WPF-UI API deprecation notices]
- `BinaryFormatter` removed from .NET runtime -- drag-drop custom types must use alternative serialization [VERIFIED: Microsoft migration guide]
- `SecureString` deprecated (DE0001) -- do not use for credential handling [CITED: REFERENCE.md]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | FocusExtension attached behavior needed for auto-focusing TextBox on F2 rename | Architecture Patterns (Pattern 6) | Low -- standard WPF technique; may need `FocusBehavior` from CommunityToolkit or custom implementation |
| A2 | TreeView VirtualizingPanel.IsVirtualizing="False" is acceptable for typical connection counts (< 1000) | Common Pitfalls (Pitfall 6) | Low -- if user has 1000+ connections, scrolling performance could degrade. Can re-enable virtualization later with ViewModel-based range calculation. |
| A3 | ContentDialog(ContentDialogHost) constructor exists in WPF-UI 4.2.0 | Architecture Patterns (Pattern 5) | Medium -- if only ContentPresenter constructor exists, must use deprecated path or service-based approach. Need to verify at implementation time. |
| A4 | AdysTech RemoveCredentials throws when target not found (vs returning false) | Code Examples | Low -- catch block handles both cases. Verify actual behavior at implementation time. |

## Open Questions

1. **ContentDialog constructor overload for ContentDialogHost**
   - What we know: WPF-UI 4.2.0 deprecated ContentPresenter-based dialog host in favor of ContentDialogHost. SetDialogHost already works with ContentDialogHost.
   - What's unclear: Whether the custom ContentDialog constructor `ContentDialog(ContentDialogHost)` exists, or if custom dialogs must use a different pattern (e.g., `IContentDialogService.ShowAsync<T>()`).
   - Recommendation: At implementation time, inspect the WPF-UI 4.2.0 API. If the constructor doesn't exist, use `new ContentDialog(contentDialogService.GetDialogHost())` or create the dialog and call `ShowAsync()` directly.

2. **PasswordBox inside ContentDialog keyboard navigation**
   - What we know: PasswordBox is a standard WPF control, auto-styled by WPF-UI ControlsDictionary. Tab navigation should work.
   - What's unclear: Whether Tab from the last field in a TabItem correctly moves to the next TabItem's first field inside a ContentDialog.
   - Recommendation: Test during implementation. If navigation breaks, add `KeyboardNavigation.TabNavigation="Cycle"` on each TabItem's content panel.

3. **Drag-drop adorner rendering inside WPF-UI styled TreeView**
   - What we know: AdornerLayer is a standard WPF mechanism. WPF-UI auto-styles TreeView but doesn't modify the adorner layer.
   - What's unclear: Whether the WPF-UI dark theme affects adorner visibility (transparency, z-order).
   - Recommendation: Use `SystemAccentColorPrimaryBrush` for the drop indicator line to ensure visibility against the dark background.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | Build/run | Yes | 10.0.x | -- |
| AdysTech.CredentialManager NuGet | CONN-07 credential storage | Not yet added | 3.1.0 (target) | -- (must add to Directory.Packages.props) |
| Windows Credential Manager (OS) | CONN-07 credential storage | Yes | OS-level | -- |
| %AppData% filesystem | CONN-03 JSON persistence | Yes | -- | -- |

**Missing dependencies with no fallback:**
- AdysTech.CredentialManager NuGet package must be added to the project (not yet referenced)

**Missing dependencies with fallback:**
- None

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3 (3.2.x) + FluentAssertions 8.9.x + NSubstitute 5.3.x |
| Config file | tests/Deskbridge.Tests/Deskbridge.Tests.csproj (OutputType: Exe, TestingPlatformDotnetTestSupport) |
| Quick run command | `dotnet test tests/Deskbridge.Tests/ -x` |
| Full suite command | `dotnet test tests/Deskbridge.Tests/` |

### Phase Requirements to Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CONN-01 | ConnectionModel has all required fields | unit | `dotnet test tests/Deskbridge.Tests/ --filter "FullyQualifiedName~ConnectionModel" -x` | Exists (Phase 1 implicitly) |
| CONN-02 | ConnectionGroup supports nesting via ParentGroupId | unit | `dotnet test tests/Deskbridge.Tests/ --filter "FullyQualifiedName~ConnectionGroup" -x` | Wave 0 |
| CONN-03 | JSON persistence with atomic writes | unit | `dotnet test tests/Deskbridge.Tests/ --filter "FullyQualifiedName~JsonConnectionStore" -x` | Wave 0 |
| CONN-04 | Tree renders groups/connections, context menu works | manual-only | Manual: verify tree rendering, context menu items | N/A (UI) |
| CONN-05 | Editor dialog opens with 4 tabs, saves correctly | manual-only | Manual: open editor, verify tabs, save | N/A (UI) |
| CONN-06 | Group editor saves group credentials | unit + manual | `dotnet test tests/Deskbridge.Tests/ --filter "FullyQualifiedName~GroupEditor" -x` | Wave 0 |
| CONN-07 | Credentials stored via AdysTech (TERMSRV + DESKBRIDGE/GROUP targets) | integration | `dotnet test tests/Deskbridge.Tests/ --filter "FullyQualifiedName~CredentialService" -x` | Wave 0 |
| CONN-08 | Credential inheritance resolves recursively up group tree | unit | `dotnet test tests/Deskbridge.Tests/ --filter "FullyQualifiedName~CredentialInheritance" -x` | Wave 0 |
| CONN-09 | Editor shows "inherited from" InfoBar | manual-only | Manual: set CredentialMode=Inherit, verify InfoBar shows group name | N/A (UI) |
| CONN-10 | Groups display key icon when credentials set | manual-only | Manual: set group credentials, verify key icon appears | N/A (UI) |

### Sampling Rate
- **Per task commit:** `dotnet test tests/Deskbridge.Tests/ -x` (fail-fast)
- **Per wave merge:** `dotnet test tests/Deskbridge.Tests/` (full suite)
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/Deskbridge.Tests/JsonConnectionStoreTests.cs` -- covers CONN-03 (persistence, atomic write, load/save, schema version)
- [ ] `tests/Deskbridge.Tests/WindowsCredentialServiceTests.cs` -- covers CONN-07 (store/get/delete for both connection and group targets)
- [ ] `tests/Deskbridge.Tests/CredentialInheritanceTests.cs` -- covers CONN-08 (recursive group walk, no-credentials-found case, multiple levels)
- [ ] `tests/Deskbridge.Tests/ConnectionQueryServiceStoreTests.cs` -- covers CR-01 fix (ConnectionQueryService delegates to IConnectionStore)

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No (master password is Phase 6) | -- |
| V3 Session Management | No (RDP sessions are Phase 4/5) | -- |
| V4 Access Control | No (single-user desktop app) | -- |
| V5 Input Validation | Yes | ObservableValidator with [Required], numeric range validation for Port (1-65535) |
| V6 Cryptography | Partial | Windows Credential Manager handles credential encryption (OS-level DPAPI). No custom crypto. |

### Known Threat Patterns for Connection Management

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Password leakage to JSON file | Information Disclosure | Never store passwords in connections.json. CredentialMode enum only. [CITED: REFERENCE.md constraint #11] |
| Password leakage to logs | Information Disclosure | Never log NetworkCredential values. ResolvedPassword XML doc warns against logging. [CITED: Phase 1 decisions] |
| Credential target collision | Tampering | Use unique target prefixes: TERMSRV/ for connections, DESKBRIDGE/GROUP/ for groups |
| connections.json corruption | Denial of Service | Atomic write pattern (temp+rename). Load failure shows InfoBar warning, starts with empty list. |
| Unauthorized credential deletion | Tampering | Windows Credential Manager is protected by OS user session. No additional auth needed for desktop app. |

## Project Constraints (from CLAUDE.md)

- **Framework**: .NET 10 LTS (net10.0-windows) with C# 14
- **UI Library**: WPF-UI (Fluent dark theme) -- all colours via DynamicResource tokens, BasedOn for style overrides
- **Credentials**: AdysTech.CredentialManager only -- no CredentialManagement NuGet, no SecureString
- **Serialisation**: System.Text.Json only -- no XML config, no SQLite
- **Security**: Never store passwords in JSON or logs
- **MVVM**: CommunityToolkit.Mvvm with [ObservableProperty], [RelayCommand]
- **DI**: Microsoft.Extensions.DependencyInjection as composition root in App.xaml.cs
- **Testing**: xUnit v3, FluentAssertions, NSubstitute
- **GSD Workflow**: Use GSD commands for all repo changes

## Sources

### Primary (HIGH confidence)
- [AdysTech/CredentialManager GitHub](https://github.com/AdysTech/CredentialManager) - Full API signatures verified from source code
- [AdysTech.CredentialManager NuGet 3.1.0](https://www.nuget.org/packages/AdysTech.CredentialManager/) - Version and compatibility confirmed
- [WPF-UI ContentDialog API](https://wpfui.lepo.co/api/Wpf.Ui.Controls.ContentDialog.html) - Properties, methods, ContentDialogHost pattern
- [WPF-UI InfoBar API](https://wpfui.lepo.co/api/Wpf.Ui.Controls.InfoBar.html) - Severity enum, IsOpen, IsClosable
- [WPF-UI GitHub Discussion #726](https://github.com/lepoco/wpfui/discussions/726) - ContentDialog tutorial and usage patterns
- [WPF-UI GitHub Discussion #848](https://github.com/lepoco/wpfui/discussions/848) - Custom ContentDialog implementation examples
- [Microsoft: BinaryFormatter WPF OLE Guidance](https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-migration-guide/winforms-wpf-ole-guidance) - Drag-drop migration from BinaryFormatter
- [Microsoft: File.Move API](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move) - Overwrite parameter and atomicity
- REFERENCE.md - ICredentialService interface, credential targets, inheritance walk
- DESIGN.md - WPF-UI ContentDialog, TreeView, InfoBar patterns
- Existing codebase - ConnectionModel.cs, ConnectionGroup.cs, IConnectionStore.cs, MainWindow.xaml, App.xaml.cs

### Secondary (MEDIUM confidence)
- [Atomic File Writes on Windows](https://antonymale.co.uk/windows-atomic-file-writes.html) - NTFS rename atomicity analysis
- [TheITBros: TERMSRV Credentials](https://theitbros.com/enable-saved-credentials-usage-rdp/) - Generic vs DomainPassword for RDP credentials
- [ygoe/MultiSelectTreeView GitHub](https://github.com/ygoe/MultiSelectTreeView) - Archived status confirmed (May 2022)
- [CodeProject: Editable TextBlock](https://www.codeproject.com/Articles/31592/Editable-TextBlock-in-WPF-for-In-place-Editing) - F2 rename pattern
- [Dirkster99/InplaceEditBoxLib GitHub](https://github.com/Dirkster99/InplaceEditBoxLib) - F2 rename library reference

### Tertiary (LOW confidence)
- [DeepWiki: WPF-UI Dialog Controls](https://deepwiki.com/lepoco/wpfui/4.4-dialog-controls) - ContentDialog OnButtonClick override pattern (needs implementation verification)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All packages verified via NuGet/GitHub, versions confirmed
- Architecture: HIGH - Patterns derived from existing codebase + verified WPF-UI docs + Microsoft docs
- Pitfalls: HIGH - All major pitfalls verified against official documentation or confirmed behavior
- Multi-select implementation: MEDIUM - ViewModel approach is standard but specific attached behavior code needs implementation verification
- ContentDialog custom constructor: MEDIUM - ContentDialogHost constructor pattern needs verification at implementation time

**Research date:** 2026-04-11
**Valid until:** 2026-05-11 (stable stack, 30 days)
