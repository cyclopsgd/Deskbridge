using Wpf.Ui.Controls;

namespace Deskbridge;

public partial class MainWindow : FluentWindow
{
    public MainWindow(ViewModels.MainWindowViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
