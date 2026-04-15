---
phase: 06-cross-cutting-features
created: 2026-04-14
researcher: gsd-researcher
status: ready_for_planning
requirement_ids:
  - CMD-01
  - CMD-02
  - CMD-03
  - CMD-04
  - NOTF-01
  - NOTF-02
  - NOTF-03
  - NOTF-04
  - LOG-01
  - LOG-02
  - LOG-03
  - LOG-04
  - LOG-05
  - SEC-01
  - SEC-02
  - SEC-03
  - SEC-04
  - SEC-05
confidence: HIGH (stack verified, patterns verified, two deviations from UI-SPEC surfaced)
---

# Phase 6: Cross-Cutting Features — Research

**Researched:** 2026-04-14
**Domain:** WPF + WPF-UI cross-cutting services — keyboard palette, toast stack, Serilog+jsonl audit, PBKDF2 auth + lock overlay + idle timer + session-switch hook
**Confidence:** HIGH overall; two LOW-confidence deviations from UI-SPEC surfaced (see Summary)

## Summary

Phase 6 is four feature groups that all consume the existing event bus and reuse the existing WPF-UI dialog/snackbar infrastructure already established in Phases 1–5. The stack is fully decided by CLAUDE.md and CONTEXT.md: Serilog 4.3.* (already wired in Phase 4 baseline), System.Text.Json in-box, `Rfc2898DeriveBytes.Pbkdf2`, `Microsoft.Win32.SystemEvents.SessionSwitch`, WPF-UI 4.2.* `ContentDialog` + `Snackbar`. No new packages are introduced.

Two concrete contradictions between the UI-SPEC and the verified library behaviour are surfaced below — both are deferred to `/gsd-plan-phase` for resolution because they change visible behaviour, not architecture:

1. **WPF-UI `SnackbarPresenter` is a FIFO queue, not a stack.** It shows exactly ONE snackbar at a time and enqueues the rest (`protected Queue<Snackbar>`). The UI-SPEC D-07 promise of "max 3 visible simultaneously, newest on top" is **not achievable with the stock presenter**. Options documented below.
2. **PBKDF2 iteration count is from 2023 OWASP guidance (600,000 for SHA-256).** OWASP's current primary recommendation for new systems is Argon2id with memory=19MiB/iter=2/parallel=1, with PBKDF2 600,000 offered only as a FIPS-140 fallback. Deskbridge is not FIPS-regulated. The CONTEXT.md locks PBKDF2 (D-20 implicit via SEC-01 "PBKDF2-hashed"), so we stay on PBKDF2-HMAC-SHA-256 @ 600,000 iterations — but flag this for the user as an `[ASSUMED]` threat-model decision.

Everything else is mechanical: `Destructure.ByTransforming<T>` (Serilog), `SystemEvents.SessionSwitch` with strong-reference handler + Dispatcher marshal, `SemaphoreSlim`-guarded `File.AppendAllTextAsync` for audit.jsonl, `ContentDialog` with `IsFooterVisible="False"` for the lock overlay, PasswordBoxAssistant attached-property pattern (plain `string`, not `SecureString`, per project constraint), and three-hook exception handler registered inside `Program.Main` after `VelopackApp.Build().Run()`.

**Primary recommendation:** Land the four plans in the order locked by D-20 (Logging → Notifications+Window State → Palette+Shortcuts → Security). Build Logging first because audit-event publishing is used by all three other groups (connect/disconnect audit lines, lock/unlock audit lines, import audit lines — the last one is in Phase 7 but will consume the same IAuditLogger). For every design claim not verified against an official doc below, see §Assumptions Log.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Command Palette:**

- **D-01 Layout:** VS Code-style centered floating — 480×auto, `ContentDialog` host pattern, dim backdrop over the whole window. Anchored near top. NOT a title-bar drop-down.
- **D-02 Empty state (no query typed):** Top 5 recent connections (sorted by `LastUsedAt` descending) followed by command entries. No placeholder hint text.
- **D-03 Fuzzy ranking:** Reuse `IConnectionQuery.Search()` dual-score unchanged (100/80/60 substring + 40/30 subsequence). Commands use the same scorer over `{name, aliases, group}`.
- **D-04 Commands list (CMD-02):** New Connection · Settings · Disconnect All · Quick Connect. Keyboard-bind `Ctrl+N` → New Connection, `Ctrl+T` → Quick Connect. Ctrl+W (CMD-04) already wired in Phase 5; F11/Esc fullscreen covered below.
- **D-05 F11/Esc fullscreen:** F11 toggles app fullscreen (WPF `WindowState.Maximized` + `WindowStyle.None`). Esc exits fullscreen. Scope note: this is APP fullscreen, not RDP session fullscreen — the latter is AxHost-controlled in Phase 4.

**Notifications:**

- **D-06 Toast severity mapping:** connected=info/2s, disconnected=info/3s, reconnecting=warning/sticky, failed=error/sticky, updates-available=info/sticky, audit events=log-only.
- **D-07 Stack behaviour:** Max 3 visible simultaneously. Newest on top. 4th toast evicts oldest. Hover pauses auto-dismiss (WPF-UI Snackbar default).
- **D-08 No modals for non-critical (NOTF-02):** Only the crash dialog (D-11) and the lock overlay (D-16) are modal. All other user feedback is toasts.
- **D-09 Window state persistence (NOTF-04) — own plan:** Save `{x, y, width, height, isMaximized, sidebarOpen, sidebarWidth}` to `%AppData%/Deskbridge/settings.json` on `Window.Closing`. Load on `Window.SourceInitialized` with fallback defaults. `System.Text.Json` source-generated, no Newtonsoft.

**Logging & Audit:**

- **D-10 Audit schema (per jsonl line):** `{"ts":"<ISO UTC>","type":"<EventType>","connectionId":"<guid|null>","user":"<WindowsUsername>","outcome":"<success|fail>","errorCode":"<optional>"}`. No IPs, no durations, no source fields.
- **D-11 Global exception handler:** recoverable → toast + log; unhandled (three hooks) → minimal crash dialog + [Copy Details] + [Restart]. No stack trace visible.
- **D-12 No credentials in logs (LOG-05):** Password fields have `[JsonIgnore]`, never interpolated. Serilog destructuring rejects `Password`, `Secret`, `CredentialData`, `Token`. Unit test: assert no plaintext password in full-run log sample.
- **D-13 Audit rotation:** Monthly. `audit-YYYY-MM.jsonl`. Append-only. Old months retained (no size cap in v1).

**App Security:**

- **D-14 Auto-lock trigger:** Deskbridge activity only resets timer. `SystemEvents.SessionSwitch` (Windows lock / user switch) DOES trigger immediate lock (SEC-04).
- **D-15 Lock overlay visual:** Full-window opaque (uses `DeskbridgeBackgroundBrush`), centered password card (~360px). NOT dim+blur.
- **D-16 Password recovery:** NONE. Forgotten master password = delete `%AppData%/Deskbridge/auth.json` and reset settings.
- **D-17 Master-password ↔ credential access — INDEPENDENT.** Master password gates the Deskbridge UI. Windows Credential Manager remains OS-level.
- **D-18 Ctrl+L manual lock (SEC-04):** Registered at `MainWindow.PreviewKeyDown` level. Idempotent.
- **D-19 Lock-on-minimise (SEC-05):** Configurable in Settings, default OFF. When ON: `Window.StateChanged` → `Minimized` → lock immediately.

**Phase Sequencing:**

- **D-20 Plan order (4 plans, 4 waves):** 06-01 Logging & Audit (Wave 1), 06-02 Notifications + Window State (Wave 2), 06-03 Command Palette + Shortcuts (Wave 3), 06-04 App Security (Wave 4). Serialised because each plan touches MainWindow.xaml.

### Claude's Discretion

- Exact Serilog `Destructure.*` API shape for credential stripping
- Exact PBKDF2 iteration count / salt length / output length parameters
- `SystemEvents.SessionSwitch` subscription lifetime + thread marshalling pattern
- `audit.jsonl` concurrent-write synchronisation primitive
- Lock overlay `ContentDialog` vs custom UserControl pattern (airspace interaction)
- Crash dialog button-role mapping (Primary left vs expected-action right)
- `PasswordBox.Password` bindability workaround (attached property vs code-behind)
- Fuzzy-match performance threshold + debouncing for >500 connections
- Auto-lock timer class selection (`DispatcherTimer` vs `System.Timers.Timer`) + suspend/resume behaviour
- Global exception hook registration order relative to Velopack custom Main
- Composition-root eager-resolution order for Phase 6 singletons
- `settings.json` vs `auth.json` schema layout + migration story

### Deferred Ideas (OUT OF SCOPE)

- Per-connection audit filtering / export UI — v1.1 or Phase 7
- Session recording — explicitly Out of Scope for v1
- Remote log shipping (syslog, Seq) — v1.1+
- Biometric unlock (Windows Hello) — v1.2 Enterprise
- Multiple master-password profiles / shared-vault mode — not in v1
- Richer audit fields (IPs, durations, source host) — v1.1
- Toast grouping / do-not-disturb mode — v1.1
- Command palette extension API / user-defined commands — not in v1
- Password-strength meter — deliberate omission (D-16 rationale)
- "Open log folder" settings button — v1.1
- Audit log viewer UI — v1.2 Enterprise (ENT-04)

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| CMD-01 | Ctrl+Shift+P opens floating search box with fuzzy match across connections and commands | §Architecture Pattern 2 (command palette via `ui:ContentDialog`); §Code Examples: `IContentDialogService.ShowAsync` + existing Phase 3 pattern using `GetDialogHostEx()` |
| CMD-02 | Commands: New Connection, Settings, Disconnect All, Quick Connect | §Architecture Pattern 2.1 (`ICommandEntry` interface, `ObservableCollection<ICommandEntry>` bound to ListBox); D-04 locks the 4 commands |
| CMD-03 | Connection results consume `IConnectionQuery.Search()` for consistent matching | §Fuzzy-match Performance — existing ConnectionQueryService scales O(N) with N connections; HIGH confidence OK up to ~5000 |
| CMD-04 | Ctrl+N, Ctrl+T, Ctrl+W, F11, Esc | §Architecture Pattern 2.2 (reuse existing `KeyboardShortcutRouter.TryRoute` pattern from Phase 5); Ctrl+N already live from 03-04 |
| NOTF-01 | Toast stack (bottom-right) for connection events | §Architecture Pattern 3 (event-bus subscriber that calls `ISnackbarService.Show`); **§Pitfall 3 — WPF-UI shows one snackbar at a time, UI-SPEC D-07 max-3 claim needs resolution** |
| NOTF-02 | No modal dialogs for non-critical events | Locked by D-08; no research needed |
| NOTF-03 | Notifications auto-generated from event bus subscriptions | Existing `IEventBus.Subscribe<T>` pattern; 7 event types mapped in UI-SPEC copywriting table |
| NOTF-04 | Window state persistence | §Architecture Pattern 5 (System.Text.Json source-gen `JsonSerializerContext`, atomic write via tmp+rename like JsonConnectionStore) |
| LOG-01 | Serilog rolling file 10MB cap, 5-file rotation | §Code Examples §Serilog. Already partially wired in App.xaml.cs; needs `fileSizeLimitBytes: 10_000_000`, `retainedFileCountLimit: 5`, `rollOnFileSizeLimit: true` |
| LOG-02 | Audit log `audit.jsonl` append-only, monthly rotation | §Architecture Pattern 1 (IAuditLogger + SemaphoreSlim-guarded writer + monthly filename derivation); §Pitfall 2 |
| LOG-03 | Audit records connection events, credential changes, imports/exports, lock/unlock | `AuditAction` enum already defined in `Deskbridge.Core.Models.Enums` (16 values). Phase 6 wires one publisher per domain |
| LOG-04 | Global exception handler with per-connection error isolation | §Architecture Pattern 4 (three-hook registration in `Program.Main` immediately after Velopack init) |
| LOG-05 | Credentials never appear in log files | §Code Examples §Serilog Destructure.ByTransforming + IDestructuringPolicy custom policy pattern; PasswordBox value never logged (never enters LogContext) |
| SEC-01 | Master password prompt on first run, PBKDF2-hashed, stored in auth.json | §Architecture Pattern 6 (`Rfc2898DeriveBytes.Pbkdf2` with SHA-256, 600K iterations, 32-byte salt, 32-byte key); §Code Examples |
| SEC-02 | Full-window lock overlay on launch | §Architecture Pattern 7 — `ui:ContentDialog` subclass with `IsFooterVisible="False"` + opaque background; **§Pitfall 5 — airspace interaction with RDP AxHost must be resolved** |
| SEC-03 | Auto-lock after configurable inactivity timeout, Deskbridge input only | §Architecture Pattern 8 (`InputManager.Current.PreProcessInput` + `DispatcherTimer`); §Pitfall 6 suspend/resume |
| SEC-04 | Ctrl+L manual lock + `SystemEvents.SessionSwitch` auto-lock | §Architecture Pattern 9 (strong-ref field for the handler, Dispatcher marshal, unsubscribe on Exit); §Pitfall 7 SessionSwitch thread/lifetime |
| SEC-05 | Lock-on-minimise configurable | `Window.StateChanged` subscription in MainWindow; settings.json boolean bound to a `ui:ToggleSwitch` (UI-SPEC already locked) |

