using System.Windows.Controls;

namespace Deskbridge.Views;

/// <summary>
/// Reconnect overlay view — see <c>ReconnectOverlay.xaml</c>. DataContext is set by
/// <c>MainWindow</c> per-request so the ViewModel's lifetime matches a single
/// disconnect episode (Plan 04-03 Task 2.1).
/// </summary>
public partial class ReconnectOverlay : UserControl
{
    public ReconnectOverlay() => InitializeComponent();
}
