---
phase: 5
slug: tab-management
status: draft
nyquist_compliant: true
wave_0_complete: false
created: 2026-04-13
---

# Phase 5 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 + FluentAssertions + NSubstitute (established Phase 1) |
| **Config file** | `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` (xUnit v3, OutputType=Exe) |
| **Quick run command** | `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj --filter "FullyQualifiedName~Tab|FullyQualifiedName~KeyboardShortcut|FullyQualifiedName~HostContainer|FullyQualifiedName~AirspaceSwapper|FullyQualifiedName~SwitchToExistingTab|FullyQualifiedName~DiComposition|FullyQualifiedName~Coordinator|FullyQualifiedName~MainWindowViewModel"` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~30–60 seconds (quick filter), ~2–3 minutes (full) |

---

## Sampling Rate

- **After every task commit:** Run quick filter for the tests touched (`dotnet test --filter "FullyQualifiedName~<symbol>"`).
- **After every plan wave:** Full suite `dotnet test`.
- **Before `/gsd-verify-work`:** Full suite must be green + 4 UAT checklists executed (see Plan 03 Task 3).
- **Max feedback latency:** 60 seconds for the quick filter; < 3 min for full suite (excluding the smoke tests that require a live RDP host).

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 05-01 T1 | 01 | 1 | TAB-05 (scaffolds) | T-05-05 | AirspaceSwapper log redaction preserved | unit + STA | `dotnet test --filter "FullyQualifiedName~AirspaceSwapper|FullyQualifiedName~TabHostManager"` | ❌ Wave 0 | ⬜ pending |
| 05-01 T2 | 01 | 1 | TAB-01, TAB-04, TAB-05 | T-05-01, T-05-02, T-05-04 | Log only ConnectionId + Hostname; snapshot-iterate CloseAllAsync; **cancels reconnect CTS before DisconnectAsync (Q2 resolution)** | unit (STA for HostMounted sim) | `dotnet test --filter "FullyQualifiedName~TabHostManager"` | ❌ Wave 0 | ⬜ pending |
| 05-01 T3 | 01 | 1 | TAB-01 (via D-02) | T-05-02 | ex.GetType + HResult only logging in Coordinator catches | unit + STA | `dotnet test --filter "FullyQualifiedName~Coordinator|FullyQualifiedName~SwitchToExistingTab|FullyQualifiedName~MainWindowViewModel"` | ❌ Wave 0 | ⬜ pending |
| 05-02 T1 | 02 | 2 | TAB-01, TAB-02 | T-05-03 (DoS on OnClosing) | Sequential CloseAllAsync; swallow best-effort catch | STA integration | `dotnet test --filter "FullyQualifiedName~HostContainerPersistence"` | ❌ Wave 0 | ⬜ pending |
| 05-02 T2 | 02 | 2 | TAB-01, TAB-05 | T-05-06 (status bar copy), T-05-07 (overlay dict), T-05-08 (model lookup) | No credential fields in status bar; silent no-op for deleted LRU entries | unit | `dotnet test --filter "FullyQualifiedName~DiComposition|FullyQualifiedName~MainWindowViewModel"` | ❌ Wave 0 | ⬜ pending |
| 05-02 T3 | 02 | 2 | TAB-01, TAB-02 | — | HostContainer invariant under rapid switch + Move | STA integration | `dotnet test --filter "FullyQualifiedName~HostContainerPersistence|FullyQualifiedName~TabReorderIntegration"` | ❌ Wave 0 | ⬜ pending |
| 05-03 T1 | 03 | 3 | TAB-01 | T-05-01 (tooltip/status copy) | TooltipText only composes Hostname + state + resolution; no credential fields | unit + XAML smoke | `dotnet test --filter "FullyQualifiedName~TabItemViewModel|FullyQualifiedName~MainWindowXamlContextMenu"` | ❌ Wave 0 | ⬜ pending |
| 05-03 T2 | 03 | 3 | TAB-01, TAB-03 | T-05-09 (DataObject), T-05-10 (rapid reorder) | In-process DataObject; O(n) Move tolerable | STA integration | `dotnet test --filter "FullyQualifiedName~TabReorderIntegration|FullyQualifiedName~KeyboardShortcut"` | ❌ Wave 0 | ⬜ pending |
| 05-03 T3 | 03 | 3 | TAB-01, TAB-02, TAB-03 | T-05-11 (UAT file placeholders) | 4 UAT checklists cover A1 keyboard, multi-host GDI, drag visuals, state matrix | manual | `tests/uat/phase-05-*.md` human sign-off | ❌ Wave 0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

These files MUST exist (as skip-scaffolds or tests that currently fail) before Plan 01 Task 2 can claim GREEN:

