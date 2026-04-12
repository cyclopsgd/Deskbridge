using System;
using System.Windows.Forms;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// Test-only AxHost stub used to exercise <see cref="Deskbridge.Protocols.Rdp.AxSiting.SiteAndConfigure"/>
/// with a forced <c>Handle == IntPtr.Zero</c> scenario.
///
/// <para>
/// <b>Why this exists</b>: under WPF 10 / .NET 10 with a pumped <see cref="System.Windows.Threading.Dispatcher"/>
/// (see <see cref="Deskbridge.Tests.Fixtures.StaRunner"/>), adding the real
/// <c>AxMsRdpClient9NotSafeForScripting</c> to an unrooted <see cref="System.Windows.Controls.Grid"/>
/// still realizes a Win32 handle — so the "handle stays 0" branch of the guard can never be
/// exercised end-to-end without a stub. Plan 04-01 Task 0.1 (line 166) pre-authorized this
/// exact approach: "private test-only <c>FakeAxHost : AxHost</c> stub".
/// </para>
///
/// <para>
/// <b>How it forces Handle == 0</b>: <c>AxSiting.SiteAndConfigure&lt;T&gt;(...)</c> constrains
/// <c>T : AxHost</c> so the call-site <c>rdp.Handle</c> statically resolves against
/// <see cref="Control.Handle"/> — the <c>new</c> keyword on a derived member would be invisible
/// to the generic dispatch. We therefore cannot "hide" Handle; we must make the real
/// <see cref="Control.Handle"/> property return zero. We do this by overriding
/// <see cref="Control.CreateHandle"/> as a no-op, which prevents the lazy handle creation
/// <see cref="Control.Handle"/> normally performs, and by no-op'ing
/// <see cref="AxHost.AttachInterfaces"/> / <see cref="AxHost.CreateSink"/> /
/// <see cref="AxHost.DetachSink"/> so the AxHost base class doesn't try to activate a COM
/// object for the fake CLSID. The resulting control's <see cref="Control.Handle"/> stays at
/// <see cref="IntPtr.Zero"/> no matter what the <see cref="System.Windows.Forms.Integration.WindowsFormsHost"/>
/// does during siting.
/// </para>
///
/// <para>
/// <b>Scope</b>: test-only. Never use this outside <c>SitingGuardTests</c>. The stub deliberately
/// has no functional ActiveX behavior — it satisfies the base class contract just enough to be
/// passed through <c>AxSiting.SiteAndConfigure</c>.
/// </para>
/// </summary>
internal sealed class FakeAxHost : AxHost
{
    // GUID is arbitrary — AxHost's base ctor requires the CLSID string format but we override
    // AttachInterfaces/CreateSink to avoid any COM activation. The GUID is never dereferenced.
    private const string FakeClsid = "00000000-0000-0000-0000-000000000001";

    public FakeAxHost() : base(FakeClsid)
    {
    }

    // ActiveX sink methods — made no-ops so base class doesn't try to instantiate a real
    // COM object for the fake GUID. Without these overrides AxHost would CoCreate a nonexistent
    // CLSID and throw during activation.
    protected override void AttachInterfaces() { }
    protected override void CreateSink() { }
    protected override void DetachSink() { }

    /// <summary>
    /// Overrides the handle-creation path so WindowsFormsHost's CreateControl() call is inert.
    /// The base <see cref="AxHost.CreateHandle"/> would walk the ActiveX siting dance; we simply
    /// no-op and let <see cref="Control.IsHandleCreated"/> stay false, which keeps
    /// <see cref="Control.Handle"/> returning <see cref="IntPtr.Zero"/>.
    /// </summary>
    protected override void CreateHandle()
    {
        // No-op: we explicitly want Handle to remain zero.
    }
}
