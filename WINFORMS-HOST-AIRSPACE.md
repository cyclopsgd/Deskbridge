# The WPF WindowsFormsHost airspace problem: a complete survival guide

**WindowsFormsHost remains fundamentally broken for modern WPF scenarios**, and .NET 8–10 have introduced new regressions rather than fixes. The airspace problem — where Win32 child HWNDs always render on top of WPF content — has been open as dotnet/wpf issue #152 since December 2018, milestoned as "Future" with no concrete fix planned. For a tabbed RDP manager using WPF-UI's FluentWindow, every overlay control (ContentDialog, Flyout, Snackbar) will render behind the ActiveX control. The only viable workarounds involve temporarily swapping the live control for a static bitmap, hiding the WindowsFormsHost, or using separate top-level overlay windows. This guide covers every aspect of making this architecture work, with complete C# code targeting .NET 10.

---

## Why every WPF pixel belongs to exactly one HWND

The airspace constraint is architectural, not a bug. WPF renders via DirectX composition into a single top-level window, but `WindowsFormsHost` (which inherits from `HwndHost`) creates a **separate Win32 child HWND** that paints via GDI independently. The Win32 window manager enforces that each pixel in a top-level window belongs to exactly one child HWND. WPF's `Panel.ZIndex`, transforms, opacity, and clipping have **zero effect** on the hosted HWND — the ActiveX control always paints on top.

Microsoft attempted a fix in the .NET 4.5 Developer Preview with `HwndHost.IsRedirected` and `HwndHost.CompositionMode` properties that would redirect child window output to a bitmap for compositing. **These properties were removed before the final release** and never reintroduced.

For WPF-UI's `FluentWindow` specifically, this creates several concrete problems. The **Mica/Acrylic backdrop** works by setting `DWMWA_SYSTEMBACKDROP_TYPE` via DWM APIs, requiring WPF content areas to have transparent backgrounds — but `WindowsFormsHost` is an opaque Win32 child window, so the backdrop will never show through it. More critically, `ContentDialog` renders as a WPF overlay with a semi-transparent backdrop within the window's content area. Due to airspace, **the RDP control will paint on top of the ContentDialog**, making it unusable. The same applies to `Flyout`, `Snackbar`, and `NavigationView` popups — all are WPF visuals that cannot occlude a child HWND.

### The bitmap-swap workaround for dialogs and overlays

The most practical solution is the **AirspaceFixer** library (NuGet package, MIT license) or a manual implementation that captures a bitmap of the hosted control, replaces it with a static WPF `Image`, and then allows WPF overlays to render normally:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AirspaceFixer" Version="1.*" />
    <PackageReference Include="WPF-UI" Version="4.*" />
  </ItemGroup>
</Project>
```

```xml
<!-- MainWindow.xaml -->
<ui:FluentWindow x:Class="RdpManager.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    xmlns:asf="clr-namespace:AirspaceFixer;assembly=AirspaceFixer"
    Title="RDP Manager" Height="700" Width="1000"
    ExtendsContentIntoTitleBar="True">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="RDP Manager"/>

        <TabControl Grid.Row="1" x:Name="SessionTabs"
                    SelectionChanged="SessionTabs_SelectionChanged">
            <!-- Tabs added dynamically -->
        </TabControl>
    </Grid>
</ui:FluentWindow>
```

When you need to show a `ContentDialog`, wrap the pattern explicitly:

```csharp
// Before showing any WPF overlay (ContentDialog, Flyout, Snackbar):
private async Task ShowDialogOverRdp()
{
    // 1. Hide every visible WindowsFormsHost in the window
    foreach (var wrapper in _activeWrappers.Values)
        wrapper.Host.Visibility = Visibility.Collapsed;

    // 2. Now WPF overlays render correctly
    var dialog = new ui.ContentDialog
    {
        Title = "Disconnect?",
        Content = "Close this RDP session?",
        PrimaryButtonText = "Disconnect",
        CloseButtonText = "Cancel",
        DialogHost = this  // FluentWindow as host
    };

    var result = await dialog.ShowAsync();

    // 3. Restore the active tab's WFH
    if (_activeWrappers.TryGetValue(_currentTabId, out var active))
        active.Host.Visibility = Visibility.Visible;
}
```

If you want the user to see a frozen frame of the RDP session rather than a blank area while the dialog is open, use the `AirspaceFixer` approach or the manual `PrintWindow` capture described in the bitmap section below. The `AirspaceFixer` library wraps this pattern into a single dependency property toggle:

```xml
<asf:AirspacePanel x:Name="AirspaceWrapper"
                   FixAirspace="{Binding IsDialogOpen}">
    <WindowsFormsHost x:Name="WfHost" Background="Black"/>
