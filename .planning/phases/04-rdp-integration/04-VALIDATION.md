---
phase: 4
slug: rdp-integration
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-12
---

# Phase 4 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 + FluentAssertions + NSubstitute |
| **Config file** | `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` (no xunit.runner.json needed) |
| **Quick run command** | `dotnet test --filter "FullyQualifiedName!~Smoke"` |
| **Full suite command** | `dotnet test` (set `DESKBRIDGE_SMOKE_RDP_HOST` env var to include Plan 4-01 live-RDP gate tests; otherwise they SkipIf) |
| **Estimated runtime** | ~5 seconds quick / ~60 seconds full with gate tests |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --filter "FullyQualifiedName!~Smoke"` (unit + integration, excludes live-RDP gate)
- **After every plan wave:** Run `dotnet test` with `DESKBRIDGE_SMOKE_RDP_HOST` set (includes the 4 gate tests)
- **Before `/gsd-verify-work`:** Full suite green + manual visual checkpoints for RDP-06 overlay and RDP-09 drag smoothness; GDI delta < 50 across 20-cycle gate
- **Max feedback latency:** ~5 seconds quick / ~60 seconds full

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 4-01-G1 | 01 | 1 | RDP-04 | T-04-GDI | No GDI leak over 20 cycles | smoke (live) | `dotnet test --filter "FullyQualifiedName~Gate1_20CycleGdiBaseline"` | ❌ W0 | ⬜ pending |
| 4-01-G2 | 01 | 1 | RDP-03 | T-04-CRED | IMsTscNonScriptable cast + ClearTextPassword works | smoke (live) | `dotnet test --filter "FullyQualifiedName~Gate2_IMsTscNonScriptable"` | ❌ W0 | ⬜ pending |
| 4-01-G3 | 01 | 1 | RDP-02 | — | Siting order guard throws if violated | smoke (live) | `dotnet test --filter "FullyQualifiedName~Gate3_SitingOrderGuard"` | ❌ W0 | ⬜ pending |
| 4-01-G4 | 01 | 1 | RDP-07 | T-04-ISO | COM error in session does not tear down app | smoke (live) | `dotnet test --filter "FullyQualifiedName~Gate4_ErrorIsolation"` | ❌ W0 | ⬜ pending |
| 4-02-01 | 02 | 1 | RDP-01 | — | RdpHostControl implements IProtocolHost correctly | unit | `dotnet test --filter "FullyQualifiedName~RdpHostControlShapeTests"` | ❌ W0 | ⬜ pending |
| 4-02-02 | 02 | 1 | RDP-02 | — | SiteAndConfigure helper throws if Handle == 0 | unit | `dotnet test --filter "FullyQualifiedName~SitingGuardTests"` | ❌ W0 | ⬜ pending |
| 4-02-03 | 02 | 1 | RDP-04 | T-04-ISO | DisposeStage runs full sequence without exception | unit (mocked) | `dotnet test --filter "FullyQualifiedName~DisposeStageTests"` | ❌ W0 | ⬜ pending |
| 4-02-04 | 02 | 2 | RDP-05 | — | Pipeline executes stages in Resolve→Create→Connect→Recents order | unit | `dotnet test --filter "FullyQualifiedName~ConnectionPipelineIntegrationTests"` | ❌ W0 | ⬜ pending |
| 4-02-05 | 02 | 2 | RDP-05 | — | Coordinator is the only connect path (code review) | manual | Code review checklist entry | n/a | ⬜ pending |
| 4-02-06 | 02 | 2 | RDP-08 | — | Each pipeline stage publishes correct event on IEventBus | unit (mocked) | `dotnet test --filter "FullyQualifiedName~ConnectStageTests"` | ❌ W0 | ⬜ pending |
| 4-02-07 | 02 | 3 | RDP-09 | — | AirspaceSwapper WM_ENTERSIZEMOVE captures bitmap, hides WFH | integration (STA) | `dotnet test --filter "FullyQualifiedName~AirspaceSwapperTests"` | ❌ W0 | ⬜ pending |
| 4-02-08 | 02 | 3 | RDP-09 | — | No black flicker during drag/resize | manual (visual) | Manual test: drag window edge rapidly during live session | n/a | ⬜ pending |
| 4-03-01 | 03 | 1 | RDP-06 | — | Reconnect coordinator backoff schedule is 2, 4, 8, 30, 30, ... across 20 attempts | unit | `dotnet test --filter "FullyQualifiedName~RdpReconnectCoordinatorTests"` | ❌ W0 | ⬜ pending |
| 4-03-02 | 03 | 1 | RDP-06 | — | DisconnectReasonClassifier maps all documented codes to correct category | unit | `dotnet test --filter "FullyQualifiedName~DisconnectReasonClassifierTests"` | ❌ W0 | ⬜ pending |
| 4-03-03 | 03 | 1 | RDP-06 | — | Auth/licensing codes skip auto-retry and go straight to manual overlay | unit | `dotnet test --filter "FullyQualifiedName~RdpReconnectCoordinatorTests.SkipsAuthFailures"` | ❌ W0 | ⬜ pending |
| 4-03-04 | 03 | 2 | RDP-06 | — | Reconnect overlay shows attempt counter and Cancel button (visual checkpoint) | manual | Screenshot compare against DESIGN.md tokens | n/a | ⬜ pending |
| 4-03-05 | 03 | 2 | RDP-07 | T-04-ISO | ErrorOccurred event fires for COM exception, process stays alive | unit (mocked) | `dotnet test --filter "FullyQualifiedName~ErrorIsolationTests"` | ❌ W0 | ⬜ pending |
| 4-SEC-01 | any | — | — | T-04-LOG | ResolvedPassword never reaches Serilog output | unit | `dotnet test --filter "FullyQualifiedName~PasswordLeakTests.NotInLogs"` | ❌ W0 | ⬜ pending |
| 4-SEC-02 | any | — | — | T-04-JSN | ConnectionModel JSON serialization contains no password-like field | unit | `dotnet test --filter "FullyQualifiedName~PasswordLeakTests.NotInJson"` | ❌ W0 | ⬜ pending |
| 4-SEC-03 | any | — | — | T-04-EXC | COM exceptions logged as GetType().Name + HResult only, never ex.Message | unit | `dotnet test --filter "FullyQualifiedName~ErrorSanitizationTests"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

