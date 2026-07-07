using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using Deskbridge.Core.Settings;
using Deskbridge.Dialogs;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Services;
using Deskbridge.ViewModels;
using Deskbridge.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace Deskbridge;

/// <summary>
/// Phase 6 Plan 06-04: implements <see cref="IHostContainerProvider"/> so
/// <see cref="AppLockController"/> can snapshot+collapse every WFH child
/// without a direct MainWindow reference (Pitfall 5 Option A).
/// </summary>
public partial class MainWindow : FluentWindow, IHostContainerProvider
{
    private readonly IConnectionCoordinator _coordinator;
    private readonly AirspaceSwapper _airspace;
    private readonly ITabHostManager _tabHostManager;
    private readonly IEventBus _eventBus;
    private readonly IWindowStateService _windowState;

    /// <summary>Plan 06-03 Q6 gate: Ctrl+Shift+P is a no-op while the app is locked.</summary>
    private readonly IAppLockState _lockState;

    /// <summary>Plan 06-03 CMD-01: factory for a fresh palette dialog instance per open.</summary>
    private readonly Func<CommandPaletteDialog> _paletteFactory;

    /// <summary>Phase 6.1: factory for a fresh change password dialog per click.</summary>
    private readonly Func<ChangePasswordDialog>? _changePasswordFactory;

    /// <summary>Phase 7 Plan 07-04: factory for a fresh import wizard dialog per click.</summary>
    private Func<ImportWizardDialog>? _importWizardFactory;

    /// <summary>Phase 6.1: master password service for toggle confirmation.</summary>
    private IMasterPasswordService? _masterPasswordService;

    /// <summary>Phase 6.1: stored for the disable-password confirmation dialog.</summary>
    private IContentDialogService? _contentDialogService;

    /// <summary>
    /// Phase 6.1: set by App.OnStartup after AppLockController is resolved (it can't
    /// be injected via ctor because of the circular dependency). Used to update
    /// <see cref="AppLockController.RequireMasterPassword"/> at runtime when the
    /// toggle changes.
    /// </summary>
    internal AppLockController? LockController { get; set; }

    /// <summary>Plan 06-03 CMD-01: idempotence guard so a held-down Ctrl+Shift+P doesn't stack dialogs.</summary>
    private bool _paletteOpen;

    /// <summary>
    /// Phase 16 (STAB-03): debounce timer for dynamic resolution updates on window resize.
    /// T-16-03: 500ms debounce prevents flooding UpdateSessionDisplaySettings.
    /// </summary>
    private DispatcherTimer? _resizeTimer;

    /// <summary>Plan 06-03 D-05: saved WindowStyle/WindowState for fullscreen restore.</summary>
    private System.Windows.WindowStyle _savedWindowStyle;
    private System.Windows.WindowState _savedWindowState;

    /// <summary>
    /// Phase 6 Plan 06-02 (NOTF-04): cached settings snapshot from OnSourceInitialized.
    /// OnClosing builds the outgoing settings via <c>_loadedSettings with { Window = ... }</c>
    /// so Security preferences from Plan 06-04 pass through untouched.
    /// </summary>
    private AppSettings _loadedSettings = new();

    /// <summary>Phase 9 (D-02): stored for card state save/load in OnSourceInitialized and TrySaveWindowState.</summary>
    private readonly Views.ConnectionTreeControl _connectionTreeControl;


    // Phase 5 D-04: per-tab overlay dict keyed by ConnectionId. Replaces the Phase 4
    // single-slot (_overlayControl, _overlayVm, _overlayAirspaceToken) fields. Each
    // entry is scoped to ONE tab; rapid-successive drops on the same tab replace the
    // entry (same pattern as Phase 4 CloseOverlay). Drops on different tabs do NOT
    // interfere — each gets its own entry inside HostContainer.
    // [CITED: audit A3] The AirspaceToken slot was removed: the dead host's WFH is
    // now purged from HostContainer (and unregistered from the AirspaceSwapper) at
    // overlay-request time, so there is no hidden-but-parented WFH left to restore.
    private readonly Dictionary<Guid, (ReconnectOverlay Control, ReconnectOverlayViewModel Vm)> _overlays = new();

    public MainWindow(
        ViewModels.MainWindowViewModel viewModel,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService,
        Views.ConnectionTreeControl connectionTreeControl,
        IConnectionCoordinator coordinator,
        AirspaceSwapper airspace,
        ITabHostManager tabHostManager,
        IEventBus eventBus,
        IWindowStateService windowState,
        IAppLockState lockState,
        Func<CommandPaletteDialog> paletteFactory,
        Func<ChangePasswordDialog>? changePasswordFactory = null,
        IMasterPasswordService? masterPasswordService = null)
    {
        DataContext = viewModel;
        InitializeComponent();

        // Audit A1: SnackbarPresenter now lives inside OverlayPopup (top-level HWND
        // above the RDP viewport's airspace — see MainWindow.xaml comment). The
        // x:Name field is still connected by InitializeComponent (XAML namescope
        // registration covers Popup children), so this registration is unchanged.
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);

