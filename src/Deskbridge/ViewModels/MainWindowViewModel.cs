namespace Deskbridge.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private partial string Title { get; set; } = "Deskbridge";
}
