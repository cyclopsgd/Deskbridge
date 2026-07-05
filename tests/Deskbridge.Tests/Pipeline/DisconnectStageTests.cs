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
    public async Task TimesOut_StillReturnsSuccess_WithWarning()
    {
        // Injects a small test-only timeout via ctor; a never-completing disconnect task
        // must still short-circuit and return success (DisposeStage always runs).
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
    public void DefaultTimeout_Is35Seconds_StaggeredAboveHostInternal30s()
    {
        // Audit C3: the stage default must stay ABOVE RdpHostControl.DisconnectAsync's
        // internal 30s polling deadline so the host's own timeout wins. Pinned via the
        // private field — no public seam exists and a behavioral test would take 35s.
        var stage = new DisconnectStage(NullLogger<DisconnectStage>.Instance);
        var timeout = (TimeSpan?)typeof(DisconnectStage)
            .GetField("_timeout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(stage);
        timeout.Should().Be(TimeSpan.FromSeconds(35));
    }

    [Fact]
    public void StageOrderIs100()
    {
        var stage = new DisconnectStage(NullLogger<DisconnectStage>.Instance);
        stage.Order.Should().Be(100);
        stage.Name.Should().Be("Disconnect");
    }
}
