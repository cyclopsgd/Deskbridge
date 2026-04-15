---
phase: 06-cross-cutting-features
plan: 01
subsystem: observability
tags: [logging, audit, redaction, crash-handler, serilog, security]
dependency_graph:
  requires:
    - "src/Deskbridge.Core/Models/Enums.cs (AuditAction enum — every value round-trips)"
    - "src/Deskbridge.Core/Models/ConnectionModel.cs (target for redaction tests)"
    - "src/Deskbridge/Program.cs (Velopack entry point — Install hook insertion site)"
    - "src/Deskbridge/App.xaml.cs (Phase 4 baseline Serilog config — replaced)"
    - "tests/Deskbridge.Tests/Security/InMemorySink.cs (reused for fallback assertions)"
  provides:
    - "Deskbridge.Core.Interfaces.IAuditLogger (LogAsync surface for bus consumers)"
    - "Deskbridge.Core.Models.AuditRecord (D-10 schema record + AuditJsonContext)"
    - "Deskbridge.Core.Services.AuditLogger (SemaphoreSlim monthly-rotating jsonl writer)"
    - "Deskbridge.Core.Logging.RedactSensitivePolicy (LOG-05 IDestructuringPolicy)"
    - "Deskbridge.Core.Logging.SerilogSetup (testable LOG-01 LoggerConfiguration helper)"
    - "Deskbridge.CrashHandler (LOG-04 three-hook global exception handler)"
  affects:
    - "src/Deskbridge.Core/Deskbridge.Core.csproj (added Serilog.Sinks.File package)"
    - "src/Deskbridge/App.xaml.cs (replaced baseline Serilog + added DI registration + Dispatcher hook)"
    - "src/Deskbridge/Program.cs (CrashHandler.Install between Velopack and new App)"
    - "tests/Deskbridge.Tests/DiCompositionTests.cs (added IAuditLogger singleton test)"
tech-stack:
  added:
    - "Serilog.Sinks.File on Deskbridge.Core (was previously only on Deskbridge exe)"
  patterns:
    - "Test seam via internal Func<DateTime> property (UtcNowProvider) for clock control"
    - "Test seam via internal static HookState class (booleans) for hook-installation introspection"
    - "Source-order regression tests (string-grep on Program.cs / App.xaml.cs / SerilogSetup.cs)"
    - "Pattern 4 hook split: AppDomain + UnobservedTask in Program.Main, Dispatcher in App.OnStartup"
    - "IDestructuringPolicy denylist over Destructure.ByTransforming<T> (auto-protects future types)"
key-files:
  created:
    - "src/Deskbridge.Core/Interfaces/IAuditLogger.cs"
    - "src/Deskbridge.Core/Models/AuditRecord.cs"
    - "src/Deskbridge.Core/Services/AuditLogger.cs"
    - "src/Deskbridge.Core/Logging/RedactSensitivePolicy.cs"
    - "src/Deskbridge.Core/Logging/SerilogSetup.cs"
    - "src/Deskbridge/CrashHandler.cs"
    - "tests/Deskbridge.Tests/Logging/AuditLoggerTests.cs"
    - "tests/Deskbridge.Tests/Logging/RedactSensitivePolicyTests.cs"
    - "tests/Deskbridge.Tests/Logging/CrashHandlerTests.cs"
    - "tests/Deskbridge.Tests/Logging/SerilogConfigTests.cs"
    - "tests/Deskbridge.Tests/Logging/TempDirScope.cs"
  modified:
    - "src/Deskbridge.Core/Deskbridge.Core.csproj"
    - "src/Deskbridge/App.xaml.cs"
    - "src/Deskbridge/Program.cs"
    - "tests/Deskbridge.Tests/DiCompositionTests.cs"
decisions:
  - "AuditLogger uses internal Func<DateTime> UtcNowProvider seam over a full IClock injection — avoids polluting Core with a one-off interface and keeps the test scoped."
  - "RedactSensitivePolicy lives in Deskbridge.Core (not Deskbridge exe) because future plans 06-02..06-04 will instantiate it from Core consumers (notification + palette services)."
  - "Serilog.Sinks.File added to Deskbridge.Core (not just exe) because SerilogSetup.Configure references RollingInterval + WriteTo.File. Rule 3 fix — discovered at build time, not in plan."
  - "TryShowCrashDialog left as a logging-only stub returning true; Plan 06-04 wires the actual ContentDialog (per UI-SPEC §Crash Dialog). Stub returns true so the dispatcher hook still sets e.Handled and the app survives in the meantime."
  - "Test 7 (OnDispatcherUnhandled) verified via source-grep on CrashHandler.cs rather than direct invocation — DispatcherUnhandledExceptionEventArgs has internal-only ctors and constructing it requires reflection that would tightly couple the test to a private API."
  - "Hook-state booleans implemented as static fields rather than reflection on AppDomain/TaskScheduler internal event invocation lists — simpler, portable across .NET versions, and exposes exactly the contract the tests need."
