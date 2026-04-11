using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;

namespace Deskbridge.Tests;

public sealed class ConnectionPipelineTests
{
    [Fact]
    public async Task Pipeline_ExecutesStagesInOrder()
    {
        var pipeline = new ConnectionPipeline();
        var executionOrder = new List<int>();

        var stage200 = CreateStage("Stage200", 200, true, executionOrder);
        var stage100 = CreateStage("Stage100", 100, true, executionOrder);
        var stage300 = CreateStage("Stage300", 300, true, executionOrder);

        pipeline.AddStage(stage200);
        pipeline.AddStage(stage100);
        pipeline.AddStage(stage300);

        await pipeline.ConnectAsync(new ConnectionModel());

        executionOrder.Should().ContainInOrder(100, 200, 300);
    }

    [Fact]
    public async Task Pipeline_AbortsOnFailure()
    {
        var pipeline = new ConnectionPipeline();
        var executionOrder = new List<int>();

        var stage100 = CreateStage("Stage100", 100, true, executionOrder);
        var stage200 = CreateStage("Stage200", 200, false, executionOrder, "Simulated failure");
        var stage300 = CreateStage("Stage300", 300, true, executionOrder);

        pipeline.AddStage(stage100);
        pipeline.AddStage(stage200);
        pipeline.AddStage(stage300);

        var result = await pipeline.ConnectAsync(new ConnectionModel());

        result.Success.Should().BeFalse();
        executionOrder.Should().ContainInOrder(100, 200);
        executionOrder.Should().NotContain(300);
    }

    [Fact]
    public async Task Pipeline_ReturnsSuccessWhenAllStagesPass()
    {
        var pipeline = new ConnectionPipeline();
        var executionOrder = new List<int>();

        pipeline.AddStage(CreateStage("S1", 100, true, executionOrder));
        pipeline.AddStage(CreateStage("S2", 200, true, executionOrder));
        pipeline.AddStage(CreateStage("S3", 300, true, executionOrder));

        var result = await pipeline.ConnectAsync(new ConnectionModel());

        result.Success.Should().BeTrue();
        executionOrder.Should().HaveCount(3);
    }

    [Fact]
    public async Task Pipeline_ReturnsFailureReasonFromFailingStage()
    {
        var pipeline = new ConnectionPipeline();
        var executionOrder = new List<int>();

        pipeline.AddStage(CreateStage("S1", 100, true, executionOrder));
        pipeline.AddStage(CreateStage("Failing", 200, false, executionOrder, "Connection refused"));

        var result = await pipeline.ConnectAsync(new ConnectionModel());

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("Connection refused");
    }

    [Fact]
    public async Task Pipeline_WithNoStages_ReturnsSuccess()
    {
        var pipeline = new ConnectionPipeline();

        var result = await pipeline.ConnectAsync(new ConnectionModel());

        result.Success.Should().BeTrue();
    }

    private static IConnectionPipelineStage CreateStage(
        string name, int order, bool succeeds, List<int> executionOrder, string? failureReason = null)
    {
        var stage = Substitute.For<IConnectionPipelineStage>();
        stage.Name.Returns(name);
        stage.Order.Returns(order);
        stage.ExecuteAsync(Arg.Any<ConnectionContext>())
            .Returns(callInfo =>
            {
                executionOrder.Add(order);
                return Task.FromResult(new PipelineResult(succeeds, succeeds ? null : failureReason));
            });
        return stage;
    }
}
