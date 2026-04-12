using System.Windows.Input;
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
        PreviewKeyDown += Dialog_PreviewKeyDown;
    }

    // Pitfall §1: Enter inside a TextBox prematurely triggers the Primary button
    // when DefaultButton=Primary. Swallow Enter when focus is in a text input so
    // typing/multi-line entry doesn't commit the dialog by accident.
    private static void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.PasswordBox)
            e.Handled = true;
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
