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
    private readonly Func<bool>? _onApply;

    /// <param name="onApply">
    /// WR-01: the persistence callback (ApplyToModels + atomic SaveBatch). Invoked INSIDE the
    /// Primary button handler so a save failure VETOES the dialog close and the error InfoBar
    /// renders on the still-open dialog. Returns false on persistence failure. When null the
    /// dialog is a pure editor (no save wired) and Primary closes once validation passes.
    /// </param>
    public BulkEditDialog(
        ContentDialogHost host,
        BulkEditViewModel viewModel,
        Func<bool>? onApply = null)
        : base(host)
    {
        _viewModel = viewModel;
        _onApply = onApply;
        DataContext = viewModel;
        InitializeComponent();
        PreviewKeyDown += Dialog_PreviewKeyDown;
    }

    /// <summary>
    /// Surfaces the atomic SaveBatch failure InfoBar ("Couldn't apply changes"). SaveBatch is
    /// all-or-nothing, so a single count reflects reality: either every edit persisted or none did
    /// (IN-04). Keeps the dialog open with the error shown.
    /// </summary>
    public void ShowSaveError(int count)
    {
        SaveErrorInfoBar.Message =
            $"{count} connections could not be updated. No changes were saved.";
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

            // WR-01: run the atomic save INSIDE the button handler so a persistence failure
            // vetoes the close (mirror the Validate() veto above) and the error InfoBar renders
            // on the still-open dialog. _onApply returns false on SaveBatch failure.
            if (_onApply is not null && !_onApply())
            {
                ShowSaveError(_viewModel.SelectedCount);
                return; // do NOT call base → dialog stays open with the InfoBar visible.
            }
        }

        base.OnButtonClick(button);
    }
}
