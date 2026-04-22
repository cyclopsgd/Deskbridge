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

    // Pitfall S1: Enter inside a TextBox prematurely triggers the Primary button
    // when DefaultButton=Primary. Swallow Enter when focus is in a text input so
    // typing/multi-line entry doesn't commit the dialog by accident.
    // Check both WPF PasswordBox (base type) and WPF-UI PasswordBox (wraps a TextBox).
    private static void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.PasswordBox
                or Wpf.Ui.Controls.PasswordBox)
            e.Handled = true;
    }

    protected override void OnButtonClick(ContentDialogButton button)
    {
        if (button == ContentDialogButton.Primary)
        {
            if (_viewModel.ShowPasswordFields)
            {
                // Password fields are visible -- either new connection or user clicked "Change Password"
                var password = PasswordBox.Password;
                var confirm = ConfirmPasswordBox.Password;

                if (_viewModel.IsChangingPassword)
                {
                    // Existing connection: validate password match before saving
                    if (!_viewModel.ValidatePasswordMatch(password, confirm))
                        return;

                    // If both empty after validation pass, no password change intended
                    if (!string.IsNullOrEmpty(password))
                        _viewModel.SetPassword(password);
                }
                else
                {
                    // New connection: set password directly (no confirmation needed for empty)
                    _viewModel.SetPassword(password);
                }
            }
            // else: password fields hidden (existing connection, user did not click Change Password)
            // -- preserve existing credential by not calling SetPassword (empty string preserves it)

            if (!_viewModel.Validate())
            {
                // Prevent dialog from closing on validation failure (T-03-10)
                return;
            }
        }
        base.OnButtonClick(button);
    }
}
