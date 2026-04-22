---
phase: 16-rdp-quality
plan: 01
subsystem: rdp-protocols
tags: [rdp, resolution, dpi, smart-sizing, viewport, dynamic-resize]
dependency_graph:
  requires: []
  provides: [viewport-measurement, dpi-scale-factors, dynamic-resize, resolution-matching]
  affects: [rdp-connection-flow, main-window-resize, rdp-host-lifecycle]
tech_stack:
  added: []
  patterns: [COM-interface-manual-declaration, viewport-pixel-measurement, debounced-resize, SmartSizing-fallback]
key_files:
  created:
    - src/Deskbridge.Protocols.Rdp/Interop/IMsRdpExtendedSettings.cs
    - src/Deskbridge.Protocols.Rdp/ViewportMeasurement.cs
    - tests/Deskbridge.Tests/Rdp/ScaleFactorCalculationTests.cs
    - tests/Deskbridge.Tests/Rdp/ViewportMeasurementTests.cs
  modified:
    - src/Deskbridge.Protocols.Rdp/RdpConnectionConfigurator.cs
    - src/Deskbridge.Protocols.Rdp/RdpHostControl.cs
    - src/Deskbridge/MainWindow.xaml.cs
decisions:
  - Used manual COM interface declaration for IMsRdpExtendedSettings since it is not in aximp-generated interop DLLs
  - Viewport dimensions flow via RdpHostControl.SetViewportDimensions -> ConnectAsync -> ConnectionContext.Properties -> RdpConnectionConfigurator (avoids modifying Core interfaces)
  - SmartSizing=false when viewport-matched; SmartSizing=true only as fallback for hardcoded 1920x1080
  - UpdateSessionDisplaySettings called directly on AxMsRdpClient9 wrapper (method exists in interop assembly)
  - _dynamicResizeFailed flag disables dynamic resize permanently per-session after first failure (xrdp compat)
metrics:
  duration: 8min
  completed: 2026-04-22
  tasks: 1
  files: 7
---

# Phase 16 Plan 01: Resolution Matching and Dynamic Resize Summary

Viewport-matched RDP resolution with DPI scale factor injection via IMsRdpExtendedSettings, dynamic resize via UpdateSessionDisplaySettings with 500ms debounce, and grey border elimination via DockStyle.Fill + Stretch alignment.

## What Was Done

### Task 1: Resolution matching infrastructure and connect-time pixel measurement (TDD)

**RED:** Created failing tests for ViewportMeasurement (ScaleFactorCalculationTests + ViewportMeasurementTests) covering all DPI-to-scale-factor mappings, ClampDesktopDimension range enforcement, and GetDpiPercent conversion.

**GREEN:** Implemented all production code:

1. **IMsRdpExtendedSettings** -- Manual COM interface declaration (GUID 302D8188-0052-4807-806A-362B628F9AC5) for DesktopScaleFactor/DeviceScaleFactor properties not present in aximp-generated interop DLLs.

2. **ViewportMeasurement** -- Static helper with GetPhysicalPixelSize (DIP-to-pixel via TransformToDevice), GetDpiPercent (scale factor to percentage), GetScaleFactors (DPI% to DesktopScale/DeviceScale pair), and ClampDesktopDimension (enforces [200, 8192] range).

3. **RdpConnectionConfigurator** -- Reads ViewportPixelWidth/Height from ConnectionContext.Properties. Sets DesktopWidth/Height to viewport physical pixels instead of 1920x1080. SmartSizing=false when viewport-matched. DPI scale factors set via SetExtendedProperty helper with COMException/InvalidCastException fallback.

4. **RdpHostControl** -- Added SetViewportDimensions (called by MainWindow before ConnectAsync), viewport dimension injection into ConnectionContext.Properties, _loginComplete gate for dynamic resize, _dynamicResizeFailed SmartSizing fallback, UpdateResolution public method, DockStyle.Fill on AxHost, and Stretch alignment on WindowsFormsHost.

5. **MainWindow** -- Measures ViewportGrid physical pixels in OnHostMounted after UpdateLayout. Passes dimensions to RdpHostControl via SetViewportDimensions. Debounced SizeChanged handler (500ms DispatcherTimer) calls UpdateResolution on active RDP host.

## Test Results

- **New tests:** 14 (8 ScaleFactorCalculation + 6 ViewportMeasurement)
- **Full suite:** 615 passed, 0 failed, 3 skipped
- **Build:** 0 errors, 0 warnings

## Commits

| Commit | Type | Description |
|--------|------|-------------|
| 80da8e0 | test | Add failing tests for scale factor and viewport measurement (RED) |
| 71fd2e3 | fix | Restore research file lost during worktree reset |
| 0b66361 | feat | Implement RDP resolution matching and dynamic resize (GREEN) |

## Deviations from Plan

None -- plan executed exactly as written.

## Known Stubs

None -- all code paths are fully wired.

## Threat Mitigations Applied

| Threat ID | Mitigation | File |
|-----------|-----------|------|
| T-16-01 | Math.Clamp to [200, 8192] on all pixel dimensions before COM | ViewportMeasurement.cs |
| T-16-02 | SetExtendedProperty logs type + HResult only, never ex.Message | RdpConnectionConfigurator.cs |
| T-16-03 | 500ms debounce timer + _dynamicResizeFailed flag | MainWindow.xaml.cs, RdpHostControl.cs |

## Self-Check: PASSED

All 7 files verified present on disk. All 3 commits verified in git log.