</phase_requirements>

## Project Constraints (from CLAUDE.md)

These directives were extracted from `CLAUDE.md` and take the same precedence as CONTEXT.md locked decisions. Any plan/task contradicting them must be rejected:

1. **Framework:** .NET 10 LTS (`net10.0-windows`), C# 14. No .NET 8/9. [VERIFIED: csproj targets observed during Phase 1-5]
2. **Mandatory reading:** `REFERENCE.md`, `DESIGN.md`, `WPF-UI-PITFALLS.md` (8 categories of silent WPF-UI failures). ContentDialog hosting is critical.
3. **No `CredentialManagement` NuGet, no `SecureString`** — SecureString is Microsoft-deprecated (DE0001). Plain `string` for in-memory credential handling; rely on Windows Credential Manager for storage. Master password in memory is a normal `string` in `LockOverlayViewModel`.
4. **No `[GeneratedComInterface]` / COM source generators** — classic aximp.exe interop. (Not relevant to Phase 6 but confirms ActiveX isn't touched.)
5. **No Newtonsoft.Json** — `auth.json` and `settings.json` and `audit.jsonl` all use `System.Text.Json` with source generators.
6. **No BinaryFormatter** — removed in .NET 10. Audit schema is JSON (already locked).
7. **System.Text.Json source-generators for AOT-safety** — project uses `JsonSerializerContext` pattern (see Phase 3 JsonConnectionStore for precedent).
8. **Velopack custom Main required** — `VelopackApp.Build().Run()` MUST be the first call in `Program.Main` before any WPF initialisation.
9. **Never store passwords in JSON or logs** — master password hash (PBKDF2 output) is allowed in auth.json. Raw master password string never persisted.
10. **PBKDF2 for master password hash** (per SEC-01 text). Not Argon2id. Research below documents OWASP current stance but respects the locked choice.
11. **Code style:** nullable warnings as errors, `TreatWarningsAsErrors=true`. All new code must pass `dotnet build` with 0 warnings 0 errors.
12. **xUnit v3** — test project already set up with MTP runner (no Microsoft.NET.Test.Sdk).

## Standard Stack

### Core (all already on disk, nothing new to install)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Serilog | 4.3.* (pinned, 4.3.1 observed) | Structured logging foundation | Already wired in App.xaml.cs OnStartup for LOG-01 baseline. `[VERIFIED: Directory.Packages.props]` |
| Serilog.Sinks.File | 7.0.* | Rolling file sink | Already referenced. Supports `fileSizeLimitBytes`, `rollOnFileSizeLimit`, `retainedFileCountLimit` for LOG-01 acceptance. `[VERIFIED: Directory.Packages.props]` |
| System.Text.Json | in-box .NET 10 | JSON for settings.json, auth.json, audit.jsonl | No external dep. Source-generator for AOT-safety and the jsonl append path. `[VERIFIED: .NET 10 ships STJ]` |
| `Rfc2898DeriveBytes.Pbkdf2` (static) | in-box (`System.Security.Cryptography`) | PBKDF2-HMAC-SHA-256 master-password hash | Static method (not the legacy instance ctor) is the correct API for new code. `[CITED: learn.microsoft.com/.../rfc2898derivebytes.pbkdf2]` |
| `RandomNumberGenerator.GetBytes` | in-box | Cryptographic salt generation | Replaces `new Random()` and older `RNGCryptoServiceProvider`. `[CITED: learn.microsoft.com/.../RandomNumberGenerator]` |
| `Microsoft.Win32.SystemEvents.SessionSwitch` | in-box (`Microsoft.Win32.SystemEvents` — ships with WindowsDesktop runtime) | Detects Windows lock / user-switch → force Deskbridge lock (SEC-04) | Single Windows-API-surfaced event that covers `SessionLock`, `SessionUnlock`, `ConsoleConnect`/`Disconnect`, `RemoteConnect`/`Disconnect`. `[CITED: learn.microsoft.com/.../Microsoft.Win32.SystemEvents.SessionSwitch]` |
| `System.Windows.Threading.DispatcherTimer` | in-box WPF | Auto-lock idle timer | UI-thread-affine, Tick delivered on Dispatcher thread → no cross-thread marshal needed. `[CITED: learn.microsoft.com/.../DispatcherTimer]` |
| `System.Windows.Input.InputManager.Current.PreProcessInput` | in-box WPF | Detect Deskbridge user activity to reset idle timer | Single global subscription catches every mouse/keyboard input routed through WPF (including anything in the FluentWindow). Does NOT fire for AxHost-captured events inside the RDP session — matches D-14 intent ("Deskbridge activity only"). `[CITED: blog + community; LOW behavioural verification]` |
| WPF-UI 4.2.* | pinned | `ContentDialog`, `Snackbar`, `NumberBox`, `ToggleSwitch`, `PasswordBox`, `SymbolIcon` | Already project-standard; Phase 6 adds NO new controls. `[VERIFIED: UI-SPEC Registry Safety §]` |
| CommunityToolkit.Mvvm | 8.4.* | `[ObservableProperty]` / `[RelayCommand]` on ViewModels | Already project-standard. `[VERIFIED]` |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Microsoft.Extensions.DependencyInjection` | 10.0.* | Register Phase 6 singletons | All new services go into `App.xaml.cs ConfigureServices`. Same pattern as Phase 5 `AddSingleton<ITabHostManager, TabHostManager>()`. |
| `SemaphoreSlim` | in-box | Guard `audit.jsonl` writer | One `SemaphoreSlim(1,1)` per file. `WaitAsync()` + `Release()` in `finally`. See §Pitfall 2. |
| `System.IO.File.AppendAllTextAsync` | in-box | Append line to audit.jsonl | Under the semaphore. Writes the JSON line + `\n`. |
| `Wpf.Ui.Extensions.ContentDialogServiceExtensions` | in-box to WPF-UI | `ShowSimpleDialogAsync` for crash dialog | Phase 3 precedent (`ConnectionTreeViewModel` delete-confirmation). Requires `using Wpf.Ui.Extensions;`. |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `Rfc2898DeriveBytes.Pbkdf2` (PBKDF2-HMAC-SHA-256) | Argon2id via a NuGet (e.g. `Konscious.Security.Cryptography.Argon2`) | OWASP 2026 primary recommendation is Argon2id (19 MiB memory / 2 iterations / 1 parallel). PBKDF2 600K is the FIPS-fallback. **Deskbridge locked PBKDF2 (SEC-01 literal, CLAUDE.md constraint implicit)** — but this is an `[ASSUMED]` threat-model decision that the user should confirm is acceptable. PBKDF2 for desktop local-attacker model is adequate; Argon2id is stronger but adds a third-party dep. `[CITED: cheatsheetseries.owasp.org/.../Password_Storage_Cheat_Sheet.html]` |
| `System.Windows.Threading.DispatcherTimer` | `System.Timers.Timer` | `System.Timers.Timer` fires on a thread-pool thread — needs `Dispatcher.Invoke` marshal to touch UI. DispatcherTimer fires on UI thread directly. For an idle-lock pattern that ends in showing a dialog, DispatcherTimer is mechanically simpler. **Both halt during Windows sleep and neither "catches up" on missed ticks — identical suspend/resume semantics.** `[CITED: copyprogramming.com/.../wpf-timer-behavior]` |
| `DispatcherTimer` (timer) | `SystemEvents.PowerModeChanged` + restart logic | Community recommendation is to subscribe to `PowerModeChanged` and explicitly restart the timer on resume with recalculated interval. **Deferred to a `[FOLLOW-UP]` in Plan 06-04** — 15 min timer with resume-missed-ticks is not security-critical (Windows also typically locks on resume, which `SystemEvents.SessionSwitch` catches). |
| `InputManager.PreProcessInput` (app-scoped idle reset) | `GetLastInputInfo` P/Invoke (OS-scoped idle) | `GetLastInputInfo` measures system-wide idle. D-14 explicitly rejects this ("user reading external docs with Deskbridge visible should not lock"). InputManager is app-scoped, which matches the intent. |
| `ui:ContentDialog` for lock overlay | Custom `UserControl` at MainWindow root | UI-SPEC §Lock Overlay Internal Layout specifies `ContentDialog` with `IsFooterVisible` collapsed and opaque background — chosen because `ContentDialogHost` is already in MainWindow.xaml (line 340) and `IContentDialogService` registration is established. Custom UserControl would duplicate backdrop/focus-trap/Esc-handling mechanics. **Kept as `ui:ContentDialog`.** Airspace risk addressed separately in §Pitfall 5. |
| WPF-UI `SnackbarService` (single + queue) | Custom ItemsControl stack at bottom-right | **UI-SPEC D-07 "max 3 visible simultaneously" is NOT supported by the stock `SnackbarPresenter`.** See §Pitfall 3 for options. |
| `PasswordBoxAssistant` attached property (plain string bound property) | Code-behind callback on `PasswordChanged` | Attached property lets the ViewModel treat Password as a bound string; code-behind requires the view to push the value into VM on every change. Attached property is the community-standard pattern — but **the password stays in memory as plain string in the ViewModel**, same security footprint as code-behind. Either works; code-behind is simpler and has no extra infrastructure. Use code-behind for the single lock overlay — this is one dialog, the attached property is overhead. `[CITED: multiple WPF-tutorials, LOW vs MEDIUM confidence]` |
| `Destructure.ByTransforming<T>` (per-type transform) | `Destructure.With<IDestructuringPolicy>` (catches anything matching predicate) + `UsingAttributes()` (NotLogged attribute) + `ByMaskingProperties` (third-party `Masking.Serilog` NuGet) | **Custom `IDestructuringPolicy` is the most robust in-box option** — it can scan every property name against a denylist (`Password`, `Secret`, `CredentialData`, `Token`, `ApiKey`) without requiring each type to opt in. `ByTransforming` requires a separate registration per type (brittle). `UsingAttributes` requires a third-party NuGet (`Serilog.Extras.Attributed`). **Recommendation below: custom `IDestructuringPolicy` implementation + in-code `[JsonIgnore]`-style convention, no third-party NuGet.** `[CITED: dev.to, serilog/wiki]` |

**Installation:**

No new `dotnet add package` commands are needed for Phase 6. All required libraries are already referenced in `Directory.Packages.props`. `[VERIFIED: 2026-04-14 Directory.Packages.props read]`

**Version verification (current against NuGet as of research date):**

All versions already pinned are valid. `WPF-UI 4.2.*`, `Serilog 4.3.*`, `System.Text.Json` in-box, `Microsoft.Win32.SystemEvents` ships with `Microsoft.WindowsDesktop.App.Ref` at 10.0.* (in-box). No action needed — no package bump for Phase 6.

## Architecture Patterns

### Recommended Project Layout

```
src/Deskbridge.Core/
├── Interfaces/
│   ├── IAuditLogger.cs                (NEW — Phase 6 Plan 01)
│   ├── IMasterPasswordService.cs       (NEW — Phase 6 Plan 04)
│   └── ICommandPaletteService.cs       (NEW — Phase 6 Plan 03)
├── Models/
│   └── AuditRecord.cs                  (NEW — jsonl line shape, [JsonSerializable])
├── Services/
│   ├── AuditLogger.cs                  (NEW — SemaphoreSlim-guarded jsonl writer)
│   ├── MasterPasswordService.cs        (NEW — PBKDF2 hash + verify)
│   └── CommandPaletteService.cs        (NEW — static command registry + ICommandEntry)
├── Logging/
│   └── RedactSensitivePolicy.cs        (NEW — IDestructuringPolicy)
└── Settings/
    ├── AppSettings.cs                  (NEW — window + security prefs POCO)
    └── AppSettingsContext.cs           (NEW — [JsonSerializable] source-gen ctx)

src/Deskbridge/
├── Dialogs/
│   ├── CommandPaletteDialog.xaml       (NEW — Plan 03)
│   ├── CommandPaletteDialog.xaml.cs    (NEW)
│   ├── LockOverlayDialog.xaml          (NEW — Plan 04)
│   ├── LockOverlayDialog.xaml.cs       (NEW — password code-behind)
│   └── CrashDialog.xaml (optional — spec says SimpleContentDialog ok, so maybe no xaml)
├── ViewModels/
│   ├── CommandPaletteViewModel.cs      (NEW — Plan 03)
│   ├── LockOverlayViewModel.cs         (NEW — Plan 04)
│   └── SettingsViewModel.cs            (NEW — Plan 02 and/or Plan 04)
├── Services/
│   ├── ToastSubscriptionService.cs     (NEW — Plan 02, IEventBus → ISnackbarService)
│   ├── WindowStateService.cs           (NEW — Plan 02, load/save settings.json)
│   ├── IdleLockService.cs              (NEW — Plan 04, DispatcherTimer + InputManager hook)
│   └── SessionLockService.cs           (NEW — Plan 04, SystemEvents.SessionSwitch hook)
└── CrashHandler.cs                     (NEW — Plan 01, three-hook registration)
```

### Pattern 1: Audit logger (jsonl writer under semaphore)

**What:** One `IAuditLogger` singleton owns append-only write access to `%AppData%/Deskbridge/audit-YYYY-MM.jsonl`. Every publisher (connect-succeeded handler, lock handler, import handler, etc.) calls `auditLogger.LogAsync(AuditRecord)`. Writes are serialised by `SemaphoreSlim(1,1)` so concurrent bus dispatches don't interleave half-lines.

**When to use:** For any structured append-only log where the writer set is the whole app and readers (future audit viewer in v1.2) need one-line-per-record JSON.

**Example:**

```csharp
// Source: Pattern inspired by Serilog's File sink + dev.to/stevsharp SemaphoreSlim guide
// https://dev.to/stevsharp/semaphoreslim-in-net-a-practical-guide-1mh7

public sealed class AuditLogger : IAuditLogger, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;

    public AuditLogger()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Deskbridge");
        Directory.CreateDirectory(_directory);
    }

    public async Task LogAsync(AuditRecord record)
    {
        // Current-month filename derived per-call (not cached) so monthly rollover
        // happens automatically with no scheduled job (D-13).
        var fileName = $"audit-{DateTime.UtcNow:yyyy-MM}.jsonl";
        var path = Path.Combine(_directory, fileName);
        var line = JsonSerializer.Serialize(record, AuditJsonContext.Default.AuditRecord) + '\n';

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, line).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Serilog fallback so we never lose the fact of the audit attempt,
            // even if disk full / read-only / permission denied.
            Log.Error(ex, "Failed to append audit record {Type}", record.Type);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
