# Phase 16: RDP Quality - Research

**Researched:** 2026-04-19
**Domain:** RDP ActiveX resolution/DPI management, WindowsFormsHost layout sizing
**Confidence:** MEDIUM-HIGH

## Summary

Phase 16 addresses two user-facing RDP visual quality issues: blurry sessions (STAB-03) and a grey border around the VM viewport (STAB-04). The blur problem stems from the current implementation using `SmartSizing=true` with a fallback 1920x1080 desktop resolution. When the remote desktop is rendered at 1920x1080 but the viewport control is a different pixel size (due to DPI scaling or window sizing), mstscax.dll stretches the bitmap via StretchBlt, producing visible interpolation artifacts. The fix is to measure the viewport's actual pixel dimensions (accounting for DPI), set `DesktopWidth`/`DesktopHeight` to match at connection time, set DPI scale factors via `IMsRdpExtendedSettings`, and optionally use `UpdateSessionDisplaySettings` to adjust resolution dynamically after resize. The grey border issue is most likely caused by a DPI rounding mismatch between WPF's device-independent pixel layout and the WindowsFormsHost's hardware pixel sizing, or by the RDP control's desktop resolution not filling the control bounds.

**Primary recommendation:** Set DesktopWidth/DesktopHeight to the viewport's physical pixel dimensions (using `TransformToDevice` to convert from DIPs), set DesktopScaleFactor/DeviceScaleFactor via `IMsRdpExtendedSettings` before connect, and call `UpdateSessionDisplaySettings` on resize to dynamically adjust the remote resolution without reconnecting.

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| STAB-03 | User sees RDP sessions rendered at native resolution matching their monitor DPI, reducing blur compared to mRemoteNG | Resolution matching via viewport pixel measurement, DPI scale factor properties, dynamic resolution updates |
| STAB-04 | User sees no grey border around the VM viewport on work machines (investigation -- may be Group Policy) | WindowsFormsHost sizing analysis, DPI rounding audit, RDP control fill behavior investigation |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- **COM/ActiveX**: Classic aximp.exe interop only -- no GeneratedComInterface, no Marshal.ReleaseComObject, site before configure
- **Airspace**: No WPF elements may overlap the RDP viewport (WinForms/ActiveX always renders on top)
- **UseWindowsForms**: Set ONLY in Deskbridge.Protocols.Rdp csproj
- **Framework**: .NET 10 LTS (net10.0-windows) with C# 14
- **Mandatory reading**: RDP-ACTIVEX-PITFALLS.md and WINFORMS-HOST-AIRSPACE.md before any RDP changes
- **No Co-Authored-By**: Do not add Co-Authored-By lines to commit messages

## Architecture Patterns

### Current Architecture (What Exists)

The current RDP display pipeline:

1. `RdpConnectionConfigurator.Apply()` sets `DesktopWidth`/`DesktopHeight` from `ConnectionModel.DisplaySettings` with fallback to 1920x1080
2. `SmartSizing` defaults to `true` -- mstscax.dll stretches the remote desktop bitmap to fit the control
3. No DPI scale factors are set (`DesktopScaleFactor`/`DeviceScaleFactor` not configured)
4. No dynamic resolution updates on window resize -- the resolution is fixed at connect time
5. `WindowsFormsHost` is parented into `HostContainer` (a plain `Grid`) inside `ViewportGrid`

```
MainWindow.xaml visual tree (relevant viewport section):
  Border (Grid.Row="1", Background=ApplicationBackgroundBrush)
    Grid x:Name="ViewportGrid"
      StackPanel (empty state -- Deskbridge logo + "New Connection" button)
      Image x:Name="ViewportSnapshot" (airspace swap -- Collapsed by default)
      Grid x:Name="HostContainer"
        WindowsFormsHost (Background=Black, Tag=ConnectionId)
          AxMsRdpClient9NotSafeForScripting
```

### Root Cause Analysis: STAB-03 (Blur)

The blur is caused by a **resolution mismatch between the requested desktop and the display area**:

