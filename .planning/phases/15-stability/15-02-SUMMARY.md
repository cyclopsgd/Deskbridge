---
phase: 15-stability
plan: 02
subsystem: ui
tags: [wpf, airspace, windowsformshost, bitmap-snapshot, tab-switching, dispatcher]

requires:
  - phase: 05-multi-session
    provides: AirspaceSwapper with CaptureHwnd, RegisterHost, multi-host tab visibility
provides:
  - SnapshotSingleHost/ClearSingleHostSnapshot API on AirspaceSwapper for per-host tab-switch snapshot
  - Flicker-free tab switching via bitmap overlay covering Win32 repaint gap
affects: [rdp-session-management, tab-management, airspace-swapper]

tech-stack:
  added: []
  patterns: [bitmap-snapshot-tab-switch, dispatcher-priority-loaded-deferred-cleanup]

key-files:
  created: []
  modified:
    - src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs
    - src/Deskbridge/MainWindow.xaml.cs

key-decisions:
  - "Reuse existing CaptureHwnd + shared ViewportSnapshot overlay for tab-switch snapshots rather than per-host Image elements"
  - "Use DispatcherPriority.Loaded for deferred snapshot cleanup -- fires after layout + render, giving incoming HWND time to paint"
  - "SnapshotSingleHost does NOT hide the WFH -- caller controls visibility to avoid coupling snapshot logic with tab-switch logic"

patterns-established:
  - "SnapshotSingleHost/ClearSingleHostSnapshot: capture one host bitmap, show overlay, caller hides WFH, deferred cleanup"
  - "DispatcherPriority.Loaded for post-render callbacks covering Win32 HWND paint timing"

requirements-completed: [STAB-02]

duration: 2min
completed: 2026-04-22
---

# Phase 15 Plan 02: Eliminate Black Flash During Tab Switching Summary

**Bitmap snapshot overlay on AirspaceSwapper covers Win32 HWND repaint gap during tab switch, eliminating single-frame black flash**

## Performance

- **Duration:** 2 min
- **Started:** 2026-04-22T20:13:24Z
- **Completed:** 2026-04-22T20:15:36Z
- **Tasks:** 1
- **Files modified:** 2

## Accomplishments
- Added `SnapshotSingleHost` and `ClearSingleHostSnapshot` public methods to AirspaceSwapper for targeted per-host bitmap capture
- Refactored `SetActiveHostVisibility` in MainWindow to capture the outgoing tab's frame as a bitmap overlay before hiding, covering the async Win32 repaint gap
- Snapshot overlay cleared at `DispatcherPriority.Loaded` after the incoming WFH has painted, ensuring smooth visual transition
- No `Visibility.Hidden` used anywhere -- strictly Collapsed per xrdp/AxHost constraint

## Task Commits

Each task was committed atomically:

1. **Task 1: Add SnapshotSingleHost to AirspaceSwapper and use in tab switching** - `0bcbade` (feat)

## Files Created/Modified
- `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs` - Added SnapshotSingleHost/ClearSingleHostSnapshot methods for per-host tab-switch bitmap capture
- `src/Deskbridge/MainWindow.xaml.cs` - Refactored SetActiveHostVisibility to use snapshot overlay during tab switch

## Decisions Made
- Reused the existing shared `ViewportSnapshot` Image element and `CaptureHwnd` infrastructure rather than creating per-host overlay Images -- all hosts already register against the same Image via `RegisterHost`
- `SnapshotSingleHost` deliberately does NOT hide the WFH itself -- separation of concerns lets the caller (SetActiveHostVisibility) control visibility independently
- Used `DispatcherPriority.Loaded` (not `Render`) for the deferred cleanup because Loaded fires after both layout AND render passes complete, giving the incoming HWND sufficient time for its first WM_PAINT

## Deviations from Plan

None - plan executed exactly as written.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes introduced. The bitmap snapshot stays in-process memory and is nulled after one render pass (same risk profile as existing SnapshotAndHideAll for dialogs, per T-15-04 accept disposition). T-15-05 mitigated: each SnapshotSingleHost overwrites the previous overlay.Source, and ClearSingleHostSnapshot nulls it.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Tab switching now uses bitmap snapshot to cover the Win32 repaint gap
- Manual testing recommended: open 5+ RDP tabs, rapidly click through them to confirm no visible black flash
- Existing dialog airspace bypass (SnapshotAndHideAll/RestoreAll) unaffected -- SnapshotSingleHost operates independently

## Self-Check: PASSED

- All created/modified files exist on disk
- Commit `0bcbade` verified in git log
- SnapshotSingleHost/ClearSingleHostSnapshot present in both target files
- DispatcherPriority.Loaded present in MainWindow.xaml.cs

---
*Phase: 15-stability*
*Completed: 2026-04-22*
