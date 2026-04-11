using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;

namespace Deskbridge.Core.Interfaces;

public interface IConnectionPipelineStage
{
    string Name { get; }
    int Order { get; }
    Task<PipelineResult> ExecuteAsync(ConnectionContext context);
}

public interface IConnectionPipeline
{
    void AddStage(IConnectionPipelineStage stage);
    Task<PipelineResult> ConnectAsync(ConnectionModel connection);
}
