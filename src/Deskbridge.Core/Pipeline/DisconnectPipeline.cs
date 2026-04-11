using Deskbridge.Core.Interfaces;

namespace Deskbridge.Core.Pipeline;

public sealed class DisconnectPipeline : IDisconnectPipeline
{
    private readonly List<IDisconnectPipelineStage> _stages = [];

    public void AddStage(IDisconnectPipelineStage stage)
    {
        _stages.Add(stage);
    }

    public async Task<PipelineResult> DisconnectAsync(DisconnectContext context)
    {
        foreach (var stage in _stages.OrderBy(s => s.Order))
        {
            var result = await stage.ExecuteAsync(context);
            if (!result.Success)
                return result;
        }
        return new PipelineResult(true);
    }
}
