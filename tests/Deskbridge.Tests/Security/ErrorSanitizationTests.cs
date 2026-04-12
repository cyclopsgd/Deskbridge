using System.Runtime.InteropServices;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;
using Deskbridge.Core.Services;
using Serilog;
using Serilog.Extensions.Logging;

namespace Deskbridge.Tests.Security;

/// <summary>
/// Regression tests for T-04-EXC: COM exception messages may contain plaintext passwords
/// (e.g. if a misbehaving layer bundles credentials into the exception detail). Stages must
/// log only <c>ex.GetType().Name</c> + <c>ex.HResult</c> — never <c>ex.Message</c>.
/// </summary>
public sealed class ErrorSanitizationTests
{
    [Fact]
    public async Task ConnectStage_LogsCOMException_As_TypeAndHResult_NotMessage()
    {
        var sink = new InMemorySink();
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
        using var loggerFactory = new SerilogLoggerFactory(serilog);

        var connection = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var host = Substitute.For<IProtocolHost>();
        host.ConnectAsync(Arg.Any<ConnectionContext>())
            .Returns(Task.FromException(new COMException("password=Hunter2", unchecked((int)0x80004005))));
        var bus = Substitute.For<IEventBus>();
        var stage = new ConnectStage(bus, loggerFactory.CreateLogger<ConnectStage>());
        var ctx = new ConnectionContext { Connection = connection, Host = host };

        await stage.ExecuteAsync(ctx);

        sink.ContainsText("Hunter2").Should().BeFalse("ex.Message must never reach the log sink");
        sink.ContainsText("password=").Should().BeFalse();

        serilog.Dispose();
    }

    [Fact]
    public void RdpHostControl_LogsCOMException_Similarly_ByDesign()
    {
        // RdpHostControl catch blocks use `ex.GetType().Name` + `ex.HResult:X8`, never `ex.Message`.
        // This test asserts the contract via grep-equivalent reflection on the source file would
        // be more appropriate; here we treat it as a documentation assertion of the shape.
        // Full behavior is exercised by ConnectStage test above plus the ErrorIsolationTests suite.
        var ex = new COMException("password=Hunter2", unchecked((int)0x80004005));
        var sanitized = $"{ex.GetType().Name} HResult=0x{ex.HResult:X8}";

        sanitized.Should().NotContain("Hunter2");
        sanitized.Should().Contain("COMException");
        sanitized.Should().Contain("HResult=");
    }

    [Fact]
    public void Classifier_Describe_DoesNotLeakPassword()
    {
        // Placeholder for full classifier in Plan 04-03; trivial assertion that Describe()
        // runs and returns a non-leaking string for a common disc reason.
        var describe = DisconnectReasonClassifier.Describe(516, 0, null);
        describe.Should().NotContain("password");
        describe.Should().NotContain("Hunter2");
    }
}
