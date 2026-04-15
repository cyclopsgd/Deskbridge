# Phase 6 UAT — Auto-lock idle timer (SEC-03, Pitfall 4, Pitfall 6)

**Scope:** validate the `DispatcherTimer`-driven inactivity auto-lock plus the
Pitfall 6 `WindowsFormsHost`-ancestor filter (D-14: Deskbridge activity only
resets the timer; typing into an active RDP session must NOT reset it).
Documents the Pitfall 4 caveat (DispatcherTimer doesn't compensate for sleep).

**Plan reference:** `.planning/phases/06-cross-cutting-features/06-04-PLAN.md`

**Prerequisites:**
- The same working RDP target used for `phase-06-security.md` §3.
- Master password already set (run `phase-06-security.md` §1 first).

---

## 1. Deskbridge-side input resets the timer (SEC-03 happy path)

1. Unlock Deskbridge. Open the settings panel (cog icon in the left rail).
2. Set **Auto-lock timeout** to **1 minute**. The `NumberBox` persists on LostFocus — click elsewhere to commit.
3. **Verify persistence:** open `%AppData%/Deskbridge/settings.json`. The `security.autoLockTimeoutMinutes` field should be `1`.
4. Click the Connections icon to close the Settings panel.
5. Move the mouse / type into a search field every 10 seconds for 90 seconds.
6. **Expect:** Deskbridge does NOT lock. The timer resets on every Deskbridge-side input event.

---

## 2. Idle → auto-lock (SEC-03 happy path)

1. With `AutoLockTimeoutMinutes=1`, stop touching the keyboard/mouse. Wait 65 seconds without interacting with Deskbridge.
2. **Expect:**
   - After approximately 60 seconds of inactivity, the lock overlay appears automatically.
   - `audit-YYYY-MM.jsonl` gets a new `"type":"AppLocked","errorCode":"Timeout"` line.
3. Unlock.

---

## 3. RDP-session typing does NOT reset the Deskbridge idle timer (D-14 + Pitfall 6) — CRITICAL

This step validates the Pitfall 6 `FindAncestor<WindowsFormsHost>` filter in `IdleLockService`.

1. With `AutoLockTimeoutMinutes=1`, open an RDP connection. Wait for it to establish.
2. Click INTO the remote desktop so the RDP session has keyboard focus.
3. Type continuously into the remote session for 70 seconds (e.g. hold a letter key so keystrokes flow to the remote desktop continuously).
4. **Expect:**
   - Approximately 60 seconds after your LAST Deskbridge-side click (step 2), the lock overlay appears EVEN THOUGH you've been typing into the RDP session.
   - `audit-YYYY-MM.jsonl` gets a new `"type":"AppLocked","errorCode":"Timeout"` line.
5. **Regression failure mode:** if Deskbridge does NOT lock (because the RDP-session keystrokes incorrectly reset its idle timer), Pitfall 6 is broken. Investigate `IdleLockService.HandleInputFromSource` — the `FindAncestor<WindowsFormsHost>` walk is failing to identify the AxHost ancestor.

---

## 4. Lock-on-minimise (SEC-05 / D-19)

1. Open the Settings panel. Enable **Lock on minimise** (toggle switch).
2. Verify `%AppData%/Deskbridge/settings.json` has `security.lockOnMinimise: true`.
3. Close the Settings panel.
4. Minimise the Deskbridge window (Win+Down or title-bar minimise).
5. Restore the window.
6. **Expect:**
   - The lock overlay is visible (the lock fired on minimise via the `OnStateChanged` handler publishing `AppLockedEvent(Minimise)`).
   - `audit-YYYY-MM.jsonl` contains a new `"type":"AppLocked","errorCode":"Minimise"` line.
7. Unlock.

## 4a. Lock-on-minimise disabled (D-19 default-off)

1. Open Settings, disable **Lock on minimise**.
2. Minimise the window.
3. Restore.
4. **Expect:** NO lock overlay. Deskbridge returns to its prior state.

---

## 5. Pitfall 4 caveat — DispatcherTimer after Windows sleep (documented, ACCEPTED)

**This is documentation, not a regression test** — the behaviour is an ACCEPT disposition on the threat register (T-06-SEC-05).

1. With `AutoLockTimeoutMinutes=5`, leave Deskbridge idle.
2. Sleep the laptop (close the lid OR Start → Sleep).
3. Wake the laptop after more than 5 minutes.
4. **Observed behaviour (NOT a regression):**
   - The `DispatcherTimer` did NOT fire during sleep and does NOT compensate for missed time on wake. Without other mitigation, Deskbridge would NOT be locked.
   - In practice, Windows' SessionSwitch `SessionLock` event fires when the machine goes to sleep on most corporate-policy machines (§4 above). Our `SessionLockService` subscribes and DOES lock Deskbridge on sleep.
   - If your test machine does NOT auto-lock Windows on sleep (non-corporate policy), Deskbridge may be unlocked on wake. Documented as acceptable in the threat register and this UAT.

**Future-enhancement note:** if stricter behaviour is needed, subscribe to `SystemEvents.PowerModeChanged` and restart the timer with `Interval = remaining` on resume. Out of v1 scope.

---

## Sign-off

- [ ] Deskbridge-side input resets the timer (§1)
- [ ] Idle → auto-lock after timeout (§2)
- [ ] RDP-session typing does NOT reset the timer — **Pitfall 6** (§3) — CRITICAL
- [ ] Lock on minimise fires when enabled (§4)
- [ ] Lock on minimise does NOT fire when disabled (§4a)
- [ ] Pitfall 4 caveat understood + accepted (§5)

**Pass condition:** §1–4a pass. §5 is documentation.

**Tester:** ___________________________ **Date:** ____________________