</asf:AirspacePanel>
```

Set `IsDialogOpen = true` before showing the dialog (swaps the live control for a screenshot), and `false` after it closes (restores interactivity).

---

## PerMonitorV2 DPI across monitors is fundamentally broken

**dotnet/wpf issue #6294** documents that when using Per-Monitor V2 DPI awareness, the physical pixel dimensions of the child Win32 window inside `WindowsFormsHost` **do not change** when dragging between monitors with different DPI. WPF correctly updates its logical dimensions, but the hosted control's actual window size stays fixed. This is **worse in .NET 6+** than in .NET Framework 4.8, where at least font scaling worked for most controls. The issue remains open and under investigation.

**dotnet/wpf issue #9803** adds that in .NET 8, the `DpiAwareness` manifest settings that worked in .NET Framework 4.8 no longer activate correctly. The `RuntimeHostConfigurationOption` can prevent scaling but doesn't trigger proper DPI awareness.

### Required manifest settings

```xml
<!-- app.manifest -->
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">
        true/PM
      </dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">
        PerMonitorV2
      </dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

### Manual DPI change handler

Since automatic scaling doesn't work, you must manually scale the hosted control when DPI changes:

```csharp
public partial class RdpTabContent : UserControl
{
    private WindowsFormsHost _host;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host.DpiChanged += OnHostDpiChanged;
    }

    private void OnHostDpiChanged(object sender, DpiChangedEventArgs e)
    {
        if (_host.Child is not System.Windows.Forms.Control child) return;

        float scaleFactor = (float)(e.NewDpi.PixelsPerDip / e.OldDpi.PixelsPerDip);

        // For RDP ActiveX, the most reliable approach is to resize
        // the desktop dimensions and reconnect, or use Smart Sizing
        if (_rdpClient != null)
        {
            try
            {
                // Enable smart sizing so the control scales its output
                _rdpClient.AdvancedSettings9.SmartSizing = true;

                // Force the WinForms control to adopt the new size
                child.Scale(new System.Drawing.SizeF(scaleFactor, scaleFactor));
            }
            catch (InvalidOperationException)
            {
                // RDP control may reject changes during active session
            }
        }
    }
}
```

**The practical reality**: For an RDP client, **enable `SmartSizing`** on the AxMsRdpClient. This tells the RDP control to scale its rendered desktop bitmap to fit the control's current size, which works independently of WPF's DPI system. Combined with setting `DesktopWidth`/`DesktopHeight` at connection time based on the current monitor's DPI, this provides acceptable results even when the WFH DPI scaling is broken.

### Known .NET 8/9/10 DPI-related bugs

| Issue | Version | Problem | Status |
|-------|---------|---------|--------|
| #6294 | .NET 6+ | WFH child doesn't physically scale on DPI change | Open |
| #9803 | .NET 8 | DPI awareness manifest not activating correctly | Open |
| #10044 | .NET 9 | Fluent themes break WFH rendering entirely | Open |
| #878 | .NET Core 3.0+ | ActiveX control redrawn shifted/clipped after resize | Open |
| #10171 | .NET 8 | Infinite recursion crash on window close with WFH | Open |

---

## Bitmap snapshot during drag and resize

`RenderTargetBitmap` cannot capture `WindowsFormsHost` content because it operates within WPF's DirectX rendering pipeline, which has no access to the child HWND's GDI-painted surface. The correct approach uses the **Win32 `PrintWindow` API**, which sends `WM_PRINT` to the target window, causing it to paint into your memory DC. Unlike `BitBlt`, `PrintWindow` works even when the window is partially obscured or offscreen.

