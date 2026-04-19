using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.ViewModels;

public record GroupDisplayItem(Guid? Id, string DisplayName, int Depth);

public record CredentialModeOption(CredentialMode Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public partial class ConnectionEditorViewModel : ObservableValidator
{
    private readonly IConnectionStore _connectionStore;
    private readonly ICredentialService _credentialService;
    private string _password = string.Empty;

    public ConnectionEditorViewModel(
        IConnectionStore connectionStore,
        ICredentialService credentialService)
    {
        _connectionStore = connectionStore;
        _credentialService = credentialService;
    }

    // --- General tab ---

    [ObservableProperty]
    [Required]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [Required]
    public partial string Hostname { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int Port { get; set; } = 3389;

    [ObservableProperty]
    public partial Guid? GroupId { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<GroupDisplayItem> AvailableGroups { get; set; } = [];

    // --- Credentials tab ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCredentialFieldsEnabled))]
    [NotifyPropertyChangedFor(nameof(IsCredentialFieldsVisible))]
    [NotifyPropertyChangedFor(nameof(IsInheritInfoBarVisible))]
    [NotifyPropertyChangedFor(nameof(IsPromptInfoBarVisible))]
    public partial CredentialMode CredentialMode { get; set; } = CredentialMode.Inherit;

    // Display-friendly options for the CredentialMode ComboBox.
    // (ComboBoxItem with an enum Tag renders as a control template marker ("..." / "-")
    // inside a ContentDialog, so we bind to an ItemsSource of real display objects.)
    public IReadOnlyList<CredentialModeOption> CredentialModes { get; } = new[]
    {
        new CredentialModeOption(CredentialMode.Inherit, "Inherit from parent group"),
        new CredentialModeOption(CredentialMode.Own, "Use own credentials"),
        new CredentialModeOption(CredentialMode.Prompt, "Prompt at connection time"),
    };

    [ObservableProperty]
    public partial string? Username { get; set; }

    [ObservableProperty]
    public partial string? Domain { get; set; }

    [ObservableProperty]
    public partial string InheritedFromMessage { get; set; } = string.Empty;

    // Computed booleans for credential mode UI switching
    public bool IsCredentialFieldsEnabled => CredentialMode == CredentialMode.Own;
    public bool IsCredentialFieldsVisible => CredentialMode != CredentialMode.Prompt;
    public bool IsInheritInfoBarVisible => CredentialMode == CredentialMode.Inherit;
    public bool IsPromptInfoBarVisible => CredentialMode == CredentialMode.Prompt;

    // --- Display tab ---

    [ObservableProperty]
    public partial int? DisplayWidth { get; set; }

    [ObservableProperty]
    public partial int? DisplayHeight { get; set; }

    [ObservableProperty]
    public partial bool SmartSizing { get; set; } = true;

    // --- Notes tab ---

    [ObservableProperty]
    public partial string? Notes { get; set; }

    // --- State ---

    public bool IsNewConnection { get; set; }
    public string DialogTitle => IsNewConnection ? "New Connection" : "Edit Connection";
    public Guid ConnectionId { get; set; }

    // --- Credential mode change handler ---

    partial void OnCredentialModeChanged(CredentialMode value)
    {
        if (value == CredentialMode.Inherit)
        {
            ComputeInheritedFromMessage();
        }
    }

    // --- Password handling (not stored in ViewModel as observable property per T-03-09) ---

    /// <summary>
    /// True when a password is already stored in Windows Credential Manager for
    /// this connection. Used by the dialog code-behind to show placeholder dots
    /// in the password field (matching the quick properties panel pattern).
    /// </summary>
    public bool HasStoredPassword { get; private set; }

    public void SetPassword(string password)
    {
        _password = password;
    }

    // --- Validation ---

    public bool Validate()
    {
        ValidateAllProperties();

        if (string.IsNullOrWhiteSpace(Hostname))
            return false;

        if (Port < 1 || Port > 65535)
            return false;

        return !HasErrors;
    }

    // --- Initialize ---

    public void Initialize(ConnectionModel? existing = null)
    {
        PopulateAvailableGroups();

        if (existing is not null)
        {
            IsNewConnection = false;
            ConnectionId = existing.Id;
            Name = existing.Name;
            Hostname = existing.Hostname;
            Port = existing.Port;
            GroupId = existing.GroupId;
            CredentialMode = existing.CredentialMode;
            Notes = existing.Notes;

            if (existing.DisplaySettings is not null)
            {
                DisplayWidth = existing.DisplaySettings.Width;
                DisplayHeight = existing.DisplaySettings.Height;
                SmartSizing = existing.DisplaySettings.SmartSizing;
            }

            // Load credential username/domain for Own mode (never load password)
            if (existing.CredentialMode == CredentialMode.Own)
            {
                var cred = _credentialService.GetForConnection(existing);
                if (cred is not null)
                {
                    Username = cred.UserName;
                    Domain = cred.Domain;
                    HasStoredPassword = true;
                }
            }

            // Compute inherited message for Inherit mode
            if (existing.CredentialMode == CredentialMode.Inherit)
            {
                ComputeInheritedFromMessage();
            }
        }
        else
        {
            IsNewConnection = true;
            ConnectionId = Guid.NewGuid();

            // Compute inherited message for default Inherit mode
            ComputeInheritedFromMessage();
        }
    }

    // --- Save ---

    public ConnectionModel Save()
    {
        var connection = _connectionStore.GetById(ConnectionId) ?? new ConnectionModel { Id = ConnectionId };

        connection.Name = string.IsNullOrWhiteSpace(Name) ? Hostname : Name;
        connection.Hostname = Hostname;
        connection.Port = Port;
        connection.GroupId = GroupId;
        connection.CredentialMode = CredentialMode;
        connection.Username = Username;
        connection.Domain = Domain;
        connection.Notes = Notes;
        connection.UpdatedAt = DateTime.UtcNow;

        if (IsNewConnection)
        {
            connection.CreatedAt = DateTime.UtcNow;
        }

        connection.DisplaySettings = new DisplaySettings
        {
            Width = DisplayWidth,
            Height = DisplayHeight,
            SmartSizing = SmartSizing
        };

        // Store credentials when mode is Own.
        // Two cases: (a) user entered a new password, (b) user changed username/domain
        // without re-entering password -- read existing password from CredentialManager
        // and re-store with updated fields. Without (b), domain/username edits silently
        // fail to propagate to CredentialManager (the stale credential persists and
        // ResolveCredentialsStage overwrites the model at connect time).
        if (CredentialMode == CredentialMode.Own)
        {
            var passwordToStore = _password;

            if (string.IsNullOrEmpty(passwordToStore) && HasStoredPassword)
            {
                // Read existing password from CredentialManager so we can re-store
                // with updated username/domain.
                var existing = _credentialService.GetForConnection(connection);
                passwordToStore = existing?.Password ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(passwordToStore))
            {
                try
                {
                    _credentialService.StoreForConnection(connection, Username ?? string.Empty, Domain, passwordToStore);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Failed to store credentials for connection {ConnectionId}", connection.Id);
                }
            }
        }

        _connectionStore.Save(connection);

        // Clear password from memory after use
        _password = string.Empty;

        return connection;
    }

    // --- Private helpers ---

    private void PopulateAvailableGroups()
    {
        var groups = _connectionStore.GetGroups();
        var items = new ObservableCollection<GroupDisplayItem>
        {
            new(null, "(Root)", 0)
        };

        // Build a parent->children lookup (use string key to avoid nullable Guid constraint)
        var childLookup = new Dictionary<string, List<ConnectionGroup>>();
        foreach (var g in groups)
        {
            var parentKey = g.ParentGroupId?.ToString() ?? string.Empty;
            if (!childLookup.ContainsKey(parentKey))
                childLookup[parentKey] = [];
            childLookup[parentKey].Add(g);
        }

        // Cycle-safe walk: hand-edited JSON can produce A.Parent=A loops that would
        // otherwise stack-overflow. Track visited IDs.
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
                var indent = new string(' ', depth * 3);
                items.Add(new GroupDisplayItem(child.Id, $"{indent}{child.Name}", depth));
                WalkGroups(child.Id, depth + 1);
            }
        }

        WalkGroups(null, 1);

        AvailableGroups = items;
    }

    private void ComputeInheritedFromMessage()
    {
        // Build a temporary model to use with ResolveInherited
        var tempModel = new ConnectionModel
        {
            Id = ConnectionId,
            GroupId = GroupId,
            CredentialMode = CredentialMode.Inherit
        };

        var inherited = _credentialService.ResolveInherited(tempModel, _connectionStore);
        if (inherited is not null && GroupId is not null)
        {
            // Walk the group chain to find which group provides the credentials
            var groupName = FindGroupWithCredentials(GroupId.Value);
            InheritedFromMessage = groupName is not null
                ? $"Credentials inherited from {groupName}"
                : "No parent group has credentials set";
        }
        else
        {
            InheritedFromMessage = "No parent group has credentials set";
        }
    }

    private string? FindGroupWithCredentials(Guid groupId)
    {
        // Cycle-safe parent walk: bad data could produce A.Parent=B, B.Parent=A.
        var visited = new HashSet<Guid>();
        var currentGroupId = (Guid?)groupId;
        while (currentGroupId is not null)
        {
            if (!visited.Add(currentGroupId.Value))
                break;

            if (_credentialService.HasGroupCredentials(currentGroupId.Value))
            {
                var group = _connectionStore.GetGroupById(currentGroupId.Value);
                return group?.Name;
            }

            var g = _connectionStore.GetGroupById(currentGroupId.Value);
            currentGroupId = g?.ParentGroupId;
        }

        return null;
    }
}
