using System.Collections.ObjectModel;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace Deskbridge.ViewModels;

public partial class ConnectionTreeViewModel : ObservableObject
{
    private readonly IConnectionStore _connectionStore;
    private readonly IConnectionQuery _connectionQuery;
    private readonly ICredentialService _credentialService;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly IServiceProvider _serviceProvider;

    // Cached full tree for restoring after search filter clears
    private ObservableCollection<TreeItemViewModel> _fullTree = [];

    public ConnectionTreeViewModel(
        IConnectionStore connectionStore,
        IConnectionQuery connectionQuery,
        ICredentialService credentialService,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService,
        IServiceProvider serviceProvider)
    {
        _connectionStore = connectionStore;
        _connectionQuery = connectionQuery;
        _credentialService = credentialService;
        _contentDialogService = contentDialogService;
        _snackbarService = snackbarService;
        _serviceProvider = serviceProvider;
    }

    // Data
    [ObservableProperty]
    public partial ObservableCollection<TreeItemViewModel> RootItems { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<TreeItemViewModel> SelectedItems { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsGroupSelected))]
    public partial TreeItemViewModel? PrimarySelectedItem { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsQuickPropertiesVisible { get; set; } = true;

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
                CredentialMode = conn.CredentialMode,
                GroupId = conn.GroupId
            });
        }

        // Nest groups into parent groups
        var rootItems = new ObservableCollection<TreeItemViewModel>();
        foreach (var kvp in groupMap)
        {
            var groupVm = kvp.Value;
            if (groupVm.ParentGroupId is not null && groupMap.TryGetValue(groupVm.ParentGroupId.Value, out var parentGroup))
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

        _fullTree = rootItems;
        RootItems = new ObservableCollection<TreeItemViewModel>(rootItems);
    }

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
        model.CredentialMode = connVm.CredentialMode;
        model.UpdatedAt = DateTime.UtcNow;
        _connectionStore.Save(model);
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

        void WalkGroups(Guid? parentId, int depth)
        {
            var key = parentId?.ToString() ?? string.Empty;
            if (!childLookup.TryGetValue(key, out var children))
                return;

            foreach (var child in children.OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
            {
                result.Add((child.Id, child.Name, depth));
                WalkGroups(child.Id, depth + 1);
            }
        }

        WalkGroups(null, 0);
        return result;
    }

    // --- Commands ---

    [RelayCommand]
    private async Task NewConnectionAsync()
    {
        var vm = _serviceProvider.GetRequiredService<ConnectionEditorViewModel>();
        vm.Initialize();

        var host = _contentDialogService.GetDialogHostEx();
        if (host is null) return;

        var dialog = new ConnectionEditorDialog(host, vm);
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            vm.Save();
            RefreshTree();
        }
    }

    [RelayCommand]
    private async Task NewGroupAsync()
    {
        var vm = _serviceProvider.GetRequiredService<GroupEditorViewModel>();
        vm.Initialize();

        var host = _contentDialogService.GetDialogHostEx();
        if (host is null) return;

        var dialog = new GroupEditorDialog(host, vm);
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            vm.Save();
            RefreshTree();
        }
    }

    [RelayCommand]
    private async Task EditItemAsync(TreeItemViewModel? item)
    {
        if (item is null) return;

        var host = _contentDialogService.GetDialogHostEx();
        if (host is null) return;

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

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedItems.Count == 0) return;

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
        var result = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = title,
                Content = content,
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel"
            });

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
        IsQuickPropertiesVisible = !IsQuickPropertiesVisible;
    }

    [RelayCommand]
    private void Connect()
    {
        /* Phase 4/5 -- RDP connection will be wired here */
    }

    [RelayCommand]
    private void RenameItem(TreeItemViewModel? item)
    {
        if (item is null) return;
        item.IsRenaming = true;
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
}