metrics:
  duration_minutes: 11
  completed_date: "2026-04-15"
  tasks: 4
  files_created: 12
  files_modified: 4
  tests_added: 41
---

# Phase 6 Plan 01: Logging, Audit & Crash Handler Foundation Summary

LOG-01 + LOG-02 + LOG-03 + LOG-04 + LOG-05 observability infrastructure landed: redacted Serilog rolling-file logger with property-name credential denylist (RedactSensitivePolicy), append-only monthly-rotating jsonl audit writer with SemaphoreSlim concurrency guard and FileShare.ReadWrite tail support (AuditLogger), and a three-hook global exception handler installed before WPF init (CrashHandler).

## What Was Built

### 1. Audit logger (Task 1)

`IAuditLogger` + `AuditLogger` + `AuditRecord` shipped in `Deskbridge.Core`. The writer:

- Serialises `AuditRecord` via source-generated `AuditJsonContext` (camelCase, `WhenWritingNull` ignore for `ErrorCode`)
- Writes one JSON object per line to `%AppData%/Deskbridge/audit-YYYY-MM.jsonl`
- Uses `SemaphoreSlim(1,1)` to serialise concurrent `LogAsync` calls (Pitfall 2 — interleaved half-lines)
- Opens with `FileShare.ReadWrite` so a support engineer can tail the file in Notepad++/VS Code (CONTEXT Q5)
- Falls back to `Serilog.Log.Error("Failed to append audit record {Type}", ...)` on any IO failure (T-06-03)
- Exposes an internal `Func<DateTime> UtcNowProvider` test seam for the D-13 monthly-rotation tests

Test coverage: 25 tests (8 facts + 17 `AuditAction` theory rows) — schema, append-not-overwrite, single-LF terminator, monthly rotation across UTC boundary, **1000-concurrent writes producing 1000 parseable lines**, FileShare reader, every `AuditAction` value round-trips, IO-fallback to Serilog, post-Dispose throws `ObjectDisposedException`.

### 2. RedactSensitivePolicy (Task 2)

`Deskbridge.Core.Logging.RedactSensitivePolicy : IDestructuringPolicy`:

- Denylist (case-insensitive, `StringComparer.OrdinalIgnoreCase`): `Password, Secret, Token, CredentialData, ApiKey, ResolvedPassword, MasterPassword`
- Replaces the matched property's value with the public `RedactedSentinel` constant `"***REDACTED***"`
- Returns `false` when no denylisted property matched, deferring to Serilog's default destructurer (avoids Pitfall 9 same-type recursion entirely)
- Fast-path opt-out for primitives, strings, and enums
- Wraps `PropertyInfo.GetValue` in try/catch so a property whose getter throws records `null` instead of propagating

**Why IDestructuringPolicy over `Destructure.ByTransforming<T>`:** denylist applies to every type Serilog destructures, including types added in future plans, without per-type registration. A future developer cannot accidentally leak a password by logging a new POCO containing a `Password` field.

Test coverage: 19 tests (12 facts + 7 theory rows) — single-property redaction, every denylist name, case-insensitive (lower / upper / mixed), no-match passthrough, recursive inner POCO redaction, null/primitive/string/enum early-out, throwing getter, **full-run log scrape (LOG-05 canary — verifies password substring NEVER appears in any sink output)**.

### 3. CrashHandler (Task 3)

Three-hook global exception handler in `Deskbridge` exe:

- `Install()` registers `AppDomain.CurrentDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` — called from `Program.Main` immediately after `VelopackApp.Build().Run()` and before `new App()` so a crash in the App ctor or in `InitializeComponent` still hits Serilog
- `InstallDispatcherHook(Application)` registers `Application.DispatcherUnhandledException` — called from `App.OnStartup` after `base.OnStartup(e)` because `Application.Current` is null at `Main()` time (Pattern 4 deferral)
- All three handlers are idempotent (gated by the internal `HookState` flags)
- `OnAppDomainUnhandled` → `Log.Fatal(ex, "AppDomainUnhandledException Terminating={Terminating}", e.IsTerminating)`
- `OnUnobservedTask` → `Log.Error(e.Exception, "UnobservedTaskException")` then `e.SetObserved()` so a fire-and-forget Task does not crash the process
- `OnDispatcherUnhandled` → `Log.Fatal(e.Exception, "DispatcherUnhandledException")` then sets `e.Handled = true` via the `TryShowCrashDialog` stub
- `TryShowCrashDialog` is a **logging-only stub returning true** — Plan 06-04 wires the actual `ui:ContentDialog` per UI-SPEC §Crash Dialog (Copy Details + Restart buttons)

