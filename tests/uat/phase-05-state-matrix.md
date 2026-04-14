# Phase 5 UAT — TabState Mutual Exclusion + Background Reconnect UX (D-12, D-14)

**Objective:** Confirm exactly one of `ProgressRing` / amber dot / red dot is
visible per tab at any given moment (D-12 mutual exclusion). Indicator must
surface on **background tabs** too — that's the entire point of D-12 so users
can see a dropped session without switching to it. D-14 specifies **no
auto-switch focus** on background drops; the badge is the only notification
channel in Phase 5.

---

## Prerequisites

- [ ] Deskbridge built and running locally (`dotnet run --project src/Deskbridge`)
- [ ] A real RDP target that can be intentionally disconnected (unplug network,
  block via firewall, or kill the session server-side)
- [ ] A second working RDP target for comparison

---

## Connecting — ProgressRing indicator

- [ ] Open a new connection. **Expect in the tab label:** a 12px WPF-UI
  ProgressRing spinning next to the title. No amber dot. No red dot.
- [ ] ProgressRing is left of the title by 8px (UI-SPEC §Spacing Scale).
- [ ] Tab tooltip (hover over the tab, wait ~400ms):
  `{Hostname} · Connecting…`
  - Middle-dot separator (U+00B7), not a hyphen.
  - Single-character ellipsis (U+2026), not `...`.

---

## Connected — no indicator (neutral)

- [ ] Wait for connection to complete (typically ≤5s). **Expect:**
  - ProgressRing disappears.
  - No dot appears.
  - Title becomes **SemiBold** (FontWeight=SemiBold) — that's the active-tab
    indicator.
  - Top 2px accent border appears on the tab.
- [ ] Tooltip: `{Hostname} · {Width}×{Height}` (e.g. `myserver · 1920×1080`
  with U+00D7 multiplication sign).
- [ ] Status bar shows `{Hostname} · Connected` on the left and `{W} × {H}` on
  the right.

---

## Background Connecting indicator (D-12 key requirement)

- [ ] Open a second connection. **Immediately switch back** to tab A by
  left-click (or Ctrl+Tab) before tab B finishes connecting.
- [ ] **Expect in tab B (now inactive):** the ProgressRing is **still
  spinning**. Background-tab visibility of the indicator is the whole point of
  D-12 — without it, connecting progress on an inactive tab is invisible.
- [ ] When tab B finishes connecting, the ProgressRing clears automatically.

---

## Reconnecting — amber dot (Background reconnect UX, D-14)

- [ ] With 2 established connections (tab A active), **kill connection B's
  network** (unplug cable, block firewall, or kill the session server-side).
- [ ] **Stay focused on tab A**. Do NOT switch to tab B.
- [ ] Within ~2 seconds of the disconnect, **expect in tab B's label** (while
  still inactive): an **8px amber Ellipse** (color `#FFCC02`, the
  `DeskbridgeWarningBrush`) to the left of the title. The amber dot appears
  even though tab B is inactive — D-12 invariant.
- [ ] **Expect NO auto-switch.** Focus stays on tab A. D-14 explicitly rejects
  auto-focus-switch on background drops.
- [ ] Hover over tab B. Tooltip: `{Hostname} · Reconnecting attempt {N}/20`
  (attempt number populates from the backoff loop).
- [ ] Switch to tab B (left-click). The ReconnectOverlay is now visible inside
  the tab's viewport (the overlay was parented inside HostContainer during the
  drop, Collapsed while the tab was inactive, now Visible on switch).
- [ ] Restore connection B's network. **Expect:** the backoff loop succeeds,
  amber dot disappears, overlay auto-closes, session is live, tab title goes
  back to no-indicator + SemiBold (Connected).

---

## Error — red dot (retry cap hit)

- [ ] Kill connection B's network again. Let the auto-retry loop exhaust
  **all 20 attempts** (takes a few minutes with 30s cap). Alternative: if you
  can force an **auth failure** server-side, that also triggers Error state
  immediately.
- [ ] **Expect:** amber dot transitions to a **red dot** (color `#F44747`, the
  `DeskbridgeErrorBrush`). The red dot is 8px, same size as the amber.
- [ ] Hover over tab B. Tooltip:
  `{Hostname} · Connection failed — click tab to reconnect`
  (U+2014 em-dash in "failed — click", not a hyphen).
- [ ] Switch to tab B. Overlay shows the **manual Reconnect** button (no
  countdown — the loop exhausted).
- [ ] Click Reconnect in the overlay. A new ConnectingEvent fires; tab label
  goes through the state machine again (red dot → ProgressRing → Connected).

---

## Mutual exclusion — the critical invariant

Visually inspect **each tab during every transition above**:

- [ ] At NO point does a tab show both amber AND red.
- [ ] At NO point does a tab show ProgressRing AND amber.
- [ ] At NO point does a tab show ProgressRing AND red.
- [ ] At NO point does a tab show a ProgressRing + dot + title all at once
  (only 0 or 1 indicator is visible at any moment).
- [ ] When a tab is in `Connected` state, no dot of any kind is visible —
  the SemiBold title + accent border communicate activity + selection
  orthogonally.

---

## Close + reopen round-trip

- [ ] Close tab B (right-click → Close). The red-dot state is discarded.
- [ ] Open a fresh connection to B (fix the network first). **Expect:** the
  new tab starts in `Connecting` (ProgressRing) and progresses normally — the
  Error state does not leak across tab instances.

---

## Status bar mirror

Throughout the transitions:

- [ ] The status bar string (bottom of window) matches the **active tab's**
  state per UI-SPEC §Status Bar Binding Contract:
  - `Ready` when no tab is active
  - `{Hostname} · Connecting…` + `—` on the right
  - `{Hostname} · Connected` + `{W} × {H}` on the right
  - `{Hostname} · Reconnecting attempt {N}/20` + `{W} × {H}` on the right
  - `{Hostname} · Disconnected` + `—` on the right

---

## Sign-off

**UAT completed by:** _____
**Date:** _____

**Outcome:** [ ] PASS  [ ] FAIL (describe below)

**Notes / failures:**

_____
