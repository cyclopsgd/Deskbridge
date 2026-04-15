---
phase: 06-cross-cutting-features
plan: 03
subsystem: ui
tags: [command-palette, shortcuts, fuzzy-match, fullscreen, lock-gate, content-dialog, wpf-ui]
dependency_graph:
  requires:
    - "Plan 06-01 (Serilog.Log.Warning used in MainWindow.OpenCommandPaletteAsync catch; RedactSensitivePolicy auto-applies to any future palette logging)"
    - "Plan 06-02 (MainWindowViewModel ctor shape with ToastStackViewModel; App.ConfigureServices palette block lands alongside the toast block)"
    - "src/Deskbridge.Core/Services/ConnectionQueryService.cs (dual-score algorithm — CMD-03 mirrored in ScoreCommand, Title=100 / Alias=80 / Subseq=40)"
    - "src/Deskbridge.Core/Interfaces/IConnectionQuery.cs (Search + GetRecent — palette reuses both)"
    - "src/Deskbridge/KeyboardShortcutRouter.cs (Phase 5 router — extended with Ctrl+Shift+P / Ctrl+N / Ctrl+T / F11 / Esc without disturbing Ctrl+Tab / Ctrl+1-9 / Ctrl+F4 / Ctrl+Shift+T)"
    - "src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml.cs (precedent for ContentDialog subclass + GetDialogHostEx() + PreviewKeyDown Enter handler)"
    - "src/Deskbridge/App.xaml (converter registration point — NullToCollapsedConverter added alongside BoolToVisibility)"
  provides:
    - "Deskbridge.Core.Interfaces.IAppLockState (IsLocked + Lock/Unlock + LockStateChanged — Plan 06-04 invokes Lock on startup before master-password verification)"
    - "Deskbridge.Core.Services.AppLockState (default IsLocked=false scaffolding; Plan 06-04 consumer unchanged)"
    - "Deskbridge.Core.Models.CommandEntry (record — Id, Title, Subtitle?, Aliases, Icon, Shortcut?, ExecuteAsync)"
    - "Deskbridge.Core.Interfaces.ICommandPaletteService (Commands registry + ScoreCommand scorer)"
    - "Deskbridge.Core.Services.CommandPaletteService (4 D-04 commands + ScoreCommand substring/subseq parity with ConnectionQueryService)"
    - "Deskbridge.ViewModels.CommandPaletteRowViewModel (unified row — ConnectionModel path publishes ConnectionRequestedEvent; CommandEntry path invokes ExecuteAsync)"
    - "Deskbridge.ViewModels.CommandPaletteViewModel (empty-state GetRecent(5)+commands / ranked-merge Search+ScoreCommand)"
    - "Deskbridge.Dialogs.CommandPaletteDialog (ui:ContentDialog subclass; IsFooterVisible=False; Pitfall 8 Enter handler)"
    - "Deskbridge.Converters.NullToCollapsedConverter (hides controls when bound value is null or empty string)"
    - "MainWindowViewModel.OpenCommandPaletteCommand / QuickConnectCommand / DisconnectAllCommand / ToggleFullscreenCommand / ExitFullscreenCommand / IsFullscreen property"
    - "KeyboardShortcutRouter extended with Ctrl+Shift+P / Ctrl+N / Ctrl+T / F11 / Esc handling (Ctrl+Shift+T still wins — Shift branch checked first)"
  affects:
    - "src/Deskbridge/App.xaml (added NullToCollapsedConverter resource)"
    - "src/Deskbridge/App.xaml.cs (4 new DI registrations — IAppLockState singleton, ICommandPaletteService singleton with factory, CommandPaletteViewModel + CommandPaletteDialog transient, Func<CommandPaletteDialog> factory)"
    - "src/Deskbridge/MainWindow.xaml.cs (2 new ctor params; OnPreviewKeyDown Ctrl+Shift+P gate + OpenCommandPaletteAsync + OnViewModelPropertyChanged fullscreen handler)"
    - "src/Deskbridge/ViewModels/MainWindowViewModel.cs (5 new [RelayCommand]s + IsFullscreen observable)"
    - "src/Deskbridge/KeyboardShortcutRouter.cs (extended TryRoute with F11/Esc/Ctrl+Shift+P/Ctrl+N/Ctrl+T branches)"
    - "tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs (10 new router tests)"
    - "tests/Deskbridge.Tests/DiCompositionTests.cs (4 new tests — IAppLockState singleton, MainWindow Ctrl+Shift+P gate source-check, Pitfall 8 handler source-check, CommandPaletteService exactly-4 source-check)"