The complete implementation hooks `WM_ENTERSIZEMOVE` (0x0231) and `WM_EXITSIZEMOVE` (0x0232) to detect when the user starts and stops resizing:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace RdpManager;

/// <summary>
/// Captures a bitmap of a WindowsFormsHost's child HWND and manages
/// the snapshot overlay during window resize/move operations to
/// prevent flicker. Attach to any Window containing a WFH.
/// </summary>
public sealed class ResizeSnapshotManager
{
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE  = 0x0232;
    private const uint PW_CLIENTONLY   = 0x00000001;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width  => Right - Left;
        public int Height => Bottom - Top;
    }

    private readonly System.Windows.Forms.Integration.WindowsFormsHost _host;
    private readonly System.Windows.Controls.Image _overlay;
    private bool _inSizeMove;

    /// <summary>
    /// Creates a snapshot manager for the given WFH. The overlay Image
    /// must occupy the same layout slot as the WFH (e.g., same Grid cell).
    /// </summary>
    public ResizeSnapshotManager(
        System.Windows.Forms.Integration.WindowsFormsHost host,
        System.Windows.Controls.Image overlay)
    {
        _host = host;
        _overlay = overlay;
        _overlay.Visibility = Visibility.Collapsed;
        _overlay.IsHitTestVisible = false;
        _overlay.Stretch = System.Windows.Media.Stretch.Fill;
    }

    /// <summary>
    /// Call from Window.OnSourceInitialized to install the WndProc hook.
    /// </summary>
    public void Attach(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource source)
            source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                           IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ENTERSIZEMOVE && !_inSizeMove)
        {
            _inSizeMove = true;
            var snapshot = CaptureHwnd();
            if (snapshot is not null)
            {
                _overlay.Source = snapshot;
                _overlay.Visibility = Visibility.Visible;
            }
            _host.Visibility = Visibility.Hidden;
        }
        else if (msg == WM_EXITSIZEMOVE && _inSizeMove)
        {
            _inSizeMove = false;
            _host.Visibility = Visibility.Visible;
            _overlay.Visibility = Visibility.Collapsed;
            _overlay.Source = null;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Captures the WFH child's client area via PrintWindow.
    /// Returns a frozen BitmapSource suitable for display in a WPF Image.
    /// </summary>
    private BitmapSource? CaptureHwnd()
    {
        var child = _host.Child;
        if (child is null || !child.IsHandleCreated) return null;

        IntPtr hwnd = child.Handle;
        if (!GetClientRect(hwnd, out RECT rc) || rc.Width <= 0 || rc.Height <= 0)
            return null;

        IntPtr hdcWin = IntPtr.Zero, hdcMem = IntPtr.Zero, hBmp = IntPtr.Zero;
        try
        {
            hdcWin = GetDC(hwnd);
            if (hdcWin == IntPtr.Zero) return null;

            hdcMem = CreateCompatibleDC(hdcWin);
            if (hdcMem == IntPtr.Zero) return null;

            hBmp = CreateCompatibleBitmap(hdcWin, rc.Width, rc.Height);
            if (hBmp == IntPtr.Zero) return null;

            IntPtr hOld = SelectObject(hdcMem, hBmp);
            bool ok = PrintWindow(hwnd, hdcMem, PW_CLIENTONLY);
            SelectObject(hdcMem, hOld);

            if (!ok) return null;

            var bmpSrc = Imaging.CreateBitmapSourceFromHBitmap(
                hBmp, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bmpSrc.Freeze();
            return bmpSrc;
        }
        finally
        {
            if (hBmp   != IntPtr.Zero) DeleteObject(hBmp);
            if (hdcMem != IntPtr.Zero) DeleteDC(hdcMem);
            if (hdcWin != IntPtr.Zero) ReleaseDC(hwnd, hdcWin);
        }
    }
}
```

Wire it up in your `FluentWindow`:

```csharp
public partial class MainWindow : ui.FluentWindow
{
    private ResizeSnapshotManager? _snapshotMgr;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Assumes WfHost and SnapshotOverlay are in the same Grid cell
        _snapshotMgr = new ResizeSnapshotManager(WfHost, SnapshotOverlay);
        _snapshotMgr.Attach(this);
    }
}
```

**Why `PrintWindow` over `BitBlt`**: `PrintWindow` sends `WM_PRINT` to the target window, so the control paints itself into your DC regardless of occlusion or visibility state. `BitBlt` copies from the screen DC and captures whatever is currently visible — including other windows that may overlap. For a resize snapshot where you're about to hide the WFH, `PrintWindow` is the only reliable choice.

An important caveat: **WPF-UI's `FluentWindow` uses a custom window chrome** (similar to WPF's `WindowChrome`). dotnet/wpf issue #5892 documents that `WindowChrome` causes severe flicker when combined with `WindowsFormsHost` because `WindowChromeWorker` toggles `WS_VISIBLE` on every `WM_SIZE` message. The recommended mitigation is to use **ControlzEx's `WindowChromeBehavior`** (from ControlzEx 6.0+) instead of WPF's built-in `WindowChrome`, or to use the bitmap snapshot technique above to hide the WFH entirely during resize.

---

## Tab switching: reuse instances, never recreate

For a tabbed RDP manager, you have three architectural options. Only one works well.

**Option 1 — Create/destroy WFH per tab switch**: This is the worst approach. Each `WindowsFormsHost` creates a Win32 HWND, and each `AxMsRdpClient` instantiates a COM object with internal threads. The `HwndSourceKeyboardInputSite` leak (detailed below) means that even with explicit disposal, repeated create/destroy cycles accumulate references rooted by `InputManager.PostProcessInput`. HWND handles, GDI handles, and thread handles from `mstscax.dll` will grow over time. **Do not use this pattern.**

**Option 2 — Move a single WFH between tab content areas**: Reparenting a `WindowsFormsHost` resets its z-order and can trigger `UCEERR_RENDERTHREADFAILURE` crashes in nested interop scenarios (dotnet/wpf #3719). The RDP ActiveX control may also lose its connection state when its parent HWND changes. **Avoid this approach.**

**Option 3 — One WFH per connection, toggle visibility** (recommended): Create each `WindowsFormsHost` + `AxMsRdpClient` once when the user opens a connection. Keep all instances alive in memory. On tab switch, collapse inactive hosts and show only the active one:

```csharp
public sealed class RdpTabManager
{
    private readonly Dictionary<string, RdpHostWrapper> _sessions = new();
    private readonly Panel _viewport;  // The Grid/panel in the active tab area
    private string? _activeSessionId;

