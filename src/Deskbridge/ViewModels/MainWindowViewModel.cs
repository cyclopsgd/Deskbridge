using System.Collections.ObjectModel;
using System.Windows;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Models;

namespace Deskbridge.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ITabHostManager _tabHostManager;
    private readonly IEventBus _eventBus;
    private readonly IConnectionStore _connectionStore;

    public MainWindowViewModel(
        ConnectionTreeViewModel connectionTree,
        ITabHostManager tabHostManager,
        IEventBus eventBus,
        IConnectionStore connectionStore)
    {
        ConnectionTree = connectionTree;
        _tabHostManager = tabHostManager;
        _eventBus = eventBus;
        _connectionStore = connectionStore;

        // Phase 5: subscribe to TabHostManager lifecycle events so the ObservableCollection
        // and status bar stay in sync. All handlers marshal to the UI dispatcher because
        // IEventBus (WeakReferenceMessenger) delivers on the publisher's thread — usually
        // the STA UI thread, but defense-in-depth against a future background publisher.
        _eventBus.Subscribe<TabOpenedEvent>(this, OnTabOpened);
        _eventBus.Subscribe<TabClosedEvent>(this, OnTabClosed);
        _eventBus.Subscribe<TabSwitchedEvent>(this, OnTabSwitched);
        _eventBus.Subscribe<TabStateChangedEvent>(this, OnTabStateChanged);
    }

    // Expose ConnectionTreeViewModel for Ctrl+N binding in MainWindow
    [ObservableProperty]
    public partial ConnectionTreeViewModel ConnectionTree { get; set; }

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
            var tab = Tabs.FirstOrDefault(t => t.ConnectionId == evt.ConnectionId);
            if (tab is null) return;
            Tabs.Remove(tab);
            if (ActiveTab == tab) ActiveTab = null;
            OnPropertyChanged(nameof(HasNoTabs));
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
