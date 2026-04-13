---
phase: 5
slug: tab-management
status: draft
nyquist_compliant: false
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
| **Config file** | `tests/Deskbridge.Core.Tests/Deskbridge.Core.Tests.csproj` and `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` (or current project names as established in Phase 1) |
| **Quick run command** | `dotnet test --filter "FullyQualifiedName~Phase5 \| FullyQualifiedName~TabHostManager \| FullyQualifiedName~Tab"` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~30–60 seconds (quick filter), ~2–3 minutes (full) |

---

## Sampling Rate

- **After every task commit:** Run quick filter for the tests touched (`dotnet test --filter "FullyQualifiedName~<symbol>"`)
- **After every plan wave:** Full suite `dotnet test`
- **Before `/gsd-verify-work`:** Full suite must be green + multi-host GDI smoke-test harness executed (see Manual-Only)
- **Max feedback latency:** 60 seconds for the quick filter

---

## Per-Task Verification Map

> Placeholder. Populated by gsd-planner during plan creation. Each task in 05-01-PLAN.md / 05-02-PLAN.md will be listed here with its REQ-ID mapping, test type (unit / integration / manual), and the automated command that verifies it. gsd-nyquist-auditor fills the Status column.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| *(to be filled by planner)* |  |  |  |  |  |  |  |  | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Deskbridge.Core.Tests/Services/TabHostManagerTests.cs` — stubs for TAB-01, TAB-02, TAB-04, TAB-05 coverage
- [ ] `tests/Deskbridge.Core.Tests/Services/TabLastClosedLruTests.cs` — if LRU is separated from TabHostManager (planner's choice); covers D-16 Ctrl+Shift+T dedupe
- [ ] `tests/Deskbridge.Tests/ViewModels/MainWindowViewModelTabTests.cs` — tab bar command plumbing (CloseTab / SwitchTab / CloseOthers / CloseAll) against mocked ITabHostManager
- [ ] Shared NSubstitute fixtures — `IEventBus`, `IConnectionCoordinator`, `IDisconnectPipeline`, `IProtocolHost` mocks already established in Phase 4 test base

No new framework install — xUnit v3 stack from Phase 1 carries forward.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Multi-host GDI stability | TAB-01, TAB-02 | Requires real RDP server + GDI handle counter (GetGuiResources) — cannot mock AxMsRdpClient9 GDI semantics | Start app. Use Resource Monitor or Process Explorer to record baseline GDI count. Open 14 real RDP connections sequentially (localhost RDP or throwaway VMs). Close all tabs. GDI count should return within ±50 of baseline. Repeat twice. |
| Ctrl+Tab / Ctrl+Shift+Tab with RDP focused | TAB-03 | UAT — verifies research Assumption A1 (PreviewKeyDown fires before AxHost when KeyboardHookMode=0). Cannot unit-test; requires live AxHost + keyboard | Open 3+ real RDP sessions. Click into the RDP viewport so AxHost has focus. Press Ctrl+Tab — active tab must change. Press Ctrl+Shift+Tab — active tab must reverse-cycle. Press Ctrl+W — active tab must close. Press Ctrl+3 — jump to third tab. Press Ctrl+9 — jump to last tab. Press Ctrl+Shift+T after closing — last-closed tab must reopen. |
| Drag-to-reorder visual feedback | D-13 (from CONTEXT.md) | Visual behavior — drop-target insertion line + ghost drag + no conflict with middle-click close | Open 4+ tabs. Hold left-button on a tab title, drag over an adjacent tab. Verify: 2px vertical insertion line appears on the drop-target edge. Release → order in `MainWindowViewModel.Tabs` reflects new position. Cancel mid-drag with ESC → order unchanged. Middle-click during non-drag still closes. |
| Background reconnect UX | D-14 (from CONTEXT.md) | Visual — amber badge on background tab, overlay hidden until switch; no auto-switch | Open 2 real RDP connections. Switch to tab A. Kill the network on tab B's connection (physically unplug or firewall-drop). Tab B's badge should turn amber within ~2 seconds. Tab A stays focused. Switch to tab B — reconnect overlay appears showing attempt counter. Reconnect B — badge clears. |
| 15+ threshold Snackbar | TAB-04 | Visual — one-time toast on crossing 14→15 | Open 14 connections sequentially (real or stubbed via testable ConnectionRequestedEvent). Open the 15th — Snackbar appears with "15 active sessions reached…" text, auto-dismisses in ~6s. Open the 16th — NO snackbar. Close 2 tabs (now 14). Open a new one (now 15 again) — snackbar fires once more. |
| Drag-resize snapshot across multi-host | D-04, Phase 4 D-13 carry-over | Visual — verify the AirspaceSwapper bitmap snapshot still fires cleanly with a persistent multi-host container | Open 3+ RDP sessions. Grab window edge and resize. Active tab's viewport must show a bitmap snapshot during resize (no flicker / black rectangle). Release → live rendering resumes. Switch to another tab while dragging — no crash. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies (planner to populate)
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s for quick filter
- [ ] `nyquist_compliant: true` set in frontmatter
- [ ] Manual UAT list reviewed against Phase 5 success criteria (Roadmap §Phase 5)

**Approval:** pending