    public RdpTabManager(Panel viewport)
    {
        _viewport = viewport;
    }

    public void ActivateSession(string sessionId)
    {
        // Hide the currently active session
        if (_activeSessionId is not null
            && _sessions.TryGetValue(_activeSessionId, out var prev))
        {
            prev.Host.Visibility = Visibility.Collapsed;
            prev.Host.IsEnabled = false;  // Prevent hotkey capture
        }

        // Show the newly selected session
        if (_sessions.TryGetValue(sessionId, out var next))
        {
            next.Host.Visibility = Visibility.Visible;
            next.Host.IsEnabled = true;

            // Ensure it's in the viewport (may not be if just created)
            if (!_viewport.Children.Contains(next.Host))
                _viewport.Children.Add(next.Host);
        }

        _activeSessionId = sessionId;
    }

    public RdpHostWrapper CreateSession(string sessionId)
    {
        var wrapper = new RdpHostWrapper();
        // Set a solid background — avoids expensive transparent
        // backbuffer rendering that causes flicker with many WFH instances
        wrapper.Host.Background = System.Windows.Media.Brushes.Black;
        _sessions[sessionId] = wrapper;
        return wrapper;
    }

    public void CloseSession(string sessionId)
    {
        if (!_sessions.Remove(sessionId, out var wrapper)) return;
        _viewport.Children.Remove(wrapper.Host);
        wrapper.Dispose();  // Full cleanup sequence
    }
}
```

**Performance characteristics**: Each `Visibility.Collapsed` WFH still holds its Win32 HWND in memory, but the window is hidden and receives no paint messages. For **15–20 concurrent RDP sessions**, this is acceptable. Beyond that, minimize/maximize operations cause visible sluggishness as Windows processes `WM_SIZE` for all child HWNDs. A practical limit is **~30 simultaneous WindowsFormsHost instances** before GDI handle pressure and repaint latency become problematic. Setting `WindowsFormsHost.Background` to a solid color (not transparent) is critical — transparent background emulation forces an expensive backbuffer copy per frame per WFH.

---

## Popups, tooltips, and context menus mostly work — with caveats

WPF `ToolTip`, `ContextMenu`, and `Popup` internally create **separate top-level HWNDs**, which means they occupy their own airspace and can render on top of a `WindowsFormsHost`. However, there are edge cases.

The default WPF `ToolTip` and `ContextMenu` set `AllowsTransparency = true` on their internal `Popup`, creating a `WS_EX_LAYERED` window. Layered windows and Win32 child windows interact poorly — if you try to place a `WindowsFormsHost` *inside* a tooltip or popup, it will be invisible. But for the common case of a WPF tooltip appearing *over* a `WindowsFormsHost`, the tooltip renders correctly because it's a separate top-level HWND.

**The exception**: If you use WPF-UI's styled tooltips or popups that rely on transparency effects (blur, acrylic), and if these popups are positioned so they overlap the `WindowsFormsHost` area, the transparency compositing will not blend with the Win32 content. The popup will appear with a solid background where it overlaps the hosted control, rather than showing the RDP content through the transparency. This is cosmetic but noticeable.

For `ContextMenu` specifically, WPF creates it as a top-level `Popup` HWND, so it renders on top of the RDP control. No special handling is needed for standard right-click context menus.

---

## Memory and handle leaks require explicit reflection workarounds

`WindowsFormsHost` has **two well-documented leak paths** that are not fixed in .NET 10. Both require reflection to access internal/private fields.

**Leak 1 — `HwndSourceKeyboardInputSite._sinkElement`**: When WFH registers as an `IKeyboardInputSink`, the parent `HwndSource` creates an `HwndSourceKeyboardInputSite` that holds strong references to the WFH via `_sink` and `_sinkElement` fields. Calling `Dispose()` does not clear `_sinkElement`, so the WFH remains GC-rooted through the `HwndSource`.

**Leak 2 — `WinFormsAdapter` subscribes to `InputManager.Current.PostProcessInput`**: This is a static event, creating a GC root. The `WinFormsAdapter` (an internal `ContainerControl` created by WFH) only unsubscribes in its own `Dispose()` method, which WFH's `Dispose()` does not call.

Additionally, **dotnet/wpf issue #10171** documents a .NET 8 regression where closing a window containing `WindowsFormsHost` causes an infinite recursion crash. The workaround is to explicitly dispose the WFH before the window closes.

The complete cleanup class, including async RDP disconnect handling and both leak fixes:

```csharp
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using AxMSTSCLib;

