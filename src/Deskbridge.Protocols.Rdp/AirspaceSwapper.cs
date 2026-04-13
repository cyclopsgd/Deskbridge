using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// Disambiguate between System.Drawing.Image (pulled via UseWindowsForms) and WPF's Image control.
using Image = System.Windows.Controls.Image;

namespace Deskbridge.Protocols.Rdp;

/// <summary>
/// Handles the WPF airspace problem for <see cref="WindowsFormsHost"/>-hosted RDP content.
/// Captures a bitmap snapshot via <c>PrintWindow(PW_CLIENTONLY)</c> when the user begins
/// a drag/resize (<c>WM_ENTERSIZEMOVE</c>), hides the WFH, shows the snapshot, and
/// restores on <c>WM_EXITSIZEMOVE</c>. Also provides a "hide-only" mode via
/// <see cref="HideWithoutSnapshot"/> for the Plan 04-03 reconnect overlay (D-07).
///
/// <para>Reference implementation copied from WINFORMS-HOST-AIRSPACE.md §ResizeSnapshotManager
/// (lines 196-342) with the additional multi-host registration + <c>HideWithoutSnapshot</c>
/// token API per plan D-13/D-14.</para>
///
/// <para>Thread-safety: all public members must be invoked on the STA UI thread.</para>
/// </summary>
public sealed class AirspaceSwapper : IDisposable
{
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const uint PW_CLIENTONLY = 0x00000001;

    private readonly Dictionary<WindowsFormsHost, Image> _hosts = new();
    private readonly List<HwndSource> _hookedSources = new();
    private readonly ILogger<AirspaceSwapper> _logger;
    private bool _inSizeMove;
    private bool _disposed;

    public AirspaceSwapper() : this(NullLogger<AirspaceSwapper>.Instance) { }

    public AirspaceSwapper(ILogger<AirspaceSwapper> logger)
    {
        _logger = logger;
    }

    public void AttachToWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ObjectDisposedException.ThrowIf(_disposed, this);
        AssertDispatcher();

        var source = (HwndSource?)PresentationSource.FromVisual(window)
            ?? HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        if (source is null) return;

        // Idempotent: don't double-hook the same HwndSource.
        if (_hookedSources.Contains(source)) return;

        source.AddHook(WndProc);
        _hookedSources.Add(source);
    }

    public void RegisterHost(WindowsFormsHost host, Image overlay)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(overlay);
        ObjectDisposedException.ThrowIf(_disposed, this);
        AssertDispatcher();

        overlay.Visibility = Visibility.Collapsed;
        overlay.IsHitTestVisible = false;
        overlay.Stretch = System.Windows.Media.Stretch.Fill;
        _hosts[host] = overlay;
    }

    public void UnregisterHost(WindowsFormsHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        AssertDispatcher();
        _hosts.Remove(host);
    }

    /// <summary>
    /// Hides the given WFH without capturing a bitmap snapshot. Returns an
    /// <see cref="IDisposable"/> whose <see cref="IDisposable.Dispose"/> restores
    /// <see cref="UIElement.Visibility"/> to <see cref="Visibility.Visible"/>.
    /// Used by the Plan 04-03 reconnect overlay (D-07) — session is already gone, so
    /// no snapshot is meaningful.
    /// </summary>
    public IDisposable HideWithoutSnapshot(WindowsFormsHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        ObjectDisposedException.ThrowIf(_disposed, this);
        AssertDispatcher();

        var previous = host.Visibility;
        host.Visibility = Visibility.Collapsed;
        return new RestoreVisibilityToken(host, previous);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var src in _hookedSources)
        {
            try { src.RemoveHook(WndProc); } catch { /* best-effort */ }
        }
        _hookedSources.Clear();
        _hosts.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ENTERSIZEMOVE && !_inSizeMove)
        {
            _inSizeMove = true;
            foreach (var (host, overlay) in _hosts)
            {
                var snapshot = CaptureHwnd(host);
                if (snapshot is not null)
                {
                    overlay.Source = snapshot;
                    overlay.Visibility = Visibility.Visible;
                }
                // Use Collapsed (not Hidden) — Hidden has been observed to cause the
                // hosted AxHost child HWND to be torn down on some servers (e.g. xrdp),
                // which raises OnDisconnected with discReason=2 (exDiscReasonAPIInitiatedLogoff)
                // and ends the live RDP session. Collapsed removes the WFH from the
                // layout pass without destroying its child HWND, so the AxRdp session
                // (and its keep-alive ping stream) survives the drag/resize gesture.
                host.Visibility = Visibility.Collapsed;
            }
            _logger.LogDebug("[airspace] ENTERSIZEMOVE: snapshot taken, WFH visibility -> Collapsed (hosts={Count})", _hosts.Count);
        }
        else if (msg == WM_EXITSIZEMOVE && _inSizeMove)
        {
            _inSizeMove = false;
            foreach (var (host, overlay) in _hosts)
            {
                host.Visibility = Visibility.Visible;
                overlay.Visibility = Visibility.Collapsed;
                overlay.Source = null;
            }
            _logger.LogDebug("[airspace] EXITSIZEMOVE: snapshot hidden, WFH visibility -> Visible (hosts={Count})", _hosts.Count);
        }
        return IntPtr.Zero;
    }

    private static BitmapSource? CaptureHwnd(WindowsFormsHost host)
    {
        var child = host.Child;
        if (child is null || !child.IsHandleCreated) return null;

        IntPtr hwnd = child.Handle;
        if (!GetClientRect(hwnd, out RECT rc) || rc.Width <= 0 || rc.Height <= 0) return null;

        IntPtr hdcWin = IntPtr.Zero, hdcMem = IntPtr.Zero, hBmp = IntPtr.Zero;
        try
        {
            hdcWin = GetDC(hwnd);
            if (hdcWin == IntPtr.Zero) return null;

            hdcMem = CreateCompatibleDC(hdcWin);
            if (hdcMem == IntPtr.Zero) return null;

            hBmp = CreateCompatibleBitmap(hdcWin, rc.Width, rc.Height);
            if (hBmp == IntPtr.Zero) return null;

            var hOld = SelectObject(hdcMem, hBmp);
            var ok = PrintWindow(hwnd, hdcMem, PW_CLIENTONLY);
            SelectObject(hdcMem, hOld);

            if (!ok) return null;

            var bmpSrc = Imaging.CreateBitmapSourceFromHBitmap(
                hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmpSrc.Freeze();
            return bmpSrc;
        }
        finally
        {
            if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
            if (hdcMem != IntPtr.Zero) DeleteDC(hdcMem);
            if (hdcWin != IntPtr.Zero) ReleaseDC(hwnd, hdcWin);
        }
    }

    private static void AssertDispatcher()
    {
        if (!Dispatcher.CurrentDispatcher.CheckAccess())
        {
            throw new InvalidOperationException("AirspaceSwapper must be invoked on the UI dispatcher thread.");
        }
    }

    private sealed class RestoreVisibilityToken : IDisposable
    {
        private readonly WindowsFormsHost _host;
        private readonly Visibility _previous;
        private bool _disposed;
        public RestoreVisibilityToken(WindowsFormsHost host, Visibility previous)
        {
            _host = host;
            _previous = previous;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _host.Visibility = _previous == Visibility.Collapsed ? Visibility.Visible : _previous;
        }
    }

    // --- P/Invoke ------------------------------------------------------------

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
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }
}
