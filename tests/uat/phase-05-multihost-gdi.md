# Phase 5 UAT — Multi-Host GDI + Drag-Resize + 15-Session Snackbar

**Objective:** Confirm three invariants across a multi-host workload:
(a) GDI handle baseline returns to ±50 of initial after a 14-host open+close
cycle (Phase 4 gate carried forward to multi-host); (b) drag-resize snapshot
works cleanly with multiple hosts; (c) 15-session Snackbar fires exactly once
per 14→15 crossing (D-09, D-10, TAB-04).

---

## Prerequisites

- [ ] Deskbridge built and running locally (`dotnet run --project src/Deskbridge`)
- [ ] **Process Explorer** (or Resource Monitor) open, **GDI Objects** column
  visible for the Deskbridge process. (View → Select Columns → Process Image →
  GDI Objects in Process Explorer.)
- [ ] RDP target that can accept ≥15 simultaneous sessions (localhost is fine
  with multiple RDP listener configurations, or a lab Windows Server with
  `fSingleSessionPerUser=0`)

---

## Baseline GDI count

- [ ] App started, no tabs open. Record baseline GDI handle count:
  **Baseline = _____**

---

## 14-host open + close (GDI leak gate, carried forward from Phase 4)

- [ ] Open 14 RDP sessions sequentially (via double-click in the tree, or
  Connect context-menu). Wait ~5s between each so the pipeline completes.
  Record GDI count after the 14th session fully loads:
  **Open-14 = _____**
- [ ] Close **all 14 tabs** via right-click → **Close All** (one operation).
  Wait 10-15s for sequential disconnect pipeline to drain each host and for
  the coordinator to release COM references.
- [ ] Record GDI count after Close All settles:
  **After-close = _____**
- [ ] **Expected:** `After-close` is within ±50 of `Baseline`. If drift is
  larger, GDI is leaking — FAIL and attach Process Explorer handle details.

- [ ] Repeat the open-14 → close-all cycle **once more** for a second data point.
  Record:
  **After-2nd-close = _____**
- [ ] **Expected:** `After-2nd-close` is within ±100 of `Baseline`. Two-cycle
  drift should stay bounded.

---

## 15-session Snackbar (D-09, D-10, TAB-04) — fire-once semantics

- [ ] From a clean state, open 14 RDP sessions. **Expected:** no Snackbar at 14.
- [ ] Open the **15th** session. **Expect:** a Snackbar appears at bottom-right
  with:
  - Title: `Approaching session limit`
  - Message: `15 active sessions reached — performance may degrade beyond this point.`
  - Appearance: Caution (yellow/amber)
  - Auto-dismiss: ~6 seconds
  - Dismissable: clicking X closes it immediately
- [ ] Open the 16th session. **Expect:** NO Snackbar re-fire. (Once per
  crossing — subsequent opens at higher counts do not re-fire.)
- [ ] Open the 17th session. **Expect:** still NO Snackbar.
- [ ] Close sessions down to **12** (3 below the 15 threshold). The threshold
  re-arms when count drops below 15.
- [ ] Open sessions back up to 15. **Expect:** Snackbar fires a **second time**
  (re-arm semantics).

---

## Drag-resize snapshot across multi-host (D-04 + Phase 4 D-13 carry-over)

- [ ] With 3-4 RDP sessions open, grab the window edge and resize.
- [ ] **Expected during drag:** the active tab's viewport shows a bitmap snapshot
  (via `ViewportSnapshot` Image). No flicker. No black rectangle. No "tearing"
  between the snapshot and the live WFH.
- [ ] Release the drag. Live rendering resumes on the active tab.
- [ ] Switch to a background tab. **Expected:** the background tab does NOT
  show a stale snapshot — its WFH resumed Collapsed visibility after the
  drag-resize ended (WR-06 fix in Plan 01 AirspaceSwapper).
- [ ] Maximize/restore the window several times. Active session stays live.
  Background sessions remain disconnected-invisible (Collapsed, IsEnabled=false).

---

## Window close — D-08 sequential drain

- [ ] Open 5 RDP sessions.
- [ ] Click the window's close button (X in titlebar).
- [ ] **Expected:** the app exits cleanly within ~5-10 seconds. No hung
  `Deskbridge.exe` process in Task Manager.
- [ ] Open the most recent log file at `%AppData%/Deskbridge/logs/deskbridge-*.log`.
  **Expect:** 5 sequential `IDisconnectPipeline.DisconnectAsync` / stage
  completion entries in chronological order (D-08 sequential drain; parallel
  disconnect was explicitly rejected).

---

## Sign-off

**UAT completed by:** _____
**Date:** _____
**GDI baseline:** _____ · **Open-14:** _____ · **After-close:** _____ · **After-2nd-close:** _____

**Outcome:** [ ] PASS  [ ] FAIL (describe below)

**Notes / failures:**

_____
