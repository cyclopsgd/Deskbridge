---
phase: 06-cross-cutting-features
plan: 02
subsystem: ui
tags: [toast, notifications, window-state, settings, event-bus, observable-collection, wpf-ui]
dependency_graph:
  requires:
    - "Plan 06-01 (IAuditLogger, SerilogSetup, RedactSensitivePolicy — RedactSensitivePolicy filters any future log emission from these new services)"
    - "src/Deskbridge.Core/Events/ConnectionEvents.cs (ConnectionEstablishedEvent, ConnectionClosedEvent, ReconnectingEvent, ConnectionFailedEvent, ConnectionImportedEvent — pre-existing, subscribed)"
    - "src/Deskbridge.Core/Events/AppEvents.cs (UpdateAvailableEvent — pre-existing, subscribed)"
    - "src/Deskbridge.Core/Services/JsonConnectionStore.cs (atomic tmp-rename pattern mirrored in WindowStateService)"
    - "src/Deskbridge/MainWindow.xaml (SnackbarPresenter line 339 — retained alongside new ToastStackControl; ContentDialogHost — retained)"
    - "src/Deskbridge/MainWindow.xaml.cs (OnSourceInitialized + OnClosing — extended with window-state hydrate/save)"
    - "src/Deskbridge/App.xaml.cs (ITabHostManager eager-resolve pattern at line 71 — mirrored for ToastSubscriptionService)"
  provides:
    - "Deskbridge.Core.Interfaces.IWindowStateService (LoadAsync / SaveAsync surface for Plan 06-04 SEC-03/SEC-05 consumption)"
    - "Deskbridge.Core.Settings.AppSettings (record — Window + Security + SchemaVersion=1)"
    - "Deskbridge.Core.Settings.WindowStateRecord / SecuritySettingsRecord (records — 06-04 reads SecuritySettingsRecord unchanged)"
    - "Deskbridge.Core.Settings.AppSettingsContext (source-gen JsonSerializerContext)"
    - "Deskbridge.Core.Services.WindowStateService (atomic tmp-rename writer, defaults-on-missing / malformed / unknown-schema)"
    - "Deskbridge.ViewModels.ToastStackViewModel (Push/Pause/Resume API for any future in-app toast publisher beyond the 6 bus events)"
    - "Deskbridge.ViewModels.ToastItemViewModel (DismissCommand + IsPaused surface)"
    - "Deskbridge.Controls.ToastStackControl (bottom-right UserControl with hover-pause wired)"
    - "Deskbridge.Services.ToastSubscriptionService (subscribes 6 events with UI-SPEC copy — eagerly resolved in App.OnStartup)"
  affects:
    - "src/Deskbridge/MainWindow.xaml (added xmlns:controls + ToastStackControl sibling; SnackbarPresenter retained for Phase 5's 15-session warning)"
    - "src/Deskbridge/MainWindow.xaml.cs (IWindowStateService ctor dep; OnSourceInitialized load + OnClosing save on BOTH first + second invocations)"
    - "src/Deskbridge/ViewModels/MainWindowViewModel.cs (ctor gains ToastStackViewModel param; ToastStack property for XAML binding)"
    - "src/Deskbridge/App.xaml.cs (3 new singletons + eager-resolve of ToastSubscriptionService between TabHostManager and Show)"
    - "tests/Deskbridge.Tests/DiCompositionTests.cs (4 new tests for NOTF singleton + source-order regression)"
    - "tests/Deskbridge.Tests/ViewModels/MainWindowViewModelTests.cs (ctor call updates)"
    - "tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs (ctor call update)"
    - "tests/Deskbridge.Tests/Integration/HostContainerPersistenceTests.cs (ctor call update)"
