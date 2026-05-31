using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// BULK-01 — GDI threshold confirmation. Shown before "Connect All" only when the projected
/// session count would exceed the GDI warning threshold (and confirmation is enabled). These
/// operations are non-destructive, so the dialog uses a Warning InfoBar and an accent Primary
/// button (NOT a red/destructive appearance). No validation gate — pure confirm/cancel.
/// </summary>
public partial class BulkConnectConfirmDialog : ContentDialog
{
    public BulkConnectConfirmDialog(
        ContentDialogHost host,
        int sessionCount,
        int threshold)
        : base(host)
    {
        InitializeComponent();

        // Mirror the shipped TabHostManager snackbar phrasing ("performance may degrade").
        BodyText.Text =
            $"This will open {sessionCount} sessions. {threshold}+ active sessions may degrade performance. Continue?";
    }
}
