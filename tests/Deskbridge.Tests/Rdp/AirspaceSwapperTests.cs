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
}