tech-stack:
  added:
    - "(none — all dependencies already pinned: WPF-UI SymbolRegular, CommunityToolkit.Mvvm, Serilog)"
  patterns:
    - "DI factory closure for ICommandPaletteService — service-level command delegates resolve VM singletons lazily so the palette service doesn't depend on the WPF exe project"
    - "Score proxy via 100-index for connection rows — preserves IConnectionQuery.Search ordering without refactoring the Phase 3 interface to expose numeric scores"
    - "Ctrl+Shift+P intercepted at MainWindow.OnPreviewKeyDown (NOT in KeyboardShortcutRouter) because the router has no IContentDialogService / IAppLockState dependency — keeps the router pure-data"
    - "Source-grep assertions for MainWindow Ctrl+Shift+P gate + Pitfall 8 handler + 4-command invariant (same pattern Plan 06-01/06-02 used for CrashHandler / App.OnStartup source-order)"
    - "Transient dialog + VM per palette session — SearchText starts blank on every open, Items re-populates from current state"
    - "OnViewModelPropertyChanged handler for IsFullscreen — keeps Window control out of the VM while still letting the router Toggle/Exit commands flip WindowStyle+WindowState"
key-files:
  created:
    - "src/Deskbridge.Core/Interfaces/IAppLockState.cs"
    - "src/Deskbridge.Core/Services/AppLockState.cs"
    - "src/Deskbridge.Core/Models/CommandEntry.cs"
    - "src/Deskbridge.Core/Interfaces/ICommandPaletteService.cs"
    - "src/Deskbridge.Core/Services/CommandPaletteService.cs"
    - "src/Deskbridge/ViewModels/CommandPaletteRowViewModel.cs"
    - "src/Deskbridge/ViewModels/CommandPaletteViewModel.cs"
    - "src/Deskbridge/Dialogs/CommandPaletteDialog.xaml"
    - "src/Deskbridge/Dialogs/CommandPaletteDialog.xaml.cs"
    - "src/Deskbridge/Converters/NullToCollapsedConverter.cs"
    - "tests/Deskbridge.Tests/Palette/CommandPaletteServiceTests.cs"
    - "tests/Deskbridge.Tests/Palette/CommandPaletteViewModelTests.cs"
  modified:
    - "src/Deskbridge/App.xaml"
    - "src/Deskbridge/App.xaml.cs"
    - "src/Deskbridge/MainWindow.xaml.cs"
    - "src/Deskbridge/KeyboardShortcutRouter.cs"
    - "src/Deskbridge/ViewModels/MainWindowViewModel.cs"
    - "tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs"
    - "tests/Deskbridge.Tests/DiCompositionTests.cs"
