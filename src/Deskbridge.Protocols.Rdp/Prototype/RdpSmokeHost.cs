using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using AxMSTSCLib;
using MSTSCLib;

// WPF Panel is System.Windows.Controls.Panel; WinForms also has Panel. Alias to disambiguate.
using Panel = System.Windows.Controls.Panel;

namespace Deskbridge.Protocols.Rdp.Prototype;

/// <summary>
/// **Throwaway prototype** used exclusively by Plan 04-01 smoke gates to prove the
/// four dangerous RDP ActiveX primitives work in isolation (GDI cleanup, siting,
/// IMsTscNonScriptable cast, COM-error containment). Plan 04-02 will implement the
/// real <c>RdpHostControl</c> — this type is **not** a dependency of that work. The
/// <c>Prototype</c> namespace signals throwaway scope.
///
/// References (all mandatory reading before editing this file):
/// <list type="bullet">
///   <item><description>RDP-ACTIVEX-PITFALLS §1 (siting), §3 (disposal), §4 (IMsTscNonScriptable), §6 (STA), §7 (connection events)</description></item>
///   <item><description>WINFORMS-HOST-AIRSPACE §RdpHostWrapper.PerformFullCleanup (both reflection-based leak fixes)</description></item>
///   <item><description>.planning/phases/04-rdp-integration/04-RESEARCH.md §Example 1 (verbatim Dispose sequence, lines 782-887)</description></item>
///   <item><description>.planning/phases/04-rdp-integration/04-CONTEXT.md §D-02 (four gate criteria) and §Specifics (default property values)</description></item>
/// </list>
///
/// Security: The <c>password</c> parameter MUST NEVER appear in any log/trace/exception
/// message. Plan 04-01 acceptance criterion enforces this via grep.
/// </summary>
public sealed class RdpSmokeHost : IDisposable
{
    // --- Fields (order matches 04-01-PLAN.md task 1.2 item 3) ---
    private WindowsFormsHost? _host;
    private AxMsRdpClient9NotSafeForScripting? _rdp;
    private TaskCompletionSource<bool>? _loginTcs;
    private TaskCompletionSource<bool>? _disconnectTcs;
    private bool _disposed;
    private int? _lastDiscReason;

    /// <summary>The hosted <see cref="WindowsFormsHost"/>. Throws if disposed.</summary>
    public WindowsFormsHost Host => _host ?? throw new ObjectDisposedException(nameof(RdpSmokeHost));

    /// <summary>
    /// Last observed <c>OnDisconnected.discReason</c> from the underlying ActiveX control,
    /// or <c>null</c> if no disconnect event has fired. Gate 4 asserts this is non-null
    /// and non-zero after a failed connect attempt.
    /// </summary>
    public int? LastDiscReason => _lastDiscReason;

    /// <summary>
    /// Raised when the ActiveX control surfaces a sanitized error (logon failure, COM exception,
    /// etc). Message NEVER contains the password. Gate 4 consumes this to verify the failure
    /// propagates cleanly without tearing down the process.
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Creates the host + AxHost pair. MUST be called on an STA thread (RDP-ACTIVEX-PITFALLS §6).
    /// </summary>
    public RdpSmokeHost()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("RdpSmokeHost must be constructed on STA thread (RDP-ACTIVEX-PITFALLS §6).");