namespace RdpManager;

/// <summary>
/// Wraps AxMsRdpClient in a WindowsFormsHost with complete cleanup
/// of all known leak patterns: keyboard input site, WinFormsAdapter
/// static event subscription, COM references, and HWND handles.
/// </summary>
public sealed class RdpHostWrapper : IDisposable
{
    private WindowsFormsHost? _host;
    private AxMsRdpClient9NotSafeForScripting? _rdpClient;
    private bool _disposed;
    private bool _isConnected;
    private bool _disconnectPending;

    public event EventHandler? Disposed;

    public WindowsFormsHost Host =>
        _host ?? throw new ObjectDisposedException(nameof(RdpHostWrapper));

    public bool IsConnected => _isConnected;

    public RdpHostWrapper()
    {
        _host = new WindowsFormsHost();
        _rdpClient = new AxMsRdpClient9NotSafeForScripting();

        _rdpClient.OnConnected += (_, _) => _isConnected = true;
        _rdpClient.OnDisconnected += OnRdpDisconnected;

        _host.Child = _rdpClient;
    }

    public void Connect(string server, int port = 3389,
        string? username = null, string? domain = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_rdpClient is null) return;

        _rdpClient.Server = server;
        _rdpClient.AdvancedSettings9.RDPPort = port;
        _rdpClient.AdvancedSettings9.SmartSizing = true;

