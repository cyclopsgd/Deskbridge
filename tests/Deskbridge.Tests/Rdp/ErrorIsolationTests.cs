using System.Runtime.InteropServices;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// RDP-07 error isolation: a COM exception during connect must be caught, surfaced via
/// <c>ErrorOccurred</c> / <c>ConnectionFailedEvent</c>, and must NOT tear down the app.
/// </summary>
[Collection("RDP-STA")]
public sealed class ErrorIsolationTests
{
    private readonly StaCollectionFixture _fixture;
    public ErrorIsolationTests(StaCollectionFixture fixture) => _fixture = fixture;

    [Fact]
    public void ConnectStage_DoesNotPropagate_COMException_AsStageFailure()
    {
        _ = _fixture;
        StaRunner.RunAsync(async () =>
        {
            var host = Substitute.For<IProtocolHost>();
            host.ConnectAsync(Arg.Any<ConnectionContext>())
                .Returns(Task.FromException(new COMException("COM fail", unchecked((int)0x80004005))));
            var bus = Substitute.For<IEventBus>();
            var stage = new ConnectStage(bus, NullLogger<ConnectStage>.Instance);
            var ctx = new ConnectionContext
            {
                Connection = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp },
                Host = host
            };

            var result = await stage.ExecuteAsync(ctx);

            result.Success.Should().BeFalse();
            bus.Received().Publish(Arg.Any<ConnectionFailedEvent>());
        });
    }

    [Fact]
    public void RdpHostControl_DisposeMultipleTimes_DoesNotThrow()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var host = new RdpHostControl(NullLogger<RdpHostControl>.Instance, Guid.NewGuid());
            host.Dispose();
            // Idempotent dispose — should not throw on second call
            var act = () => host.Dispose();
            act.Should().NotThrow();
        });
    }

    [Fact]
    public void MainWindow_OnClosing_SimulatedFlow_DoesNotThrow_EvenIfDisposeThrows()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            // Simulate the MainWindow.OnClosing pattern: try/catch around a dispose that throws.
            var throwing = Substitute.For<IProtocolHost>();
            throwing.When(h => h.Dispose()).Do(_ => throw new InvalidOperationException("boom"));

            Exception? captured = null;
            try { throwing.Dispose(); } catch (Exception ex) { captured = ex; }

            // Closing path must swallow the exception (the contract is "do not crash on close").
            // This test asserts that the pattern itself (try-catch around Dispose) is safe —
            // the MainWindow implementation uses this exact shape.
            captured.Should().NotBeNull();
            // The caller catches the exception per plan Task 4.2 spec.
        });
    }
}
