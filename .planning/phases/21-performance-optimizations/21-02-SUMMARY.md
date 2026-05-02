---
phase: 21-performance-optimizations
plan: 02
subsystem: ui
tags: [debounce, dispatcher-timer, search, mvvm, tdd, di]

# Dependency graph
requires:
  - phase: 03-connection-management
    provides: ConnectionTreeViewModel.OnSearchTextChanged + SearchText [ObservableProperty]
  - phase: 16-rdp-quality
    provides: DispatcherTimer reset-on-input pattern (MainWindow.xaml.cs:411-418)
  - phase: 21-performance-optimizations/21-01
    provides: Phase 21 baseline (XAML virtualization + group count badges)
provides:
  - IDebouncer abstraction (Schedule/Cancel) + DispatcherTimerDebouncer 250ms production impl
  - Trailing-fire search debounce on ConnectionTreeViewModel.SearchText (D-01)
  - Synchronous clear-search bypass restoring full tree without debounce wait (D-02)
  - FakeDebouncer test double pattern for VM-side timing tests under xUnit.v3
affects: [21-03 (async startup load — same ConnectionTreeViewModel ctor seam), 21-04 (perf regression validation), 23 (bulk operations UX)]

# Tech tracking
tech-stack:
  added: []  # No new package dependencies — pure abstraction over System.Windows.Threading
  patterns:
    - "IDebouncer abstraction with DispatcherTimer-backed prod impl + synchronous test double"
    - "5-step DispatcherTimer reset-on-input pattern (Stop/??=new/Tick-=/+=/Start) reused"
    - "Trailing ctor parameter convention for new VM dependencies — preserves DI auto-resolution"

key-files:
  created:
    - src/Deskbridge.Core/Interfaces/IDebouncer.cs
    - src/Deskbridge/Services/DispatcherTimerDebouncer.cs
    - tests/Deskbridge.Tests/Fakes/FakeDebouncer.cs
    - tests/Deskbridge.Tests/ViewModels/ConnectionTreeSearchDebounceTests.cs
  modified:
    - src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs
    - src/Deskbridge/App.xaml.cs
    - tests/Deskbridge.Tests/ViewModels/ConnectionTreeStateTrackingTests.cs
    - tests/Deskbridge.Tests/ViewModels/SwitchToExistingTabTests.cs
    - tests/Deskbridge.Tests/ViewModels/MainWindowViewModelTests.cs
    - tests/Deskbridge.Tests/ViewModels/ConnectionTreeContextMenuParentContextTests.cs
    - tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs
    - tests/Deskbridge.Tests/Integration/HostContainerPersistenceTests.cs

key-decisions:
  - "IDebouncer abstraction (over Action<TimeSpan,Action> delegate) — explicit two-method interface (Schedule/Cancel) reads as a domain concept and makes Cancel testable separately from Schedule"
  - "FakeDebouncer is internal sealed in tests/Fakes/ namespace — no MS.DI auto-discovery, mirrors NSubstitute pattern at test-construction sites"
  - "AddTransient<IDebouncer> (not Singleton) — each consumer gets its own DispatcherTimer; the VM is the only consumer today but the contract is per-consumer in case search debounce is later split (e.g., command palette)"
  - "Trailing ctor parameter (not optional default) so MS.DI auto-resolves; would-be-null defaults silently skip injection in MS.DI's binder and would defeat the abstraction"
  - "ApplySearchFilter is private (not internal) — tests assert via ScheduleCallCount counters at the IDebouncer seam, not by reaching into VM internals"

patterns-established:
  - "VM debounce abstraction pattern: trailing IDebouncer ctor param + synchronous bypass for canonical-empty input + Schedule-with-snapshot for non-empty input"
  - "FakeDebouncer Fire() pattern: tests deterministically run the trailing-fire action without dispatcher pump, asserting both ScheduleCallCount (rate) and HasPending (state-cleared after Fire)"

requirements-completed: [PERF-02]

# Metrics
duration: 7min
completed: 2026-05-02
---

# Phase 21 Plan 02: Search Debounce TDD Summary

**IDebouncer abstraction + 250ms DispatcherTimer-backed search debounce on ConnectionTreeViewModel, with synchronous clear-search bypass and a FakeDebouncer test double that makes the timing contract deterministically unit-testable under xUnit.v3.**

## Performance

