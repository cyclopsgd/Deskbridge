using Deskbridge.ViewModels;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

public partial class ConnectionEditorDialog : ContentDialog
{
    private readonly ConnectionEditorViewModel _viewModel;

    public ConnectionEditorDialog(
        ContentDialogHost dialogHost,
        ConnectionEditorViewModel viewModel)
        : base(dialogHost)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override void OnButtonClick(ContentDialogButton button)
    {
        if (button == ContentDialogButton.Primary)
        {
            // Pass PasswordBox.Password to ViewModel for credential storage (T-03-09)
            _viewModel.SetPassword(PasswordBox.Password);
            if (!_viewModel.Validate())
            {
                // Prevent dialog from closing on validation failure (T-03-10)
                return;
            }
        }
        base.OnButtonClick(button);
    }
}
