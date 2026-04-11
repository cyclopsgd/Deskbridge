using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Pipeline;

public sealed class ConnectionPipeline : IConnectionPipeline
{
    private readonly List<IConnectionPipelineStage> _stages = [];

    public void AddStage(IConnectionPipelineStage stage)
    {
        _stages.Add(stage);
    }

    public async Task<PipelineResult> ConnectAsync(ConnectionModel connection)
    {
        var context = new ConnectionContext { Connection = connection };
        foreach (var stage in _stages.OrderBy(s => s.Order))
        {
            var result = await stage.ExecuteAsync(context);
            if (!result.Success)
                return result;
        }
        return new PipelineResult(true);
    }
}
