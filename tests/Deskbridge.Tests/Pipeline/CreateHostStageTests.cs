using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;

namespace Deskbridge.Tests.Pipeline;

public sealed class CreateHostStageTests
{
    [Fact]
    public async Task CreatesHost_ViaFactory_ForConnectionProtocol()
    {
        var connection = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var fakeHost = Substitute.For<IProtocolHost>();
        var factory = Substitute.For<IProtocolHostFactory>();
        factory.Create(Protocol.Rdp).Returns(fakeHost);
        var bus = Substitute.For<IEventBus>();
        var stage = new CreateHostStage(factory, bus);
        var ctx = new ConnectionContext { Connection = connection };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Host.Should().BeSameAs(fakeHost);
        factory.Received(1).Create(Protocol.Rdp);
    }

    [Fact]
    public async Task SetsHostOnContext_BeforeReturningSuccess()
    {
        var connection = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var fakeHost = Substitute.For<IProtocolHost>();
        var factory = Substitute.For<IProtocolHostFactory>();
        factory.Create(Arg.Any<Protocol>()).Returns(fakeHost);
        var bus = Substitute.For<IEventBus>();
        var stage = new CreateHostStage(factory, bus);
        var ctx = new ConnectionContext { Connection = connection };

        await stage.ExecuteAsync(ctx);

        ctx.Host.Should().NotBeNull();
    }

    [Fact]
    public void StageOrderIs200_AndName_IsCreateHost()
    {
        var stage = new CreateHostStage(
            Substitute.For<IProtocolHostFactory>(),
            Substitute.For<IEventBus>());

        stage.Order.Should().Be(200);
        stage.Name.Should().Be("CreateHost");
    }

    /// <summary>
    /// Regression guard for the siting-order bug: the stage MUST publish
    /// <see cref="HostCreatedEvent"/> before returning so the coordinator gets a chance
    /// to mount the host + force a layout pass BEFORE ConnectStage (Order=300) runs.
    /// See RDP-ACTIVEX-PITFALLS §1.
    /// </summary>
    [Fact]
    public async Task PublishesHostCreatedEvent_WithSameHost_BeforeReturning()
    {
        var connection = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var fakeHost = Substitute.For<IProtocolHost>();
        var factory = Substitute.For<IProtocolHostFactory>();
        factory.Create(Protocol.Rdp).Returns(fakeHost);
        var bus = Substitute.For<IEventBus>();

        HostCreatedEvent? captured = null;
        bus.When(b => b.Publish(Arg.Any<HostCreatedEvent>()))
            .Do(ci => captured = ci.Arg<HostCreatedEvent>());

        var stage = new CreateHostStage(factory, bus);
        var ctx = new ConnectionContext { Connection = connection };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Host.Should().BeSameAs(fakeHost);
        captured.Host.Should().BeSameAs(ctx.Host);
        captured.Connection.Should().BeSameAs(connection);
    }
}
