using System.ComponentModel;
using Deskbridge.Core.Interfaces;
using Deskbridge.Protocols.Rdp;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge;

public partial class MainWindow : FluentWindow
{
    private readonly IConnectionCoordinator _coordinator;
    private readonly AirspaceSwapper _airspace;
    private IProtocolHost? _activeRdpHost;

    public MainWindow(
        ViewModels.MainWindowViewModel viewModel,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService,
        Views.ConnectionTreeControl connectionTreeControl,
        IConnectionCoordinator coordinator,
        AirspaceSwapper airspace)
    {
        DataContext = viewModel;
        InitializeComponent();

        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);

        // Place the connection tree control into the Connections panel
        ConnectionsContent.Content = connectionTreeControl;

        _coordinator = coordinator;
        _airspace = airspace;
        _coordinator.HostMounted += OnHostMounted;
        _coordinator.HostUnmounted += OnHostUnmounted;
    }

    /// <summary>
    /// Attaches the AirspaceSwapper to the window once the HwndSource is realized
    /// (04-RESEARCH Pattern 5 usage). Safe after InitializeComponent + Show.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _airspace.AttachToWindow(this);
    }

    /// <summary>
    /// Mitigates dotnet/wpf #10171 (infinite recursion on close with live WFH) by explicitly
    /// disposing the active RdpHostControl before <c>base.OnClosing</c>. Swallows dispose
    /// failures so the window still closes if teardown throws.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        try
        {
            _activeRdpHost?.Dispose();
        }
        catch
        {
            // Non-fatal — Serilog sink may already be gone; best-effort.
        }
        _activeRdpHost = null;
        base.OnClosing(e);
    }

    private void OnHostMounted(object? sender, IProtocolHost host)
    {
        if (host is not RdpHostControl rdp) return;
        Dispatcher.Invoke(() =>
        {
            // D-12 single live host: swap the WFH into the viewport.
            ViewportGrid.Children.Add(rdp.Host);

            // Fires BEFORE ConnectStage runs (coordinator raises HostMounted on
            // HostCreatedEvent, Order=200). Force an immediate, synchronous layout pass so
            // the WindowsFormsHost is parented to a realized HwndSource and AxHost.Handle
            // becomes non-zero — otherwise the downstream ConnectStage throws
            // "called before host was added to the visual tree" (RDP-ACTIVEX-PITFALLS §1).
            // UpdateLayout() is blocking and does NOT pump messages, so it's safe here —
            // Dispatcher.Yield / PushFrame would re-enter the pump and risk ordering issues.
            ViewportGrid.UpdateLayout();

            _airspace.RegisterHost(rdp.Host, ViewportSnapshot);
            _activeRdpHost = rdp;
        });
    }

    private void OnHostUnmounted(object? sender, IProtocolHost host)
    {
        if (host is not RdpHostControl rdp) return;
        Dispatcher.Invoke(() =>
        {
            _airspace.UnregisterHost(rdp.Host);
            ViewportGrid.Children.Remove(rdp.Host);
            if (ReferenceEquals(_activeRdpHost, rdp))
            {
                _activeRdpHost = null;
            }
        });
    }
}
