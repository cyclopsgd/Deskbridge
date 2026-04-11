using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;

namespace Deskbridge.Tests;

public sealed class DisconnectPipelineTests
{
    [Fact]
    public async Task DisconnectPipeline_ExecutesStagesInOrder()
    {
        var pipeline = new DisconnectPipeline();
        var executionOrder = new List<int>();

        var stage200 = CreateStage("Stage200", 200, true, executionOrder);
        var stage100 = CreateStage("Stage100", 100, true, executionOrder);
        var stage300 = CreateStage("Stage300", 300, true, executionOrder);

        pipeline.AddStage(stage200);
        pipeline.AddStage(stage100);
        pipeline.AddStage(stage300);

        var context = new DisconnectContext
        {
            Connection = new ConnectionModel(),
            Reason = DisconnectReason.UserInitiated
        };

        await pipeline.DisconnectAsync(context);

        executionOrder.Should().ContainInOrder(100, 200, 300);
    }

    [Fact]
    public async Task DisconnectPipeline_AbortsOnFailure()
    {
        var pipeline = new DisconnectPipeline();
        var executionOrder = new List<int>();

        pipeline.AddStage(CreateStage("S1", 100, true, executionOrder));
        pipeline.AddStage(CreateStage("S2", 200, false, executionOrder, "Cleanup failed"));
        pipeline.AddStage(CreateStage("S3", 300, true, executionOrder));

        var context = new DisconnectContext
        {
            Connection = new ConnectionModel(),
            Reason = DisconnectReason.UserInitiated
        };

        var result = await pipeline.DisconnectAsync(context);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("Cleanup failed");
        executionOrder.Should().ContainInOrder(100, 200);
        executionOrder.Should().NotContain(300);
    }

    [Fact]
    public async Task DisconnectPipeline_ReturnsSuccessWhenAllStagesPass()
    {
        var pipeline = new DisconnectPipeline();
        var executionOrder = new List<int>();

        pipeline.AddStage(CreateStage("S1", 100, true, executionOrder));
        pipeline.AddStage(CreateStage("S2", 200, true, executionOrder));
        pipeline.AddStage(CreateStage("S3", 300, true, executionOrder));

        var context = new DisconnectContext
        {
            Connection = new ConnectionModel(),
            Reason = DisconnectReason.RemoteDisconnect
        };

        var result = await pipeline.DisconnectAsync(context);

        result.Success.Should().BeTrue();
        executionOrder.Should().HaveCount(3);
    }

    private static IDisconnectPipelineStage CreateStage(
        string name, int order, bool succeeds, List<int> executionOrder, string? failureReason = null)
    {
        var stage = Substitute.For<IDisconnectPipelineStage>();
        stage.Name.Returns(name);
        stage.Order.Returns(order);
        stage.ExecuteAsync(Arg.Any<DisconnectContext>())
            .Returns(callInfo =>
            {
                executionOrder.Add(order);
                return Task.FromResult(new PipelineResult(succeeds, succeeds ? null : failureReason));
            });
        return stage;
    }
}
