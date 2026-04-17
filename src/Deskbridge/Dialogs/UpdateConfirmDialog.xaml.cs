using Wpf.Ui.Controls;

namespace Deskbridge.Dialogs;

/// <summary>
/// Phase 7 Plan 07-01 (UPD-02 / T-07-04): restart confirmation dialog shown
/// after the update package has been downloaded. Warns about active RDP sessions
/// being disconnected on restart. No ViewModel needed -- this is a simple
/// confirmation with static content.
///
/// <para><b>"Restart Now"</b> returns <see cref="ContentDialogResult.Primary"/>;
/// <b>"Later"</b> returns <see cref="ContentDialogResult.None"/>. The caller
/// (MainWindow.xaml.cs) handles the restart flow when Primary is selected.</para>
/// </summary>
public partial class UpdateConfirmDialog : ContentDialog
{
    public UpdateConfirmDialog(ContentDialogHost dialogHost)
        : base(dialogHost)
    {
        InitializeComponent();
    }
}
