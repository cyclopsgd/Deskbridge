using System.Net;
using System.Text.Json;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace Deskbridge.Tests.Security;

/// <summary>
/// Regression tests for T-04-LOG and T-04-JSN — password must never appear in Serilog
/// output or in <see cref="ConnectionModel"/>/<see cref="ConnectionContext"/> JSON serialization.
/// </summary>
public sealed class PasswordLeakTests
{
    private const string TestPassword = "SecretSquirrel123!";

    [Fact]
    public async Task NotInLogs()
    {
        var sink = new InMemorySink();
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
        using var loggerFactory = new SerilogLoggerFactory(serilog);

        var connection = new ConnectionModel
        {
            Name = "test",
            Hostname = "server.local",
            CredentialMode = CredentialMode.Own
        };
        var creds = Substitute.For<ICredentialService>();
        creds.GetForConnection(connection).Returns(new NetworkCredential("alice", TestPassword, "CORP"));
        var store = Substitute.For<IConnectionStore>();
        var bus = Substitute.For<IEventBus>();

        var stage = new ResolveCredentialsStage(
            creds, store, bus, loggerFactory.CreateLogger<ResolveCredentialsStage>());

        var ctx = new ConnectionContext { Connection = connection };
        var result = await stage.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.ResolvedPassword.Should().Be(TestPassword);
        sink.ContainsText(TestPassword).Should().BeFalse("password must not leak into Serilog output");

        serilog.Dispose();
    }

    [Fact]
    public void NotInJson()
    {
        var connection = new ConnectionModel
        {
            Name = "test",
            Hostname = "server.local",
            Username = "alice",
            Domain = "CORP",
        };

        var modelJson = JsonSerializer.Serialize(connection);
        modelJson.Should().NotContain("password", "ConnectionModel must have no password-like field");
        modelJson.Should().NotContain("credential");
        modelJson.Should().NotContain("Credential");
        modelJson.Should().NotContain("Password");

        // ConnectionContext.ResolvedPassword must be [JsonIgnore]'d or otherwise not serialized
        var ctx = new ConnectionContext
        {
            Connection = connection,
            ResolvedPassword = TestPassword,
        };
        var ctxJson = JsonSerializer.Serialize(ctx);
        ctxJson.Should().NotContain(TestPassword,
            "ResolvedPassword must not serialize (add [JsonIgnore] if needed)");
    }
}