decisions:
  - "Ctrl+Shift+P handled DIRECTLY in MainWindow.OnPreviewKeyDown rather than via KeyboardShortcutRouter.TryRoute — the router is pure-data (Phase 5 D-16) with no dependency surface; adding IContentDialogService or IAppLockState to TryRoute would break its test seam. MainWindowViewModel.OpenCommandPaletteCommand is a no-op placeholder kept so the router has a consistent ICommand surface."
  - "Connection-row score proxy = 100 - index (not a direct IConnectionQuery refactor). IConnectionQuery.Search returns IReadOnlyList<ConnectionModel> without numeric scores. Exposing per-result scores would change a Phase 3 surface consumed by Phase 3 tests. 100-index preserves the Search ordering relative to itself and lets commands interleave by their ScoreCommand output (0-100)."
  - "ICommandPaletteService registered via DI FACTORY (not straight AddSingleton<ICommandPaletteService, CommandPaletteService>) — the 4 ExecuteAsync closures must delegate to the ViewModel-hosted commands. The factory resolves MainWindowViewModel + ConnectionTreeViewModel at service-provider time and captures their IRelayCommand surfaces as closures. This keeps Deskbridge.Core free of Deskbridge exe types."
  - "AppLockState.Lock/Unlock are idempotent — calling Lock() when already locked is a no-op and does NOT re-raise LockStateChanged. Protects future Plan 06-04 consumers (toast subscribers, audit logger) from duplicate Locked/Unlocked events when the master-password verify retries."
  - "F11 matched by the router BEFORE the `!ctrl` guard (it's a non-Ctrl shortcut). Esc has an early-exit branch too — but only returns true when IsFullscreen is true; otherwise returns false so Esc can bubble to ContentDialog for its native backdrop-close (critical for the palette's own dismissal)."
  - "ScoreCommand substring-before-subseq gate mirrors ConnectionQueryService.CalculateScore's `if (score == 0)` fallback. Without this gate 'new' would score 100 (substring) AND 40 (subsequence) on 'New Connection' — substring wins and the subseq path never fires, matching Phase 3 behaviour."
  - "Command tie-breaker: on equal Score, commands come BEFORE connections (ThenBy(IsCommand ? 0 : 1)). UI-SPEC rationale — when a user types 'new' they almost always mean the command, not the connection named 'new-box'."
  - "CommandPaletteDialog + CommandPaletteViewModel are TRANSIENT (not singleton) — one instance per Ctrl+Shift+P. SearchText starts blank every open; the empty-state recents refresh from current state (user may have used more connections since the last open)."
  - "Enter-in-TextBox handler (Pitfall 8) is internal (not private) so DiCompositionTests.CommandPaletteDialog_Has_Pitfall8_EnterHandler can verify by source-grep. Full unit test with KeyEventArgs construction would require reflection into internal-only WPF ctors — source-grep catches the regression more reliably."
metrics:
  duration_minutes: 14
  completed_date: "2026-04-15"
  tasks: 3
  files_created: 12
  files_modified: 7
  tests_added: 45
---

# Phase 6 Plan 03: Command Palette + Keyboard Shortcuts Summary

**VS Code-style Ctrl+Shift+P floating palette (CMD-01 / CMD-02 / CMD-03) merges fuzzy-matched connections with 4 D-04 commands, plus Ctrl+N / Ctrl+T / F11 / Esc global shortcuts (CMD-04) and an IAppLockState Q6 gate that Plan 06-04 will activate.**

## Performance

- **Duration:** 14 min
- **Started:** 2026-04-15T11:54:15Z
- **Completed:** 2026-04-15T12:08:26Z
- **Tasks:** 3
- **Files created:** 12
- **Files modified:** 7
- **Tests added:** 45 (16 CommandPaletteServiceTests + 15 CommandPaletteViewModelTests + 10 KeyboardShortcutTests + 4 DiCompositionTests)

## What Was Built

### 1. IAppLockState + CommandPaletteService + CommandEntry (Task 1 — CMD-02, CMD-03, Q6)

`IAppLockState` + default `AppLockState` shipped in `Deskbridge.Core.Interfaces` / `.Services`. `IsLocked` starts `false`; Plan 06-04 will invoke `Lock()` on startup before master-password verification and `Unlock()` on success. `Lock` and `Unlock` are idempotent — calling either while already in that state is a no-op and does NOT re-raise `LockStateChanged`.

`CommandEntry` record with `Id / Title / Subtitle? / Aliases / Icon / Shortcut? / ExecuteAsync`. Registered in `CommandPaletteService` as exactly the 4 D-04 commands with UI-SPEC copy verbatim:

| Id | Title | Shortcut | Icon | Aliases |
|----|-------|----------|------|---------|
| new-connection | New Connection | Ctrl+N | Add24 | create, add |
| settings | Settings | null | Settings24 | preferences, options |
| disconnect-all | Disconnect All | null | PlugDisconnected24 | close all, end all |
| quick-connect | Quick Connect | Ctrl+T | PlugConnected24 | qc, connect to |

