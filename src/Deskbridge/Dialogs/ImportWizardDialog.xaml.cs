using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Deskbridge.ViewModels;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// Phase 7 Plan 07-04 (MIG-02): Import wizard ContentDialog with 4-step flow.
/// Extends ContentDialog per WPF-UI pattern. Uses BasedOn style (Pitfall 1)
/// and PreviewKeyDown Enter handler (Pitfall 8).
///
/// Phase 22 Plan 22-02 (IMP-03 / D-05): adds two new lifecycle handlers —
///  - <see cref="OnDialogClosing"/> suppresses Cancel / Esc / backdrop dismiss
///    while the prepare loop is running (mirrors CrashDialog.xaml.cs:91 pattern).
///  - <see cref="OnDialogLoaded"/> best-effort visual greying of the close button
///    via the WPF-UI template part (PATTERNS.md correction #2(b)) — wrapped in
///    try/catch + Log.Warning so a future template change cannot crash the app;
///    runtime cancel-suppression still fires regardless.
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

        // Phase 22 (D-05, RESEARCH P3): suppress Cancel/Esc/backdrop dismiss
        // while the prepare loop is running. Same pattern as CrashDialog.xaml.cs:91.
        Closing += OnDialogClosing;

        // Phase 22 (UI-SPEC §Acceptance #3, PATTERNS.md correction #2(b)):
        // best-effort visual greying. Template-part name is convention-based;
        // null-check + Log.Warning fallback makes this robust to WPF-UI bumps.
        Loaded += OnDialogLoaded;
    }

    /// <summary>
    /// Phase 22 (D-05): suppresses dialog close while
    /// <see cref="ImportWizardViewModel.IsImportWriteInProgress"/> is true.
    /// Mirrors the verbatim CrashDialog template at <c>CrashDialog.xaml.cs:91</c>
    /// — WPF-UI 4.2's <c>ButtonClicked</c> args have no <c>Cancel</c>; the
    /// <c>Closing</c> event args do.
    /// </summary>
    private void OnDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs e)
    {
        if (_viewModel.IsImportWriteInProgress)
        {
            e.Cancel = true;
        }
    }

    /// <summary>
    /// Phase 22 (UI-SPEC §"Acceptance Criteria #3"): best-effort visual greying
    /// of the dialog's close button while the prepare loop runs. Binds the
    /// template-part button's <c>IsEnabled</c> to <c>!IsImportWriteInProgress</c>.
    /// If the template part is renamed in a future WPF-UI release the binding
    /// silently fails — runtime cancel-suppression in <see cref="OnDialogClosing"/>
    /// still prevents close, so the worst-case is the button looking enabled
    /// while clicks are no-ops.
    /// </summary>
    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Template?.FindName("CloseButton", this) is Control closeBtn)
            {
                var binding = new System.Windows.Data.Binding(
                    nameof(ImportWizardViewModel.IsImportWriteInProgress))
                {
                    Source = _viewModel,
                    Converter = new Deskbridge.Converters.InverseBoolConverter(),
                };
                closeBtn.SetBinding(Control.IsEnabledProperty, binding);
            }
            else
            {
                Serilog.Log.Warning(
                    "ImportWizardDialog: ContentDialog template part 'CloseButton' " +
                    "not found — visual greying disabled. Closing-cancel still in effect. " +
                    "WPF-UI version: {Version}",
                    typeof(ContentDialog).Assembly.GetName().Version);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex,
                "ImportWizardDialog: Failed to bind close-button IsEnabled — " +
                "visual greying disabled. Closing-cancel still in effect.");
        }
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

            // IMP-02: prevent double-click while processing
            if (_viewModel.IsProcessing) return;

            // Advance the wizard step
            _ = _viewModel.NextStepCommand.ExecuteAsync(null);
            return; // Don't close the dialog
        }

        // Close/Cancel button
        base.OnButtonClick(button);
    }
}
