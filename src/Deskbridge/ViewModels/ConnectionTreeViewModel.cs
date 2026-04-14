using System.Collections.ObjectModel;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace Deskbridge.ViewModels;

public enum DropPosition
{
    Before,
    Into,
    After
}

public partial class ConnectionTreeViewModel : ObservableObject
{
    private readonly IConnectionStore _connectionStore;
    private readonly IConnectionQuery _connectionQuery;
    private readonly ICredentialService _credentialService;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBus _eventBus;
    private readonly ITabHostManager _tabHostManager;

    // Cached full tree for restoring after search filter clears
    private ObservableCollection<TreeItemViewModel> _fullTree = [];

    // Guard against concurrent dialog opens (ShowAsync throws if host is already busy)
    private bool _isDialogOpen;

    public ConnectionTreeViewModel(
        IConnectionStore connectionStore,
        IConnectionQuery connectionQuery,
        ICredentialService credentialService,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService,
        IServiceProvider serviceProvider,
        IEventBus eventBus,
        ITabHostManager tabHostManager)
    {
        _connectionStore = connectionStore;
        _connectionQuery = connectionQuery;
        _credentialService = credentialService;
        _contentDialogService = contentDialogService;
        _snackbarService = snackbarService;
        _serviceProvider = serviceProvider;
        _eventBus = eventBus;
        _tabHostManager = tabHostManager;
    }