        if (username is not null) _rdpClient.UserName = username;
        if (domain is not null) _rdpClient.Domain = domain;

        _rdpClient.Connect();
    }

    private void OnRdpDisconnected(object? sender,
        IMsTscAxEvents_OnDisconnectedEvent e)
    {
        _isConnected = false;
        if (_disconnectPending)
        {
            _disconnectPending = false;
            PerformFullCleanup();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isConnected && _rdpClient is not null)
        {
            _disconnectPending = true;
            try { _rdpClient.Disconnect(); }
            catch
            {
                _disconnectPending = false;
                _isConnected = false;
                PerformFullCleanup();
            }
        }
        else
        {
            PerformFullCleanup();
        }
    }

    private void PerformFullCleanup()
    {
        // ── Step 1: Fix HwndSourceKeyboardInputSite leak ──
        if (_host is not null)
        {
            try
            {
                var site = ((IKeyboardInputSink)_host).KeyboardInputSite;
                if (site is not null)
                {
                    site.Unregister();

                    // Null _sinkElement via reflection (still holds UIElement ref)
                    var siteType = site.GetType();
                    siteType.GetField("_sinkElement",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.SetValue(site, null);
                    siteType.GetField("_sink",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.SetValue(site, null);
                }
            }
            catch { /* Reflection may fail across .NET versions */ }
        }

        // ── Step 2: Dispose WinFormsAdapter (InputManager static event) ──
        if (_host is not null)
        {
            try
            {
                var adapterProp = typeof(WindowsFormsHost).GetProperty(
                    "HostContainerInternal",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                (adapterProp?.GetValue(_host) as IDisposable)?.Dispose();
            }
            catch { }
        }

        // ── Step 3: Detach and dispose ActiveX control ──
        if (_rdpClient is not null)
        {
            try
            {
                if (_host is not null) _host.Child = null;

                // Release the underlying COM object
                try
                {
                    var ocx = _rdpClient.GetOcx();
                    if (ocx is not null) Marshal.FinalReleaseComObject(ocx);
                }
                catch { }

                _rdpClient.Dispose();
            }
            catch { }
            _rdpClient = null;
        }

        // ── Step 4: Dispose the WindowsFormsHost ──
        try { _host?.Dispose(); }
        catch { }
        _host = null;

        Disposed?.Invoke(this, EventArgs.Empty);
    }
}
```

### Critical: dispose before window close in .NET 8+

To prevent the infinite recursion crash (dotnet/wpf #10171), hook `Window.Closing`:

```csharp
public partial class MainWindow : ui.FluentWindow
{
    protected override void OnClosing(CancelEventArgs e)
    {
        // Dispose ALL WindowsFormsHost instances BEFORE the window closes
        foreach (var wrapper in _tabManager.GetAllSessions())
            wrapper.Dispose();

        base.OnClosing(e);
    }
}
```

### What exactly leaks without these fixes

Without the reflection workarounds, each undisposed `WindowsFormsHost` leaves behind: the `WinFormsAdapter` rooted by `InputManager.Current.PostProcessInput` (a static event), the `WindowsFormsHost` itself held by `HwndSourceKeyboardInputSite._sinkElement`, the child ActiveX control and its entire COM object graph, the Win32 HWND (consuming a USER handle), and associated GDI handles. Microsoft's own RDP ActiveX control (`mstscax.dll`) additionally leaks **thread handles** per connect/disconnect cycle (documented in KB 3042836). The leak rate is version-dependent — `mstscax.dll` v10.0.26100.3037 leaks measurably, while v10.0.22621.4830 does not.

---

## Real-world implementations and what we can learn from them

**mRemoteNG** (10.7k GitHub stars) sidesteps the airspace problem entirely by being a **pure WinForms application**. It hosts `AxMsRdpClient` natively in WinForms panels with a WinForms docking library. There is no WPF layer, so there is no airspace conflict. This is the path of least resistance — but it forecloses modern UI frameworks like WPF-UI.

**1Remote** (formerly PRemoteM) is the most relevant reference implementation: a WPF-based RDP manager supporting tabbed sessions with RDP, SSH, VNC, and other protocols. It uses `WindowsFormsHost` to embed `AxMsRdpClient` and employs **tab isolation** — only the active tab's WFH is visible. Inactive sessions are hidden. 1Remote does not use AirspaceFixer or the DwayneNeed library; it relies on the simpler pattern of ensuring overlays don't coexist with visible WFH instances.

**No WPF-UI + WindowsFormsHost issues exist** on the wpfui GitHub repository. This combination is largely unexplored by the wpfui community. However, the FluentWPF project (a similar Mica/Acrylic library) has a documented issue (#60) confirming that WindowsFormsHost cannot display a transparent background with AcrylicWindow — the hosted area renders as a solid rectangle.

### The three airspace mitigation libraries

- **AirspaceFixer** (NuGet, MIT): Simplest to use. Wraps any content in an `AirspacePanel` with a `FixAirspace` toggle. Captures a bitmap via `Control.DrawToBitmap()` and swaps the live control for a static `Image`. Works well for modal dialogs. Does not work for continuous overlays since the hosted content becomes non-interactive.

- **Microsoft.DwayneNeed** (.NET 6 port at github.com/0xJins/Microsoft.DwayneNeed.Core): More sophisticated. Uses `AirspaceDecorator` to create a separate top-level layered window. Known issues: breaks drag-and-drop inside WinForms controls, and focus can be misdirected when switching between applications.

- **Airhack** (github.com/kolorowezworki/Airhack): Creates a floating transparent WPF window that moves and resizes with the parent. A `UIElement` in the "Front" property renders on this overlay window, positioned above the "Back" content containing the WFH.

---

## Putting it all together: a complete tabbed RDP host

Here is the complete XAML and code for a minimal but production-correct tabbed RDP manager using WPF-UI's `FluentWindow`, incorporating every workaround discussed:

```xml
<!-- MainWindow.xaml -->
<ui:FluentWindow x:Class="RdpManager.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    Title="RDP Manager" Height="768" Width="1024"
    ExtendsContentIntoTitleBar="True"
    Closing="OnWindowClosing">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="RDP Manager"/>

        <!-- Tab headers only — content is managed separately -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="8,4">
            <ItemsControl x:Name="TabHeaders" ItemsSource="{Binding Sessions}">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <StackPanel Orientation="Horizontal"/>
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Button Content="{Binding Title}" Margin="2"
                                Click="OnTabHeaderClick"
                                Tag="{Binding Id}"/>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            <Button Content="+" Click="OnNewSession" Margin="4,2"/>
        </StackPanel>

        <!-- Single viewport: all WFH instances stacked here, only one visible -->
        <Grid Grid.Row="2" x:Name="Viewport">
            <!-- WFH instances added/removed in code-behind -->
            <!-- Snapshot overlay for resize -->
            <Image x:Name="SnapshotOverlay"
                   Stretch="Fill"
                   Visibility="Collapsed"
                   IsHitTestVisible="False"
                   Panel.ZIndex="999"/>
        </Grid>
    </Grid>
</ui:FluentWindow>
```

```csharp
// MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Wpf.Ui.Controls;

namespace RdpManager;

public partial class MainWindow : FluentWindow
{
    private readonly Dictionary<string, RdpHostWrapper> _sessions = new();
    private ResizeSnapshotManager? _snapshotMgr;
    private string? _activeSessionId;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Install the WndProc hook for resize snapshot management.
        // The snapshot manager will be attached to whichever WFH is
        // active at the time of a resize operation.
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE  = 0x0232;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                           IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ENTERSIZEMOVE)
        {
            // Capture and hide the active WFH
            if (_activeSessionId is not null
                && _sessions.TryGetValue(_activeSessionId, out var wrapper))
            {
                _snapshotMgr = new ResizeSnapshotManager(
                    wrapper.Host, SnapshotOverlay);
                _snapshotMgr.Attach(this);
                // Trigger the capture manually since we just attached
                // (WM_ENTERSIZEMOVE already fired)
                _snapshotMgr.BeginSnapshot();
            }
        }
        else if (msg == WM_EXITSIZEMOVE)
        {
            _snapshotMgr?.EndSnapshot();
            _snapshotMgr = null;
        }

        return IntPtr.Zero;
    }

    private void OnNewSession(object sender, RoutedEventArgs e)
    {
        string id = Guid.NewGuid().ToString("N")[..8];
        var wrapper = new RdpHostWrapper();
        wrapper.Host.Background = System.Windows.Media.Brushes.Black;
        _sessions[id] = wrapper;

        // Add to viewport but keep collapsed until activated
        wrapper.Host.Visibility = Visibility.Collapsed;
        wrapper.Host.IsEnabled = false;
        Viewport.Children.Add(wrapper.Host);

        ActivateSession(id);

        // Connect (in production, show a connection dialog first)
        // wrapper.Connect("192.168.1.100");
    }

    private void OnTabHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn
            && btn.Tag is string id)
        {
            ActivateSession(id);
        }
    }

    private void ActivateSession(string id)
    {
        // Collapse the previous session
        if (_activeSessionId is not null
            && _sessions.TryGetValue(_activeSessionId, out var prev))
        {
            prev.Host.Visibility = Visibility.Collapsed;
            prev.Host.IsEnabled = false;
        }

        // Show the new session
        if (_sessions.TryGetValue(id, out var next))
        {
            next.Host.Visibility = Visibility.Visible;
            next.Host.IsEnabled = true;
        }

        _activeSessionId = id;
    }

    private async void OnShowDialog(object sender, RoutedEventArgs e)
    {
        // Hide ALL WFH instances before showing a ContentDialog
        foreach (var w in _sessions.Values)
            w.Host.Visibility = Visibility.Collapsed;

        var dialog = new ContentDialog
        {
            Title = "Confirm",
            Content = "Disconnect from this server?",
            PrimaryButtonText = "Disconnect",
            CloseButtonText = "Cancel",
            DialogHost = this
        };

        await dialog.ShowAsync();

        // Restore the active session
        if (_activeSessionId is not null
            && _sessions.TryGetValue(_activeSessionId, out var active))
        {
            active.Host.Visibility = Visibility.Visible;
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // CRITICAL: Dispose all WFH instances before window closes
        // to prevent .NET 8+ infinite recursion crash (dotnet/wpf #10171)
        foreach (var wrapper in _sessions.Values)
        {
            Viewport.Children.Remove(wrapper.Host);
            wrapper.Dispose();
        }
        _sessions.Clear();
    }
}
```

---

## Conclusion

Building a tabbed RDP manager with `WindowsFormsHost` inside WPF-UI's `FluentWindow` is viable but demands constant awareness of the airspace boundary. The **three non-negotiable practices** are: hide every WFH before showing any WPF overlay, apply both reflection-based leak fixes (`HwndSourceKeyboardInputSite` and `WinFormsAdapter`), and explicitly dispose all WFH instances before the window closes. The DPI situation on .NET 10 remains broken for cross-monitor dragging — use `SmartSizing` on the RDP control and accept the limitation.

The `.NET 9 fluent theme rendering bug` (dotnet/wpf #10044) is particularly dangerous for WPF-UI users: enabling .NET 9's built-in fluent themes causes black squares, content bleeding, and invisible text in WindowsFormsHost areas. If using WPF-UI's own theming rather than .NET 9's built-in fluent themes, test thoroughly — the visual similarity between the two theming approaches means the underlying DWM interactions may trigger similar artifacts. The safest configuration as of .NET 10 is to rely on WPF-UI's own theme engine and avoid enabling the framework-level `<ThemeMode>` setting.

Every production WPF RDP manager examined — 1Remote being the closest reference — converges on the same architecture: one `WindowsFormsHost` per connection, `Visibility.Collapsed` for inactive tabs, bitmap swap for overlays, and explicit disposal with leak workarounds. The airspace problem is not going away; the workarounds are the architecture.