**Threat Refs:**
- T-04-GDI: GDI handle exhaustion via leaked disposal
- T-04-CRED: Credential leak via Serilog structured logging of `ConnectionContext`
- T-04-ISO: COM exception in one session tears down entire app
- T-04-LOG: ResolvedPassword leaked through `Log.*("{@Context}", ctx)` pattern
- T-04-JSN: Password-like field serialized via `System.Text.Json` reaching disk
- T-04-EXC: COM exception message containing plaintext password logged via `ex.ToString()` or `ex.Message`

---

## Wave 0 Requirements

- [ ] `tests/Deskbridge.Tests/Smoke/RdpHostControlSmokeTests.cs` — Plan 4-01 gate tests (4 SkippableFact cases for live-RDP host)
- [ ] `tests/Deskbridge.Tests/Fixtures/StaCollectionFixture.cs` — xUnit v3 collection fixture setting apartment state STA for smoke and AirspaceSwapper tests
- [ ] `tests/Deskbridge.Tests/Pipeline/ResolveCredentialsStageTests.cs`
- [ ] `tests/Deskbridge.Tests/Pipeline/CreateHostStageTests.cs`
- [ ] `tests/Deskbridge.Tests/Pipeline/ConnectStageTests.cs`
- [ ] `tests/Deskbridge.Tests/Pipeline/UpdateRecentsStageTests.cs`
- [ ] `tests/Deskbridge.Tests/Pipeline/DisconnectStageTests.cs`
- [ ] `tests/Deskbridge.Tests/Pipeline/DisposeStageTests.cs`
- [ ] `tests/Deskbridge.Tests/Pipeline/PublishClosedEventStageTests.cs`
- [ ] `tests/Deskbridge.Tests/Pipeline/ConnectionPipelineIntegrationTests.cs`
- [ ] `tests/Deskbridge.Tests/Rdp/DisconnectReasonClassifierTests.cs` — table-driven tests for all 19 documented codes
- [ ] `tests/Deskbridge.Tests/Rdp/RdpReconnectCoordinatorTests.cs` — backoff schedule + cap + cancel + classification-skip
- [ ] `tests/Deskbridge.Tests/Rdp/AirspaceSwapperTests.cs` — WndProc hook + hide/show + snapshot capture (requires STA fixture)
- [ ] `tests/Deskbridge.Tests/Rdp/RdpHostControlShapeTests.cs` — interface conformance, property shape
- [ ] `tests/Deskbridge.Tests/Rdp/SitingGuardTests.cs` — SiteAndConfigure throws if unsited
- [ ] `tests/Deskbridge.Tests/Rdp/ErrorIsolationTests.cs` — COM exception does not leak out
- [ ] `tests/Deskbridge.Tests/Security/PasswordLeakTests.cs` — Serilog + JSON regression tests
- [ ] `tests/Deskbridge.Tests/Security/ErrorSanitizationTests.cs` — COM exception message sanitization
- [ ] `tests/Deskbridge.Tests/Coordinator/ConnectionCoordinatorTests.cs` — event-bus subscription, dispatcher marshal, active-host replacement policy
- [ ] Add `Xunit.SkippableFact` package (or implement inline via `Assert.Skip` — planner to pick) — xUnit v3 doesn't ship SkippableFact out of the box

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Reconnect overlay visual design matches DESIGN.md tokens | RDP-06 | Visual quality, tokens rendered correctly | During Plan 4-03 visual checkpoint: disconnect a live session, observe overlay, screenshot and compare accent colour (#007ACC), card corner radius, button styles against DESIGN.md §6 tokens |
| No black flicker during window drag/resize | RDP-09 | Visual smoothness is subjective | During Plan 4-02 visual checkpoint: connect a live session, rapidly drag window edges, verify the bitmap snapshot swap is smooth and no black rectangles appear |
| Coordinator is the only connect path (no direct IConnectionPipeline call from UI) | RDP-05 | Architectural invariant | Code-review checklist: grep for `IConnectionPipeline` usage in `src/Deskbridge/` (should only appear in `ConnectionCoordinator` + DI registration) |
| Plan 4-01 prototype gate: GDI delta < 50 over 20 cycles | RDP-04 | Requires live RDP host | Set `DESKBRIDGE_SMOKE_RDP_HOST=127.0.0.1` (or a VM), enable Remote Desktop on that host, run `dotnet test --filter "Gate1"` — assert delta |
| Plan 4-01 prototype gate: IMsTscNonScriptable cast works against real RDP | RDP-03 | Requires live RDP host | Set env var, run `dotnet test --filter "Gate2"` with a valid username+password, assert OnLoginComplete fires |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references (19 new test files)
- [ ] No watch-mode flags (`--watch` forbidden; planner must use single-run `dotnet test`)
- [ ] Feedback latency < 60s full suite, < 5s quick
- [ ] `nyquist_compliant: true` set in frontmatter after plan-checker approval

**Approval:** pending
