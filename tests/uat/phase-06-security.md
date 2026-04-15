# Phase 6 UAT — App security (SEC-01 / SEC-02 / SEC-04, Pitfall 5 airspace)

**Scope:** requirements that cannot be covered by unit tests — the lock overlay
visual chrome, the Pitfall 5 airspace guarantee (RDP pixels MUST NOT leak through
the lock overlay), the real Windows `SessionSwitch` trigger, and the first-run
setup flow with disk persistence.

**Plan reference:** `.planning/phases/06-cross-cutting-features/06-04-PLAN.md`

**Prerequisites:**
- A working RDP target for step 3 (e.g. a local Hyper-V VM, any on-network host).
- Local admin on the test machine so Windows+L round-trips cleanly.
- Delete `%AppData%/Deskbridge/auth.json` before starting to exercise the first-run path.

---

## 1. First-run master-password setup (SEC-01)

1. **Preparation:** delete `%AppData%/Deskbridge/auth.json` if present.
2. Launch Deskbridge from the IDE (F5) or a built binary.
3. **Expect:** the lock overlay appears in first-run mode.
   - Two `PasswordBox` fields visible (master password + confirm).
   - Button text reads **"Set Password"**.
   - Body copy reads verbatim: *"Set a master password to protect your connections. This password cannot be recovered — choose something memorable."*
   - No title-bar "X" close button. No Primary/Secondary/Close button row below the card.
   - Background is fully opaque — no shell chrome (connections panel, RDP tabs, status bar, title bar) is visible behind the dialog.
4. Type a **7-character** password in both fields. Click Set Password.
   - **Expect:** error message *"Password must be at least 8 characters."* (red text below the confirm field). `auth.json` is NOT written.
5. Type an **8+ character** password in the first field; type a DIFFERENT 8+ char password in Confirm. Click Set Password.
   - **Expect:** error message *"Passwords do not match."* `auth.json` is NOT written.
6. Type matching 8+ char passwords in both fields. Click Set Password.
   - **Expect:** the lock overlay dismisses. The main app becomes interactive.
   - **Verify on disk:** `%AppData%/Deskbridge/auth.json` exists; its `passwordHash` field starts with `v1.` and contains two dot-separated base64 segments.
   - **Audit:** `%AppData%/Deskbridge/audit-YYYY-MM.jsonl` contains a new line with `"type":"AppLocked","errorCode":"Manual"` then a `"type":"AppUnlocked"` line — the startup lock flow.

---

## 2. Return-user unlock (SEC-02)

1. Close Deskbridge cleanly (File → Exit or Alt+F4).
2. Relaunch.
3. **Expect:** the lock overlay appears in unlock mode.
   - Single `PasswordBox` (no Confirm field).
   - Button text reads **"Unlock"**.
   - Body copy reads: *"Locked. Enter your master password to continue."*
   - Same opaque chrome rules as step 1.3.
4. Type the wrong password. Click Unlock (or press Enter).
   - **Expect:** error message *"Incorrect password. Try again."* The password field is cleared and re-focused.
5. Type the correct password. Press **Enter** (Pitfall 8 — Enter must invoke Unlock, not a phantom PrimaryButton).
   - **Expect:** the overlay dismisses. The app is interactive.

---

## 3. Lock overlay airspace (SEC-02 / Pitfall 5 Option A) — CRITICAL

This step validates the single most important security invariant of Phase 6.

1. With the app unlocked, open an RDP connection. Wait for it to establish and render the remote desktop.
2. Press **Ctrl+L**.
3. **Expect:**
   - The lock overlay appears. The `WindowsFormsHost` hosting the RDP AxHost is **collapsed** — no RDP pixels are visible through or behind the overlay.
   - The card is opaque, fully covering the viewport.
4. Enter the correct master password. Click Unlock (or press Enter).
5. **Expect:**
   - The overlay dismisses.
   - The RDP session is **still connected** — the previously-visible tab becomes visible again with the session still active (scroll should work, keyboard should type into the remote desktop, etc.). Pitfall 5 Option A mandates sessions stay connected in the background during lock; only visibility flipped.
6. **Regression failure mode:** if you see even a single-pixel-strip of the RDP desktop bleeding through the lock overlay, STOP and file a bug. The `ContentDialogSmokeFill` override + the `AppLockController.CaptureAndCollapseHosts` method is broken.

---

## 4. Windows session-lock auto-trigger (SEC-04, Pattern 9 + Pitfall 7)

1. Unlock Deskbridge. Bring focus to another app (e.g. Notepad) so Deskbridge is NOT the focused window.
2. Press **Windows+L** to lock Windows.
3. Wait 2 seconds, then unlock Windows.
4. Switch back to Deskbridge.
5. **Expect:**
   - Deskbridge now shows its own lock overlay (unlock mode).
   - No cross-thread `InvalidOperationException` crash dialog (Pitfall 7 — the `SessionSwitch` event fires on a dedicated non-UI thread; `SessionLockService` marshals via `Dispatcher.BeginInvoke` so WPF DependencyObjects don't blow up).
   - `%AppData%/Deskbridge/audit-YYYY-MM.jsonl` contains a new line with `"type":"AppLocked","errorCode":"SessionSwitch"`.
6. Enter the correct master password. Unlock.

---

## 5. Repeat-lock regression (D-18 idempotency)

1. Unlock Deskbridge.
2. Press **Ctrl+L** rapidly 5 times in a row.
3. **Expect:**
   - Exactly ONE lock overlay is shown (not 5 stacked dialogs).
   - `audit-YYYY-MM.jsonl` gets exactly ONE new `AppLocked` record (not 5).
   - After unlocking, a subsequent Ctrl+L press still shows the overlay (the idempotency only suppresses re-locks while ALREADY locked).

---

## Sign-off

- [ ] First-run setup + validation errors + success + disk write (§1)
- [ ] Unlock mode + wrong password error + clear + focus + Enter-to-unlock (§2)
- [ ] Pitfall 5 airspace — **no RDP pixels visible through overlay** (§3) — CRITICAL
- [ ] SessionSwitch auto-trigger + Dispatcher marshal + audit record (§4)
- [ ] D-18 idempotent repeat Ctrl+L (§5)

**Pass condition:** all 5 sections pass. Any RDP-pixel leak in §3 blocks the plan.

**Tester:** ___________________________ **Date:** ____________________
