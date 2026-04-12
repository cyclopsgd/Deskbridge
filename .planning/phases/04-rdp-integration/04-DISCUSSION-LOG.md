# Phase 4: RDP Integration - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-12
**Phase:** 04-rdp-integration
**Areas discussed:** Prototype-first split, Reconnection flow & UX, Pipeline & host architecture, Airspace strategy for overlays

---

## Area Selection

| Option | Description | Selected |
|--------|-------------|----------|
| Prototype-first split | STATE.md blocker — does 4-01 exist as a smoke test before 4-02/4-03 build the real host? | ✓ |
| Reconnection flow & UX | Auto vs manual retry, attempt cap, OnAutoReconnecting interaction, overlay rendering | ✓ |
| Pipeline & host architecture | Pipeline stages, stage home (Core vs Rdp), STA affinity, host viewport lifecycle (Phase 4/5 boundary) | ✓ |
| Airspace strategy for overlays | WFH hide vs bitmap snapshot vs NuGet vs differentiated approach | ✓ |

**User's choice:** All four areas selected.

---

## Prototype-first split

### Q1: What should Plan 4-01 deliver?

| Option | Description | Selected |
|--------|-------------|----------|
| Smoke-test prototype only | Bare-minimum RdpHostControl that connects + disposes cleanly, verified via 20-cycle GDI check. No pipeline, no reconnection, no events. | ✓ |
| Full host in one plan | Plan 4-01 = RdpHostControl wired into pipelines end-to-end. Faster overall but pipeline + RDP bugs tangle during debugging. | |
| Hybrid — prototype checkpoint inside 4-01 | Real RdpHostControl with a mid-plan manual smoke test before wiring into the pipeline. One plan, embedded gate. | |

**User's choice:** Smoke-test prototype only.

### Q2: What does the smoke-test prototype have to prove before we move on?

| Option | Description | Selected |
|--------|-------------|----------|
| GDI handle count returns to baseline after 20 connect/disconnect cycles | #1 mRemoteNG pain point. Uses GetGuiResources, fails fast if disposal sequence is wrong. | ✓ |
| Password cast via IMsTscNonScriptable works against a real RDP server | Proves GetOcx() + cast + ClearTextPassword end-to-end. Requires test RDP target (localhost or VM). | ✓ |
| Siting-before-configure order enforced by a helper that throws if violated | Guard wrapper that makes "set property before siting" a hard failure. | ✓ |
| COM exceptions inside ActiveX callbacks don't tear down the app | Provoke a COM error (bad hostname, auth failure), prove app survives and publishes ConnectionFailedEvent cleanly. | ✓ |

**User's choice:** All four gate criteria must pass.

---

## Reconnection flow & UX

### Q1: When the session drops, who drives the reconnect?

| Option | Description | Selected |
|--------|-------------|----------|
| Automatic with visible attempt counter | Auto-start backoff (2s → 4s → 8s → 30s cap), overlay shows "Reconnecting... attempt N" with Cancel. | ✓ |
| Manual — overlay waits for user click | "Connection lost — Reconnect / Close" overlay, no auto-retry. Click Reconnect triggers one attempt. | |
| Hybrid: auto-retry a few times, then prompt | Auto-attempt 3 times, then switch to manual overlay. | |

**User's choice:** Automatic with visible attempt counter.

### Q2: How does the reconnect backoff interact with the RDP control's OnAutoReconnecting?

| Option | Description | Selected |
|--------|-------------|----------|
| Disable control's auto-reconnect, drive retries ourselves | `EnableAutoReconnect = false`, dispose + recreate host per attempt with our own backoff timer. Clean state, testable, full logs. | ✓ |
| Trust the control's built-in auto-reconnect | Subscribe to OnAutoReconnecting2, surface attempts in overlay. Less code but we trust mstscax's history. | |

**User's choice:** Disable control's auto-reconnect, drive retries ourselves.

### Q3: When should the reconnect give up?

| Option | Description | Selected |
|--------|-------------|----------|
| Hard cap at 20 attempts, then manual overlay | ~10 minutes at 30s cap. Switches to manual "Reconnect / Close" so user decides. Matches pitfalls doc example. | ✓ |
| Unlimited retries until user cancels | Never give up automatically. Fine for always-on networks, risky for flaky links. | |
| Classify disconnect reason, skip retry on auth/licensing | Parse discReason categories, skip retry for 2055/2567/3335/3847 etc. | |

**User's choice:** Hard cap at 20 attempts. (Follow-up confirmed we also layer in the classification skip — see Q5.)

### Q4: Where does the reconnect overlay render?

| Option | Description | Selected |
|--------|-------------|----------|
| WPF overlay on top, WFH hidden during display | Collapse WindowsFormsHost, show WPF overlay. Session is dead anyway, no snapshot needed. | ✓ |
| WPF overlay with bitmap snapshot behind it | PrintWindow capture of last frame behind the overlay. Smoother visual but session is already gone. | |
| WinForms overlay panel layered inside the WFH | Avoid airspace, but weird DPI/theming and more code. | |

**User's choice:** WPF overlay on top, WFH hidden during display.

### Q5: On top of the 20-attempt cap, should we skip auto-retry for reasons where retrying is pointless?

| Option | Description | Selected |
|--------|-------------|----------|
| Yes — skip auto-retry for auth/licensing failures | Disconnect reasons 2055/2567/2823/3335/3591/3847 (auth) and 2056/2312 (licensing) skip backoff, go straight to manual overlay. | ✓ |
| No — always retry up to the cap regardless of reason | Simpler logic, user can always cancel. Noisier logs. | |

