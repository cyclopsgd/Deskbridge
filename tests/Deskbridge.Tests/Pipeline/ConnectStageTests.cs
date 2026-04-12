using System.Runtime.InteropServices;
using Deskbridge.Core.Events;
using Deskbridge.Core.Exceptions;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deskbridge.Tests.Pipeline;

public sealed class ConnectStageTests
{
    [Fact]
    public async Task AwaitsConnectAsync_AndPublishesEstablishedEvent_OnSuccess()
    {
        var connection = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var host = Substitute.For<IProtocolHost>();
        host.ConnectAsync(Arg.Any<ConnectionContext>()).Returns(Task.CompletedTask);
        var bus = Substitute.For<IEventBus>();
        var stage = new ConnectStage(bus, NullLogger<ConnectStage>.Instance);
        var ctx = new ConnectionContext { Connection = connection, Host = host };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        bus.Received().Publish(Arg.Any<ConnectionEstablishedEvent>());
    }

    [Fact]
    public async Task TimesOutAfter_Timeout_AndPublishesFailedEvent()
    {
        var connection = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var host = Substitute.For<IProtocolHost>();
        // Never completes
        host.ConnectAsync(Arg.Any<ConnectionContext>()).Returns(new TaskCompletionSource<bool>().Task);
        var bus = Substitute.For<IEventBus>();
        var stage = new ConnectStage(bus, NullLogger<ConnectStage>.Instance, TimeSpan.FromMilliseconds(100));
        var ctx = new ConnectionContext { Connection = connection, Host = host };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        bus.Received().Publish(Arg.Any<ConnectionFailedEvent>());
    }

    [Fact]
    public async Task RdpConnectFailedException_IsPublishedAsFailedEvent_WithHumanReason()
    {
        var connection = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var host = Substitute.For<IProtocolHost>();
        host.ConnectAsync(Arg.Any<ConnectionContext>())
            .Returns(Task.FromException(new RdpConnectFailedException(516, "Socket closed")));
        var bus = Substitute.For<IEventBus>();
        var stage = new ConnectStage(bus, NullLogger<ConnectStage>.Instance);
        var ctx = new ConnectionContext { Connection = connection, Host = host };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("Socket closed");
        bus.Received().Publish(Arg.Is<ConnectionFailedEvent>(e => e.Reason == "Socket closed"));
    }

    [Fact]
    public async Task COMException_IsCaught_AndPublishedAsFailedEvent()
    {
        var connection = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var host = Substitute.For<IProtocolHost>();
        host.ConnectAsync(Arg.Any<ConnectionContext>())
            .Returns(Task.FromException(new COMException("password=Hunter2", unchecked((int)0x80004005))));
        var bus = Substitute.For<IEventBus>();
        var stage = new ConnectStage(bus, NullLogger<ConnectStage>.Instance);
        var ctx = new ConnectionContext { Connection = connection, Host = host };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        bus.Received().Publish(Arg.Any<ConnectionFailedEvent>());
    }

    [Fact]
    public async Task HostNotCreated_ReturnsFailure_WithoutPublishingEvent()
    {
        var connection = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var bus = Substitute.For<IEventBus>();
        var stage = new ConnectStage(bus, NullLogger<ConnectStage>.Instance);
        var ctx = new ConnectionContext { Connection = connection, Host = null };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        bus.DidNotReceive().Publish(Arg.Any<ConnectionEstablishedEvent>());
    }

    [Fact]
    public void StageOrderIs300()
    {
        var stage = new ConnectStage(Substitute.For<IEventBus>(), NullLogger<ConnectStage>.Instance);
        stage.Order.Should().Be(300);
        stage.Name.Should().Be("Connect");
    }
}
