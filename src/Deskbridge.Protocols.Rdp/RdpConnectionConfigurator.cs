using System.Runtime.InteropServices;
using AxMSTSCLib;
using Deskbridge.Core.Pipeline;
using Deskbridge.Protocols.Rdp.Interop;

namespace Deskbridge.Protocols.Rdp;

/// <summary>
/// Copies connection model + display settings into the AxMsRdpClient9 property set,
/// applying Phase 4 defaults from 04-RESEARCH.md Pattern 4 and 04-CONTEXT.md Specifics.
///
/// <para><b>Phase 4 defaults (research-verified, D-04 etc.):</b>
/// SmartSizing=true, EnableCredSspSupport=true, CachePersistenceActive=0 (GDI mitigation),
/// BitmapPeristence=0 (COM typelib misspelling — do NOT "fix"), KeyboardHookMode=0,
/// GrabFocusOnConnect=false, EnableAutoReconnect=false (D-04 we own reconnect),
/// ContainerHandledFullScreen=0 (Phase 6 CMD-04 toggles this).</para>
///
/// <para><b>Phase 16 (STAB-03):</b> Prefers viewport-matched resolution from
/// <c>ConnectionContext.Properties["ViewportPixelWidth"]</c>/<c>["ViewportPixelHeight"]</c>
/// over the hardcoded 1920x1080 fallback. Sets <c>SmartSizing=false</c> when viewport-matched
/// for 1:1 pixel mapping (no StretchBlt blur). DPI scale factors are communicated via
/// <see cref="IMsRdpExtendedSettings"/> when <c>Properties["DpiPercent"]</c> is present.</para>
///
/// <para><b>Security:</b> This class MUST NOT set the password. Password is written
/// separately via <c>IMsTscNonScriptable.ClearTextPassword</c> after siting — see
/// <see cref="RdpHostControl.ConnectAsync"/>.</para>
/// </summary>
public static class RdpConnectionConfigurator
{
    public static void Apply(AxMsRdpClient9NotSafeForScripting rdp, ConnectionContext ctx)
    {
        var c = ctx.Connection;
        rdp.Server = c.Hostname;
        rdp.AdvancedSettings9.RDPPort = c.Port;
        rdp.UserName = c.Username ?? "";
        rdp.Domain = c.Domain ?? "";

        // STAB-03: Prefer viewport-matched resolution over hardcoded 1920x1080.
        // MainWindow.OnHostMounted writes these properties after measuring ViewportGrid.
        int desktopW, desktopH;
        bool viewportMatched = false;
        if (ctx.Properties.TryGetValue("ViewportPixelWidth", out var vpw) && vpw is int w && w > 0
            && ctx.Properties.TryGetValue("ViewportPixelHeight", out var vph) && vph is int h && h > 0)
        {
            desktopW = ViewportMeasurement.ClampDesktopDimension(w);
            desktopH = ViewportMeasurement.ClampDesktopDimension(h);
            viewportMatched = true;
        }
        else
        {
            // Fallback: user-specified display settings or safe default
            desktopW = c.DisplaySettings?.Width is > 0 ? c.DisplaySettings.Width.Value : 1920;
            desktopH = c.DisplaySettings?.Height is > 0 ? c.DisplaySettings.Height.Value : 1080;
        }
        rdp.DesktopWidth = desktopW;
        rdp.DesktopHeight = desktopH;

        // SmartSizing=false when viewport-matched (1:1 pixel mapping, no StretchBlt blur).
        // SmartSizing=true only for the fallback path where the desktop resolution may not
        // match the viewport — scaling prevents a letterboxed remote desktop.
        rdp.AdvancedSettings9.SmartSizing = viewportMatched
            ? false
            : (c.DisplaySettings?.SmartSizing ?? true);

        rdp.ColorDepth = 32;

        // DPI scale factors via IMsRdpExtendedSettings (Phase 16, 16-RESEARCH Pattern 3).
        // Communicates the client monitor's DPI to the remote session so text/UI elements
        // render at the correct size. Wrapped in try/catch — older mstscax.dll versions
        // may not support these properties. T-16-02: log type + HResult only, never ex.Message.
        if (ctx.Properties.TryGetValue("DpiPercent", out var dpiObj) && dpiObj is double dpiPct)
        {
            var (desktopScale, deviceScale) = ViewportMeasurement.GetScaleFactors(dpiPct);
            SetExtendedProperty(rdp, "DesktopScaleFactor", desktopScale);
            SetExtendedProperty(rdp, "DeviceScaleFactor", deviceScale);
        }

        // CredSSP / NLA: default true for Windows RDP servers. xrdp and other non-Windows RDP
        // implementations don't support CredSSP; forcing it causes an indefinite stall during
        // negotiation. Exposed on ConnectionModel so users can opt out per-connection.
        rdp.AdvancedSettings9.EnableCredSspSupport = c.EnableCredSspSupport;
        // Server auth level: 0=no auth required (xrdp / self-signed), 1=must auth, 2=warn (default).
        rdp.AdvancedSettings9.AuthenticationLevel = c.AuthenticationLevel;
        rdp.AdvancedSettings9.CachePersistenceActive = 0;
        // NOTE: "BitmapPeristence" (no 's') — COM typelib misspelling; MUST match exactly.
        rdp.AdvancedSettings9.BitmapPeristence = 0;
        rdp.SecuredSettings3.KeyboardHookMode = 0;
        rdp.AdvancedSettings9.GrabFocusOnConnect = false;
        rdp.AdvancedSettings9.EnableAutoReconnect = false;
        rdp.AdvancedSettings9.ContainerHandledFullScreen = 0;
        rdp.AdvancedSettings9.RedirectClipboard = true;
    }

    /// <summary>
    /// Sets an extended property on the RDP control via <see cref="IMsRdpExtendedSettings"/>.
    /// Casts <c>GetOcx()</c> to the manually-declared COM interface. Catches
    /// <see cref="COMException"/> and <see cref="InvalidCastException"/> — logs warning
    /// and continues (connection works without DPI awareness on older mstscax.dll).
    /// <para><b>T-16-02:</b> Logs type + HResult only, never <c>ex.Message</c>.</para>
    /// </summary>
    private static void SetExtendedProperty(AxMsRdpClient9NotSafeForScripting rdp, string name, object value)
    {
        try
        {
            var ocx = rdp.GetOcx();
            if (ocx is IMsRdpExtendedSettings extSettings)
            {
                extSettings.set_Property(name, ref value);
            }
        }
        catch (COMException ex)
        {
            Serilog.Log.Warning(
                "Failed to set extended property {Name}: {ExceptionType} HResult=0x{HResult:X8}",
                name, ex.GetType().Name, ex.HResult);
        }
        catch (InvalidCastException ex)
        {
            Serilog.Log.Warning(
                "Failed to cast GetOcx to IMsRdpExtendedSettings for {Name}: {ExceptionType}",
                name, ex.GetType().Name);
        }
    }
}