1. `DesktopWidth=1920, DesktopHeight=1080` is requested at connect time [VERIFIED: RdpConnectionConfigurator.cs line 35-36]
2. The actual viewport pixel dimensions depend on window size, DPI scaling, sidebar state, and tab bar height
3. On a 1920x1080 monitor at 100% DPI: viewport is approximately 1620x618 pixels (after sidebar, tab bar, title bar, status bar deductions)
4. On a 2560x1440 monitor at 125% DPI: viewport pixel dimensions differ further
5. `SmartSizing=true` causes mstscax.dll to StretchBlt the 1920x1080 bitmap into the smaller control area
6. StretchBlt uses bilinear interpolation which produces noticeable blur on text and UI elements

**mRemoteNG comparison:** mRemoteNG defaults to "FitToWindow" mode which sets `DesktopWidth`/`DesktopHeight` to match the control's actual pixel size, then uses `SmartSizing=false`. This produces a 1:1 pixel mapping with no scaling blur. When the window resizes, mRemoteNG PR #2188 added dynamic resolution support via `UpdateSessionDisplaySettings` to change the remote resolution without reconnecting. [CITED: github.com/mRemoteNG/mRemoteNG/pull/2188, github.com/mRemoteNG/mRemoteNG/issues/1546]

### Root Cause Analysis: STAB-04 (Grey Border)

Multiple potential causes, ordered by likelihood:

1. **DPI rounding mismatch (MOST LIKELY):** WPF uses device-independent pixels (DIPs) for layout. `WindowsFormsHost` converts DIPs to hardware pixels during `ArrangeOverride`. At non-100% DPI scaling, `double`-to-`int` rounding can produce a 1-pixel gap between the WFH's physical HWND size and the WPF layout slot. This gap reveals the parent `Border`'s `ApplicationBackgroundBrush` (which is a dark background, but may appear as grey depending on the theme token). [CITED: learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/layout-considerations-for-the-windowsformshost-element]

2. **RDP desktop smaller than control:** If `DesktopWidth`/`DesktopHeight` are smaller than the control's pixel dimensions AND `SmartSizing=false`, the remote desktop does not fill the control -- the remaining area shows as the control's background color (typically grey from the WinForms default `SystemColors.Control`). With `SmartSizing=true` this should not happen since the bitmap stretches to fill.

3. **Group Policy restricting resolution:** Some enterprise policies limit remote session resolution. If the server negotiates a lower resolution than requested, and SmartSizing stretches that smaller bitmap, the border may be a rounding artifact at the edges.

4. **WindowsFormsHost not filling its container:** The HostContainer Grid has no explicit sizing constraints. WFH should stretch to fill by default (HorizontalAlignment/VerticalAlignment default to Stretch). However, if the AxHost child reports a preferred size smaller than the available space, WFH may honor that smaller size. [VERIFIED: RdpHostControl.cs line 104 -- no alignment override on WFH]

### Recommended Architecture: Resolution Matching

**Strategy: "Match viewport pixels, then dynamic-resize"**

```
Connect-time:
  1. Measure ViewportGrid.ActualWidth/ActualHeight (DIPs)
  2. Convert to physical pixels using PresentationSource.CompositionTarget.TransformToDevice
  3. Set DesktopWidth/DesktopHeight to those physical pixel values
  4. Set DesktopScaleFactor/DeviceScaleFactor via IMsRdpExtendedSettings
  5. Set SmartSizing = false (1:1 pixel mapping, no stretch)
  6. Connect

Post-connect resize:
  1. Hook ViewportGrid.SizeChanged (debounced ~500ms)
  2. Re-measure physical pixel dimensions
  3. Call UpdateSessionDisplaySettings to change remote resolution
  4. OR: toggle SmartSizing=true as a fallback if the server doesn't support dynamic resolution
```

### Pattern 1: Viewport Pixel Measurement

**What:** Convert WPF DIP dimensions to physical pixels for the monitor the window is on.
**When to use:** Before every RDP connection, and after window resize/DPI change.

