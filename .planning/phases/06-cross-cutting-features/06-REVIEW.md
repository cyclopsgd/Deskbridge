---
phase: 06-cross-cutting-features
reviewed: 2026-04-15T00:00:00Z
depth: standard
files_reviewed: 62
files_reviewed_list:
  - src/Deskbridge.Core/Deskbridge.Core.csproj
  - src/Deskbridge.Core/Interfaces/IAppLockState.cs
  - src/Deskbridge.Core/Interfaces/IAuditLogger.cs
  - src/Deskbridge.Core/Interfaces/ICommandPaletteService.cs
  - src/Deskbridge.Core/Interfaces/IMasterPasswordService.cs
  - src/Deskbridge.Core/Interfaces/IWindowStateService.cs
  - src/Deskbridge.Core/Logging/RedactSensitivePolicy.cs
  - src/Deskbridge.Core/Logging/SerilogSetup.cs
  - src/Deskbridge.Core/Models/AuditRecord.cs
  - src/Deskbridge.Core/Models/AuthFile.cs
  - src/Deskbridge.Core/Models/CommandEntry.cs
  - src/Deskbridge.Core/Services/AppLockState.cs
  - src/Deskbridge.Core/Services/AuditLogger.cs
  - src/Deskbridge.Core/Services/CommandPaletteService.cs
  - src/Deskbridge.Core/Services/MasterPasswordService.cs
  - src/Deskbridge.Core/Services/WindowStateService.cs
  - src/Deskbridge.Core/Settings/AppSettings.cs
  - src/Deskbridge.Core/Settings/AppSettingsContext.cs
  - src/Deskbridge/App.xaml
  - src/Deskbridge/App.xaml.cs
  - src/Deskbridge/Controls/ToastStackControl.xaml
  - src/Deskbridge/Controls/ToastStackControl.xaml.cs
  - src/Deskbridge/Converters/NullToCollapsedConverter.cs
  - src/Deskbridge/CrashHandler.cs
  - src/Deskbridge/Dialogs/CommandPaletteDialog.xaml
  - src/Deskbridge/Dialogs/CommandPaletteDialog.xaml.cs
  - src/Deskbridge/Dialogs/CrashDialog.xaml
  - src/Deskbridge/Dialogs/CrashDialog.xaml.cs
  - src/Deskbridge/Dialogs/LockOverlayDialog.xaml
  - src/Deskbridge/Dialogs/LockOverlayDialog.xaml.cs
  - src/Deskbridge/KeyboardShortcutRouter.cs
  - src/Deskbridge/MainWindow.xaml
  - src/Deskbridge/MainWindow.xaml.cs
  - src/Deskbridge/Program.cs
  - src/Deskbridge/Services/AppLockController.cs
  - src/Deskbridge/Services/IHostContainerProvider.cs
  - src/Deskbridge/Services/IdleLockService.cs
  - src/Deskbridge/Services/SessionLockService.cs
  - src/Deskbridge/Services/ToastSubscriptionService.cs
  - src/Deskbridge/ViewModels/CommandPaletteRowViewModel.cs
  - src/Deskbridge/ViewModels/CommandPaletteViewModel.cs
  - src/Deskbridge/ViewModels/LockOverlayViewModel.cs
  - src/Deskbridge/ViewModels/MainWindowViewModel.cs
  - src/Deskbridge/ViewModels/ToastItemViewModel.cs
  - src/Deskbridge/ViewModels/ToastStackViewModel.cs
  - tests/Deskbridge.Tests/DiCompositionTests.cs
  - tests/Deskbridge.Tests/Integration/HostContainerPersistenceTests.cs
  - tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs
  - tests/Deskbridge.Tests/Logging/AuditLoggerTests.cs
  - tests/Deskbridge.Tests/Logging/CrashHandlerTests.cs
  - tests/Deskbridge.Tests/Logging/RedactSensitivePolicyTests.cs
  - tests/Deskbridge.Tests/Logging/SerilogConfigTests.cs
  - tests/Deskbridge.Tests/Logging/TempDirScope.cs
  - tests/Deskbridge.Tests/Notifications/ToastStackViewModelTests.cs
  - tests/Deskbridge.Tests/Notifications/ToastSubscriptionServiceTests.cs
  - tests/Deskbridge.Tests/Notifications/WindowStateServiceTests.cs
  - tests/Deskbridge.Tests/Palette/CommandPaletteServiceTests.cs
  - tests/Deskbridge.Tests/Palette/CommandPaletteViewModelTests.cs
  - tests/Deskbridge.Tests/Security/AppLockControllerTests.cs
  - tests/Deskbridge.Tests/Security/IdleLockServiceTests.cs
  - tests/Deskbridge.Tests/Security/LockOverlayViewModelTests.cs
  - tests/Deskbridge.Tests/Security/MasterPasswordServiceTests.cs
  - tests/Deskbridge.Tests/Security/SessionLockServiceTests.cs
  - tests/Deskbridge.Tests/ViewModels/MainWindowViewModelTests.cs
  - tests/uat/phase-06-auto-lock.md
  - tests/uat/phase-06-crash.md
  - tests/uat/phase-06-security.md