tech-stack:
  added:
    - "(none — all dependencies already pinned: System.Text.Json, WPF-UI, CommunityToolkit.Mvvm, Serilog)"
  patterns:
    - "Newest-at-index-0 ObservableCollection invariant for stacked overlays (ToastStackViewModel.Items)"
    - "DispatcherTimer-per-item with dictionary-keyed lifecycle cleanup (StartTimer / Remove)"
    - "Monotonic sequence counter (Interlocked.Increment) for ordering when DateTime.UtcNow resolution is insufficient"
    - "Hover-pause via code-behind MouseEnter/Leave → VM Pause/Resume (keeps DispatcherTimer state in the VM, UI events in the control)"
    - "AppSettings dual-schema (Window + Security) reserved up-front so Plan 06-04 slots into SecuritySettingsRecord without schema migration"
    - "MainWindow.OnClosing double-save: first invocation (before async CloseAllAsync) + second invocation (after CloseAllAsync rejoins via Close()) — survives user drag during disconnect window"
    - "RestoreBounds captured on save when IsMaximized=true so un-maximised coordinates survive across sessions"
    - "Source-order regression tests via file-read + IndexOf (mirrors Plan 06-01 CrashHandler / App.OnStartup assertions)"
key-files:
  created:
    - "src/Deskbridge/ViewModels/ToastItemViewModel.cs"
    - "src/Deskbridge/ViewModels/ToastStackViewModel.cs"
    - "src/Deskbridge/Controls/ToastStackControl.xaml"
    - "src/Deskbridge/Controls/ToastStackControl.xaml.cs"
    - "src/Deskbridge/Services/ToastSubscriptionService.cs"
    - "src/Deskbridge.Core/Interfaces/IWindowStateService.cs"
    - "src/Deskbridge.Core/Settings/AppSettings.cs"
    - "src/Deskbridge.Core/Settings/AppSettingsContext.cs"
    - "src/Deskbridge.Core/Services/WindowStateService.cs"
    - "tests/Deskbridge.Tests/Notifications/ToastStackViewModelTests.cs"
    - "tests/Deskbridge.Tests/Notifications/ToastSubscriptionServiceTests.cs"
    - "tests/Deskbridge.Tests/Notifications/WindowStateServiceTests.cs"
  modified:
    - "src/Deskbridge/MainWindow.xaml"
    - "src/Deskbridge/MainWindow.xaml.cs"
    - "src/Deskbridge/ViewModels/MainWindowViewModel.cs"
    - "src/Deskbridge/App.xaml.cs"
    - "tests/Deskbridge.Tests/DiCompositionTests.cs"
    - "tests/Deskbridge.Tests/ViewModels/MainWindowViewModelTests.cs"
    - "tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs"
    - "tests/Deskbridge.Tests/Integration/HostContainerPersistenceTests.cs"
decisions:
  - "Q1 Option B (custom ItemsControl stack) locked in — ToastStackControl replaces WPF-UI SnackbarPresenter for bus-event feedback because SnackbarPresenter is a single-visible FIFO queue (Pitfall 3) incompatible with D-07's max-3 / hover-pause / eviction semantics."
  - "SnackbarPresenter RETAINED alongside ToastStackControl — Phase 5 ConnectionTreeViewModel still uses ISnackbarService for the 15-session warning; routing that through the new stack is a v1.1 follow-up. Both render bottom-right but never collide in practice (15-session warning is single-shot)."
  - "ToastStackViewModel + ToastSubscriptionService MUST be singletons — the XAML DataContext binding reads Items while ToastSubscriptionService writes Items; transient scope would silently drop every push from the subscription side. DiCompositionTests.NOTF_Services_ResolveAsSingletons asserts this invariant."
  - "ConnectionImportedEvent already existed in Core (added in an earlier phase for Phase 7 importer plumbing) — plan assumed it needed to be added; discovered pre-existing. No deviation tracked because we only reuse what's there."
  - "Reconnect → Reconnected disambiguation via _reconnectingIds HashSet on ToastSubscriptionService — ReconnectingEvent adds id; a subsequent ConnectionEstablishedEvent with id-in-set produces 'Reconnected' 3s copy (UI-SPEC line 392). ConnectionFailedEvent CLEARS the id so a later user-retry reads as fresh 'Connected'."
  - "AppSettings Security sub-record built NOW (not in Plan 06-04) so the schema is locked before 06-04 executes — 06-04 only adds consumer code, no schema migration, no new file."
  - "MainWindow.OnClosing calls TrySaveWindowState on BOTH the first-invocation path (before the async CloseAllAsync kicks off) AND the _shutdownInProgress path (after CloseAllAsync re-enters via Close()) — the first write protects against a crash during CloseAllAsync; the second captures any drag/resize that happened while disconnects ran. Atomic tmp-rename makes the redundant write cheap."
  - "Window state load is synchronous (.GetAwaiter().GetResult()) in OnSourceInitialized — settings.json is <1KB and an async load would flicker at XAML-default size before the continuation applied saved bounds."
  - "Sidebar width kept at a hardcoded 240.0 in TrySaveWindowState (not bound to VM) — the Phase 2 panel has fixed width; if the panel becomes resizable in a later plan, the width source swaps without changing the settings schema."
  - "Test suite has a pre-existing flaky test (CrashHandlerTests.OnUnobservedTask_LogsErrorAndSetsObserved from Plan 06-01) — sometimes a JsonConnectionStore.Load warning from a parallel test leaks into the shared Log.Logger replaced by InMemorySink. Ran 4× total during Plan 06-02; 3 passes, 1 flake. Not caused by Plan 06-02. Defer fix to a dedicated test-isolation plan."
