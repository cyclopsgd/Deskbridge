# Phase 5 UAT — Drag-Reorder Visual Feedback

**Objective:** Confirm the D-13 drag-to-reorder flow shows the 2px accent
insertion line per UI-SPEC §Tab Bar Interaction Contract, reorders the `Tabs`
ObservableCollection via `Move` (not `Remove + Insert`, which would re-parent
the WFH and break D-04), and does not conflict with middle-click close or
left-click switch.

---

## Prerequisites

- [ ] Deskbridge built and running locally (`dotnet run --project src/Deskbridge`)
- [ ] 4+ tabs open (any real RDP targets — session identity is what matters,
  not content)

---

## 2px accent insertion line adorner

- [ ] Hold left mouse button on tab 2. Drag horizontally to the right past
  tab 3 but stop on its right-half. After moving past ~12px (3x system drag
  threshold), a **2px vertical line** appears on the **right edge** of tab 3.
  - Line is crisp, 2px thick, **accent blue** (#007ACC, matches active-tab
    accent border + status bar background + rail-selected indicator).
  - Line is 30px tall — full tab-bar height.
- [ ] Drag to the **left half** of tab 3. Line moves to the **left edge** of tab 3.
- [ ] Drag away from any tab (empty area past the last tab). No line shown —
  the drop-to-end path is silent.

---

## Drop semantics — Move, not Remove+Insert

- [ ] From the 4-tab setup, drop tab 2 on the **right half** of tab 3. Tabs
  end up in order: tab 1, tab 3, **tab 2**, tab 4 (tab 2 inserted AFTER tab 3).
- [ ] Reset (or start a fresh 4-tab setup). Drop tab 2 on the **left half** of
  tab 3. Tabs end up in order: tab 1, **tab 2**, tab 3, tab 4 — no change
  (dropping a tab immediately before its next neighbor is a no-op, which is
  expected behavior because oldIndex < newIndex triggers the newIndex-- self-
  removal adjustment, and the net delta is 0).
- [ ] From 4 tabs, drop tab 4 on the **left half** of tab 2. Tabs end up:
  tab 1, **tab 4**, tab 2, tab 3.

**Session integrity check** (D-04 persistent HostContainer invariant):

- [ ] After each drag-drop above, click into the RDP viewport of the dragged
  tab. The session is still **live** — cursor moves on the remote desktop.
  No disconnect, no "Connection lost" overlay, no session respawn.
- [ ] The viewport does NOT flicker / black out during the reorder. Because the
  reorder is an `ObservableCollection.Move` on the `Tabs` collection and the
  persistent `HostContainer` is untouched, the WFH does not re-parent and the
  HwndSource stays alive.

---

## ESC cancels drag

- [ ] Start dragging tab 2 past the threshold. While the 2px adorner is
  visible, press **ESC**. Drag is cancelled — tab order unchanged, adorner
  disappears immediately.
- [ ] Release the mouse off-screen (outside the tab bar). No-op — the drop is
  cancelled by the WPF drag system.

---

## No conflict with middle-click / left-click / right-click

- [ ] **Middle-click** on a tab (quick click, no movement past threshold).
  Tab closes — MouseBinding still fires. The drag-pending arm released on
  MouseUp without committing to a drag.
- [ ] **Left-click** on an inactive tab (quick click). Tab switches — LeftClick
  MouseBinding still fires. Active-tab accent border moves.
- [ ] **Right-click** on a tab. ContextMenu opens with Close / Close Others /
  Close All. Drag-reorder is not confused with right-click gesture.

---

## Rapid reorders + stress

- [ ] Rapid-fire drag tabs (4-5 reorders in 10 seconds). No crash, no freeze.
  Tab order is always what you dropped on. No memory leak visible in Task
  Manager (GDI handle count stable; process memory doesn't grow unboundedly).
- [ ] After rapid reorders, all tabs still respond to left-click switch and
  Ctrl+1..Ctrl+9 jump correctly to their **new positional index**.

---

## Cursor feedback

- [ ] During drag, cursor shows a **Hand** (drop-allowed) when over a valid
  target; shows a **No-drop** when over a non-target (e.g. empty space off
  the tab bar).

---

## Sign-off

**UAT completed by:** _____
**Date:** _____

**Outcome:** [ ] PASS  [ ] FAIL (describe below)

**Notes / failures:**

_____
