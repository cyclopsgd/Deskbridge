using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using AxMSTSCLib;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Tests.Fixtures;
using Xunit;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// Pure-unit tests for <see cref="AxSiting.SiteAndConfigure"/>.
///
/// No live RDP required. These tests validate the siting-order guard that both
/// the Plan 04-01 prototype (<c>RdpSmokeHost</c>) and the Plan 04-02 production
/// <c>RdpHostControl</c> consume. See RDP-ACTIVEX-PITFALLS §1.
/// </summary>
[Collection("RDP-STA")]
public class SitingGuardTests
{
    private readonly StaCollectionFixture _fixture;

    public SitingGuardTests(StaCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Throws_When_HandleIsZero_AfterAddingToUnrootedPanel()
    {
        Skip.IfNot(_fixture.IsSta(), "STA required");

        // Arrange: Grid is NOT added to any Window, so no HwndSource exists and
        // the AxHost's Handle will stay IntPtr.Zero even after being added.
        var viewport = new Grid();
        var host = new WindowsFormsHost();
        var rdp = new AxMsRdpClient9NotSafeForScripting();

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AxSiting.SiteAndConfigure(viewport, host, rdp, r => r.Server = "ignored"));

        Assert.Contains("not sited", ex.Message);

        // Cleanup — do not let the AxHost linger rooted in the test host process.
        try { host.Child = null; } catch { }
        try { rdp.Dispose(); } catch { }
        try { host.Dispose(); } catch { }
    }

    [Fact]
    public void DoesNotThrow_When_HandleIsNonZero_AndInvokesConfigureOnce()
    {
        Skip.IfNot(_fixture.IsSta(), "STA required");

        // Arrange: a hidden Window gives the Grid a real HwndSource, which lets the
        // WindowsFormsHost create its handle — AxHost.Handle will be non-zero.
        var window = new Window
        {
            Width = 10,
            Height = 10,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden,
        };
        var viewport = new Grid();
        window.Content = viewport;

        // Realize the window without displaying it so the HwndSource is created.
        window.Show();

        try
        {
            var host = new WindowsFormsHost();
            var rdp = new AxMsRdpClient9NotSafeForScripting();
            int configureCallCount = 0;

            // Act
            AxSiting.SiteAndConfigure(viewport, host, rdp, _ => configureCallCount++);

            // Assert
            Assert.Equal(1, configureCallCount);
            Assert.NotEqual(IntPtr.Zero, rdp.Handle);

            // Cleanup
            try { viewport.Children.Remove(host); } catch { }
            try { host.Child = null; } catch { }
            try { rdp.Dispose(); } catch { }
            try { host.Dispose(); } catch { }
        }
        finally
        {
            window.Close();
        }
    }
}
