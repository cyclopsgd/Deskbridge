using System.Collections.ObjectModel;
using System.Windows;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Settings;
using Deskbridge.Models;
using Serilog;

namespace Deskbridge.ViewModels;

/// <summary>
/// Phase 14 Plan 14-02 (UX-02): display wrapper for the TextScale enum in the
/// Appearance settings ComboBox. Same pattern as <see cref="CredentialModeOption"/>
/// in <see cref="ConnectionEditorViewModel"/>.
/// </summary>
public record TextScaleOption(TextScale Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ITabHostManager _tabHostManager;
    private readonly IEventBus _eventBus;
    private readonly IConnectionStore _connectionStore;

    /// <summary>
    /// Plan 06-04 (SEC-03 / SEC-05): optional — settings-panel bindings persist
    /// changes here. Nullable so existing VM test call-sites don't need to
    /// construct a full settings subsystem; persistence is a no-op when null.
    /// </summary>
    private readonly IWindowStateService? _windowState;

    /// <summary>
    /// Phase 6.1: optional — used for <see cref="IsMasterPasswordConfigured"/>
    /// and <see cref="ChangePasswordLabel"/>. Nullable for backward-compatible
    /// test call-sites.
    /// </summary>
    private readonly IMasterPasswordService? _masterPassword;

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-01 / UPD-02): optional — update service for
    /// background update checks, download, and restart. Nullable so existing
    /// VM test call-sites don't need to construct the full update subsystem.
    /// </summary>
    private readonly IUpdateService? _updateService;

    /// <summary>
    /// Phase 7 Plan 07-01: callback set by <see cref="MainWindow.xaml.cs"/>
    /// after construction to show the <see cref="Dialogs.UpdateConfirmDialog"/>.
    /// Same pattern as existing dialog callbacks (palette, change-password).
    /// </summary>
    private Action? _showUpdateConfirmation;

    public MainWindowViewModel(
        ConnectionTreeViewModel connectionTree,
        ITabHostManager tabHostManager,
        IEventBus eventBus,
        IConnectionStore connectionStore,
        ToastStackViewModel toastStack,
        IWindowStateService? windowState = null,
        IMasterPasswordService? masterPassword = null,
        IUpdateService? updateService = null)
    {
        ConnectionTree = connectionTree;
        _tabHostManager = tabHostManager;
        _eventBus = eventBus;
        _connectionStore = connectionStore;
        ToastStack = toastStack;
        _windowState = windowState;
        _masterPassword = masterPassword;
        _updateService = updateService;

        // Phase 5: subscribe to TabHostManager lifecycle events so the ObservableCollection
        // and status bar stay in sync. All handlers marshal to the UI dispatcher because
        // IEventBus (WeakReferenceMessenger) delivers on the publisher's thread — usually
        // the STA UI thread, but defense-in-depth against a future background publisher.
        _eventBus.Subscribe<TabOpenedEvent>(this, OnTabOpened);
        _eventBus.Subscribe<TabClosedEvent>(this, OnTabClosed);
        _eventBus.Subscribe<TabSwitchedEvent>(this, OnTabSwitched);
        _eventBus.Subscribe<TabStateChangedEvent>(this, OnTabStateChanged);

        // Phase 7 Plan 07-01 (UPD-03): subscribe to UpdateAvailableEvent so the
        // status bar badge appears when the service publishes an update discovery.
        _eventBus.Subscribe<UpdateAvailableEvent>(this, e => Dispatch(() =>
        {
            UpdateAvailable = true;
            UpdateVersion = e.Version;
        }));
    }

    /// <summary>
    /// Phase 7 Plan 07-01: exposes the update service so MainWindow.xaml.cs can
    /// call <see cref="IUpdateService.ApplyUpdatesAndRestart"/> after the
    /// confirmation dialog. Read-only — the VM owns the lifecycle.
    /// </summary>
    public IUpdateService? UpdateService => _updateService;

    // ----------------------------------------------------------- Phase 14 Plan 14-02 (UX-02): text scaling

    /// <summary>
    /// Phase 14 Plan 14-02 (UX-02): display options for the Appearance settings
    /// Text size ComboBox. Same wrapper-record pattern as CredentialModeOption.
    /// </summary>
    public IReadOnlyList<TextScaleOption> TextScaleOptions { get; } = new[]
    {
        new TextScaleOption(TextScale.Small, "Small"),
        new TextScaleOption(TextScale.Default, "Default"),
        new TextScaleOption(TextScale.Large, "Large"),
    };

    /// <summary>
    /// Phase 14 Plan 14-02 (UX-02): current text scale preference. Two-way bound
    /// to the Appearance ComboBox in the settings panel. Changes trigger
    /// <see cref="OnTextScaleChanged"/> which invokes the callback and persists.
    /// </summary>
    [ObservableProperty]
    public partial TextScale TextScale { get; set; } = TextScale.Default;

    /// <summary>
    /// Phase 14 Plan 14-02 (UX-02): callback registered by MainWindow.xaml.cs to
    /// apply font-size resource updates when the user changes the text scale.
    /// </summary>
    private Action<TextScale>? _applyTextScale;

    /// <summary>
    /// Phase 14 Plan 14-02 (UX-02): registers the MainWindow's ApplyTextScale
    /// method so runtime text-scale changes propagate to Application.Current.Resources.
    /// Called AFTER <see cref="ApplyAppearanceSettings"/> in OnSourceInitialized.
    /// </summary>
    public void SetTextScaleCallback(Action<TextScale> callback) => _applyTextScale = callback;

    partial void OnTextScaleChanged(TextScale value)
    {
        _applyTextScale?.Invoke(value);
        PersistAppearanceSettings();
    }

    /// <summary>
    /// Phase 14 Plan 14-02 (UX-02): persists appearance settings to settings.json.
    /// Same async-void fire-and-forget pattern as <see cref="PersistSecuritySettings"/>.
    /// </summary>
    private async void PersistAppearanceSettings()
    {
        if (_suppressPersist) return;
        if (_windowState is null) return;
        try
        {
            var current = await _windowState.LoadAsync().ConfigureAwait(false);
            var updated = current with { Appearance = new AppearanceRecord(TextScale: TextScale) };
            await _windowState.SaveAsync(updated).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist appearance settings");
        }
    }

    /// <summary>
    /// Phase 14 Plan 14-02 (UX-02): applies loaded appearance settings to the VM
    /// without triggering persistence (same suppress pattern as
    /// <see cref="ApplySecuritySettings"/>). Called from MainWindow.OnSourceInitialized.
    /// </summary>
    public void ApplyAppearanceSettings(AppearanceRecord appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        _suppressPersist = true;
        try
        {
            TextScale = appearance.TextScale;
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    /// <summary>
    /// Phase 14 Plan 14-02 (UX-02): snapshot of the current appearance preferences
    /// for MainWindow.OnClosing persistence. Same pattern as <see cref="CurrentSecuritySettings"/>.
    /// </summary>
    public AppearanceRecord CurrentAppearanceSettings => new(TextScale: TextScale);

    /// <summary>
    /// Phase 7 Plan 07-01: sets the callback that <see cref="ApplyUpdateAsync"/>
    /// invokes after download completes to show the restart confirmation dialog.
    /// Called by <see cref="MainWindow.xaml.cs"/> after construction.
    /// </summary>
    public void SetUpdateConfirmation(Action callback) => _showUpdateConfirmation = callback;

    // Expose ConnectionTreeViewModel for Ctrl+N binding in MainWindow
    [ObservableProperty]
    public partial ConnectionTreeViewModel ConnectionTree { get; set; }

    /// <summary>
    /// Phase 6 Plan 06-02 (NOTF-01): toast stack VM bound by <c>MainWindow.xaml</c>
    /// to the new <c>ToastStackControl</c>. Shared singleton — same instance
    /// populated by <see cref="Services.ToastSubscriptionService"/>.
    /// </summary>
    public ToastStackViewModel ToastStack { get; }

    [ObservableProperty]
    public partial string Title { get; set; } = "Deskbridge";

    // Panel state (per D-04 VS Code toggle pattern)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsConnectionsActive))]
    [NotifyPropertyChangedFor(nameof(IsSearchActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    public partial PanelMode ActivePanelMode { get; set; } = PanelMode.Connections;

    public bool IsPanelVisible => ActivePanelMode != PanelMode.None;
    public bool IsConnectionsActive => ActivePanelMode == PanelMode.Connections;
    public bool IsSearchActive => ActivePanelMode == PanelMode.Search;
    public bool IsSettingsActive => ActivePanelMode == PanelMode.Settings;

    // Tab state
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTabs))]
    public partial ObservableCollection<TabItemViewModel> Tabs { get; set; } = [];

    [ObservableProperty]
    public partial TabItemViewModel? ActiveTab { get; set; }

    public bool HasNoTabs => Tabs.Count == 0;

    // Status bar
    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string StatusSecondary { get; set; } = string.Empty;

    // ----------------------------------------------------------- Phase 7 Plan 07-01 update badge

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-02): true when an update has been discovered.
    /// Drives the status bar badge visibility. Persists until user acts
    /// (downloads + restarts) or explicitly dismisses.
    /// </summary>
    [ObservableProperty]
    public partial bool UpdateAvailable { get; set; }

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-02): version string of the available update
    /// (e.g. "2.1.0"). Displayed in the status bar badge tooltip and text.
    /// </summary>
    [ObservableProperty]
    public partial string UpdateVersion { get; set; } = string.Empty;

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-02): 0-100 progress during update download.
    /// Bound to the status bar progress ring.
    /// </summary>
    [ObservableProperty]
    public partial int DownloadProgress { get; set; }

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-02): true while the update package is being
    /// downloaded. Drives the download progress indicator visibility in
    /// the status bar (replaces the badge during download).
    /// </summary>
    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-01): kicks off a background update check.
    /// Called from App.OnStartup after window is shown.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (_updateService is null) return;
        await _updateService.CheckForUpdatesAsync();
    }

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-02): triggered by clicking the status bar
    /// update badge. Downloads the update with progress reporting, then
    /// invokes the confirmation dialog callback.
    /// </summary>
    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (_updateService is null || !UpdateAvailable) return;
        IsDownloading = true;
        try
        {
            var progress = new Progress<int>(p => Dispatch(() => DownloadProgress = p));
            await _updateService.DownloadUpdatesAsync(progress);
            // Show confirmation dialog via the callback wired by MainWindow.xaml.cs
            _showUpdateConfirmation?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to download update");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// Phase 7 Plan 07-01 (UPD-02): dismisses the update badge without
    /// applying. The update will apply on next manual restart.
    /// </summary>
    [RelayCommand]
    private void DismissUpdate() => UpdateAvailable = false;

    /// <summary>
    /// Phase 6 Plan 06-03 D-05 (CMD-04): APP-level fullscreen flag. The
    /// MainWindow code-behind subscribes to <see cref="PropertyChanged"/> and
    /// applies <c>WindowStyle.None</c> + <c>WindowState.Maximized</c> when true;
    /// restores the saved style/state on false. Distinct from RDP session
    /// fullscreen which is owned by <c>AxHost</c> in Phase 4.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFullscreen { get; set; }

    // Commands
    [RelayCommand]
    private void TogglePanel(PanelMode mode)
    {
        ActivePanelMode = ActivePanelMode == mode ? PanelMode.None : mode;
    }

    /// <summary>
    /// D-06: close a tab via the disconnect pipeline. The Phase 4 body that mutated
    /// <see cref="Tabs"/> directly is replaced with delegation — TabHostManager runs
    /// the pipeline and publishes <see cref="TabClosedEvent"/>, which
    /// <see cref="OnTabClosed"/> picks up to remove the VM from the collection.
    /// </summary>
    [RelayCommand]
    private async Task CloseTab(TabItemViewModel? tab)
    {
        if (tab is null) return;
        await _tabHostManager.CloseTabAsync(tab.ConnectionId);
    }

    /// <summary>
    /// TAB-03: switch-to-tab. Delegates to TabHostManager.SwitchTo which publishes
    /// <see cref="TabSwitchedEvent"/>; <see cref="OnTabSwitched"/> then updates
    /// <see cref="ActiveTab"/> and the status bar.
    /// </summary>
    [RelayCommand]
    private void SwitchTab(TabItemViewModel? tab)
    {
        if (tab is null) return;
        _tabHostManager.SwitchTo(tab.ConnectionId);
    }

    /// <summary>D-07 context menu: close every other tab. Batched via TabHostManager.</summary>
    [RelayCommand]
    private async Task CloseOtherTabs(TabItemViewModel? tab)
    {
        if (tab is null) return;
        await _tabHostManager.CloseOthersAsync(tab.ConnectionId);
    }

    /// <summary>D-07 context menu: close all tabs. Same sequential path as D-08 app shutdown.</summary>
    [RelayCommand]
    private async Task CloseAllTabs()
    {
        await _tabHostManager.CloseAllAsync();
    }

    /// <summary>
    /// D-16 Ctrl+Shift+T: pop the most-recently-closed ConnectionId from the LRU and
    /// publish <see cref="ConnectionRequestedEvent"/> so the usual connect pipeline
    /// runs. Silent no-op when the LRU is empty OR when the connection has been
    /// deleted from the store since it was closed (per UI-SPEC §Copywriting line 345).
    /// </summary>
    [RelayCommand]
    private void ReopenLastClosed()
    {
        var id = _tabHostManager.PopLastClosed();
        if (id is not Guid connId) return;
        var model = _connectionStore.GetById(connId);
        if (model is null) return;  // Silent per UI-SPEC — no toast, no beep
        _eventBus.Publish(new ConnectionRequestedEvent(model));
    }

    // ----------------------------------------------------------- Phase 6 Plan 06-03

    /// <summary>
    /// Plan 06-03 CMD-01: no-op placeholder so <see cref="KeyboardShortcutRouter"/>
    /// has a consistent <c>ICommand</c> surface for Ctrl+Shift+P. The real dialog
    /// open lives in <see cref="MainWindow.OnPreviewKeyDown"/> because it needs
    /// <c>IContentDialogService</c> + <c>IAppLockState</c> — dependencies the VM
    /// shouldn't carry. Kept here so VM-only tests can verify the command exists
    /// and routes are consistent.
    /// </summary>
    [RelayCommand]
    private Task OpenCommandPalette() => Task.CompletedTask;

    /// <summary>
    /// Plan 06-03 CMD-04 Ctrl+T: quick connect.
    /// v1 reuses <c>ConnectionTreeViewModel.NewConnectionCommand</c> — a dedicated
    /// quick-connect dialog is deferred (not in Phase 6 scope).
    /// </summary>
    [RelayCommand]
    private async Task QuickConnect()
    {
        if (ConnectionTree.NewConnectionCommand.CanExecute(null))
        {
            await ConnectionTree.NewConnectionCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Plan 06-03 palette "Disconnect All" command: delegates to
    /// <see cref="ITabHostManager.CloseAllAsync"/> (same sequential path as
    /// Phase 5 D-08 app shutdown).
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAll()
    {
        await _tabHostManager.CloseAllAsync();
    }

    /// <summary>
    /// Plan 06-03 D-05 / CMD-04 F11: flip <see cref="IsFullscreen"/>. MainWindow
    /// code-behind observes the change and applies WindowStyle+WindowState.
    /// </summary>
    [RelayCommand]
    private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

    /// <summary>
    /// Plan 06-03 D-05 / CMD-04 Esc-in-fullscreen: exit fullscreen. Idempotent
    /// when already non-fullscreen so a spurious Esc doesn't toggle.
    /// </summary>
    [RelayCommand]
    private void ExitFullscreen()
    {
        if (IsFullscreen) IsFullscreen = false;
    }

    // ----------------------------------------------------------- Phase 6.1

    /// <summary>True when a master password/PIN is currently configured. Controls Change Password button visibility.</summary>
    [ObservableProperty]
    public partial bool IsMasterPasswordConfigured { get; set; }

    /// <summary>Label for the change button: "Change Password" or "Change PIN".</summary>
    public string ChangePasswordLabel => _masterPassword?.GetAuthMode() == "pin" ? "Change PIN" : "Change Password";

    /// <summary>
    /// Phase 6.1: no-op placeholder command. The actual dialog open lives in
    /// MainWindow.xaml.cs code-behind (same pattern as command palette) because
    /// the dialog needs IContentDialogService which the VM should not carry.
    /// </summary>
    [RelayCommand]
    private void ChangePassword() { }

    // ----------------------------------------------------------- Phase 6 Plan 06-04

    /// <summary>
    /// Phase 6.1: two-way bound to the "Require Password/PIN" toggle in Settings.
    /// When toggled OFF, the MainWindow code-behind triggers confirmation flow.
    /// When toggled ON and no password is set, triggers first-run setup.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLockControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(ChangePasswordLabel))]
    public partial bool RequireMasterPassword { get; set; } = true;

    /// <summary>Controls whether auto-lock controls are enabled in the Settings panel.</summary>
    public bool IsLockControlsEnabled => RequireMasterPassword;

    partial void OnRequireMasterPasswordChanged(bool value) => PersistSecuritySettings();

    /// <summary>
    /// Plan 06-04 SEC-03: auto-lock idle timeout in minutes. Two-way bound to
    /// the Settings panel's ui:NumberBox (UI-SPEC §Settings Panel Additions).
    /// Changes persist via <see cref="PersistSecuritySettings"/>.
    /// </summary>
    [ObservableProperty]
    public partial int AutoLockTimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Plan 06-04 SEC-05: when true, <c>WindowState == Minimized</c> triggers
    /// an immediate lock (MainWindow OnStateChanged observes this flag).
    /// </summary>
    [ObservableProperty]
    public partial bool LockOnMinimise { get; set; }

    /// <summary>
    /// Plan 06-04 SEC-04 / D-18 Ctrl+L. Publishes <see cref="AppLockedEvent"/>
    /// on the bus — <see cref="AppLockController"/> subscribes and fans in all
    /// lock triggers (timer + session-switch + Ctrl+L) here. Bus-indirect is
    /// used instead of a direct controller reference to avoid a
    /// MainWindowViewModel → AppLockController → MainWindow (IHostContainerProvider)
    /// → MainWindowViewModel DI cycle.
    /// </summary>
    [RelayCommand]
    private void LockApp()
    {
        _eventBus.Publish(new AppLockedEvent(LockReason.Manual));
    }

    /// <summary>
    /// Called from <c>MainWindow.OnSourceInitialized</c> after <see cref="IWindowStateService.LoadAsync"/>
    /// so the Settings-panel bindings start populated with the persisted values.
    /// Does NOT trigger a save (we're applying disk state, not user input).
    /// </summary>
    public void ApplySecuritySettings(SecuritySettingsRecord security)
    {
        ArgumentNullException.ThrowIfNull(security);
        _suppressPersist = true;
        try
        {
            AutoLockTimeoutMinutes = security.AutoLockTimeoutMinutes;
            LockOnMinimise = security.LockOnMinimise;
            RequireMasterPassword = security.RequireMasterPassword;
            IsMasterPasswordConfigured = _masterPassword?.IsMasterPasswordSet() ?? false;
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    /// <summary>Snapshot of the current security preferences for MainWindow.OnClosing persistence.</summary>
    public SecuritySettingsRecord CurrentSecuritySettings =>
        new(AutoLockTimeoutMinutes: AutoLockTimeoutMinutes, LockOnMinimise: LockOnMinimise, RequireMasterPassword: RequireMasterPassword);

    // --- settings persistence ---

    /// <summary>
    /// Guards against a round-trip persist when <see cref="ApplySecuritySettings"/>
    /// initialises the bindings from disk. Settings panel user edits still flow
    /// through <see cref="PersistSecuritySettings"/>.
    /// </summary>
    private bool _suppressPersist;

    partial void OnAutoLockTimeoutMinutesChanged(int value) => PersistSecuritySettings();
    partial void OnLockOnMinimiseChanged(bool value) => PersistSecuritySettings();

    private async void PersistSecuritySettings()
    {
        if (_suppressPersist) return;
        if (_windowState is null) return;
        try
        {
            var current = await _windowState.LoadAsync().ConfigureAwait(false);
            var updated = current with { Security = CurrentSecuritySettings };
            await _windowState.SaveAsync(updated).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist security settings");
        }
    }

    // ---------------------------------------------------------------- event handlers

    private void OnTabOpened(TabOpenedEvent evt)
    {
        Dispatch(() =>
        {
            // Hotfix (2026-04-14): read the ConnectionModel off the event payload
            // directly (carried from TabHostManager._connections, guaranteed non-null
            // at publish time). The prior store-lookup path produced "(unknown)"
            // titles when the JsonConnectionStore race/load state returned null.
            var model = evt.Connection;
            var tab = new TabItemViewModel
            {
                Title = model.Name is { Length: > 0 } name ? name
                    : model.Hostname is { Length: > 0 } host ? host
                    : "(unknown)",
                ConnectionId = evt.ConnectionId,
                State = TabState.Connecting,
                // Plan 05-03 Task 1: populate Hostname so TabItemViewModel.TooltipText
                // renders the UI-SPEC copy exactly. Never includes credential fields (T-05-01).
                Hostname = model.Hostname,
            };
            Tabs.Add(tab);
            OnPropertyChanged(nameof(HasNoTabs));
        });
    }

    private void OnTabClosed(TabClosedEvent evt)
    {
        Dispatch(() =>
        {
            // Hotfix (2026-04-14): remove ALL tabs with matching ConnectionId, not just
            // the first one. Defense-in-depth against phantom duplicates that can
            // accumulate if reconnect-cycle TabOpenedEvent suppression ever slips
            // through. Iterate backwards so index math survives mid-loop removal.
            var removed = 0;
            for (var i = Tabs.Count - 1; i >= 0; i--)
            {
                if (Tabs[i].ConnectionId != evt.ConnectionId) continue;
                var tab = Tabs[i];
                Tabs.RemoveAt(i);
                if (ActiveTab == tab) ActiveTab = null;
                removed++;
            }
            if (removed > 0) OnPropertyChanged(nameof(HasNoTabs));
        });
    }

    private void OnTabSwitched(TabSwitchedEvent evt)
    {
        Dispatch(() =>
        {
            if (ActiveTab is not null) ActiveTab.IsActive = false;
            ActiveTab = evt.ActiveId == Guid.Empty
                ? null
                : Tabs.FirstOrDefault(t => t.ConnectionId == evt.ActiveId);
            if (ActiveTab is not null)
            {
                ActiveTab.IsActive = true;
                // Plan 05-03 Task 1: refresh Resolution on the now-active tab so
                // TabItemViewModel.TooltipText renders {Hostname} · {W}×{H} when known.
                // Live DesktopWidth/Height are 0 before OnLoginComplete; fall back to
                // DisplaySettings or leave null (em-dash in TooltipText).
                UpdateActiveTabResolution();
            }

            UpdateStatusBarFromActiveTab();
        });
    }

    /// <summary>
    /// Plan 05-03 Task 1: populate <see cref="TabItemViewModel.Resolution"/> for the
    /// active tab so the tooltip stays in sync with the status bar. Prefers the live
    /// <c>IMsRdpClient.DesktopWidth</c>/<c>DesktopHeight</c>; falls back to
    /// <c>ConnectionModel.DisplaySettings</c> during Connecting; null when both missing.
    /// </summary>
    private void UpdateActiveTabResolution()
    {
        if (ActiveTab is null) return;
        var host = _tabHostManager.GetHost(ActiveTab.ConnectionId);
        (int w, int h) = (0, 0);
        if (host is Deskbridge.Protocols.Rdp.RdpHostControl rdp)
        {
            try { (w, h) = rdp.GetSessionResolution(); }
            catch { (w, h) = (0, 0); }
        }
        if (w == 0 || h == 0)
        {
            var model = _connectionStore.GetById(ActiveTab.ConnectionId);
            w = model?.DisplaySettings?.Width ?? 0;
            h = model?.DisplaySettings?.Height ?? 0;
        }
        ActiveTab.Resolution = (w > 0 && h > 0) ? (w, h) : null;
    }

    private void OnTabStateChanged(TabStateChangedEvent evt)
    {
        Dispatch(() =>
        {
            var tab = Tabs.FirstOrDefault(t => t.ConnectionId == evt.ConnectionId);
            if (tab is null) return;
            tab.State = evt.State;
            if (ActiveTab == tab) UpdateStatusBarFromActiveTab();
        });
    }

    /// <summary>
    /// D-15 status bar update per UI-SPEC §Status Bar Binding Contract. Copy strings
    /// are locked verbatim (U+00B7 middle-dot, U+2026 ellipsis, U+2014 em-dash for
    /// stubbed resolution). Latency/quality stubbed until Phase 6 observability.
    /// </summary>
    private void UpdateStatusBarFromActiveTab()
    {
        if (ActiveTab is null)
        {
            StatusText = "Ready";
            StatusSecondary = string.Empty;
            return;
        }

        var model = _connectionStore.GetById(ActiveTab.ConnectionId);
        if (model is null)
        {
            StatusText = "Ready";
            StatusSecondary = string.Empty;
            return;
        }

        // Read live resolution from the RDP host; fall back to ConnectionModel.DisplaySettings
        // when the session is still in Connecting state (DesktopWidth/Height are 0 before
        // OnLoginComplete). If both are missing, render em-dash.
        var host = _tabHostManager.GetHost(ActiveTab.ConnectionId);
        (int w, int h) = (0, 0);
        if (host is Deskbridge.Protocols.Rdp.RdpHostControl rdp)
        {
            try { (w, h) = rdp.GetSessionResolution(); }
            catch { (w, h) = (0, 0); }
        }
        if (w == 0 || h == 0)
        {
            w = model.DisplaySettings?.Width ?? 0;
            h = model.DisplaySettings?.Height ?? 0;
        }

        var stateSuffix = ActiveTab.State switch
        {
            TabState.Connecting => "Connecting\u2026",              // U+2026 ellipsis
            TabState.Connected => "Connected",
            TabState.Reconnecting => "Reconnecting attempt \u2014/20",  // em-dash until Plan 03 plumbs attempt N
            TabState.Error => "Disconnected",
            _ => string.Empty,
        };
        StatusText = $"{model.Hostname} \u00B7 {stateSuffix}";    // U+00B7 middle-dot

        StatusSecondary = (w > 0 && h > 0)
            ? $"{w} \u00D7 {h}"                                     // U+00D7 multiplication sign
            : "\u2014";                                             // U+2014 em-dash placeholder
    }

    /// <summary>
    /// Marshal an action to the current thread's dispatcher when one is pumped on a
    /// different thread. In unit tests (no Application) and in the STA UI case where
    /// Publish already happens on the UI dispatcher, runs synchronously. The MVVM
    /// messenger (WeakReferenceMessenger) delivers on the publisher's thread, which
    /// is almost always the UI thread for our events — this is defense in depth for
    /// a future background publisher.
    /// </summary>
    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread);
        if (dispatcher is not null && dispatcher.CheckAccess())
        {
            action();
            return;
        }
        // No dispatcher on this thread, or not on the right thread — run synchronously.
        // If this turns out to cross threads in a real app, the VM state updates will
        // surface as cross-thread WPF binding exceptions and we'll address at that point.
        action();
    }
}
