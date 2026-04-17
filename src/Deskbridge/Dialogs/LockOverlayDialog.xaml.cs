using System.Windows;
using System.Windows.Input;
using Deskbridge.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-01 / SEC-02) lock overlay. Full-window opaque
/// <see cref="ContentDialog"/> subclass with <c>IsFooterVisible="False"</c>
/// (no stock Primary/Secondary/Close buttons) and a ContentDialogSmokeFill
/// resource override that makes the backdrop fully opaque (see XAML comments).
///
/// <para><b>Pitfall 8</b> (WPF-UI Issue #1404): even with IsFooterVisible="False"
/// the dialog's built-in Enter handler fires the PrimaryButton. We mitigate by
/// intercepting Enter in <see cref="Dialog_PreviewKeyDown"/> and invoking the
/// VM's UnlockCommand when a PasswordBox or TextBox (PIN cells) has focus.</para>
///
/// <para>PasswordBox.Password is NOT a <see cref="DependencyProperty"/> (by
/// design — to avoid the plaintext living in the WPF binding pipeline). We
/// push the value into the VM on every PasswordChanged event via the code-behind
/// hook. The password-mode fields use <see cref="Wpf.Ui.Controls.PasswordBox"/>
/// (for PlaceholderText DP support). PIN-mode fields use
/// <see cref="Controls.PinInputControl"/> with two-way Pin DP binding.</para>
/// </summary>
public partial class LockOverlayDialog : ContentDialog
{
    private readonly LockOverlayViewModel _vm;

    public LockOverlayDialog(LockOverlayViewModel vm, IContentDialogService dialogService)
        : base(dialogService.GetDialogHostEx())
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(dialogService);

        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        Loaded += OnLoaded;
        PreviewKeyDown += Dialog_PreviewKeyDown;

        // Clear password fields when mode changes (first-run only) so switching
        // from password to PIN doesn't leave 12-char text in the field.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LockOverlayViewModel.IsPinMode))
            {
                PasswordField.Password = "";
                ConfirmField.Password = "";
                PinField.Clear();
                ConfirmPinField.Clear();
                _vm.Password = "";
                _vm.ConfirmPassword = "";
                _vm.ErrorMessage = null;

                if (_vm.IsPinMode)
                    PinField.FocusFirst();
                else
                    PasswordField.Focus();
            }
        };

        // RequestFocusPassword fires after a failed unlock — the VM clears the
        // password, then raises this so we re-focus the field (and clear the UI-side
        // PasswordBox.Password which isn't two-way bound).
        _vm.RequestFocusPassword += (_, _) =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_vm.IsPinMode)
                {
                    PinField.Clear();
                    PinField.FocusFirst();
                }
                else
                {
                    PasswordField.Password = "";
                    PasswordField.Focus();
                }
            }));

        // Auto-submit when 6th PIN digit is entered during unlock mode (not first-run,
        // where the user still needs to fill the confirm field).
        PinField.PinComplete += (_, _) =>
        {
            if (!_vm.IsFirstRun && _vm.UnlockCommand.CanExecute(null))
                _vm.UnlockCommand.Execute(null);
        };
    }

    /// <summary>Exposes the VM to <see cref="Services.AppLockController"/> so it can subscribe to <see cref="LockOverlayViewModel.UnlockSucceeded"/>.</summary>
    public LockOverlayViewModel ViewModel => _vm;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Focus the appropriate field on open so the user can type immediately.
        if (_vm.IsPinMode)
            PinField.FocusFirst();
        else
            PasswordField.Focus();
    }

    /// <summary>
    /// WPF-UI Pitfall 8 mitigation (Issue #1404). Enter inside a PasswordBox would
    /// otherwise fire the built-in PrimaryButton (a phantom button because
    /// IsFooterVisible=False). Intercept here and route to the VM's UnlockCommand.
    /// <c>internal</c> so DiComposition source-grep tests can verify the regression
    /// guard by file-read. Handles <see cref="Wpf.Ui.Controls.PasswordBox"/>
    /// (password mode), <see cref="System.Windows.Controls.PasswordBox"/>
    /// (defense-in-depth), and <see cref="System.Windows.Controls.TextBox"/>
    /// (PinInputControl cells are standard TextBox instances).
    /// </summary>
    internal void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var focused = Keyboard.FocusedElement;
        if (focused is Wpf.Ui.Controls.PasswordBox
                     or System.Windows.Controls.PasswordBox
                     or System.Windows.Controls.TextBox)
        {
            if (_vm.UnlockCommand.CanExecute(null))
            {
                _vm.UnlockCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void PasswordField_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.Password = ((Wpf.Ui.Controls.PasswordBox)sender).Password;

    private void ConfirmField_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.ConfirmPassword = ((Wpf.Ui.Controls.PasswordBox)sender).Password;
}