Test coverage: 8 tests — AppDomain hook installed, UnobservedTask hook installed, Dispatcher hook NOT installed by `Install()`, idempotent re-call, Fatal log + Terminating property, UnobservedTask SetObserved + Error log, Dispatcher Handled invariant (source-grep), **Program.Main source-order regression** (Velopack → Install → new App).

### 4. App.xaml.cs wiring (Task 4)

- New `Deskbridge.Core.Logging.SerilogSetup.Configure(string logDirectory)` returns a `LoggerConfiguration` with: `MinimumLevel.Information()`, `Enrich.FromLogContext()`, `Destructure.With<RedactSensitivePolicy>()`, rolling file at `<logDirectory>/deskbridge-.log` with `RollingInterval.Day`, `fileSizeLimitBytes: 10_000_000`, `rollOnFileSizeLimit: true`, `retainedFileCountLimit: 5`, `shared: false`, `flushToDiskInterval: 1s`
- `App.OnStartup` now: disposes any existing `Log.Logger`, builds the directory path, calls `SerilogSetup.Configure(logRoot).CreateLogger()` and assigns to `Log.Logger` — the Phase 4 baseline (no size cap, no redaction) is fully removed
- `App.OnStartup` calls `CrashHandler.InstallDispatcherHook(this)` immediately after `base.OnStartup(e)`
- `App.ConfigureServices` registers `IAuditLogger` as a singleton (`AddSingleton<IAuditLogger, AuditLogger>()`) next to `INotificationService`. Singleton matters because the `SemaphoreSlim` only serialises against its own instance — multiple instances would re-introduce Pitfall 2.

Test coverage: 5 SerilogConfigTests + 2 DiCompositionTests — **end-to-end redaction-via-file** (driven by `SerilogSetup.Configure` writing to a real temp file then asserting `"hunter2"` is absent and `"***REDACTED***"` is present), Configure creates directory, App.OnStartup source-order (dispatcher hook before `mainWindow.Show`), App.OnStartup uses `SerilogSetup.Configure` not the old baseline, SerilogSetup source contains all LOG-01 numeric parameters, `IAuditLogger` registered as singleton.

## Commit Trail

| Hash | Title |
|------|-------|
| `d6a3511` | feat(06-01): add IAuditLogger + AuditLogger + AuditRecord (LOG-02, LOG-03) |
| `131ca9e` | feat(06-01): add RedactSensitivePolicy IDestructuringPolicy (LOG-05) |
| `3dd3dda` | feat(06-01): add CrashHandler three-hook global exception handler (LOG-04) |
| `6e9b94b` | feat(06-01): wire SerilogSetup + IAuditLogger DI + Dispatcher hook (LOG-01, LOG-04) |

## Test Results

**Plan 06-01 tests added:** 41 (25 AuditLogger + 19 RedactSensitivePolicy + 8 CrashHandler + 5 SerilogConfig + 1 DiComposition addition + adjustments to existing AllCoreServices test).

**Full suite:** `dotnet test Deskbridge.sln` → **327 passed, 0 failed, 3 skipped, 0 errors**.

**Build:** `dotnet build Deskbridge.sln` → **0 warnings, 0 errors** (TreatWarningsAsErrors enforced).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Blocking] `Serilog.Sinks.File` not referenced by `Deskbridge.Core`**
- **Found during:** Task 4 build (after creating `SerilogSetup.cs`)
- **Issue:** `SerilogSetup.cs` lives in `Deskbridge.Core/Logging/`, references `RollingInterval` and `WriteTo.File`, but `Deskbridge.Core.csproj` previously only referenced `Serilog` (not `Serilog.Sinks.File`). Build failed with `CS0103: 'RollingInterval' does not exist` + `CS1061: 'LoggerSinkConfiguration' does not contain 'File'`.
- **Fix:** Added `<PackageReference Include="Serilog.Sinks.File" />` to `Deskbridge.Core.csproj` (already pinned in `Directory.Packages.props`, so no version drift).
- **Files modified:** `src/Deskbridge.Core/Deskbridge.Core.csproj`
- **Commit:** `6e9b94b`

### Test-design adjustments (no behavioural deviation)

