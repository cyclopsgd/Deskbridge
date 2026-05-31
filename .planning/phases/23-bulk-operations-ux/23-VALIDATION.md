---
phase: 23
slug: bulk-operations-ux
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-31
---

# Phase 23 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.
> Scope: **Connect All (BULK-01) / Disconnect All (BULK-02) / Bulk Edit (BULK-03)** — per the approved 23-UI-SPEC.md, ROADMAP, and REQUIREMENTS.md. Bulk delete/move already ship and are out of scope.
> Derived from the `## Validation Architecture` section of `23-RESEARCH.md`.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit.v3 3.2.* + FluentAssertions 8.9.* + NSubstitute 5.3.* (`Directory.Packages.props:25-27`, `Deskbridge.Tests.csproj:13-15`) |
| **Config file** | none — Microsoft.Testing.Platform via `TestingPlatformDotnetTestSupport` (`Deskbridge.Tests.csproj:6`) |
| **Quick run command** | `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj --filter "FullyQualifiedName~BulkEdit\|FullyQualifiedName~ConnectAll\|FullyQualifiedName~DisconnectAll"` |
| **Full suite command** | `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj` |
| **Estimated runtime** | ~5s quick (no STA), ~baseline for full suite |

Fixture convention: per-file `<Content Include>` with `CopyToOutputDirectory=PreserveNewest` OR a directory glob (`Fixtures\large\*.xml`) (`Deskbridge.Tests.csproj:25-32`). **Bulk-ops tests need no XML fixtures** — construct `ConnectionModel`/`ConnectionGroup` in-memory against a temp-file `JsonConnectionStore` (mirror `BulkDeleteTests.cs:14-20`). STA-requiring tests use `StaCollectionFixture`; VM-logic-only tests need no STA.

---

## Sampling Rate

- **After every task commit:** Run the quick command (< 5s, no STA)
- **After every plan wave:** Run the full suite (753/754 baseline per STATE.md)
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** ~5 seconds (quick), full suite at wave boundaries

---

## Per-Task Verification Map

| Req ID | Behavior | Test Type | Automated Command (filter) | File Exists |
|--------|----------|-----------|-------------|-------------|
| BULK-03 | Bulk edit writes ONLY checked fields to all selected; unchecked divergent fields untouched | unit (VM) | `~BulkEditViewModel` | ❌ W0 |
| BULK-03 | Divergent field → `IsShared=false` / "Multiple values"; shared field pre-fills shared value | unit (VM) | `~BulkEditViewModel` | ❌ W0 |
| BULK-03 | `CanApply` false until ≥1 field checked; true after | unit (VM) | `~BulkEditViewModel` | ❌ W0 |
| BULK-03 | Apply persists via single `SaveBatch` (not N× Save) and survives store reload | unit (store) | `~BulkEdit` (mirror `BulkDeleteTests.cs:126`) | ❌ W0 |
| BULK-03 | Name is never modified by bulk edit | unit (VM) | `~BulkEditViewModel` | ❌ W0 |
| BULK-03 | Port validation (1–65535) and non-empty Hostname on checked fields | unit (VM) | `~BulkEditViewModel` | ❌ W0 |
| BULK-01 | Projected count = `ActiveCount + group.ConnectionCount`; confirm shown only when `> threshold && ConfirmBeforeBulkOperations` | unit (VM, mocked tab mgr/settings) | `~ConnectAll` | ❌ W0 |
| BULK-01 | Connect All publishes `ConnectionRequestedEvent` per descendant; skips already-open tabs (SwitchTo) | unit (VM, fake `IEventBus` + fake tab mgr) | `~ConnectAll` | ❌ W0 |
| BULK-01 | ≤ threshold OR `ConfirmBeforeBulkOperations=false` → connects immediately, no dialog | unit (VM) | `~ConnectAll` | ❌ W0 |
| BULK-01 | Threshold boundary: `== threshold` does NOT warn; `threshold+1` warns (confirm against `TabHostManager.cs:368`) | unit (VM) | `~ConnectAll` | ❌ W0 |
| BULK-02 | Disconnect All calls `CloseTabAsync` for every descendant with an active tab; none for inactive | unit (VM, fake tab mgr) | `~DisconnectAll` | ❌ W0 |
| BULK-02 | Disconnect All enabled iff group has ≥1 active session (`GroupHasActiveSessions`) | unit (VM) | `~DisconnectAll` | ❌ W0 |
| Cross | Bulk edit selection after Apply: cleared (default decision A3) | unit (VM) | `~BulkEditViewModel` | ❌ W0 |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky — all currently ❌ Wave 0 (not yet written).*

---

## Wave 0 Requirements

- [ ] `tests/Deskbridge.Tests/ViewModels/BulkEditViewModelTests.cs` — field diffing, per-field enable, ApplyToModels, Port/Hostname validation, Name-excluded (BULK-03)
- [ ] `tests/Deskbridge.Tests/Services/BulkEditPersistenceTests.cs` — SaveBatch round-trip, single atomic write (BULK-03); mirror `BulkDeleteTests.cs`
- [ ] `tests/Deskbridge.Tests/ViewModels/ConnectAllTests.cs` — count/threshold/confirm/publish/skip-open logic (BULK-01); fake `ITabHostManager` + `IEventBus` + `IWindowStateService`
- [ ] `tests/Deskbridge.Tests/ViewModels/DisconnectAllTests.cs` — descendant close + enable-state (BULK-02); fake tab manager
- [ ] No new fixtures needed (in-memory models + temp-file store)
- [ ] No framework install needed (xUnit.v3 already configured)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| GDI confirmation ContentDialog renders Warning InfoBar over an active RDP viewport without airspace bleed | BULK-01 | ContentDialog + WindowsFormsHost airspace is visual; not unit-testable | Open ≥1 RDP session, right-click a group exceeding threshold, choose Connect All, confirm the warning dialog z-orders correctly and the viewport is snapshot-hidden |
| BulkEditDialog ComboBox (Credential mode / Group) shows correct ItemTemplate (no "- - -" glyph) | BULK-03 | WPF rendering trap | Select ≥2 connections → Edit… → inspect ComboBoxes render readable items |
| Disconnect All menu item disabled when group has no active sessions (WPF-UI #1387 imperative enable) | BULK-02 | Menu enable-state set imperatively at build time; visual | Right-click a group with no sessions → Disconnect All greyed; with sessions → enabled |

---

## Validation Sign-Off

- [ ] All tasks have automated verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 5s (quick)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending

---

## Notes for Planner (testability — highest-value decision)

- Make `BulkEditViewModel` a **thin, dependency-light VM** (ideally takes just the selected `ConnectionModel`s) so its diffing/apply logic tests need no mocks.
- New command logic on `ConnectionTreeViewModel` (count/threshold/publish/close) is unit-testable by mocking `ITabHostManager`, `IEventBus`, `IWindowStateService`, `IConnectionStore` — follow the precedent in `ConnectionTreeStateTrackingTests` / `ConnectionTreeContextMenuParentContextTests`.
- The GDI threshold is private to `TabHostManager` — inject `IWindowStateService` (or equivalent) to make it reachable + mockable from the tree VM.

*Generated by /gsd-plan-phase Nyquist validation*
