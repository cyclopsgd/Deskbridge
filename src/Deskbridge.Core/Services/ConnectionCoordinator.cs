using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Deskbridge.Core.Services;

/// <summary>
/// Singleton event-bus bridge: subscribes to <see cref="ConnectionRequestedEvent"/>,
/// marshals to the STA dispatcher (D-11), and runs <see cref="IConnectionPipeline"/>.
/// Tracks the single active host per the Phase 4 single-host policy (Open Question #2) —
/// a second <see cref="ConnectionRequestedEvent"/> disconnects the current host before
/// connecting the new one. Phase 5 replaces this with tab-keyed multi-host storage.
///
/// <para>Dispatcher is injectable (Open Question #3) so xUnit tests can inject their
/// STA-runner's dispatcher rather than relying on <see cref="Dispatcher.CurrentDispatcher"/>.</para>
/// </summary>
public sealed class ConnectionCoordinator : IConnectionCoordinator, IDisposable
{
    private readonly IEventBus _bus;
    private readonly IConnectionPipeline _connect;
    private readonly IDisconnectPipeline _disconnect;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<ConnectionCoordinator> _logger;
    private readonly RdpReconnectCoordinator _reconnectCoordinator;
    // Phase 5 (D-01/D-05): single-slot `_active` tuple replaced by a dict keyed by
    // ConnectionId so the coordinator's post-connect cleanup paths (OnConnectionFailed,
    // OnConnectionClosed, OnDisconnectedAfterConnect) can find the right host without
    // a global "current" field. ITabHostManager remains the source of truth for
    // "which tabs are open"; this dict is the coordinator's internal bookkeeping for
    // its own lifecycle cleanup. ActiveHost shim reads the most-recent-mounted entry.
    private readonly Dictionary<Guid, (IProtocolHost Host, ConnectionModel Model)> _coordinatorHosts = new();

    // Hotfix (2026-04-14): rapid-double-click dedupe set. ConnectionTreeViewModel.Connect's
    // publisher-side TryGetExistingTab check (D-02) is racy on the INITIAL connect — the
    // first click hasn't yet populated TabHostManager._hosts when the second click arrives,
    // so both pass the "not already open" gate and two pipelines run in parallel. Each
    // creates its own AxHost, each fires HostCreatedEvent, and MainWindow.OnHostMounted
    // parents BOTH WFHs into HostContainer with the same Tag=ConnectionId, producing
    // airspace chaos that manifests as a black viewport on first connect. This in-flight
    // set lives in the coordinator because it owns the pipeline lifecycle and is the
    // earliest point where duplicate requests can be seen and rejected.
    private readonly HashSet<Guid> _pendingConnects = new();
    private Guid? _activeId;
    private IProtocolHost? _suppressedHost;
    private CancellationTokenSource? _reconnectCts;
    private bool _disposed;

    public ConnectionCoordinator(
        IEventBus bus,
        IConnectionPipeline connect,
        IDisconnectPipeline disconnect,
        ILogger<ConnectionCoordinator> logger,
        Dispatcher? dispatcher = null,
        RdpReconnectCoordinator? reconnectCoordinator = null)
    {
        _bus = bus;
        _connect = connect;
        _disconnect = disconnect;
        _logger = logger;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _reconnectCoordinator = reconnectCoordinator ?? new RdpReconnectCoordinator();

        _bus.Subscribe<ConnectionRequestedEvent>(this, OnConnectionRequested);
        _bus.Subscribe<HostCreatedEvent>(this, OnHostCreated);
        _bus.Subscribe<ConnectionEstablishedEvent>(this, OnConnectionEstablished);
        _bus.Subscribe<ConnectionFailedEvent>(this, OnConnectionFailed);
        _bus.Subscribe<ConnectionClosedEvent>(this, OnConnectionClosed);
        _bus.Subscribe<CredentialRequestedEvent>(this, OnCredentialRequested);
    }

    // Phase 5 (D-01): shim over the dict-keyed storage. Returns the most-recently-mounted
    // host (matches Phase 4 single-slot semantics for existing callers). ITabHostManager
    // is the canonical source of truth for multi-host queries; new code should prefer
    // that interface.
    public IProtocolHost? ActiveHost =>
        _activeId is { } id && _coordinatorHosts.TryGetValue(id, out var entry) ? entry.Host : null;