```

`AuditRecord` is a `record` with `[JsonSerializable]` generated context:

```csharp
public sealed record AuditRecord(
    string Ts,                 // DateTime.UtcNow.ToString("O")
    string Type,               // AuditAction.ToString()
    Guid? ConnectionId,        // null for app-scope events
    string User,               // Environment.UserName
    string Outcome,            // "success" | "fail"
    string? ErrorCode);        // optional

[JsonSerializable(typeof(AuditRecord))]
internal partial class AuditJsonContext : JsonSerializerContext { }
```

### Pattern 2: Command palette dialog

**What:** `ui:ContentDialog` subclass with `IsFooterVisible="False"` (no Primary/Secondary/Close buttons — Esc-to-close and click-outside-to-close are built-in). Body is a `DockPanel` with a search `ui:TextBox` and a `ListBox` of `ICommandPaletteRow`. `CommandPaletteViewModel` owns `SearchText`, `Items` (ObservableCollection<ICommandPaletteRow>), `SelectedItem`.

**When to use:** For Ctrl+Shift+P (CMD-01). Shown via `IContentDialogService.ShowAsync()` — same mechanism Phase 3 already uses for the ConnectionEditor/GroupEditor dialogs via `GetDialogHostEx()`.

**Example (shown from MainWindow.PreviewKeyDown):**

```csharp
// In MainWindow.xaml.cs OnPreviewKeyDown (extends existing KeyboardShortcutRouter)
if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
{
    _ = OpenCommandPaletteAsync(); // fire-and-forget; dialog handles its own lifetime
    e.Handled = true;
    return;
}

private async Task OpenCommandPaletteAsync()
{
    if (_paletteOpen) return; // idempotent per UI-SPEC
    _paletteOpen = true;
    try
    {
        var dialog = _services.GetRequiredService<CommandPaletteDialog>();
        await dialog.ShowAsync(); // uses RootContentDialog host (established Phase 2)
    }
    finally { _paletteOpen = false; }
}
```

**Pattern 2.1 — `ICommandPaletteRow` abstraction:**

Both connection rows and command rows implement the same interface so the ListBox DataTemplate is uniform (UI-SPEC §Command Palette Result Row Anatomy).

```csharp
public interface ICommandPaletteRow
{
    string Title { get; }
    string? Subtitle { get; }          // null hides subtitle line
    SymbolRegular Icon { get; }        // e.g. SymbolRegular.Desktop24
    string? Shortcut { get; }          // e.g. "Ctrl+N", null hides badge
    int Score { get; }                 // fuzzy score for sort
    Task ExecuteAsync();               // invoked on Enter/click
}
```

Two implementations:
- `ConnectionPaletteRow` — wraps `ConnectionModel`, publishes `ConnectionRequestedEvent` on Execute.
- `CommandPaletteRow` — wraps a static command definition, calls the respective VM method directly.

**Pattern 2.2 — Fuzzy scoring over the mixed list:**

```csharp
// CommandPaletteViewModel.OnSearchTextChanged
var query = SearchText?.Trim() ?? string.Empty;

var connectionRows = _query.Search(query)
    .Select(c => new ConnectionPaletteRow(c, ConnectionScoreFor(c, query)));

var commandRows = _commandPalette.Commands
    .Select(cmd => new CommandPaletteRow(cmd, CommandScoreFor(cmd, query)))
    .Where(r => query.Length == 0 || r.Score > 0);

Items.Clear();
foreach (var row in connectionRows.Concat(commandRows)
                                  .OrderByDescending(r => r.Score)
                                  .ThenBy(r => r is CommandPaletteRow ? 0 : 1)
                                  .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase))
{
    Items.Add(row);
}
SelectedItem = Items.FirstOrDefault();
```

`ConnectionScoreFor` reuses `IConnectionQuery.Search()` (already returns ordered) — the scorer is inside the service; palette just gets back the ranked list and maps to rows. Commands use the SAME `CalculateScore` contract but over `{Title, Aliases, "command"}` — a small helper in `CommandPaletteService`.

### Pattern 3: Toast subscription service

**What:** A singleton `ToastSubscriptionService` that subscribes to 7 event types in its ctor and calls `ISnackbarService.Show()` with the UI-SPEC copy strings.

**When to use:** For NOTF-01 / NOTF-03. Registered as singleton; eager-resolved in `App.OnStartup` (same pattern as Phase 5's `ITabHostManager` eager resolve — line 67 of App.xaml.cs).

**Example:**

```csharp
public sealed class ToastSubscriptionService
{
    private readonly ISnackbarService _snackbar;
    public ToastSubscriptionService(ISnackbarService snackbar, IEventBus bus,
        INotificationService notifications)
    {
        _snackbar = snackbar;
        bus.Subscribe<ConnectionEstablishedEvent>(this, OnConnected);
        bus.Subscribe<ConnectionClosedEvent>(this, OnClosed);
        bus.Subscribe<ReconnectingEvent>(this, OnReconnecting);
        bus.Subscribe<ConnectionFailedEvent>(this, OnFailed);
        bus.Subscribe<UpdateAvailableEvent>(this, OnUpdateAvailable);
        bus.Subscribe<ConnectionImportedEvent>(this, OnImported);
        // Silent on user-initiated close (D-06) — filter on DisconnectReason inside OnClosed.
    }

    private void OnConnected(ConnectionEstablishedEvent e)
    {
        _snackbar.Show("Connected", $"Connected to {e.Connection.Hostname}.",
            ControlAppearance.Info,
            new SymbolIcon(SymbolRegular.Info24),
            TimeSpan.FromSeconds(2));
    }
    // ...one handler per event, exact copy strings from UI-SPEC §Toast Copywriting
}
```

### Pattern 4: Global exception handler (three hooks)

**What:** Register all three handlers inside `Program.Main` IMMEDIATELY after `VelopackApp.Build().Run()` and BEFORE `new App().Run()`. Hooks survive any Application-level swap.

**When to use:** For LOG-04. Order matters — handlers must be in place before any WPF code can throw.

**Example:**

```csharp
// src/Deskbridge/Program.cs (modify existing)
[STAThread]
public static int Main(string[] args)
{
    try
    {
        VelopackApp.Build().Run();
        CrashHandler.Install();         // <-- NEW — installs 3 hooks
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
    catch (Exception ex)
    {
        // Fallback — handlers not yet registered (only Velopack startup failure).
        File.WriteAllText(EmergencyLogPath, ex.ToString());
        return 1;
    }
}
```

```csharp
// src/Deskbridge/CrashHandler.cs (NEW)
public static class CrashHandler
{
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
        TaskScheduler.UnobservedTaskException       += OnUnobservedTask;
        // DispatcherUnhandledException is attached in App.OnStartup — Application.Current
        // doesn't exist yet at Main() time.
    }

    internal static void InstallDispatcherHook(Application app)
    {
        app.DispatcherUnhandledException += OnDispatcherUnhandled;
    }

    private static void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "DispatcherUnhandledException");
        if (TryShowCrashDialog(e.Exception))
            e.Handled = true; // survive — user may click Copy Details first
    }

    private static void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "AppDomainUnhandledException terminating={Terminating}", e.IsTerminating);
        // Cannot prevent termination here.
    }

    private static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "UnobservedTaskException");
        e.SetObserved(); // survive
    }

    private static bool TryShowCrashDialog(Exception ex) { /* marshal to UI, show ContentDialog */ }
}
```

**Key point:** `App.OnStartup` must call `CrashHandler.InstallDispatcherHook(this)` after `base.OnStartup(e)` — `Application.Current` is valid from `OnStartup` onwards.

### Pattern 5: Settings persistence (`settings.json`)

**What:** A POCO `AppSettings` serialised with System.Text.Json source-generator, written atomically (tmp + rename — same pattern as Phase 3 `JsonConnectionStore.PersistAtomically`). Shares ONE file for window state + security prefs — one file is simpler to migrate; no per-subsystem fragmentation.

**When to use:** For NOTF-04 (window state) AND SEC-03 (auto-lock timeout) AND SEC-05 (lock-on-minimise). auth.json is SEPARATE — it holds only the PBKDF2 hash + salt and never changes except on master-password reset.

**Example:**

```csharp
public sealed record AppSettings(
    WindowState Window,
    SecuritySettings Security,
    int SchemaVersion = 1);   // bump on breaking changes

