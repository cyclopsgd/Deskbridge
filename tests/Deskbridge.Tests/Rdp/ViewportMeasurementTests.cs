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

    /// <summary>
    /// Quick task 260428-sdu: RDP requires DesktopWidth/Height to be a multiple of 8.
    /// Today's bug: 924/716 hit mstscax.dll discReason=1 (DR_Local) handshake abort.
    /// Aligning DOWN to the nearest multiple of 8 inside ClampDesktopDimension fixes
    /// both the initial-connect path (RdpConnectionConfigurator.Apply) and the dynamic
    /// resize path (RdpHostControl.UpdateResolution) with a single change.
    /// </summary>
    [Theory]
    [InlineData(924, 920)]    // today's repro width (924 % 8 == 4)
    [InlineData(716, 712)]    // today's repro height (716 % 8 == 4)
    [InlineData(1366, 1360)]  // work-laptop typical width (1366 % 8 == 6)
    [InlineData(1920, 1920)]  // already aligned
    [InlineData(1024, 1024)]  // already aligned
    [InlineData(1280, 1280)]  // already aligned
    [InlineData(3840, 3840)]  // already aligned
    [InlineData(8189, 8184)]  // just below max, align-down case
    [InlineData(8192, 8192)]  // max boundary, already aligned
    public void ClampDesktopDimension_AlignsDownToMultipleOf8(int input, int expected)
    {
        ViewportMeasurement.ClampDesktopDimension(input).Should().Be(expected);
    }

    /// <summary>
    /// 200 is itself a multiple of 8 (200 = 25 * 8), so the min-clamp behaviour is
    /// preserved bit-for-bit after the alignment fix lands. This theory acts as a
    /// regression guard alongside <see cref="ClampDesktopDimension_ClampsToMinimum"/>.
    /// </summary>
    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 200)]
    [InlineData(199, 200)]
    public void ClampDesktopDimension_MinClampLandsOnAlignedValue(int input, int expected)
    {
        ViewportMeasurement.ClampDesktopDimension(input).Should().Be(expected);
        (expected % 8).Should().Be(0, "200 is already a multiple of 8 so alignment is a no-op at the floor");
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
