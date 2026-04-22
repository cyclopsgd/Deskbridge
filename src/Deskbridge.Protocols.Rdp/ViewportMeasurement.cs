using System.Windows;
using System.Windows.Media;

namespace Deskbridge.Protocols.Rdp;

/// <summary>
/// Static helpers for measuring the RDP viewport's physical pixel dimensions and
/// calculating DPI scale factors for <c>IMsRdpExtendedSettings</c>. All pixel math
/// accounts for the WPF DIP-to-physical-pixel transform so the remote session
/// resolution matches the actual viewport size on the current monitor.
///
/// <para><b>STAB-03:</b> Eliminates StretchBlt blur by providing exact pixel dimensions
/// to <see cref="RdpConnectionConfigurator"/>.</para>
/// <para><b>STAB-04:</b> <see cref="ClampDesktopDimension"/> enforces the RDP-valid
/// range [200, 8192] — prevents 0x0 black viewport and out-of-range COM errors.</para>
///
/// <para>Thread safety: all methods are pure functions with no shared state.</para>
/// </summary>
public static class ViewportMeasurement
{
    /// <summary>Minimum desktop dimension accepted by AxMsRdpClient9.</summary>
    private const int MinDimension = 200;

    /// <summary>Maximum desktop dimension accepted by AxMsRdpClient9.</summary>
    private const int MaxDimension = 8192;

    /// <summary>
    /// Converts a <see cref="FrameworkElement"/>'s DIP dimensions to physical pixels
    /// using the element's <see cref="PresentationSource"/>. Falls back to raw
    /// <c>ActualWidth</c>/<c>ActualHeight</c> if the element is not yet part of a
    /// visual tree (guard for early <c>SizeChanged</c> events — 16-RESEARCH Pitfall 2).
    /// Both dimensions are clamped to the RDP-valid range [200, 8192].
    /// </summary>
    public static (int Width, int Height) GetPhysicalPixelSize(FrameworkElement element)
    {
        var source = PresentationSource.FromVisual(element);
        int pixelWidth, pixelHeight;

        if (source?.CompositionTarget is { } ct)
        {
            var transform = ct.TransformToDevice;
            pixelWidth = (int)Math.Round(element.ActualWidth * transform.M11);
            pixelHeight = (int)Math.Round(element.ActualHeight * transform.M22);
        }
        else
        {
            // PresentationSource not yet available — use raw DIP values as best effort.
            pixelWidth = (int)element.ActualWidth;
            pixelHeight = (int)element.ActualHeight;
        }

        return (ClampDesktopDimension(pixelWidth), ClampDesktopDimension(pixelHeight));
    }

    /// <summary>
    /// Converts the <c>M11</c> or <c>M22</c> value from
    /// <see cref="Visual.TransformToDevice"/> to a DPI percentage (e.g. 1.25 -> 125).
    /// </summary>
    public static uint GetDpiPercent(double transformScaleFactor)
    {
        return (uint)Math.Round(transformScaleFactor * 100);
    }

    /// <summary>
    /// Maps a DPI percentage to the <c>(DesktopScaleFactor, DeviceScaleFactor)</c> pair
    /// required by <c>IMsRdpExtendedSettings</c>.
    ///
    /// <para><b>DesktopScaleFactor:</b> Snaps to the nearest valid RDP value
    /// (100, 125, 150, 175, 200) or passes through up to 500 max.</para>
    /// <para><b>DeviceScaleFactor:</b> Only three valid values — 100 (<=100% DPI),
    /// 140 (101-199% DPI), 180 (>=200% DPI).</para>
    ///
    /// [CITED: learn.microsoft.com/en-us/windows/win32/termserv/imsrdpextendedsettings-property]
    /// [CITED: devolutions.net/blog/smart-resizing-and-high-dpi-issues-in-remote-desktop-manager/]
    /// </summary>
    public static (uint DesktopScale, uint DeviceScale) GetScaleFactors(double dpiPercent)
    {
        uint desktopScale = dpiPercent switch
        {
            <= 100 => 100,
            <= 125 => 125,
            <= 150 => 150,
            <= 175 => 175,
            <= 200 => 200,
            _ => Math.Min((uint)dpiPercent, 500)
        };

        uint deviceScale = desktopScale switch
        {
            <= 100 => 100,
            <= 199 => 140,
            _ => 180
        };

        return (desktopScale, deviceScale);
    }

    /// <summary>
    /// Clamps a pixel dimension to the RDP-valid range [200, 8192].
    /// <para><b>T-16-01:</b> Prevents out-of-range values from reaching the COM
    /// ActiveX control's <c>DesktopWidth</c>/<c>DesktopHeight</c> properties.</para>
    /// </summary>
    public static int ClampDesktopDimension(int value)
    {
        return Math.Clamp(value, MinDimension, MaxDimension);
    }
}