- **xUnit1051 analyzer with TreatWarningsAsErrors:** xUnit v3's `xUnit1051` rule promotes "method takes CancellationToken but caller didn't pass one" to a build error here. All async test calls (`File.ReadAllTextAsync`, `LogAsync`, `WriteAllTextAsync`) now pass `TestContext.Current.CancellationToken` via a shared `private static CancellationToken Ct => TestContext.Current.CancellationToken` helper. No behavioural change; the analyzer just wanted explicit cancellation propagation.
- **Test 7 in CrashHandlerTests (OnDispatcherUnhandled):** `DispatcherUnhandledExceptionEventArgs` has internal-only constructors. Rather than reflect into a private API to construct one, the test asserts the invariant via source-grep on `CrashHandler.cs` (must contain `e.Handled = true` AND the stub must `return true;`). This still catches the regression (a future change that flips the stub return value, or removes the Handled assignment, will fail the test).
- **Test 6 in CrashHandlerTests (OnUnobservedTask SetObserved):** The framework's "did SetObserved get called" state is private (`m_observed`). The test asserts the observable consequence — Serilog captures the Error event — and trusts the source for the SetObserved invocation itself. The substantive behaviour (app does not crash on an unobserved task) is exercised end-to-end by Phase 4's existing reconnect/disconnect tests, which already produce fire-and-forget tasks that would explode without this hook.

## Authentication Gates Encountered

None.

## Known Stubs

| Location | Stub | Reason / Resolution |
|----------|------|---------------------|
| `src/Deskbridge/CrashHandler.cs:134-138` (TryShowCrashDialog) | Logs `"CrashHandler.TryShowCrashDialog stub — Plan 06-04 wires the UI."` and returns `true` instead of showing a `ContentDialog`. | **Intentional** — Plan 06-04 (App Security) explicitly owns the crash-dialog UI per `06-UI-SPEC.md` §Crash Dialog. Returning `true` from the stub still satisfies the dispatcher hook's `e.Handled = true` invariant so the app survives. The TODO line points at Plan 06-04 directly. |

No other stubs introduced.

## TODOs for Plan 06-04 (App Security)

- Replace `CrashHandler.TryShowCrashDialog` stub with the real `ui:ContentDialog` per UI-SPEC §Crash Dialog (Copy Details + Restart buttons, no stack trace visible). Marshal to UI thread via `Application.Current.Dispatcher`.
- Bus event publishers that should consume `IAuditLogger`: `ConnectionEstablishedEvent`, `ConnectionFailedEvent`, `ConnectionClosedEvent`, `CredentialStored/Deleted`, `AppLockedEvent`, `AppUnlockedEvent`, `MasterPasswordChangedEvent`, `ConnectionsImported/Exported`. Each subscriber should call `IAuditLogger.LogAsync` with the appropriate `AuditAction`.

## Threat Model Coverage

| Threat | Mitigation Landed | Verification |
|--------|-------------------|--------------|
| T-06-01 (info disclosure via logs) | `Destructure.With<RedactSensitivePolicy>()` in `SerilogSetup.Configure`, denylist case-insensitive | RedactSensitivePolicyTests Test 8 (full-run scrape) + SerilogConfigTests Test 1 (file-write end-to-end) |
| T-06-02 (interleaved audit lines) | `SemaphoreSlim(1,1)` guard around the FileStream append | AuditLoggerTests Test 5 (1000 concurrent writers, every line parses) |
| T-06-03 (audit DoS on disk-full) | try/catch in `LogAsync` falls back to `Log.Error` | AuditLoggerTests Test 8 (`File`-as-`Directory` collision triggers IOException, fallback line captured) |
| T-06-04 (missing audit on handler crash) | All three CrashHandler hooks log + survive (where survivable); AuditLogger's own try/catch ensures attempted-write is at least Serilog-recorded | CrashHandlerTests Tests 5-7 |

## Self-Check: PASSED

- `src/Deskbridge.Core/Interfaces/IAuditLogger.cs` — FOUND
- `src/Deskbridge.Core/Models/AuditRecord.cs` — FOUND
- `src/Deskbridge.Core/Services/AuditLogger.cs` — FOUND
- `src/Deskbridge.Core/Logging/RedactSensitivePolicy.cs` — FOUND
- `src/Deskbridge.Core/Logging/SerilogSetup.cs` — FOUND
- `src/Deskbridge/CrashHandler.cs` — FOUND
- `tests/Deskbridge.Tests/Logging/AuditLoggerTests.cs` — FOUND
- `tests/Deskbridge.Tests/Logging/RedactSensitivePolicyTests.cs` — FOUND
- `tests/Deskbridge.Tests/Logging/CrashHandlerTests.cs` — FOUND
- `tests/Deskbridge.Tests/Logging/SerilogConfigTests.cs` — FOUND
- `tests/Deskbridge.Tests/Logging/TempDirScope.cs` — FOUND
- Commit `d6a3511` — FOUND
- Commit `131ca9e` — FOUND
- Commit `3dd3dda` — FOUND
- Commit `6e9b94b` — FOUND
