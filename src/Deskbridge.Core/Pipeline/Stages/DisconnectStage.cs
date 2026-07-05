using Deskbridge.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Deskbridge.Core.Pipeline.Stages;

/// <summary>
/// Awaits <see cref="IProtocolHost.DisconnectAsync"/> with a 35s timeout. Disconnect failure
/// is non-fatal: the stage still returns success so <c>DisposeStage</c> always runs.
/// </summary>
public sealed class DisconnectStage : IDisconnectPipelineStage
{
    private readonly ILogger<DisconnectStage> _logger;
    private readonly TimeSpan _timeout;

    public DisconnectStage(ILogger<DisconnectStage> logger, TimeSpan? timeout = null)
    {
        _logger = logger;
        // [CITED: audit C3] 35s staggers ABOVE RdpHostControl.DisconnectAsync's internal
        // 30s polling deadline so the host's own timeout wins and its loop completes
        // instead of being abandoned mid-poll by the stage timeout.
        _timeout = timeout ?? TimeSpan.FromSeconds(35);
    }

    public string Name => "Disconnect";
    public int Order => 100;

    public async Task<PipelineResult> ExecuteAsync(DisconnectContext ctx)
    {
        if (ctx.Host is null)
        {
            return new PipelineResult(true);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            var disconnectTask = ctx.Host.DisconnectAsync();
            var finished = await Task.WhenAny(disconnectTask, Task.Delay(Timeout.Infinite, cts.Token));
            if (finished != disconnectTask)
            {
                _logger.LogWarning("Disconnect timed out for {Hostname}", ctx.Connection.Hostname);
                // [CITED: audit C3] Observe the abandoned polling task so a late fault
                // doesn't surface as an unobserved-task exception in CrashHandler.
                _ = disconnectTask.ContinueWith(
                    t => _logger.LogDebug(
                        "Abandoned disconnect task faulted: {ExceptionType}",
                        t.Exception?.GetBaseException().GetType().Name),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
            else
            {
                await disconnectTask;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Disconnect threw for {Hostname}: {ExceptionType} HResult={HResult:X8}",
                ctx.Connection.Hostname, ex.GetType().Name, ex.HResult);
        }

        // Disconnect failure is non-fatal; DisposeStage must run.
        return new PipelineResult(true);
    }
}
