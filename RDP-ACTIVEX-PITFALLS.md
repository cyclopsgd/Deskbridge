# Hosting RDP ActiveX in WPF: every known pitfall

**The Microsoft RDP ActiveX control (mstscax.dll) is one of the most treacherous COM components to host in managed code.** Across eight critical dimensions — siting order, GDI leaks, disposal, COM casting, keyboard focus, thread affinity, connection lifecycle, and multi-instance scaling — dozens of documented bugs exist in the control itself, in the .NET AxHost wrapper, and in the WPF WindowsFormsHost interop layer. This report catalogs every known failure mode with production-ready C# workarounds, drawn from the mRemoteNG, MsRdpEx, and dotnet/winforms issue trackers, Microsoft KBs, and real-world crash reports. Targeting .NET 10, special attention is given to the .NET 8.0.11 `AxHost.Dispose()` regression and its downstream fixes.

---

## 1. Siting order: the AxHost state machine you cannot violate

The AxHost base class maintains an internal state machine with five states: **OC_PASSIVE (0)** → **OC_LOADED (1)** → **OC_RUNNING (2)** → **OC_INPLACE (4)** → **OC_UIACTIVE (8)**. Every AxImp-generated property accessor checks `if (this.ocx == null)` and throws `AxHost.InvalidActiveXStateException` if the underlying COM object hasn't been created. The `ocx` field is populated during `AttachInterfaces()`, which fires during `TransitionUpTo(OC_RUNNING)`, which happens when the control is **sited** — added to a container that triggers handle creation.

**Every scenario that throws InvalidActiveXStateException:**

| Scenario | Root cause | Fix |
|---|---|---|
| Set properties after `new AxMsRdpClient9()` but before adding to container | `ocx` is null — `AttachInterfaces()` hasn't fired | Set `WindowsFormsHost.Child = rdp` first |
| Access properties in WPF Window constructor | WindowsFormsHost not yet in visual tree | Move to `Window.Loaded` event |
| Call `GetOcx()` before siting | Returns `null` — COM object not yet created | Access only after control is sited |
| Create control on a background thread | AxHost constructor calls `Application.OleRequired()` and throws `ThreadStateException` if not STA | Always create on the STA UI thread |
| Remove control from one parent, add to another (tab re-parenting) | State transitions down to OC_RUNNING between removal and re-addition; properties inaccessible in the gap | Hide/show containers instead of re-parenting |
| Access properties after `Dispose()` | `axState[disposed]` prevents re-siting | Create a new instance |
| Use `async Main` with `[STAThread]` | C# compiler generates a synthetic entry point; the attribute lands on the async method, not the actual entry point | Use synchronous `Main` or manually set apartment state |

**XAML vs code-behind:** You cannot instantiate AxHost-derived controls in XAML. The RDP control must be created in code-behind. The critical ordering is:

```csharp
// CORRECT — site the control, THEN set properties
var rdp = new AxMSTSCLib.AxMsRdpClient9NotSafeForScripting();
var host = new WindowsFormsHost();
host.Child = rdp;          // Triggers CreateControl() → TransitionUpTo(OC_INPLACE) → AttachInterfaces()
myGrid.Children.Add(host); // Adds to WPF visual tree

// NOW safe to configure:
rdp.DesktopWidth = 1920;
rdp.DesktopHeight = 1080;
rdp.Server = "10.0.0.1";
((IMsTscNonScriptable)rdp.GetOcx()).ClearTextPassword = "secret";
rdp.Connect();
```

```csharp
// WRONG — throws InvalidActiveXStateException immediately
var rdp = new AxMSTSCLib.AxMsRdpClient9NotSafeForScripting();
rdp.Server = "10.0.0.1"; // BOOM: ocx is null
```

**Re-siting after tab switch** is the subtlest pitfall. When you remove an AxHost from its parent, `DestroyHandle()` transitions the control down to OC_RUNNING. The `ocx` field remains valid, but the control is no longer in-place active. Any property access in the gap between removal and re-addition can throw. The safe pattern is to **never re-parent** — instead, keep each RDP control permanently in its own WindowsFormsHost and toggle the host's `Visibility`:

```csharp
// SAFE tab switching — hide/show, never re-parent
tabControl.SelectionChanged += (s, e) =>
{
    foreach (TabItem tab in tabControl.Items)
    {
        var host = (WindowsFormsHost)((ContentControl)tab).Content;
        host.Visibility = tab.IsSelected ? Visibility.Visible : Visibility.Collapsed;
    }
};
```

**The `CreateControl()` escape hatch:** Calling `rdp.CreateControl()` forces the state machine to transition up even without a visible parent. This creates an invisible HWND and populates `ocx`, but the control lacks proper parenting — keyboard focus, display rendering, and message pumping may behave incorrectly. Use this only for pre-configuration scenarios where you need to read interface capabilities before showing the control.

---

## 2. GDI handle leaks: the 10,000-handle cliff

Each process has a **default GDI handle quota of 10,000 objects** (configurable up to 65,536 via `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows\GDIProcessHandleQuota`). When the limit is hit, GDI allocations fail silently: fonts render as squares, minimize/maximize buttons show "0"/"1"/"r" instead of icons, and the app eventually crashes with "Generic error in GDI+."

**Confirmed leaks in mstscax.dll itself:**

Microsoft acknowledged handle leaks in **KB2992062** (RDP 8.1 on Windows 7/Server 2008 R2 — handles not closed on disconnect) and **KB3003743** (Windows 8.1 — thread handle leaks on disconnect). More critically, a **memory leak in mstscax.dll v10.0.26100.3037** (Windows 11 24H2) was confirmed in March 2025 via Windows Performance Analyzer: memory grows indefinitely during repeated connect/disconnect cycles. The leak does not occur in v10.0.22621.4830 (22H2).

**Framework-level GDI leaks:** Issue dotnet/winforms **#11334** documents a massive GDI region leak starting in .NET 7, specifically around RDP connect/disconnect operations. Issue **#13499** reveals that in .NET 8/9, the AxHost's internal `AxContainer` keeps a reference to the parent Form, preventing garbage collection of the entire form tree — and all its GDI resources. This was fixed by PR **#13532**.

**SmartSizing** does not directly leak GDI handles in a documented, reproducible way, but it creates internal bitmaps for StretchBlt scaling operations. Each resolution change via `UpdateSessionDisplaySettings()` potentially creates new bitmaps; if the old ones aren't released (which depends on the mstscax.dll version), handles accumulate.

**Monitor GDI handles programmatically:**

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

public static class GdiMonitor
{
    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

    private const int GR_GDIOBJECTS = 0;
    private const int GR_USEROBJECTS = 1;

    public static int GetGdiHandleCount()
        => GetGuiResources(Process.GetCurrentProcess().Handle, GR_GDIOBJECTS);

    public static int GetUserHandleCount()
        => GetGuiResources(Process.GetCurrentProcess().Handle, GR_USEROBJECTS);

    /// <summary>
    /// Call periodically (e.g., every 30s via DispatcherTimer) to detect leaks early.
    /// </summary>
    public static void WarnIfApproachingLimit(int threshold = 8000)
    {
        int gdi = GetGdiHandleCount();
        if (gdi > threshold)
        {
            Trace.TraceWarning($"GDI handle count is {gdi} — approaching the 10,000 limit. " +
                               $"User handles: {GetUserHandleCount()}");
        }
    }
}
```

**mRemoteNG's GDI crash pattern:** Issue **#1715** reports that mRemoteNG v1.77.2 cannot sustain more than **14 parallel RDP connections** before crashing with "Common error in GDI+." The fix involves setting `CacheBitmaps = false` on each connection, which reduces per-connection GDI usage at the cost of rendering performance:

```csharp
rdp.AdvancedSettings2.CachePersistenceActive = 0; // Disable bitmap caching
```

For external GDI analysis, use **NirSoft GDIView** (breaks down handles by type: Bitmap, DC, Font, Region) or **Sysinternals Process Explorer** (GDI Objects column in the Details tab).

---

## 3. Disposal: the exact sequence that won't hang or crash

Disposing an RDP ActiveX control incorrectly is the most common cause of application hangs and access violations. The control performs asynchronous network teardown internally. Calling `Dispose()` while connected triggers `AxHost.ReleaseAxControl()`, which can deadlock when internal RDP threads are still active.

**The .NET 8.0.11 fix (dotnet/winforms #12056, PR #12281):** In .NET 8.0.0 through 8.0.10, `ReleaseAxControl()` was changed from `Marshal.FinalReleaseComObject(_instance)` to `Marshal.ReleaseComObject(_instance)`. The single-decrement call doesn't bring the RCW reference count to zero because ActiveX controls have multiple internal COM interface references (event sinks, IOleObject, etc.). **The COM destructor was never called during Dispose()**, leaking all unmanaged resources. PR #12281 reverted to `FinalReleaseComObject`. This fix ships in .NET 8.0.11+ and was forward-ported to .NET 9 and .NET 10.

**The companion bug (dotnet/winforms #13499, PR #13532):** Even after the 8.0.11 fix, the AxHost's internal container holds a reference to the parent Form in .NET 8/9, preventing garbage collection. Fixed by PR #13532 (May 2025). **Verify this PR is included in your .NET 10 SDK build.**

**Complete safe disposal pattern:**

```csharp
public class RdpSession : IDisposable
{
    private AxMsRdpClient9NotSafeForScripting _rdp;
    private WindowsFormsHost _host;
    private bool _disposed;
    private bool _disconnecting;