- **Duration:** ~7 min
- **Started:** 2026-05-02T08:31:00Z (approx)
- **Completed:** 2026-05-02T08:38:00Z (approx)
- **Tasks:** 2 (RED + GREEN)
- **Files modified:** 12 (4 created, 8 modified)

## Accomplishments

- New `IDebouncer` interface in `Deskbridge.Core.Interfaces` exposing `Schedule(Action)` + `Cancel()`.
- `DispatcherTimerDebouncer` (production, 250ms) reuses the proven 5-step reset-on-input pattern verbatim from `MainWindow.xaml.cs:411-418` (Phase 16 STAB-03 resize debounce).
- `ConnectionTreeViewModel.OnSearchTextChanged` now splits two paths: empty/whitespace bypasses the debouncer (calls `Cancel()` then `ApplySearchFilter` synchronously per D-02); non-empty captures a snapshot in a closure and calls `Schedule()` per D-01/D-03.
- `ApplySearchFilter` private method holds the original filter body (ObservableCollection construction, `FlattenAndFilter` call, `RootItems` assignment) — extracted whole-cloth, no behaviour change.
- DI registration: `services.AddTransient<IDebouncer, DispatcherTimerDebouncer>()` placed adjacent to the existing `AddSingleton<ConnectionTreeViewModel>` so MS.DI auto-resolves the new ctor parameter.
- 4 new xUnit facts in `ConnectionTreeSearchDebounceTests` lock the contract: single-keystroke schedules once, five rapid keystrokes schedule five times then collapse to one trailing fire on `FakeDebouncer.Fire()`, empty/whitespace cancels and clears pending state.
- 6 sibling test classes that construct `ConnectionTreeViewModel` directly (`ConnectionTreeStateTrackingTests`, `SwitchToExistingTabTests`, `MainWindowViewModelTests`, `ConnectionTreeContextMenuParentContextTests`, `KeyboardShortcutTests`, `HostContainerPersistenceTests`) updated to pass `new FakeDebouncer()` as the trailing ctor argument and import `Deskbridge.Tests.Fakes`. Mechanical change with no test-logic implications.

## Task Commits

Each TDD gate was committed atomically:

1. **Task 1 (RED)** — `aa17a02` (test): add failing search-debounce tests for PERF-02. Build of `tests/Deskbridge.Tests` produces CS1729 (`ConnectionTreeViewModel` does not contain a constructor that takes 10 arguments) — proof the test exercises the new contract.
2. **Task 2 (GREEN)** — `0eab489` (feat): implement IDebouncer + 250ms DispatcherTimer search debounce. All 4 new facts pass, full test suite remains green (679 passed, 3 skipped, 0 failed), main app builds clean.

REFACTOR: not needed — both gates produced clean, readable code. The OnSearchTextChanged body is 8 lines including the empty-check guard; ApplySearchFilter is the unmodified original body. No further extraction warranted.

## Files Created/Modified

**Created:**
- `src/Deskbridge.Core/Interfaces/IDebouncer.cs` — Two-method interface (Schedule/Cancel), no XML doc to match `IConnectionStore.cs` style.
- `src/Deskbridge/Services/DispatcherTimerDebouncer.cs` — Production impl with `DebounceMs = 250` const, lazy `_timer` field, `OnTick` handler that stops first then invokes captured action.
- `tests/Deskbridge.Tests/Fakes/FakeDebouncer.cs` — Synchronous test double with `ScheduleCallCount`, `CancelCallCount`, `HasPending`, and manual `Fire()` to drive the trailing action without a Dispatcher.
- `tests/Deskbridge.Tests/ViewModels/ConnectionTreeSearchDebounceTests.cs` — 4 facts covering D-01 trailing fire and D-02 instant clear contract.

**Modified:**
- `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` — Added trailing `IDebouncer debouncer` ctor parameter, `_searchDebouncer` field, refactored `OnSearchTextChanged` to debounce-gate, extracted `ApplySearchFilter`.
- `src/Deskbridge/App.xaml.cs` — Added `services.AddTransient<IDebouncer, DispatcherTimerDebouncer>()` registration.
- 6 test files — Pass `new FakeDebouncer()` as trailing arg to `new ConnectionTreeViewModel(...)` calls; added `using Deskbridge.Tests.Fakes;`.

## Decisions Made