```csharp
// Source: Microsoft Learn - PresentationSource / TransformToDevice
// [CITED: learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware]
public static (int Width, int Height) GetPhysicalPixelSize(FrameworkElement element)
{
    var source = PresentationSource.FromVisual(element);
    if (source?.CompositionTarget == null)
        return ((int)element.ActualWidth, (int)element.ActualHeight);

    var transform = source.CompositionTarget.TransformToDevice;
    var pixelWidth = (int)Math.Round(element.ActualWidth * transform.M11);
    var pixelHeight = (int)Math.Round(element.ActualHeight * transform.M22);

    // RDP requires minimum 200x200 and maximum 8192x8192
    pixelWidth = Math.Clamp(pixelWidth, 200, 8192);
    pixelHeight = Math.Clamp(pixelHeight, 200, 8192);

    return (pixelWidth, pixelHeight);
}
```

### Pattern 2: DPI Scale Factor Calculation

**What:** Calculate DesktopScaleFactor and DeviceScaleFactor from the current monitor DPI.
**When to use:** Before RDP connection, set via `IMsRdpExtendedSettings`.

```csharp
// Source: Devolutions blog + Microsoft Learn IMsRdpExtendedSettings
// [CITED: devolutions.net/blog/smart-resizing-and-high-dpi-issues-in-remote-desktop-manager/]
// [CITED: learn.microsoft.com/en-us/windows/win32/termserv/imsrdpextendedsettings-property]
public static (uint DesktopScale, uint DeviceScale) GetScaleFactors(double dpiPercent)
{
    // DesktopScaleFactor: valid 100-500, mapped from DPI percentage
    uint desktopScale = dpiPercent switch
    {
        <= 100 => 100,
        <= 125 => 125,
        <= 150 => 150,
        <= 175 => 175,
        <= 200 => 200,
        _ => Math.Min((uint)dpiPercent, 500)
    };

    // DeviceScaleFactor: valid values are 100, 140, 180 only
    uint deviceScale = desktopScale switch
    {
        <= 100 => 100,
        <= 199 => 140,
        _ => 180
    };

    return (desktopScale, deviceScale);
}
```

### Pattern 3: Setting Extended Properties Before Connect

**What:** Use `IMsRdpExtendedSettings` from `GetOcx()` to set DPI scale factors.
**When to use:** After siting, before `Connect()`.

```csharp
// Source: Microsoft Learn - IMsRdpExtendedSettings
// [CITED: learn.microsoft.com/en-us/windows/win32/termserv/imsrdpextendedsettings-property]
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
        // Some older mstscax.dll versions may not support these properties
        // Log and continue -- the connection will still work, just without DPI awareness
        Log.Warning("Failed to set extended property {Name}: {Type} HResult=0x{HResult:X8}",
            name, ex.GetType().Name, ex.HResult);
    }
}

// Usage in RdpConnectionConfigurator:
// SetExtendedProperty(rdp, "DesktopScaleFactor", desktopScale);
// SetExtendedProperty(rdp, "DeviceScaleFactor", deviceScale);
```

**Critical note:** `IMsRdpExtendedSettings` may NOT be present in the aximp-generated interop assemblies. The interface `IID_IMsRdpExtendedSettings` is `302D8188-0052-4807-806A-362B628F9AC5`. If it is missing from AxMSTSCLib/MSTSCLib, it must be declared manually via a COM interface definition or accessed via `dynamic`/reflection on the OCX. [ASSUMED]

### Pattern 4: Dynamic Resolution on Resize

**What:** Call `UpdateSessionDisplaySettings` to change remote resolution without reconnecting.
**When to use:** After the window is resized and the user stops dragging (debounced).