    public async Task CleanupAsync()
    {
        if (_disposed || _rdp == null) return;

        // MUST be on STA UI thread
        Debug.Assert(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA);

        if (_rdp.Connected != 0)
        {
            _disconnecting = true;
            try { _rdp.Disconnect(); } catch { /* may throw if already mid-teardown */ }

            // Wait for OnDisconnected with a hard timeout
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (_rdp.Connected != 0 && DateTime.UtcNow < deadline)
            {
                // Must pump messages — COM events fire on the STA message loop
                await Task.Delay(100);
            }

            if (_rdp.Connected != 0)
            {
                Trace.TraceWarning("RDP disconnect timed out after 30s — force-disposing");
            }
        }

        // Unsubscribe all events BEFORE dispose to prevent callback-into-disposed-object
        try
        {
            _rdp.OnConnected -= OnConnected;
            _rdp.OnDisconnected -= OnDisconnected;
            _rdp.OnLoginComplete -= OnLoginComplete;
        }
        catch { }

        // Dispose the AxHost wrapper — this calls ReleaseAxControl() internally
        // Do NOT call Marshal.ReleaseComObject manually; let AxHost handle it
        try { _rdp.Dispose(); }
        catch (Exception ex) when (ex is AccessViolationException
                                    or InvalidComObjectException
                                    or COMException)
        {
            Trace.TraceError($"RDP dispose threw: {ex.GetType().Name}: {ex.Message}");
        }

        // Dispose the WindowsFormsHost — Microsoft explicitly warns that
        // not disposing it leaks resources in hybrid WPF/WinForms apps
        try { _host?.Dispose(); }
        catch { }

        _disposed = true;
    }

