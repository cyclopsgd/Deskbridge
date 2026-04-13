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
    private (IProtocolHost Host, ConnectionModel Model)? _active;
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

    public IProtocolHost? ActiveHost => _active?.Host;

    public event EventHandler<IProtocolHost>? HostMounted;
    public event EventHandler<IProtocolHost>? HostUnmounted;
    public event EventHandler<ReconnectUiRequest>? ReconnectOverlayRequested;

    private void OnConnectionRequested(ConnectionRequestedEvent evt)
    {
        if (_disposed) return;

        // Marshal to STA (D-11) — bus may deliver on any thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => OnConnectionRequested(evt));
            return;
        }

        // Rapid-double-click guard. If the user clicks the SAME connection again while
        // a host for that model is still active (in-flight Connect or already connected),
        // drop the duplicate request. Without this, the replace-active-host branch below
        // would dispose the in-flight RdpHostControl mid-Connect(), causing the
        // RdpConnectFailedException discReason=1 → COMException 0x83450003 cascade observed
        // in the field. Auto-reconnect is unaffected: RdpReconnectCoordinator (and the
        // manual-reconnect handler) call _connect.ConnectAsync directly, never going
        // through ConnectionRequestedEvent, and OnDisconnectedAfterConnect clears _active
        // before kicking off the loop anyway.
        if (_active is { } current && current.Model.Id == evt.Connection.Id)
        {
            _logger.LogInformation(
                "Ignoring duplicate connect request for {Hostname} — host for connection id {ConnectionId} still active",
                evt.Connection.Hostname, evt.Connection.Id);
            return;
        }

        // Single-host replacement policy (Phase 4, Open Question #2). If a host is active
        // for a DIFFERENT model, dispatch disconnect first. Phase 5 replaces this with
        // tab-keyed storage.
        if (_active is { } active)
        {
            _logger.LogInformation(
                "Replacing active host for {OldHost} with new connection to {NewHost}",
                active.Model.Hostname, evt.Connection.Hostname);
            _ = RunDisconnectSafely(new DisconnectContext
            {
                Connection = active.Model,
                Host = active.Host,
                Reason = DisconnectReason.UserInitiated,
            });
        }

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

        // WR-01 fix: the replace-active-host branch in OnConnectionRequested fires both
        // RunDisconnectSafely(A) and RunConnectSafely(B) fire-and-forget. CreateHostStage
        // for B is synchronous while A's disconnect pipeline awaits DisconnectAsync, so
        // B's HostCreatedEvent typically lands BEFORE A's ConnectionClosedEvent. Without
        // this handoff, _active overwrites to B while A's WFH is still mounted in
        // ViewportGrid, and A's later ConnectionClosedEvent no-ops because Model.Id no
        // longer matches. Result: two WFHs parented, A's covering B's viewport. Unmount
        // + unsubscribe the previous host explicitly before overwriting.
        if (_active is { } previous && !ReferenceEquals(previous.Host, evt.Host))
        {
            try { previous.Host.DisconnectedAfterConnect -= OnDisconnectedAfterConnect; }
            catch { /* disposed host may throw */ }
            HostUnmounted?.Invoke(this, previous.Host);
            _active = null;
        }

        _active = (evt.Host, evt.Connection);

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

        if (_active is { } active && active.Model.Id == evt.Connection.Id)
        {
            var host = active.Host;
            _active = null;
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
    /// Phase 4 stop-gap (Phase 6 scope: real prompt dialog). <c>CredentialMode.Prompt</c> and
    /// unresolved Own/Inherit cases publish <see cref="CredentialRequestedEvent"/>, but there's
    /// no prompt UI yet. Before this handler existed the event vanished silently and the user
    /// saw nothing. Log a warning and surface a <see cref="ConnectionFailedEvent"/> so the
    /// existing failure UI path shows feedback.
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
            "CredentialRequestedEvent for {Hostname} — prompt mode not yet implemented. " +
            "Use CredentialMode.Own with stored password for now.",
            evt.Connection.Hostname);
        _bus.Publish(new ConnectionFailedEvent(
            evt.Connection,
            "Prompt mode not yet implemented — use Own mode with stored password (Phase 6 scope).",
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

        if (_active is { } active && active.Model.Id == evt.Connection.Id)
        {
            var host = active.Host;
            _active = null;
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
        if (_active is not { } active || !ReferenceEquals(active.Host, host)) return;

        var model = active.Model;
        var category = DisconnectReasonClassifier.Classify(discReason);
        _logger.LogInformation(
            "Post-connect disconnect for {Hostname}: discReason={DiscReason} category={Category}",
            model.Hostname, discReason, category);

        // Suppress the HostUnmounted/Dispose that the normal failure / closed pipeline
        // would trigger — we want the overlay to stay visible over the viewport until
        // either a fresh host mounts (success) or the user closes the overlay.
        _suppressedHost = host;
        _active = null;
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

        _reconnectCts?.Cancel();
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

        if (_active is { } active)
        {
            try { active.Host.DisconnectedAfterConnect -= OnDisconnectedAfterConnect; }
            catch { /* disposed host may throw */ }
            try
            {
                _disconnect.DisconnectAsync(new DisconnectContext
                {
                    Connection = active.Model,
                    Host = active.Host,
                    Reason = DisconnectReason.AppShutdown,
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Disconnect during coordinator dispose threw: {ExceptionType} HResult={HResult:X8}",
                    ex.GetType().Name, ex.HResult);
            }
            _active = null;
        }

        _suppressedHost = null;
    }
}