        // Audit A1: overlay popup lifecycle + window tracking. A Popup's HWND does
        // not follow its placement target automatically, so we nudge the offset on
        // every window move/resize (standard WPF trick — the offset change forces a
        // reposition pass). Activation tracking hides the popup when the app is
        // minimized or in the background: WPF popup HWNDs are topmost, so a
        // permanently-open popup would otherwise float above OTHER applications.
        Loaded += (_, _) =>
        {
            // -=/+= pair keeps the subscription idempotent if Loaded fires more
            // than once (same pattern as RestartResolutionDebounce).
            ViewportGrid.SizeChanged -= OnViewportSizeChangedSyncOverlay;
            ViewportGrid.SizeChanged += OnViewportSizeChangedSyncOverlay;
            SyncOverlayPopupToViewport();
            UpdateOverlayPopupVisibility();
        };
        LocationChanged += (_, _) => NudgeOverlayPopup();
        SizeChanged += (_, _) => NudgeOverlayPopup();
        Activated += (_, _) => UpdateOverlayPopupVisibility();
        Deactivated += (_, _) => UpdateOverlayPopupVisibility();

        // Place the connection tree control into the Connections panel
        ConnectionsContent.Content = connectionTreeControl;
        _connectionTreeControl = connectionTreeControl;

        _coordinator = coordinator;
        _airspace = airspace;
        _tabHostManager = tabHostManager;
        _eventBus = eventBus;
        _windowState = windowState;
        _lockState = lockState;
        _paletteFactory = paletteFactory;
        _changePasswordFactory = changePasswordFactory;
        _masterPasswordService = masterPasswordService;
        _contentDialogService = contentDialogService;

        _coordinator.HostMounted += OnHostMounted;
        _coordinator.HostUnmounted += OnHostUnmounted;
        _coordinator.ReconnectOverlayRequested += OnReconnectOverlayRequested;

        // Audit A1 follow-up (lock-screen info leak): OverlayPopup is a top-level
        // HWND that renders ABOVE in-window ContentDialog scrims — an accepted
        // trade-off for ordinary dialogs (see the OverlayPopup comment in
        // MainWindow.xaml), but the LOCK overlay is also a ContentDialog, so toast
        // text (hostnames) would stay readable while the app is locked. Close the
        // popup for the lock's duration and restore on unlock via the single
        // visibility authority. Toast state lives in the VM, so pending toasts
        // reappear when the popup re-opens. MainWindow and IAppLockState are both
        // app-lifetime singletons — no unsubscribe needed.
        _lockState.LockStateChanged += (_, _) =>
        {
            if (Dispatcher.CheckAccess())
            {
                // Synchronous when already on the dispatcher (the lock flow is) so
                // the popup is closed BEFORE the lock overlay dialog renders.
                UpdateOverlayPopupVisibility();
            }
            else
            {
                // Defensive best-effort path: every current raiser is on the
                // dispatcher (SessionLockService marshals via BeginInvoke, the
                // idle timer is a DispatcherTimer, Ctrl+L/minimise are UI-thread
                // flows), so this branch should never run. If a future raiser
                // fires off-thread, InvokeAsync closes the popup as soon as the
                // dispatcher pumps — NOT guaranteed to beat the lock dialog's
                // render. A synchronous Invoke would restore that guarantee but
                // risks deadlocking an unknown caller; best-effort is the safer
                // trade.
                Dispatcher.InvokeAsync(UpdateOverlayPopupVisibility);
            }
        };

        // Phase 6 Plan 06-03 (D-05): observe IsFullscreen changes so we can apply
        // WindowStyle.None + WindowState.Maximized (and restore) without moving the
        // actual window control into the VM.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

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