        _host = new WindowsFormsHost { Background = System.Windows.Media.Brushes.Black };
        _rdp = new AxMsRdpClient9NotSafeForScripting();
    }

    /// <summary>
    /// Sites the ActiveX control into <paramref name="viewport"/>, wires connect-time events,
    /// applies the default property set from 04-CONTEXT §Specifics, writes the password via
    /// <c>IMsTscNonScriptable.ClearTextPassword</c>, and calls <c>Connect()</c>. The returned
    /// task completes via <c>OnLoginComplete</c> (success) or <c>OnDisconnected</c>
    /// (connect-time failure) — per RDP-ACTIVEX-PITFALLS §7 both must race on the same TCS.
    /// </summary>
    public Task ConnectAsync(
        Panel viewport,
        string hostname,
        int port,
        string? username,
        string? domain,
        string? password,
        CancellationToken ct)
    {
        if (_disposed || _rdp is null || _host is null)
            throw new ObjectDisposedException(nameof(RdpSmokeHost));

        AssertSta();

        _loginTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Wire handlers BEFORE siting so we don't miss events that might fire on CreateControl path.
        _rdp.OnLoginComplete += OnLoginCompleteHandler;
        _rdp.OnDisconnected += OnDisconnectedDuringConnectHandler;
        _rdp.OnLogonError += OnLogonErrorHandler;

        // Site first (triggers CreateControl + handle creation), THEN configure.
        AxSiting.SiteAndConfigure(viewport, _host, _rdp, rdp =>
        {
            rdp.Server = hostname;
            rdp.AdvancedSettings9.RDPPort = port;
            rdp.UserName = username ?? "";
            rdp.Domain = domain ?? "";
            rdp.ColorDepth = 32;
            rdp.AdvancedSettings9.SmartSizing = true;
            rdp.AdvancedSettings9.EnableCredSspSupport = true;
            rdp.AdvancedSettings9.CachePersistenceActive = 0;
            // NOTE: "BitmapPeristence" (no 's') — this is the COM typelib's misspelling; do NOT "fix".
            rdp.AdvancedSettings9.BitmapPeristence = 0;
            rdp.SecuredSettings3.KeyboardHookMode = 0;
            rdp.AdvancedSettings9.GrabFocusOnConnect = false;
            rdp.AdvancedSettings9.EnableAutoReconnect = false;
            rdp.AdvancedSettings9.ContainerHandledFullScreen = 0;
        });

        // After siting: safe to cast to IMsTscNonScriptable and set the password.
        // Do NOT log the password; do NOT interpolate it into any exception.
        try
        {
            var ocx = _rdp.GetOcx() as IMsTscNonScriptable
                ?? throw new InvalidOperationException("GetOcx returned non-IMsTscNonScriptable (control may not be sited).");
            ocx.ClearTextPassword = password ?? "";
        }
        catch (COMException ex)
        {
            ErrorOccurred?.Invoke(this, "ClearTextPassword COMException: " + ex.Message);
            _loginTcs.TrySetException(ex);
            return _loginTcs.Task;
        }
        catch (InvalidCastException ex)
        {
            ErrorOccurred?.Invoke(this, "ClearTextPassword InvalidCastException: " + ex.Message);
            _loginTcs.TrySetException(ex);
            return _loginTcs.Task;
        }
        catch (NullReferenceException ex)
        {
            ErrorOccurred?.Invoke(this, "ClearTextPassword NullReferenceException (OCX null?): " + ex.Message);
            _loginTcs.TrySetException(ex);
            return _loginTcs.Task;
        }

        // Hook cancellation — test timeouts flow through here.
        ct.Register(() => _loginTcs?.TrySetCanceled(ct));

        // Fire the connect. Returns immediately; OnLoginComplete / OnDisconnected race on _loginTcs.
        try
        {
            _rdp.Connect();
        }
        catch (COMException ex)
        {
            ErrorOccurred?.Invoke(this, "Connect COMException: " + ex.Message);
            _loginTcs.TrySetException(ex);
        }
        catch (AxHost.InvalidActiveXStateException ex)
        {
            ErrorOccurred?.Invoke(this, "Connect InvalidActiveXStateException: " + ex.Message);
            _loginTcs.TrySetException(ex);
        }

        return _loginTcs.Task;
    }

    /// <summary>
    /// Best-effort graceful disconnect with a 30-second cap (RDP-ACTIVEX-PITFALLS §3).
    /// Uses plain <c>await Task.Delay</c> — no <c>ConfigureAwait(false)</c> — so continuations
    /// stay on the STA thread (D-11).
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_disposed || _rdp is null) return;
        AssertSta();

        if (_rdp.Connected != 0)
        {
            try { _rdp.Disconnect(); } catch { /* may throw mid-teardown — swallow */ }

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (_rdp.Connected != 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);  // Plain await — stays on STA
            }
        }
    }

    /// <summary>
    /// Full disposal sequence copied verbatim from 04-RESEARCH §Example 1 (lines 802-880).
    /// Includes BOTH mandatory reflection-based WFH leak fixes (Common Pitfalls 1) because
    /// Gate 1's GDI delta measurement depends on them.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AssertSta();

        // Synchronous disposal is OK on STA — the message pump is active.
        try { DisconnectAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { DebugTrace("DisconnectAsync during Dispose threw: " + ex.GetType().Name); }

        // --- Event unsubscribe (must precede _rdp.Dispose to prevent callback-into-disposed-object) ---
        if (_rdp is not null)
        {
            try
            {
                _rdp.OnLoginComplete -= OnLoginCompleteHandler;
                _rdp.OnDisconnected -= OnDisconnectedDuringConnectHandler;
                _rdp.OnDisconnected -= OnDisconnectedAfterConnectHandler;
                _rdp.OnLogonError -= OnLogonErrorHandler;
            }
            catch { /* unsubscribe is best-effort */ }
        }

        // --- WFH leak fix #1: HwndSourceKeyboardInputSite._sinkElement + _sink null-out ---
        // Without this, the WFH keyboard input site holds a UIElement reference that never
        // releases. dotnet/winforms #13499 is still reported as broken in .NET 10 preview 6.
        if (_host is not null)
        {
            try
            {
                var site = ((IKeyboardInputSink)_host).KeyboardInputSite;
                if (site is not null)
                {
                    site.Unregister();
                    var siteType = site.GetType();
                    siteType.GetField("_sinkElement", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.SetValue(site, null);
                    siteType.GetField("_sink", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.SetValue(site, null);
                }
            }
            catch (Exception ex)
            {
                DebugTrace("KeyboardInputSite leak-fix failed (non-fatal): " + ex.GetType().Name);
            }
        }

        // --- WFH leak fix #2: WinFormsAdapter via HostContainerInternal dispose ---
        // Without this, the static InputManager event list retains the host, leaking the
        // whole WFH/AxHost/form tree and its GDI handles.
        if (_host is not null)
        {
            try
            {
                var adapterProp = typeof(WindowsFormsHost).GetProperty(
                    "HostContainerInternal", BindingFlags.NonPublic | BindingFlags.Instance);
                (adapterProp?.GetValue(_host) as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                DebugTrace("WinFormsAdapter leak-fix failed (non-fatal): " + ex.GetType().Name);
            }
        }

        // --- Release COM + dispose AxHost ---
        if (_rdp is not null)
        {
            try
            {
                if (_host is not null) _host.Child = null;
                try
                {
                    var ocx = _rdp.GetOcx();
                    if (ocx is not null) Marshal.FinalReleaseComObject(ocx);
                }
                catch { /* OCX may already be released mid-teardown */ }
                _rdp.Dispose();
            }
            catch (Exception ex) when (ex is AccessViolationException
                                        or InvalidComObjectException
                                        or COMException)
            {
                DebugTrace("AxHost dispose threw — continuing teardown: " + ex.GetType().Name);
            }
            _rdp = null;
        }

        // --- Dispose WFH ---
        try { _host?.Dispose(); } catch { /* best-effort */ }
        _host = null;
    }

    // --- Event handlers ---------------------------------------------------

    private void OnLoginCompleteHandler(object? sender, EventArgs e)
    {
        // Post-login: swap the OnDisconnected handler from "during connect" (fails _loginTcs)
        // to "after connect" (signals _disconnectTcs). RDP-ACTIVEX-PITFALLS §7.
        if (_rdp is not null)
        {
            try { _rdp.OnDisconnected -= OnDisconnectedDuringConnectHandler; } catch { }
            try { _rdp.OnDisconnected += OnDisconnectedAfterConnectHandler; } catch { }
        }
        _disconnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _loginTcs?.TrySetResult(true);
    }

    private void OnDisconnectedDuringConnectHandler(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        _lastDiscReason = e.discReason;
        ErrorOccurred?.Invoke(this, "Disconnected during connect: discReason=" + e.discReason);
        _loginTcs?.TrySetException(new Exception("Disconnected during connect: discReason=" + e.discReason));
    }

    private void OnDisconnectedAfterConnectHandler(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        _lastDiscReason = e.discReason;
        _disconnectTcs?.TrySetResult(true);
    }

    private void OnLogonErrorHandler(object? sender, IMsTscAxEvents_OnLogonErrorEvent e)
    {
        ErrorOccurred?.Invoke(this, "LogonError: " + e.lError);
    }

    // --- Helpers ----------------------------------------------------------

    private static void AssertSta()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("RdpSmokeHost operation must run on STA thread (RDP-ACTIVEX-PITFALLS §6).");
    }

    /// <summary>
    /// Prototype-scope trace sink. Production <c>RdpHostControl</c> (Plan 04-02) uses
    /// <c>ILogger&lt;RdpHostControl&gt;</c>. MUST NEVER be passed the password.
    /// </summary>
    private static void DebugTrace(string message)
    {
        Debug.WriteLine("[RdpSmokeHost] " + message);
    }
}
