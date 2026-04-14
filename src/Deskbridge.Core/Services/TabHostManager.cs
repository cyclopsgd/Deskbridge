using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
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
    /// <summary>D-09: soft threshold where the UI Snackbar warns the user.
    /// Centralised here so Phase 6 can promote it to a setting without touching consumers.</summary>
    public const int GdiWarningThreshold = 15;

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
        Dispatcher? dispatcher = null)
    {
        _bus = bus;
        _coordinator = coordinator;
        _disconnect = disconnect;
        _snackbar = snackbar;
        _logger = logger;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;

        _coordinator.HostMounted += OnHostMounted;
        _coordinator.HostUnmounted += OnHostUnmounted;
        _coordinator.ReconnectOverlayRequested += OnReconnectOverlayRequested;
        _bus.Subscribe<HostCreatedEvent>(this, OnHostCreated);
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

    public async Task CloseTabAsync(Guid connectionId)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            await _dispatcher.InvokeAsync(() => CloseTabAsync(connectionId));
            return;
        }

        if (!_hosts.TryGetValue(connectionId, out var host)) return;

        // Q2 (Phase 5): cancel the reconnect backoff loop BEFORE the disconnect pipeline
        // so RdpReconnectCoordinator.RunAsync cannot fire ConnectAsync against a host that
        // is about to be disposed. See IConnectionCoordinator.CancelReconnect XML doc.
        _coordinator.CancelReconnect(connectionId);

        if (!_connections.TryGetValue(connectionId, out var model))
        {
            // HostCreatedEvent should have populated _connections before HostMounted fires;
            // if it didn't (e.g. a race with disposal), we still dispose the host below but
            // skip the pipeline because DisconnectContext requires a ConnectionModel.
            _logger.LogWarning(
                "CloseTabAsync: no ConnectionModel recorded for {ConnectionId}; skipping disconnect pipeline",
                connectionId);
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
                connectionId, ex.GetType().Name, ex.HResult);
        }
        // OnHostUnmounted (from coordinator) performs the _hosts.Remove + LRU push +
        // TabClosedEvent publish + neighbor activation.
    }

    public async Task CloseOthersAsync(Guid keepConnectionId)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            await _dispatcher.InvokeAsync(() => CloseOthersAsync(keepConnectionId));
            return;
        }

        // Pitfall 4: snapshot the keys before iterating. Each CloseTabAsync awaits the
        // disconnect pipeline and can re-enter OnHostUnmounted mid-loop, which mutates
        // _hosts. Without the snapshot the foreach throws InvalidOperationException.
        var targets = _hosts.Keys.Where(k => k != keepConnectionId).ToList();
        foreach (var id in targets)
        {
            // Q2 (Phase 5): cancel reconnect loop BEFORE the disconnect pipeline so
            // RdpReconnectCoordinator cannot race ConnectAsync against a disposing host.
            // CloseTabAsync also calls CancelReconnect — the repeated call is a no-op
            // because the single-CTS design ignores cancels after the loop has exited.
            _coordinator.CancelReconnect(id);
            await CloseTabAsync(id);
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

        // Pitfall 4: snapshot before iterating (see CloseOthersAsync rationale).
        var targets = _hosts.Keys.ToList();
        foreach (var id in targets)
        {
            // Q2 (Phase 5): explicit CancelReconnect per host before its disconnect
            // pipeline runs (CloseTabAsync also cancels; the repeat is defensive).
            _coordinator.CancelReconnect(id);
            await CloseTabAsync(id);
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

    private void OnHostCreated(HostCreatedEvent evt)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            // Synchronous Invoke so the connection model is recorded before HostMounted
            // (which fires later in ConnectionCoordinator.OnHostCreated) asks for it.
            _dispatcher.Invoke(() => OnHostCreated(evt));
            return;
        }

        _connections[evt.Connection.Id] = evt.Connection;
    }

    private void OnHostMounted(object? sender, IProtocolHost host)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => OnHostMounted(sender, host));
            return;
        }

        _hosts[host.ConnectionId] = host;
        var previous = _activeId;
        _activeId = host.ConnectionId;

        _bus.Publish(new TabOpenedEvent(host.ConnectionId));
        _bus.Publish(new TabSwitchedEvent(previous, host.ConnectionId));

        // D-09 + D-10: fire-once-per-crossing warning. Fires on the 14 → 15 crossing,
        // does NOT re-fire at 16/17/..., re-arms only when the count drops below 15.
        // Values (title/message/appearance/icon/timeout) locked by UI-SPEC §Snackbar.
        if (!_warned15 && _hosts.Count == GdiWarningThreshold)
        {
            _warned15 = true;
            try
            {
                _snackbar.Show(
                    "Approaching session limit",
                    "15 active sessions reached — performance may degrade beyond this point.",
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
        if (_hosts.Count < GdiWarningThreshold) _warned15 = false;

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

        _hosts.Clear();
        _connections.Clear();
        _lastClosedLru.Clear();
    }
}
