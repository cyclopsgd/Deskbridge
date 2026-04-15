---
phase: 06
slug: cross-cutting-features
status: approved
nyquist_compliant: true
wave_0_complete: false
created: 2026-04-15
---

# Phase 06 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

Carries over from Phase 5's xUnit v3 / NSubstitute / FluentAssertions infrastructure — no new framework installs required. Phase 6 adds three UAT files under `tests/uat/` for manual verifications (lock overlay airspace, auto-lock suspend/resume, crash dialog) that are not unit-testable.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 |
| **Config file** | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| **Quick run command** | `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj --filter "Category!=Slow&Category!=UAT"` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~30-60s quick, ~90s full |
| **STA collection** | `[Collection("RDP-STA")]` fixture from Phase 5 reused for toast stack timers, lock overlay PasswordBox, and AppLockController Visibility tests |

---

## Sampling Rate

- **After every task commit:** Run quick filter.
- **After every plan wave:** Run full suite.
- **Before `/gsd-verify-work`:** Full suite must be green.
- **Max feedback latency:** 60 seconds.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 06-01-T1 | 01 | 1 | LOG-02, LOG-03 | T-06-02 | Audit append-only with SemaphoreSlim + FileShare.ReadWrite + monthly rotation | unit (9 tests incl. 1000-concurrency) | `dotnet test --filter FullyQualifiedName~AuditLogger` | ❌ Wave 0 | ⬜ pending |
| 06-01-T2 | 01 | 1 | LOG-05 | T-06-01 | RedactSensitivePolicy denylist of 7 property names across ALL types | unit (9 tests incl. full-run log scrape) | `dotnet test --filter FullyQualifiedName~RedactSensitivePolicy` | ❌ Wave 0 | ⬜ pending |
| 06-01-T3 | 01 | 1 | LOG-04 | — | CrashHandler three-hook registration + Program.Main ordering regression | unit (8 tests incl. source-order) | `dotnet test --filter FullyQualifiedName~CrashHandler` | ❌ Wave 0 | ⬜ pending |
| 06-01-T4 | 01 | 1 | LOG-01, LOG-05 | T-06-01 | Serilog rolling config with 10MB cap + 5 retained + RedactSensitivePolicy wired | unit (3 tests incl. file-write redaction scrape) | `dotnet test --filter FullyQualifiedName~SerilogConfig` | ❌ Wave 0 | ⬜ pending |
| 06-02-T1 | 02 | 2 | NOTF-01 | — | Custom ItemsControl stack: cap=3, FIFO evict, newest-on-top, hover-pause | unit (8 tests, STA) | `dotnet test --filter FullyQualifiedName~ToastStackViewModel` | ❌ Wave 0 | ⬜ pending |
| 06-02-T2 | 02 | 2 | NOTF-01, NOTF-02, NOTF-03 | — | 6-event bus subscriber + UI-SPEC copy exactness + zero IContentDialogService calls | unit (10 tests) | `dotnet test --filter FullyQualifiedName~ToastSubscriptionService` | ❌ Wave 0 | ⬜ pending |
| 06-02-T3 | 02 | 2 | NOTF-04 | — | WindowStateService atomic tmp-rename + defaults-on-missing + schema v1 gating | unit (6 tests) | `dotnet test --filter FullyQualifiedName~WindowStateService` | ❌ Wave 0 | ⬜ pending |
| 06-02-T4 | 02 | 2 | NOTF-01, NOTF-04 | — | MainWindow wiring: ToastStackControl swap, Window state load/save, eager-resolve ordering | unit (DiComposition source-order tests) | `dotnet test --filter FullyQualifiedName~DiComposition` | ✓ existing — extend | ⬜ pending |
| 06-03-T1 | 03 | 3 | CMD-02, CMD-03, Q6 gate | — | ICommandPaletteService 4-command registry + ScoreCommand algorithm parity with ConnectionQueryService + IAppLockState scaffolding | unit (8 tests) | `dotnet test --filter FullyQualifiedName~CommandPaletteService` | ❌ Wave 0 | ⬜ pending |
| 06-03-T2 | 03 | 3 | CMD-01, CMD-03 | — | CommandPaletteViewModel empty-state recents+commands / ranked-merge / Pitfall 8 Enter | unit (10 tests; CMD-03 via NSubstitute IConnectionQuery.Received(1)) | `dotnet test --filter FullyQualifiedName~CommandPaletteViewModel` | ❌ Wave 0 | ⬜ pending |
| 06-03-T3 | 03 | 3 | CMD-01, CMD-04 | T-06-CMD-01 | KeyboardShortcutRouter Ctrl+Shift+P/N/T/F11/Esc; MainWindow IsLocked gate for Ctrl+Shift+P (Q6) | unit (+ source-order check for IsLocked gate) | `dotnet test --filter FullyQualifiedName~KeyboardShortcutRouter` | ✓ existing — extend | ⬜ pending |
| 06-04-T0 | 04 | 4 | SEC-02 | — | Wave 0 visual spike: LockOverlayDialog chrome (no X close, no footer) | manual visual (Q4) | (n/a — checkpoint:human-verify) | N/A | ⬜ pending |
| 06-04-T1 | 04 | 4 | SEC-01 | T-06-03 | PBKDF2 600k + FixedTimeEquals + auth.json v1 format + atomic write | unit (10 tests incl. KDF cost ≥100ms) | `dotnet test --filter FullyQualifiedName~MasterPasswordService` | ❌ Wave 0 | ⬜ pending |
| 06-04-T2 | 04 | 4 | SEC-01, SEC-02 | T-06-05 | LockOverlayViewModel first-run + unlock modes + Pitfall 8 Enter + error copy exactness | unit (8 tests, STA for Pitfall 8) | `dotnet test --filter FullyQualifiedName~LockOverlayViewModel` | ❌ Wave 0 | ⬜ pending |
| 06-04-T3 | 04 | 4 | SEC-03, SEC-04 | T-06-04, T-06-04b, T-06-04c | IdleLockService timer + Pitfall 6 filter; SessionLockService strong-ref + marshal + unsub; AppLockController Pitfall 5 capture/collapse/restore + audit | unit (~12 tests across 3 files, STA for AppLockController) | `dotnet test --filter FullyQualifiedName~IdleLockService\|FullyQualifiedName~SessionLockService\|FullyQualifiedName~AppLockController` | ❌ Wave 0 | ⬜ pending |
| 06-04-T4 | 04 | 4 | LOG-04 completion | — | CrashDialog real UI replacing Plan 06-01 stub; Copy Details clipboard + Restart | build + static source check (UAT covers runtime) | `dotnet build` + `grep Clipboard.SetText` | ❌ Wave 0 | ⬜ pending |
| 06-04-T5 | 04 | 4 | SEC-04, SEC-05 | — | Ctrl+L router + Window.StateChanged + settings panel bindings + App.xaml.cs startup lock wiring | unit (+ DiComposition source-order) | `dotnet test --filter FullyQualifiedName~KeyboardShortcutRouter\|FullyQualifiedName~DiComposition` | ✓ existing — extend | ⬜ pending |
| 06-04-T6 | 04 | 4 | SEC-02 airspace, SEC-03 suspend/resume, LOG-04 runtime | T-06-04, T-06-SEC-05 | Manual UAT for behaviours that are not unit-testable | manual (3 UAT files) | (n/a — checkpoint:human-verify) | `tests/uat/phase-06-*.md` | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Before Task 1 of Plan 06-04 can proceed, the Wave 0 visual spike (Task 0) must render an empty LockOverlayDialog and the user must approve the chrome behaviour (Q4 resolution: does IsFooterVisible=False hide the footer AND the title-bar X, or do we need a TitleTemplate override?). No other Wave 0 scaffolding needed — Plans 06-01 / 06-02 / 06-03 create their own interfaces + tests in their first task of each plan without a separate Wave 0.