- [ ] `tests/Deskbridge.Tests/Tabs/TabHostManagerTests.cs` — 10 scaffold [Fact]s with `Assert.Skip("Wave 0 — implementation lands in Task 2")`
- [ ] `tests/Deskbridge.Tests/Tabs/TabHostManagerLruTests.cs` — 4 scaffold [Fact]s for LRU dedupe/cap/empty
- [ ] `tests/Deskbridge.Tests/Rdp/AirspaceSwapperTests.cs` — new `MultiHost_ExitSizeMove_RestoresPreDragVisibility` test (NOT a scaffold — passes after Plan 01 Task 1 AirspaceSwapper patch)
- [ ] `src/Deskbridge.Core/Events/TabEvents.cs` with TabOpenedEvent / TabClosedEvent / TabSwitchedEvent / TabStateChangedEvent records
- [ ] `src/Deskbridge.Core/Models/TabState.cs` (or `src/Deskbridge/Models/TabState.cs` if the WPF project must own it) with Connecting / Connected / Reconnecting / Error enum

After Plan 02 Task 1 completes:
- [ ] `tests/Deskbridge.Tests/Integration/HostContainerPersistenceTests.cs` — 6 STA integration tests (HostContainer existence, no re-parent on switch, visibility correct, OnClosing drain, overlay tag-based visibility)

After Plan 02 Task 3 completes:
- [ ] `tests/Deskbridge.Tests/Integration/TabReorderIntegrationTests.cs` — 2 STA integration tests (Move doesn't re-parent, Tag correlation survives)

After Plan 03 Task 1 completes:
- [ ] `tests/Deskbridge.Tests/ViewModels/MainWindowXamlContextMenuTests.cs` — STA smoke test asserting ContextMenu has exactly 3 items in order
- [ ] `tests/Deskbridge.Tests/ViewModels/TabItemViewModelTests.cs` — 6 additional tests covering State / derived / TooltipText

After Plan 03 Task 2 completes:
- [ ] `tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs` — 12 STA integration tests routing keyboard events and asserting command invocations

No new framework install — xUnit v3 stack from Phase 1 carries forward. No new MSBuild property changes. No new NuGet packages.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions | Checklist |
|----------|-------------|------------|-------------------|-----------|
| Ctrl+Tab / Ctrl+Shift+Tab with RDP focused | TAB-03, Assumption A1 | UAT — verifies research Assumption A1 (PreviewKeyDown fires before AxHost when KeyboardHookMode=0). Cannot unit-test; requires live AxHost + keyboard. **If this fails, Phase 6 global shortcut service must pull forward.** | See `tests/uat/phase-05-keyboard.md` | Plan 03 Task 3 |
| Multi-host GDI stability (14 sessions open+close) | TAB-01, TAB-02, TAB-04 | Requires real RDP server + GDI handle counter (GetGuiResources) — cannot mock AxMsRdpClient9 GDI semantics | See `tests/uat/phase-05-multihost-gdi.md` | Plan 03 Task 3 |
| Drag-to-reorder visual feedback | D-13 | Visual behavior — 2px accent insertion line + no conflict with middle-click close | See `tests/uat/phase-05-drag.md` | Plan 03 Task 3 |
| Background reconnect UX (amber badge on inactive tab) | D-14 | Visual — amber badge on background tab, overlay hidden until switch; no auto-switch | See `tests/uat/phase-05-state-matrix.md` | Plan 03 Task 3 |
| 15+ threshold Snackbar (visual + timing) | TAB-04 | Visual — one-time toast on crossing 14→15, dismissable, auto-dismiss ~6s | See `tests/uat/phase-05-multihost-gdi.md` | Plan 03 Task 3 |
| TabState mutual exclusion under live transitions | D-12 | Visual — exactly one indicator (ProgressRing / amber / red) visible per tab | See `tests/uat/phase-05-state-matrix.md` | Plan 03 Task 3 |
| Drag-resize snapshot smoothness across multi-host | D-04 + Phase 4 D-13 carry-over | Visual — verify AirspaceSwapper's per-host pre-drag visibility capture works with multiple hosts | See `tests/uat/phase-05-multihost-gdi.md` last bullet | Plan 03 Task 3 |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies (planner populated: ✅ every task has an `<automated>` stanza, Plan 03 Task 3 is the sole `checkpoint:human-verify`)
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify (✅ every task has automated verify EXCEPT Plan 03 Task 3 which is the UAT gate — allowed per deep_work_rules for checkpoint tasks)
- [ ] Wave 0 covers all MISSING references (pending executor — Plan 01 Task 1 creates scaffolds)
- [ ] No watch-mode flags (✅)
- [ ] Feedback latency < 60s for quick filter (✅)
- [ ] `nyquist_compliant: true` set in frontmatter (✅ set above)
- [ ] Manual UAT list reviewed against Phase 5 success criteria (Roadmap §Phase 5) (✅ — every Roadmap success criterion maps to a UAT checklist line)

**Approval:** pending executor
