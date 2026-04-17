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
        // Always set a non-zero desktop size. AxMsRdpClient9 defaults to 0x0 which renders a black
        // viewport even when the session is otherwise live (mouse events flow, but no display). Use
        // explicit model dimensions if provided; otherwise 1920x1080 as a safe default — SmartSizing=true
        // scales to actual viewport at render time.
        rdp.DesktopWidth = c.DisplaySettings?.Width is > 0 ? c.DisplaySettings.Width.Value : 1920;
        rdp.DesktopHeight = c.DisplaySettings?.Height is > 0 ? c.DisplaySettings.Height.Value : 1080;
        rdp.ColorDepth = 32;

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
}
