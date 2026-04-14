using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using AxMSTSCLib;
using Deskbridge.Core.Exceptions;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Services;
using Microsoft.Extensions.Logging;
using MSTSCLib;

namespace Deskbridge.Protocols.Rdp;

/// <summary>
/// Production <see cref="IProtocolHost"/> wrapping <c>AxMsRdpClient9NotSafeForScripting</c>
/// inside a <see cref="WindowsFormsHost"/>. Owns the strict-order COM lifecycle, the
/// IMsTscNonScriptable password write, and the full disposal sequence including both
/// reflection-based WFH leak fixes.
///
/// <para>Mandatory reading before editing: RDP-ACTIVEX-PITFALLS.md §1, §3, §4, §6, §7;
/// WINFORMS-HOST-AIRSPACE.md §leaks (both reflection fixes); 04-RESEARCH.md §Example 1
/// lines 782-887 for the verbatim Dispose sequence.</para>
///
/// <para><b>Security:</b> never log <see cref="ConnectionContext.ResolvedPassword"/>, never
/// log <c>ex.Message</c> or <c>ex.ToString()</c> for COM exceptions — type + HResult only.</para>
/// </summary>
public sealed class RdpHostControl : IProtocolHost
{
    private readonly ILogger<RdpHostControl> _logger;
    private WindowsFormsHost? _host;
    private AxMsRdpClient9NotSafeForScripting? _rdp;
    private TaskCompletionSource<bool>? _loginTcs;
    private bool _disposed;

    // Phase 5 fire-and-forget close integration: set by DisconnectAsync before
    // _rdp.Disconnect(). Consumed by OnDisconnectedAfterConnectHandler to
    // suppress DisconnectedAfterConnect when the user initiated the close —
    // ConnectionCoordinator would otherwise raise ReconnectOverlayRequested for
    // a tab that is already gone from the UI. Unexpected drops (network, server
    // reboot, session timeout) still fire the event and trigger the overlay.
    private bool _userInitiatedClose;

    public Guid ConnectionId { get; private set; }

    /// <summary>
    /// True iff the underlying ActiveX control reports a non-zero Connected state.
    /// Returns false defensively if the control has not been sited yet or is disposed —
    /// <c>AxMsRdpClient9.Connected</c> throws <c>InvalidActiveXStateException</c> before siting.
    /// </summary>
    public bool IsConnected
    {
        get
        {
            if (_rdp is null) return false;
            try { return _rdp.Connected != 0; }
            catch (AxHost.InvalidActiveXStateException) { return false; }
            catch (COMException) { return false; }
        }
    }

    /// <summary>The hosted <see cref="WindowsFormsHost"/>. Throws if disposed.</summary>
    public WindowsFormsHost Host => _host ?? throw new ObjectDisposedException(nameof(RdpHostControl));

    /// <summary>
    /// Reads the current live session resolution from the RDP ActiveX control. Returns
    /// (0, 0) if the control has not yet sited, is disposed, or <c>OnLoginComplete</c>
    /// has not yet fired (DesktopWidth/Height are 0 during the initial negotiation).
    ///
    /// <para>Phase 5 D-15 status bar fallback: callers should fall back to
    /// <c>ConnectionModel.DisplaySettings</c> when this returns (0, 0).</para>
    /// </summary>
    public (int Width, int Height) GetSessionResolution()
    {
        if (_disposed || _rdp is null) return (0, 0);
        try
        {
            return (_rdp.DesktopWidth, _rdp.DesktopHeight);
        }
        catch (AxHost.InvalidActiveXStateException) { return (0, 0); }
        catch (COMException) { return (0, 0); }
    }

    /// <summary>Sanitized error stream — type + HResult only, never credential material.</summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Post-login disconnect notification. Plan 04-03's reconnect coordinator subscribes here
    /// to classify discReason and drive the auto-retry loop per D-03/D-04.
    /// </summary>
    public event EventHandler<int>? DisconnectedAfterConnect;

    public RdpHostControl(ILogger<RdpHostControl> logger)
    {
        // [CITED: RDP-ACTIVEX-PITFALLS §6] STA assertion — defensive guard (D-11).
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException(
                "RdpHostControl must be created on the STA UI thread. See RDP-ACTIVEX-PITFALLS §6.");
        }

