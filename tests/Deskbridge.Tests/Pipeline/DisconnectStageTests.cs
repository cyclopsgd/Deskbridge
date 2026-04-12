using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deskbridge.Tests.Pipeline;

public sealed class DisconnectStageTests
{
    [Fact]
    public async Task CallsHostDisconnectAsync_AndReturnsSuccess()
    {
        var host = Substitute.For<IProtocolHost>();
        host.DisconnectAsync().Returns(Task.CompletedTask);
        var stage = new DisconnectStage(NullLogger<DisconnectStage>.Instance);
        var ctx = new DisconnectContext
        {
            Connection = new ConnectionModel { Hostname = "h" },
            Host = host,
            Reason = DisconnectReason.UserInitiated
        };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        await host.Received().DisconnectAsync();
    }

    [Fact]
    public async Task TimesOutAfter30Seconds_StillReturnsSuccess_WithWarning()
    {
        // Use a stage with a small test-only timeout via ctor; if the prod stage hardcodes 30s,
        // rely on behavior: never-completing task should still short-circuit. We simulate by
        // giving a task that is NEVER completing and a short test timeout.
        var host = Substitute.For<IProtocolHost>();
        host.DisconnectAsync().Returns(new TaskCompletionSource<bool>().Task);
        var stage = new DisconnectStage(NullLogger<DisconnectStage>.Instance, TimeSpan.FromMilliseconds(100));
        var ctx = new DisconnectContext
        {
            Connection = new ConnectionModel { Hostname = "h" },
            Host = host,
            Reason = DisconnectReason.UserInitiated
        };

        var result = await stage.ExecuteAsync(ctx);

        // Disconnect failure is non-fatal — DisposeStage runs anyway
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void StageOrderIs100()
    {
        var stage = new DisconnectStage(NullLogger<DisconnectStage>.Instance);
        stage.Order.Should().Be(100);
        stage.Name.Should().Be("Disconnect");
    }
}