**User's choice:** Yes — skip auto-retry for auth/licensing failures.

---

## Pipeline & host architecture

### Q1: Which connect-pipeline stages should Phase 4 add?

| Option | Description | Selected |
|--------|-------------|----------|
| ResolveCredentialsStage | Walks inheritance chain, sets ConnectionContext.ResolvedPassword. Deferred from Phase 3. | ✓ |
| CreateHostStage | Resolves IProtocolHost factory by Protocol enum, instantiates RdpHostControl. | ✓ |
| ConnectStage | Calls host.ConnectAsync; awaits OnLoginComplete or OnDisconnected with timeout; publishes events. | ✓ |
| UpdateRecents + Audit stages | Touch LastUsedAt and append to audit trail. Audit is Phase 6 — these could be no-op stubs now. | (user asked for recommendation) |

**User's choice:** Three core stages selected. Asked for recommendation on UpdateRecents + Audit vs deferring to Phase 6 with a ui-phase there.

**Notes:** Claude recommended: add UpdateRecentsStage now (tiny, no UI, populates LastUsedAt for Phase 6 command palette), defer AuditStage to Phase 6 where the real audit sink is built. Skip `/gsd-ui-phase 4` — one reconnection overlay card is too small for a systematic UI pass; Phase 6's ui-phase covers all overlays together.

### Q2: Where do RDP-specific pipeline stages live?

| Option | Description | Selected |
|--------|-------------|----------|
| Protocol-agnostic stages in Core, RDP knows nothing about the pipeline | All stages in Deskbridge.Core.Pipeline.Stages, operate on IProtocolHost. RDP project has host + factory only. Cleanest for future SSH/VNC. | ✓ |
| RDP-specific stages in Deskbridge.Protocols.Rdp | Stages live next to RdpHostControl. Tighter coupling, more code per protocol. | |

**User's choice:** Protocol-agnostic stages in Core.

### Q3: How do we guarantee STA thread affinity across pipeline stage awaits?

| Option | Description | Selected |
|--------|-------------|----------|
| Pipeline runs on UI dispatcher, stages use plain await | Invoke from UI thread, no ConfigureAwait(false), continuations return to STA. Guard assertion in RdpHostControl. | ✓ |
| Stages explicitly marshal COM calls via Dispatcher.InvokeAsync | Defensive but verbose, deadlock risk if misused. | |

**User's choice:** Pipeline runs on UI dispatcher, stages use plain await.

### Q4: Where does RdpHostControl live in the visual tree across Phase 4 and Phase 5?

| Option | Description | Selected |
|--------|-------------|----------|
| Phase 4 single live host in viewport; Phase 5 converts to parked collection | Phase 4 swaps one host into Viewport.Content. Phase 5 adds persistent container with Visibility toggling. | ✓ |
| Phase 4 builds the permanent-host container from day one | Add RdpHostCollection panel now. More upfront but no re-parenting risk later. | |

**User's choice:** Phase 4 single live host in viewport; Phase 5 converts to parked collection.

---

## Airspace strategy for overlays

### Q1: How should Phase 4 handle airspace when a WPF overlay needs to appear over the RDP viewport?

| Option | Description | Selected |
|--------|-------------|----------|
| Hide WFH + manual PrintWindow snapshot, rolled ourselves | In-tree AirspaceSwapper helper. Capture PrintWindow bitmap, show WPF Image, collapse WFH. Handles drag/resize, reconnect, future ContentDialog with one pattern. | ✓ |
| AirspaceFixer NuGet package | AirspacePanel + FixAirspace binding. Less code but third-party dependency risk. | |
| Hide WFH only, no snapshot (black area briefly) | Simplest. Works for reconnect, ugly for drag/resize. | |
| Differentiated: black for reconnect, snapshot for drag/resize | Two code paths optimized per case. Most complex. | |

**User's choice:** Hide WFH + manual PrintWindow snapshot, rolled ourselves.

### Q2: When should the PrintWindow snapshot helper land?

| Option | Description | Selected |
|--------|-------------|----------|
| Plan 4-02 alongside RdpHostControl | Build AirspaceSwapper + ResizeSnapshotManager with the host. Prototype 4-01 doesn't need it. | ✓ |
| Plan 4-03 as the last piece | Build host + reconnect in 4-02 using plain hide-WFH, add polish in 4-03. Flicker visible until 4-03. | |

**User's choice:** Plan 4-02 alongside RdpHostControl.

---

## Claude's Discretion

- Connect trigger wiring (tree double-click → pipeline via event bus or direct call)
- ConnectionModel.DisplaySettings → AxMsRdpClient9 property mapping
- Default RDP properties when DisplaySettings fields unset
- Reconnect timer implementation (DispatcherTimer per pitfalls)
- Testing strategy (mocked IProtocolHost for pipeline tests; live RDP only in prototype plan 4-01)
- Overlay visual styling (reuse Phase 2 WPF-UI card/button tokens)
- Disconnect reason code classification helper (shared enum/extension)

## Deferred Ideas

- AuditStage — Phase 6
- `/gsd-ui-phase 4` — skipped, Phase 6 covers all overlays
- ContainerHandledFullScreen / F11 — Phase 6 (CMD-04)
- Multi-host container + tab Visibility toggle — Phase 5 (TAB-01, TAB-02, TAB-05)
- 15+ GDI limit warning — Phase 5 (TAB-04)
- Process-level COM crash isolation — out of scope for v1
- Input activity tracking during RDP session — Phase 6 (SEC-03), not needed in Phase 4
