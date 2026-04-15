# Phase 6 UAT — Crash dialog (LOG-04 completion, D-11)

**Scope:** the `CrashDialog` UI + clipboard flow + restart flow cannot be
meaningfully unit-tested (clipboard requires a message pump + Process.Start
spawns a real process). This UAT covers the end-to-end behaviour.

**Plan reference:** `.planning/phases/06-cross-cutting-features/06-04-PLAN.md`

**Prerequisites:**
- A built Deskbridge binary (Debug or Release) — the `Process.Start(MainModule.FileName)` restart path needs a real on-disk exe.
- Master password set (run `phase-06-security.md` §1 first).

---

## 1. Trigger a crash dialog

A dedicated "crash trigger" command is not shipped in v1 (intentional — users shouldn't be able to crash the app on demand). For the UAT we add a temporary trigger in a branch, then REMOVE it before merging.

**Temporary trigger (add to `MainWindow.xaml.cs` `OnPreviewKeyDown`):**

```csharp
// TEMPORARY UAT TRIGGER — remove before committing. Ctrl+F12 throws on the
// dispatcher so CrashHandler.OnDispatcherUnhandled fires → TryShowCrashDialog.
if (e.Key == Key.F12 && Keyboard.Modifiers == ModifierKeys.Control)
{
    throw new InvalidOperationException(
        "UAT crash test — Password=hunter2 (should not appear in log, should not appear in dialog clipboard)");
}
```

Build + run. Unlock. Press **Ctrl+F12**.

### Expected dialog appearance

1. A modal `ContentDialog` is shown.
2. **Title** reads verbatim: *"Deskbridge encountered an unexpected error"*.
3. **Body** reads verbatim: *"An unexpected error occurred. A log file with details has been saved to your AppData folder. You can copy the error details to clipboard for support, or restart the application."*
4. **NO stack trace** is visible in the dialog body.
5. The button row shows:
   - Left / Primary: **"Copy Details"** (Secondary appearance — muted)
   - Right / Close: **"Restart"** (Primary appearance — accent-coloured)

---

## 2. Copy Details → clipboard

1. Click **Copy Details**.
2. **Expect:**
   - The button label transforms to **"Copied"** for approximately 2 seconds, then reverts to **"Copy Details"**.
   - The dialog stays OPEN (does NOT dismiss — we cancel the close for Primary via the `Closing` event).
3. Open Notepad. Paste (Ctrl+V).
4. **Expect the paste content to contain:**
   - `System.InvalidOperationException: UAT crash test ...`
   - A stack trace including `MainWindow.OnPreviewKeyDown`.
5. **Expect the paste content to NOT contain:**
   - The raw password from the exception message — the paste IS the raw exception message (no `RedactSensitivePolicy` applied to the clipboard payload; Serilog-side redaction only covers the log file). Document this in the sign-off; callers are expected to scrub before sharing with support.

**Note:** Plan 06-01's `RedactSensitivePolicy` redacts `Password`/`Secret`/`Token`-property values when Serilog destructures POCOs. Exception messages are free-form strings and are NOT automatically redacted — this is intentional (we can't parse English for secrets).

---

## 3. Restart

1. Click **Restart**.
2. **Expect:**
   - Deskbridge closes (`Application.Current.Shutdown()`).
   - A fresh Deskbridge process launches (`Process.Start(Process.GetCurrentProcess().MainModule.FileName)`).
   - On relaunch, the lock overlay appears (unlock mode — the SEC-02 startup flow re-runs).
3. Unlock.

### Verification

- **Process list:** Task Manager should show Deskbridge.exe running with a fresh PID (different from pre-crash).
- **Log file:** `%AppData%/Deskbridge/logs/deskbridge-YYYYMMDD.log` should contain a FATAL entry from `OnDispatcherUnhandled` recording the `UAT crash test` exception. The FATAL log entry should NOT contain the raw password (Plan 06-01 RedactSensitivePolicy is expected to scrub `Password` property values in any POCO we log — but free-form exception messages flow through untouched; document observed behaviour).

---

## 4. Cleanup

1. **REMOVE the temporary Ctrl+F12 trigger** from `MainWindow.xaml.cs`.
2. Rebuild and sanity-check that Ctrl+F12 no longer throws.
3. Commit ONLY the UAT file changes — NEVER commit the UAT crash trigger.

---

## Sign-off

- [ ] Crash dialog title + body + NO stack trace visible (§1)
- [ ] Copy Details transforms label to "Copied" for ~2s (§2)
- [ ] Clipboard paste contains exception + stack trace (§2)
- [ ] Restart kills + relaunches process with fresh PID (§3)
- [ ] Lock overlay re-appears on relaunch (§3)
- [ ] `deskbridge-YYYYMMDD.log` FATAL entry exists (§3)
- [ ] Ctrl+F12 trigger removed from source before commit (§4)

**Pass condition:** all boxes checked, source returned to clean state.

**Tester:** ___________________________ **Date:** ____________________
