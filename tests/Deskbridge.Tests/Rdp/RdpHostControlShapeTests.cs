using System.Windows.Forms.Integration;
using Deskbridge.Core.Interfaces;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// Shape tests for <see cref="RdpHostControl"/>. No live RDP required — these verify
/// the interface contract, STA guard, and disposed-state behavior.
/// </summary>
[Collection("RDP-STA")]
public sealed class RdpHostControlShapeTests
{
    private readonly StaCollectionFixture _fixture;
    public RdpHostControlShapeTests(StaCollectionFixture fixture) => _fixture = fixture;

    [Fact]
    public void Implements_IProtocolHost()
    {
        _ = _fixture;
        typeof(IProtocolHost).IsAssignableFrom(typeof(RdpHostControl))
            .Should().BeTrue();
    }

    [Fact]
    public void Constructor_ThrowsInvalidOperationException_OnMtaThread()
    {
        _ = _fixture;
        // This test is NOT wrapped in StaRunner — we want MTA behavior.
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { _ = new RdpHostControl(NullLogger<RdpHostControl>.Instance); }
            catch (Exception ex) { captured = ex; }
        });
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();

        captured.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void IsConnected_ReturnsFalse_BeforeConnect()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var host = new RdpHostControl(NullLogger<RdpHostControl>.Instance);
            try
            {
                host.IsConnected.Should().BeFalse();
            }
            finally
            {
                host.Dispose();
            }
        });
    }

    [Fact]
    public void Host_ThrowsObjectDisposedException_AfterDispose()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var host = new RdpHostControl(NullLogger<RdpHostControl>.Instance);
            host.Dispose();
            Assert.Throws<ObjectDisposedException>(() => { var _ = host.Host; });
        });
    }

    [Fact]
    public void Constructor_WiresAxHostAsWindowsFormsHostChild()
    {
        _ = _fixture;
        // Regression: before this wire-up, MainWindow adding rdp.Host to the visual tree
        // left the AxHost orphaned — Handle stayed 0 and ConnectStage threw "not sited".
        // RDP-ACTIVEX-PITFALLS §1 requires host.Child = rdp BEFORE the WFH is parented.
        StaRunner.Run(() =>
        {
            var host = new RdpHostControl(NullLogger<RdpHostControl>.Instance);
            try
            {
                host.Host.Child.Should().NotBeNull(
                    "RdpHostControl ctor must wire the AxHost as WindowsFormsHost.Child so " +
                    "the AxHost sites when the WFH is added to the visual tree.");
            }
            finally
            {
                host.Dispose();
            }
        });
    }
}