findings:
  critical: 0
  warning: 7
  info: 9
  total: 16
status: issues_found
---

# Phase 6: Code Review Report

**Reviewed:** 2026-04-15T00:00:00Z
**Depth:** standard
**Files Reviewed:** 62
**Status:** issues_found

## Summary

Phase 6 delivers four cross-cutting concerns — structured logging + audit + crash
handling (06-01), notifications + window-state persistence (06-02), command
palette + keyboard shortcuts (06-03), and app security with master password +
app lock (06-04). The code is well-documented, tests are thorough, and the
security-sensitive code (PBKDF2, `FixedTimeEquals`, `RedactSensitivePolicy`)
follows OWASP guidance carefully.

No critical security or data-loss bugs were found. The master-password service
uses OWASP-recommended PBKDF2 parameters and constant-time comparison. The audit
logger correctly serialises concurrent writes via `SemaphoreSlim`. The lock
controller implements Pitfall 5 Option A correctly.

The findings below are mostly correctness-adjacent concerns: a handful of
concurrency/disposal edge cases, a few places where handler references could be
leaked, and several opportunities for more defensive coding around nullable
inputs and exception paths.

## Warnings

### WR-01: `CrashHandler.InstallDispatcherHook` not idempotent against multiple Application instances

**File:** `src/Deskbridge/CrashHandler.cs:77-83`
**Issue:** `InstallDispatcherHook` uses `HookState.DispatcherInstalled` as an
idempotence gate but the hook is attached to the *specific* `Application`
instance passed in. If tests (or a weird hot-restart flow) construct a new
`Application`, the flag prevents attaching to the new instance AND the old
instance is never detached, so its event handlers root the old `Application`
in memory. In production this is unreachable (one `App` per process), but the
contract is subtly wrong — the flag tracks "any dispatcher hook was ever
installed" rather than "this dispatcher has a hook."

