using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge;

public partial class MainWindow : FluentWindow
{
    public MainWindow(
        ViewModels.MainWindowViewModel viewModel,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService)
    {
        DataContext = viewModel;
        InitializeComponent();

        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);
    }
}
