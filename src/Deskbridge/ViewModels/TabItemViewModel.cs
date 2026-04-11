namespace Deskbridge.ViewModels;

public partial class TabItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    public Guid ConnectionId { get; init; }
}