**Fix:** Either document the "one Application per process" invariant explicitly,
or track the target application reference and short-circuit only when the flag
is set AND the target matches. Alternatively, drop the flag entirely for the
dispatcher hook and rely on `DispatcherUnhandledException` being a multi-cast
event (double-subscription would produce duplicate log lines — still a bug,
but a noisier one that's easier to catch in testing).

```csharp
private static Application? _hookedApplication;
public static void InstallDispatcherHook(Application application)
{
    ArgumentNullException.ThrowIfNull(application);
    if (_hookedApplication == application) return;      // already hooked THIS one
    if (_hookedApplication is not null)
    {
        _hookedApplication.DispatcherUnhandledException -= OnDispatcherUnhandled;
    }
    application.DispatcherUnhandledException += OnDispatcherUnhandled;
    _hookedApplication = application;
    HookState.DispatcherInstalled = true;
}
```

### WR-02: `MainWindowViewModel.PersistSecuritySettings` is `async void` and swallows cancellation

**File:** `src/Deskbridge/ViewModels/MainWindowViewModel.cs:286-303`
**Issue:** `OnAutoLockTimeoutMinutesChanged` and `OnLockOnMinimiseChanged` both
fire-and-forget `PersistSecuritySettings()` — an `async void` method. Any
exception thrown from `LoadAsync` or `SaveAsync` before the `try` can tear down
the process (async void unhandled exceptions propagate to the
`SynchronizationContext`, i.e. the dispatcher, where `CrashHandler` catches
them — but this conflates a benign persistence failure with a user-facing
"unexpected error" dialog). Additionally: two rapid setting changes can race —
change A kicks off LoadAsync/SaveAsync, change B kicks off a second
LoadAsync/SaveAsync, and whichever SaveAsync completes last wins. With
`CurrentSecuritySettings` capturing the VM's current state at save time, this
is probably correct behaviour, but there's no serialisation.

**Fix:** Make the method `private async Task`, wrap the entire body in
try/catch (it already is), and serialise via `SemaphoreSlim` or a simple
`_persistInFlight` flag. Or use the `FluentSemaphore` pattern to coalesce
rapid changes:

```csharp
private readonly SemaphoreSlim _persistGate = new(1, 1);

private async Task PersistSecuritySettingsAsync()
{
    if (_suppressPersist || _windowState is null) return;
    if (!await _persistGate.WaitAsync(0)) return;  // coalesce concurrent
    try
    {
        var current = await _windowState.LoadAsync().ConfigureAwait(false);
        var updated = current with { Security = CurrentSecuritySettings };
        await _windowState.SaveAsync(updated).ConfigureAwait(false);
    }
    catch (Exception ex) { Log.Warning(ex, "Failed to persist security settings"); }
    finally { _persistGate.Release(); }
}

partial void OnAutoLockTimeoutMinutesChanged(int value) => _ = PersistSecuritySettingsAsync();
```

### WR-03: `IdleLockService` ctor attaches InputManager handler even when constructed on a non-UI thread

**File:** `src/Deskbridge/Services/IdleLockService.cs:68-79`
**Issue:** The ctor falls back to `Dispatcher.CurrentDispatcher` when
`Application.Current?.Dispatcher` is null. `Dispatcher.CurrentDispatcher` is a
PROPERTY that *creates* a dispatcher for the calling thread if none exists,
which means a background thread that happens to construct an `IdleLockService`
gets its own dispatcher, and the `DispatcherTimer` will only fire on THAT
thread — not on the UI thread. Worse: `InputManager.Current.PreProcessInput
+= _handler` attaches to the calling thread's input manager (InputManager is
thread-local via `[ThreadStatic]`), which means the handler will never fire
for UI events if the service was constructed off-thread.

In production this isn't reachable (App.OnStartup is on the UI thread), but
the defense-in-depth fallback is silently wrong.

**Fix:** Throw if neither `Application.Current?.Dispatcher` nor a valid UI
dispatcher can be obtained. The service MUST run on the UI thread; a silent
degradation is a latent bug.

```csharp
var dispatcher = System.Windows.Application.Current?.Dispatcher;
if (dispatcher is null)
{
    throw new InvalidOperationException(
        "IdleLockService must be constructed on the WPF UI thread. " +
        "Application.Current.Dispatcher is null.");
}
```

### WR-04: `CrashDialog.OnClosing` restart path calls `Shutdown()` even if `Process.Start` succeeded, but runs INSIDE a `Closing` handler that already cancelled for Primary

**File:** `src/Deskbridge/Dialogs/CrashDialog.xaml.cs:91-127`
**Issue:** `OnClosing` is invoked by the dialog close handshake and can be
called MULTIPLE times if the user clicks Copy Details repeatedly (each Primary
click triggers the Closing event with `e.Cancel = true`). That code path is
correctly handled — the early return for `Result == Primary`. But the Restart
branch has no idempotence guard: a user clicking Restart twice rapidly could
spawn two new `Deskbridge.exe` processes before `Shutdown()` takes effect, and
`Application.Current?.Shutdown()` is idempotent but the `Process.Start`
call is not. In practice the dialog hides after the first Close, so the
second click can't fire — but there's nothing in code enforcing that.

**Fix:** Add an `_isRestarting` flag and short-circuit:

```csharp
private bool _isRestarting;
private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs e)
{
    if (e.Result == ContentDialogResult.Primary) { e.Cancel = true; return; }
    if (_isRestarting) return;
    _isRestarting = true;
    // ... existing Process.Start + Shutdown
}
```

### WR-05: `WindowStateService.SaveAsync` leaves `.tmp` on disk if `File.Move` fails

