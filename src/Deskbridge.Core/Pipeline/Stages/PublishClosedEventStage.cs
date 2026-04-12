using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;

namespace Deskbridge.Core.Pipeline.Stages;

/// <summary>
/// Final disconnect-pipeline stage: publishes <see cref="ConnectionClosedEvent"/> so
/// subscribers (tab UI, reconnect coordinator, audit log) can react.
/// </summary>
public sealed class PublishClosedEventStage(IEventBus bus) : IDisconnectPipelineStage
{
    public string Name => "PublishClosed";
    public int Order => 300;

    public Task<PipelineResult> ExecuteAsync(DisconnectContext ctx)
    {
        bus.Publish(new ConnectionClosedEvent(ctx.Connection, ctx.Reason));
        return Task.FromResult(new PipelineResult(true));
    }
}