public sealed record WindowState(
    double X, double Y, double Width, double Height,
    bool IsMaximized, bool SidebarOpen, double SidebarWidth);

public sealed record SecuritySettings(
    int AutoLockTimeoutMinutes = 15,
    bool LockOnMinimise = false);

[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsContext : JsonSerializerContext { }
```

Schema migration story: the `SchemaVersion` integer on the root record. If v1 loads and version > 1, log a warning and fall back to defaults (lossy) OR run a migrator (for v2+). For v1, migration code is zero — but the field reserves the shape.

### Pattern 6: PBKDF2 hash + verify

```csharp
// Source: https://anthonysimmon.com/evolutive-and-robust-password-hashing-using-pbkdf2-in-dotnet/
//         https://learn.microsoft.com/.../rfc2898derivebytes.pbkdf2
public sealed class MasterPasswordService : IMasterPasswordService
{
    private const int Iterations = 600_000;           // OWASP 2023 for PBKDF2-HMAC-SHA256
    private const int SaltBytes = 32;                 // 256-bit salt
    private const int KeyBytes = 32;                  // 256-bit derived key
    private const int SchemaVersion = 1;

    private static readonly HashAlgorithmName Alg = HashAlgorithmName.SHA256;

    public string HashNewPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Alg, KeyBytes);

        // Storage format: v<int>.<base64 salt>.<base64 key>
        // Lets future versions (e.g. iteration bump) still verify old hashes.
        return $"v{SchemaVersion}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || parts[0] != $"v{SchemaVersion}") return false;

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Alg, KeyBytes);

        // Constant-time compare avoids timing attacks.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
```

Storage on disk is the `stored` string above in `auth.json`:

```json
{ "passwordHash": "v1.base64salt.base64key", "schemaVersion": 1 }
```

### Pattern 7: Lock overlay dialog

**What:** `ui:ContentDialog` subclass with `IsFooterVisible="False"` (hides the library's Primary/Secondary/Close button row). Custom content includes the password card per UI-SPEC. Opaque background (`ApplicationBackgroundBrush`) covers remote pixels.

**Key property (verified):** `ContentDialog.IsFooterVisible` — `[VERIFIED: wpfui.lepo.co/api/Wpf.Ui.Controls.ContentDialog.html]`.

**Example:**

```xml
<ui:ContentDialog x:Class="Deskbridge.Dialogs.LockOverlayDialog"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    xmlns:local="clr-namespace:Deskbridge.Dialogs"
    IsFooterVisible="False"
    DialogWidth="NaN" DialogHeight="NaN"
    Background="{DynamicResource ApplicationBackgroundBrush}">
    <ui:ContentDialog.Resources>
        <Style BasedOn="{StaticResource {x:Type ui:ContentDialog}}"
               TargetType="{x:Type local:LockOverlayDialog}" />
    </ui:ContentDialog.Resources>
    <Grid HorizontalAlignment="Center" VerticalAlignment="Center">
        <Border Width="360" Padding="32" CornerRadius="8"
                Background="{DynamicResource ControlFillColorDefaultBrush}"
                BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                BorderThickness="1">
            <StackPanel>
                <TextBlock Text="Deskbridge" FontSize="20" FontWeight="SemiBold"
                           HorizontalAlignment="Center"/>
                <!-- ...body text, PasswordBox, error, Unlock button per UI-SPEC -->
            </StackPanel>
        </Border>
    </Grid>
</ui:ContentDialog>
```

Code-behind wires the PasswordBox:

```csharp
public partial class LockOverlayDialog : ContentDialog
{
    private readonly LockOverlayViewModel _vm;
    public LockOverlayDialog(LockOverlayViewModel vm) : base(GetDialogHost())
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    // PasswordBox.Password is not a DP — push on every change.
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.Password = ((PasswordBox)sender).Password;
}
```

### Pattern 8: Idle-reset via `InputManager.Current.PreProcessInput`

**What:** One static subscription in `IdleLockService` that resets a `DispatcherTimer` on every `InputEventArgs` (mouse, keyboard, stylus) routed through WPF for the main window. When the timer fires, publish `AppLockedEvent(LockReason.Timeout)`.

**Why not Application-level mouse/keyboard handlers:** those only fire on the hit-tested element; `InputManager.PreProcessInput` catches input even on focused AxHost siblings — HOWEVER, see §Pitfall 6: input inside the RDP session HWND does NOT route through WPF input manager (airspace), which matches D-14 intent ("Deskbridge activity only resets the timer").

**Example:**

```csharp
public sealed class IdleLockService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly IEventBus _bus;
    private readonly PreProcessInputEventHandler _handler;  // strong ref!

    public IdleLockService(IEventBus bus, IAppSettings settings)
    {
        _bus = bus;
        _timer = new DispatcherTimer {
            Interval = TimeSpan.FromMinutes(settings.AutoLockTimeoutMinutes)
        };
        _timer.Tick += (_, _) => { _timer.Stop(); _bus.Publish(new AppLockedEvent(LockReason.Timeout)); };

        _handler = (_, _) => { _timer.Stop(); _timer.Start(); };
        InputManager.Current.PreProcessInput += _handler;
        _timer.Start();
    }

    public void Dispose()
    {
        InputManager.Current.PreProcessInput -= _handler;
        _timer.Stop();
    }
}
```

### Pattern 9: `SystemEvents.SessionSwitch` handler

**What:** `SessionLockService` subscribes to `SystemEvents.SessionSwitch` during DI construction, holds the delegate in a strong-reference field, marshals the event back to the UI dispatcher (because SessionSwitch fires on a dedicated internal thread), and unsubscribes on `Dispose` / `App.OnExit`.

**Example:**

```csharp
public sealed class SessionLockService : IDisposable
{
    private readonly IEventBus _bus;
    private readonly Dispatcher _uiDispatcher;
    private readonly SessionSwitchEventHandler _handler; // strong ref

    public SessionLockService(IEventBus bus)
    {
        _bus = bus;
        _uiDispatcher = Application.Current.Dispatcher;

        _handler = (_, e) =>
        {
            // Fires on a dedicated SystemEvents thread — marshal to UI.
            if (e.Reason == SessionSwitchReason.SessionLock
             || e.Reason == SessionSwitchReason.ConsoleDisconnect
             || e.Reason == SessionSwitchReason.RemoteDisconnect)
            {
                _uiDispatcher.BeginInvoke(() =>
                    _bus.Publish(new AppLockedEvent(LockReason.SessionSwitch)));
            }
        };

        SystemEvents.SessionSwitch += _handler;
    }

