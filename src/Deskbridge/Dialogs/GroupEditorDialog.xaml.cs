using Deskbridge.ViewModels;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

public partial class GroupEditorDialog : ContentDialog
{
    private readonly GroupEditorViewModel _viewModel;

    public GroupEditorDialog(
        ContentDialogHost dialogHost,
        GroupEditorViewModel viewModel)
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
            _viewModel.SetPassword(GroupPasswordBox.Password);
            if (!_viewModel.Validate())
            {
                // Prevent dialog from closing on validation failure
                return;
            }
        }
        base.OnButtonClick(button);
    }
}
