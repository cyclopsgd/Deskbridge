using System.Windows;
using System.Windows.Input;
using Deskbridge.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// Phase 6.1: Change password/PIN dialog. ContentDialog subclass with three
/// PasswordBox fields (current, new, confirm) and PIN MaxLength DataTriggers.
/// PrimaryButton click delegates to <see cref="ChangePasswordViewModel.Submit"/>
/// and only closes on success.
///
/// <para><b>Pitfall 8</b> (WPF-UI Issue #1404): Enter inside a PasswordBox would
/// prematurely trigger the PrimaryButton. We intercept Enter in PreviewKeyDown
/// and invoke Submit manually.</para>
///
/// <para><b>WPF-UI 4.2.0 API</b>: uses <c>OnButtonClick</c> virtual override
/// pattern (same as ConnectionEditorDialog). Not calling <c>base.OnButtonClick</c>
/// prevents the dialog from closing on validation failure.</para>
/// </summary>
public partial class ChangePasswordDialog : ContentDialog
{
    private readonly ChangePasswordViewModel _vm;

    public ChangePasswordDialog(ChangePasswordViewModel vm, IContentDialogService dialogService)
        : base(dialogService.GetDialogHostEx())
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(dialogService);

        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        Loaded += (_, _) => CurrentField.Focus();
        PreviewKeyDown += Dialog_PreviewKeyDown;
    }

    /// <summary>
    /// WPF-UI 4.2.0: OnButtonClick override — not calling base prevents the dialog
    /// from closing. On Primary, delegate to Submit and only close on success.
    /// </summary>
    protected override void OnButtonClick(ContentDialogButton button)
    {
        if (button == ContentDialogButton.Primary)
        {
            _vm.Submit();
            if (!_vm.IsSuccess) return; // don't close on validation failure
        }
        base.OnButtonClick(button);
    }

    /// <summary>
    /// Pitfall 8 mitigation: Enter in a PasswordBox invokes Submit, not the phantom PrimaryButton.
    /// </summary>
    internal void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var focused = Keyboard.FocusedElement;
        if (focused is Wpf.Ui.Controls.PasswordBox or System.Windows.Controls.PasswordBox)
        {
            _vm.Submit();
            if (_vm.IsSuccess) Hide();
            e.Handled = true;
        }
    }

    private void CurrentField_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.CurrentPassword = ((Wpf.Ui.Controls.PasswordBox)sender).Password;
    private void NewField_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.NewPassword = ((Wpf.Ui.Controls.PasswordBox)sender).Password;
    private void ConfirmField_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.ConfirmNewPassword = ((Wpf.Ui.Controls.PasswordBox)sender).Password;
}