    public void Dispose()
    {
        // Static event — detach is MANDATORY to avoid memory leak.
        // [CITED: learn.microsoft.com/.../Microsoft.Win32.SystemEvents.SessionSwitch "you must detach your event handlers when your application is disposed, or memory leaks will result"]
        SystemEvents.SessionSwitch -= _handler;
    }
}
```

### Anti-Patterns to Avoid

- **Subscribing `SystemEvents.SessionSwitch` with a lambda held only by the event** — the lambda is captured by the static event's invocation list, BUT the service instance that "owns" it is garbage-collected if no field references it. Keep the `SessionSwitchEventHandler` in a field. `[CITED: learn.microsoft.com — "must detach your event handlers"]`
- **`PasswordBox.Password` bound via `SecureString` (SecurePassword)** — project constraint explicitly bans SecureString (DE0001 deprecation).
- **`BinaryFormatter` for clipboard drag/drop of audit data** — removed in .NET 10.
- **`Marshal.ReleaseComObject` on the RDP control** — project ban, not relevant to Phase 6 but noted.
- **Showing the crash dialog from the `UnhandledException` handler directly** — that handler runs on a non-UI thread. Marshal via `Dispatcher.Invoke` first, and keep the fallback log-then-exit path for cases where the Dispatcher itself is broken.
- **`DispatcherTimer` with non-UI-created Dispatcher** — must be created ON the UI thread (or pass `Application.Current.Dispatcher` to the `DispatcherTimer` ctor overload). Instantiate `IdleLockService` inside App.OnStartup, not in DI factory that might run on a different thread.
- **Duplicating the fuzzy scorer in `CommandPaletteService`** — CMD-03 explicitly says reuse `IConnectionQuery.Search()`. Use it verbatim; the command-specific scorer is a 30-line helper that applies the SAME substring/subsequence rules to the command's `{Title, Aliases, "command"}` fields.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Structured logging / rolling files | Custom file writer with locking | Serilog 4.3.1 + Serilog.Sinks.File 7.0.0 | Already wired. Handles rotation, thread safety, format templates, enrichers. |
| Cryptographic password hashing | Custom SHA-256 + random salt + iteration loop | `Rfc2898DeriveBytes.Pbkdf2(...)` static method | In-box. Constant-time, hardware-accelerated. Never roll your own KDF. |
| Cryptographically secure random | `new Random()` + `Guid.NewGuid()` | `RandomNumberGenerator.GetBytes()` | `Random` is predictable; `Guid.NewGuid()` has no guarantees of cryptographic randomness. |
| Constant-time byte comparison | `==` / `SequenceEqual` | `CryptographicOperations.FixedTimeEquals` | Prevents timing side-channel on hash verify. |
| Idle detection | Hook WH_MOUSE_LL + WH_KEYBOARD_LL global hooks | `InputManager.Current.PreProcessInput` | Low-level hooks require unmanaged lifetime, slow down the whole OS input stream, and trigger SmartScreen/AV scans. |
| Windows session-state detection | Poll `GetLastInputInfo` or parse Event Log | `Microsoft.Win32.SystemEvents.SessionSwitch` | First-party API. Documented behaviour. Covers local + RDP + user-switch. |
| Append-only concurrent jsonl writer | Custom stream + lock + rotation cron | `SemaphoreSlim` + `File.AppendAllTextAsync` + filename derivation | 20 lines of code, no scheduled jobs, no temp-file churn. |
| Modal dialog lifecycle | Custom `Window` subclass with backdrop + focus trap + Esc | `ui:ContentDialog` | Already in MainWindow.xaml. Inherits modality + backdrop + focus trap + Esc + Tab cycle. |
| Fuzzy string scoring | New scorer class | Existing `ConnectionQueryService.CalculateScore` (public-via-reuse) | CMD-03 mandates this. Extract the algorithm into a static helper if `CommandPaletteService` needs to score without a `ConnectionModel`. |
| Restart-current-exe | `Process.Start(Assembly.GetExecutingAssembly().Location)` | Velopack `UpdateManager.RestartApp()` OR `Process.Start(Process.GetCurrentProcess().MainModule.FileName)` | Velopack's API handles the `Update.exe waitForExit` pattern that avoids races. For crash dialog restart, `MainModule.FileName` is acceptable — we're not applying an update. |
| Password-change UI | Dialog with 3 PasswordBoxes | **NOT BUILT in v1 (D-16 recovery = delete auth.json manually)** | Do NOT build a "change password" flow. Deferred. |
| Settings page | Full navigation/routing infrastructure | Existing slide-out panel (UI-SPEC §Settings Panel Additions) | UI-SPEC explicitly defers proper Settings page to post-v1. |

**Key insight:** Every Phase 6 capability has a first-party .NET API. The only "third-party" consideration is whether to bring in `Serilog.Extras.Attributed` for `[NotLogged]`-attribute destructuring — rejected here because a custom `IDestructuringPolicy` gives denylist-based safety without an extra dependency.

## Runtime State Inventory

**Skip rationale:** Phase 6 is a NEW feature phase (greenfield scope), not a rename/refactor/migration. No existing runtime state is being renamed, moved, or migrated. Settings.json and auth.json are NEW files; no prior versions exist. Log files are append-only and carry no old-name references.

**Nothing found in any category:** None — this phase creates new files and subscribes new handlers. All references verified greenfield.

## Common Pitfalls

### Pitfall 1: `SystemEvents.SessionSwitch` handler captured only by the static event

**What goes wrong:** You write `SystemEvents.SessionSwitch += (s, e) => _bus.Publish(...)` inside a service ctor, the service instance is GC'd because nothing else holds a reference, and the lambda closure silently stops firing (or worse, keeps firing against a disposed capture). Also, on app shutdown, the static event retains the dead closure → memory leak across session.

**Why it happens:** `SystemEvents.SessionSwitch` is a STATIC event. The handler delegate is the only strong root to the closure. The service instance is NOT held by the event.

**How to avoid:** (a) Keep the service as a DI SINGLETON so the DI container holds the instance. (b) Store the handler in an `internal readonly SessionSwitchEventHandler _handler` field on the service so the delegate is unambiguously rooted by the instance. (c) Call `SystemEvents.SessionSwitch -= _handler` in `Dispose()` / `App.OnExit`.

**Warning signs:** SessionSwitch events fire once then stop after a GC. Memory profiler shows growing `SessionSwitchEventHandler` invocation list across app restarts.

### Pitfall 2: Concurrent writers to audit.jsonl produce interleaved half-lines

**What goes wrong:** Two event-bus dispatches call `IAuditLogger.LogAsync` simultaneously. `File.AppendAllTextAsync` opens-writes-closes the file independently per call — on Windows, two concurrent opens for append are allowed (each gets its own handle), and writes can interleave: `{"ts":"2026-...","ty{"ts":"2026-...pe":"Connec` — producing an unparseable audit file.

**Why it happens:** `File.AppendAllTextAsync` does NOT acquire the `FileShare.None` lock long enough to serialise against other callers. NTFS opportunistic locks are short-lived.

**How to avoid:** Wrap every `AppendAllTextAsync` in a `SemaphoreSlim(1,1).WaitAsync() / Release()` pair. See Pattern 1. Only ONE semaphore per process (audit file is process-scoped; no cross-process concurrency expected since it's `%AppData%`-relative).

**Warning signs:** Any line in audit.jsonl failing `JsonDocument.Parse`. Test: write 1000 records from 20 concurrent tasks, assert every line parses.

### Pitfall 3: WPF-UI `SnackbarPresenter` is a FIFO queue, not a visible stack [CRITICAL — UI-SPEC deviation]

**What goes wrong:** UI-SPEC D-07 promises "max 3 visible simultaneously, newest on top, 4th evicts oldest". The stock WPF-UI 4.2.0 `SnackbarPresenter` is implemented as `protected Queue<Snackbar> Queue`, displaying ONE snackbar at a time (`Content` property holds the current), and `ShowQueuedSnackbarsAsync` loops sequentially. `[VERIFIED: source read of SnackbarPresenter.cs on main branch]`

**Why it happens:** WPF-UI follows the Windows 11 single-snackbar design pattern. Multiple simultaneous toasts are a Material-Design convention, not a Fluent one.

**How to avoid:** Three options for Plan 06-02 — the planner/user must choose:

| Option | Work | Fidelity to UI-SPEC D-07 |
|--------|------|--------------------------|
| **(A) Accept single-snackbar** (soften D-07 to "queue, not stack — next appears after current dismisses") | 0 LoC changes to library; ToastSubscriptionService works as-is | **Breaks D-07 literal** but matches WPF-UI library intent. Many reconnecting+connected events during a storm will queue sequentially — user still sees every one, just not all at once. |
| **(B) Replace `SnackbarPresenter` with a custom `ItemsControl`** at the same anchor position | ~150 LoC: ObservableCollection<SnackbarItem>, eviction logic, stack layout, auto-dismiss DispatcherTimer per item, close button, hover-pause | **Honours D-07 literal** but requires parallel maintenance against WPF-UI upgrades. Lose WPF-UI's default animations unless ported. |
| **(C) Instance-per-toast + sibling presenters** | ~80 LoC: pool of 3 SnackbarPresenter instances in a `StackPanel`, ToastSubscriptionService picks the next free one | Partial — works until all 3 are busy; 4th call must still either queue against one presenter or evict. |

**Recommendation:** (A) is simpler, cheaper, and matches WPF-UI intent. If the user insists on (B), treat it as a separate architecture spike inside Plan 06-02 with its own UAT. Flag this for `/gsd-plan-phase` to surface to the user.

**Warning signs:** During a reconnect storm (RDP server bounces), user sees "Reconnecting" then "Reconnecting" then "Reconnected" then "Connected" sequentially with 2s gaps — not simultaneously. If 10 events fire in 1 second, user waits ~20s for the queue to drain.

### Pitfall 4: `DispatcherTimer` after Windows sleep does NOT compensate for missed time

**What goes wrong:** User sets 15-minute auto-lock. Locks their screen externally (Windows Start → Lock). Sleeps laptop 8 hours. Opens → Deskbridge is NOT locked because timer was frozen and has 14 minutes remaining instead of having fired.

**Why it happens:** Both `DispatcherTimer` and `System.Timers.Timer` halt on system sleep and resume from their paused state — neither catches up missed ticks. `[CITED: copyprogramming.com/.../wpf-timer-behavior]`

**How to avoid:** This is NOT a problem in practice for Deskbridge because (a) Windows lock on sleep is default on all corporate machines and (b) `SessionSwitch` fires on resume with `SessionUnlock` → we DO lock on resume. Document the behaviour in Plan 06-04. If stricter is needed (v1.1+), subscribe `SystemEvents.PowerModeChanged` and restart the timer on `Resume` with `Interval = remaining`.

**Warning signs:** UAT laptop-close / laptop-open test doesn't trigger auto-lock when it "should" have.

### Pitfall 5: Lock overlay can be painted OVER by active AxHost (airspace)

**What goes wrong:** User has an RDP session active. Auto-lock fires. The `ui:ContentDialog` lock overlay is a WPF element layered via `ContentDialogHost`. Because `WindowsFormsHost`+`AxHost` is airspace-rendered (its HWND is OS-composited, NOT via WPF's visual tree), the WPF overlay is PAINTED BEHIND the AxHost bitmap. User sees the live remote desktop through the "lock overlay". Secrets leak. **This is a security defect if not addressed.**

**Why it happens:** WindowsFormsHost airspace. WPF-UI's `ContentDialog` does not suppress HWND siblings.

**How to avoid:** Three options, in increasing rigour:

| Option | Description | Tradeoff |
|--------|-------------|----------|
| **(A) Collapse every `WindowsFormsHost` in `HostContainer` on lock, restore on unlock** | `foreach (var c in HostContainer.Children) c.Visibility = Visibility.Collapsed;` | Simplest. AxHost HWND becomes invisible; Dialog is only thing visible. Sessions STAY CONNECTED in the background. On unlock, restore each host's prior Visibility (use the Phase 5 `_preDragVisibility` pattern from `AirspaceSwapper`). **RECOMMENDED.** |
| **(B) Disconnect every active session on lock** | `_tabHostManager.CloseAllAsync()` inside the lock handler | Extreme — user loses in-flight file copies, long-running commands. Rejected unless user explicitly requests. |
| **(C) Overlay is itself a Win32 HWND** | Build lock card as a transparent topmost Form placed over MainWindow | Impractical — violates WPF-UI visual coherence, requires separate DPI handling, loses Fluent styling. Rejected. |

**Recommendation:** (A). The lock overlay's `IsLocked` state flips visibility on ALL `HostContainer.Children` via the same `SetActiveHostVisibility`-style Tag-keyed dict from Phase 5. On unlock, restore captured pre-lock Visibility.

**Warning signs:** User UAT: start a connection, wait for auto-lock; lock overlay appears BUT the RDP viewport is still visible in the background/foreground. Visual regression.

**Cross-reference:** This interaction was called out in CLAUDE.md's `WINFORMS-HOST-AIRSPACE.md` (repo root). Plan 06-04 MUST reference that document when implementing.

### Pitfall 6: `InputManager.PreProcessInput` fires for RDP keystrokes when keyboard hook mode is default

**What goes wrong:** The default `AxMsRdpClient9.KeyboardHookMode = 2` sends key combos LIKE Alt+Tab to the remote session but lets WPF see other keys. If `PreProcessInput` catches keys that are routed through WPF before being forwarded to the AxHost, the user's remote-session typing resets the Deskbridge idle timer — violating D-14 ("Deskbridge activity only").

**Why it happens:** Phase 5 sets `KeyboardHookMode=0` (no low-level hook — see MainWindow.xaml.cs line 362-365 comment: "KeyboardHookMode=0 lets PreviewKeyDown fire BEFORE the AxHost receives input"). This means keyboard events PASS THROUGH WPF's input manager — which is GOOD for the Phase 5 tab shortcuts but BAD for D-14 idle tracking.

**How to avoid:** In `IdleLockService._handler`, filter the `PreProcessInputEventArgs`:

```csharp
_handler = (_, e) =>
{
    // Ignore input that bubbled UP from an AxHost child (the remote session).
    // OriginalSource is the AxHost when input came from inside the WFH.
    if (e.StagingItem?.Input?.Source is DependencyObject src
        && FindAncestor<WindowsFormsHost>(src) is not null)
    {
        return; // remote-session input — do NOT reset idle timer
    }
    _timer.Stop(); _timer.Start();
};
```

Alternative: track `Keyboard.FocusedElement` — if it's a WindowsFormsHost or child thereof, skip the reset.

**Warning signs:** UAT — open a connection, sit idle while typing into the RDP session for 15 minutes. If Deskbridge does NOT lock, this pitfall was not mitigated.

### Pitfall 7: `SystemEvents.SessionSwitch` fires on a dedicated SystemEvents thread (NOT UI)

**What goes wrong:** Handler publishes `AppLockedEvent` on the bus from the SystemEvents thread. Subscribers that update UI (LockOverlayViewModel bindings) throw `InvalidOperationException: The calling thread cannot access this object` because WPF DependencyObjects have thread affinity.

**Why it happens:** SessionSwitch is documented to fire from a dedicated non-UI thread (running the SystemEvents message pump). `[CITED: multiple community sources; confirmed by .NET documentation note "Do not perform time-consuming processing on the same thread"]`

**How to avoid:** Marshal every SessionSwitch action to the UI dispatcher. See Pattern 9. `Application.Current.Dispatcher.BeginInvoke(...)` inside the handler.

**Warning signs:** First test of manual Windows lock screen crashes the app with cross-thread access exception.

### Pitfall 8: `ContentDialog` Primary button fires on Enter inside a TextBox

**What goes wrong:** Command palette opens, user types in search box, presses Enter to "pick the selected row" — BUT the built-in `ContentDialog` Enter handler fires the PrimaryButton first. Since our palette has `IsFooterVisible="False"`, there IS no primary button to fire — but the keystroke is still consumed.

**Why it happens:** Known WPF-UI behaviour (Issue #1404). Handled by the CLAUDE.md mandatory reading `WPF-UI-PITFALLS.md §1`.

**How to avoid:** Add `PreviewKeyDown` handler on the dialog:

```csharp
private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox)
    {
        _vm.ExecuteSelectedCommand();
        e.Handled = true;
    }
}
```

**Warning signs:** Palette opens, you type and press Enter, and the dialog closes without executing anything (or executes a phantom primary button action).

### Pitfall 9: `Destructure.ByTransforming<T>` returning the same type causes infinite recursion

**What goes wrong:** You register `Destructure.ByTransforming<ConnectionModel>(c => c)` thinking you'll mutate it in place — or `Destructure.ByTransforming<User>(u => new User { ... redacted ... })`. Both cause Serilog to call the transform recursively → stack overflow.

**Why it happens:** Serilog's ByTransforming applies to any incoming object of that type. If the transform returns the same type, it applies again.

**How to avoid:** Return an ANONYMOUS type:

```csharp
.Destructure.ByTransforming<ConnectionModel>(c => new { c.Id, c.Name, c.Hostname /* no Password */ })
```

Or use a custom `IDestructuringPolicy` (which returns `LogEventPropertyValue` directly, bypassing the type-matching).

**Warning signs:** `StackOverflowException` thrown from Serilog's internals on first log call.

**Cross-reference:** `[CITED: github.com/serilog/serilog/issues/973]`

## Fuzzy-Match Performance (CMD-03)

**Existing `ConnectionQueryService.Search`:** `[VERIFIED: read of src/Deskbridge.Core/Services/ConnectionQueryService.cs]`

Per-query complexity: `O(N × M)` where N=connections, M=|query|+|hostname|+|tag list length|. In practice each score is `~5 × StringComparison.OrdinalIgnoreCase.IndexOf` over strings of length < 64. For typical N:

| N connections | Approximate per-keystroke latency (measured-order estimate) |
|---------------|-------------------------------------------------------------|
| 10 | <0.1 ms |
| 100 | <1 ms |
| 500 | ~2-5 ms |
| 1000 | ~5-10 ms |
| 5000 | ~25-50 ms |

Under 500, no debouncing needed — runs faster than a keystroke-to-render frame. Above ~1000 is where typing feels "behind". Deskbridge target user has tens-to-hundreds of connections (per PROJECT.md core value statement) — well within the no-debounce envelope.

**Recommendation:** Do NOT add debouncing in v1. Add a `TextChanged` → VM update on every keystroke. If UAT reveals perceptible lag at a user's actual scale, retrofit a 50 ms debounce via `DispatcherTimer`. No ObservableCollection mass-reset either — clear + foreach is fine for <500 items. `[ASSUMED: linear-scan latency; measure in UAT if N > 1000]`

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes (local master password) | PBKDF2-HMAC-SHA-256 @ 600k iterations, 32-byte salt, 32-byte key, constant-time verify |
| V3 Session Management | yes (app unlock = session start) | Idle timer (SEC-03) + explicit lock (SEC-04) + session-switch hook (SEC-04) + lock-on-minimise (SEC-05) |
| V4 Access Control | partial | Post-unlock UI shows all; pre-unlock shows nothing but the lock overlay. No per-connection ACL (single-user local app). |
| V5 Input Validation | yes (master password min length 8) | In-memory check in `LockOverlayViewModel` before calling `MasterPasswordService.HashNewPassword` |
| V6 Cryptography | yes | `Rfc2898DeriveBytes.Pbkdf2` static (not the deprecated ctor defaults); `RandomNumberGenerator.GetBytes` for salt; `CryptographicOperations.FixedTimeEquals` for compare. NEVER hand-roll. |
| V7 Error Handling & Logging | yes | `IDestructuringPolicy` redacts property names matching `{Password, Secret, Token, CredentialData, ApiKey}` across ALL types logged. Credentials never hit `LogContext`. |
| V8 Data Protection | yes | auth.json stores only the hash; raw master password never persisted. Windows Credential Manager (from Phase 3) holds connection secrets — unchanged by Phase 6. |
| V14 Configuration | partial | SchemaVersion field on AppSettings + AuthFile for future migrations. |

### Known Threat Patterns for WPF+STJ+PBKDF2 Stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Timing attack on PBKDF2 verify | Spoofing | `CryptographicOperations.FixedTimeEquals` on the derived key compare (Pattern 6) |
| Plain-text password in memory (PasswordBox + ViewModel string) | Information Disclosure | Accept — project constraint rejects SecureString. Mitigate by scoping the lifetime tightly (only during unlock) and not logging. |
| Audit-log tampering | Tampering | File ACL relies on `%AppData%` per-user. Integrity hash not implemented in v1. Accept — local attacker who can edit your AppData already has full session access. |
| Audit-log interleaving | DoS / Data integrity | SemaphoreSlim-guarded writer (Pattern 1). |
| Recursive Serilog destructuring triggered by circular refs (e.g. logged graph of ConnectionModel → ConnectionGroup → ConnectionModel) | DoS | Serilog's destructuring depth limit (default 10). Not specific to Phase 6. |
| Clipboard leak of crash details (exception message with tokens) | Information Disclosure | `Copy Details` handler runs the exception through the same `IDestructuringPolicy` redaction as log lines before writing to clipboard. |
| Lock bypass via forgotten-password flow | Authorization | **None — D-16 deliberate.** Recovery = delete auth.json manually. Document in UI copy (already done). |
| RDP viewport visible under lock | Information Disclosure | Pitfall 5 mitigation: collapse WindowsFormsHost children on lock. **Must be UAT-verified.** |
| `SystemEvents.SessionSwitch` memory leak | Reliability | Strong-reference handler field + `Dispose` unsubscribe (Pattern 9). |

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Serilog | LOG-01..05 | ✓ | 4.3.1 (Directory.Packages.props pinned 4.3.*) | — |
| Serilog.Sinks.File | LOG-01 | ✓ | 7.0.0 (pinned 7.0.*) | — |
| WPF-UI | CMD-01, NOTF-01, SEC-01..05 | ✓ | 4.2.0 (pinned 4.2.*) | — |
| System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2 | SEC-01 | ✓ (in-box .NET 10) | .NET 10 | — |
| Microsoft.Win32.SystemEvents | SEC-04 | ✓ (in-box with WindowsDesktop) | 10.0.* | — |
| `%AppData%/Deskbridge/` write access | LOG-01, NOTF-04, SEC-01..05, LOG-02 | ✓ (precedent: Phase 3 JsonConnectionStore creates directory) | — | — |
| Segoe Fluent Icons | CMD-01 (SymbolIcon markup extension) | ✓ Windows 11 only — Windows 10 must bundle font OR icons render as empty rectangles | — | Bundle font in future Phase 7 installer; not Phase 6's concern |

**Missing dependencies with no fallback:** None.

**Missing dependencies with fallback:** None.

**Probes run:** No shell probes needed — every dependency is already referenced in the solution's Directory.Packages.props (`[VERIFIED: file read 2026-04-14]`). Velopack/SDK versions were validated during Phase 01.

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit v3 (3.2.*) via Microsoft Testing Platform |
| Config file | `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` (`OutputType=Exe`, `TestingPlatformDotnetTestSupport=true`) |
| Quick run command | `dotnet test tests/Deskbridge.Tests/` |
| Full suite command | `dotnet test Deskbridge.sln` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CMD-01 | Ctrl+Shift+P opens palette | integration (XAML text-parse + KeyboardShortcutRouter extension unit test) | `dotnet test --filter FullyQualifiedName~CommandPaletteTests` | ❌ Wave 0 |
| CMD-02 | 4 commands registered in `ICommandPaletteService.Commands` | unit | `dotnet test --filter FullyQualifiedName~CommandPaletteServiceTests` | ❌ Wave 0 |
| CMD-03 | Palette uses `IConnectionQuery.Search` (no reimplementation) | unit (via NSubstitute `Received()` on mock IConnectionQuery) | same | ❌ Wave 0 |
| CMD-04 | Ctrl+N/Ctrl+T/Ctrl+W/F11/Esc routed | unit (extend `KeyboardShortcutRouterTests`) | `dotnet test --filter FullyQualifiedName~KeyboardShortcutRouterTests` | ✓ existing — extend |
| NOTF-01 | Event-bus → Snackbar subscription firing | unit (NSubstitute `ISnackbarService.Received(1).Show(...)`) | `dotnet test --filter FullyQualifiedName~ToastSubscriptionServiceTests` | ❌ Wave 0 |
| NOTF-02 | No modals from non-critical events | unit — assert `IContentDialogService` NOT called on bus events | same | ❌ Wave 0 |
| NOTF-03 | 7 event types mapped to toasts | unit — parameterized [Theory] over 7 events | same | ❌ Wave 0 |
| NOTF-04 | Window state saved on Closing, loaded on SourceInitialized | integration — real temp dir, WindowStateService roundtrip | `dotnet test --filter FullyQualifiedName~WindowStateServiceTests` | ❌ Wave 0 |
| LOG-01 | Rolling file config: 10MB cap, 5 files | **manual-only (Serilog internals)** — plus unit asserting config contains those numbers | smoke assertion on `Log.Logger` config | ❌ Wave 0 |
| LOG-02 | Jsonl write appends valid line | unit — real temp file + `JsonDocument.Parse` on line | `dotnet test --filter FullyQualifiedName~AuditLoggerTests` | ❌ Wave 0 |
| LOG-02 | Monthly rotation derived per call | unit — freeze time to 2026-04-30T23:59 then 2026-05-01T00:01, assert 2 files | same | ❌ Wave 0 |
| LOG-02 | Concurrent writers produce parseable output | unit — 20 tasks × 50 records each, assert every line parses | same | ❌ Wave 0 |
| LOG-03 | AuditAction enum → jsonl `type` field | unit — Theory over every AuditAction value | same | ❌ Wave 0 |
| LOG-04 | Three hooks installed | unit — reflective check of handler lists after `CrashHandler.Install()` | `dotnet test --filter FullyQualifiedName~CrashHandlerTests` | ❌ Wave 0 |
| LOG-04 | Crash dialog marshals to UI thread | **manual / UAT** — hand-triggered `throw` from background task; assert dialog appears |  — | N/A |
| LOG-05 | RedactSensitivePolicy strips Password properties | unit — policy applied to `ConnectionModel { Password = "secret" }`, assert LogEvent has no "secret" | `dotnet test --filter FullyQualifiedName~RedactSensitivePolicyTests` | ❌ Wave 0 |
| LOG-05 | Full-run log scrape | integration — tail logs/*.log after representative scenario, grep for known-plaintext password | manual + grep | ❌ Wave 0 (UAT scenario) |
| SEC-01 | PBKDF2 hash roundtrip | unit — hash + verify with correct/wrong password | `dotnet test --filter FullyQualifiedName~MasterPasswordServiceTests` | ❌ Wave 0 |
| SEC-01 | Timing-safe compare | unit — hand-craft two hashes, assert `FixedTimeEquals` used (via exception if swapped) | same | ❌ Wave 0 |
| SEC-02 | Lock overlay shown on launch when auth.json exists | integration — spawn App with pre-seeded auth.json, assert LockOverlayDialog shown first | UAT checklist | ❌ Wave 0 (UAT) |
| SEC-03 | Idle timer resets on PreProcessInput | unit — drive handler manually with synthetic events, assert timer `IsEnabled` cycles | `dotnet test --filter FullyQualifiedName~IdleLockServiceTests` | ❌ Wave 0 |
| SEC-03 | Timer fires `AppLockedEvent(Timeout)` after interval | unit — shrink interval to 100ms, wait, assert Publish called | same | ❌ Wave 0 |
| SEC-04 | Ctrl+L publishes `AppLockedEvent(Manual)` | unit — KeyboardShortcutRouter Ctrl+L case | extends existing | ✓ existing — extend |
| SEC-04 | SessionSwitch publishes `AppLockedEvent(SessionSwitch)` marshalled to UI | unit — drive handler manually, assert Dispatcher.BeginInvoke called (NSubstitute wraps) | `dotnet test --filter FullyQualifiedName~SessionLockServiceTests` | ❌ Wave 0 |
| SEC-05 | Window.StateChanged → Minimized locks (when enabled) | unit on MainWindowViewModel or integration via window property | tests | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** `dotnet test tests/Deskbridge.Tests/` (quick run; xUnit v3 default < 5s for whole Phase 6 suite)
- **Per wave merge:** `dotnet build Deskbridge.sln && dotnet test Deskbridge.sln`
- **Phase gate:** Full suite green + manual UAT checklist (SEC-02, LOG-04 UI marshal, LOG-05 log scrape, Pitfall 5 airspace lock, Pitfall 6 RDP-session idle exclusion)

### Wave 0 Gaps

- [ ] `tests/Deskbridge.Tests/Palette/CommandPaletteServiceTests.cs` — covers CMD-02, CMD-03
- [ ] `tests/Deskbridge.Tests/Palette/CommandPaletteViewModelTests.cs` — covers CMD-01 (VM fuzzy merge behaviour)
- [ ] `tests/Deskbridge.Tests/Notifications/ToastSubscriptionServiceTests.cs` — covers NOTF-01, NOTF-02, NOTF-03
- [ ] `tests/Deskbridge.Tests/Notifications/WindowStateServiceTests.cs` — covers NOTF-04
- [ ] `tests/Deskbridge.Tests/Logging/AuditLoggerTests.cs` — covers LOG-02, LOG-03 (concurrency + rotation + schema)
- [ ] `tests/Deskbridge.Tests/Logging/RedactSensitivePolicyTests.cs` — covers LOG-05
- [ ] `tests/Deskbridge.Tests/Logging/CrashHandlerTests.cs` — covers LOG-04 (hook registration)
- [ ] `tests/Deskbridge.Tests/Security/MasterPasswordServiceTests.cs` — covers SEC-01
- [ ] `tests/Deskbridge.Tests/Security/IdleLockServiceTests.cs` — covers SEC-03 (extend `KeyboardShortcutRouterTests` for SEC-04 Ctrl+L)
- [ ] `tests/Deskbridge.Tests/Security/SessionLockServiceTests.cs` — covers SEC-04 SessionSwitch path
- [ ] UAT checklist doc for SEC-02, LOG-04, LOG-05, Pitfall 5, Pitfall 6 (cannot be unit-tested, require live window / sleep / actual RDP session)

No new framework install needed. All existing test infra (xUnit v3 3.2.*, FluentAssertions 8.9.*, NSubstitute 5.3.*) already in place and handles all Phase 6 test shapes.

## Code Examples

### Serilog with credential redaction (LOG-01 + LOG-05)

```csharp
// Source: https://github.com/serilog/serilog/wiki/Structured-Data
//         https://dev.to/auvansangit/prevent-sensitive-data-exposure-in-log-with-serilog-1pk7

