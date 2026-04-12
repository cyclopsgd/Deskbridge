using Deskbridge.Core.Models;

namespace Deskbridge.ViewModels;

public partial class ConnectionTreeItemViewModel : TreeItemViewModel
{
    [ObservableProperty]
    public partial string Hostname { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int Port { get; set; } = 3389;

    [ObservableProperty]
    public partial string? Username { get; set; }

    [ObservableProperty]
    public partial string? Domain { get; set; }

    [ObservableProperty]
    public partial CredentialMode CredentialMode { get; set; } = CredentialMode.Inherit;

    public Guid? GroupId { get; set; }

    public int SortOrder { get; set; }
}