    // Data
    [ObservableProperty]
    public partial ObservableCollection<TreeItemViewModel> RootItems { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<TreeItemViewModel> SelectedItems { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsGroupSelected))]
    [NotifyPropertyChangedFor(nameof(SelectedItemCredentialMode))]
    [NotifyPropertyChangedFor(nameof(IsQuickCredentialFieldsVisible))]
    [NotifyPropertyChangedFor(nameof(IsQuickCredentialFieldsEnabled))]
    [NotifyPropertyChangedFor(nameof(IsQuickPasswordVisible))]
    public partial TreeItemViewModel? PrimarySelectedItem { get; set; }

    /// <summary>
    /// Bridge property for the quick properties CredentialMode ComboBox.
    /// Binding directly to <c>PrimarySelectedItem.CredentialMode</c> caused the
    /// ComboBox's SelectionBoxItem to render placeholder glyphs ("- - -") when
    /// collapsed; the ViewModel-owned bridge mirrors the pattern used by the
    /// editor dialog ComboBox and renders the selected display text correctly.
    /// </summary>
    public CredentialMode SelectedItemCredentialMode
    {
        get => (PrimarySelectedItem as ConnectionTreeItemViewModel)?.CredentialMode
            ?? CredentialMode.Inherit;
        set
        {
            if (PrimarySelectedItem is ConnectionTreeItemViewModel item && item.CredentialMode != value)
            {
                item.CredentialMode = value;
                SaveConnectionFromQuickEdit(item);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsQuickCredentialFieldsVisible));
                OnPropertyChanged(nameof(IsQuickCredentialFieldsEnabled));
                OnPropertyChanged(nameof(IsQuickPasswordVisible));
            }
        }
    }

    /// <summary>
    /// True only when CredentialMode is Own — password field in quick properties
    /// is only shown in Own mode since showing empty/disabled password fields in
    /// Inherit/Prompt modes is confusing.
    /// </summary>
    public bool IsQuickPasswordVisible =>
        SelectedItemCredentialMode == CredentialMode.Own;

    /// <summary>
    /// True when the current selection's CredentialMode is Inherit or Own (i.e.
    /// the Username/Domain fields should be visible). Prompt hides them because
    /// credentials are requested at connect time.
    /// </summary>
    public bool IsQuickCredentialFieldsVisible =>
        SelectedItemCredentialMode != CredentialMode.Prompt;

    /// <summary>
    /// True only when the current selection's CredentialMode is Own (i.e. the
    /// Username/Domain fields should be editable). In Inherit mode the fields
    /// are visible but disabled, matching the editor dialog behavior.
    /// </summary>
    public bool IsQuickCredentialFieldsEnabled =>
        SelectedItemCredentialMode == CredentialMode.Own;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsQuickPropertiesExpanded { get; set; } = true;

    // Display-friendly options for the CredentialMode ComboBox in the quick properties
    // panel. Raw Enum.GetValues() binding rendered as placeholder glyphs ("- - -") under
    // the WPF-UI ComboBox template, so we expose typed display wrappers. See
    // ConnectionEditorViewModel.CredentialModes for the same pattern.
    public IReadOnlyList<CredentialModeOption> CredentialModes { get; } = new[]
    {
        new CredentialModeOption(CredentialMode.Inherit, "Inherit"),
        new CredentialModeOption(CredentialMode.Own, "Own"),
        new CredentialModeOption(CredentialMode.Prompt, "Prompt"),
    };

    // Quick properties (computed from PrimarySelectedItem)
    public bool IsConnectionSelected => PrimarySelectedItem is ConnectionTreeItemViewModel;
    public bool IsGroupSelected => PrimarySelectedItem is GroupTreeItemViewModel;

    // --- Tree building ---

    public void LoadTree()
    {
        BuildTree();
    }

    public void RefreshTree()
    {
        BuildTree();
    }

    private void BuildTree()
    {
        var connections = _connectionStore.GetAll();
        var groups = _connectionStore.GetGroups();

        // Build group lookup: GroupId -> GroupTreeItemViewModel
        var groupMap = new Dictionary<Guid, GroupTreeItemViewModel>();
        foreach (var group in groups)
        {
            groupMap[group.Id] = new GroupTreeItemViewModel
            {
                Id = group.Id,
                Name = group.Name,
                ParentGroupId = group.ParentGroupId,
                SortOrder = group.SortOrder,
                IsExpanded = true,
                HasCredentials = _credentialService.HasGroupCredentials(group.Id)
            };
        }

        // Build connection ViewModels
        var connectionVms = new List<ConnectionTreeItemViewModel>();
        foreach (var conn in connections)
        {
            connectionVms.Add(new ConnectionTreeItemViewModel
            {
                Id = conn.Id,
                Name = conn.Name,
                Hostname = conn.Hostname,
                Port = conn.Port,
                Username = conn.Username,
                Domain = conn.Domain,
                CredentialMode = conn.CredentialMode,
                GroupId = conn.GroupId,
                SortOrder = conn.SortOrder
            });
        }

        // Nest groups into parent groups. Detect cycles (hand-edited JSON with
        // A.Parent=A or A->B->A loops) by walking each group's parent chain
        // before deciding its placement. A group participating in a cycle is
        // promoted to root so it remains reachable in the UI.
        var rootItems = new ObservableCollection<TreeItemViewModel>();
        foreach (var kvp in groupMap)
        {
            var groupVm = kvp.Value;
            bool participatesInCycle = false;
            if (groupVm.ParentGroupId is not null)
            {
                var visited = new HashSet<Guid> { groupVm.Id };
                var cursor = groupVm.ParentGroupId;
                while (cursor is not null)
                {
                    if (!visited.Add(cursor.Value))
                    {
                        participatesInCycle = true;
                        Serilog.Log.Warning("Cycle detected in group parent chain at {GroupId}; promoting to root", groupVm.Id);
                        break;
                    }
                    if (!groupMap.TryGetValue(cursor.Value, out var next))
                        break;
                    cursor = next.ParentGroupId;
                }
            }

            if (!participatesInCycle
                && groupVm.ParentGroupId is not null
                && groupMap.TryGetValue(groupVm.ParentGroupId.Value, out var parentGroup))
            {
                parentGroup.Children.Add(groupVm);
            }
            else
            {
                rootItems.Add(groupVm);
            }
        }

        // Place connections into their groups or root
        foreach (var connVm in connectionVms)
        {
            if (connVm.GroupId is not null && groupMap.TryGetValue(connVm.GroupId.Value, out var parentGroup))
            {
                parentGroup.Children.Add(connVm);
            }
            else
            {
                rootItems.Add(connVm);
            }
        }

        // Sort siblings by SortOrder ascending (groups and connections sort alongside each other).
        // Tiebreaker on Name keeps deterministic ordering when SortOrder values collide (e.g. zero-init).
        SortSiblings(rootItems);
        foreach (var kvp in groupMap)
        {
            SortSiblings(kvp.Value.Children);
        }

        _fullTree = rootItems;
        RootItems = new ObservableCollection<TreeItemViewModel>(rootItems);
    }

    private static void SortSiblings(ObservableCollection<TreeItemViewModel> siblings)
    {
        var sorted = siblings
            .OrderBy(GetSortOrder)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        siblings.Clear();
        foreach (var item in sorted)
            siblings.Add(item);
    }

    private static int GetSortOrder(TreeItemViewModel item) => item switch
    {
        ConnectionTreeItemViewModel c => c.SortOrder,
        GroupTreeItemViewModel g => g.SortOrder,
        _ => 0
    };

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

    private static void FlattenAndFilter(
        IEnumerable<TreeItemViewModel> items,
        string searchLower,
        ObservableCollection<TreeItemViewModel> results)
    {
        foreach (var item in items)
        {
            if (item is ConnectionTreeItemViewModel conn)
            {
                if (conn.Name.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                    conn.Hostname.Contains(searchLower, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(conn);
                }
            }
            else if (item is GroupTreeItemViewModel group)
            {
                if (group.Name.Contains(searchLower, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(group);
                }
                // Always recurse into children for connections inside groups
                FlattenAndFilter(group.Children, searchLower, results);
            }
        }
    }

    // --- Quick properties inline edit ---

    public void SaveConnectionFromQuickEdit(ConnectionTreeItemViewModel connVm)
    {
        var hostname = connVm.Hostname?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(hostname))
            return;

        // Validate port range (T-03-07)
        if (connVm.Port < 1 || connVm.Port > 65535)
            return;

        var model = _connectionStore.GetById(connVm.Id);
        if (model is null)
            return;

        model.Name = connVm.Name;
        model.Hostname = hostname;
        model.Port = connVm.Port;
        model.Username = connVm.Username;
        model.Domain = connVm.Domain;
        model.CredentialMode = connVm.CredentialMode;
        model.UpdatedAt = DateTime.UtcNow;
        _connectionStore.Save(model);
    }

    /// <summary>
    /// Persist a password typed into the quick properties PasswordBox. Only
    /// applies when the selection is a connection in Own mode; skips empty
    /// passwords so a stray LostFocus doesn't overwrite stored credentials.
    /// </summary>
    public void SaveQuickPassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return;
        if (PrimarySelectedItem is not ConnectionTreeItemViewModel connVm) return;
        if (connVm.CredentialMode != CredentialMode.Own) return;

        var model = _connectionStore.GetById(connVm.Id);
        if (model is null) return;

        // Matches ConnectionEditorViewModel.Save (line 217-224): credential-store
        // failures here are reached via PasswordBox.LostFocus → Dispatcher, and an
        // unhandled CredentialAPIException on that path terminates the process
        // (observed in Windows Event Log as 0xe0434352 CLR fatal). Quick-props is a
        // convenience path — log and swallow; the user can retry via the editor.
        try
        {
            _credentialService.StoreForConnection(
                model,
                connVm.Username ?? string.Empty,
                connVm.Domain,
                password);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to store credentials for connection {ConnectionId} via quick-properties", model.Id);
        }
    }

    public void SaveGroupFromQuickEdit(GroupTreeItemViewModel groupVm)
    {
        var name = groupVm.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return;

        var group = _connectionStore.GetGroupById(groupVm.Id);
        if (group is null)
            return;

        group.Name = name;
        _connectionStore.SaveGroup(group);
    }

    // --- Move-to group list for context menu ---

    /// <summary>
    /// Returns flat list of (Guid id, string displayName, int depth) for the "Move to..." submenu.
    /// </summary>
    public List<(Guid Id, string DisplayName, int Depth)> GetAvailableGroupsForMove()
    {
        var groups = _connectionStore.GetGroups();
        var result = new List<(Guid Id, string DisplayName, int Depth)>();

        // Build parent->children lookup
        var childLookup = new Dictionary<string, List<ConnectionGroup>>();
        foreach (var g in groups)
        {
            var parentKey = g.ParentGroupId?.ToString() ?? string.Empty;
            if (!childLookup.ContainsKey(parentKey))
                childLookup[parentKey] = [];
            childLookup[parentKey].Add(g);
        }

        // Cycle-safe walk: hand-edited JSON can contain A.Parent=A or A->B->A loops
        // (see WPF-TREEVIEW-PATTERNS.md — always operate on model with cycle guard).
        var visited = new HashSet<Guid>();

        void WalkGroups(Guid? parentId, int depth)
        {
            var key = parentId?.ToString() ?? string.Empty;
            if (!childLookup.TryGetValue(key, out var children))
                return;

            foreach (var child in children.OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
            {
                if (!visited.Add(child.Id))
                {
                    Serilog.Log.Warning("Cycle detected in group parent chain at {GroupId}", child.Id);
                    continue;
                }
                result.Add((child.Id, child.Name, depth));
                WalkGroups(child.Id, depth + 1);
            }
        }

        WalkGroups(null, 0);
        return result;
    }

    // --- Commands ---

    [RelayCommand]
    private async Task NewConnectionAsync(Guid? defaultGroupId = null)
    {
        if (_isDialogOpen) return;

        _isDialogOpen = true;
        try
        {
            var host = _contentDialogService.GetDialogHostEx();
            if (host is null)
            {
                Serilog.Log.Error("ContentDialogHost is null; cannot show New Connection dialog");
                return;
            }

            var vm = _serviceProvider.GetRequiredService<ConnectionEditorViewModel>();
            vm.Initialize();

            // Pre-select the group when invoked from a group context menu
            if (defaultGroupId is not null)
            {
                vm.GroupId = defaultGroupId;
            }

            var dialog = new ConnectionEditorDialog(host, vm);
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                vm.Save();
                RefreshTree();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to show New Connection dialog");
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    [RelayCommand]
    private async Task NewGroupAsync()
    {
        if (_isDialogOpen) return;

        _isDialogOpen = true;
        try
        {
            var host = _contentDialogService.GetDialogHostEx();
            if (host is null)
            {
                Serilog.Log.Error("ContentDialogHost is null; cannot show New Group dialog");
                return;
            }

            var vm = _serviceProvider.GetRequiredService<GroupEditorViewModel>();
            vm.Initialize();

            var dialog = new GroupEditorDialog(host, vm);
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                vm.Save();
                RefreshTree();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to show New Group dialog");
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    [RelayCommand]
    private async Task EditItemAsync(TreeItemViewModel? item)
    {
        if (item is null) return;
        if (_isDialogOpen) return;

        _isDialogOpen = true;
        try
        {
            var host = _contentDialogService.GetDialogHostEx();
            if (host is null)
            {
                Serilog.Log.Error("ContentDialogHost is null; cannot show Edit dialog");
                return;
            }

            if (item is ConnectionTreeItemViewModel connItem)
            {
                var existing = _connectionStore.GetById(connItem.Id);
                if (existing is null) return;

                var vm = _serviceProvider.GetRequiredService<ConnectionEditorViewModel>();
                vm.Initialize(existing);

                var dialog = new ConnectionEditorDialog(host, vm);
                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    vm.Save();
                    RefreshTree();
                }
            }
            else if (item is GroupTreeItemViewModel groupItem)
            {
                var existing = _connectionStore.GetGroupById(groupItem.Id);
                if (existing is null) return;

                var vm = _serviceProvider.GetRequiredService<GroupEditorViewModel>();
                vm.Initialize(existing);

                var dialog = new GroupEditorDialog(host, vm);
                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    vm.Save();
                    RefreshTree();
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to show Edit dialog");
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedItems.Count == 0) return;
        if (_isDialogOpen) return;

        // Snapshot selected items (Pitfall 7: collection may change during async)
        var itemsToDelete = SelectedItems.ToList();

        // Build confirmation message per UI-SPEC copywriting contract
        string title;
        string content;

        if (itemsToDelete.Count == 1)
        {
            var single = itemsToDelete[0];
            if (single is GroupTreeItemViewModel groupVm)
            {
                var connectionCount = groupVm.ConnectionCount;
                title = "Delete group?";
                content = $"Are you sure you want to delete the group {single.Name} and all {connectionCount} connections inside it? This action cannot be undone.";
            }
            else
            {
                title = "Delete connection?";
                content = $"Are you sure you want to delete {single.Name}? This action cannot be undone.";
            }
        }
        else
        {
            title = $"Delete {itemsToDelete.Count} items?";
            content = $"Are you sure you want to delete {itemsToDelete.Count} selected items? This action cannot be undone.";
        }

        // Show confirmation dialog
        ContentDialogResult result;
        _isDialogOpen = true;
        try
        {
            result = await _contentDialogService.ShowSimpleDialogAsync(
                new SimpleContentDialogCreateOptions
                {
                    Title = title,
                    Content = content,
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel"
                });
        }
        finally
        {
            _isDialogOpen = false;
        }

        if (result != ContentDialogResult.Primary) return;

        // Delete confirmed - execute deletions
        foreach (var item in itemsToDelete)
        {
            if (item is ConnectionTreeItemViewModel connItem)
            {
                var model = _connectionStore.GetById(connItem.Id);
                if (model is not null)
                {
                    _credentialService.DeleteForConnection(model);
                }
                _connectionStore.Delete(connItem.Id);
            }
            else if (item is GroupTreeItemViewModel groupItem)
            {
                // T-03-15: DeleteGroup orphans connections (sets GroupId=null), does not delete them
                _credentialService.DeleteForGroup(groupItem.Id);
                _connectionStore.DeleteGroup(groupItem.Id);
            }
        }

        SelectedItems.Clear();
        PrimarySelectedItem = null;
        RefreshTree();
    }

    [RelayCommand]
    private void ToggleQuickProperties()
    {
        IsQuickPropertiesExpanded = !IsQuickPropertiesExpanded;
    }

    [RelayCommand]
    private void Connect(TreeItemViewModel? item = null)
    {
        // Phase 4 (RDP-05): publish ConnectionRequestedEvent. ConnectionCoordinator subscribes,
        // marshals to STA (D-11), and runs IConnectionPipeline.ConnectAsync. Failures surface
        // via ConnectionFailedEvent on the bus — do not call IConnectionPipeline directly here.
        var target = item as ConnectionTreeItemViewModel
                     ?? PrimarySelectedItem as ConnectionTreeItemViewModel;
        if (target is null) return;
        var model = _connectionStore.GetById(target.Id);
        if (model is null) return;

        // D-02 (Phase 5): switch to existing tab if one is already open. Publisher-side
        // check breaks the would-be circular DI between ConnectionCoordinator and
        // ITabHostManager (Pitfall 5 — both are singletons, ctor-injecting each other
        // throws CircularDependency). The duplicate-click guard previously at
        // ConnectionCoordinator.OnConnectionRequested was deleted in the same Phase 5
        // refactor; this is the single chokepoint that owns switch-to-existing.
        if (_tabHostManager.TryGetExistingTab(model.Id, out _))
        {
            _tabHostManager.SwitchTo(model.Id);
            return;
        }

        _eventBus.Publish(new ConnectionRequestedEvent(model));
    }

    [RelayCommand]
    private void CopyHostname(ConnectionTreeItemViewModel? item)
    {
        if (item is null) return;

        Clipboard.SetText(item.Hostname);
        _snackbarService.Show(
            "Copied",
            "Hostname copied to clipboard",
            ControlAppearance.Info,
            null,
            TimeSpan.FromSeconds(2));
    }

    [RelayCommand]
    private void DuplicateConnection(ConnectionTreeItemViewModel? item)
    {
        if (item is null) return;

        var original = _connectionStore.GetById(item.Id);
        if (original is null) return;

        // Create copy with new Guid and "(Copy)" suffix (T-03-14: do NOT copy credentials)
        var copy = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = $"{original.Name} (Copy)",
            Hostname = original.Hostname,
            Port = original.Port,
            Username = original.Username,
            Domain = original.Domain,
            Protocol = original.Protocol,
            GroupId = original.GroupId,
            Notes = original.Notes,
            CredentialMode = CredentialMode.Inherit, // Default to Inherit, user must set own creds
            DisplaySettings = original.DisplaySettings is not null
                ? new DisplaySettings
                {
                    Width = original.DisplaySettings.Width,
                    Height = original.DisplaySettings.Height,
                    SmartSizing = original.DisplaySettings.SmartSizing
                }
                : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _connectionStore.Save(copy);
        RefreshTree();
    }

    [RelayCommand]
    private void MoveToGroup(Guid? targetGroupId)
    {
        if (SelectedItems.Count == 0) return;

        // Snapshot items (collection may change during iteration)
        var items = SelectedItems.ToList();

        foreach (var item in items)
        {
            if (item is ConnectionTreeItemViewModel connItem)
            {
                var model = _connectionStore.GetById(connItem.Id);
                if (model is not null)
                {
                    model.GroupId = targetGroupId;
                    model.UpdatedAt = DateTime.UtcNow;
                    _connectionStore.Save(model);
                }
            }
            else if (item is GroupTreeItemViewModel groupItem)
            {
                // Prevent moving a group into itself or its own descendant (circular reference)
                if (targetGroupId is not null && IsDescendantOrSelf(groupItem, targetGroupId.Value))
                    continue;

                var group = _connectionStore.GetGroupById(groupItem.Id);
                if (group is not null)
                {
                    group.ParentGroupId = targetGroupId;
                    _connectionStore.SaveGroup(group);
                }
            }
        }

        RefreshTree();
    }

    /// <summary>
    /// Append dragged items to the root level (ParentGroupId = null / GroupId = null).
    /// Used when the user drops onto empty tree area (past the last item), which the
    /// DragOver handler surfaces as a null drop target.
    /// </summary>
    public void MoveItemsToRoot(IReadOnlyList<TreeItemViewModel> draggedItems)
    {
        if (draggedItems.Count == 0) return;

        // Place items at the end of the root level, each with SortOrder past the
        // current maximum so they land after existing root entries.
        var rootSiblings = GetSiblingsAtLevel(null);
        int nextSort = (rootSiblings.Count == 0 ? 0 : rootSiblings.Max(s => s.sortOrder)) + 10;

        foreach (var item in draggedItems)
        {
            ApplyMove(item, newGroupId: null, sortOrder: nextSort);
            nextSort += 10;
        }
        RefreshTree();
    }

    /// <summary>
    /// Reorder dragged items relative to a target. <paramref name="position"/> controls placement:
    /// Before inserts dragged items just before target, After inserts after target, Into drops into
    /// target (only valid if target is a group).
    /// </summary>
    public void ReorderItems(
        IReadOnlyList<TreeItemViewModel> draggedItems,
        TreeItemViewModel target,
        DropPosition position)
    {
        if (draggedItems.Count == 0) return;

        // "Into" a group is equivalent to the existing MoveToGroup semantics.
        if (position == DropPosition.Into)
        {
            if (target is not GroupTreeItemViewModel targetGroup) return;

            foreach (var item in draggedItems)
            {
                ApplyMove(item, targetGroup.Id, sortOrder: null);
            }
            RefreshTree();
            return;
        }

        // Determine new parent (the group that the target sits inside) and compute SortOrder
        // midway between target and its adjacent sibling based on drop position.
        Guid? newParentId;
        int targetSortOrder;
        if (target is ConnectionTreeItemViewModel targetConn)
        {
            newParentId = targetConn.GroupId;
            targetSortOrder = targetConn.SortOrder;
        }
        else if (target is GroupTreeItemViewModel targetGrp)
        {
            newParentId = targetGrp.ParentGroupId;
            targetSortOrder = targetGrp.SortOrder;
        }
        else return;

        // Collect all siblings at the target's level (connections + groups), sorted by SortOrder.
        var siblings = GetSiblingsAtLevel(newParentId)
            .OrderBy(s => s.sortOrder)
            .ToList();

        int targetIndex = siblings.FindIndex(s => s.id == target.Id);
        if (targetIndex < 0) return;

        int newSortOrder;
        if (position == DropPosition.Before)
        {
            int prevSort = targetIndex > 0 ? siblings[targetIndex - 1].sortOrder : targetSortOrder - 20;
            newSortOrder = (prevSort + targetSortOrder) / 2;
            // Collision fallback: when neighbouring values are identical or only 1 apart,
            // bump everything after so integer midpoints stay unique.
            if (newSortOrder == prevSort || newSortOrder == targetSortOrder)
            {
                RebaseSortOrders(newParentId);
                return;
            }
        }
        else // After
        {
            int nextSort = targetIndex < siblings.Count - 1 ? siblings[targetIndex + 1].sortOrder : targetSortOrder + 20;
            newSortOrder = (targetSortOrder + nextSort) / 2;
            if (newSortOrder == targetSortOrder || newSortOrder == nextSort)
            {
                RebaseSortOrders(newParentId);
                return;
            }
        }

        foreach (var item in draggedItems)
        {
            ApplyMove(item, newParentId, newSortOrder);
            // Bump slightly so multi-drag preserves order within the drop slot.
            newSortOrder += 1;
        }

        RefreshTree();
    }

    private void ApplyMove(TreeItemViewModel item, Guid? newGroupId, int? sortOrder)
    {
        if (item is ConnectionTreeItemViewModel connItem)
        {
            var model = _connectionStore.GetById(connItem.Id);
            if (model is null) return;
            model.GroupId = newGroupId;
            if (sortOrder is not null) model.SortOrder = sortOrder.Value;
            model.UpdatedAt = DateTime.UtcNow;
            _connectionStore.Save(model);
        }
        else if (item is GroupTreeItemViewModel groupItem)
        {
            // Prevent cycle: moving a group into itself or a descendant.
            if (newGroupId is not null && IsDescendantOrSelf(groupItem, newGroupId.Value))
                return;
            var group = _connectionStore.GetGroupById(groupItem.Id);
            if (group is null) return;
            group.ParentGroupId = newGroupId;
            if (sortOrder is not null) group.SortOrder = sortOrder.Value;
            _connectionStore.SaveGroup(group);
        }
    }

    private List<(Guid id, int sortOrder)> GetSiblingsAtLevel(Guid? parentId)
    {
        var result = new List<(Guid, int)>();
        foreach (var g in _connectionStore.GetGroups())
        {
            if (g.ParentGroupId == parentId)
                result.Add((g.Id, g.SortOrder));
        }
        foreach (var c in _connectionStore.GetAll())
        {
            if (c.GroupId == parentId)
                result.Add((c.Id, c.SortOrder));
        }
        return result;
    }

    /// <summary>
    /// When SortOrder integer midpoints collide (e.g. 5 and 6, midpoint = 5), rebuild the
    /// sibling sequence using gaps of 10 so future inserts have room. This is a rare path
    /// only triggered after many drags without rebase.
    /// </summary>
    private void RebaseSortOrders(Guid? parentId)
    {
        var siblings = GetSiblingsAtLevel(parentId)
            .OrderBy(s => s.sortOrder)
            .ToList();

        int order = 10;
        foreach (var (id, _) in siblings)
        {
            var g = _connectionStore.GetGroupById(id);
            if (g is not null)
            {
                g.SortOrder = order;
                _connectionStore.SaveGroup(g);
            }
            else
            {
                var c = _connectionStore.GetById(id);
                if (c is not null)
                {
                    c.SortOrder = order;
                    c.UpdatedAt = DateTime.UtcNow;
                    _connectionStore.Save(c);
                }
            }
            order += 10;
        }

        RefreshTree();
    }

    /// <summary>
    /// Returns true if <paramref name="targetGroupId"/> is the group itself or a descendant of it.
    /// Used to prevent circular group nesting.
    /// </summary>
    private static bool IsDescendantOrSelf(GroupTreeItemViewModel group, Guid targetGroupId)
    {
        if (group.Id == targetGroupId) return true;
        foreach (var child in group.Children)
        {
            if (child is GroupTreeItemViewModel childGroup && IsDescendantOrSelf(childGroup, targetGroupId))
                return true;
        }
        return false;
    }
}