metrics:
  duration_minutes: 12
  completed_date: "2026-04-15"
  tasks: 4
  files_created: 12
  files_modified: 8
  tests_added: 30
---

# Phase 6 Plan 02: Toast Notifications + Window State Persistence Summary

**Custom bottom-right toast stack (max 3, newest-on-top, hover-pause) replacing WPF-UI's FIFO SnackbarPresenter for 6 bus events, plus atomic settings.json persistence with a dual Window+Security schema ready for Plan 06-04.**

## Performance

- **Duration:** 12 min
- **Started:** 2026-04-15T11:36:19Z
- **Completed:** 2026-04-15T11:48:39Z
- **Tasks:** 4
- **Files created:** 12
- **Files modified:** 8
- **Tests added:** 30 (8 ToastStack + 12 ToastSubscription + 6 WindowState + 4 DiComposition)

## What Was Built

### 1. ToastStackViewModel + ToastItemViewModel + ToastStackControl (Task 1 — NOTF-01, D-07, Q1 Option B)

Custom ItemsControl-based stack replacing the WPF-UI SnackbarPresenter for all bus-event feedback. Invariants enforced in the VM:

- **Newest-at-index-0** ObservableCollection (XAML StackPanel renders top-to-bottom naturally)
- **Max 3 visible** — a 4th push evicts the oldest (by `Sequence` monotonic counter, including stickies per D-07 explicit)
- **Per-item DispatcherTimer** for auto-dismiss; timer lifecycle tracked in a `Dictionary<Guid, DispatcherTimer>`
- **Pause/Resume** stops/restarts every timer and sets `IsPaused` on every item; called by `ToastStackControl.xaml.cs` from `MouseEnter`/`MouseLeave` (D-07 hover-pause)
- **Sticky items** (`Duration == null`) only dismiss via explicit `DismissCommand` or 4th-push eviction
- **Monotonic `Sequence`** via `Interlocked.Increment` — safe under rapid pushes where `DateTime.UtcNow` resolution is insufficient

`ToastStackControl.xaml` (UserControl): bottom-right anchored, `IsHitTestVisible=True`, `Width=360` per toast, `DynamicResource` tokens for every brush (no hardcoded hex), `ui:SymbolIcon` 24px for severity, `ui:Button` Dismiss16 for close.

**Test coverage:** 8 STA-collection tests — newest-on-top invariant, 4th push evicts oldest, auto-dismiss after Duration, sticky stays indefinitely, explicit dismiss, Pause/Resume freezes+unfreezes both items, sticky eviction on full stack, monotonic sequence under rapid pushes.