        // Phase 7 Plan 07-01 (UPD-02 / T-07-04): wire the update confirmation
        // dialog callback. After download completes, the VM invokes this to show
        // the "Restart Now? / Later" dialog. If user confirms, apply + restart.
        viewModel.SetUpdateConfirmation(() =>
        {
            _ = ShowUpdateConfirmDialogAsync();
        });
    }

    /// <summary>
    /// Attaches the AirspaceSwapper to the window once the HwndSource is realized
    /// (04-RESEARCH Pattern 5 usage). Safe after InitializeComponent + Show.
    ///
    /// <para>Phase 6 Plan 06-02 (NOTF-04): hydrate persisted window state from
    /// <c>settings.json</c> BEFORE <see cref="_airspace"/>.AttachToWindow so the
    /// window is sized/positioned correctly the first time it renders. Load is
    /// synchronous (<c>.GetAwaiter().GetResult()</c>) — the file is small (&lt;1 KB)
    /// and an async load would let the window flicker at default dimensions before
    /// the async continuation applied the saved bounds.</para>
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // NOTF-04: hydrate window state from settings.json before the window renders.
        try
        {
            _loadedSettings = _windowState.LoadAsync().GetAwaiter().GetResult();
            var w = _loadedSettings.Window;
            Left = w.X;
            Top = w.Y;
            Width = w.Width;
            Height = w.Height;
            if (w.IsMaximized)
            {
                WindowState = System.Windows.WindowState.Maximized;
            }

            // Plan 06-04 (SEC-03 / SEC-05): push the loaded Security section into
            // the MainWindowViewModel so the settings-panel bindings render the
            // persisted values on first open. Suppressed-persist flag in the VM
            // ensures this initial apply doesn't round-trip a save.
            if (DataContext is ViewModels.MainWindowViewModel vm)
            {
                vm.ApplySecuritySettings(_loadedSettings.Security);

                // Phase 14 (UX-02): apply persisted text scale
                var appearance = _loadedSettings.Appearance ?? AppearanceRecord.Default;
                vm.ApplyAppearanceSettings(appearance);
                vm.SetTextScaleCallback(ApplyTextScale);
                ApplyTextScale(appearance.TextScale);

                // Phase 18: apply bulk operations and uninstall settings
                var bulkOps = _loadedSettings.BulkOperations ?? BulkOperationsRecord.Default;
                vm.ApplyBulkOperationsSettings(bulkOps);

                var uninstall = _loadedSettings.Uninstall ?? UninstallRecord.Default;
                vm.ApplyUninstallSettings(uninstall);
            }

            // Phase 9 (D-02): apply persisted card expand/collapse state
            var treeVm = _connectionTreeControl?.DataContext as ViewModels.ConnectionTreeViewModel;
            treeVm?.ApplyPropertiesPanelSettings(
                _loadedSettings.PropertiesPanel ?? PropertiesPanelRecord.Default);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to apply loaded window state — falling back to XAML defaults");
        }

        _airspace.AttachToWindow(this);

        // Phase 16 (STAB-03): subscribe to ViewportGrid.SizeChanged for debounced
        // dynamic resolution updates. Must be AFTER OnSourceInitialized completes
        // so PresentationSource is available for DPI measurement.
        ViewportGrid.SizeChanged += OnViewportSizeChanged;
    }

    /// <summary>
    /// Plan 06-04 (IHostContainerProvider): exposes the persistent HostContainer
    /// Grid (MainWindow.xaml line 318) as the <see cref="IHostContainerProvider"/>
    /// surface so <see cref="AppLockController"/> can snapshot + collapse every
    /// WFH child on lock (Pitfall 5 Option A). Explicit interface implementation
    /// avoids colliding with the XAML-generated <c>HostContainer</c> field.
    /// </summary>
    Panel IHostContainerProvider.HostContainer => HostContainer;

    /// <summary>
    /// Plan 06-04 (SEC-05 / D-19): minimise-to-lock. Fires when <see cref="Window.WindowState"/>
    /// changes. When LockOnMinimise is enabled and the window is minimised, publishes
    /// <see cref="AppLockedEvent"/>(<see cref="LockReason.Minimise"/>) on the bus —
    /// <see cref="AppLockController"/> subscribes and drives the lock flow. Bus-indirect
    /// is used instead of a direct controller reference because the VM-hosted LockApp
    /// command uses the same pattern (DI cycle avoidance).
    /// </summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // Audit A1: hide the overlay popup while minimized, restore it otherwise
        // (Activated/Deactivated handle the background-app case).
        UpdateOverlayPopupVisibility();

        if (WindowState != System.Windows.WindowState.Minimized) return;
        if (DataContext is not ViewModels.MainWindowViewModel vm) return;
        if (!vm.LockOnMinimise) return;

        _eventBus.Publish(new AppLockedEvent(LockReason.Minimise));
    }

    // ----------------------------------------------------------- Audit A1: overlay popup tracking

    /// <summary>
    /// Audit A1 + WINFORMS-HOST-AIRSPACE §Popups: sizes the popup's root panel to
    /// the viewport so the toast stack's bottom-right alignment anchors to the
    /// viewport corner, then nudges the popup so its HWND follows the new layout.
    /// Subscribed to <c>ViewportGrid.SizeChanged</c> (fires on window resize AND
    /// side-panel toggle, both of which move the viewport's on-screen rectangle).
    /// </summary>
    private void OnViewportSizeChangedSyncOverlay(object sender, SizeChangedEventArgs e)
    {
        SyncOverlayPopupToViewport();
        NudgeOverlayPopup();
    }

    /// <summary>
    /// Audit A1: OverlayPopupRoot must match ViewportGrid's dimensions — the popup
    /// is placed Relative at the viewport's top-left, and the toast stack /
    /// snackbar anchor themselves via alignment inside this viewport-sized panel.
    /// </summary>
    private void SyncOverlayPopupToViewport()
    {
        OverlayPopupRoot.Width = ViewportGrid.ActualWidth;
        OverlayPopupRoot.Height = ViewportGrid.ActualHeight;
    }

    /// <summary>
    /// Audit A1: standard WPF reposition trick — a Popup's top-level HWND does not
    /// track its placement target, but any offset change forces a placement pass.
    /// The +1/-1 pair is net-zero so the visual position stays anchored.
    /// </summary>
    private void NudgeOverlayPopup()
    {
        if (!OverlayPopup.IsOpen) return;
        OverlayPopup.HorizontalOffset += 1;
        OverlayPopup.HorizontalOffset -= 1;
    }

    /// <summary>
    /// Audit A1: single authority for the popup's open state. Open only while the
    /// window is loaded, non-minimized, unlocked, and the foreground application —
    /// WPF popup HWNDs are topmost, so leaving it open while another app has focus
    /// would float toast cards above that application. Toast state is unaffected by
    /// visibility (the VM owns timers/eviction), so sticky failure toasts are
    /// still there when the popup re-opens on activation/restore/unlock.
    ///
    /// <para>Lock gate (A1 follow-up): the popup's top-level HWND renders above the
    /// ContentDialog lock overlay's scrim (the documented A1 trade-off), so without
    /// this check toast text — connection hostnames — would leak through the lock
    /// screen. Driven by <see cref="IAppLockState.LockStateChanged"/> (ctor wiring).</para>
    /// </summary>
    private void UpdateOverlayPopupVisibility()
    {
        OverlayPopup.IsOpen = IsLoaded
            && IsActive
            && !_lockState.IsLocked
            && WindowState != System.Windows.WindowState.Minimized;
    }

    /// <summary>
    /// Audit A1: a StaysOpen popup is not force-closed with its owner window in
    /// every teardown order — close it explicitly so no orphaned topmost HWND
    /// outlives the main window.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        OverlayPopup.IsOpen = false;
        base.OnClosed(e);
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
            // NOTF-04: save again here so the window state captured reflects any
            // size/position changes made between the first OnClosing and the async
            // shutdown completing (e.g. user dragged the window while disconnects ran).
            TrySaveWindowState();
            try { _eventBus.Unsubscribe<TabSwitchedEvent>(this); } catch { /* best-effort */ }
            try { _eventBus.Unsubscribe<TabClosedEvent>(this); } catch { /* best-effort */ }
            base.OnClosing(e);
            return;
        }

        // NOTF-04: first invocation — save window state synchronously BEFORE kicking off
        // the async shutdown path. Protects against a crash during CloseAllAsync leaving
        // settings.json unchanged from the previous session. Redundant write on the second
        // invocation above is cheap (atomic tmp-rename); same-shape updates no-op visibly.
        TrySaveWindowState();

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

    /// <summary>
    /// Phase 6 Plan 06-02 (NOTF-04): capture the current window geometry + sidebar
    /// state and persist to <c>settings.json</c>. Uses <see cref="System.Windows.Window.RestoreBounds"/>
    /// when maximised so the un-maximised position survives the session. Security
    /// preferences from Plan 06-04 pass through the <c>_loadedSettings with</c>
    /// expression untouched.
    ///
    /// <para>Swallows every exception — a failure to persist UI preferences must
    /// never prevent the app from closing. The Serilog warning records the failure
    /// for diagnosis.</para>
    /// </summary>
    private void TrySaveWindowState()
    {
        try
        {
            var vm = DataContext as ViewModels.MainWindowViewModel;
            var sidebarOpen = vm?.IsPanelVisible ?? true;
            var sidebarWidth = 240.0;  // Phase 2 fixed width — bind to VM in a future plan if the panel becomes resizable.

            var isMaximized = WindowState == System.Windows.WindowState.Maximized;
            var x = isMaximized ? RestoreBounds.Left : Left;
            var y = isMaximized ? RestoreBounds.Top : Top;
            var w = isMaximized ? RestoreBounds.Width : Width;
            var h = isMaximized ? RestoreBounds.Height : Height;

            // Plan 06-04 (SEC-03 / SEC-05): pick up the VM's current Security values
            // (bindings may have mutated them since OnSourceInitialized). Fall back to
            // the loaded record when the VM is missing or mid-teardown.
            var security = vm?.CurrentSecuritySettings ?? _loadedSettings.Security;

            // Phase 9 (D-02): capture card expand/collapse state from tree ViewModel
            var treeVm = _connectionTreeControl?.DataContext as ViewModels.ConnectionTreeViewModel;
            var propertiesPanel = treeVm?.GetPropertiesPanelSettings()
                ?? _loadedSettings.PropertiesPanel
                ?? PropertiesPanelRecord.Default;

            // Phase 14 (UX-02): capture current text scale from ViewModel
            var appearance = vm?.CurrentAppearanceSettings
                ?? _loadedSettings.Appearance
                ?? AppearanceRecord.Default;

            // Phase 18: capture bulk operations and uninstall settings
            var bulkOps = vm?.CurrentBulkOperationsSettings
                ?? _loadedSettings.BulkOperations
                ?? BulkOperationsRecord.Default;

            var uninstallSettings = vm?.CurrentUninstallSettings
                ?? _loadedSettings.Uninstall
                ?? UninstallRecord.Default;

            var updated = _loadedSettings with
            {
                Window = new WindowStateRecord(x, y, w, h, isMaximized, sidebarOpen, sidebarWidth),
                Security = security,
                PropertiesPanel = propertiesPanel,
                Appearance = appearance,
                BulkOperations = bulkOps,
                Uninstall = uninstallSettings,
            };

            _windowState.SaveAsync(updated).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to save window state on close");
        }
    }

    /// <summary>
    /// Phase 14 Plan 14-02 (UX-02): overwrites the DynamicResource font-size keys in
    /// <see cref="Application.Current.Resources"/> so all typography styles update
    /// immediately. Called on startup with the persisted scale and at runtime via the
    /// <see cref="ViewModels.MainWindowViewModel.SetTextScaleCallback"/> callback.
    /// </summary>
    internal static void ApplyTextScale(TextScale scale)
    {
        // Scale offsets: Small = -2px, Default = 0, Large = +2px
        var offset = scale switch
        {
            TextScale.Tiny => -4.0,
            TextScale.Small => -2.0,
            TextScale.Large => 2.0,
            _ => 0.0
        };

        // Base sizes match TypographyStyles.xaml defaults
        Application.Current.Resources["DeskbridgeSectionFontSize"] = 11.0 + offset;
        Application.Current.Resources["DeskbridgeCaptionFontSize"] = 12.0 + offset;
        Application.Current.Resources["DeskbridgeBodyFontSize"] = 14.0 + offset;
        Application.Current.Resources["DeskbridgeCardTitleFontSize"] = 16.0 + offset;
        Application.Current.Resources["DeskbridgeSubtitleFontSize"] = 20.0 + offset;
        Application.Current.Resources["DeskbridgeHintFontSize"] = 12.0 + offset;
    }

    /// <summary>
    /// Phase 16 (STAB-03 / T-16-03): debounced resize handler. Resets the 500ms timer
    /// on every SizeChanged event. Only fires <see cref="OnResizeSettled"/> when the
    /// user stops dragging, preventing flooding of <c>UpdateSessionDisplaySettings</c>.
    /// </summary>
    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RestartResolutionDebounce();
    }

    /// <summary>
    /// Shared debounce restart for the two dynamic-resolution triggers: viewport
    /// SizeChanged (Phase 16 STAB-03) and window DPI change (audit A5). Both route
    /// through <see cref="OnResizeSettled"/>, which re-measures the viewport's
    /// physical pixels and DPI at fire time — so a DPI change needs no dedicated
    /// measurement path.
    /// </summary>
    private void RestartResolutionDebounce()
    {
        _resizeTimer?.Stop();
        _resizeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _resizeTimer.Tick -= OnResizeSettled;
        _resizeTimer.Tick += OnResizeSettled;
        _resizeTimer.Start();
    }

    /// <summary>
    /// [CITED: audit A5 + WINFORMS-HOST-AIRSPACE §PerMonitorV2] Manual DPI-change
    /// handler. The app manifest declares PerMonitorV2, so WPF re-lays-out on a
    /// cross-monitor drag keeping the LOGICAL size roughly constant — meaning
    /// <c>ViewportGrid.SizeChanged</c> does not reliably fire and the remote session
    /// kept the old monitor's resolution with SmartSizing off (viewport-matched
    /// connects set SmartSizing=false, so there was no bitmap-scaling fallback).
    ///
    /// <para>Route through the SAME debounce as the resize path: when the timer
    /// settles, <see cref="OnResizeSettled"/> re-measures physical pixels + dpiPercent
    /// from the CURRENT <c>TransformToDevice</c> (already updated for the new monitor)
    /// and calls <c>UpdateResolution</c> on the active host, which itself falls back
    /// to SmartSizing when the server rejects dynamic resize. Future connects need no
    /// stored-state update — <c>SetViewportDimensions</c> is measured fresh in
    /// <see cref="OnHostMounted"/> at each mount.</para>
    ///
    /// <para>Also invalidate any in-flight drag snapshot: a cross-monitor drag IS a
    /// size-move, so the AirspaceSwapper may be showing a frozen bitmap captured at
    /// the old DPI, which would render stretched until WM_EXITSIZEMOVE.</para>
    /// </summary>
    protected override void OnDpiChanged(System.Windows.DpiScale oldDpi, System.Windows.DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        try
        {
            _airspace.InvalidateSnapshots();
        }
        catch (Exception ex)
        {
            // T-04-EXC: the invalidation path runs GDI/imaging interop
            // (CaptureHwnd → CreateBitmapSourceFromHBitmap) that surfaces COM-family
            // exceptions — log type + HResult only, never ex.Message, matching every
            // other COM-adjacent catch in this codebase (review finding).
            Serilog.Log.Warning(
                "Failed to invalidate drag snapshots on DPI change: {ExceptionType} HResult={HResult:X8}",
                ex.GetType().Name, ex.HResult);
        }

        RestartResolutionDebounce();
    }

    /// <summary>
    /// Phase 16 (STAB-03): fires 500ms after the last resize event. Measures the
    /// viewport's physical pixel dimensions and calls <see cref="RdpHostControl.UpdateResolution"/>
    /// on the active host to dynamically adjust the remote session resolution.
    /// </summary>
    private void OnResizeSettled(object? sender, EventArgs e)
    {
        _resizeTimer?.Stop();

        // Only update if the active host is an RDP session
        var activeId = _tabHostManager.ActiveId;
        if (activeId is null) return;
        var host = _tabHostManager.GetHost(activeId.Value);
        if (host is not RdpHostControl activeRdp) return;

        var (w, h) = ViewportMeasurement.GetPhysicalPixelSize(ViewportGrid);
        var source = System.Windows.PresentationSource.FromVisual(ViewportGrid);
        var dpiPercent = source?.CompositionTarget is { } ct
            ? ViewportMeasurement.GetDpiPercent(ct.TransformToDevice.M11)
            : 100.0;
        activeRdp.UpdateResolution(w, h, dpiPercent);
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

            // [CITED: audit A3] Defense in depth: purge any pre-existing WFH with
            // this connection's Tag before parenting the fresh one. The primary
            // cleanup is in OnReconnectOverlayRequested (dead host removed at
            // overlay-request time); this backstop covers any path where that
            // purge failed (e.g. the Host getter threw because the host was
            // already disposed). Only SAME-Tag children are touched — the Phase 4
            // WR-02 remove-ALL loop stays deleted (it contradicted "never
            // re-parent" for other tabs' LIVE hosts). A same-Tag leftover is by
            // construction a dead prior generation of this connection: the
            // coordinator's OnHostCreated duplicate guard means a live host for
            // this id would have prevented this mount entirely.
            for (var i = HostContainer.Children.Count - 1; i >= 0; i--)
            {
                if (HostContainer.Children[i] is WindowsFormsHost stale &&
                    stale.Tag is Guid staleId && staleId == host.ConnectionId)
                {
                    _airspace.UnregisterHost(stale);
                    HostContainer.Children.RemoveAt(i);
                }
            }

            // D-04 persistent container: add WFH to HostContainer ONCE. Tag
            // correlates back to ConnectionId for tab switching and overlay
            // routing.
            rdp.Host.Tag = host.ConnectionId;
            HostContainer.Children.Add(rdp.Host);

            // Force synchronous layout so AxHost's HWND is realized BEFORE
            // ConnectStage (coordinator raises HostMounted on HostCreatedEvent
            // Order=200, ConnectStage is Order=300). UpdateLayout is blocking
            // and does NOT pump the message queue.
            HostContainer.UpdateLayout();

            _airspace.RegisterHost(rdp.Host, ViewportSnapshot);

            // STAB-03: Measure viewport physical pixels and pass to RdpHostControl
            // so RdpConnectionConfigurator sets DesktopWidth/Height to match.
            // Must happen AFTER UpdateLayout (so ViewportGrid has final dimensions)
            // and BEFORE ConnectStage (Order=300) calls ConnectAsync.
            var (vpWidth, vpHeight) = ViewportMeasurement.GetPhysicalPixelSize(ViewportGrid);
            var source = System.Windows.PresentationSource.FromVisual(ViewportGrid);
            var dpiPercent = source?.CompositionTarget is { } ct
                ? ViewportMeasurement.GetDpiPercent(ct.TransformToDevice.M11)
                : 100.0;
            rdp.SetViewportDimensions(vpWidth, vpHeight, dpiPercent);

            // TabHostManager's OnHostMounted handler runs via the same coordinator
            // event and publishes TabSwitchedEvent(previous, thisHost). OnTabSwitched
            // (below) then flips Visibility across every child of HostContainer so
            // the just-mounted host becomes Visible and the prior active collapses.
        });
    }

    /// <summary>
    /// Phase 5 fire-and-forget close refactor (2026-04-14): WFH removal is now
    /// owned by <see cref="OnTabClosedSync"/> which fires on the synchronous
    /// <see cref="TabClosedEvent"/>. This handler only handles the
    /// <see cref="AirspaceSwapper"/> bookkeeping that remains coupled to the
    /// coordinator's lifecycle. The subscription stays live to prevent
    /// event-handler leaks if the coordinator's internals change in a future
    /// phase. No WFH removal here — that would race with OnTabClosedSync.
    /// </summary>
    private void OnHostUnmounted(object? sender, IProtocolHost host)
    {
        if (host is not RdpHostControl rdp) return;
        Dispatcher.Invoke(() =>
        {
            _airspace.UnregisterHost(rdp.Host);
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
        WindowsFormsHost? outgoing = null;
        WindowsFormsHost? incoming = null;

        // Identify outgoing (currently visible) and incoming (target) hosts.
        foreach (var child in HostContainer.Children)
        {
            if (child is WindowsFormsHost wfh && wfh.Tag is Guid id)
            {
                if (id == activeId)
                    incoming = wfh;
                else if (wfh.Visibility == Visibility.Visible)
                    outgoing = wfh;
            }
        }

        if (incoming is null) return;
        if (outgoing == incoming) return;

        // STAB-02: capture outgoing frame as bitmap overlay before hiding. This
        // covers the single-frame black gap between the WPF layout pass completing
        // and the incoming Win32 child HWND finishing its first WM_PAINT. The
        // bitmap is shown on the shared ViewportSnapshot Image registered in
        // AirspaceSwapper, which sits at the same Z-position as the HWND viewport.
        bool snapshotTaken = false;
        if (outgoing is not null)
        {
            snapshotTaken = _airspace.SnapshotSingleHost(outgoing);
        }

        // Apply visibility: exactly one WFH Visible + IsEnabled, all others Collapsed.
        // Use Collapsed (not Hidden). AirspaceSwapper.WndProc documents that
        // Hidden tears down the AxHost child HWND on some servers (e.g. xrdp),
        // raising OnDisconnected with discReason=2 and ending the live RDP
        // session. Collapsed removes the WFH from the layout pass without
        // destroying its child HWND, so the RDP session survives tab switches.
        foreach (var child in HostContainer.Children)
        {
            if (child is WindowsFormsHost wfh)
            {
                var isActive = wfh.Tag is Guid id && id == activeId;
                wfh.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
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

        // STAB-02: clear snapshot overlay after the incoming WFH has had a chance
        // to paint. DispatcherPriority.Loaded fires after layout + render, giving
        // the incoming HWND time to process its first WM_PAINT so the user sees
        // the new session frame instead of black.
        if (snapshotTaken && outgoing is not null)
        {
            var hostToClean = outgoing;
            Dispatcher.InvokeAsync(() =>
            {
                _airspace.ClearSingleHostSnapshot(hostToClean);
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Hotfix (2026-04-14): synchronous visual removal of a closed tab's WFH. Pairs
    /// with the fire-and-forget disconnect pipeline in TabHostManager.CloseTabAsync —
    /// the coordinator's HostUnmounted event fires AFTER the background disconnect
    /// completes (seconds later), but the WFH needs to leave the visual tree NOW so
    /// the empty-state placeholder can become visible when the last tab closes.
    /// This is the SINGLE source of truth for WFH removal — OnHostUnmounted is a
    /// pure no-op for visual state (it only does AirspaceSwapper bookkeeping).
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
                    // [CITED: audit A3] Also drop the AirspaceSwapper registration —
                    // this removal path runs seconds before OnHostUnmounted's
                    // bookkeeping (fire-and-forget disconnect pipeline), and any
                    // phantom duplicate removed here would never see HostUnmounted
                    // at all. UnregisterHost is idempotent, so the later
                    // OnHostUnmounted call for the primary host is harmless.
                    _airspace.UnregisterHost(wfh);
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

            // [CITED: audit A3] Purge the dead session's WFH instead of merely
            // hiding it (the previous D-07 HideWithoutSnapshot token). The
            // coordinator's drop path deliberately never raises HostUnmounted for
            // this host (the overlay must stay visible over the viewport), so
            // without this purge the dead WFH stayed parented in HostContainer and
            // registered in the AirspaceSwapper forever: one stale _hosts entry per
            // drop (iterated on every drag), and after a successful reconnect a
            // disposed orphan WFH sharing the fresh host's Tag — which broke the
            // single-visible-WFH invariant because SetActiveHostVisibility sets ALL
            // Tag matches Visible.
            //
            // ORDERING (verified against ConnectionCoordinator.OnDisconnectedAfterConnect):
            // since audit C2 the coordinator defers host.Dispose() via
            // _dispatcher.BeginInvoke, and it raises ReconnectOverlayRequested
            // synchronously from the same dispatcher frame AFTER queueing that
            // dispose. This handler's Dispatcher.Invoke therefore runs inline on
            // the dispatcher thread before the frame unwinds — the queued dispose
            // cannot have run yet, so rdp.Host is still valid here. Capture the
            // WFH reference synchronously.
            //
            // Removing a DEAD host's WFH from the visual tree is NOT the forbidden
            // re-parenting of RDP-ACTIVEX-PITFALLS §1 — it is never re-added; a
            // successful reconnect mounts a brand-new host/WFH pair.
            var host = _tabHostManager.GetHost(id);
            if (host is RdpHostControl rdp)
            {
                try
                {
                    var deadWfh = rdp.Host;
                    _airspace.UnregisterHost(deadWfh);
                    HostContainer.Children.Remove(deadWfh);
                }
                catch
                {
                    // Best-effort: if the host is already disposed the Host getter
                    // throws ObjectDisposedException; OnHostMounted's same-Tag purge
                    // is the defense-in-depth backstop. The overlay renders regardless.
                }
            }

            // D-14: overlay mounts inside HostContainer (not at ViewportGrid level).
            // Its Visibility follows the tab's active state via Tag + SetActiveHostVisibility.
            HostContainer.Children.Add(ctrl);
            System.Windows.Controls.Panel.SetZIndex(ctrl, 1000);

            // Initial visibility — if Reconnecting fires on a background tab, the
            // overlay starts Collapsed and becomes Visible on tab switch.
            ctrl.Visibility = (_tabHostManager.ActiveId == id) ? Visibility.Visible : Visibility.Collapsed;

            _overlays[id] = (ctrl, vm);
        });
    }

    /// <summary>
    /// Remove the overlay for a specific tab. No-op if no overlay is open for
    /// <paramref name="id"/>. [CITED: audit A3] The airspace token disposal is
    /// gone — the dead WFH is removed outright at overlay-request time, so there
    /// is no captured visibility left to restore here.
    /// </summary>
    private void CloseOverlayFor(Guid id)
    {
        if (!_overlays.TryGetValue(id, out var entry)) return;
        _overlays.Remove(id);
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
        // Plan 06-03 CMD-01: Ctrl+Shift+P opens the command palette. Handled HERE
        // (not in KeyboardShortcutRouter) because the router has no
        // IContentDialogService / IAppLockState dependency. Q6 gate: Ctrl+Shift+P
        // is a no-op while the app is locked so the palette cannot render above
        // the lock overlay. The key event is still consumed (e.Handled=true) so
        // it doesn't bubble to the focused AxHost.
        if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (!_lockState.IsLocked)
            {
                _ = OpenCommandPaletteAsync();
            }
            e.Handled = true;
            return;
        }

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

    /// <summary>
    /// Plan 06-03 CMD-01: open the command palette via <see cref="IContentDialogService"/>
    /// using the transient <see cref="CommandPaletteDialog"/> factory. Idempotent —
    /// a held-down Ctrl+Shift+P does NOT stack dialogs. Exceptions are swallowed
    /// with a Serilog warning so a dialog-host misconfiguration can't crash the app.
    /// </summary>
    private async Task OpenCommandPaletteAsync()
    {
        if (_paletteOpen) return;
        _paletteOpen = true;
        try
        {
            var dialog = _paletteFactory();
            _airspace.SnapshotAndHideAll();
            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                _airspace.RestoreAll();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to open command palette");
        }
        finally
        {
            _paletteOpen = false;
        }
    }

    /// <summary>
    /// Phase 6.1: opens the change password/PIN dialog from the Settings panel button.
    /// Same pattern as the command palette — factory-created transient dialog shown
    /// via the ContentDialogHost.
    /// </summary>
    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lockState.IsLocked) return;
        if (_changePasswordFactory is null) return;
        try
        {
            var dialog = _changePasswordFactory();
            _airspace.SnapshotAndHideAll();
            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                _airspace.RestoreAll();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to open change password dialog");
        }
    }

    // ----------------------------------------------------------- Phase 7 Plan 07-04 import/export

    /// <summary>
    /// Phase 7 Plan 07-04: sets the import wizard factory after DI build so
    /// the settings panel import button can open the wizard. Called from
    /// App.OnStartup after the service provider is built.
    /// </summary>
    internal void SetImportWizardFactory(Func<ImportWizardDialog> factory) =>
        _importWizardFactory = factory;

    /// <summary>
    /// Phase 7 Plan 07-04 (MIG-02): Settings panel "Import Connections..." button.
    /// Opens the import wizard ContentDialog.
    /// </summary>
    private async void ImportConnectionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lockState.IsLocked) return;
        if (_importWizardFactory is null) return;
        try
        {
            var dialog = _importWizardFactory();
            _airspace.SnapshotAndHideAll();
            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                _airspace.RestoreAll();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to open import wizard");
        }
    }

    /// <summary>
    /// Phase 7 Plan 07-04 (MIG-06): Settings panel "Export as JSON" button.
    /// Shows SaveFileDialog then writes JSON export (no credentials).
    /// </summary>
    private async void ExportJsonButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lockState.IsLocked) return;
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = "deskbridge-connections.json"
            };
            if (dlg.ShowDialog() == true)
            {
                var store = ((App)Application.Current).Services?.GetService(typeof(IConnectionStore)) as IConnectionStore
                    ?? throw new InvalidOperationException("IConnectionStore not available");
                var json = ConnectionExporter.ExportJson(store.GetAll(), store.GetGroups());
                await System.IO.File.WriteAllTextAsync(dlg.FileName, json);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to export connections as JSON");
        }
    }

    /// <summary>
    /// Phase 7 Plan 07-04 (MIG-06): Settings panel "Export as CSV" button.
    /// Shows SaveFileDialog then writes CSV export (no credentials).
    /// </summary>
    private async void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lockState.IsLocked) return;
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = "deskbridge-connections.csv"
            };
            if (dlg.ShowDialog() == true)
            {
                var store = ((App)Application.Current).Services?.GetService(typeof(IConnectionStore)) as IConnectionStore
                    ?? throw new InvalidOperationException("IConnectionStore not available");
                var csv = ConnectionExporter.ExportCsv(store.GetAll(), store.GetGroups());
                await System.IO.File.WriteAllTextAsync(dlg.FileName, csv);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to export connections as CSV");
        }
    }

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-02 / T-07-04): shows the update confirmation dialog
    /// after download completes. If the user clicks "Restart Now", applies the update
    /// and restarts. If "Later", the update applies on next manual restart.
    /// </summary>
    private async Task ShowUpdateConfirmDialogAsync()
    {
        try
        {
            var dialog = new UpdateConfirmDialog(RootContentDialog);
            _airspace.SnapshotAndHideAll();
            try
            {
                var result = await dialog.ShowAsync();
                if (result == Wpf.Ui.Controls.ContentDialogResult.Primary)
                {
                    // T-07-04: user confirmed restart — apply update.
                    // ApplyUpdatesAndRestart calls Environment.Exit(); active sessions
                    // will be terminated. The confirmation dialog warned the user.
                    ViewModel.UpdateService?.ApplyUpdatesAndRestart();
                }
            }
            finally
            {
                _airspace.RestoreAll();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to show update confirmation dialog");
        }
    }

    /// <summary>
    /// Plan 06-03 D-05 / CMD-04: observe <see cref="MainWindowViewModel.IsFullscreen"/>
    /// and apply WindowStyle+WindowState. Entering fullscreen saves the current
    /// style/state so the restore is exact (including Maximized-before-F11).
    ///
    /// Phase 6.1: also handles <see cref="MainWindowViewModel.RequireMasterPassword"/>
    /// toggle changes — triggers confirmation flow when disabling, setup flow when
    /// re-enabling without an existing password.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.IsFullscreen))
        {
            var vm = (ViewModels.MainWindowViewModel)sender!;
            if (vm.IsFullscreen)
            {
                _savedWindowStyle = WindowStyle;
                _savedWindowState = WindowState;
                WindowStyle = System.Windows.WindowStyle.None;
                WindowState = System.Windows.WindowState.Maximized;
            }
            else
            {
                WindowStyle = _savedWindowStyle;
                WindowState = _savedWindowState;
            }
            return;
        }

        if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.RequireMasterPassword))
        {
            _ = HandleRequireMasterPasswordToggleAsync();
        }
    }

    /// <summary>
    /// Phase 6.1: when the user toggles RequireMasterPassword OFF, show a confirmation
    /// dialog requiring their current password/PIN. On confirm: delete auth.json. On
    /// cancel: revert the toggle. When toggling ON with no password set: trigger
    /// first-run setup via the lock overlay.
    /// </summary>
    private async Task HandleRequireMasterPasswordToggleAsync()
    {
        var vm = ViewModel;
        if (_masterPasswordService is null || _contentDialogService is null) return;

        if (!vm.RequireMasterPassword)
        {
            // Toggling OFF — require confirmation
            if (!_masterPasswordService.IsMasterPasswordSet())
            {
                // No password was set — just let it stay off
                return;
            }

            try
            {
                var isPinMode = _masterPasswordService.GetAuthMode() == "pin";
                var prompt = new CredentialPromptDialog(
                    _contentDialogService.GetDialogHostEx()!,
                    "Disable Protection",
                    prefillUsername: null,
                    prefillDomain: null);
                prompt.Title = isPinMode
                    ? "Enter your PIN to disable protection"
                    : "Enter your password to disable protection";
                prompt.HideUsernameAndDomain();
                if (isPinMode) prompt.UsePinMode();
                prompt.PrimaryButtonText = "Disable";

                _airspace.SnapshotAndHideAll();
                ContentDialogResult result;
                try
                {
                    result = await prompt.ShowAsync();
                }
                finally
                {
                    _airspace.RestoreAll();
                }

                if (result == ContentDialogResult.Primary)
                {
                    if (!_masterPasswordService.VerifyMasterPassword(prompt.EnteredPassword))
                    {
                        vm.ApplySecuritySettings(vm.CurrentSecuritySettings with { RequireMasterPassword = true });
                        return;
                    }

                    _masterPasswordService.DeleteAuthFile();
                    vm.IsMasterPasswordConfigured = false;

                    if (LockController is not null)
                    {
                        await LockController.ForceDisableAsync();
                    }
                }
                else
                {
                    vm.ApplySecuritySettings(vm.CurrentSecuritySettings with { RequireMasterPassword = true });
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to show disable-password confirmation dialog");
                // Revert on error
                vm.ApplySecuritySettings(vm.CurrentSecuritySettings with { RequireMasterPassword = true });
            }
        }
        else
        {
            // Toggling ON — if no password is set, trigger first-run setup
            if (!_masterPasswordService.IsMasterPasswordSet())
            {
                // Update controller FIRST so the lock overlay can proceed
                if (LockController is not null)
                {
                    LockController.RequireMasterPassword = true;
                }
                _eventBus.Publish(new AppLockedEvent(LockReason.Manual));
            }
            else
            {
                // Password already exists, just re-enabling the requirement
                if (LockController is not null)
                {
                    LockController.RequireMasterPassword = true;
                }
            }
            vm.IsMasterPasswordConfigured = _masterPasswordService.IsMasterPasswordSet();
        }
    }
}