    public void Dispose() => CleanupAsync().GetAwaiter().GetResult();
}
```

**What happens in each failure scenario:**

- **Dispose() while connected:** `ReleaseAxControl()` internally calls `GC.GetTotalMemory(true)` which forces GC collection repeatedly; this can block indefinitely while the COM control is mid-teardown. Always disconnect first.
- **Remote host unreachable during disconnect:** `OnDisconnected` eventually fires (30–120 seconds) with reason code **264** (connection timed out) or **2308** (socket closed). The timeout in the polling loop is essential.
- **Dispose() from non-STA thread:** Throws `ThreadStateException` or causes undefined behavior — COM calls may be auto-marshaled via a proxy to the STA thread, which can deadlock if the STA isn't pumping messages.
- **Marshal.ReleaseComObject manually:** Causes `InvalidComObjectException` if AxHost subsequently tries to use the now-dead RCW during its own teardown. Never do this.

---

## 4. IMsTscNonScriptable: the password-setting cast that silently fails

The `ClearTextPassword` property lives on the `IMsTscNonScriptable` COM interface, not on the primary `IMsRdpClient` interface. You must cast the raw COM object obtained from `GetOcx()`. This cast fails in three documented scenarios.

**When the cast fails:**

1. **Control not sited:** `GetOcx()` returns `null`. Casting null doesn't throw — you get a null reference, and the subsequent property set throws `NullReferenceException`.
2. **Interop assembly version mismatch:** If the interop assembly was generated against a different mstscax.dll version, `QueryInterface` returns `E_NOINTERFACE` (0x80004002), which surfaces as `InvalidCastException`.
3. **Thread violation:** Calling from a non-STA thread may return a COM proxy that cannot be cast to the correct interface.

**The complete NonScriptable interface hierarchy:**

| Interface | Min RDP version | Key additions |
|---|---|---|
| `IMsTscNonScriptable` | 4.0 | `ClearTextPassword`, `BinaryPassword` |
| `IMsRdpClientNonScriptable` | 5.1 (XP) | `SendKeys()`, `NotifyRedirectDeviceChange()` |
| `IMsRdpClientNonScriptable2` | 5.2 | Connection bar UI settings |
| `IMsRdpClientNonScriptable3` | 6.0 (Vista) | `EnableCredSspSupport`, `NegotiateSecurityLayer` |
| `IMsRdpClientNonScriptable4` | 7.0 (Win 7) | `MarkRdpSettingsSecure`, `AllowCredentialSaving` |
| `IMsRdpClientNonScriptable5` | 8.0 (Win 8) | Device redirection extensions |

All interfaces inherit from `IMsTscNonScriptable`, so **all expose `ClearTextPassword`**. Cast to `IMsTscNonScriptable` for maximum compatibility; cast to a higher-numbered interface only when you need its specific features.

**Robust password-setting pattern:**

```csharp
private void SetPassword(AxMsRdpClient9NotSafeForScripting rdp, string password)
{
    // Precondition: control must be sited and disconnected
    if (rdp.Connected != 0)
        throw new InvalidOperationException("Cannot set password while connected");

    object ocx = rdp.GetOcx();
    if (ocx == null)
        throw new InvalidOperationException(
            "GetOcx() returned null — control is not sited. " +
            "Add it to a container (host.Child = rdp) before setting the password.");

    // Try the base interface first for maximum compatibility
    if (ocx is IMsTscNonScriptable nonScriptable)
    {
        nonScriptable.ClearTextPassword = password;
        return;
    }

    // Fallback: try the scriptable path via AdvancedSettings
    // (available on IMsRdpClientAdvancedSettings)
    try
    {
        rdp.AdvancedSettings2.ClearTextPassword = password;
        return;
    }
    catch (COMException ex)
    {
        throw new InvalidOperationException(
            $"Failed to set password via both NonScriptable and AdvancedSettings interfaces. " +
            $"COM error: 0x{ex.ErrorCode:X8}. Check mstscax.dll version and interop assembly compatibility.", ex);
    }
}
```

**Key constraints on `ClearTextPassword`:** The property is **write-only** (cannot be read back), can only be set when `Connected == 0`, and returns `E_FAIL` if set while connected. The password is transmitted over the encrypted RDP channel. If authentication fails despite a correct password, check whether `EnableCredSspSupport` (NLA) is properly configured on `IMsRdpClientNonScriptable3`.

---

## 5. Focus and keyboard: fighting the control's aggressive capture

The RDP ActiveX control installs a **low-level keyboard hook** (`SetWindowsHookEx(WH_KEYBOARD_LL, ...)`) when it gains focus with `KeyboardHookMode` set to 1 or 2. Because the most recently installed hook is called first, the RDP hook intercepts keystrokes before any application-level hooks. In WPF, the problem is compounded: the control creates its own child HWND within the WindowsFormsHost chain, completely bypassing WPF's managed keyboard routing. **WPF's `PreviewKeyDown` and `KeyDown` events never fire for keys consumed by the RDP control.**

**The `KeyboardHookMode` property** (set before `Connect()`):

| Value | Behavior |
|---|---|
| 0 | Keys stay local — RDP does NOT install its keyboard hook |
| 1 | All key combinations sent to remote server |
| 2 | Keys sent to remote only in full-screen mode (**default**) |

**mRemoteNG's approach** is simple but limited: they set `KeyboardHookMode = 1` when "Redirect Keys" is enabled and provide no mechanism to intercept individual keys. This means users cannot use Ctrl+Tab to switch tabs while an RDP session has focus — a well-documented complaint (issues #1535, #1749, #1831). mRemoteNG does not implement any custom keyboard hooks or `IMessageFilter`.

**Recommended layered strategy for a WPF tabbed manager:**

**Layer 1 — Set `KeyboardHookMode = 0` to keep app shortcuts local:**

```csharp
rdp.SecuredSettings2.KeyboardHookMode = 0; // Don't capture special keys
rdp.AdvancedSettings2.ContainerHandledFullScreen = 1; // Container manages full-screen
```

**Layer 2 — RegisterHotKey for app-level shortcuts (most reliable):**

`RegisterHotKey` operates at the Win32 level and delivers `WM_HOTKEY` messages regardless of which HWND has focus. This is the only approach that **reliably** intercepts keys before the RDP control's own hook.

```csharp
public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004;
    private const uint VK_TAB = 0x09, VK_W = 0x57, VK_P = 0x50;
    private const int ID_CTRL_TAB = 9001, ID_CTRL_W = 9002, ID_CTRL_SHIFT_P = 9003;

    private HwndSource _source;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source.AddHook(WndProc);

        // These are system-wide — will fail if another app already registered them
        RegisterHotKey(handle, ID_CTRL_TAB, MOD_CONTROL, VK_TAB);
        RegisterHotKey(handle, ID_CTRL_W, MOD_CONTROL, VK_W);
        RegisterHotKey(handle, ID_CTRL_SHIFT_P, MOD_CONTROL | MOD_SHIFT, VK_P);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312) // WM_HOTKEY
        {
            switch (wParam.ToInt32())
            {
                case ID_CTRL_TAB:
                    SwitchToNextTab();
                    handled = true;
                    break;
                case ID_CTRL_W:
                    CloseCurrentTab();
                    handled = true;
                    break;
                case ID_CTRL_SHIFT_P:
                    ShowCommandPalette();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, ID_CTRL_TAB);
        UnregisterHotKey(handle, ID_CTRL_W);
        UnregisterHotKey(handle, ID_CTRL_SHIFT_P);
        _source.RemoveHook(WndProc);
        base.OnClosed(e);
    }
}
```

**Caveat:** `RegisterHotKey` is system-wide. If another application (or another instance of your app) has already registered the same combination, the call returns `false`. For keys that can't be registered as hotkeys, fall back to a low-level keyboard hook.

**Layer 3 — Low-level keyboard hook as fallback:**

```csharp
public sealed class KeyboardInterceptor : IDisposable
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private IntPtr _hookId;
    private readonly LowLevelKeyboardProc _proc;
    public event Action<int>? AppHotkeyPressed; // virtual key code

    public KeyboardInterceptor()
    {
        _proc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, _proc,
            GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == 0x0100 /*WM_KEYDOWN*/ || wParam == 0x0104 /*WM_SYSKEYDOWN*/))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            bool ctrl = (GetAsyncKeyState(0xA2) & 0x8000) != 0 || (GetAsyncKeyState(0xA3) & 0x8000) != 0;

            if (ctrl && vkCode == 0x09 /*VK_TAB*/)
            {
                AppHotkeyPressed?.Invoke(vkCode);
                return (IntPtr)1; // Swallow the key
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => UnhookWindowsHookEx(_hookId);
}
```

**Critical issue:** The RDP control installs its own `WH_KEYBOARD_LL` hook when it gains focus. Since hooks are called in reverse installation order, your hook must be installed **after** the RDP hook to intercept keys first. When the RDP control emits a synthetic `vkFF` keystroke (indicating it has installed its hook), reinstall yours by calling `UnhookWindowsHookEx` followed by `SetWindowsHookEx`.

**Layer 4 — Focus management on tab switch:**

```csharp
tabControl.SelectionChanged += (s, e) =>
{
    // Delay focus restoration until the visual tree has settled
    Dispatcher.BeginInvoke(() =>
    {
        if (GetActiveRdpHost() is WindowsFormsHost host)
            host.Child?.Focus();
    }, DispatcherPriority.ContextIdle);
};
```

**Known WindowsFormsHost keyboard bugs:** `InputManager.Current.IsInMenuMode` can get stuck at `true` after interacting with WPF menus/ribbons, permanently blocking keyboard input to the hosted control. Also, `HwndSource.DefaultAcquireHwndFocusInMenuMode = false` (sometimes recommended in WPF documentation) causes WindowsFormsHost to lose keyboard input after menu interactions — do not set it.

---

## 6. Thread affinity: everything must stay on the STA

The RDP ActiveX control registers with `ThreadingModel=Apartment` (STA). The AxHost constructor enforces this by calling `Application.OleRequired()` and throwing `ThreadStateException` if the current thread is not STA. **All subsequent method calls, property access, and event handling must occur on the same STA thread that created the control.**

**The async/await trap:**

```csharp
// DANGEROUS — after ConfigureAwait(false), continuation runs on a thread pool thread
await SomeNetworkCallAsync().ConfigureAwait(false);
rdp.Connect(); // May be on an MTA thread — undefined behavior