public sealed class RedactSensitivePolicy : IDestructuringPolicy
{
    private static readonly HashSet<string> Denylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "Secret", "Token", "CredentialData", "ApiKey",
        "ResolvedPassword", "MasterPassword"
    };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory factory,
                               [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        if (value is null) { result = null; return false; }

        // Only intervene for types with at least one denylisted property.
        var type = value.GetType();
        if (type.IsPrimitive || type == typeof(string)) { result = null; return false; }

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead);

        var values = new List<LogEventProperty>();
        var touched = false;
        foreach (var p in props)
        {
            if (Denylist.Contains(p.Name))
            {
                values.Add(new LogEventProperty(p.Name, new ScalarValue("***REDACTED***")));
                touched = true;
            }
            else
            {
                object? raw;
                try { raw = p.GetValue(value); } catch { raw = null; }
                values.Add(new LogEventProperty(p.Name, factory.CreatePropertyValue(raw, true)));
            }
        }

        if (!touched) { result = null; return false; } // let default policy handle
        result = new StructureValue(values);
        return true;
    }
}

// In App.xaml.cs OnStartup — REPLACE the existing baseline config from Phase 4:
var logRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Deskbridge", "logs");
Directory.CreateDirectory(logRoot);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Destructure.With<RedactSensitivePolicy>()
    .WriteTo.File(
        path: Path.Combine(logRoot, "deskbridge-.log"),
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 10_000_000,         // LOG-01: 10 MB cap
        rollOnFileSizeLimit: true,              // LOG-01: roll on size too
        retainedFileCountLimit: 5,              // LOG-01: keep 5 files
        shared: false,                          // single writer per process
        flushToDiskInterval: TimeSpan.FromSeconds(1))
    .CreateLogger();
