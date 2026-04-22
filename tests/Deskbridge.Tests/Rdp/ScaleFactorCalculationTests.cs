using Deskbridge.Protocols.Rdp;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// Unit tests for <see cref="ViewportMeasurement.GetScaleFactors"/>. Pure math — no WPF
/// or COM dependencies. Validates the DPI-to-scale-factor mapping from 16-RESEARCH.md
/// Pattern 2 and Microsoft Learn IMsRdpExtendedSettings documentation.
/// </summary>
public sealed class ScaleFactorCalculationTests
{
    [Theory]
    [InlineData(100, 100u, 100u)]
    [InlineData(125, 125u, 140u)]
    [InlineData(150, 150u, 140u)]
    [InlineData(175, 175u, 140u)]
    [InlineData(200, 200u, 180u)]
    [InlineData(250, 250u, 180u)]
    public void GetScaleFactors_MapsCorrectly(double dpiPercent, uint expectedDesktop, uint expectedDevice)
    {
        var (desktopScale, deviceScale) = ViewportMeasurement.GetScaleFactors(dpiPercent);

        desktopScale.Should().Be(expectedDesktop);
        deviceScale.Should().Be(expectedDevice);
    }

    [Fact]
    public void GetScaleFactors_ClampsAbove500()
    {
        var (desktopScale, _) = ViewportMeasurement.GetScaleFactors(600);

        desktopScale.Should().Be(500);
    }

    [Fact]
    public void GetScaleFactors_BelowHundred_Returns100_100()
    {
        var (desktopScale, deviceScale) = ViewportMeasurement.GetScaleFactors(75);

        desktopScale.Should().Be(100);
        deviceScale.Should().Be(100);
    }
}
