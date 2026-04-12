using Deskbridge.Core.Interfaces;

namespace Deskbridge.Core.Pipeline.Stages;

/// <summary>
/// Sets <c>ConnectionModel.LastUsedAt = DateTime.UtcNow</c> and persists via
/// <see cref="IConnectionStore.Save"/>. Runs after successful connect (Order=400).
/// </summary>
public sealed class UpdateRecentsStage(IConnectionStore store) : IConnectionPipelineStage
{
    public string Name => "UpdateRecents";
    public int Order => 400;

    public Task<PipelineResult> ExecuteAsync(ConnectionContext ctx)
    {
        ctx.Connection.LastUsedAt = DateTime.UtcNow;
        store.Save(ctx.Connection);
        return Task.FromResult(new PipelineResult(true));
    }
}
