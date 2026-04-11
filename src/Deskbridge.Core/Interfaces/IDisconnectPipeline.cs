using Deskbridge.Core.Pipeline;

namespace Deskbridge.Core.Interfaces;

public interface IDisconnectPipelineStage
{
    string Name { get; }
    int Order { get; }
    Task<PipelineResult> ExecuteAsync(DisconnectContext context);
}

public interface IDisconnectPipeline
{
    void AddStage(IDisconnectPipelineStage stage);
    Task<PipelineResult> DisconnectAsync(DisconnectContext context);
}
