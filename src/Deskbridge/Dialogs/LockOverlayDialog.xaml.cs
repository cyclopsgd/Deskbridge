using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// Phase 6 Plan 06-04 Task 0 (Wave 0 spike, Q4): minimal <see cref="ContentDialog"/>
/// shell to confirm <c>IsFooterVisible="False"</c> and the opaque
/// <c>ApplicationBackgroundBrush</c> hide the footer and any default title-bar
/// close-X. Task 2 adds the full VM binding, first-run vs unlock modes, and
/// Pitfall 8 Enter handler per UI-SPEC §Lock Overlay Internal Layout.
/// </summary>
public partial class LockOverlayDialog : ContentDialog
{
    public LockOverlayDialog(IContentDialogService dialogService)
        : base(dialogService.GetDialogHostEx())
    {
        ArgumentNullException.ThrowIfNull(dialogService);
        InitializeComponent();
    }
}
