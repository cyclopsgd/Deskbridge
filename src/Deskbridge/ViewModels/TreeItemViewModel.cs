namespace Deskbridge.ViewModels;

public abstract partial class TreeItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    public Guid Id { get; init; }
}
