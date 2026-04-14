using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Tests.Fixtures;

// Disambiguate from System.Drawing.Image (transitively pulled via WinForms references)
using Image = System.Windows.Controls.Image;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// Tests for <see cref="AirspaceSwapper"/>. Runs on STA to instantiate WPF <see cref="Window"/>.
/// </summary>
[Collection("RDP-STA")]
public sealed class AirspaceSwapperTests
{
    private readonly StaCollectionFixture _fixture;
    public AirspaceSwapperTests(StaCollectionFixture fixture) => _fixture = fixture;

    [Fact]
    public void AttachToWindow_DoesNotThrow_AndIsIdempotent()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var window = new Window
            {
                Width = 10,
                Height = 10,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Visibility = Visibility.Hidden,
            };
            window.Show();
            try
            {
                swapper.AttachToWindow(window);
                // Calling twice must not throw — idempotent
                swapper.AttachToWindow(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void RegisterHost_ThenUnregister_DoesNotThrow()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var window = new Window
            {
                Width = 10, Height = 10,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Visibility = Visibility.Hidden,
            };
            window.Show();
            try
            {
                swapper.AttachToWindow(window);
                var host = new WindowsFormsHost();
                var overlay = new Image();
                swapper.RegisterHost(host, overlay);
                swapper.UnregisterHost(host);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HideWithoutSnapshot_Returns_DisposableToken_ThatRestoresVisibility()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var host = new WindowsFormsHost { Visibility = Visibility.Visible };

            var token = swapper.HideWithoutSnapshot(host);
            host.Visibility.Should().Be(Visibility.Collapsed);

            token.Dispose();
            host.Visibility.Should().Be(Visibility.Visible);
        });
    }

    /// <summary>
    /// WR-06 (Phase 5 / Pattern 4) regression guard. In the multi-host tab model
    /// background tabs are Collapsed on tab switch; on WM_ENTERSIZEMOVE we snapshot
    /// each host's pre-drag visibility and on WM_EXITSIZEMOVE we restore per-host
    /// (NOT unconditional Visible). This test registers two hosts, one Visible
    /// (active tab) and one Collapsed (background tab), drives the WndProc through
    /// ENTER then EXIT, and asserts the active tab returns to Visible while the
    /// background tab stays Collapsed.
    /// </summary>
    [Fact]
    public void MultiHost_ExitSizeMove_RestoresPreDragVisibility()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var swapper = new AirspaceSwapper();
            var window = new Window
            {
                Width = 100, Height = 100,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Visibility = Visibility.Hidden,
            };
            window.Show();
            try
            {
                swapper.AttachToWindow(window);

                var hostA = new WindowsFormsHost { Visibility = Visibility.Visible };
                var hostB = new WindowsFormsHost { Visibility = Visibility.Collapsed };
                var overlayA = new Image();
                var overlayB = new Image();
                swapper.RegisterHost(hostA, overlayA);
                swapper.RegisterHost(hostB, overlayB);

                InvokeWndProc(swapper, 0x0231); // WM_ENTERSIZEMOVE
                hostA.Visibility.Should().Be(Visibility.Collapsed,
                    "WM_ENTERSIZEMOVE collapses every host while the snapshot overlay is visible");
                hostB.Visibility.Should().Be(Visibility.Collapsed,
                    "host B was already Collapsed before the drag");

                InvokeWndProc(swapper, 0x0232); // WM_EXITSIZEMOVE
                hostA.Visibility.Should().Be(Visibility.Visible,
                    "WM_EXITSIZEMOVE restores host A to its pre-drag Visibility (Visible)");
                hostB.Visibility.Should().Be(Visibility.Collapsed,
                    "WM_EXITSIZEMOVE restores host B to its pre-drag Visibility (Collapsed) — NOT unconditional Visible");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Invokes the private <c>WndProc</c> on the swapper directly. We bypass the
    /// HwndSource hook because simulating a real drag-resize in a hidden unit-test
    /// window is flaky (Window.Hidden suppresses the WM_ENTERSIZEMOVE pump). The
    /// contract under test is the per-host visibility restore logic inside WndProc,
    /// which doesn't care whether it was called via the hook or reflection.
    /// </summary>
    private static void InvokeWndProc(AirspaceSwapper sut, int msg)
    {
        var method = typeof(AirspaceSwapper).GetMethod(
            "WndProc",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AirspaceSwapper.WndProc not found via reflection");

        var handled = false;
        var args = new object?[] { IntPtr.Zero, msg, IntPtr.Zero, IntPtr.Zero, handled };
        method.Invoke(sut, args);
    }
}
