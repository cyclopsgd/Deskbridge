using System.ComponentModel;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Services;
using Deskbridge.Protocols.Rdp;
using Deskbridge.ViewModels;
using Deskbridge.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge;

public partial class MainWindow : FluentWindow
{
    private readonly IConnectionCoordinator _coordinator;
    private readonly AirspaceSwapper _airspace;
    private IProtocolHost? _activeRdpHost;
    private ReconnectOverlay? _overlayControl;
    private ReconnectOverlayViewModel? _overlayVm;
    private IDisposable? _overlayAirspaceToken;

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
        _coordinator.ReconnectOverlayRequested += OnReconnectOverlayRequested;
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
        // Plan 04-03: close any live reconnect overlay first so the AirspaceSwapper
        // restore token runs before the host disappears.
        try { CloseOverlay(); } catch { /* best-effort */ }

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
            // A fresh host mounting means the reconnect succeeded — close the overlay
            // so the new session is visible. No-op if no overlay is showing.
            CloseOverlay();

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

    /// <summary>
    /// Plan 04-03 D-07 bridge: wires the Core <see cref="ReconnectOverlayHandle"/> to a
    /// freshly created <see cref="ReconnectOverlayViewModel"/> + <see cref="ReconnectOverlay"/>,
    /// mounts the overlay into <c>ViewportGrid</c>, and collapses the current
    /// WindowsFormsHost via <see cref="AirspaceSwapper.HideWithoutSnapshot"/> so the
    /// WPF overlay is not obscured by the WinForms airspace.
    /// </summary>
    private void OnReconnectOverlayRequested(object? sender, ReconnectUiRequest req)
    {
        Dispatcher.Invoke(() =>
        {
            // Replace any existing overlay (edge case: rapid successive drops).
            CloseOverlay();

            _overlayVm = new ReconnectOverlayViewModel { ConnectionName = req.Connection.Hostname };
            _overlayControl = new ReconnectOverlay { DataContext = _overlayVm };

            // Core -> UI: coordinator pushes updates through these actions.
            req.Handle.UpdateAttempt = (attempt, delay) =>
                Dispatcher.Invoke(() => _overlayVm?.Update(attempt, delay));
            req.Handle.SwitchToManual = () =>
                Dispatcher.Invoke(() => _overlayVm?.SwitchToManual());
            req.Handle.Close = () =>
                Dispatcher.Invoke(CloseOverlay);

            // UI -> Core: user intents fan out through the handle.
            _overlayVm.Cancelled += (_, _) => req.Handle.RaiseCancel();
            _overlayVm.ReconnectRequested += (_, _) => req.Handle.RaiseManualReconnect();
            _overlayVm.CloseRequested += (_, _) => req.Handle.RaiseManualClose();

            // Hide the WFH behind the overlay (D-07) — session is already gone so no
            // snapshot is meaningful; just collapse Visibility. Airspace helper returns a
            // token that restores Visible on dispose when the overlay closes.
            if (_activeRdpHost is RdpHostControl rdp)
            {
                try { _overlayAirspaceToken = _airspace.HideWithoutSnapshot(rdp.Host); }
                catch
                {
                    // Best-effort: if the host is already disposed the airspace hide fails;
                    // the overlay still renders on top of the viewport grid regardless.
                }
            }

            ViewportGrid.Children.Add(_overlayControl);
            System.Windows.Controls.Panel.SetZIndex(_overlayControl, 1000);
        });
    }

    private void CloseOverlay()
    {
        if (_overlayControl is null && _overlayVm is null && _overlayAirspaceToken is null) return;
        Dispatcher.Invoke(() =>
        {
            try { _overlayAirspaceToken?.Dispose(); } catch { /* best-effort */ }
            _overlayAirspaceToken = null;
            if (_overlayControl is not null && ViewportGrid.Children.Contains(_overlayControl))
            {
                ViewportGrid.Children.Remove(_overlayControl);
            }
            _overlayControl = null;
            _overlayVm = null;
        });
    }
}
