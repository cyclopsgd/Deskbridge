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

        // WR-04: ONE visible copy of the warning — the dynamic count+threshold sentence lives in
        // the InfoBar Message (mirrors the shipped TabHostManager "performance may degrade" phrasing).
        WarningInfoBar.Message =
            $"This will open {sessionCount} sessions. {threshold}+ active sessions may degrade performance. Continue?";
    }
}
