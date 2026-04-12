using Deskbridge.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Deskbridge.Core.Pipeline.Stages;

/// <summary>
/// Runs the strict disposal sequence on <see cref="DisconnectContext.Host"/>. Exceptions
/// are caught and logged (type + HResult only); pipeline continues so
/// <c>PublishClosedEventStage</c> always fires.
/// </summary>
public sealed class DisposeStage(ILogger<DisposeStage> logger) : IDisconnectPipelineStage
{
    public string Name => "Dispose";
    public int Order => 200;

    public Task<PipelineResult> ExecuteAsync(DisconnectContext ctx)
    {
        try
        {
            ctx.Host?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Dispose threw: {ExceptionType} HResult={HResult:X8}",
                ex.GetType().Name, ex.HResult);
        }
        return Task.FromResult(new PipelineResult(true));
    }
}