### 2. ToastSubscriptionService (Task 2 — NOTF-01, NOTF-02, NOTF-03)

Singleton registered by App.ConfigureServices and eagerly resolved in `OnStartup` AFTER `ITabHostManager` and BEFORE `mainWindow.Show()`. Subscribes to exactly 6 bus events on ctor:

| Event | Handler | Toast |
|-------|---------|-------|
| `ConnectionEstablishedEvent` | `OnConnected` | If id in `_reconnectingIds`: Info 3s "Reconnected" + remove id. Else: Info 2s "Connected". |
| `ConnectionClosedEvent` | `OnClosed` | `UserInitiated` → NO toast (UI-SPEC deliberate silence). Else: Info 3s "Disconnected". |
| `ReconnectingEvent` | `OnReconnecting` | Adds id to `_reconnectingIds`. Caution sticky "Reconnecting … attempt N/20" with ArrowClockwise24 icon. |
| `ConnectionFailedEvent` | `OnFailed` | Removes id from `_reconnectingIds`. Danger sticky "Connection failed" with ErrorCircle24 icon. |
| `UpdateAvailableEvent` | `OnUpdateAvailable` | Info sticky "Update available". |
| `ConnectionImportedEvent` | `OnImported` | Info 3s "Import complete". |

**NOTF-02 compliance:** zero `IContentDialogService` references in the file. Asserted via `dialogs.ReceivedCalls().Should().BeEmpty()` after exercising every handler.

**Reconnect disambiguation:** `_reconnectingIds` HashSet tracks which connections are currently inside a reconnect loop. Reconnect Success → Info 3s "Reconnected" copy; a Failed clears the state so the user's later retry reads as fresh Connected.

**Test coverage:** 12 tests — exact-string assertions per event, UserInitiated silence, subscription count (`Received(1)` per event type), NOTF-02 (dialog service never touched), reconnect transition, failed-then-established returns to fresh Connected copy.

### 3. AppSettings + WindowStateService (Task 3 — NOTF-04, Pattern 5)

Dual-schema `AppSettings` record (`Window`, `Security`, `SchemaVersion=1`). `SecuritySettingsRecord` landed NOW (not in Plan 06-04) so the full schema is locked — 06-04 will only add a consumer, no file change and no migration.

`AppSettingsContext` source-generated `JsonSerializerContext` with `WriteIndented=true` + `CamelCase` naming, mirroring `AuditJsonContext` from Plan 06-01.

`WindowStateService`:
- **Load:** returns `new AppSettings()` defaults when file missing / JSON malformed / `SchemaVersion != 1` (logged as warnings, never throws).
- **Save:** atomic `.tmp` + `File.Move(overwrite: true)` — same pattern as `JsonConnectionStore.PersistAtomically` (Phase 3 precedent). UTF-8 **without BOM** (`UTF8Encoding(emitBOM: false)`) so the first byte is `{`.

**Test coverage:** 6 tests — defaults-on-missing, full round-trip, no `.tmp` leftover, malformed JSON → defaults, unknown `SchemaVersion` → defaults, no-BOM UTF-8.

### 4. MainWindow + App.xaml.cs Wiring (Task 4)

- **MainWindow.xaml:** added `xmlns:controls` + `ToastStackControl` as a sibling of the retained `SnackbarPresenter` + `ContentDialogHost`. `DataContext="{Binding ToastStack}"` binds the VM singleton.
- **MainWindow.xaml.cs `OnSourceInitialized`:** synchronously `LoadAsync` → apply `Left/Top/Width/Height/Maximized` BEFORE `_airspace.AttachToWindow(this)` so the airspace snapshot captures correct dimensions on first resize.
- **MainWindow.xaml.cs `OnClosing`:** `TrySaveWindowState()` called on BOTH the first-invocation path (before async `CloseAllAsync`) and the `_shutdownInProgress` path (second invocation after async rejoins). `RestoreBounds` used when maximised so un-maximised coords survive across sessions. All exceptions swallowed with warning.
- **MainWindowViewModel:** ctor gains `ToastStackViewModel` param; exposes `ToastStack` property for XAML binding.
- **App.xaml.cs:** three new singleton registrations (`IWindowStateService`, `ToastStackViewModel`, `ToastSubscriptionService`) + eager-resolve of `ToastSubscriptionService` between `ITabHostManager` and `mainWindow.Show()`.

