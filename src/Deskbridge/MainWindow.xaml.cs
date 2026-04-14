using System.ComponentModel;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using Deskbridge.Core.Events;
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
    private readonly ITabHostManager _tabHostManager;
    private readonly IEventBus _eventBus;

    // Phase 5 D-04: per-tab overlay dict keyed by ConnectionId. Replaces the Phase 4
    // single-slot (_overlayControl, _overlayVm, _overlayAirspaceToken) fields. Each
    // entry is scoped to ONE tab; rapid-successive drops on the same tab replace the
    // entry (same pattern as Phase 4 CloseOverlay). Drops on different tabs do NOT
    // interfere — each gets its own entry inside HostContainer.
    private readonly Dictionary<Guid, (ReconnectOverlay Control, ReconnectOverlayViewModel Vm, IDisposable? AirspaceToken)> _overlays = new();

    public MainWindow(
        ViewModels.MainWindowViewModel viewModel,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService,
        Views.ConnectionTreeControl connectionTreeControl,
        IConnectionCoordinator coordinator,
        AirspaceSwapper airspace,
        ITabHostManager tabHostManager,
        IEventBus eventBus)
    {
        DataContext = viewModel;
        InitializeComponent();

        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);

        // Place the connection tree control into the Connections panel
        ConnectionsContent.Content = connectionTreeControl;

        _coordinator = coordinator;
        _airspace = airspace;
        _tabHostManager = tabHostManager;
        _eventBus = eventBus;

        _coordinator.HostMounted += OnHostMounted;
        _coordinator.HostUnmounted += OnHostUnmounted;
        _coordinator.ReconnectOverlayRequested += OnReconnectOverlayRequested;

        // Phase 5: subscribe to TabSwitchedEvent so SetActiveHostVisibility can flip
        // Visibility + IsEnabled across every HostContainer child by Tag correlation.
        // TabHostManager (singleton, eager-resolved in App.OnStartup) publishes this
        // event from HostMounted/OnHostUnmounted/SwitchTo.
        _eventBus.Subscribe<TabSwitchedEvent>(this, OnTabSwitched);

        // Hotfix (2026-04-14): also subscribe to TabClosedEvent for synchronous
        // WFH removal. OnHostUnmounted is driven by the coordinator's
        // ConnectionClosedEvent which now fires AFTER the background
        // (fire-and-forget) disconnect pipeline completes — seconds later for
        // unresponsive targets. Without a synchronous removal path, the closed
        // WFH stays parented in HostContainer with its last-rendered frame
        // showing through the viewport, blocking the "Ctrl+N to create" empty-
        // state placeholder from being visible.
        _eventBus.Subscribe<TabClosedEvent>(this, OnTabClosedSync);
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

    private bool _shutdownInProgress;

    /// <summary>
    /// D-08 (Phase 5): sequential app-shutdown disconnect via <see cref="ITabHostManager.CloseAllAsync"/>.
    /// Hotfix (2026-04-14): run async rather than block the UI thread with
    /// <c>.GetAwaiter().GetResult()</c>. The previous approach deadlocked when
    /// <c>DisconnectAsync</c> awaited <c>OnDisconnected</c> events that needed the
    /// dispatcher to pump — but the dispatcher was blocked by <c>GetResult</c>,
    /// producing a 30-second frozen window per live tab. New flow: cancel the
    /// close, kick off <c>CloseAllAsync</c>, then invoke <c>Close()</c> from the
    /// continuation which re-enters <c>OnClosing</c> with <c>_shutdownInProgress</c>
    /// set so we skip straight to base. Parallel disconnects still explicitly
    /// rejected — CloseAllAsync preserves its sequential-await shape internally.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_shutdownInProgress)
        {
            // Second invocation (after our async continuation calls Close()). Let it close.
            try { _eventBus.Unsubscribe<TabSwitchedEvent>(this); } catch { /* best-effort */ }
            try { _eventBus.Unsubscribe<TabClosedEvent>(this); } catch { /* best-effort */ }
            base.OnClosing(e);
            return;
        }

        _shutdownInProgress = true;
        e.Cancel = true;  // cancel THIS close; we'll re-invoke Close() when disconnects finish

        // Kick off async shutdown. Dispatcher stays free to pump messages so
        // OnDisconnected events and other continuations can complete.
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await _tabHostManager.CloseAllAsync();
            }
            catch
            {
                // Non-fatal — Serilog sink may already be gone; best-effort.
            }
            Close();  // re-enters OnClosing; _shutdownInProgress routes to base
        });
    }

    private void OnHostMounted(object? sender, IProtocolHost host)
    {
        if (host is not RdpHostControl rdp) return;
        Dispatcher.Invoke(() =>
        {
            // A fresh host mounting for this connection means either a reconnect
            // succeeded or a first open — close any OPEN overlay for THIS specific
            // tab. Other tabs' overlays stay open (D-14 per-tab isolation).
            CloseOverlayFor(host.ConnectionId);

            // D-04 persistent container: add WFH to HostContainer ONCE. Tag
            // correlates back to ConnectionId for tab switching and overlay
            // routing. The Phase 4 WR-02 defense-in-depth loop that removed all
            // existing WFH children before mounting a new host is DELETED — it
            // was valid for single-host but contradicts "never re-parent" and
            // would force a re-realize of HwndSource on every new tab.
            rdp.Host.Tag = host.ConnectionId;
            HostContainer.Children.Add(rdp.Host);

            // Force synchronous layout so AxHost's HWND is realized BEFORE
            // ConnectStage (coordinator raises HostMounted on HostCreatedEvent
            // Order=200, ConnectStage is Order=300). UpdateLayout is blocking
            // and does NOT pump the message queue.
            HostContainer.UpdateLayout();

            _airspace.RegisterHost(rdp.Host, ViewportSnapshot);

            // TabHostManager's OnHostMounted handler runs via the same coordinator
            // event and publishes TabSwitchedEvent(previous, thisHost). OnTabSwitched
            // (below) then flips Visibility across every child of HostContainer so
            // the just-mounted host becomes Visible and the prior active collapses.
        });
    }

    private void OnHostUnmounted(object? sender, IProtocolHost host)
    {
        if (host is not RdpHostControl rdp) return;
        Dispatcher.Invoke(() =>
        {
            CloseOverlayFor(host.ConnectionId);
            _airspace.UnregisterHost(rdp.Host);

            // This is the ONLY path where a WFH is removed from HostContainer.
            // Every other path (tab switch, drag-reorder) uses Visibility toggle.
            HostContainer.Children.Remove(rdp.Host);

            // TabHostManager fires TabClosedEvent + auto-activates the neighbor
            // via TabSwitchedEvent — OnTabSwitched flips Visibility for the new
            // active tab.
        });
    }

    /// <summary>
    /// Phase 5: subscriber for <see cref="TabSwitchedEvent"/>. Marshals to the
    /// UI dispatcher and calls <see cref="SetActiveHostVisibility"/> to flip the
    /// Tag-keyed Visibility/IsEnabled flags across every child of HostContainer.
    /// </summary>
    private void OnTabSwitched(TabSwitchedEvent evt)
    {
        Dispatcher.Invoke(() => SetActiveHostVisibility(evt.ActiveId));
    }

    /// <summary>
    /// D-04: Toggle Visibility + IsEnabled on every HostContainer child based on
    /// the Tag-keyed correlation to <paramref name="activeId"/>. Exactly one
    /// WindowsFormsHost ends up Visible + IsEnabled. ReconnectOverlay children
    /// follow the same rule — their Visibility tracks the tab's active state.
    ///
    /// <para>IsEnabled is critical per WINFORMS-HOST-AIRSPACE line 397 — a
    /// hidden WFH that stays IsEnabled=true can still capture keyboard input,
    /// which would cause typed keys to leak into the wrong session.</para>
    /// </summary>
    private void SetActiveHostVisibility(Guid activeId)
    {
        foreach (var child in HostContainer.Children)
        {
            if (child is WindowsFormsHost wfh)
            {
                var isActive = wfh.Tag is Guid id && id == activeId;
                // Hotfix (2026-04-14): Hidden (not Collapsed) for WFHs. Visibility.Collapsed
                // can cause WPF to tear down the underlying HwndSource, which orphans the
                // AxHost's HWND and produces a black viewport when the tab is re-shown
                // (ActiveX loses its rendering surface and has nothing to repaint into
                // until the server pushes fresh frame data). Visibility.Hidden keeps the
                // HwndSource alive and layout slot reserved so the ActiveX keeps its
                // render target — switching back is instant.
                wfh.Visibility = isActive ? Visibility.Visible : Visibility.Hidden;
                wfh.IsEnabled = isActive;
            }
            else if (child is ReconnectOverlay ov)
            {
                // Per-tab overlay follows its tab's active state (D-14). Pure WPF element,
                // Collapsed is safe and preferred (doesn't reserve layout space).
                var isActive = ov.Tag is Guid id && id == activeId;
                ov.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// Hotfix (2026-04-14): synchronous visual removal of a closed tab's WFH. Pairs
    /// with the fire-and-forget disconnect pipeline in TabHostManager.CloseTabAsync —
    /// the coordinator's HostUnmounted event fires AFTER the background disconnect
    /// completes (seconds later), but the WFH needs to leave the visual tree NOW so
    /// the empty-state placeholder can become visible when the last tab closes.
    /// OnHostUnmounted still runs its own Remove() later; the second Remove() is a
    /// no-op because the WFH is already gone from HostContainer.Children.
    /// </summary>
    private void OnTabClosedSync(TabClosedEvent evt)
    {
        Dispatcher.Invoke(() =>
        {
            // Walk backwards so index math survives mid-loop removal. Remove ALL WFHs
            // with the matching Tag — defense-in-depth against any phantom duplicates.
            for (var i = HostContainer.Children.Count - 1; i >= 0; i--)
            {
                var child = HostContainer.Children[i];
                if (child is WindowsFormsHost wfh && wfh.Tag is Guid id && id == evt.ConnectionId)
                {
                    HostContainer.Children.RemoveAt(i);
                }
            }
            // Also drop any reconnect overlay for this tab (it's a sibling in
            // HostContainer parented alongside the WFH per D-14).
            CloseOverlayFor(evt.ConnectionId);
        });
    }

    /// <summary>
    /// Phase 5 D-14: per-tab reconnect overlay. Replaces the Phase 4 single-slot
    /// fields with an entry in <see cref="_overlays"/> keyed by ConnectionId.
    /// The overlay is parented into HostContainer alongside the WFH and its
    /// Visibility follows the tab's active state via the Tag correlation rule
    /// in <see cref="SetActiveHostVisibility"/>. No auto-switch focus — an
    /// overlay raised for a background tab stays Collapsed until the user
    /// switches to that tab.
    /// </summary>
    private void OnReconnectOverlayRequested(object? sender, ReconnectUiRequest req)
    {
        Dispatcher.Invoke(() =>
        {
            var id = req.Connection.Id;

            // Replace any existing overlay for this specific tab (rapid
            // successive drops). Other tabs' overlays are untouched.
            CloseOverlayFor(id);

            var vm = new ReconnectOverlayViewModel { ConnectionName = req.Connection.Hostname };
            var ctrl = new ReconnectOverlay { DataContext = vm };
            // Correlate the overlay with its tab so SetActiveHostVisibility
            // can follow the tab's active state on switch.
            ctrl.Tag = id;

            // Core -> UI: coordinator pushes updates through these actions.
            req.Handle.UpdateAttempt = (attempt, delay) =>
                Dispatcher.Invoke(() => vm.Update(attempt, delay));
            req.Handle.SwitchToManual = () =>
                Dispatcher.Invoke(vm.SwitchToManual);
            req.Handle.Close = () =>
                Dispatcher.Invoke(() => CloseOverlayFor(id));

            // UI -> Core: user intents fan out through the handle.
            vm.Cancelled += (_, _) => req.Handle.RaiseCancel();
            vm.ReconnectRequested += (_, _) => req.Handle.RaiseManualReconnect();
            vm.CloseRequested += (_, _) => req.Handle.RaiseManualClose();

            // Hide the WFH behind the overlay (D-07) — session is already gone
            // so no snapshot is meaningful; just collapse Visibility. Token
            // restores the captured previous value on dispose.
            IDisposable? token = null;
            var host = _tabHostManager.GetHost(id);
            if (host is RdpHostControl rdp)
            {
                try { token = _airspace.HideWithoutSnapshot(rdp.Host); }
                catch
                {
                    // Best-effort: if the host is already disposed the airspace
                    // hide fails; the overlay still renders regardless.
                }
            }

            // D-14: overlay mounts inside HostContainer (not at ViewportGrid level).
            // Its Visibility follows the tab's active state via Tag + SetActiveHostVisibility.
            HostContainer.Children.Add(ctrl);
            System.Windows.Controls.Panel.SetZIndex(ctrl, 1000);

            // Initial visibility — if Reconnecting fires on a background tab, the
            // overlay starts Collapsed and becomes Visible on tab switch.
            ctrl.Visibility = (_tabHostManager.ActiveId == id) ? Visibility.Visible : Visibility.Collapsed;

            _overlays[id] = (ctrl, vm, token);
        });
    }

    /// <summary>
    /// Remove the overlay for a specific tab. No-op if no overlay is open for
    /// <paramref name="id"/>. Dispose the airspace token first (restores WFH
    /// Visibility), then remove the control from HostContainer. Best-effort
    /// try/catch protects against a race where the token is already disposed.
    /// </summary>
    private void CloseOverlayFor(Guid id)
    {
        if (!_overlays.TryGetValue(id, out var entry)) return;
        _overlays.Remove(id);
        try { entry.AirspaceToken?.Dispose(); } catch { /* best-effort */ }
        if (HostContainer.Children.Contains(entry.Control))
        {
            HostContainer.Children.Remove(entry.Control);
        }
    }

    /// <summary>
    /// Typed accessor for the VM so <see cref="OnPreviewKeyDown"/> can invoke the
    /// Phase 5 tab commands without casting every time. DataContext is set in the
    /// ctor and never replaced.
    /// </summary>
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    /// <summary>
    /// D-16: Phase 5 keyboard shortcut routing. Ctrl+Tab and Ctrl+Shift+Tab cannot
    /// be XAML <c>KeyBinding</c>s (WPF intercepts them for focus navigation — see
    /// MainWindow.xaml line 17-19 comment). Ctrl+W is already bound in XAML at
    /// window level; we do NOT re-handle it here.
    ///
    /// <para>Shortcuts handled here:</para>
    /// <list type="bullet">
    /// <item><c>Ctrl+Tab</c> / <c>Ctrl+Shift+Tab</c> — cycle forward/backward (TAB-03).</item>
    /// <item><c>Ctrl+F4</c> — close active tab (D-16 alias for Ctrl+W).</item>
    /// <item><c>Ctrl+Shift+T</c> — reopen last closed tab from LRU.</item>
    /// <item><c>Ctrl+1..Ctrl+8</c> — jump to the N-th tab.</item>
    /// <item><c>Ctrl+9</c> — jump to the LAST tab (Chrome / VS Code convention).</item>
    /// </list>
    ///
    /// <para>All recognized shortcuts set <c>e.Handled = true</c> so the key event
    /// does not bubble into the focused AxHost and get sent to the remote session.
    /// Assumption A1 (RESEARCH §Pitfall 2): <c>KeyboardHookMode=0</c> on the RDP
    /// ActiveX control causes the AxHost to forgo the low-level keyboard hook,
    /// so <c>PreviewKeyDown</c> fires on the WPF window BEFORE the AxHost sees the
    /// keystroke. UAT Task 3 validates this against a live session.</para>
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Delegate to the pure-data router so the routing logic is unit-testable
        // without a real Window + Dispatcher + routed-event plumbing.
        if (KeyboardShortcutRouter.TryRoute(ViewModel, e.Key, Keyboard.Modifiers))
        {
            // Handled = true prevents the keystroke from bubbling into the focused
            // AxHost and being sent to the remote session. Assumption A1 (RESEARCH
            // §Pitfall 2): KeyboardHookMode=0 lets PreviewKeyDown fire BEFORE the
            // AxHost receives input. UAT Task 3 validates this against a live session.
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