**File:** `src/Deskbridge.Core/Services/WindowStateService.cs:73-88`
**Issue:** If `File.WriteAllTextAsync(tmp, ...)` succeeds but `File.Move(tmp,
_path, overwrite: true)` throws (e.g. antivirus lock on the destination, disk
full between write and rename, file locked by another reader), the `.tmp`
file remains on disk indefinitely. The next `SaveAsync` would re-create it
(overwriting), but if `SaveAsync` itself fails repeatedly, `.tmp` accumulates.
Same pattern exists in `MasterPasswordService.WriteAuthFileAtomically`
(line 180-190) — no cleanup on Move failure.

**Fix:** Wrap Move in try/catch and clean up the tmp on failure:

```csharp
await File.WriteAllTextAsync(tmp, json, bomless, cancellationToken).ConfigureAwait(false);
try
{
    File.Move(tmp, _path, overwrite: true);
}
catch
{
    try { File.Delete(tmp); } catch { /* best-effort */ }
    throw;
}
```

Same pattern for `MasterPasswordService.WriteAuthFileAtomically`.

### WR-06: `AppLockController.ShowLockOverlayAsync` captures `handler` by reference but unsubscribes BEFORE the dialog hides

**File:** `src/Deskbridge/Services/AppLockController.cs:203-244`
**Issue:** The handler for `UnlockSucceeded` unsubscribes itself (`dialog.ViewModel.UnlockSucceeded -= handler;`)
then awaits `UnlockAsync()`. If `UnlockAsync` throws, `dialog.Hide()` is still
called (inside `finally`), but the unsubscribe already happened — which is
fine. However, `_activeDialog = null;` in the outer `finally` runs only after
`await dialog.ShowAsync()` returns. If a second `LockAsync` arrives during
the brief window between `_lockState.Unlock()` (which returns IsLocked=false)
and `_activeDialog = null`, `ShowLockOverlayAsync` early-returns ("already
showing") and then the caller publishes `AppUnlockedEvent` — but the incoming
LockAsync already bailed because IsLocked became false, so no second lock
happens. Net: the logic is probably correct, but the interaction between
`_activeDialog` guard + `_lockState.IsLocked` guard + handler-self-unsubscribe
is subtle enough to warrant a test covering the "lock arrives during unlock"
race. None of the existing tests exercise this path.

**Fix:** Add a test for the re-entrancy race, or tighten the ordering:

```csharp
handler = async (_, _) =>
{
    dialog.ViewModel.UnlockSucceeded -= handler;
    try
    {
        dialog.Hide();          // Hide FIRST so any re-lock sees _activeDialog=null path
        await UnlockAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "UnlockAsync failed from dialog continuation");
    }
};
```

Note: `Hide()` before `UnlockAsync()` reverses the current order. Either order
has trade-offs; the point is to make a conscious choice and test the race.

### WR-07: `ToastStackViewModel.Push` not thread-safe against concurrent callers

**File:** `src/Deskbridge/ViewModels/ToastStackViewModel.cs:36-69`
**Issue:** `Push`, `Remove`, `Pause`, and `Resume` all mutate `Items`
(`ObservableCollection<T>`) and `_timers` (`Dictionary<,>`). `ObservableCollection`
is NOT thread-safe and must be mutated on the dispatcher thread. If a future
bus publisher fires `ConnectionEstablishedEvent` from a background thread (the
`ToastSubscriptionService` handlers call `_stack.Push` directly without a
dispatcher marshal), WPF will throw a cross-thread
`NotSupportedException` because XAML bindings observe `Items.CollectionChanged`.
`Interlocked.Increment(ref _sequence)` handles the counter but not the rest.

The code comment in `MainWindowViewModel.Dispatch` acknowledges "WeakReferenceMessenger
delivers on the publisher's thread" as the same concern — but
`ToastSubscriptionService` doesn't have a similar `Dispatch` helper.

**Fix:** Either marshal inside `Push`/`Remove`/`Pause`/`Resume` (safe but adds
dispatcher dependency to the VM), or have `ToastSubscriptionService` marshal
its handlers via a dispatcher the same way `MainWindowViewModel.Dispatch`
does. Recommend the subscription-side marshal because it aligns with the
existing pattern:

```csharp
public ToastSubscriptionService(IEventBus bus, ToastStackViewModel stack)
{
    // ... existing ctor ...
    bus.Subscribe<ConnectionEstablishedEvent>(this, e => MarshalToUi(() => OnConnected(e)));
    // etc.
}

private static void MarshalToUi(Action a)
{
    var d = Application.Current?.Dispatcher;
    if (d is null || d.CheckAccess()) a();
    else d.BeginInvoke(a);
}
```

## Info

### IN-01: `CommandPaletteService.ScoreCommand` subsequence case conversion allocates on every call

**File:** `src/Deskbridge.Core/Services/CommandPaletteService.cs:95-105`
**Issue:** `IsSubsequence` calls `.ToLowerInvariant()` on both the query and
target, which allocates two fresh strings per call. With the 4 D-04 commands,
scoring a single keystroke allocates 8 strings. Not a correctness issue; a
micro-optimisation.

**Fix:** Use `char.ToLowerInvariant(char)` in the loop comparison instead:

```csharp
private static bool IsSubsequence(ReadOnlySpan<char> q, ReadOnlySpan<char> t)
{
    int qi = 0;
    for (int ti = 0; ti < t.Length && qi < q.Length; ti++)
    {
        if (char.ToLowerInvariant(q[qi]) == char.ToLowerInvariant(t[ti])) qi++;
    }
    return qi == q.Length;
}
```

### IN-02: `AuthFile` `PasswordHash` property name leaks through `JsonSourceGenerationOptions` camelCase to `passwordHash`, but destructuring by Serilog would match the `PasswordHash` pascal-case denylist — double-check

**File:** `src/Deskbridge.Core/Models/AuthFile.cs:15`
**Issue:** `AuthFile.PasswordHash` is a public record property. If an instance
were ever logged via `Log.Information("Auth state {@Auth}", authFile)`,
`RedactSensitivePolicy` would match `PasswordHash` against the denylist which
contains `Password` (case-insensitive contains-check isn't what the denylist
does — it's a `HashSet.Contains` EQUALITY check). `PasswordHash` is NOT in
the denylist. So an accidental log of an `AuthFile` would leak the hash.

Hashes are not plaintext passwords, but they're still sensitive (offline
cracking material). The denylist should include `PasswordHash`.

**Fix:** Add `PasswordHash` to the denylist in
`RedactSensitivePolicy.Denylist` (`src/Deskbridge.Core/Logging/RedactSensitivePolicy.cs:35-44`):

```csharp
internal static readonly HashSet<string> Denylist = new(StringComparer.OrdinalIgnoreCase)
{
    "Password",
    "PasswordHash",       // <-- add
    "Secret",
    // ...
};
```

### IN-03: `LockOverlayViewModel.Unlock` swallows exceptions from `SetMasterPassword` / `VerifyMasterPassword`

**File:** `src/Deskbridge/ViewModels/LockOverlayViewModel.cs:79-114`
**Issue:** `_masterPassword.SetMasterPassword(Password)` can throw if disk is
read-only or the directory is locked. The method runs synchronously inside
the RelayCommand and any exception propagates to the CommunityToolkit's
command infrastructure, where it becomes a silent failure (or eventually
surfaces via `TaskScheduler.UnobservedTaskException` for async paths). The
user would see no error message and the UnlockSucceeded event wouldn't fire
— they'd be stuck on the first-run screen with no feedback.

**Fix:** Wrap the KDF calls in try/catch and surface a user-facing error:

```csharp
try
{
    _masterPassword.SetMasterPassword(Password);
}
catch (Exception ex)
{
    Serilog.Log.Error(ex, "Failed to write auth.json during first-run setup");
    ErrorMessage = "Could not save password. Check disk permissions and retry.";
    return;
}
```

### IN-04: `CommandPaletteViewModel.Refresh` rebuilds `Items` even when query is unchanged

**File:** `src/Deskbridge/ViewModels/CommandPaletteViewModel.cs:54-56`
**Issue:** `OnSearchTextChanged` is invoked from the `[ObservableProperty]`
setter, which CommunityToolkit fires on every assignment — even if the new
value equals the old value. WPF's `UpdateSourceTrigger=PropertyChanged` will
drive the setter on every keystroke; the partial method fires regardless of
whether the search string actually changed (e.g. TextBox restoring focus).
Not a correctness bug — just unnecessary rebuild work.

**Fix:** The CommunityToolkit setter already short-circuits equal assignments
by default (`EqualityComparer<T>.Default.Equals`), so this is likely a
non-issue. Verify via the generated code and close.

### IN-05: `MasterPasswordService.HashNewPassword` returns a string with base64 `=` padding inside a `.`-separated envelope — no ambiguity, but document

**File:** `src/Deskbridge.Core/Services/MasterPasswordService.cs:85-91`
**Issue:** `Convert.ToBase64String` emits standard base64 which includes `/`,
`+`, and `=`. `/` and `+` are fine inside a dot-separated envelope, but
could trip up a future migrator that expects URL-safe base64 or logs the
envelope to URL-based telemetry. Not a current bug.

**Fix:** Document the format in the XML comments (already partially done) or
switch to `Convert.ToBase64UrlString` for web-safety. Low priority; current
format is unambiguous.

### IN-06: `SessionLockService` doesn't subscribe to `SystemEvents.PowerModeChanged` — sleep/wake doesn't extend the idle timer

**File:** `src/Deskbridge/Services/SessionLockService.cs` (and IdleLockService.cs)
**Issue:** The UAT `phase-06-auto-lock.md §5` documents this as
"accepted" — Pitfall 4 says DispatcherTimer doesn't compensate for sleep.
The mitigation is that Windows+L fires SessionSwitch on most corporate
machines. Fine for v1.

**Fix:** None needed for v1. File a Phase 7 improvement to subscribe to
`SystemEvents.PowerModeChanged` and restart the timer with `Interval =
remaining` on `PowerModeChangedEventArgs.Mode == Resume`.

### IN-07: `ToastSubscriptionService` handlers use captured closure over `_reconnectingIds` without a lock

**File:** `src/Deskbridge/Services/ToastSubscriptionService.cs:29,48,87,103`
**Issue:** `_reconnectingIds.Add`, `Remove`, and implicit iteration via the
`HashSet<Guid>` are not thread-safe. If bus events fire from multiple threads
(same concern as WR-07), `HashSet<T>` can corrupt its internal buckets and
throw or return stale membership results. In current code the bus delivers
on the publisher's thread which is nearly always the UI thread, so this is
latent — but defense-in-depth is the project pattern.

**Fix:** Either marshal to UI (fixes WR-07 at the same time) or use a
`ConcurrentDictionary<Guid, byte>` for Add/Remove operations. The marshal
approach is cleaner.

### IN-08: `CommandPaletteDialog.Dialog_PreviewKeyDown` fires `ExecuteSelectedAsync` as fire-and-forget without error handling

**File:** `src/Deskbridge/Dialogs/CommandPaletteDialog.xaml.cs:54-62`
**Issue:** `_ = _vm.ExecuteSelectedAsync();` discards the task. If the
underlying command throws (e.g. `newConnection` closure invokes a VM command
that throws), the exception becomes a `UnobservedTaskException` — logged but
not shown to the user. The palette dismisses anyway via `Hide()`.

**Fix:** Capture the task and log failures synchronously:

```csharp
var t = _vm.ExecuteSelectedAsync();
t.ContinueWith(
    tt => Serilog.Log.Warning(tt.Exception, "Command palette action failed"),
    TaskContinuationOptions.OnlyOnFaulted);
Hide();
```

Or `await` it via an `async void` handler — `PreviewKeyDown` is a routed
event and the framework tolerates async void.

### IN-09: Several source-grep tests rely on string matches that would break on harmless refactors

**File:** `tests/Deskbridge.Tests/DiCompositionTests.cs:108-110`, `tests/Deskbridge.Tests/Logging/CrashHandlerTests.cs:188-200`, others
**Issue:** The `App_EagerResolvesToastSubscriptionService_AfterTabHostManager_BeforeShow`
test (and similar) search for literal strings like
`"GetRequiredService<ITabHostManager>()"`. A harmless refactor — using
`sp.GetRequiredService<ITabHostManager>()` via an explicit variable, or
aliasing to a helper — would break the test without changing behaviour. The
tests acknowledge this (comments mention "safer than reflecting on runtime
type") but the trade-off deserves a note: these are order-invariants that
can't be checked any other way without instantiating `App` in an STA test,
which the team has avoided because of Freezable brush issues.

**Fix:** Add a top-level comment to each source-grep test noting it's a
best-effort guard and explaining why structural checks would be preferable.
Alternatively, move the eager-resolve invariant into a public read-only
property (`App.EagerResolveOrder`) that tests can inspect — overkill for
the current surface.

---

_Reviewed: 2026-04-15T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