```

### PBKDF2 auth.json schema + MasterPasswordService

```csharp
// auth.json schema
public sealed record AuthFile(string PasswordHash, int SchemaVersion = 1);

[JsonSerializable(typeof(AuthFile))]
internal partial class AuthJsonContext : JsonSerializerContext { }

public sealed class MasterPasswordService : IMasterPasswordService
{
    private const int Iterations = 600_000;
    private const int SaltBytes = 32, KeyBytes = 32;
    private static readonly HashAlgorithmName Alg = HashAlgorithmName.SHA256;

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Deskbridge", "auth.json");

    public bool IsMasterPasswordSet() => File.Exists(_path);

    public void SetMasterPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key  = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Alg, KeyBytes);
        var hash = $"v1.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";

        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(
            new AuthFile(hash), AuthJsonContext.Default.AuthFile);

        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);  // atomic on NTFS
    }

    public bool VerifyMasterPassword(string password)
    {
        if (!File.Exists(_path)) return false;
        var auth = JsonSerializer.Deserialize(
            File.ReadAllText(_path), AuthJsonContext.Default.AuthFile);
        if (auth is null) return false;

        var parts = auth.PasswordHash.Split('.');
        if (parts.Length != 3 || parts[0] != "v1") return false;
        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Alg, KeyBytes);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
```

### Reusing IConnectionQuery.Search for palette

```csharp
// In CommandPaletteViewModel
partial void OnSearchTextChanged(string value)
{
    var q = value?.Trim() ?? string.Empty;

    // Connection rows — DIRECTLY reuse existing dual-score scorer.
    var conns = _query.Search(q).Select(c => new ConnectionPaletteRow(c));

    // Command rows — apply SAME algorithm over command fields.
    var cmds = _commandPalette.Commands
        .Select(c => new { Cmd = c, Score = ScoreCommand(c, q) })
        .Where(x => q.Length == 0 || x.Score > 0)
        .Select(x => new CommandPaletteRow(x.Cmd, x.Score));

    Items.Clear();
    if (string.IsNullOrEmpty(q))
    {
        // Empty-state per D-02 — recents, then commands, NO merging / sorting.
        foreach (var c in _query.GetRecent(5).Select(c => new ConnectionPaletteRow(c))) Items.Add(c);
        foreach (var c in cmds) Items.Add(c);
    }
    else
    {
        // Ranked merge.
        foreach (var row in cmds.Cast<ICommandPaletteRow>().Concat(conns)
                                 .OrderByDescending(r => r.Score)
                                 .ThenBy(r => r is CommandPaletteRow ? 0 : 1)
                                 .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase))
            Items.Add(row);
    }
    SelectedItem = Items.FirstOrDefault();
}

