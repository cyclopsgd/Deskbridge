using System.Windows.Input;
using Deskbridge.ViewModels;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

public partial class ConnectionEditorDialog : ContentDialog
{
    private readonly ConnectionEditorViewModel _viewModel;

    private const string PasswordPlaceholder = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";
    private bool _suppressPasswordChanged;

    public ConnectionEditorDialog(
        ContentDialogHost dialogHost,
        ConnectionEditorViewModel viewModel)
        : base(dialogHost)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        PreviewKeyDown += Dialog_PreviewKeyDown;

        // Show placeholder dots when a password is already stored in CredMan
        if (_viewModel.HasStoredPassword)
        {
            _suppressPasswordChanged = true;
            PasswordBox.Password = PasswordPlaceholder;
            _suppressPasswordChanged = false;
        }

        PasswordBox.GotFocus += PasswordBox_GotFocus;
        PasswordBox.LostFocus += PasswordBox_LostFocus;
        PasswordBox.PasswordChanged += PasswordBox_PasswordChanged;
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

    private void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (PasswordBox.Password == PasswordPlaceholder)
        {
            _suppressPasswordChanged = true;
            PasswordBox.Password = string.Empty;
            _suppressPasswordChanged = false;
        }
    }

    private void PasswordBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // If the user clicked away without typing anything, restore the placeholder
        // so the stored password is not wiped.
        if (string.IsNullOrEmpty(PasswordBox.Password) && _viewModel.HasStoredPassword)
        {
            _suppressPasswordChanged = true;
            PasswordBox.Password = PasswordPlaceholder;
            _suppressPasswordChanged = false;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressPasswordChanged) return;
        // Real user input -- no special handling needed here; OnButtonClick reads
        // PasswordBox.Password when save is clicked.
    }

    protected override void OnButtonClick(ContentDialogButton button)
    {
        if (button == ContentDialogButton.Primary)
        {
            // Pass PasswordBox.Password to ViewModel for credential storage (T-03-09).
            // If the field still shows placeholder dots, treat as empty (no password change).
            var password = PasswordBox.Password;
            if (password == PasswordPlaceholder)
                password = string.Empty;

            _viewModel.SetPassword(password);
            if (!_viewModel.Validate())
            {
                // Prevent dialog from closing on validation failure (T-03-10)
                return;
            }
        }
        base.OnButtonClick(button);
    }
}
