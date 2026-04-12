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
    private (IProtocolHost Host, ConnectionModel Model)? _active;
    private bool _disposed;

    public ConnectionCoordinator(
        IEventBus bus,
        IConnectionPipeline connect,
        IDisconnectPipeline disconnect,
        ILogger<ConnectionCoordinator> logger,
        Dispatcher? dispatcher = null)
    {
        _bus = bus;
        _connect = connect;
        _disconnect = disconnect;
        _logger = logger;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;

        _bus.Subscribe<ConnectionRequestedEvent>(this, OnConnectionRequested);
        _bus.Subscribe<HostCreatedEvent>(this, OnHostCreated);
        _bus.Subscribe<ConnectionEstablishedEvent>(this, OnConnectionEstablished);
        _bus.Subscribe<ConnectionFailedEvent>(this, OnConnectionFailed);
        _bus.Subscribe<ConnectionClosedEvent>(this, OnConnectionClosed);
    }

    public IProtocolHost? ActiveHost => _active?.Host;

    public event EventHandler<IProtocolHost>? HostMounted;
    public event EventHandler<IProtocolHost>? HostUnmounted;

    private void OnConnectionRequested(ConnectionRequestedEvent evt)
    {
        if (_disposed) return;

        // Marshal to STA (D-11) — bus may deliver on any thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => OnConnectionRequested(evt));
            return;
        }

        // Single-host replacement policy (Phase 4, Open Question #2). If a host is active,
        // dispatch disconnect first. Phase 5 replaces this with tab-keyed storage.
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
            var safeMessage = ex is System.Runtime.InteropServices.COMException
                                or System.Runtime.InteropServices.ExternalException
                                or System.Security.Authentication.AuthenticationException
                                or System.Net.WebException
                ? "<redacted: sensitive exception type>"
                : ex.Message;
            _logger.LogError(
                "Connection pipeline threw for {Hostname}: {ExceptionType} HResult={HResult:X8} Message={Message}",
                model.Hostname, ex.GetType().Name, ex.HResult, safeMessage);
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
            var safeMessage = ex is System.Runtime.InteropServices.COMException
                                or System.Runtime.InteropServices.ExternalException
                                or System.Security.Authentication.AuthenticationException
                                or System.Net.WebException
                ? "<redacted: sensitive exception type>"
                : ex.Message;
            _logger.LogError(
                "Disconnect pipeline threw for {Hostname}: {ExceptionType} HResult={HResult:X8} Message={Message}",
                ctx.Connection.Hostname, ex.GetType().Name, ex.HResult, safeMessage);
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

        _active = (evt.Host, evt.Connection);
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
            HostUnmounted?.Invoke(this, host);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _bus.Unsubscribe<ConnectionRequestedEvent>(this);
        _bus.Unsubscribe<HostCreatedEvent>(this);
        _bus.Unsubscribe<ConnectionEstablishedEvent>(this);
        _bus.Unsubscribe<ConnectionFailedEvent>(this);
        _bus.Unsubscribe<ConnectionClosedEvent>(this);

        if (_active is { } active)
        {
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
    }
}