// SAFE — WPF's SynchronizationContext resumes on the UI thread by default
await SomeNetworkCallAsync(); // ConfigureAwait(true) is the default
rdp.Connect(); // Still on the STA UI thread

// SAFEST — explicit marshaling
await SomeNetworkCallAsync().ConfigureAwait(false);
await Dispatcher.InvokeAsync(() => rdp.Connect()); // Explicitly marshaled to STA
```

**Timer callbacks:**

| Timer type | Thread | Safe for ActiveX? |
|---|---|---|
| `System.Threading.Timer` | Thread pool (MTA) | **No** — must marshal via `Dispatcher.Invoke()` |
| `System.Timers.Timer` | Thread pool (MTA) | **No** — same problem |
| `DispatcherTimer` | WPF UI thread (STA) | **Yes** |
| `System.Windows.Forms.Timer` | WinForms UI thread (STA) | **Yes** |

```csharp
// WRONG — timer callback on thread pool, COM call from MTA
var timer = new System.Threading.Timer(_ =>
{
    var status = rdp.Connected; // COM call from wrong apartment!
}, null, 0, 5000);

// RIGHT — use DispatcherTimer
var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
timer.Tick += (s, e) =>
{
    int gdi = GdiMonitor.GetGdiHandleCount();
    bool connected = rdp.Connected != 0;
    StatusText = $"GDI: {gdi} | Connected: {connected}";
};
timer.Start();
```

**Deadlock scenario to avoid:**

```csharp
// UI thread (STA):
await Task.Run(() =>
{
    // Thread pool (MTA):
    Dispatcher.Invoke(() =>   // Blocks waiting for STA
    {
        rdp.Disconnect();     // Never executes
    });
});
// UI thread is blocked waiting for Task.Run → Task.Run blocked waiting for Dispatcher.Invoke → DEADLOCK
```

Use `Dispatcher.InvokeAsync()` instead of `Dispatcher.Invoke()` from background threads to avoid deadlocks.

---

## 7. Connection events and the 50+ disconnect reason codes

**Event firing order during a successful connection:**

1. `OnConnecting` — fired immediately when `Connect()` is called
2. `OnConnected` — TCP/TLS connection established (before user logon)
3. `OnLoginComplete` — session desktop is visible
4. *(session active)*
5. `OnDisconnected(discReason)` — session ends

If logon fails, `OnLogonError` fires between `OnConnected` and `OnDisconnected`. During auto-reconnect, `OnAutoReconnecting` fires with an attempt counter and a controllable output parameter.

**Calling `Connect()` while already connected** is not allowed. The control checks its internal state and either silently ignores the call or throws. Always check `rdp.Connected != 0` first, and if reconnection is needed, disconnect first and wait for `OnDisconnected`.

**Disconnect reason code structure:** The `discReason` parameter is a **compound value**. The low byte indicates the error class; upper bytes indicate the specific error. Use `rdp.GetErrorDescription(discReason, (uint)rdp.ExtendedDisconnectReason)` for a human-readable string.

**Key disconnect reason codes by category:**

| Category | Codes | Meaning |
|---|---|---|
| **User-initiated** | 1 (local disconnect), 2 (remote by user) | Normal closure |
| **Server-initiated** | 3 (remote by server) | Admin disconnect, policy, etc. |
| **Network lost** | 264 (timeout), 516 (connect failed), 772 (send failed), 1028 (recv failed), 2308 (socket closed/FD_CLOSE) | Connection interrupted |
| **DNS failure** | 260 (DNS lookup failed), 520 (host not found) | Name resolution |
| **Authentication** | 2055 (logon failure), 2567 (no such user), 2823 (account disabled), 3335 (locked out), 3591 (account expired), 3847 (password expired) | Credential issues |
| **Licensing** | 2056 (licensing failed), 2312 (licensing timeout) | RDS CAL problems |
| **Protocol/resource** | 3334 (protocol error — often memory exhaustion) | Client resource limits |

**Extended disconnect reasons** (from `rdp.ExtendedDisconnectReason`) provide server-side context: **3** = idle timeout, **5** = replaced by another connection, **11** = user-initiated disconnect via RPC, **12** = user logged off. Values **256–267** indicate licensing subsystem errors.

**Auto-reconnect handling:**

```csharp
rdp.OnAutoReconnecting2 += (object sender,
    IMsTscAxEvents_OnAutoReconnecting2Event e) =>
{
    // e.disconnectReason: why the session dropped
    // e.attemptCount: which retry attempt (1, 2, 3, ...)
    // e.pArcContinueStatus: output — controls reconnection behavior

    if (_userClosedTab)
    {
        // User closed the tab during reconnect — cancel it
        e.pArcContinueStatus = AutoReconnectContinueState.autoReconnectContinueStop;
        return;
    }

    if (e.attemptCount > 20)
    {
        // Too many retries — give up
        e.pArcContinueStatus = AutoReconnectContinueState.autoReconnectContinueStop;
        return;
    }

    // Let it keep trying
    e.pArcContinueStatus = AutoReconnectContinueState.autoReconnectContinueAutomatic;
};
```

**Comprehensive event wiring pattern:**

```csharp
private void WireEvents(AxMsRdpClient9NotSafeForScripting rdp)
{
    rdp.OnConnecting += (s, e) =>
        Trace.TraceInformation("RDP: Connecting...");

    rdp.OnConnected += (s, e) =>
        Trace.TraceInformation("RDP: TCP connected (pre-logon)");

    rdp.OnLoginComplete += (s, e) =>
        Trace.TraceInformation("RDP: Login complete — session active");

    rdp.OnDisconnected += (s, e) =>
    {
        string desc = rdp.GetErrorDescription((uint)e.discReason, 0);
        var extended = rdp.ExtendedDisconnectReason;
        Trace.TraceInformation($"RDP: Disconnected. Reason={e.discReason} " +
                               $"Extended={extended} Desc={desc}");

        bool isNetworkLoss = e.discReason is 264 or 516 or 772 or 1028 or 2308;
        bool isUserInitiated = e.discReason is 1 or 2;
        bool isServerKick = e.discReason == 3;
        bool isAuthFailure = (e.discReason & 0xFF) == 0x07 && e.discReason > 2000;
    };

    rdp.OnLogonError += (s, e) =>
        Trace.TraceWarning($"RDP: Logon error code {e.lError}");
}
```

---

## 8. Multiple instances: what fails at scale and how to survive

Real-world data from mRemoteNG, 1Remote, and Royal TS reveals hard limits when hosting many RDP ActiveX instances in a single process.

**Observed failure thresholds:**

| Source | Limit | Failure mode |
|---|---|---|
| mRemoteNG #1715 | **14 connections** | "Common error in GDI+" crash |
| mRemoteNG #824 | **18 connections** | Error 3334 (protocol error) |
| mRemoteNG #864 | **15–20 connections** | Error 3334 — limit is per-process |
| mRemoteNG #616 | **8–11 connections** | Stack overflow (0xc00000fd) in ntdll.dll |

**What fails first, in order:**

1. **Address space** (32-bit processes only): Each RDP connection consumes **100–200 MB** for bitmap caching. A 32-bit process hits the 2 GB limit at roughly 10–15 connections. The `/LARGEADDRESSAWARE` flag extends this to ~3.5 GB on 64-bit Windows. **For .NET 10, always target x64 or AnyCPU (prefer 64-bit).**

2. **GDI handles**: Each active RDP instance creates an estimated **200–800 GDI objects** (DCs, bitmaps, brushes, regions) depending on resolution, color depth, and bitmap cache settings. At the default 10,000-handle limit, exhaustion occurs around 12–15 simultaneous connections.

3. **COM serialization**: All ActiveX instances share the single STA thread and its message pump. All COM calls are serialized through this thread. With many active sessions, the UI thread becomes a bottleneck — event processing, screen updates, and user input all compete for the same thread. This manifests as UI sluggishness before any hard crash.

**Crash propagation:** All instances live in the same process. A crash in mstscax.dll (e.g., stack buffer overrun 0xc0000409, as in mRemoteNG #1671) terminates the entire process and all connections. There is no isolation between instances.

**Production-ready multi-instance management:**

```csharp
public class RdpConnectionPool
{
    private readonly List<RdpSession> _sessions = new();
    private readonly DispatcherTimer _healthTimer;

