using System.Collections.ObjectModel;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.ViewModels;

public partial class ConnectionTreeViewModel : ObservableObject
{
    private readonly IConnectionStore _connectionStore;
    private readonly IConnectionQuery _connectionQuery;
    private readonly ICredentialService _credentialService;

    // Cached full tree for restoring after search filter clears
    private ObservableCollection<TreeItemViewModel> _fullTree = [];

    public ConnectionTreeViewModel(
        IConnectionStore connectionStore,
        IConnectionQuery connectionQuery,
        ICredentialService credentialService)
    {
        _connectionStore = connectionStore;
        _connectionQuery = connectionQuery;
        _credentialService = credentialService;
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

    // --- Commands (stubs for Plan 03/04) ---

    [RelayCommand]
    private Task NewConnectionAsync()
    {
        /* Plan 03 — implemented in Plan 03 (editor dialog) */
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task NewGroupAsync()
    {
        /* Plan 03 — implemented in Plan 03 (editor dialog) */
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task DeleteSelectedAsync()
    {
        /* Plan 04 — not yet implemented */
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleQuickProperties()
    {
        IsQuickPropertiesVisible = !IsQuickPropertiesVisible;
    }

    [RelayCommand]
    private void Connect()
    {
        /* Phase 4/5 — not yet implemented */
    }

    [RelayCommand]
    private void EditItem(TreeItemViewModel item)
    {
        /* Plan 04 — not yet implemented */
    }

    [RelayCommand]
    private void RenameItem(TreeItemViewModel item)
    {
        /* Plan 04 — not yet implemented */
    }

    [RelayCommand]
    private void CopyHostname(ConnectionTreeItemViewModel item)
    {
        /* Plan 04 — not yet implemented */
    }

    [RelayCommand]
    private void DuplicateConnection(ConnectionTreeItemViewModel item)
    {
        /* Plan 04 — not yet implemented */
    }

    [RelayCommand]
    private void MoveToGroup(Guid? targetGroupId)
    {
        /* Plan 04 — not yet implemented */
    }
}