All other Wave 0 file creations (test files for missing `<automated>` references) are authored inside the first task of each plan — no standalone Wave 0 task is needed because every plan creates its own interfaces + tests together.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Lock overlay ContentDialog chrome (no X close button, no footer) | SEC-02 | WPF visual rendering not testable headlessly | Wave 0 spike in Plan 06-04 Task 0 (checkpoint:human-verify) |
| Auto-lock timer across Windows suspend/resume | SEC-03 | Requires real suspend cycle | `tests/uat/phase-06-auto-lock.md` step 4 (documents Pitfall 4 accept-path) |
| SystemEvents.SessionSwitch fires reliably | SEC-04 | Requires Windows session lock event | `tests/uat/phase-06-security.md` step 6 |
| Airspace: WindowsFormsHost hidden under lock overlay (Pitfall 5 Option A) | SEC-02 | Requires live RDP session + lock trigger | `tests/uat/phase-06-security.md` step 4 (CRITICAL gate) |
| Pitfall 6: AxHost input does NOT reset Deskbridge idle timer | SEC-03 | Requires live RDP session + sustained typing | `tests/uat/phase-06-auto-lock.md` step 3 |
| Crash dialog appearance + Copy Details clipboard transform + Restart | LOG-04 | Requires real unhandled exception | `tests/uat/phase-06-crash.md` (3 steps + cleanup) |
| Toast visual stack (3 visible, newest top, hover pauses) | NOTF-01 | Visual timing + hover behaviour | DEFERRED — ToastStackViewModelTests cover the semantics via DispatcherFrame pumping; if manual visual smoke reveals issues, add `tests/uat/phase-06-notifications.md` in a gap-closure plan. |
| Window state round-trips across close/reopen cycles | NOTF-04 | Requires real window close + process restart | Covered by WindowStateServiceTests at unit level; manual smoke is the 2-minute "close + reopen" check in tests/uat/phase-06-security.md step 1 (implicit). |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0/checkpoint dependencies (Task 0 + Task 6 in Plan 06-04 are explicit checkpoint:human-verify — they have no `<automated>` block but are gated by `resume-signal`)
- [x] Sampling continuity: no 3 consecutive tasks without automated verify (every auto task has `<automated>` command; the 2 checkpoints in 06-04 are bracketed by automated tasks)
- [x] Wave 0 covers the single visual chrome gate (Task 0 of Plan 06-04)
- [x] No watch-mode flags
- [x] Feedback latency < 60s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** approved by planner 2026-04-15