        _logger = logger;
        _host = new WindowsFormsHost { Background = System.Windows.Media.Brushes.Black };
        _rdp = new AxMsRdpClient9NotSafeForScripting();
        // [CITED: RDP-ACTIVEX-PITFALLS §1] Pre-wire AxHost as WFH.Child so that when the
        // caller (MainWindow.OnHostMounted) adds _host to the visual tree, the AxHost's
        // Win32 Handle realizes. Without this step the AxHost is orphaned — adding the WFH
        // to the visual tree is insufficient on its own, and ConnectStage would throw
        // "not sited" despite the WFH being parented. Matches the canonical sequence used
        // by Plan 04-01's RdpSmokeHost via AxSiting.SiteAndConfigure (step 1).
        _host.Child = _rdp;
    }

    public Task ConnectAsync(ConnectionContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ConnectionId = context.Connection.Id;

        // [CITED: RDP-ACTIVEX-PITFALLS §1] Site BEFORE configure. Runtime Handle guard — if
        // the caller (MainWindow) has not yet added _host to the visual tree, Handle stays 0.
        if (_rdp!.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "RdpHostControl.ConnectAsync called before host was added to the visual tree. " +
                "The control has not been sited. See RDP-ACTIVEX-PITFALLS §1.");
        }

        _loginTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Wire events BEFORE configuring — OnDisconnected must fire even if Connect() throws.
        _rdp.OnLoginComplete += OnLoginComplete;
        _rdp.OnDisconnected += OnDisconnectedDuringConnect;
        _rdp.OnLogonError += OnLogonError;

        RdpConnectionConfigurator.Apply(_rdp, context);

        _logger.LogInformation(
            "[diag] RDP settings applied: EnableCredSspSupport={CredSsp} AuthenticationLevel={AuthLevel} " +
            "SmartSizing={SmartSizing} ColorDepth={ColorDepth} DesktopWidth={DW} DesktopHeight={DH}",
            _rdp.AdvancedSettings9.EnableCredSspSupport,
            _rdp.AdvancedSettings9.AuthenticationLevel,
            _rdp.AdvancedSettings9.SmartSizing,
            _rdp.ColorDepth,
            _rdp.DesktopWidth,
            _rdp.DesktopHeight);

        // [CITED: RDP-ACTIVEX-PITFALLS §4] IMsTscNonScriptable password — sited + not-connected.
        // GetOcxForPasswordSet is an internal virtual seam for ErrorIsolationTests to stub the cast.
        if (!string.IsNullOrEmpty(context.ResolvedPassword))
        {
            try
            {
                var ocx = GetOcxForPasswordSet(_rdp) as IMsTscNonScriptable
                    ?? throw new InvalidOperationException("GetOcx() did not return IMsTscNonScriptable.");
                ocx.ClearTextPassword = context.ResolvedPassword;
                // Defense in depth (T-04-CRED): clear from context immediately after write.
                context.ResolvedPassword = null;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or NullReferenceException)
            {
                ErrorOccurred?.Invoke(this, $"Password set failed: {ex.GetType().Name}");
                _logger.LogWarning(
                    "Password set failed during connect to {Hostname}: {ExceptionType} HResult={HResult:X8}",
                    context.Connection.Hostname, ex.GetType().Name, ex.HResult);
                throw;
            }
        }

        _logger.LogInformation(
            "Connecting to {Hostname}:{Port} as {Username}",
            context.Connection.Hostname, context.Connection.Port, context.Connection.Username);

        try
        {
            _rdp.Connect();
        }
        catch (Exception ex) when (ex is COMException or AxHost.InvalidActiveXStateException)
        {
            ErrorOccurred?.Invoke(this, $"Connect threw: {ex.GetType().Name} HResult=0x{ex.HResult:X8}");
            _logger.LogWarning(
                "Connect threw for {Hostname}: {ExceptionType} HResult={HResult:X8}",
                context.Connection.Hostname, ex.GetType().Name, ex.HResult);
            _loginTcs.TrySetException(ex);
        }

        return _loginTcs.Task;
    }

    /// <summary>
    /// Internal seam for COM-exception simulation in <c>ErrorIsolationTests</c>. The class is
    /// <c>sealed</c> (per plan acceptance criterion "public sealed class RdpHostControl"), so
    /// this is a non-virtual internal method; tests exercise the catch-COM-exception flow via
    /// the ConnectStage mocked-host path rather than by subclassing. Deviation logged in SUMMARY.
    /// </summary>
    internal object? GetOcxForPasswordSet(AxMsRdpClient9NotSafeForScripting rdp) => rdp.GetOcx();

    public async Task DisconnectAsync()
    {
        if (_disposed || _rdp is null) return;
        AssertSta();

        // Flag the close as user-initiated so OnDisconnectedAfterConnectHandler
        // suppresses the DisconnectedAfterConnect event. Without this,
        // ConnectionCoordinator would raise ReconnectOverlayRequested for a tab
        // that TabHostManager.DoVisualClose has already removed from the UI.
        _userInitiatedClose = true;

        if (_rdp.Connected != 0)
        {
            try { _rdp.Disconnect(); }
            catch (Exception ex) when (ex is COMException)
            {
                _logger.LogDebug(
                    "Disconnect threw mid-teardown: {ExceptionType} HResult={HResult:X8}",
                    ex.GetType().Name, ex.HResult);
            }

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (_rdp.Connected != 0 && DateTime.UtcNow < deadline)
            {
                // Plain await — stays on STA per D-11
                await Task.Delay(100);
            }

            if (_rdp.Connected != 0)
            {
                _logger.LogWarning("RDP disconnect timed out after 30s — force disposing");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Only assert STA if we have resources to clean up — idempotent dispose must not throw.
        if (_rdp is null && _host is null) return;

        AssertSta();

        // Synchronous disposal is OK on STA — the message pump is active.
        try { DisconnectAsync().GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogDebug(
                "DisconnectAsync during Dispose threw: {ExceptionType} HResult={HResult:X8}",
                ex.GetType().Name, ex.HResult);
        }

        // --- Event unsubscribe ---
        if (_rdp is not null)
        {
            try
            {
                _rdp.OnLoginComplete -= OnLoginComplete;
                _rdp.OnDisconnected -= OnDisconnectedDuringConnect;
                _rdp.OnDisconnected -= OnDisconnectedAfterConnectHandler;
                _rdp.OnLogonError -= OnLogonError;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Event unsubscribe threw (non-fatal): {ExceptionType}", ex.GetType().Name);
            }
        }

        // --- WFH leak fix #1: HwndSourceKeyboardInputSite._sinkElement + _sink null-out ---
        // Without this, the WFH keyboard input site retains a UIElement reference that never
        // releases. dotnet/winforms #13499 status uncertain in .NET 10 GA — reflection fix mandatory.
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
                _logger.LogDebug("KeyboardInputSite leak-fix failed (non-fatal): {ExceptionType}", ex.GetType().Name);
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
                _logger.LogDebug("WinFormsAdapter leak-fix failed (non-fatal): {ExceptionType}", ex.GetType().Name);
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
                catch (Exception ex)
                {
                    _logger.LogDebug("FinalReleaseComObject threw (non-fatal): {ExceptionType}", ex.GetType().Name);
                }
                _rdp.Dispose();
            }
            catch (Exception ex) when (ex is AccessViolationException
                                        or InvalidComObjectException or COMException)
            {
                _logger.LogError(
                    "AxHost dispose threw — continuing teardown: {ExceptionType} HResult={HResult:X8}",
                    ex.GetType().Name, ex.HResult);
            }
            _rdp = null;
        }

        // --- Dispose WFH ---
        try { _host?.Dispose(); }
        catch (Exception ex)
        {
            _logger.LogDebug("WindowsFormsHost dispose threw (non-fatal): {ExceptionType}", ex.GetType().Name);
        }
        _host = null;
    }

    // --- Event handlers ------------------------------------------------------

    private void OnLoginComplete(object? sender, EventArgs e)
    {
        // Swap OnDisconnected from "during connect" (fails _loginTcs) to "after connect"
        // (raises DisconnectedAfterConnect for Plan 04-03 reconnect coordinator).
        if (_rdp is not null)
        {
            try { _rdp.OnDisconnected -= OnDisconnectedDuringConnect; } catch { }
            try { _rdp.OnDisconnected += OnDisconnectedAfterConnectHandler; } catch { }
        }
        _loginTcs?.TrySetResult(true);
    }

    private void OnDisconnectedDuringConnect(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        var extended = 0;
        Func<uint, uint, string>? describe = null;
        if (_rdp is not null)
        {
            try { extended = (int)_rdp.ExtendedDisconnectReason; } catch { }
            try { describe = _rdp.GetErrorDescription; } catch { }
        }
        var human = DisconnectReasonClassifier.Describe(e.discReason, extended, describe);
        _loginTcs?.TrySetException(new RdpConnectFailedException(e.discReason, human));
    }

    private void OnDisconnectedAfterConnectHandler(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        // Plan 04-03 subscribes here for reconnect logic. Phase 5 hotfix
        // (2026-04-14): suppress for user-initiated closes so the reconnect
        // overlay doesn't fire for a tab the user just dismissed. Network
        // drops, server reboots, session timeouts, and auth failures still
        // propagate normally.
        if (_userInitiatedClose) return;
        DisconnectedAfterConnect?.Invoke(this, e.discReason);
    }

    private void OnLogonError(object? sender, IMsTscAxEvents_OnLogonErrorEvent e)
    {
        ErrorOccurred?.Invoke(this, $"LogonError: {e.lError}");
        _logger.LogWarning("OnLogonError: lError={LError}", e.lError);
    }

    // --- Helpers -------------------------------------------------------------

    private static void AssertSta()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("RdpHostControl operation must run on STA thread.");
        }
    }
}