**Test coverage:** 4 `DiCompositionTests` — NOTF singleton resolution, App.OnStartup eager-resolve source-order, MainWindow.OnSourceInitialized load-before-attach source-order, MainWindow.OnClosing save-on-both-paths.

## Commit Trail

| Hash | Title |
|------|-------|
| `5ea9a38` | feat(06-02): add ToastStackViewModel + ToastStackControl (NOTF-01, D-07) |
| `492dd0c` | feat(06-02): add ToastSubscriptionService (NOTF-01, NOTF-02, NOTF-03) |
| `3b11c3c` | feat(06-02): add WindowStateService + AppSettings schema (NOTF-04) |
| `0593c2a` | feat(06-02): wire ToastStack + WindowState into MainWindow + App DI (NOTF-01/03/04) |

## Test Results

**Plan 06-02 tests added:** 30 (8 `ToastStackViewModelTests` + 12 `ToastSubscriptionServiceTests` + 6 `WindowStateServiceTests` + 4 new `DiCompositionTests`).

**Full suite:** `dotnet test Deskbridge.sln` → **357 passed, 0 failed, 3 skipped** (327 prior + 30 new).

**Build:** `dotnet build Deskbridge.sln` → **0 warnings, 0 errors** (TreatWarningsAsErrors enforced).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Blocking] Pre-existing tests broken by new MainWindowViewModel ctor parameter**
- **Found during:** Task 4 build
- **Issue:** Adding `ToastStackViewModel` to the `MainWindowViewModel` ctor broke 6 existing test sites in `MainWindowViewModelTests.cs`, `KeyboardShortcutTests.cs`, and `HostContainerPersistenceTests.cs`. Build failed with `CS7036: There is no argument given that corresponds to the required parameter 'toastStack'`.
- **Fix:** Updated every `new MainWindowViewModel(...)` call site to pass a fresh `new ToastStackViewModel()` — harmless for these tests since they don't exercise toast behaviour, and forcing the tests to construct the VM in its final shape protects them from future ctor drift.
- **Files modified:** 3 test files
- **Commit:** `0593c2a`

### Plan-Assumption Corrections (no deviation tracked — pre-existing)

- **ConnectionImportedEvent already existed.** Plan Task 2 action step 1 says "Edit `ConnectionEvents.cs` — add `ConnectionImportedEvent`". The record already exists at line 23 (committed in an earlier phase for Phase 7 importer plumbing). Executor reused the existing record; no file change needed.

### Test-design adjustments (no behavioural deviation)

- **xUnit1051 CancellationToken propagation** — `WindowStateServiceTests` passes `TestContext.Current.CancellationToken` to every async call (File.ReadAllTextAsync / WriteAllTextAsync / LogAsync) via a `private static CancellationToken Ct => TestContext.Current.CancellationToken` helper, matching the Plan 06-01 `AuditLoggerTests` pattern. Required because `TreatWarningsAsErrors` promotes xUnit1051 to a build error.
- **`DispatcherTimer` test isolation via `StaRunner.Run` + manual `Dispatcher.PushFrame`** — `ToastStackViewModelTests` defines a private `AdvanceDispatcher(TimeSpan)` helper that schedules a background `DispatcherTimer` on `Dispatcher.CurrentDispatcher` and pumps frames until it ticks. Required because xUnit v3 3.2.2 workers default to MTA and the production code uses `Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher`.

**Total deviations:** 1 auto-fixed (Rule 3 blocking). Necessary for test compilation.

**Impact on plan:** Scope unchanged; plan executed as designed.

## Authentication Gates Encountered

None.

## Known Stubs

