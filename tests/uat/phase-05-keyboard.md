# Phase 5 UAT — Keyboard Shortcuts with Live RDP

**Objective:** Validate Assumption A1 — `KeyboardHookMode=0` (set in Phase 4
`RdpConnectionConfigurator.cs`) is sufficient for WPF `PreviewKeyDown` to fire
for Ctrl+Tab while the AxHost has focus. If this fails, the Phase 6
`RegisterHotKey` global shortcut service must pull forward.

**Blocking gate:** the Ctrl+Tab + Ctrl+Shift+Tab lines below are the Assumption A1
gate. If they fail, mark the whole UAT "BLOCKED — A1 failure" and reply
"blocked — A1 failure, pull Phase 6 global shortcut service forward".

---

## Prerequisites

- [ ] Deskbridge built and running locally (`dotnet run --project src/Deskbridge`)
- [ ] 3 different real RDP targets accessible (localhost + 2 test VMs is fine;
  loopback RDP is acceptable if host RDP is enabled)
- [ ] Serilog log visible at `%AppData%/Deskbridge/logs/deskbridge-*.log`

---

## Ctrl+Tab / Ctrl+Shift+Tab — Assumption A1 gate (BLOCKING)

- [ ] Open 3 RDP connections sequentially. All 3 tabs are visible in the tab bar.
- [ ] Click INTO the RDP viewport of tab A so the AxHost has focus — you should
  see the remote cursor. Verify: type a letter and confirm the keystroke reaches
  the remote session (e.g. into a notepad / terminal on the remote host).
- [ ] **Ctrl+Tab** (with AxHost focused). Active tab changes from tab A → tab B.
  Tab B's viewport is now foreground.
- [ ] Switch back to tab A and confirm **no literal TAB character** was typed
  into the remote notepad / terminal when you Ctrl+Tab-switched away. The
  remote session must not receive the Tab keystroke.
- [ ] **Ctrl+Shift+Tab** (with AxHost focused). Active tab returns to tab A
  (reverse cycle).
- [ ] With 3+ tabs open, cycle all the way around with repeated Ctrl+Tab. Wraps
  first → last → first.
- [ ] Ctrl+Shift+Tab from the first tab wraps to the last tab.

**If any of the above fail:** Open `%AppData%/Deskbridge/logs/deskbridge-*.log`
and confirm `KeyboardHookMode=0` is logged at connect time. If the log shows
hook mode 0 but Ctrl+Tab still doesn't fire WPF `PreviewKeyDown`, escalate to
Phase 6 global shortcut service (RegisterHotKey-based) pulling forward —
write "blocked — A1 failure" and attach the failing log line.

---

## Ctrl+W — close active tab

- [ ] With 3 tabs open, focus the RDP viewport of the active tab. Press
  **Ctrl+W**. Active tab closes. The session disconnects cleanly (Disconnect
  pipeline runs — no hung process; check Task Manager for leftover mstsc /
  Deskbridge children).
- [ ] With only 1 tab open and focused, press **Ctrl+W**. Tab closes; empty-state
  placeholder ("Deskbridge" / "Ctrl+N to create a connection") reappears.

---

## Ctrl+F4 — close tab alias

- [ ] With ≥1 tab open and RDP viewport focused, press **Ctrl+F4**. Active tab
  closes, identical behavior to Ctrl+W.

---

## Ctrl+1 … Ctrl+8 — jump to N-th tab

- [ ] Open 5 tabs. Focus RDP viewport of tab 3. Press **Ctrl+1**. Jump to tab 1.
- [ ] Press **Ctrl+4**. Jump to tab 4.
- [ ] Press **Ctrl+8** with only 5 tabs open. No-op (out of range — the key is
  handled but nothing changes). No crash.

---

## Ctrl+9 — jump to LAST tab (Chrome / VS Code convention)

- [ ] Open 5 tabs. Press **Ctrl+9**. Jump to tab 5 (the LAST tab, not literally
  the ninth). This is the Chrome / VS Code convention — Ctrl+9 always hits the
  last tab even when there are fewer than 9 tabs.
- [ ] Open 12 tabs (open 12 if feasible; 5 is acceptable if 12 isn't practical
  given your test VMs). Press **Ctrl+9**. Jump to tab 12 (the LAST).

---

## Ctrl+Shift+T — reopen last closed tab (LRU)

- [ ] Open 4 tabs. Close tab 4 (by middle-click, Ctrl+W, or context menu).
  Press **Ctrl+Shift+T**. Tab 4 reopens (new pipeline run — a fresh session
  is created, not the original one).
- [ ] Close tabs 3, then 2, then 1. Press **Ctrl+Shift+T** three times. Tabs
  reopen in reverse-close order (newest-first pop: tab 1 reopens first, then 2,
  then 3).
- [ ] **Empty LRU silent no-op:** restart the app (fresh start, empty LRU).
  Press **Ctrl+Shift+T** with no tabs ever closed. **Expect:** nothing happens.
  No crash, no Snackbar, no beep, no log error. The LRU is empty; the shortcut
  is a silent no-op per UI-SPEC §Copywriting line 345.
- [ ] **Deleted-connection silent no-op:** close a tab, then delete that
  connection from the tree. Press **Ctrl+Shift+T**. **Expect:** nothing happens.
  The Guid was popped but the store returned null; the handler silently drops
  the request. No error.

---

## Cross-feature interactions

- [ ] Ctrl+Tab followed immediately by Ctrl+W closes the newly-focused tab (not
  the previously-focused one) — shortcut chain works.
- [ ] Click Connections in the icon rail (panel opens), then Ctrl+Tab. The tab
  cycles correctly even when the panel is open — `PreviewKeyDown` handler at
  FluentWindow root level is reached regardless of focus inside the panel.
- [ ] Focus a text field in the Connection editor dialog. Ctrl+Tab in the
  dialog does NOT cycle app tabs — dialog handles Ctrl+Tab first (standard
  WPF focus routing).

---

## Sign-off

**UAT completed by:** _____
**Date:** _____
**Outcome:** [ ] PASS  [ ] FAIL (describe below)  [ ] BLOCKED — A1 failure

**Notes / failures:**

_____

---

*If Ctrl+Tab fails with RDP focused despite `KeyboardHookMode=0` verified in
logs, reply "blocked — A1 failure, pull Phase 6 global shortcut service
forward" to the orchestrator.*
