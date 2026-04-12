using AxMSTSCLib;
using Deskbridge.Core.Pipeline;

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

        rdp.AdvancedSettings9.SmartSizing = c.DisplaySettings?.SmartSizing ?? true;
        if (c.DisplaySettings?.Width is > 0) rdp.DesktopWidth = c.DisplaySettings.Width.Value;
        if (c.DisplaySettings?.Height is > 0) rdp.DesktopHeight = c.DisplaySettings.Height.Value;
        rdp.ColorDepth = 32;

        rdp.AdvancedSettings9.EnableCredSspSupport = true;
        rdp.AdvancedSettings9.CachePersistenceActive = 0;
        // NOTE: "BitmapPeristence" (no 's') — COM typelib misspelling; MUST match exactly.
        rdp.AdvancedSettings9.BitmapPeristence = 0;
        rdp.SecuredSettings3.KeyboardHookMode = 0;
        rdp.AdvancedSettings9.GrabFocusOnConnect = false;
        rdp.AdvancedSettings9.EnableAutoReconnect = false;
        rdp.AdvancedSettings9.ContainerHandledFullScreen = 0;
    }
}
