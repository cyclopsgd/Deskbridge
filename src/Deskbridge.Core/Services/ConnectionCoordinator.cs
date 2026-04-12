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
        _bus.Subscribe<ConnectionEstablishedEvent>(this, OnConnectionEstablished);
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

        _logger.LogInformation(
            "[diag] Dispatching connect for {Hostname} — thread={ThreadId} apartment={Apartment} dispatcherAccess={HasAccess}",
            evt.Connection.Hostname,
            System.Environment.CurrentManagedThreadId,
            Thread.CurrentThread.GetApartmentState(),
            _dispatcher.CheckAccess());

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
        _logger.LogInformation(
            "[diag] RunConnectSafely entry — thread={ThreadId} apartment={Apartment}",
            System.Environment.CurrentManagedThreadId,
            Thread.CurrentThread.GetApartmentState());
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

    private void OnConnectionEstablished(ConnectionEstablishedEvent evt)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => OnConnectionEstablished(evt));
            return;
        }

        _active = (evt.Host, evt.Connection);
        HostMounted?.Invoke(this, evt.Host);
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
        _bus.Unsubscribe<ConnectionEstablishedEvent>(this);
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
