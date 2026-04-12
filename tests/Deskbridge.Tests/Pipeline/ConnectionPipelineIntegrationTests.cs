using System.Net;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deskbridge.Tests.Pipeline;

public sealed class ConnectionPipelineIntegrationTests
{
    [Fact]
    public async Task ConnectPipeline_RunsStages_InResolve100_Create200_Connect300_Recents400_Order()
    {
        var trace = new List<string>();

        var creds = Substitute.For<ICredentialService>();
        creds.GetForConnection(Arg.Any<ConnectionModel>()).Returns(new NetworkCredential("u", "p", "d"));
        var store = Substitute.For<IConnectionStore>();
        var bus = Substitute.For<IEventBus>();

        var host = Substitute.For<IProtocolHost>();
        host.ConnectAsync(Arg.Any<ConnectionContext>()).Returns(Task.CompletedTask);
        var factory = Substitute.For<IProtocolHostFactory>();
        factory.Create(Protocol.Rdp).Returns(host);

        // Wrap stages to trace execution
        var resolve = new TracingConnectStage(
            new ResolveCredentialsStage(creds, store, bus, NullLogger<ResolveCredentialsStage>.Instance),
            "Resolve", trace);
        var create = new TracingConnectStage(new CreateHostStage(factory), "Create", trace);
        var connect = new TracingConnectStage(new ConnectStage(bus, NullLogger<ConnectStage>.Instance), "Connect", trace);
        var recents = new TracingConnectStage(new UpdateRecentsStage(store), "Recents", trace);

        var pipeline = new ConnectionPipeline();
        // Add in reverse to verify Order property is used
        pipeline.AddStage(recents);
        pipeline.AddStage(connect);
        pipeline.AddStage(create);
        pipeline.AddStage(resolve);

        var result = await pipeline.ConnectAsync(new ConnectionModel
        {
            Hostname = "h",
            Protocol = Protocol.Rdp,
            CredentialMode = CredentialMode.Own
        });

        result.Success.Should().BeTrue();
        trace.Should().ContainInOrder("Resolve", "Create", "Connect", "Recents");
    }

    [Fact]
    public async Task DisconnectPipeline_RunsStages_InDisconnect100_Dispose200_Publish300_Order()
    {
        var trace = new List<string>();
        var host = Substitute.For<IProtocolHost>();
        host.DisconnectAsync().Returns(Task.CompletedTask);
        var bus = Substitute.For<IEventBus>();

        var disconnect = new TracingDisconnectStage(new DisconnectStage(NullLogger<DisconnectStage>.Instance), "Disconnect", trace);
        var dispose = new TracingDisconnectStage(new DisposeStage(NullLogger<DisposeStage>.Instance), "Dispose", trace);
        var publish = new TracingDisconnectStage(new PublishClosedEventStage(bus), "Publish", trace);

        var pipeline = new DisconnectPipeline();
        pipeline.AddStage(publish);
        pipeline.AddStage(dispose);
        pipeline.AddStage(disconnect);

        var result = await pipeline.DisconnectAsync(new DisconnectContext
        {
            Connection = new ConnectionModel { Hostname = "h" },
            Host = host,
            Reason = DisconnectReason.UserInitiated
        });

        result.Success.Should().BeTrue();
        trace.Should().ContainInOrder("Disconnect", "Dispose", "Publish");
    }

    // --- Tracing wrappers preserve the Order property while recording execution order ---
    private sealed class TracingConnectStage(IConnectionPipelineStage inner, string label, List<string> trace)
        : IConnectionPipelineStage
    {
        public string Name => inner.Name;
        public int Order => inner.Order;
        public async Task<PipelineResult> ExecuteAsync(ConnectionContext context)
        {
            trace.Add(label);
            return await inner.ExecuteAsync(context);
        }
    }

    private sealed class TracingDisconnectStage(IDisconnectPipelineStage inner, string label, List<string> trace)
        : IDisconnectPipelineStage
    {
        public string Name => inner.Name;
        public int Order => inner.Order;
        public async Task<PipelineResult> ExecuteAsync(DisconnectContext context)
        {
            trace.Add(label);
            return await inner.ExecuteAsync(context);
        }
    }
}
