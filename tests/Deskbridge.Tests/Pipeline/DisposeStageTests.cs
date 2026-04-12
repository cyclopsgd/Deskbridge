using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deskbridge.Tests.Pipeline;

public sealed class DisposeStageTests
{
    [Fact]
    public async Task CallsHostDispose()
    {
        var host = Substitute.For<IProtocolHost>();
        var stage = new DisposeStage(NullLogger<DisposeStage>.Instance);
        var ctx = new DisconnectContext
        {
            Connection = new ConnectionModel { Hostname = "h" },
            Host = host,
            Reason = DisconnectReason.UserInitiated
        };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        host.Received(1).Dispose();
    }

    [Fact]
    public async Task ExceptionInDispose_IsCaughtAndLogged_DoesNotAbortPipeline()
    {
        var host = Substitute.For<IProtocolHost>();
        host.When(h => h.Dispose()).Do(_ => throw new InvalidOperationException("boom"));
        var stage = new DisposeStage(NullLogger<DisposeStage>.Instance);
        var ctx = new DisconnectContext
        {
            Connection = new ConnectionModel { Hostname = "h" },
            Host = host,
            Reason = DisconnectReason.UserInitiated
        };

        var result = await stage.ExecuteAsync(ctx);

        // Dispose failure is caught — pipeline must continue so PublishClosedEventStage runs
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void StageOrderIs200()
    {
        var stage = new DisposeStage(NullLogger<DisposeStage>.Instance);
        stage.Order.Should().Be(200);
        stage.Name.Should().Be("Dispose");
    }
}
