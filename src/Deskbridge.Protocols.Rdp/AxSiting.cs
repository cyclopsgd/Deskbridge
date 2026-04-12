using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

// Use an alias so callers can import this file without pulling in
// System.Windows.Controls.Panel / System.Windows.Forms.Panel ambiguity.
using Panel = System.Windows.Controls.Panel;

namespace Deskbridge.Protocols.Rdp;

/// <summary>
/// Enforces the mandatory "site-before-configure" order for any <see cref="AxHost"/>-derived
/// ActiveX wrapper hosted inside a <see cref="WindowsFormsHost"/>.
///
/// The RDP ActiveX control (<c>AxMsRdpClient9NotSafeForScripting</c>) silently ignores — or
/// worse, throws <c>AxHost.InvalidActiveXStateException</c> deep inside COM — if any property
/// is written before the control has been parented into a visual tree with a real
/// <see cref="System.Windows.Interop.HwndSource"/>. See RDP-ACTIVEX-PITFALLS §1 for the full
/// state-machine and the mRemoteNG issues this pattern avoids (#1715, #1671).
///
/// The helper is used by both the Plan 04-01 prototype (<c>RdpSmokeHost</c>) and the Plan
/// 04-02 production <c>RdpHostControl</c>. Gate 3 of the Plan 04-01 smoke suite verifies the
/// throw path; <c>SitingGuardTests</c> exercises both paths as unit tests.
/// </summary>
public static class AxSiting
{
    /// <summary>
    /// Sites the ActiveX control correctly, then runs <paramref name="configure"/>.
    ///
    /// Order (load-bearing — see RDP-ACTIVEX-PITFALLS §1):
    /// <list type="number">
    ///   <item><description><c>host.Child = rdp</c> — assigns the AxHost to the WFH, which triggers <c>CreateControl()</c> inside the WFH.</description></item>
    ///   <item><description><c>viewport.Children.Add(host)</c> — adds the WFH to the WPF visual tree; if the viewport is already parented to a Window (i.e. an <c>HwndSource</c> exists), this realizes the AxHost's Win32 handle.</description></item>
    ///   <item><description><c>Handle != IntPtr.Zero</c> assertion — throws <see cref="InvalidOperationException"/> with <c>"not sited"</c> if step 2 did not create the handle (parent collapsed, no window, etc.).</description></item>
    ///   <item><description><c>configure(rdp)</c> — safe to set properties now.</description></item>
    /// </list>
    /// </summary>
    /// <typeparam name="T">The AxHost-derived wrapper (e.g. <c>AxMsRdpClient9NotSafeForScripting</c>).</typeparam>
    /// <param name="viewport">The WPF <see cref="Panel"/> into which the WFH will be inserted. Must already be attached to a realized <see cref="System.Windows.Window"/>.</param>
    /// <param name="host">The <see cref="WindowsFormsHost"/> wrapper. Must not yet have a Child.</param>
    /// <param name="rdp">The freshly-constructed AxHost instance. Must not yet be parented.</param>
    /// <param name="configure">Callback that sets properties on the (now sited) control.</param>
    /// <exception cref="InvalidOperationException">Thrown if the handle is still 0 after adding to the visual tree — message contains the substring <c>"not sited"</c>.</exception>
    public static void SiteAndConfigure<T>(
        Panel viewport,
        WindowsFormsHost host,
        T rdp,
        Action<T> configure) where T : AxHost
    {
        host.Child = rdp;                   // (1) Child assignment triggers CreateControl() inside WFH
        viewport.Children.Add(host);         // (2) Add to visual tree — triggers handle creation
        if (rdp.Handle == IntPtr.Zero)
            throw new InvalidOperationException(
                "AxHost not sited after adding to visual tree. " +
                "Parent container may be collapsed or have no layout. See RDP-ACTIVEX-PITFALLS §1.");
        configure(rdp);                      // (3) Now safe to set properties
    }
}