- **IDebouncer over Action<TimeSpan,Action>:** Two-method explicit interface reads as a domain concept; Cancel is testable as a separate verb, which the clear-search contract (D-02) requires asserting independently from Schedule.
- **Transient lifetime for IDebouncer:** Each consumer gets its own DispatcherTimer instance. ConnectionTreeViewModel is the only consumer today, but per-consumer is the correct default if Phase 23 (Bulk Operations) or future command-palette work adds a second debouncer consumer. No cost — `DispatcherTimer` is GC-eligible with the consumer.
- **Trailing ctor parameter, no default:** MS.DI auto-resolves required-parameter dependencies; an optional parameter with `null` default would be silently skipped by the binder, defeating the abstraction. The existing `AddSingleton<ConnectionTreeViewModel>` registration is plain (no factory lambda) so no manual wiring is needed.
- **`internal sealed` FakeDebouncer:** Tests-only fake; the `internal` access matches the production class style (`DispatcherTimerDebouncer` is also `internal sealed`).
- **`partial void OnSearchTextChanged` body became the gate, not the work:** The existing source-generated `[ObservableProperty]` on SearchText keeps its hook; we only changed what runs when the hook fires. The `ApplySearchFilter` extraction preserves byte-for-byte the original filter body, so D-02 ("clear-search restores full tree synchronously") is provably unchanged from the previous implementation — we only added the synchronous-bypass branch and the closure-capturing schedule branch.

## Deviations from Plan

None — plan executed exactly as written.

The plan's `<action>` for Task 2 step 4 anticipated mechanical ctor-arity updates for sibling test classes; the 6 affected files match exactly what the plan flagged. Imports were added per the plan's instruction to add `using Deskbridge.Tests.Fakes;`.

## Issues Encountered

- **xUnit.v3 MTP runner ignores `--filter` flag passed via `dotnet test`:** First attempt to run only the new tests via `dotnet test --filter "FullyQualifiedName~ConnectionTreeSearchDebounceTests"` produced a `MTP0001` warning and ran all 679 tests instead. Worked around by running the full suite (which is fast — 5s) and separately invoking the xUnit.v3 in-process runner via `dotnet run -- -class Deskbridge.Tests.ViewModels.ConnectionTreeSearchDebounceTests` to get the targeted "Total: 4, Passed: 4" report. No code change needed; this is purely a test-tooling observation for future TDD plans on this project.

## TDD Gate Compliance

- **RED gate:** `aa17a02` — `test(21-02): add failing search-debounce tests for PERF-02 (RED)`. Build correctly failed with CS1729 before any production change. No "test passes unexpectedly during RED" anti-pattern triggered (the new ctor parameter forces a compile-time gate).
- **GREEN gate:** `0eab489` — `feat(21-02): implement IDebouncer + 250ms DispatcherTimer search debounce (GREEN)`. All 4 new facts and 675 pre-existing facts pass.
- **REFACTOR gate:** None recorded — implementation is already minimal (the production debouncer is 25 lines; the VM gate is 8 lines). No code-cleanup commit warranted.

Sequence in `git log --oneline`: RED → GREEN. Compliant.

## Threat Surface Scan

No new trust boundaries, network surface, file-access patterns, or schema changes introduced. The `<threat_model>` register in 21-02-PLAN.md correctly classified T-21-02 (DoS) and T-21-03 (Tampering) as `accept` (LOW severity) — debouncer is consumer-controlled internal abstraction, no untrusted code path. No threat flags raised.

## Next Phase Readiness

- **21-03 (Async startup load):** Will modify the same `App.xaml.cs` `OnStartup` and `IConnectionStore`/`JsonConnectionStore` surface. The `IConnectionStore` interface remains untouched by this plan, so 21-03's `Task LoadAsync()` addition lands on a clean baseline.
- **21-04 (Perf regression validation):** SearchBenchmark in `Deskbridge.Benchmarks` (Phase 20) does not exercise the VM debounce — it tests the underlying `FlattenAndFilter` directly — so no benchmark regression is expected from this plan. The trailing-fire shape will improve perceived UX but won't move the BenchmarkDotNet numbers.
- **23 (Bulk Operations UX):** If a future bulk operation needs its own debounce (e.g., live-preview of "Edit N selected"), it can take its own `IDebouncer` from DI without touching this plan's surface.

No blockers for plan 21-03.

## Self-Check: PASSED

All 4 created files exist on disk. All 8 modified files exist on disk. Both task commits (`aa17a02` RED, `0eab489` GREEN) verified present in `git log --oneline --all`.

---
*Phase: 21-performance-optimizations*
*Completed: 2026-05-02*