`ScoreCommand` mirrors `ConnectionQueryService.CalculateScore` — substring on Title = 100, substring on any Alias = 80, subsequence fallback on Title = 40 (only when no substring matched — matches Phase 3's `if (score == 0)` gate).

**Test coverage:** 16 tests — 4 registry (Commands.Count == 4, each command's Title/Shortcut/Icon matches UI-SPEC, ExecuteAsync invokes ctor closure), 8 scoring (Title-100, Alias-80, Subseq-40, case-insensitive, empty-query → 0, no-match → 0, substring-priority-over-subseq), 4 AppLockState scaffolding (default unlocked, Lock+Unlock raises event, idempotent Lock, idempotent Unlock).

### 2. CommandPaletteRowViewModel + CommandPaletteViewModel (Task 2 — CMD-01, CMD-03, D-02)

`CommandPaletteRowViewModel`: unified row with two ctors. **Connection ctor** — wraps a `ConnectionModel`; Title=Name (or Hostname if Name empty); Subtitle=null when Title equals Hostname (UI-SPEC avoids "srv01 / srv01" duplication); Icon=Desktop24; Shortcut=null; IsCommand=false. `ExecuteAsync` publishes `ConnectionRequestedEvent` so the Phase 4 pipeline takes over. **Command ctor** — wraps a `CommandEntry`; Title/Subtitle/Icon/Shortcut copied from the entry; IsCommand=true. `ExecuteAsync` invokes the delegate.

`CommandPaletteViewModel`:

- **Empty state** (D-02): `GetRecent(5)` connection rows FIRST, then the 4 commands. The 5-recent cap is hardcoded to the D-02 contract.
- **Ranked merge** (CMD-03): `_query.Search(q)` called **exactly once** per SearchText change (CMD-03 guarantee, verified by NSubstitute `Received(1)`). Commands scored via `ScoreCommand`; connections scored via `100 - index` proxy. Merge order: `OrderByDescending(Score).ThenBy(IsCommand ? 0 : 1).ThenBy(Title, OrdinalIgnoreCase)` — commands win ties over connections per RESEARCH Pattern 2.2.
- `SelectedItem` auto-sets to `Items.FirstOrDefault()` after every refresh so Enter fires the best match without manual navigation.
- `ExecuteSelectedAsync` [RelayCommand] — no-op when `SelectedItem` is null.

**Test coverage:** 15 tests — empty-state recents+commands ordering (+ zero-recents variant), `Search.Received(1)` per keystroke (+ `GetRecent.DidNotReceive`), commands-before-connections tie-break, connection-row search-order preservation, SelectedItem auto-track, `ExecuteSelectedAsync` connection path (Publish verified on IEventBus), command path (delegate invoked), null-selection no-op, Subtitle hidden when Title==Hostname (and visible when they differ), Desktop24 icon on connection rows, command rows copy Icon+Shortcut+CommandId from CommandEntry, subsequent-search-change regression (Received(1) PER unique query).

### 3. KeyboardShortcutRouter + MainWindow + App.xaml.cs Wiring (Task 3 — CMD-01, CMD-04, D-05, Q6)

**`KeyboardShortcutRouter.TryRoute` extended** with 5 new branches WITHOUT disturbing Phase 5's Ctrl+Tab / Ctrl+Shift+Tab / Ctrl+F4 / Ctrl+1-9 / Ctrl+Shift+T paths:

- `F11` (no Ctrl/Alt) → `ToggleFullscreenCommand` (D-05 / CMD-04). Matched BEFORE the `!ctrl` guard.
- `Esc` (plain) → `ExitFullscreenCommand` IF `IsFullscreen`, otherwise returns **false** so Esc bubbles to ContentDialog for its native backdrop-close (critical for the palette's own dismissal).
- `Ctrl+Shift+P` → `OpenCommandPaletteCommand` (no-op placeholder; real open in MainWindow).
- `Ctrl+N` → `ConnectionTree.NewConnectionCommand` (reuses Phase 3).
- `Ctrl+T` (no Shift) → `QuickConnectCommand`. Ctrl+Shift+T still wins for `ReopenLastClosed` — the Shift-branch is checked first in the router body.

**`MainWindowViewModel`** gains 5 `[RelayCommand]`s: `OpenCommandPalette` (no-op — real open is in MainWindow.OnPreviewKeyDown), `QuickConnect` (delegates to `ConnectionTree.NewConnectionCommand` — a dedicated quick-connect dialog is deferred per plan), `DisconnectAll` (`ITabHostManager.CloseAllAsync`), `ToggleFullscreen` / `ExitFullscreen` (flip `IsFullscreen` observable). `IsFullscreen` is `[ObservableProperty]`.

**`CommandPaletteDialog.xaml` + `.xaml.cs`** (UI-SPEC compliant):

- `ui:ContentDialog` subclass; `IsFooterVisible="False"`; `DialogMaxWidth="480"`; `DialogMaxHeight="480"`.
- `DockPanel` (`Width="480"`): 48px search row (Search24 icon + `ui:TextBox` + Esc badge) + `ListBox` (`MaxHeight=400`) bound to `Items` with a `DataTemplate` for `CommandPaletteRowViewModel` (icon + title/subtitle + shortcut badge).
- Every brush via `DynamicResource` (no hardcoded hex). `BasedOn` style applied per WPF-UI Pitfall 1.
- `Subtitle` TextBlock and `Shortcut` badge visibility bound via `NullToCollapsedConverter` (new converter registered in `App.xaml` as `NullToCollapsed`).
- Code-behind ctor: accepts `CommandPaletteViewModel` + `IContentDialogService` (`GetDialogHostEx()` per Phase 3 precedent). `Loaded` focuses SearchBox. `PreviewKeyDown` Enter handler (Pitfall 8 mitigation) — when focus is in a `TextBoxBase`, intercept Enter → `ExecuteSelectedAsync()` + `Hide()` + `e.Handled=true`. Marked `internal` so DiComposition source-grep can verify the regression.

**`MainWindow.xaml.cs` wiring:**

- 2 new ctor params: `IAppLockState lockState`, `Func<CommandPaletteDialog> paletteFactory`.
- `OnPreviewKeyDown` intercepts **Ctrl+Shift+P FIRST** (before `KeyboardShortcutRouter.TryRoute`) — `if (!_lockState.IsLocked) _ = OpenCommandPaletteAsync();` then `e.Handled=true`. Q6 gate: palette is a no-op while locked (so it can't render above the future lock overlay), but the key is still consumed so it doesn't leak to the focused AxHost.
- `OpenCommandPaletteAsync` guards with `_paletteOpen` (idempotent — held-down Ctrl+Shift+P doesn't stack dialogs) and swallows exceptions with a `Serilog.Log.Warning`.
- `OnViewModelPropertyChanged` observes `IsFullscreen` and applies `WindowStyle.None + WindowState.Maximized` on true; restores `_savedWindowStyle / _savedWindowState` on false.

**`App.xaml.cs` DI:**

- `IAppLockState` → `AppLockState` singleton.
- `ICommandPaletteService` → singleton via factory closure (resolves `MainWindowViewModel` + `ConnectionTreeViewModel` singletons and captures their `IRelayCommand` surfaces as `Func<Task>` delegates).
- `CommandPaletteViewModel` + `CommandPaletteDialog` transient (fresh instance per palette open).
- `Func<CommandPaletteDialog>` transient factory.

**Test coverage:** 10 new `KeyboardShortcutTests` (Ctrl+Shift+P handled / Ctrl+N delegates to ConnectionTree / Ctrl+T no-Shift handled / F11 toggle + Alt guard / Esc in fullscreen + passthrough + Ctrl guard / Ctrl+Shift+T regression unchanged / Ctrl+Shift+N unhandled) + 4 new `DiCompositionTests` (IAppLockState singleton / MainWindow Ctrl+Shift+P gate source-check / Pitfall 8 handler source-check / exactly-4 commands source-check).

## Commit Trail

| Hash | Title |
|------|-------|
| `454b2f7` | feat(06-03): add IAppLockState + ICommandPaletteService + CommandEntry (CMD-02, CMD-03, Q6) |
| `7e88fd0` | feat(06-03): add CommandPaletteRowViewModel + CommandPaletteViewModel (CMD-01, CMD-03, D-02) |
| `c5aa3ee` | feat(06-03): extend KeyboardShortcutRouter + MainWindowViewModel commands (CMD-04, D-05) |
| `1255b7f` | feat(06-03): add CommandPaletteDialog + NullToCollapsedConverter (CMD-01, Pitfall 8) |
| `c16b1b3` | feat(06-03): wire palette + IAppLockState + fullscreen into MainWindow + App DI (CMD-01, Q6, D-05) |

## Test Results

**Plan 06-03 tests added:** 45 (16 `CommandPaletteServiceTests` + 15 `CommandPaletteViewModelTests` + 10 new `KeyboardShortcutTests` + 4 new `DiCompositionTests`).

**Full suite:** `dotnet test Deskbridge.sln` → **408 passed, 0 failed, 3 skipped** (363 prior + 45 new).

**Build:** `dotnet build Deskbridge.sln` → **0 warnings, 0 errors** (TreatWarningsAsErrors enforced).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Blocking] `PanelMode` namespace wrong in App.xaml.cs**
- **Found during:** Task 3d build
- **Issue:** Plan's action step 6 referenced `Models.PanelMode` without qualifying the namespace. Initial `using Deskbridge.Core.Models;` compiled the `CommandEntry` references but `PanelMode` lives in `Deskbridge.Models` (exe project), not `Deskbridge.Core.Models`. Build failed with `CS0103: The name 'PanelMode' does not exist`.
- **Fix:** Removed `using Deskbridge.Core.Models;` (the CommandEntry reference was already resolved via `Deskbridge.Core.Services` for the `CommandPaletteService` ctor), added `using Deskbridge.Models;` for `PanelMode`.
- **Files modified:** `src/Deskbridge/App.xaml.cs`
- **Commit:** `c16b1b3`

### Plan-Assumption Corrections (no deviation tracked — plan-vs-existing reconciliation)

- **Test file location.** Plan referenced `tests/Deskbridge.Tests/KeyboardShortcutRouterTests.cs` (Task 3 action step 7) but the Phase 5 router tests actually live at `tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs`. Executor extended the existing file rather than creating a sibling file that would duplicate the helper setup.
- **ScoreCommand alias edge-case test.** Initial `[Theory]` row `[InlineData("qc", "quick-connect", 100)]` assumed "qc" would match Title — but "Quick Connect" does not contain "qc" as a substring; it matches the alias "qc" at score 80. The test body includes an explicit branch for the "qc" case to assert 80 (kept in test for documentation; future cleanup can split into two theory rows).
- **`GetRecent` sort key.** Plan said `LastUsedAt`, actual `ConnectionQueryService.GetRecent` sorts by `UpdatedAt` (Phase 3 implementation). Plan reads recent connections via `IConnectionQuery.GetRecent(5)` which already returns the correct order — the VM doesn't care about the underlying sort field. No behavioural change.

### Test-design adjustments (no behavioural deviation)

- **Pitfall 8 Enter handler test is a source-grep (DiComposition) instead of a direct KeyEventArgs invocation.** `KeyEventArgs` has internal-only ctors and reflecting into them would couple the test to WPF internals. DiCompositionTests.CommandPaletteDialog_Has_Pitfall8_EnterHandler asserts the handler body contains `Key.Enter` + `TextBoxBase` + `ExecuteSelectedAsync` + `e.Handled = true`, matching the Plan 06-01 CrashHandler Dispatcher-hook pattern.
- **MainWindow Ctrl+Shift+P gate verified via source-grep rather than MainWindow instantiation.** Same rationale as Pitfall 8 — `DiCompositionTests.MainWindow_CtrlShiftP_Gated_By_LockState_IsLocked` asserts the source file contains `Key.P` + `ModifierKeys.Control | ModifierKeys.Shift` + `_lockState.IsLocked` + `OpenCommandPaletteAsync`. Covers the Q6 regression contract without attempting to instantiate MainWindow on a per-test STA thread.

**Total deviations:** 1 auto-fixed (Rule 3 blocking — namespace). Necessary for build.

**Impact on plan:** Scope unchanged; plan executed as designed.

## Authentication Gates Encountered

None.

## Known Stubs

| Location | Stub | Reason / Resolution |
|----------|------|---------------------|
| `src/Deskbridge/ViewModels/MainWindowViewModel.cs` (OpenCommandPalette command) | Returns `Task.CompletedTask` — no action. The real palette open is in `MainWindow.OnPreviewKeyDown` → `OpenCommandPaletteAsync()`. | **Intentional** — the router needs a consistent ICommand surface for Ctrl+Shift+P (so it can return `true` and consume the key before it leaks to AxHost). The real open lives in the Window because it needs `IContentDialogService` and `IAppLockState` that the VM shouldn't carry. Plan decision 1. |
| `src/Deskbridge/ViewModels/MainWindowViewModel.cs` (QuickConnect command) | Delegates to `ConnectionTree.NewConnectionCommand` — no dedicated quick-connect dialog. | **Intentional** — per plan Task 3 action step 3, v1 reuses NewConnection. A dedicated "type a hostname and Enter" dialog is a v1.1 follow-up beyond Phase 6 scope. |

No other stubs introduced.

## TODOs for Plan 06-04 (App Security)

- Invoke `_lockState.Lock()` at app startup (App.OnStartup) BEFORE `mainWindow.Show()` so Ctrl+Shift+P is gated until the master password is verified.
- Invoke `_lockState.Unlock()` from the master-password verification success handler.
- Add lock overlay UI that renders atop `MainWindow` when `IAppLockState.IsLocked` is true — the Q6 gate on Ctrl+Shift+P protects against key-leak, but the overlay is what prevents the user from interacting with the visible tree / tabs.
- Route `AppLockedEvent` / `AppUnlockedEvent` publications through `IAuditLogger` (Plan 06-01 LOG-02).
- Publisher for `ConnectionImportedEvent` is still Phase 7 (unchanged from Plan 06-02).

## Threat Model Coverage

| Threat | Mitigation Landed | Verification |
|--------|-------------------|--------------|
| T-06-CMD-01 (Elevation of Privilege — palette bypassing lock) | `_lockState.IsLocked` check in `MainWindow.OnPreviewKeyDown` Ctrl+Shift+P branch. Plan 06-04 flips IsLocked=true on startup so the gate is armed before the first keystroke. | `DiCompositionTests.MainWindow_CtrlShiftP_Gated_By_LockState_IsLocked` (source-grep on `_lockState.IsLocked` + `Key.P` + `Control | Shift`) |
| T-06-CMD-02 (Information Disclosure — connection metadata in palette while locked) | Same gate — palette never opens while locked, so no connection names/hostnames render. Even without the gate, connection metadata is non-secret per CONTEXT D-17 (secrets live in Windows Credential Manager, not the palette data stream). | Same source-grep test. `CommandPaletteViewModelTests` also verifies that `ExecuteSelectedAsync` only publishes `ConnectionRequestedEvent` (no Credential-level access). |

No new threat flags introduced beyond the `<threat_model>` block in the plan. No new network / auth / trust-boundary surfaces.

## Self-Check: PASSED

- `src/Deskbridge.Core/Interfaces/IAppLockState.cs` — FOUND
- `src/Deskbridge.Core/Services/AppLockState.cs` — FOUND
- `src/Deskbridge.Core/Models/CommandEntry.cs` — FOUND
- `src/Deskbridge.Core/Interfaces/ICommandPaletteService.cs` — FOUND
- `src/Deskbridge.Core/Services/CommandPaletteService.cs` — FOUND
- `src/Deskbridge/ViewModels/CommandPaletteRowViewModel.cs` — FOUND
- `src/Deskbridge/ViewModels/CommandPaletteViewModel.cs` — FOUND
- `src/Deskbridge/Dialogs/CommandPaletteDialog.xaml` — FOUND
- `src/Deskbridge/Dialogs/CommandPaletteDialog.xaml.cs` — FOUND
- `src/Deskbridge/Converters/NullToCollapsedConverter.cs` — FOUND
- `tests/Deskbridge.Tests/Palette/CommandPaletteServiceTests.cs` — FOUND
- `tests/Deskbridge.Tests/Palette/CommandPaletteViewModelTests.cs` — FOUND
- Commit `454b2f7` — FOUND
- Commit `7e88fd0` — FOUND
- Commit `c5aa3ee` — FOUND
- Commit `1255b7f` — FOUND
- Commit `c16b1b3` — FOUND

---
*Phase: 06-cross-cutting-features*
*Plan: 03*
*Completed: 2026-04-15*
