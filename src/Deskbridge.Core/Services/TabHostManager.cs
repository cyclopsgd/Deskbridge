using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Settings;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge.Core.Services;

/// <summary>
/// Multi-host tab state owner (D-01). Phase 5 replacement for Phase 4's single-slot
/// <c>_active</c> tracking on <see cref="ConnectionCoordinator"/>. Subscribes to the
/// coordinator's <c>HostMounted</c> / <c>HostUnmounted</c> / <c>ReconnectOverlayRequested</c>
/// seams and the <c>HostCreatedEvent</c> on the bus; republishes tab lifecycle as
/// <c>TabOpenedEvent</c> / <c>TabClosedEvent</c> / <c>TabSwitchedEvent</c> /
/// <c>TabStateChangedEvent</c> for the UI layer (D-11 status bar, D-12 badges).
///
/// <para>Pure C# service — no WPF visual-tree management. MainWindow remains the sole
/// owner of <c>HostContainer</c>'s Visual children; this type tracks which hosts exist and
/// announces their presence on the event bus. Keeps <c>WindowsFormsHost</c> and AxHost
/// references out of Core (D-10 boundary).</para>
///
/// <para>D-04 invariant enforcement: TabHostManager never touches <see cref="IProtocolHost"/>
/// or its view; MainWindow's <c>OnHostMounted</c> / <c>OnHostUnmounted</c> handlers do
/// the parenting under <c>HostContainer</c> and guarantee WFHs are never re-parented.
/// See WINFORMS-HOST-AIRSPACE.md §Option 3.</para>
/// </summary>
public sealed class TabHostManager : ITabHostManager, IDisposable
{
    /// <summary>D-09: fallback threshold when no IWindowStateService is injected.</summary>
    private const int DefaultGdiWarningThreshold = 15;

    private readonly int _gdiWarningThreshold;

    /// <summary>D-16: bounded LRU for Ctrl+Shift+T reopen-last-closed.</summary>
    public const int LastClosedLruCapacity = 10;

    // All state lives on the STA dispatcher — no locks needed. See D-11.
    private readonly Dictionary<Guid, IProtocolHost> _hosts = new();
    private readonly Dictionary<Guid, ConnectionModel> _connections = new();
    private readonly LinkedList<Guid> _lastClosedLru = new();

    private readonly IEventBus _bus;
    private readonly IConnectionCoordinator _coordinator;
    private readonly IDisconnectPipeline _disconnect;
    private readonly ISnackbarService _snackbar;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<TabHostManager> _logger;

    private Guid? _activeId;
    private bool _warned15;
    private bool _disposed;

    public TabHostManager(
        IEventBus bus,
        IConnectionCoordinator coordinator,
        IDisconnectPipeline disconnect,
        ISnackbarService snackbar,
        ILogger<TabHostManager> logger,
        Dispatcher? dispatcher = null,
        IWindowStateService? windowState = null)
    {
        _bus = bus;
        _coordinator = coordinator;
        _disconnect = disconnect;
        _snackbar = snackbar;
        _logger = logger;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;

        var settings = windowState?.LoadAsync().GetAwaiter().GetResult();
        var bulk = settings?.BulkOperations ?? BulkOperationsRecord.Default;
        _gdiWarningThreshold = bulk.GdiWarningThreshold;

        _coordinator.HostMounted += OnHostMounted;
        _coordinator.HostUnmounted += OnHostUnmounted;
        _coordinator.ReconnectOverlayRequested += OnReconnectOverlayRequested;
        _bus.Subscribe<HostCreatedEvent>(this, OnHostCreated);
        _bus.Subscribe<ConnectionEstablishedEvent>(this, OnConnectionEstablished);
    }

    public int ActiveCount => _hosts.Count;

    public Guid? ActiveId => _activeId;

    // Snapshot the values so consumers iterating during a close path don't see mid-mutation state.
    public IReadOnlyCollection<IProtocolHost> AllHosts => _hosts.Values.ToList();