```csharp
// Source: IMsRdpClient9 interface
// [CITED: learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclient9-syncsessiondisplaysettings]
// [ASSUMED: UpdateSessionDisplaySettings method availability via interop]
public void OnViewportResized(int newPixelWidth, int newPixelHeight,
                              uint desktopScale, uint deviceScale)
{
    if (_rdp?.Connected == 0) return;

    try
    {
        // IMsRdpClient9.UpdateSessionDisplaySettings signature:
        // HRESULT UpdateSessionDisplaySettings(
        //   ULONG ulDesktopWidth, ULONG ulDesktopHeight,
        //   ULONG ulPhysicalWidth, ULONG ulPhysicalHeight,
        //   ULONG ulOrientation,
        //   ULONG ulDesktopScaleFactor, ULONG ulDeviceScaleFactor)
        var client9 = (IMsRdpClient9)_rdp.GetOcx();
        client9.UpdateSessionDisplaySettings(
            (uint)newPixelWidth, (uint)newPixelHeight,
            (uint)newPixelWidth, (uint)newPixelHeight,
            0, // orientation: 0 = landscape
            desktopScale, deviceScale);
    }
    catch (COMException ex)
    {
        // Server may not support dynamic resolution (RDP 8.0 or older, xrdp)
        // Fall back to SmartSizing=true for this session
        _rdp.AdvancedSettings9.SmartSizing = true;
    }
}
```

**Timing constraint:** `UpdateSessionDisplaySettings` cannot be called immediately after `Connect()`. It requires the RDP session to be fully established (after `OnLoginComplete`). Calling it too early throws `E_FAIL`. [CITED: devolutions.net/blog/smart-resizing-and-high-dpi-issues-in-remote-desktop-manager/]

### Pattern 5: Grey Border Fix

**What:** Ensure the RDP control fills its container completely with no gap.
**When to use:** At WindowsFormsHost creation and on resize.

```csharp
// Ensure WFH fills the entire layout slot:
_host = new WindowsFormsHost
{
    Background = System.Windows.Media.Brushes.Black,
    HorizontalAlignment = HorizontalAlignment.Stretch,
    VerticalAlignment = VerticalAlignment.Stretch,
    Margin = new Thickness(0),
};

// Ensure the AxHost child fills the WFH:
// WindowsFormsHost automatically sets Child.Dock = DockStyle.Fill
// But verify after siting:
_rdp.Dock = System.Windows.Forms.DockStyle.Fill;
```

### Anti-Patterns to Avoid

- **Setting DesktopWidth/Height to 0:** AxMsRdpClient9 defaults to 0x0 which renders a black viewport even when the session is live. Always set non-zero values. [VERIFIED: RdpConnectionConfigurator.cs comment line 32]
- **Calling UpdateSessionDisplaySettings before OnLoginComplete:** Throws E_FAIL or silently fails. Must wait until login is complete. [CITED: devolutions.net blog]
- **Using SmartSizing=true with matched resolution:** If DesktopWidth/Height already matches the control pixel size, SmartSizing=true still causes a StretchBlt pass (1:1 stretch), which is a wasted operation and may introduce sub-pixel artifacts on some GPU drivers. Use SmartSizing=false when resolution is matched.
- **Hardcoding 1920x1080:** The current fallback. Produces blur on any viewport that is not exactly 1920x1080 pixels.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| DPI-aware pixel measurement | Custom P/Invoke GetDpiForWindow | `PresentationSource.CompositionTarget.TransformToDevice` | WPF provides this natively, correctly handles per-monitor DPI |
| Resize debouncing | Manual timer management | `DispatcherTimer` with 500ms interval, reset on each SizeChanged | Standard WPF pattern, avoids flooding UpdateSessionDisplaySettings |
| Extended property setting | Raw COM interface declaration | Cast `GetOcx()` to `IMsRdpExtendedSettings` (or `dynamic` if interface not in interop) | Microsoft's documented approach |

## Common Pitfalls

### Pitfall 1: IMsRdpExtendedSettings Not in Interop Assembly

**What goes wrong:** The aximp-generated interop DLLs may not include `IMsRdpExtendedSettings` because it was added in a later RDP SDK version than what was used to generate them.
**Why it happens:** aximp generates interfaces from the type library embedded in the specific mstscax.dll version used at generation time.
**How to avoid:** Check if the interface exists in the interop assembly. If not, declare it manually:

```csharp
[ComImport, Guid("302D8188-0052-4807-806A-362B628F9AC5")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IMsRdpExtendedSettings
{
    [DispId(1)]
    object get_Property([MarshalAs(UnmanagedType.BStr)] string bstrPropertyName);

    [DispId(1)]
    void set_Property(
        [MarshalAs(UnmanagedType.BStr)] string bstrPropertyName,
        [In] ref object pValue);
}
```