None. Every new component is fully wired:
- `ToastStackControl.xaml` renders with real DynamicResource brushes and a live SymbolIcon icon binding.
- `ToastSubscriptionService` handlers all use the exact UI-SPEC copy strings.
- `WindowStateService.SaveAsync` is actually invoked from `MainWindow.OnClosing` and its output is actually consumed by `MainWindow.OnSourceInitialized`.
- MainWindow.xaml still contains the pre-existing Phase 2 "Settings will appear here" placeholder at line 172 — this is NOT a Plan 06-02 stub; Plan 06-04 §Settings Panel Additions owns its replacement per UI-SPEC lines 182-196.

## TODOs for Plan 06-04 (App Security)

- Replace the Phase 2 "Settings will appear here" placeholder with the Security section (Auto-lock timeout NumberBox + Lock-on-minimise ToggleSwitch) per UI-SPEC lines 182-196. The `SecuritySettingsRecord` schema is already in place — just add the settings panel content + bind it to `AppSettings.Security` via `IWindowStateService.LoadAsync` / `SaveAsync`.
- Route Phase 5's 15-session GDI warning (currently `_snackbarService.Show(...)` in `ConnectionTreeViewModel` line 686) through the new `ToastStackViewModel.Push` as an Info sticky — then remove `SnackbarPresenter` + `ISnackbarService` registration entirely. v1.1 task; not blocking Plan 06-04.
- Publisher for `ConnectionImportedEvent` is Phase 7's mRemoteNG importer — Phase 6 only subscribes; the import count/source arrive from Phase 7.

## Threat Model Coverage

| Threat | Mitigation Landed | Verification |
|--------|-------------------|--------------|
| T-06-NOTF-01 (information disclosure via toast interpolation) | `TextBlock.Text` binding treats `{Hostname}` / `{Reason}` as literal text — no XAML/HTML parsing, no injection surface | Copy assertions in `ToastSubscriptionServiceTests` (Tests 1-8) verify exact string content |
| T-06-NOTF-02 (settings.json tampering) | `LoadAsync` returns defaults on malformed JSON + unknown SchemaVersion | `WindowStateServiceTests` Tests 4-5 (malformed / unknown-schema → defaults) |
| T-06-NOTF-03 (event-storm DoS flooding ToastStack) | Eviction cap (3 items) prevents UI lockup; `_reconnectingIds` HashSet dedupes sticky Reconnecting toasts per connection id | `ToastStackViewModelTests` Test 2 (cap at 3) + Test 7 (sticky eviction) |

No new threat flags introduced beyond the `<threat_model>` block in the plan. All network/auth/trust-boundary surfaces are pre-existing.

## Self-Check: PASSED

- `src/Deskbridge/ViewModels/ToastItemViewModel.cs` — FOUND
- `src/Deskbridge/ViewModels/ToastStackViewModel.cs` — FOUND
- `src/Deskbridge/Controls/ToastStackControl.xaml` — FOUND
- `src/Deskbridge/Controls/ToastStackControl.xaml.cs` — FOUND
- `src/Deskbridge/Services/ToastSubscriptionService.cs` — FOUND
- `src/Deskbridge.Core/Interfaces/IWindowStateService.cs` — FOUND
- `src/Deskbridge.Core/Settings/AppSettings.cs` — FOUND
- `src/Deskbridge.Core/Settings/AppSettingsContext.cs` — FOUND
- `src/Deskbridge.Core/Services/WindowStateService.cs` — FOUND
- `tests/Deskbridge.Tests/Notifications/ToastStackViewModelTests.cs` — FOUND
- `tests/Deskbridge.Tests/Notifications/ToastSubscriptionServiceTests.cs` — FOUND
- `tests/Deskbridge.Tests/Notifications/WindowStateServiceTests.cs` — FOUND
- Commit `5ea9a38` — FOUND
- Commit `492dd0c` — FOUND
- Commit `3b11c3c` — FOUND
- Commit `0593c2a` — FOUND

---
*Phase: 06-cross-cutting-features*
*Plan: 02*
*Completed: 2026-04-15*