    public IProtocolHost? GetHost(Guid connectionId) =>
        _hosts.TryGetValue(connectionId, out var h) ? h : null;

    public bool TryGetExistingTab(Guid connectionId, out IProtocolHost host)
    {
        if (_hosts.TryGetValue(connectionId, out var existing))
        {
            host = existing;
            return true;
        }
        host = null!;
        return false;
    }

    public void SwitchTo(Guid connectionId)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => SwitchTo(connectionId));
            return;
        }
        if (!_hosts.ContainsKey(connectionId)) return;  // Silent no-op for stale clicks
        if (_activeId == connectionId) return;

        var previous = _activeId;
        _activeId = connectionId;
        _bus.Publish(new TabSwitchedEvent(previous, connectionId));
    }

    public Task CloseTabAsync(Guid connectionId)
    {
        if (_disposed) return Task.CompletedTask;
        if (!_dispatcher.CheckAccess())
        {
            return _dispatcher.InvokeAsync(() => CloseTabAsync(connectionId)).Task.Unwrap();
        }

        // Q2 (Phase 5): cancel the reconnect backoff loop BEFORE everything else so
        // RdpReconnectCoordinator.RunAsync cannot fire ConnectAsync against a host that
        // is about to be disposed. See IConnectionCoordinator.CancelReconnect XML doc.
        _coordinator.CancelReconnect(connectionId);

        // Hotfix (2026-04-14): user-initiated tab close must be instant from the UI
        // perspective. Phase 4's IDisconnectPipeline waits up to 30s for OnDisconnected
        // before disposing, which made mis-clicks while Connecting look like the close
        // button didn't work. Split into immediate visual removal + fire-and-forget
        // disconnect so the tab disappears before the pipeline completes. D-08 app-
        // shutdown teardown (CloseAllAsync) still awaits sequentially to preserve the
        // 20-cycle GDI baseline.
        var removed = DoVisualClose(connectionId);
        if (removed is null) return Task.CompletedTask;

        // Fire-and-forget: the async method runs synchronously up to its first await,
        // so DisconnectAsync IS called before this returns (tests rely on this). The
        // rest of the pipeline runs without blocking the caller.
        _ = RunDisconnectAsync(removed.Value.Model, removed.Value.Host);
        return Task.CompletedTask;
    }

    public async Task CloseOthersAsync(Guid keepConnectionId)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            await _dispatcher.InvokeAsync(() => CloseOthersAsync(keepConnectionId));
            return;
        }

        // Pitfall 4: snapshot the keys before iterating — user-initiated close path so
        // each tab uses the fire-and-forget disconnect (same UX as CloseTabAsync).
        var targets = _hosts.Keys.Where(k => k != keepConnectionId).ToList();
        foreach (var id in targets)
        {
            _coordinator.CancelReconnect(id);
            var removed = DoVisualClose(id);
            if (removed is null) continue;
            _ = RunDisconnectAsync(removed.Value.Model, removed.Value.Host);
        }
    }

    public async Task CloseAllAsync()
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            await _dispatcher.InvokeAsync(() => CloseAllAsync());
            return;
        }

        // D-08 app-shutdown teardown: sequential AWAIT per host so strict disposal
        // ordering from RDP-ACTIVEX-PITFALLS §3 is preserved and the 20-cycle GDI
        // baseline survives app close. This is different from user-initiated close
        // (CloseTabAsync / CloseOthersAsync), which fire-and-forget for UI snappiness.
        var targets = _hosts.Keys.ToList();
        foreach (var id in targets)
        {
            _coordinator.CancelReconnect(id);
            var removed = DoVisualClose(id);
            if (removed is null) continue;
            await RunDisconnectAsync(removed.Value.Model, removed.Value.Host);
        }
    }

    /// <summary>
    /// Hotfix helper: performs the synchronous, instant-visual side of a tab close —
    /// removes from <c>_hosts</c> / <c>_connections</c>, pushes LRU, publishes
    /// <see cref="TabClosedEvent"/>, and activates a neighbor if needed. Returns the
    /// captured (Model, Host) so the caller can drive disposal either awaited
    /// (<see cref="CloseAllAsync"/>) or fire-and-forget (user close paths). Returns
    /// <c>null</c> if the host was not in the dict (stale / double-fire guard).
    /// </summary>
    private (ConnectionModel? Model, IProtocolHost Host)? DoVisualClose(Guid connectionId)
    {
        if (!_hosts.TryGetValue(connectionId, out var host)) return null;
        _connections.TryGetValue(connectionId, out var model);

        _hosts.Remove(connectionId);
        _connections.Remove(connectionId);

        // D-09 re-arm: allow a future 14→15 crossing to warn again.
        if (_hosts.Count < _gdiWarningThreshold) _warned15 = false;

        // D-16: push to LRU with dedupe.
        PushLru(connectionId);

        _bus.Publish(new TabClosedEvent(connectionId));

        // Auto-activate a neighbor if the closed tab was active (mRemoteNG UX).
        if (_activeId == connectionId)
        {
            var next = _hosts.Keys.LastOrDefault();  // Last-added = right-most tab
            var previous = _activeId;
            _activeId = next == Guid.Empty ? null : next;
            _bus.Publish(new TabSwitchedEvent(previous, _activeId ?? Guid.Empty));
        }

        return (model, host);
    }

    /// <summary>
    /// Drives <see cref="IDisconnectPipeline"/> with proper error isolation. When invoked
    /// fire-and-forget from <see cref="CloseTabAsync"/>, the synchronous portion (up to
    /// the first <c>await</c>) calls <c>DisconnectAsync</c> before returning, so tests
    /// that assert <c>Received(1).DisconnectAsync(...)</c> after <c>await sut.CloseTabAsync</c>
    /// still pass without needing explicit yields.
    /// </summary>
    private async Task RunDisconnectAsync(ConnectionModel? model, IProtocolHost host)
    {
        if (model is null)
        {
            // HostCreatedEvent should have populated _connections before HostMounted;
            // if it didn't (race with disposal), we still dispose the host directly.
            _logger.LogWarning(
                "RunDisconnectAsync: no ConnectionModel recorded for {ConnectionId}; skipping disconnect pipeline",
                host.ConnectionId);
            try { host.Dispose(); }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Host dispose (no-model fallback) threw: {ExceptionType} HResult={HResult:X8}",
                    ex.GetType().Name, ex.HResult);
            }
            return;
        }

        var ctx = new DisconnectContext
        {
            Connection = model,
            Host = host,
            Reason = DisconnectReason.UserInitiated,
        };
        try
        {
            await _disconnect.DisconnectAsync(ctx);
        }
        catch (Exception ex)
        {
            // T-05-02 + T-04-EXC: log type + HResult only. Never interpolate ex.Message.
            _logger.LogError(
                "Disconnect pipeline for {ConnectionId} threw: {ExceptionType} HResult={HResult:X8}",
                model.Id, ex.GetType().Name, ex.HResult);
        }
    }

    public Guid? PopLastClosed()
    {
        if (_disposed) return null;
        if (!_dispatcher.CheckAccess())
        {
            return _dispatcher.Invoke(() => PopLastClosed());
        }

        if (_lastClosedLru.Count == 0) return null;
        var id = _lastClosedLru.First!.Value;
        _lastClosedLru.RemoveFirst();
        return id;
    }

    // ---------------------------------------------------------------------- events

    /// <summary>
    /// Hotfix (2026-04-14): tab registration was previously split between OnHostCreated
    /// (bus) for model recording and OnHostMounted (coordinator delegate) for the
    /// TabOpenedEvent publish. But the bus delivers to subscribers in registration
    /// order — ConnectionCoordinator subscribes to HostCreatedEvent FIRST (created
    /// earlier in DI), and its handler synchronously raises the HostMounted delegate
    /// before TabHostManager's OnHostCreated bus handler runs. Result: OnHostMounted
    /// fired with an empty _connections dict, logged "no ConnectionModel recorded",
    /// and suppressed the TabOpenedEvent — no tab ever appeared.
    ///
    /// <para>Fix: do ALL tab-registration work in OnHostCreated. HostCreatedEvent
    /// carries both Connection and Host, so we don't need to wait for HostMounted.
    /// OnHostMounted stays subscribed as a defensive no-op for back-compat.</para>
    /// </summary>
    private void OnHostCreated(HostCreatedEvent evt)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => OnHostCreated(evt));
            return;
        }

        // Hotfix (2026-04-14): idempotency guard. HostCreatedEvent fires on the INITIAL
        // connect AND on every reconnect attempt (D-04 "dispose + recreate host per
        // attempt"). Without this guard, each reconnect cycle re-publishes TabOpenedEvent,
        // which MainWindowViewModel.OnTabOpened handles by calling Tabs.Add(new
        // TabItemViewModel) — producing phantom duplicate tabs in the UI that cannot be
        // closed because the close path only removes one match at a time. Evidence was
        // repeated "Connection established" + "Connecting to X" pairs in the log with no
        // TabClosedEvent between them.
        var isReconnectCycle = _hosts.ContainsKey(evt.Connection.Id);

        _connections[evt.Connection.Id] = evt.Connection;
        _hosts[evt.Connection.Id] = evt.Host;

        if (isReconnectCycle)
        {
            // Same tab, new underlying host — swap the dict entry silently and let the
            // UI keep its existing TabItemViewModel. No TabOpenedEvent, no 15+ warning
            // (count didn't change), no TabSwitchedEvent (active didn't change).
            _logger.LogDebug(
                "OnHostCreated: reconnect cycle detected for {ConnectionId}; suppressing duplicate TabOpenedEvent",
                evt.Connection.Id);
            return;
        }

        var previous = _activeId;
        _activeId = evt.Connection.Id;

        _bus.Publish(new TabOpenedEvent(evt.Connection.Id, evt.Connection));
        _bus.Publish(new TabSwitchedEvent(previous, evt.Connection.Id));

        // D-09 + D-10: fire-once-per-crossing warning. Moved here from OnHostMounted
        // because _hosts is now populated synchronously in this handler.
        FireGdiWarningIfCrossingThreshold();
    }

    /// <summary>
    /// Defensive no-op retained to keep <see cref="IConnectionCoordinator.HostMounted"/>
    /// subscribed — prevents event-handler leaks if coordinator internal behavior
    /// changes in a future phase. Tab registration (formerly here) moved to
    /// <see cref="OnHostCreated"/> to fix the bus-subscription-order bug.
    /// </summary>
    private void OnHostMounted(object? sender, IProtocolHost host)
    {
        if (_disposed) return;
        // Intentionally empty — see OnHostCreated for tab registration work.
    }

    private void FireGdiWarningIfCrossingThreshold()
    {

        // D-09 + D-10: fire-once-per-crossing warning. Fires on the 14 → 15 crossing,
        // does NOT re-fire at 16/17/..., re-arms only when the count drops below 15.
        // Values (title/message/appearance/icon/timeout) locked by UI-SPEC §Snackbar.
        if (!_warned15 && _hosts.Count == _gdiWarningThreshold)
        {
            _warned15 = true;
            try
            {
                _snackbar.Show(
                    "Approaching session limit",
                    $"{_gdiWarningThreshold} active sessions reached — performance may degrade beyond this point.",
                    ControlAppearance.Caution,
                    new SymbolIcon { Symbol = SymbolRegular.Warning24 },
                    TimeSpan.FromSeconds(6));
            }
            catch (Exception ex)
            {
                // Don't let a snackbar failure block the connection — log and continue.
                _logger.LogWarning(
                    "Snackbar.Show for 15-session warning threw: {ExceptionType} HResult={HResult:X8}",
                    ex.GetType().Name, ex.HResult);
            }
        }
    }

    private void OnHostUnmounted(object? sender, IProtocolHost host)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => OnHostUnmounted(sender, host));
            return;
        }

        var id = host.ConnectionId;
        if (!_hosts.Remove(id)) return;  // Already removed (double-fire guard)
        _connections.Remove(id);

        // D-09 re-arm: allow a future 14→15 crossing to warn again.
        if (_hosts.Count < _gdiWarningThreshold) _warned15 = false;

        // D-16: push to LRU with dedupe.
        PushLru(id);

        _bus.Publish(new TabClosedEvent(id));

        // Auto-activate a neighbor if the closed tab was active (mRemoteNG UX).
        // When _hosts.Count == 0 the neighbor is "no tab" — ActiveId becomes null and
        // subscribers treat Guid.Empty in TabSwitchedEvent as "no active tab".
        if (_activeId == id)
        {
            var next = _hosts.Keys.LastOrDefault();  // Last-added = right-most tab
            var previous = _activeId;
            _activeId = next == Guid.Empty ? null : next;
            _bus.Publish(new TabSwitchedEvent(previous, _activeId ?? Guid.Empty));
        }
    }

    private void OnReconnectOverlayRequested(object? sender, ReconnectUiRequest req)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => OnReconnectOverlayRequested(sender, req));
            return;
        }

        // D-14: surface the amber Reconnecting badge even when the tab is backgrounded.
        // MainWindow (Phase 4) drives the overlay visuals; this handler just raises the
        // state change event for the TabItemViewModel binding.
        _bus.Publish(new TabStateChangedEvent(req.Connection.Id, TabState.Reconnecting));
    }

    /// <summary>
    /// Hotfix (2026-04-14): publish <see cref="TabStateChangedEvent"/> with
    /// <see cref="TabState.Connected"/> when the connect pipeline's
    /// <see cref="ConnectionEstablishedEvent"/> fires. Without this, tabs were stuck in
    /// <see cref="TabState.Connecting"/> forever — the state property was initialised on
    /// tab creation but no handler transitioned it to Connected on login completion.
    /// The ProgressRing spinner kept running, tooltip kept showing "Connecting…", and
    /// the status bar never cleared the ellipsis suffix.
    /// </summary>
    private void OnConnectionEstablished(ConnectionEstablishedEvent evt)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => OnConnectionEstablished(evt));
            return;
        }

        // Silent no-op if the tab is no longer tracked (user closed mid-connect).
        if (!_hosts.ContainsKey(evt.Connection.Id)) return;

        _bus.Publish(new TabStateChangedEvent(evt.Connection.Id, TabState.Connected));
    }

    // ---------------------------------------------------------------------- LRU internals

    private void PushLru(Guid id)
    {
        // Dedupe-on-push: if id is already in the list, remove and re-add to the front.
        _lastClosedLru.Remove(id);  // O(n) on a 10-entry list — acceptable.
        _lastClosedLru.AddFirst(id);
        while (_lastClosedLru.Count > LastClosedLruCapacity)
        {
            _lastClosedLru.RemoveLast();
        }
    }

    /// <summary>
    /// Test seam: lets the LRU unit tests populate entries without re-implementing
    /// OnHostUnmounted's side effects. Internal + [InternalsVisibleTo(Deskbridge.Tests)].
    /// </summary>
    internal void PushLastClosedForTesting(Guid id) => PushLru(id);

    // ---------------------------------------------------------------------- dispose

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _coordinator.HostMounted -= OnHostMounted; } catch { /* best-effort */ }
        try { _coordinator.HostUnmounted -= OnHostUnmounted; } catch { /* best-effort */ }
        try { _coordinator.ReconnectOverlayRequested -= OnReconnectOverlayRequested; } catch { /* best-effort */ }
        try { _bus.Unsubscribe<HostCreatedEvent>(this); } catch { /* best-effort */ }
        try { _bus.Unsubscribe<ConnectionEstablishedEvent>(this); } catch { /* best-effort */ }

        _hosts.Clear();
        _connections.Clear();
        _lastClosedLru.Clear();
    }
}