    public event EventHandler<IProtocolHost>? HostMounted;
    public event EventHandler<IProtocolHost>? HostUnmounted;
    public event EventHandler<ReconnectUiRequest>? ReconnectOverlayRequested;

    /// <summary>
    /// Q2 resolution (Phase 5): cancel any in-flight auto-reconnect backoff loop for
    /// <paramref name="connectionId"/>. Invoked by <c>TabHostManager</c>'s close paths
    /// BEFORE <see cref="IDisconnectPipeline.DisconnectAsync"/> so <c>RdpReconnectCoordinator.RunAsync</c>
    /// cannot fire <c>ConnectAsync</c> against a host that is about to be disposed.
    ///
    /// <para>The current single-CTS design (inherited from Phase 4 single-host) only ever
    /// tracks ONE active reconnect loop at a time — <see cref="_reconnectCts"/> is assigned
    /// in <see cref="OnDisconnectedAfterConnect"/> for the dropped connection and nulled in
    /// <see cref="RunAutoReconnectAsync"/>'s <c>finally</c>. Calling Cancel here is therefore
    /// safe regardless of whether the currently running backoff loop is for this connection
    /// id or a different one; if it's for a different one, the cancellation still unblocks
    /// that loop but that's acceptable because the caller (TabHostManager.CloseAllAsync /
    /// CloseOthersAsync) is closing that connection too. Per-connection CTS is deferred
    /// until multiple concurrent backoff loops are actually possible — not in Phase 5 scope.</para>
    /// </summary>
    public void CancelReconnect(Guid connectionId)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => CancelReconnect(connectionId));
            return;
        }
        try { _reconnectCts?.Cancel(); }
        catch (ObjectDisposedException) { /* already disposed, nothing to cancel */ }
        _logger.LogInformation(
            "CancelReconnect invoked for connection id {ConnectionId}",
            connectionId);
    }

    private void OnConnectionRequested(ConnectionRequestedEvent evt)
    {
        if (_disposed) return;

        // Marshal to STA (D-11) — bus may deliver on any thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => OnConnectionRequested(evt));
            return;
        }

        // D-02 (Phase 5): switch-to-existing is handled publisher-side in
        // ConnectionTreeViewModel.Connect, but that check is RACY on the initial
        // connect — the first click hasn't populated TabHostManager._hosts when a
        // rapid second click arrives, so both pass and two pipelines race. Hotfix
        // (2026-04-14): dedupe here against an in-flight set + the mounted dict.
        if (_pendingConnects.Contains(evt.Connection.Id) ||
            _coordinatorHosts.ContainsKey(evt.Connection.Id))
        {
            _logger.LogInformation(
                "Ignoring duplicate connect request for {Hostname} — pipeline already in-flight or mounted",
                evt.Connection.Hostname);
            return;
        }

        // D-01/D-05 (Phase 5): single-host replacement branch (Phase 4 lines 90-104)
        // was deleted. Multi-host coexistence is owned by TabHostManager + the
        // persistent HostContainer (Plan 02); the coordinator no longer disconnects
        // the previous host on a new ConnectionRequestedEvent.
        _pendingConnects.Add(evt.Connection.Id);
        _logger.LogInformation("Connecting to {Hostname}", evt.Connection.Hostname);
        _ = RunConnectSafely(evt.Connection);
    }

    // Fire-and-forget wrappers. The pipelines throw on failure (no internal try/catch), and
    // if the outer _ = _connect.ConnectAsync(...) pattern is used directly the exception is
    // unobserved — no log, no ConnectionFailedEvent, diagnostic trail goes silent. These
    // helpers guarantee every pipeline throw is logged (type + HResult only, per T-04-EXC)
    // and surfaced on the bus so UI and tests can observe failures.
    private async Task RunConnectSafely(ConnectionModel model)
    {
        try
        {
            await _connect.ConnectAsync(model);
        }
        catch (Exception ex)
        {
            // WR-05: drop ex.Message unconditionally. The prior allow-list omitted
            // InvalidOperationException (AxHost.InvalidActiveXStateException derives
            // from it), SocketException, HttpRequestException, etc — any of which
            // could carry RDP state detail or network target metadata. T-04-EXC and
            // RDP-ACTIVEX-PITFALLS §3 mandate type + HResult only for COM-family
            // errors, and ConnectStage's catch filters already follow that pattern;
            // match it here rather than trying to classify per exception type.
            _logger.LogError(
                "Connection pipeline threw for {Hostname}: {ExceptionType} HResult={HResult:X8}",
                model.Hostname, ex.GetType().Name, ex.HResult);
            _bus.Publish(new ConnectionFailedEvent(
                model,
                $"{ex.GetType().Name} (HResult 0x{ex.HResult:X8})",
                ex));
        }
        finally
        {
            // Hotfix (2026-04-14): drain the in-flight dedupe set so subsequent
            // open requests for the same connection (e.g. user closes and re-opens)
            // aren't blocked. Must marshal to STA because the pipeline's awaits
            // can resume on any dispatcher captured context.
            if (_dispatcher.CheckAccess())
            {
                _pendingConnects.Remove(model.Id);
            }
            else
            {
                _dispatcher.Invoke(() => _pendingConnects.Remove(model.Id));
            }
        }
    }

    private async Task RunDisconnectSafely(DisconnectContext ctx)
    {
        try
        {
            await _disconnect.DisconnectAsync(ctx);
        }
        catch (Exception ex)
        {
            // WR-05: drop ex.Message unconditionally (see RunConnectSafely above).
            _logger.LogError(
                "Disconnect pipeline threw for {Hostname}: {ExceptionType} HResult={HResult:X8}",
                ctx.Connection.Hostname, ex.GetType().Name, ex.HResult);
        }
    }

    /// <summary>
    /// Fires between <c>CreateHostStage</c> (Order=200) and <c>ConnectStage</c> (Order=300).
    /// We do TWO things here, both critical to the siting-order state machine
    /// (RDP-ACTIVEX-PITFALLS §1):
    /// <list type="number">
    ///   <item>Record the single active host NOW (not at ConnectionEstablished), so a
    ///         concurrent request replaces the in-flight host instead of leaking it —
    ///         and so disposal during Connect failure has the right handle to release.</item>
    ///   <item>Raise <see cref="HostMounted"/>. MainWindow's handler parents the
    ///         <c>WindowsFormsHost</c> into <c>ViewportGrid</c> and forces a layout pass;
    ///         by the time this event handler returns, AxHost's HWND is realized and
    ///         <c>ConnectStage</c> can safely call <c>ConnectAsync</c>.</item>
    /// </list>
    /// Delivery is synchronous: <c>EventBus.Publish</c> → <c>WeakReferenceMessenger.Send</c>
    /// is inline, and since <c>CreateHostStage.ExecuteAsync</c> runs on the STA dispatcher
    /// thread (pipeline → coordinator → Connect are all STA-affined per D-11), the
    /// dispatcher marshal below is a no-op. It's kept as a defensive wrapper in case the
    /// event source ever changes.
    /// </summary>
    private void OnHostCreated(HostCreatedEvent evt)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            // Use Invoke (not InvokeAsync) so siting completes BEFORE we return to the
            // pipeline. ConnectStage must see a sited host. CheckAccess()==true on STA
            // so this branch is rarely hit.
            _dispatcher.Invoke(() => OnHostCreated(evt));
            return;
        }

        // Hotfix (2026-04-14): defensive guard against duplicate host mounts.
        // OnConnectionRequested dedupes bus-driven connect requests, but pipelines
        // can also be kicked off directly by RunAutoReconnectAsync and
        // WireManualHandlers (manual reconnect). If either fires for a connection
        // whose host is still mounted, we'd get TWO WFHs parented in HostContainer
        // with the same Tag=ConnectionId — airspace chaos, first-connect black
        // viewport. Reject the duplicate by disposing the new host and not
        // raising HostMounted. The pipeline's downstream ConnectStage will call
        // .ConnectAsync on the disposed host and throw; RunConnectSafely catches
        // it and logs. The original mounted session continues uninterrupted.
        if (_coordinatorHosts.ContainsKey(evt.Connection.Id))
        {
            _logger.LogWarning(
                "OnHostCreated: host already mounted for {ConnectionId} ({Hostname}) — disposing duplicate to preserve single-mount invariant",
                evt.Connection.Id, evt.Connection.Hostname);
            try { evt.Host.Dispose(); }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Duplicate-host dispose threw: {ExceptionType} HResult={HResult:X8}",
                    ex.GetType().Name, ex.HResult);
            }
            return;
        }

        _coordinatorHosts[evt.Connection.Id] = (evt.Host, evt.Connection);
        _activeId = evt.Connection.Id;

        // Plan 04-03: subscribe to the post-connect disconnect stream so we can drive
        // the reconnect loop. IProtocolHost exposes the event abstractly (D-10);
        // RdpHostControl raises it from its OnDisconnectedAfterConnectHandler after
        // OnLoginComplete swaps the OnDisconnected handler.
        evt.Host.DisconnectedAfterConnect += OnDisconnectedAfterConnect;

        HostMounted?.Invoke(this, evt.Host);
    }

    /// <summary>
    /// Post-connect observability only. Host tracking and <see cref="HostMounted"/> now
    /// fire earlier, in <see cref="OnHostCreated"/>, so <c>ConnectStage</c> finds a sited
    /// host. This handler stays subscribed so tests and telemetry can observe the
    /// established event reliably.
    /// </summary>
    private void OnConnectionEstablished(ConnectionEstablishedEvent evt)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => OnConnectionEstablished(evt));
            return;
        }

        _logger.LogInformation(
            "Connection established for {Hostname}", evt.Connection.Hostname);
    }

    /// <summary>
    /// Surfaces pipeline failures to the log and cleans up the active host. Before this
    /// handler existed, <c>ConnectionFailedEvent</c> was published by the pipeline but no
    /// subscriber logged it, producing the "silent hang" symptom where the UI showed a
    /// spinner but no diagnostic trail existed. Single-host policy (Open Question #2):
    /// if the failure is for our active host, dispose + raise <see cref="HostUnmounted"/>
    /// so MainWindow unparents the WFH.
    /// </summary>
    private void OnConnectionFailed(ConnectionFailedEvent evt)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => OnConnectionFailed(evt));
            return;
        }

        // Reason is already sanitized by DisconnectReasonClassifier (or "<Type> (HResult 0x...)"
        // from RunConnectSafely/ConnectStage) — safe to log.
        _logger.LogWarning(
            "Connection to {Hostname} failed: {Reason}",
            evt.Connection.Hostname, evt.Reason);

        if (_coordinatorHosts.TryGetValue(evt.Connection.Id, out var entry))
        {
            var host = entry.Host;
            _coordinatorHosts.Remove(evt.Connection.Id);
            if (_activeId == evt.Connection.Id) _activeId = null;

            // Defense-in-depth: unsubscribe the reconnect handler BEFORE dispose so a
            // late-firing OnDisconnected (mid-teardown COM event) cannot re-enter the
            // reconnect path with a partially-disposed host.
            try { host.DisconnectedAfterConnect -= OnDisconnectedAfterConnect; }
            catch { /* disposed host may throw */ }
            // Unmount FIRST (MainWindow removes from visual tree while Host getter is still valid),
            // THEN dispose (frees COM resources + nulls _host). Reverse order throws
            // ObjectDisposedException in MainWindow.OnHostUnmounted's rdp.Host access.
            HostUnmounted?.Invoke(this, host);
            try { host.Dispose(); }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    "Dispose after failed connect threw: {ExceptionType} HResult={HResult:X8}",
                    ex.GetType().Name, ex.HResult);
            }
        }
    }

    /// <summary>
    /// Fallback handler for <see cref="CredentialRequestedEvent"/>. With the credential
    /// prompt dialog wired into <c>ResolveCredentialsStage</c>, this event now only fires
    /// for unresolved Own/Inherit cases (stored credential missing). Surface a
    /// <see cref="ConnectionFailedEvent"/> so the failure toast shows feedback.
    /// </summary>
    private void OnCredentialRequested(CredentialRequestedEvent evt)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => OnCredentialRequested(evt));
            return;
        }

        _logger.LogWarning(
            "CredentialRequestedEvent for {Hostname} — no stored credential found. " +
            "Configure credentials via Edit Connection or use CredentialMode.Prompt.",
            evt.Connection.Hostname);
        _bus.Publish(new ConnectionFailedEvent(
            evt.Connection,
            "No credentials found — configure via Edit Connection or switch to Prompt mode.",
            null));
    }

    private void OnConnectionClosed(ConnectionClosedEvent evt)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => OnConnectionClosed(evt));
            return;
        }

        if (_coordinatorHosts.TryGetValue(evt.Connection.Id, out var entry))
        {
            var host = entry.Host;
            _coordinatorHosts.Remove(evt.Connection.Id);
            if (_activeId == evt.Connection.Id) _activeId = null;
            try { host.DisconnectedAfterConnect -= OnDisconnectedAfterConnect; }
            catch { /* disposed host may throw */ }
            HostUnmounted?.Invoke(this, host);
        }
    }

    /// <summary>
    /// Plan 04-03 D-03/D-04/D-06/D-07 entry point. Fires when an established session
    /// drops. Classifies the discReason; for retriable categories we run the
    /// <see cref="RdpReconnectCoordinator"/> backoff loop, for auth/licensing we go
    /// straight to the manual overlay. Either way we first dispose the current host
    /// (D-04 fresh instance per attempt).
    /// </summary>
    internal void OnDisconnectedAfterConnect(object? sender, int discReason)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => OnDisconnectedAfterConnect(sender, discReason));
            return;
        }

        if (sender is not IProtocolHost host) return;
        // Phase 5: look up the connection model via the dict (keyed by ConnectionId)
        // instead of the old single-slot tuple. Sender-identity check preserved.
        if (!_coordinatorHosts.TryGetValue(host.ConnectionId, out var entry) ||
            !ReferenceEquals(entry.Host, host)) return;

        var model = entry.Model;
        var category = DisconnectReasonClassifier.Classify(discReason);
        _logger.LogInformation(
            "Post-connect disconnect for {Hostname}: discReason={DiscReason} category={Category}",
            model.Hostname, discReason, category);

        // RELY-02: logoff from within the session -> close the tab, no overlay.
        if (category == DisconnectCategory.Logoff)
        {
            _coordinatorHosts.Remove(host.ConnectionId);
            if (_activeId == host.ConnectionId) _activeId = null;
            try { host.DisconnectedAfterConnect -= OnDisconnectedAfterConnect; }
            catch { /* disposed host may throw */ }
            try { host.Dispose(); }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    "Host dispose during logoff threw: {ExceptionType} HResult={HResult:X8}",
                    ex.GetType().Name, ex.HResult);
            }
            // Publish ConnectionClosedEvent so TabHostManager closes the tab and
            // MainWindow unparents the WFH. DisconnectReason.RemoteDisconnect because
            // the server ended the session (the user logged off inside the VM).
            _bus.Publish(new ConnectionClosedEvent(model, DisconnectReason.RemoteDisconnect));
            return;
        }

        // Suppress the HostUnmounted/Dispose that the normal failure / closed pipeline
        // would trigger — we want the overlay to stay visible over the viewport until
        // either a fresh host mounts (success) or the user closes the overlay.
        _suppressedHost = host;
        _coordinatorHosts.Remove(host.ConnectionId);
        if (_activeId == host.ConnectionId) _activeId = null;
        try { host.DisconnectedAfterConnect -= OnDisconnectedAfterConnect; }
        catch { /* disposed host may throw */ }

        try { host.Dispose(); }
        catch (Exception ex)
        {
            _logger.LogError(
                "Host dispose during reconnect threw: {ExceptionType} HResult={HResult:X8}",
                ex.GetType().Name, ex.HResult);
        }

        var handle = new ReconnectOverlayHandle();
        ReconnectOverlayRequested?.Invoke(this, new ReconnectUiRequest(model, handle));

        if (!DisconnectReasonClassifier.ShouldAutoRetry(category))
        {
            // D-06: auth or licensing -> manual overlay only.
            handle.SwitchToManual?.Invoke();
            WireManualHandlers(handle, model);
            return;
        }

        // Suppress auto-reconnect when another active connection targets the same host.
        // Two sessions with the same user on the same VM steal each other's RDP session
        // (discReason=3 ServerInitiated), creating an infinite reconnect loop.
        if (category == DisconnectCategory.ServerInitiated &&
            _coordinatorHosts.Values.Any(e => string.Equals(e.Model.Hostname, model.Hostname, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning(
                "Suppressing auto-reconnect for {Hostname} — another active connection exists to the same host",
                model.Hostname);
            handle.SwitchToManual?.Invoke();
            WireManualHandlers(handle, model);
            return;
        }

        var oldCts = _reconnectCts;
        oldCts?.Cancel();
        oldCts?.Dispose();
        _reconnectCts = new CancellationTokenSource();
        handle.CancelRequested += (_, _) =>
        {
            try { _reconnectCts?.Cancel(); } catch { /* already disposed */ }
        };
        _ = RunAutoReconnectAsync(model, handle, _reconnectCts.Token);
    }

    private async Task RunAutoReconnectAsync(ConnectionModel model, ReconnectOverlayHandle handle, CancellationToken ct)
    {
        try
        {
            var success = await _reconnectCoordinator.RunAsync(
                model,
                reconnect: async m =>
                {
                    try
                    {
                        var result = await _connect.ConnectAsync(m);
                        return result.Success;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            "Reconnect attempt for {Hostname} threw: {ExceptionType} HResult={HResult:X8}",
                            m.Hostname, ex.GetType().Name, ex.HResult);
                        return false;
                    }
                },
                notifyAttempt: (attempt, delay) =>
                {
                    handle.UpdateAttempt?.Invoke(attempt, delay);
                    _bus.Publish(new ReconnectingEvent(model, attempt, delay));
                    return Task.CompletedTask;
                },
                ct);

            if (success)
            {
                // ConnectStage already published ConnectionEstablishedEvent on success,
                // which landed via OnHostCreated -> _active reset + HostMounted. Close
                // the overlay so the fresh session is visible.
                handle.Close?.Invoke();
            }
            else if (ct.IsCancellationRequested)
            {
                handle.Close?.Invoke();
                _bus.Publish(new ConnectionClosedEvent(model, DisconnectReason.UserInitiated));
            }
            else
            {
                // D-05 cap hit -> manual overlay.
                handle.SwitchToManual?.Invoke();
                WireManualHandlers(handle, model);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "RunAutoReconnectAsync for {Hostname} threw: {ExceptionType} HResult={HResult:X8}",
                model.Hostname, ex.GetType().Name, ex.HResult);
            handle.Close?.Invoke();
            _bus.Publish(new ConnectionClosedEvent(model, DisconnectReason.Error));
        }
        finally
        {
            _suppressedHost = null;
        }
    }

    private void WireManualHandlers(ReconnectOverlayHandle handle, ConnectionModel model)
    {
        handle.ManualReconnectRequested += async (_, _) =>
        {
            handle.Close?.Invoke();
            _suppressedHost = null;
            try
            {
                await _connect.ConnectAsync(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Manual reconnect for {Hostname} threw: {ExceptionType} HResult={HResult:X8}",
                    model.Hostname, ex.GetType().Name, ex.HResult);
            }
        };
        handle.ManualCloseRequested += (_, _) =>
        {
            handle.Close?.Invoke();
            _bus.Publish(new ConnectionClosedEvent(model, DisconnectReason.UserInitiated));
            _suppressedHost = null;
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _reconnectCts?.Cancel(); } catch { /* already disposed */ }
        try { _reconnectCts?.Dispose(); } catch { /* best-effort */ }
        _reconnectCts = null;

        _bus.Unsubscribe<ConnectionRequestedEvent>(this);
        _bus.Unsubscribe<HostCreatedEvent>(this);
        _bus.Unsubscribe<ConnectionEstablishedEvent>(this);
        _bus.Unsubscribe<ConnectionFailedEvent>(this);
        _bus.Unsubscribe<ConnectionClosedEvent>(this);
        _bus.Unsubscribe<CredentialRequestedEvent>(this);

        // D-08 (Phase 5): app-shutdown disconnect now runs through
        // TabHostManager.CloseAllAsync, invoked from MainWindow.OnClosing in Plan 02.
        // The coordinator still has a best-effort cleanup pass here for any hosts it
        // knows about that TabHostManager never saw (edge case: failed Connect that
        // never produced HostCreatedEvent won't register with TabHostManager, but
        // OnHostCreated fires before ConnectStage so in practice this dict should
        // already be empty by the time Dispose runs).
        foreach (var entry in _coordinatorHosts.Values.ToList())
        {
            try { entry.Host.DisconnectedAfterConnect -= OnDisconnectedAfterConnect; }
            catch { /* disposed host may throw */ }
            try
            {
                _disconnect.DisconnectAsync(new DisconnectContext
                {
                    Connection = entry.Model,
                    Host = entry.Host,
                    Reason = DisconnectReason.AppShutdown,
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Disconnect during coordinator dispose threw: {ExceptionType} HResult={HResult:X8}",
                    ex.GetType().Name, ex.HResult);
            }
        }
        _coordinatorHosts.Clear();
        _activeId = null;
        _suppressedHost = null;
    }
}
