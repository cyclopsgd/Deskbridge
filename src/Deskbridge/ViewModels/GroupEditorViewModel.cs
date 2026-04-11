using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.ViewModels;

public partial class GroupEditorViewModel : ObservableValidator
{
    private readonly IConnectionStore _connectionStore;
    private readonly ICredentialService _credentialService;
    private string _password = string.Empty;
    private bool _hadCredentialsPreviously;

    public GroupEditorViewModel(
        IConnectionStore connectionStore,
        ICredentialService credentialService)
    {
        _connectionStore = connectionStore;
        _credentialService = credentialService;
    }

    // --- Properties ---

    [ObservableProperty]
    [Required]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Guid? ParentGroupId { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<GroupDisplayItem> AvailableParentGroups { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInheritanceCount))]
    public partial string? CredentialUsername { get; set; }

    [ObservableProperty]
    public partial string? CredentialDomain { get; set; }

    [ObservableProperty]
    public partial int InheritanceCount { get; set; }

    // --- State ---

    public bool IsNewGroup { get; set; }
    public string DialogTitle => IsNewGroup ? "New Group" : "Edit Group";
    public Guid GroupId { get; set; }
    public bool ShowInheritanceCount => !string.IsNullOrWhiteSpace(CredentialUsername);

    // --- Password handling (not stored in ViewModel per T-03-09) ---

    public void SetPassword(string password)
    {
        _password = password;
    }

    // --- Validation ---

    public bool Validate()
    {
        ValidateAllProperties();

        if (string.IsNullOrWhiteSpace(Name))
            return false;

        return !HasErrors;
    }

    // --- Initialize ---

    public void Initialize(ConnectionGroup? existing = null)
    {
        if (existing is not null)
        {
            IsNewGroup = false;
            GroupId = existing.Id;
            Name = existing.Name;
            ParentGroupId = existing.ParentGroupId;

            // Load existing credential username/domain from credential store
            var cred = _credentialService.GetForGroup(existing.Id);
            if (cred is not null)
            {
                CredentialUsername = cred.UserName;
                CredentialDomain = cred.Domain;
                _hadCredentialsPreviously = true;
            }

            // Compute inheritance count
            InheritanceCount = ComputeInheritanceCount(existing.Id);
        }
        else
        {
            IsNewGroup = true;
            GroupId = Guid.NewGuid();
        }

        PopulateAvailableParentGroups();
    }

    // --- Save ---

    public ConnectionGroup Save()
    {
        var group = _connectionStore.GetGroupById(GroupId) ?? new ConnectionGroup { Id = GroupId };

        group.Name = Name;
        group.ParentGroupId = ParentGroupId;

        // Handle credentials
        if (!string.IsNullOrWhiteSpace(CredentialUsername))
        {
            // Store credentials when username is provided
            try
            {
                _credentialService.StoreForGroup(GroupId, CredentialUsername, CredentialDomain, _password);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to store group credentials for group {GroupId}", GroupId);
            }
        }
        else if (_hadCredentialsPreviously)
        {
            // Delete credentials when username cleared (was previously set)
            _credentialService.DeleteForGroup(GroupId);
        }

        _connectionStore.SaveGroup(group);

        // Clear password from memory after use
        _password = string.Empty;

        return group;
    }

    // --- Private helpers ---

    private void PopulateAvailableParentGroups()
    {
        var groups = _connectionStore.GetGroups();
        var items = new ObservableCollection<GroupDisplayItem>
        {
            new(null, "(Root)", 0)
        };

        // Collect this group's descendants to exclude (prevent self-parenting and circular chains)
        var excludeIds = new HashSet<Guid>();
        if (!IsNewGroup)
        {
            excludeIds.Add(GroupId);
            CollectDescendantIds(GroupId, groups, excludeIds);
        }

        // Build a parent->children lookup (use string key to avoid nullable Guid constraint)
        var childLookup = new Dictionary<string, List<ConnectionGroup>>();
        foreach (var g in groups)
        {
            if (excludeIds.Contains(g.Id))
                continue;

            var parentKey = g.ParentGroupId?.ToString() ?? string.Empty;
            if (!childLookup.ContainsKey(parentKey))
                childLookup[parentKey] = [];
            childLookup[parentKey].Add(g);
        }

        // Walk tree depth-first to produce indented display
        void WalkGroups(Guid? parentId, int depth)
        {
            var key = parentId?.ToString() ?? string.Empty;
            if (!childLookup.TryGetValue(key, out var children))
                return;

            foreach (var child in children.OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
            {
                var indent = new string(' ', depth * 3);
                items.Add(new GroupDisplayItem(child.Id, $"{indent}{child.Name}", depth));
                WalkGroups(child.Id, depth + 1);
            }
        }

        WalkGroups(null, 1);

        AvailableParentGroups = items;
    }

    private static void CollectDescendantIds(Guid parentId, IReadOnlyList<ConnectionGroup> allGroups, HashSet<Guid> result)
    {
        foreach (var g in allGroups)
        {
            if (g.ParentGroupId == parentId && !result.Contains(g.Id))
            {
                result.Add(g.Id);
                CollectDescendantIds(g.Id, allGroups, result);
            }
        }
    }

    private int ComputeInheritanceCount(Guid groupId)
    {
        // Count all connections that would inherit from this group
        // Walk all connections and check if their group chain includes this group
        var connections = _connectionStore.GetAll();
        var groups = _connectionStore.GetGroups();
        var groupLookup = groups.ToDictionary(g => g.Id);

        int count = 0;
        foreach (var conn in connections)
        {
            if (conn.CredentialMode != CredentialMode.Inherit)
                continue;

            // Walk up the group chain to see if this group is in the chain
            var currentGroupId = conn.GroupId;
            while (currentGroupId is not null)
            {
                if (currentGroupId.Value == groupId)
                {
                    count++;
                    break;
                }

                if (groupLookup.TryGetValue(currentGroupId.Value, out var g))
                    currentGroupId = g.ParentGroupId;
                else
                    break;
            }
        }

        return count;
    }
}
