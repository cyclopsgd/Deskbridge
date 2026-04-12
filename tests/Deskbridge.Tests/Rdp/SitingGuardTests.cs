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
///
/// <para>
/// <b>STA enforcement</b>: xUnit v3 3.2.2 worker threads default to MTA on Windows and the
/// package exposes no STA attribute. Each test body is wrapped in
/// <see cref="StaRunner.Run(System.Action)"/>, which spawns a fresh STA thread with a pumped
/// <see cref="System.Windows.Threading.Dispatcher"/>. This is the pump that
/// <see cref="WindowsFormsHost"/> and WPF <see cref="Window"/> instances require;
/// without it, a bare <c>Thread.Start</c> deadlocks on the visual tree. See
/// <see cref="StaCollectionFixture"/> for the full rationale and the <c>RDP-STA</c>
/// collection declaration.
/// </para>
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
        _ = _fixture; // Ensure fixture lifetime is observed (collection-scoped teardown).

        StaRunner.Run(() =>
        {
            // Arrange: FakeAxHost forces Handle == IntPtr.Zero by swapping Control._window for
            // a fresh NativeWindow and no-op'ing CreateHandle(). Under WPF 10 / .NET 10 the real
            // AxMsRdpClient9NotSafeForScripting realizes a handle even on an unrooted Grid (the
            // StaRunner Dispatcher pump is enough), so a stub is the only way to cover the
            // "handle stays zero" branch of the guard. See FakeAxHost.cs for the rationale and
            // Plan 04-01 Task 0.1 line 166 for the approved approach.
            var viewport = new Grid();
            var host = new WindowsFormsHost();
            var rdp = new FakeAxHost();

            // Act + Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                AxSiting.SiteAndConfigure(viewport, host, rdp, _ => { /* should not execute */ }));

            Assert.Contains("not sited", ex.Message);

            // Cleanup — do not let the AxHost linger rooted in the test host process.
            try { host.Child = null; } catch { }
            try { rdp.Dispose(); } catch { }
            try { host.Dispose(); } catch { }
        });
    }

    [Fact]
    public void DoesNotThrow_When_HandleIsNonZero_AndInvokesConfigureOnce()
    {
        _ = _fixture;

        StaRunner.Run(() =>
        {
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
        });
    }
}
