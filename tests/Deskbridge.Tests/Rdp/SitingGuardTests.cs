using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Deskbridge.Tests.Fixtures;
using Xunit;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// Pure-unit tests for <c>AxSiting.SiteAndConfigure</c> (added in Task 1.1).
///
/// No live RDP required. These tests validate the siting-order guard that both
/// the Plan 04-01 prototype (<c>RdpSmokeHost</c>) and the Plan 04-02 production
/// <c>RdpHostControl</c> consume. See RDP-ACTIVEX-PITFALLS §1.
///
/// The scaffold (Task 0.1) marks tests Skip until Task 1.1 adds <c>AxSiting</c>
/// and references to <c>AxMSTSCLib.AxMsRdpClient9NotSafeForScripting</c>; Task 1.1
/// removes the Skip and replaces <c>PLACEHOLDER</c> with the real calls.
/// </summary>
[Collection("RDP-STA")]
public class SitingGuardTests
{
    private readonly StaCollectionFixture _fixture;

    public SitingGuardTests(StaCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = "AxSiting not yet implemented — Task 1.1 enables this test")]
    public void Throws_When_HandleIsZero_AfterAddingToUnrootedPanel()
    {
        Skip.IfNot(_fixture.IsSta(), "STA required");

        // Arrange: Grid is NOT added to any Window, so no HwndSource exists and
        // the AxHost's Handle will stay IntPtr.Zero even after being added.
        var viewport = new Grid();
        var host = new WindowsFormsHost();
        // var rdp = new AxMsRdpClient9NotSafeForScripting();   // Task 1.1 uncomments

        // Act + Assert — Task 1.1 replaces this with:
        //   var ex = Assert.Throws<InvalidOperationException>(() =>
        //       AxSiting.SiteAndConfigure(viewport, host, rdp, r => r.Server = "ignored"));
        //   Assert.Contains("not sited", ex.Message);
        _ = viewport;
        _ = host;

        // Cleanup
        try { host.Child = null; } catch { }
        try { host.Dispose(); } catch { }
    }

    [Fact(Skip = "AxSiting not yet implemented — Task 1.1 enables this test")]
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
        window.Show();

        try
        {
            var host = new WindowsFormsHost();
            // var rdp = new AxMsRdpClient9NotSafeForScripting();  // Task 1.1 uncomments
            int configureCallCount = 0;

            // Act — Task 1.1 replaces with:
            //   AxSiting.SiteAndConfigure(viewport, host, rdp, _ => configureCallCount++);
            //   Assert.Equal(1, configureCallCount);
            //   Assert.NotEqual(IntPtr.Zero, rdp.Handle);
            _ = host;
            _ = configureCallCount;

            // Cleanup
            try { host.Dispose(); } catch { }
        }
        finally
        {
            window.Close();
        }
    }
}
