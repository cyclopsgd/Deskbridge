using System.Windows;
using System.Windows.Input;
using Deskbridge.ViewModels;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// BULK-03 — bulk edit modal. Hosts the per-field enable grid (Hostname / Port / Credential mode /
/// Username / Domain / Group). Name and password are intentionally absent. Apply is gated by
/// <see cref="BulkEditViewModel.CanApply"/> and validated by <see cref="BulkEditViewModel.Validate"/>.
/// Persistence (SaveBatch) is wired by plan 23-03; this dialog only edits the in-memory models.
/// </summary>
public partial class BulkEditDialog : ContentDialog
{
    private readonly BulkEditViewModel _viewModel;

    public BulkEditDialog(
        ContentDialogHost host,
        BulkEditViewModel viewModel)
        : base(host)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        PreviewKeyDown += Dialog_PreviewKeyDown;
    }

    /// <summary>
    /// Surfaces the atomic SaveBatch failure InfoBar ("Couldn't apply changes"). Called by the
    /// tree VM (plan 23-03) when persistence fails so the dialog stays open with the error shown.
    /// </summary>
    public void ShowSaveError(int failedCount, int totalCount)
    {
        SaveErrorInfoBar.Message =
            $"{failedCount} of {totalCount} connections could not be updated. No changes were saved.";
        SaveErrorInfoBar.IsOpen = true;
    }

    // Pitfall S1: Enter inside a TextBox prematurely triggers the Primary button when
    // DefaultButton=Primary. Swallow Enter when focus is in a text input.
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
            if (!_viewModel.Validate())
            {
                // Validation failure: show the message and keep the dialog open (do NOT call base).
                ValidationMessage.Text =
                    "Enter a valid port (1-65535) and a non-empty hostname for the fields you've enabled.";
                ValidationMessage.Visibility = Visibility.Visible;
                return;
            }

            ValidationMessage.Visibility = Visibility.Collapsed;
        }

        base.OnButtonClick(button);
    }
}
