using System.Runtime.InteropServices;
using Deskbridge.Core.Events;
using Deskbridge.Core.Exceptions;
using Deskbridge.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Deskbridge.Core.Pipeline.Stages;

/// <summary>
/// Awaits <see cref="IProtocolHost.ConnectAsync"/> with a configurable timeout (default 30s).
/// Publishes <see cref="ConnectionEstablishedEvent"/> on success or
/// <see cref="ConnectionFailedEvent"/> on <see cref="RdpConnectFailedException"/> /
/// <see cref="TimeoutException"/> / <see cref="COMException"/>.
///
/// <para>Logging discipline: COM exceptions log <c>ex.GetType().Name</c> + <c>ex.HResult:X8</c>
/// only — never <c>ex.Message</c> or <c>ex.ToString()</c> (T-04-EXC).</para>
/// </summary>
public sealed class ConnectStage : IConnectionPipelineStage
{
    private readonly IEventBus _bus;
    private readonly ILogger<ConnectStage> _logger;
    private readonly TimeSpan _timeout;

    public ConnectStage(IEventBus bus, ILogger<ConnectStage> logger, TimeSpan? connectTimeout = null)
    {
        _bus = bus;
        _logger = logger;
        _timeout = connectTimeout ?? TimeSpan.FromSeconds(30);
    }

    public string Name => "Connect";
    public int Order => 300;

    public async Task<PipelineResult> ExecuteAsync(ConnectionContext ctx)
    {
        if (ctx.Host is null)
        {
            return new PipelineResult(false, "Host not created — CreateHostStage must run first.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            // Plain await — continuations stay on STA per D-11 (WPF DispatcherSynchronizationContext).
            var connectTask = ctx.Host.ConnectAsync(ctx);
            var finished = await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, cts.Token));
            if (finished != connectTask)
            {
                throw new TimeoutException($"RDP connect exceeded {_timeout.TotalSeconds}s timeout.");
            }
            await connectTask;  // Propagate exception if any

            _bus.Publish(new ConnectionEstablishedEvent(ctx.Connection, ctx.Host));
            return new PipelineResult(true);
        }
        catch (RdpConnectFailedException ex)
        {
            // HumanReason is sanitized classifier output (see DisconnectReasonClassifier) —
            // safe to log alongside the raw discReason. No credential material leaks.
            _logger.LogWarning(
                "Connect failed for {Hostname}: RdpConnectFailedException discReason={DiscReason} reason={Reason}",
                ctx.Connection.Hostname, ex.DiscReason, ex.HumanReason);
            _bus.Publish(new ConnectionFailedEvent(ctx.Connection, ex.HumanReason, ex));
            return new PipelineResult(false, ex.HumanReason);
        }
        catch (Exception ex) when (ex is TimeoutException or COMException or OperationCanceledException)
        {
            _logger.LogWarning(
                "Connect failed for {Hostname}: {ExceptionType} HResult={HResult:X8}",
                ctx.Connection.Hostname, ex.GetType().Name, ex.HResult);
            var reason = $"{ex.GetType().Name} (HResult 0x{ex.HResult:X8})";
            _bus.Publish(new ConnectionFailedEvent(ctx.Connection, reason, ex));
            return new PipelineResult(false, reason);
        }
    }
}
