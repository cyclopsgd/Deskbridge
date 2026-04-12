using Deskbridge.Core.Interfaces;

namespace Deskbridge.Core.Pipeline.Stages;

/// <summary>
/// Resolves the protocol host factory for the connection's protocol and stores the
/// created host on <see cref="ConnectionContext.Host"/> for downstream stages.
/// </summary>
public sealed class CreateHostStage(IProtocolHostFactory factory) : IConnectionPipelineStage
{
    public string Name => "CreateHost";
    public int Order => 200;

    public Task<PipelineResult> ExecuteAsync(ConnectionContext ctx)
    {
        ctx.Host = factory.Create(ctx.Connection.Protocol);
        return Task.FromResult(new PipelineResult(true));
    }
}
