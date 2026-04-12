using System.Net;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deskbridge.Tests.Pipeline;

/// <summary>
/// Unit tests for <see cref="ResolveCredentialsStage"/> (Order=100, protocol-agnostic, D-08/D-10).
///
/// The stage walks <see cref="CredentialMode"/> branches: Own → GetForConnection,
/// Inherit → ResolveInherited, Prompt → fail with CredentialRequestedEvent. On resolution it
/// must copy username/domain into <see cref="ConnectionContext.Connection"/> and set
/// <see cref="ConnectionContext.ResolvedPassword"/> — never log the password.
/// </summary>
public sealed class ResolveCredentialsStageTests
{
    [Fact]
    public async Task Own_WithStoredCredential_AppliesToContext()
    {
        var connection = new ConnectionModel
        {
            Name = "Test",
            Hostname = "server.local",
            CredentialMode = CredentialMode.Own
        };
        var creds = Substitute.For<ICredentialService>();
        creds.GetForConnection(connection).Returns(new NetworkCredential("alice", "SecretSquirrel123!", "CORP"));
        var store = Substitute.For<IConnectionStore>();
        var bus = Substitute.For<IEventBus>();
        var stage = new ResolveCredentialsStage(creds, store, bus, NullLogger<ResolveCredentialsStage>.Instance);
        var ctx = new ConnectionContext { Connection = connection };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Connection.Username.Should().Be("alice");
        ctx.Connection.Domain.Should().Be("CORP");
        ctx.ResolvedPassword.Should().Be("SecretSquirrel123!");
    }

    [Fact]
    public async Task Own_WithoutStoredCredential_PublishesCredentialRequestedEvent_AndReturnsFailure()
    {
        var connection = new ConnectionModel { Hostname = "h", CredentialMode = CredentialMode.Own };
        var creds = Substitute.For<ICredentialService>();
        creds.GetForConnection(connection).Returns((NetworkCredential?)null);
        var bus = Substitute.For<IEventBus>();
        var stage = new ResolveCredentialsStage(creds, Substitute.For<IConnectionStore>(), bus, NullLogger<ResolveCredentialsStage>.Instance);
        var ctx = new ConnectionContext { Connection = connection };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        bus.Received().Publish(Arg.Any<CredentialRequestedEvent>());
    }

    [Fact]
    public async Task Inherit_ResolvesViaCredentialService_ResolveInherited()
    {
        var connection = new ConnectionModel { Hostname = "h", CredentialMode = CredentialMode.Inherit };
        var store = Substitute.For<IConnectionStore>();
        var creds = Substitute.For<ICredentialService>();
        creds.ResolveInherited(connection, store).Returns(new NetworkCredential("svc", "pass", null));
        var stage = new ResolveCredentialsStage(creds, store, Substitute.For<IEventBus>(), NullLogger<ResolveCredentialsStage>.Instance);
        var ctx = new ConnectionContext { Connection = connection };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Connection.Username.Should().Be("svc");
        ctx.ResolvedPassword.Should().Be("pass");
    }

    [Fact]
    public async Task Inherit_NoMatch_PublishesRequestedEvent_AndReturnsFailure()
    {
        var connection = new ConnectionModel { Hostname = "h", CredentialMode = CredentialMode.Inherit };
        var store = Substitute.For<IConnectionStore>();
        var creds = Substitute.For<ICredentialService>();
        creds.ResolveInherited(connection, store).Returns((NetworkCredential?)null);
        var bus = Substitute.For<IEventBus>();
        var stage = new ResolveCredentialsStage(creds, store, bus, NullLogger<ResolveCredentialsStage>.Instance);
        var ctx = new ConnectionContext { Connection = connection };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        bus.Received().Publish(Arg.Any<CredentialRequestedEvent>());
    }

    [Fact]
    public async Task Prompt_PublishesRequestedEvent_AndReturnsFailure()
    {
        var connection = new ConnectionModel { Hostname = "h", CredentialMode = CredentialMode.Prompt };
        var bus = Substitute.For<IEventBus>();
        var stage = new ResolveCredentialsStage(
            Substitute.For<ICredentialService>(),
            Substitute.For<IConnectionStore>(),
            bus,
            NullLogger<ResolveCredentialsStage>.Instance);
        var ctx = new ConnectionContext { Connection = connection };

        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        bus.Received().Publish(Arg.Any<CredentialRequestedEvent>());
    }

    [Fact]
    public void StageOrderIs100()
    {
        var stage = new ResolveCredentialsStage(
            Substitute.For<ICredentialService>(),
            Substitute.For<IConnectionStore>(),
            Substitute.For<IEventBus>(),
            NullLogger<ResolveCredentialsStage>.Instance);

        stage.Order.Should().Be(100);
        stage.Name.Should().Be("ResolveCredentials");
    }
}