    private const int MaxConnectionsPerProcess = 10; // Conservative safe limit
    private const int GdiWarningThreshold = 8000;

    public RdpConnectionPool()
    {
        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _healthTimer.Tick += MonitorHealth;
        _healthTimer.Start();
    }

    public bool CanAddConnection()
    {
        if (_sessions.Count >= MaxConnectionsPerProcess) return false;
        if (GdiMonitor.GetGdiHandleCount() > GdiWarningThreshold) return false;
        return true;
    }

    private void MonitorHealth(object? sender, EventArgs e)
    {
        int gdi = GdiMonitor.GetGdiHandleCount();
        int user = GdiMonitor.GetUserHandleCount();
        long memoryMb = Process.GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024);

        Trace.TraceInformation(
            $"Health: {_sessions.Count} sessions, GDI={gdi}, USER={user}, Memory={memoryMb}MB");

        if (gdi > 9000)
        {
            Trace.TraceError("CRITICAL: GDI handles at {0} — approaching 10,000 limit", gdi);
            // Consider force-closing idle sessions
        }
    }

    public RdpSession CreateSession()
    {
        var rdp = new AxMsRdpClient9NotSafeForScripting();

        // Reduce per-connection resource usage
        rdp.AdvancedSettings2.CachePersistenceActive = 0; // No bitmap cache persistence
        rdp.AdvancedSettings7.EnableCredSspSupport = true;
        rdp.AdvancedSettings2.GrabFocusOnConnect = false;  // Don't steal focus on connect

        var session = new RdpSession(rdp);
        _sessions.Add(session);
        return session;
    }
}
```

**Out-of-process hosting** is the nuclear option for true isolation. Devolutions' MsRdpEx supports launching each RDP connection as a separate mstsc.exe or msrdc.exe process and embedding the window via `SetParent`. This eliminates per-process resource limits and crash propagation entirely, at the cost of significantly more complex window management. For most applications, staying in-process with **10–12 connections max per process** and aggressive GDI monitoring is the pragmatic choice.

---

## Conclusion

Building a reliable tabbed RDP manager on .NET 10 requires navigating pitfalls at every layer. **The siting order is unforgiving** — one property access before `host.Child = rdp` and the control throws. **GDI handles are the practical scaling limit** at ~12–15 connections per process, not memory or COM apartments. **The .NET 8.0.11 fix (PR #12281) for `FinalReleaseComObject`** is present in .NET 10, but verify that the companion fix from PR #13532 (AxContainer keeping parent alive) is also included in your SDK build. **Never call `Dispose()` without disconnecting first and pumping messages.** For keyboard interception, `RegisterHotKey` is the only approach that reliably beats the RDP control's own `WH_KEYBOARD_LL` hook — `IMessageFilter` and WPF's `PreviewKeyDown` are ineffective when the control has focus. **The AltGr bug** (issue #655, #1461) is a known upstream defect in mstscax.dll that no hosting application can fix. And **every crash in mstscax.dll takes down the entire process** — for mission-critical deployments with many simultaneous sessions, consider Devolutions' out-of-process approach via MsRdpEx.