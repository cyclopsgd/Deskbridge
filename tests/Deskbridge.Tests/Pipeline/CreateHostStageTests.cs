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
        var stage = new CreateHostStage(factory);
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
        var stage = new CreateHostStage(factory);
        var ctx = new ConnectionContext { Connection = connection };

        await stage.ExecuteAsync(ctx);

        ctx.Host.Should().NotBeNull();
    }

    [Fact]
    public void StageOrderIs200_AndName_IsCreateHost()
    {
        var stage = new CreateHostStage(Substitute.For<IProtocolHostFactory>());

        stage.Order.Should().Be(200);
        stage.Name.Should().Be("CreateHost");
    }
}
