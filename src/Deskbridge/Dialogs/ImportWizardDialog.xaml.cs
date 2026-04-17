using System.Windows.Input;
using Deskbridge.ViewModels;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// Phase 7 Plan 07-04 (MIG-02): Import wizard ContentDialog with 4-step flow.
/// Extends ContentDialog per WPF-UI pattern. Uses BasedOn style (Pitfall 1)
/// and PreviewKeyDown Enter handler (Pitfall 8).
/// </summary>
public partial class ImportWizardDialog : ContentDialog
{
    private readonly ImportWizardViewModel _viewModel;

    public ImportWizardDialog(
        ContentDialogHost dialogHost,
        ImportWizardViewModel viewModel)
        : base(dialogHost)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        PreviewKeyDown += Dialog_PreviewKeyDown;
    }

    /// <summary>
    /// Pitfall 8: Enter inside a TextBox prematurely triggers the Primary button.
    /// Swallow Enter when focus is in a text input so file path TextBox entry
    /// doesn't commit the dialog by accident.
    /// </summary>
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
            if (_viewModel.CurrentStep == 4)
            {
                // Done -- close the dialog
                base.OnButtonClick(button);
                return;
            }

            // Advance the wizard step
            _ = _viewModel.NextStepCommand.ExecuteAsync(null);
            return; // Don't close the dialog
        }

        // Close/Cancel button
        base.OnButtonClick(button);
    }
}
