using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Deskbridge.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// Phase 6 Plan 06-03 (CMD-01): VS Code-style command palette.
/// <para>Subclass of <see cref="ContentDialog"/> anchored to the
/// <c>RootContentDialog</c> host registered on <c>IContentDialogService</c>.
/// IsFooterVisible=False (no Save/Cancel buttons — selecting an item executes
/// and dismisses). DialogMaxWidth=480 / DialogMaxHeight=480 per UI-SPEC.</para>
/// <para><strong>Pitfall 8 (WPF-UI Issue #1404)</strong>: even with
/// <c>IsFooterVisible=False</c> the dialog's built-in Enter handler fires the
/// PrimaryButton. <see cref="Dialog_PreviewKeyDown"/> intercepts Enter while a
/// <see cref="TextBoxBase"/> has focus and routes to
/// <see cref="CommandPaletteViewModel.ExecuteSelectedAsync"/>.</para>
/// </summary>
public partial class CommandPaletteDialog : ContentDialog
{
    private readonly CommandPaletteViewModel _vm;

    public CommandPaletteDialog(CommandPaletteViewModel vm, IContentDialogService dialogService)
        : base(dialogService.GetDialogHostEx())
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(dialogService);

        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        Loaded += OnLoaded;
        PreviewKeyDown += Dialog_PreviewKeyDown;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Focus the search TextBox on open so the user can type immediately.
        SearchBox.Focus();
    }

    /// <summary>
    /// Pitfall 8 mitigation: Enter inside the search TextBox would otherwise fire
    /// the dialog's built-in PrimaryButton (a phantom button because
    /// IsFooterVisible=False — see WPF-UI Issue #1404). Intercept it here and
    /// route to <see cref="CommandPaletteViewModel.ExecuteSelectedAsync"/> so the
    /// SelectedItem executes and the dialog dismisses. Marked <c>internal</c> so
    /// tests can exercise the handler via InternalsVisibleTo.
    /// </summary>
    internal void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBoxBase)
        {
            _ = _vm.ExecuteSelectedAsync();
            Hide();
            e.Handled = true;
        }
    }
}