private static int ScoreCommand(ICommandEntry c, string query)
{
    if (query.Length == 0) return 0;
    int score = 0;
    var lc = query.ToLowerInvariant();
    if (c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 100;
    if (c.Aliases?.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)) == true) score += 80;
    return score;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `new Rfc2898DeriveBytes(password, salt, iterations)` instance ctor with SHA-1 default | `Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, SHA256, keySize)` static method | .NET 6 (2021) | Static avoids SHA-1 footgun; constructor overloads are proposed-obsolete (dotnet/runtime#57046) |
| PBKDF2 with 100k iterations (ASP.NET Core Identity legacy) | 600k iterations for SHA-256 | OWASP 2023 guidance | 6× computational cost per verify — still sub-second on modern CPUs |
| `BinaryFormatter` for clipboard/serialisation | `System.Text.Json` for structured data | .NET 5 deprecated, .NET 10 REMOVED | All Phase 6 files use STJ |
| `SecureString` for passwords in memory | Plain `string` | SecureString marked DE0001 obsolete (Microsoft Security Cryptography guidance) | Project constraint aligns with Microsoft guidance |
| Serilog `SetContentPresenter` for ContentDialog host | `SetDialogHost(RootContentDialog : ui:ContentDialogHost)` | WPF-UI 4.x | Already adopted in Phase 2 — Phase 6 MUST use existing pattern |
| Queue-by-concurrency-mismatch in audit log | SemaphoreSlim + AppendAllTextAsync | .NET 6+ async file APIs | Simpler than Channel<T> for this scale (≤ 10 events/sec) |
| WPF-UI `GetDialogHost()` | `GetDialogHostEx()` | WPF-UI 4.2.0 (deprecation) | Phase 3 migrated; Phase 6 must use `Ex` variant |

**Deprecated / outdated:**

- `CredentialManagement` NuGet — project rejected.
- WPF-UI 3.x docs — 4.x namespaces differ; never reference 3.x samples.
- `Microsoft.NET.Test.Sdk` package — removed in Phase 1; do NOT re-add.
- ASP.NET Core Identity's default 100k PBKDF2 iteration count — well below 2026 OWASP guidance; Deskbridge uses 600k.
- `[GeneratedComInterface]` for RDP — not Phase 6's domain, but confirming nothing in Phase 6 attempts ActiveX interop.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | PBKDF2 @ 600k iterations SHA-256 is acceptable for Deskbridge's threat model (vs OWASP 2026 primary recommendation Argon2id) | Standard Stack §Core + Pattern 6 | Low: local-desktop attacker model is dominated by Windows-session access, not brute-force of auth.json. PBKDF2 600k remains OWASP-sanctioned as FIPS-path. User should confirm this tradeoff in `/gsd-discuss-phase 6` follow-up. |
| A2 | WPF-UI `SnackbarPresenter` queue-not-stack behaviour contradicts UI-SPEC D-07 — Option A (accept single-snackbar) is the recommended path | Pitfall 3 | Medium: UI-SPEC explicitly claims "max 3 visible simultaneously". Plan 06-02 MUST resolve this — surfaces to user. |
| A3 | `InputManager.PreProcessInput` fires for input on WPF visual-tree elements but not for input captured inside an AxHost HWND (airspace) | Pattern 8 + Pitfall 6 | Medium: Pitfall 6 documents the fallback filter. If AxHost input DOES pass through, idle timer resets during RDP typing (violates D-14). UAT-verifiable. |
| A4 | `SystemEvents.SessionSwitch` fires reliably on Windows 11 workstation SKU | Pattern 9 + Open Questions | Low for target platform (Windows 10/11 per app.manifest). Known unreliable on Windows Service context (app has message pump, OK). Does NOT work on macOS (dotnet/runtime#30035) — not relevant here. |
| A5 | Keeping the `SessionSwitchEventHandler` in a field on a DI singleton is sufficient to prevent GC of the service instance | Pattern 9 + Pitfall 1 | Low: DI container holds the singleton reference for the app lifetime; the field holds the delegate. Documented Microsoft guidance. |
| A6 | Fuzzy-search over 500 connections runs under 5ms per keystroke with current `ConnectionQueryService.Search` | §Fuzzy-Match Performance | Low: linear-scan over small strings. UAT at the user's actual N. If N > 1000, consider 50ms debounce. |
| A7 | Storing master password hash in a single `auth.json` separate from `settings.json` is simpler than combining | Pattern 5 | Low: separation makes the recovery instruction ("delete auth.json") clean and isolates blast radius. Lock-reset does not wipe window state. |
| A8 | Collapsing `WindowsFormsHost` children on lock is sufficient to hide the RDP viewport without disconnecting | Pitfall 5 | Medium: AxMsRdpClient9 may keep rendering into its HWND even when parent is collapsed. UAT-verifiable. If a visual leak persists, fall back to Pitfall 5 Option (B) (disconnect) and surface to user. |
| A9 | Velopack `VelopackApp.Build().Run()` DOES NOT swallow `AppDomain.UnhandledException` registrations that precede `new App()` — handlers installed in Main after Velopack remain active | Pattern 4 | Low: `CrashHandler.Install()` runs after Velopack.Run but before App construction; these are all in the same AppDomain. Standard .NET behaviour. |
| A10 | Crash dialog can be shown from `DispatcherUnhandledException` handler when the Dispatcher itself is healthy (most cases); falls back to emergency log-file write if Dispatcher is broken | Pattern 4 | Low: matches community-accepted pattern. Extremely rare case (Dispatcher corrupted) gets a log line, not a dialog. |
| A11 | `Process.Start(Process.GetCurrentProcess().MainModule.FileName)` + `Application.Current.Shutdown()` is an acceptable restart path for the crash dialog (not using Velopack's RestartApp) | Pattern 4 + Don't Hand-Roll | Low: crash is NOT an update event; Velopack's restart semantics (waitForExit) are overkill. Plain re-exec is fine. |

**Confirmation needed from user before Plan 06-04 execution:** A1 (PBKDF2 vs Argon2id), A2 (SnackbarPresenter queue vs stack).

## Open Questions

1. **Question: Should the toast stack deviate from WPF-UI default (queue → visible stack)?**
   - What we know: WPF-UI `SnackbarPresenter` is FIFO queue with single visible item. UI-SPEC D-07 specifies "max 3 visible".
   - What's unclear: Does the user (George) prefer library-native queue semantics (simpler, WPF-UI-upgrade-safe) or custom stack presenter (matches spec literally, ~150 LoC + maintenance)?
   - Recommendation: Surface to `/gsd-plan-phase 6` as a blocker question before Plan 06-02 planning; default to library-native.

2. **Question: PBKDF2 vs Argon2id?**
   - What we know: SEC-01 text says PBKDF2; CLAUDE.md lists PBKDF2 as the approved hash. OWASP 2026 primary recommendation is Argon2id.
   - What's unclear: Is the PBKDF2 choice intentional (FIPS compliance) or residual from an older draft of SEC-01?
   - Recommendation: Confirm with user — if FIPS is not a requirement, consider upgrading to Argon2id via `Konscious.Security.Cryptography.Argon2` (+1 NuGet). For v1 as locked, stay on PBKDF2.

3. **Question: Should `Ctrl+L` also work when the command palette is open?**
   - What we know: D-18 says Ctrl+L is registered at `MainWindow.PreviewKeyDown` and idempotent when locked. The palette is a `ContentDialog` — its focus trap may or may not swallow Ctrl+L.
   - What's unclear: Will `MainWindow.PreviewKeyDown` fire for keystrokes inside an open `ContentDialog`? ContentDialog has its own input routing.
   - Recommendation: Assume YES (PreviewKeyDown is window-level, fires before the dialog). Verify in UAT. If NO, register a duplicate binding inside the palette dialog.

4. **Question: Does `ui:ContentDialog` with `IsFooterVisible="False"` still render the default title bar "X" close button?**
   - What we know: `IsFooterVisible` controls the FOOTER button row per WPF-UI API docs.
   - What's unclear: ContentDialog's title/chrome area is separate — does `IsFooterVisible` leave a bare title bar with close-X? For the lock overlay, we explicitly do NOT want a close button (users must unlock, not dismiss).
   - Recommendation: Plan 06-04 Wave 0 visual spike — render a dummy ContentDialog with `IsFooterVisible="False"`, observe chrome, adjust template if needed. Use `IsHitTestVisible="False"` on the chrome region or override the `TitleTemplate` with an empty template.

5. **Question: Cross-process audit log reading — future Phase 7 importer or external tools may read audit.jsonl while Deskbridge is appending. Do we need FileShare.ReadWrite?**
   - What we know: `File.AppendAllTextAsync` default opens with `FileShare.Read`.
   - What's unclear: If a support engineer opens audit.jsonl in VS Code / Notepad++ while Deskbridge is running, does the writer error?
   - Recommendation: Plan 06-01 task — use `File.Open(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)` + StreamWriter + WriteLine to explicitly allow shared read. Document in the IAuditLogger XML doc.

6. **Question: Does the command palette need to disable when the app is locked?**
   - What we know: Lock overlay is a ContentDialog; a second ContentDialog (palette) would stack ON TOP. Ctrl+Shift+P is at MainWindow.PreviewKeyDown level.
   - What's unclear: Should Ctrl+Shift+P no-op when locked, or allow (pointless) palette on top of lock overlay?
   - Recommendation: In `CommandPaletteService.TryOpen`, check `IAppLockState.IsLocked` first; if locked, return without opening. Simple gate.

## Sources

### Primary (HIGH confidence)

- [Microsoft Learn: SystemEvents.SessionSwitch Event](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents.sessionswitch?view=windowsdesktop-10.0) — static event, message-pump requirement, must detach handlers to avoid memory leak, InvalidOperationException in non-interactive contexts
- [Microsoft Learn: Rfc2898DeriveBytes.Pbkdf2 Method](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2) — static-method signature, SHA256 parameter, byte[] return
- [Microsoft Learn: DispatcherTimer Class](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatchertimer?view=windowsdesktop-10.0) — UI-thread-affine Tick, Dispatcher parameter overload
- [Microsoft Learn: Application.DispatcherUnhandledException](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-8.0) — `Handled` property semantics, UI-thread-only coverage
- [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html) — Argon2id as 2026 primary; PBKDF2-HMAC-SHA-256 @ 600,000 iterations as FIPS-path
- [WPF-UI 4.2 ContentDialog API](https://wpfui.lepo.co/api/Wpf.Ui.Controls.ContentDialog.html) — `IsFooterVisible`, `PrimaryButtonAppearance`, `DialogMaxWidth`
- [WPF-UI SnackbarPresenter source (main branch)](https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui/Controls/Snackbar/SnackbarPresenter.cs) — `Queue<Snackbar>`, `ShowQueuedSnackbarsAsync` sequential loop
- [Serilog wiki: Structured Data](https://github.com/serilog/serilog/wiki/structured-data) — `Destructure.ByTransforming<T>`, `Destructure.AsScalar<T>`, custom `IDestructuringPolicy`
- [WPF-UI Snackbar Service source](https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui/SnackbarService.cs) — single `_snackbar` instance, lazy init
- Project files read: Directory.Packages.props, App.xaml.cs, MainWindow.xaml.cs, ConnectionQueryService.cs, Enums.cs, AppEvents.cs, ConnectionEvents.cs, JsonConnectionStore via Phase 3 summary, Phase 5 summaries, WPF-UI-PITFALLS.md (repo root)

### Secondary (MEDIUM confidence — verified against primary source)

- [Anthony Simmon: Evolutive and robust password hashing using PBKDF2 in .NET](https://anthonysimmon.com/evolutive-and-robust-password-hashing-using-pbkdf2-in-dotnet/) — versioned hash format (`v1.salt.key`), fully concrete code
- [Serilog masking library (evjenio/masking.serilog)](https://github.com/evjenio/masking.serilog) — `ByMaskingProperties` third-party pattern; not used but shows alternatives
- [dev.to: Prevent sensitive data exposure in log with Serilog](https://dev.to/auvansangit/prevent-sensitive-data-exposure-in-log-with-serilog-1pk7) — IDestructuringPolicy implementation
- [dev.to: SemaphoreSlim in .NET, a practical guide](https://dev.to/stevsharp/semaphoreslim-in-net-a-practical-guide-with-the-rest-of-the-toolbox-1mh7) — WaitAsync + Release pattern

### Tertiary (LOW confidence — verified by cross-check with primary where indicated)

- [WPF-UI Discussion #1223 — How to use snackbar with ViewModel?](https://github.com/lepoco/wpfui/discussions/1223) — community Q&A, corroborates SnackbarPresenter single-snackbar default
- [copyprogramming.com: WPF Timer behavior after hibernate and sleep](https://copyprogramming.com/howto/how-system-timers-timer-behave-in-wpf-application-after-hibernate-and-sleep) — suspend/resume semantics match Microsoft docs
- [Johan Danforth blog: Detecting Idle Time with Global Mouse and Keyboard Hooks in WPF](https://weblogs.asp.net/jdanforth/detecting-idle-time-with-global-mouse-and-keyboard-hooks-in-wpf/) — InputManager.PreProcessInput idle pattern; community, not official
- [PasswordBox + MVVM attached property (multiple community blogs)](https://antonymale.co.uk/binding-to-a-passwordbox-password-in-wpf.html) — attached-property pattern; code-behind simpler for single use

## Metadata

**Confidence breakdown:**

- Standard stack: HIGH — every library version verified against Directory.Packages.props; no new NuGets added.
- Architecture patterns: HIGH for logging/audit/PBKDF2/SessionSwitch; MEDIUM for idle detection (InputManager filter behaviour with AxHost is design-correct per docs but not UAT-verified); MEDIUM for crash dialog UI marshal (covered by Pattern 4 but order of hook install matters).
- Pitfalls: HIGH for 1,2,3,8,9 (verified against sources); MEDIUM for 4,7 (docs support, no code verification yet); MEDIUM for 5,6 (airspace interaction documented in CLAUDE.md WINFORMS-HOST-AIRSPACE.md — Plan 06-04 UAT critical).
- Validation architecture: HIGH — mirrors existing Phase 5 xUnit v3 + NSubstitute + FluentAssertions patterns.

**Research date:** 2026-04-14
**Valid until:** 2026-05-14 (fast-moving ecosystem checks: Serilog 4.x, WPF-UI 4.2.x NuGet versions, OWASP guidance revision)