**Warning signs:** `InvalidCastException` when casting `GetOcx()` to `IMsRdpExtendedSettings`.

### Pitfall 2: WPF SizeChanged Fires Before Layout Is Complete

**What goes wrong:** `ViewportGrid.SizeChanged` fires during initial layout before `PresentationSource` is available, causing `TransformToDevice` to be null.
**Why it happens:** WPF fires SizeChanged during MeasureOverride/ArrangeOverride passes.
**How to avoid:** Guard with null check on `PresentationSource.FromVisual(element)` and only process after `Window.Loaded`.

### Pitfall 3: DPI Changes on Monitor Drag

**What goes wrong:** When the window is dragged from a 100% DPI monitor to a 150% DPI monitor, the viewport pixel dimensions change but the RDP session resolution does not update.
**Why it happens:** `WindowsFormsHost` DPI scaling is broken in .NET 6+ (dotnet/wpf #6294). The child HWND physical size does NOT change when DPI changes.
**How to avoid:** Hook `Window.DpiChanged` event and call `UpdateSessionDisplaySettings` with the new physical pixel dimensions. This is a best-effort mitigation since WFH DPI scaling is fundamentally broken.
**Warning signs:** Blurry text after moving window between monitors. [CITED: github.com/dotnet/wpf/issues/6294]

### Pitfall 4: UpdateSessionDisplaySettings Timing

**What goes wrong:** Calling `UpdateSessionDisplaySettings` too early (before OnLoginComplete or during initial negotiation) causes `E_FAIL` (0x80004005).
**Why it happens:** The RDP control needs an active session with a fully negotiated display channel.
**How to avoid:** Only call after `OnLoginComplete` fires. Add a boolean `_loginComplete` flag to `RdpHostControl` and check it before any dynamic resolution calls.
**Warning signs:** COMException with HResult 0x80004005 during resize.

### Pitfall 5: xrdp and Non-Windows RDP Servers

**What goes wrong:** Non-Windows RDP servers (xrdp, FreeRDP server) may not support dynamic resolution or DPI scale factors.
**Why it happens:** Dynamic resolution via `UpdateSessionDisplaySettings` requires the RDP 8.1 Display Update Virtual Channel Extension (MS-RDPEDISP). xrdp has partial support.
**How to avoid:** Wrap `UpdateSessionDisplaySettings` in a try/catch. On failure, fall back to `SmartSizing=true` for that session. Track the fallback per-session so resize events do not repeatedly attempt and fail.

### Pitfall 6: DesktopWidth/Height Only Settable Before Connect

**What goes wrong:** `DesktopWidth` and `DesktopHeight` properties throw if set while `Connected != 0`.
**Why it happens:** These are negotiation-time properties baked into the RDP connection request PDU.
**How to avoid:** Only set them in `RdpConnectionConfigurator.Apply()` before `_rdp.Connect()`. For post-connect resolution changes, use `UpdateSessionDisplaySettings` exclusively.

## Code Examples

### Complete Resolution Matching Flow

```csharp
// Source: Combination of Microsoft docs + Devolutions approach
// [CITED: learn.microsoft.com/en-us/windows/win32/termserv/imsrdpextendedsettings-property]
// [CITED: devolutions.net/blog/smart-resizing-and-high-dpi-issues-in-remote-desktop-manager/]

// In RdpConnectionConfigurator.Apply() -- before Connect:
public static void Apply(AxMsRdpClient9NotSafeForScripting rdp, ConnectionContext ctx,
                          int viewportPixelWidth, int viewportPixelHeight,
                          double dpiPercent)
{
    // ... existing property sets ...

    // STAB-03: Set desktop size to match viewport physical pixels
    rdp.DesktopWidth = viewportPixelWidth;
    rdp.DesktopHeight = viewportPixelHeight;
    rdp.AdvancedSettings9.SmartSizing = false; // 1:1 pixel mapping

    // Set DPI scale factors via extended settings
    var (desktopScale, deviceScale) = GetScaleFactors(dpiPercent);
    SetExtendedProperty(rdp, "DesktopScaleFactor", (uint)desktopScale);
    SetExtendedProperty(rdp, "DeviceScaleFactor", (uint)deviceScale);
}
```

### Debounced Resize Handler

```csharp
// In MainWindow.xaml.cs or a dedicated ResizeManager service:
private DispatcherTimer? _resizeTimer;

private void ViewportGrid_SizeChanged(object sender, SizeChangedEventArgs e)
{
    _resizeTimer?.Stop();
    _resizeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
    _resizeTimer.Tick -= OnResizeSettled;
    _resizeTimer.Tick += OnResizeSettled;
    _resizeTimer.Start();
}

private void OnResizeSettled(object? sender, EventArgs e)
{
    _resizeTimer?.Stop();
    var (w, h) = GetPhysicalPixelSize(ViewportGrid);
    // Forward to the active RdpHostControl's resize handler
    // Only if login is complete and session supports dynamic resolution
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Fixed 1920x1080 + SmartSizing=true | Viewport-matched resolution + SmartSizing=false | RDP 8.1 (2013) | Eliminates stretch blur |
| Reconnect on resize | UpdateSessionDisplaySettings (dynamic) | RDP 8.1 / IMsRdpClient9 | Seamless resize without session interruption |
| No DPI awareness | DesktopScaleFactor + DeviceScaleFactor via IMsRdpExtendedSettings | Windows 8+ | Remote session honors client DPI |
| mRemoteNG fixed resolution | mRemoteNG PR #2188 dynamic resolution | March 2022 | mRemoteNG now supports dynamic resize |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `IMsRdpExtendedSettings` interface may not be in the aximp-generated interop DLLs | Pattern 3, Pitfall 1 | Would need manual COM interface declaration -- minor code addition |
| A2 | `UpdateSessionDisplaySettings` is accessible via the aximp-generated `AxMsRdpClient9NotSafeForScripting` interop or requires casting to `IMsRdpClient9` | Pattern 4 | If not in interop, need manual COM declaration or dynamic invocation |
| A3 | The grey border on "work machines" is a DPI rounding artifact, not Group Policy | Root Cause STAB-04 | If Group Policy, no code fix possible -- document as known limitation |
| A4 | xrdp partial support for dynamic resolution -- fallback to SmartSizing needed | Pitfall 5 | Could break non-Windows connections if not handled |

## Open Questions

1. **Are IMsRdpExtendedSettings and IMsRdpClient9.UpdateSessionDisplaySettings in the interop DLLs?**
   - What we know: The interop DLLs were generated via aximp.exe against a specific mstscax.dll version
   - What's unclear: Which version of mstscax.dll was used, and whether it includes these newer interfaces
   - Recommendation: During plan execution, check the interop assembly for these interfaces. If missing, declare them manually as COM imports. This is a task prerequisite -- attempt the cast first, add manual declarations if needed.

2. **Is the grey border reproducible on the developer's machine or only on "work machines"?**
   - What we know: User reports grey border on work machines specifically
   - What's unclear: Whether this is DPI-related, Group Policy, or specific to certain Windows builds
   - Recommendation: First fix the resolution matching (which eliminates the most common cause). If grey border persists, add diagnostic logging for WFH actual dimensions vs layout slot dimensions. Document Group Policy as out-of-scope if code changes don't help.

3. **Does the current AxMsRdpClient9 interop expose Reconnect() or only Connect()?**
   - What we know: The code currently uses `Connect()` and `Disconnect()`
   - What's unclear: Whether `Reconnect()` is available for session recovery after resolution changes fail
   - Recommendation: Use `UpdateSessionDisplaySettings` (no reconnect needed). If that fails, fall back to SmartSizing rather than reconnecting.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit (project uses Deskbridge.Tests) |
| Config file | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| Quick run command | `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~Rdp" -x` |
| Full suite command | `dotnet test tests/Deskbridge.Tests` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| STAB-03 | Viewport pixel measurement returns correct physical pixels | unit | `dotnet test --filter "FullyQualifiedName~ViewportPixel" -x` | Wave 0 |
| STAB-03 | Scale factor calculation returns valid RDP values | unit | `dotnet test --filter "FullyQualifiedName~ScaleFactor" -x` | Wave 0 |
| STAB-03 | DesktopWidth/Height set to viewport dimensions, not 1920x1080 | unit | `dotnet test --filter "FullyQualifiedName~ResolutionMatch" -x` | Wave 0 |
| STAB-04 | WindowsFormsHost fills layout slot with no gap | manual-only | Visual inspection at different DPI levels | N/A |

### Sampling Rate
- **Per task commit:** `dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~Rdp" -x`
- **Per wave merge:** `dotnet test tests/Deskbridge.Tests`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/Deskbridge.Tests/Rdp/ScaleFactorCalculationTests.cs` -- covers STAB-03 DPI mapping
- [ ] `tests/Deskbridge.Tests/Rdp/ViewportMeasurementTests.cs` -- covers STAB-03 pixel conversion (may need mock PresentationSource)

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | N/A |
| V3 Session Management | no | N/A |
| V4 Access Control | no | N/A |
| V5 Input Validation | yes | Clamp DesktopWidth/Height to valid RDP ranges (200-8192) |
| V6 Cryptography | no | N/A |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Integer overflow in pixel calculations | Tampering | Math.Clamp to valid RDP range before passing to COM |
| COM exception information disclosure | Information Disclosure | Log type + HResult only, never exception message (existing pattern) |

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn: IMsRdpExtendedSettings Property](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpextendedsettings-property) -- Complete property table with DesktopScaleFactor (VT_UI4, 100-500) and DeviceScaleFactor (VT_UI4, 100/140/180)
- [Microsoft Learn: Layout Considerations for WindowsFormsHost](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/layout-considerations-for-the-windowsformshost-element) -- DIP-to-pixel conversion, rounding behavior, sizing algorithm
- [dotnet/wpf #6294](https://github.com/dotnet/wpf/issues/6294) -- WindowsFormsHost DPI scaling broken in .NET 6+, still open
- [Codebase: RdpConnectionConfigurator.cs](src/Deskbridge.Protocols.Rdp/RdpConnectionConfigurator.cs) -- Current SmartSizing=true, 1920x1080 fallback
- [Codebase: RdpHostControl.cs](src/Deskbridge.Protocols.Rdp/RdpHostControl.cs) -- WFH creation, Connect flow
- [Codebase: MainWindow.xaml](src/Deskbridge/MainWindow.xaml) -- ViewportGrid/HostContainer layout structure

### Secondary (MEDIUM confidence)
- [Devolutions Blog: Smart Resizing and High DPI](https://devolutions.net/blog/smart-resizing-and-high-dpi-issues-in-remote-desktop-manager/) -- DesktopScaleFactor/DeviceScaleFactor implementation, SetExtendedProperty pattern, valid value mappings
- [mRemoteNG PR #1933: Fix high DPI scaling](https://github.com/mRemoteNG/mRemoteNG/pull/1933) -- DesktopScaleFactor/DeviceScaleFactor calculation fix
- [mRemoteNG PR #2188 / Issue #1546: Dynamic resolution](https://github.com/mRemoteNG/mRemoteNG/issues/1546) -- UpdateSessionDisplaySettings implementation

### Tertiary (LOW confidence)
- [mRemoteNG Issue #1828: SmartSizing and Resolution](https://github.com/mRemoteNG/mRemoteNG/issues/1828) -- Community discussion on SmartSizing vs fixed resolution tradeoffs

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no new libraries needed, all changes within existing AxMSTSCLib/MSTSCLib interop and WPF APIs
- Architecture: MEDIUM-HIGH -- viewport measurement and scale factor patterns are well-documented; dynamic resolution via UpdateSessionDisplaySettings needs interop verification
- Pitfalls: HIGH -- DPI scaling bugs in WindowsFormsHost are well-documented (.NET issue trackers), COM interface availability is the main risk
- Grey border (STAB-04): MEDIUM -- most likely DPI rounding, but Group Policy remains a possibility that cannot be fixed in code

**Research date:** 2026-04-19
**Valid until:** 2026-05-19 (stable domain, no fast-moving dependencies)
