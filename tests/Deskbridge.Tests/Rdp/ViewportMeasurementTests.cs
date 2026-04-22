using Deskbridge.Protocols.Rdp;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// Unit tests for <see cref="ViewportMeasurement"/> helper methods. Pure math — no WPF
/// PresentationSource required. Tests ClampDesktopDimension and GetDpiPercent.
/// </summary>
public sealed class ViewportMeasurementTests
{
    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 200)]
    [InlineData(199, 200)]
    public void ClampDesktopDimension_ClampsToMinimum(int input, int expected)
    {
        ViewportMeasurement.ClampDesktopDimension(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(9000, 8192)]
    [InlineData(10000, 8192)]
    public void ClampDesktopDimension_ClampsToMaximum(int input, int expected)
    {
        ViewportMeasurement.ClampDesktopDimension(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(1920)]
    [InlineData(3840)]
    [InlineData(8192)]
    public void ClampDesktopDimension_PassesThroughValidValues(int input)
    {
        ViewportMeasurement.ClampDesktopDimension(input).Should().Be(input);
    }

    [Theory]
    [InlineData(1.0, 100u)]
    [InlineData(1.25, 125u)]
    [InlineData(1.5, 150u)]
    [InlineData(2.0, 200u)]
    public void GetDpiPercent_ConvertsCorrectly(double scaleFactor, uint expected)
    {
        ViewportMeasurement.GetDpiPercent(scaleFactor).Should().Be(expected);
    }
}
