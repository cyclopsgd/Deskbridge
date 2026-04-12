using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;

namespace Deskbridge.Tests.Pipeline;

public sealed class PublishClosedEventStageTests
{
    [Fact]
    public async Task PublishesConnectionClosedEvent_WithContextReason()
    {
        var connection = new ConnectionModel { Hostname = "h" };
        var bus = Substitute.For<IEventBus>();
        var stage = new PublishClosedEventStage(bus);
        var ctx = new DisconnectContext
        {
            Connection = connection,
            Reason = DisconnectReason.UserInitiated
        };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        bus.Received(1).Publish(Arg.Is<ConnectionClosedEvent>(e =>
            e.Connection == connection && e.Reason == DisconnectReason.UserInitiated));
    }

    [Fact]
    public void StageOrderIs300()
    {
        var stage = new PublishClosedEventStage(Substitute.For<IEventBus>());
        stage.Order.Should().Be(300);
        stage.Name.Should().Be("PublishClosed");
    }
}
