# Phase 4: RDP Integration - Context

**Gathered:** 2026-04-12
**Status:** Ready for planning

<domain>
## Phase Boundary

Build `RdpHostControl` (AxMsRdpClient9NotSafeForScripting wrapped in WindowsFormsHost, implementing IProtocolHost) with strict siting/disposal per RDP-ACTIVEX-PITFALLS.md, drive connect/disconnect exclusively through the Phase 1 pipelines (adding the RDP-specific stages plus the credential-resolution stage that was deferred from Phase 3), add automatic reconnection with exponential backoff and an attempt-counter overlay, isolate per-connection COM errors, and solve the WPF/WinForms airspace problem for the reconnection overlay and the WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE drag-resize snapshot. Phase 4 delivers a single live RDP session rendered in the viewport; multi-tab state management is explicitly Phase 5.

</domain>

<decisions>
## Implementation Decisions

### Plan Split & Prototype Gate
- **D-01:** Plan 4-01 = smoke-test prototype only. A bare-minimum RdpHostControl that connects and disposes cleanly. No pipeline integration, no reconnection, no event bus. Its sole job is to prove the dangerous primitives work before 4-02 builds on them. Aligns with the STATE.md blocker ("minimal connect/dispose prototype before full implementation").
- **D-02:** The prototype must pass all four gate checks before 4-02 begins:
  1. GDI handle count returns to baseline after 20 connect/disconnect cycles (probed via GetGuiResources)
  2. IMsTscNonScriptable cast + ClearTextPassword succeeds against a real RDP target (localhost RDP enabled or throwaway VM)
  3. Siting-before-configure order is enforced by a helper that throws if a property is set before the control is added to its container (no silent AxHost.InvalidActiveXStateException)
  4. Intentional COM error (bad hostname, auth failure) does not tear down the app and is surfaced cleanly (ConnectionFailedEvent in the real plan; console trace in the prototype)

### Reconnection Flow
- **D-03:** Automatic retry with visible attempt counter. On disconnect, immediately start backoff (2s → 4s → 8s → capped 30s) and retry until success, user cancels, or cap hit. Overlay shows `Reconnecting… attempt N` with a Cancel button.
- **D-04:** Disable the RDP control's built-in auto-reconnect (`EnableAutoReconnect = false` or stop via `OnAutoReconnecting2.pArcContinueStatus`). We dispose + recreate the host per attempt with our own DispatcherTimer-driven backoff. Clean state every retry, full logging visibility, sidesteps mstscax's documented auto-reconnect leak history.
- **D-05:** Hard cap at 20 attempts (~10 minutes at 30s cap). After the cap, swap overlay to manual `Connection lost — Reconnect / Close`.
- **D-06:** Skip auto-retry entirely for disconnect reasons where retry is pointless: auth failures (2055, 2567, 2823, 3335, 3591, 3847) and licensing (2056, 2312). Classify the `discReason` code, and if it falls in these ranges, go straight to the manual overlay. Network-lost codes (264, 516, 772, 1028, 2308) and server-initiated (3) trigger the normal auto-retry loop.
- **D-07:** Overlay renders as a WPF panel on top of a hidden WindowsFormsHost. Session is already gone during a disconnect, so a bitmap snapshot behind the overlay is unnecessary — the WFH is simply collapsed and the overlay becomes the visible content. The same AirspaceSwapper helper (D-12) manages the WFH Visibility toggle.

### Pipeline Stages
- **D-08:** Phase 4 adds four connect-pipeline stages: `ResolveCredentialsStage` (walks inheritance chain per Phase 3 rules, sets `ConnectionContext.ResolvedPassword`), `CreateHostStage` (resolves `IProtocolHost` factory by `Protocol` enum and instantiates `RdpHostControl`), `ConnectStage` (invokes `host.ConnectAsync(context)`, awaits `OnLoginComplete` or `OnDisconnected` with timeout, publishes `ConnectionEstablishedEvent` or `ConnectionFailedEvent`), and `UpdateRecentsStage` (sets `ConnectionModel.LastUsedAt` and persists via `IConnectionStore`). The audit pipeline stage is deferred to Phase 6 where the audit log sink is built — adding a stub here would be premature.
- **D-09:** Disconnect pipeline in Phase 4: `DisconnectStage` (invokes `host.DisconnectAsync()`, waits for `OnDisconnected` with 30s timeout per pitfalls doc §3), `DisposeStage` (runs the strict disposal sequence: disconnect → unsubscribe events → rdp.Dispose → host.Child = null → host.Dispose → remove from visual tree), `PublishClosedEventStage` (raises `ConnectionClosedEvent`).
- **D-10:** Protocol-agnostic stages live in `Deskbridge.Core/Pipeline/Stages/`. They operate on the `IProtocolHost` abstraction and know nothing about AxMSTSCLib. `Deskbridge.Protocols.Rdp` contains only `RdpHostControl`, the airspace helper, and the RDP factory registration. Keeps v2 SSH/VNC paths clean.
- **D-11:** STA thread affinity is enforced by running `ConnectionPipeline.ConnectAsync` from the UI dispatcher and using plain `await` (no `ConfigureAwait(false)`) inside stages, so continuations return to the STA thread. `RdpHostControl` asserts apartment state at entry points as a defensive guard.
- **D-12:** Phase 4 = single live host in viewport. Phase 4 swaps a single `RdpHostControl` directly into `MainWindow.Viewport.Content` (or the equivalent viewport container from Phase 2) on connect, and removes it on disconnect. Phase 5 replaces this with a persistent container holding all hosts permanently, toggling Visibility per active tab ("never re-parent" pattern from the pitfalls doc). Phase 4 does not prematurely build the multi-host collection.

### Airspace Strategy
- **D-13:** Manual PrintWindow-based `AirspaceSwapper` helper, implemented in-tree (no NuGet dependency on AirspaceFixer). Captures a bitmap via `PrintWindow(hwnd, hdcBlt, PW_CLIENTONLY)`, shows it as a WPF `Image`, collapses the WindowsFormsHost. On close, restores WFH Visibility and hides the Image. Handles drag/resize (RDP-09) via `WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` hooks. Same helper instance serves the reconnect overlay (hide-only mode, no snapshot needed) and will serve future ContentDialog-over-RDP in Phase 6. Full code exists in `WINFORMS-HOST-AIRSPACE.md` §`ResizeSnapshotManager`.
- **D-14:** The AirspaceSwapper and the ResizeSnapshotManager both land in Plan 4-02 alongside `RdpHostControl`. The prototype in 4-01 does not need them (it only proves connect/dispose primitives; no overlay involved). 4-03 wires them into the reconnection flow and final polish.

### Claude's Discretion
- Connect trigger wiring — tree `OnItemDoubleClick` likely raises `ConnectionRequestedEvent` on the event bus; a `ConnectionCoordinator` singleton subscribes and calls `IConnectionPipeline.ConnectAsync`. Alternative: direct pipeline call from the tree ViewModel. Claude picks what composes cleanest.
- `ConnectionModel.DisplaySettings` → `AxMsRdpClient9` property mapping (resolution, color depth, SmartSizing, CachePersistenceActive, KeyboardHookMode, EnableCredSspSupport). Follow REFERENCE.md §RDP ActiveX Reference as the default template; apply per-connection overrides where `DisplaySettings` fields are set.
- Default RDP properties for unset fields: `SmartSizing = true`, `EnableCredSspSupport = true`, `CachePersistenceActive = 0` (per pitfalls doc — reduces GDI at scale), `KeyboardHookMode = 0` (keep app shortcuts local, per pitfalls §5), `BitmapPeristence = 0`, `RDPPort = connection.Port`.
- Reconnect timer implementation — `DispatcherTimer` (STA-safe per pitfalls §6) not `System.Threading.Timer`.
- Testing strategy — mock `IProtocolHost` with NSubstitute for pipeline stage tests; the prototype plan (4-01) is the only place live RDP gets exercised.
- Overlay visual styling — reuse Phase 2 WPF-UI card/button tokens; no new visual design needed for Phase 4.
- Disconnect reason code classification helper — utility method on an enum/extension so stages and overlay can share categorization logic.

### Folded Todos
None — no matching pending todos for Phase 4.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### RDP ActiveX — mandatory reading
- `RDP-ACTIVEX-PITFALLS.md` — full document, all 8 sections. Required before writing any line of RDP interop code. Covers siting state machine, GDI leak thresholds, .NET 8.0.11 AxHost regression fix, disposal sequence, IMsTscNonScriptable cast rules, focus/keyboard hooks, STA thread affinity, 50+ disconnect reason codes, multi-instance failure modes.
- `RDP-ACTIVEX-PITFALLS.md` §1 — siting order state machine, XAML-vs-code-behind, re-siting gotchas
- `RDP-ACTIVEX-PITFALLS.md` §2 — GDI handle leaks, mRemoteNG 14-connection crash pattern, `CachePersistenceActive = 0` mitigation
- `RDP-ACTIVEX-PITFALLS.md` §3 — complete safe disposal pattern (the code we copy into `DisposeStage`)
- `RDP-ACTIVEX-PITFALLS.md` §4 — IMsTscNonScriptable cast, password-set preconditions
- `RDP-ACTIVEX-PITFALLS.md` §6 — STA thread affinity, DispatcherTimer requirement, deadlock pattern to avoid
- `RDP-ACTIVEX-PITFALLS.md` §7 — connection event firing order, disconnect reason code categories (used by D-06 classification)
- `RDP-ACTIVEX-PITFALLS.md` §8 — multi-instance failure thresholds (informs Phase 5 GDI warning at 15+)

### Airspace — mandatory reading
- `WINFORMS-HOST-AIRSPACE.md` — full document. Required before writing airspace helper or overlay logic. Covers why airspace is architectural, bitmap-swap pattern, PerMonitorV2 DPI caveats, PrintWindow capture code, WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE hook code.
- `WINFORMS-HOST-AIRSPACE.md` §`ResizeSnapshotManager` — reference implementation copied into `AirspaceSwapper`
- `WINFORMS-HOST-AIRSPACE.md` §PerMonitorV2 DPI — informs why we prefer SmartSizing over manual DPI scaling

### Architecture & Spec
- `REFERENCE.md` §Feature Specification §RDP Integration — wrapper definition, siting order, password via IMsTscNonScriptable, reconnection overlay spec, event bus contract
- `REFERENCE.md` §RDP ActiveX Reference — canonical siting + disposal code samples
- `REFERENCE.md` §UI Design §Drag/Resize Smoothness — WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE pattern (RDP-09 implementation reference)
- `REFERENCE.md` §Architecture — IProtocolHost, IConnectionPipeline, IDisconnectPipeline contracts

### WPF-UI Patterns
- `WPF-UI-PITFALLS.md` §1 — ContentDialog host registration (relevant for future Phase 6 ContentDialog-over-RDP but flagged now so the AirspaceSwapper is designed to support it)
- `WPF-UI-PITFALLS.md` §5 — Color vs Brush resource keys (applies to reconnect overlay styling)

### Existing Code
- `src/Deskbridge.Core/Interfaces/IProtocolHost.cs` — implement this with RdpHostControl
- `src/Deskbridge.Core/Interfaces/IConnectionPipeline.cs` + `IDisconnectPipeline.cs` — stage contracts to add stages against
- `src/Deskbridge.Core/Pipeline/ConnectionContext.cs` — where ResolvedPassword + Host are carried (ResolvedPassword already exists from Phase 1, do not log)
- `src/Deskbridge.Core/Pipeline/ConnectionPipeline.cs` + `DisconnectPipeline.cs` — pipeline runners (stages are ordered by `Order` property)
- `src/Deskbridge.Core/Events/ConnectionEvents.cs` — ConnectionEstablishedEvent, ConnectionFailedEvent, ConnectionClosedEvent, ReconnectingEvent records already defined, publish to these
- `src/Deskbridge.Core/Interfaces/ICredentialService.cs` — Phase 3 credential service, ResolveCredentialsStage calls into this
- `src/Deskbridge.Core/Interfaces/IConnectionStore.cs` — UpdateRecentsStage persists LastUsedAt via this
- `src/Deskbridge.Protocols.Rdp/Deskbridge.Protocols.Rdp.csproj` — UseWindowsForms = true, references AxMSTSCLib + MSTSCLib from `Interop/`
- `src/Deskbridge.Protocols.Rdp/Interop/AxMSTSCLib.dll` + `MSTSCLib.dll` — the classic COM interop assemblies (do not regenerate)
- `src/Deskbridge/MainWindow.xaml` — viewport region (airspace-safe) from Phase 2, where the RdpHostControl mounts

### Prior Phase Context
- `.planning/phases/03-connection-management/03-CONTEXT.md` — credential service, inheritance rules (D-15 defers ResolveCredentialsStage to Phase 4)
- `.planning/phases/02-application-shell/02-CONTEXT.md` — viewport region spec, airspace-safe layout
- `.planning/phases/01-foundation/01-CONTEXT.md` — pipeline interfaces, disconnect-pipeline decision (D-03)
- `.planning/research/ARCHITECTURE.md` — thread affinity concerns for COM pipeline stages
- `.planning/research/PITFALLS.md` — CommunityToolkit.Mvvm .NET 10 version pin

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `IProtocolHost` interface — exact signature `ConnectAsync(ConnectionContext)`, `DisconnectAsync()`, `IsConnected`, `ConnectionId`, `ErrorOccurred` event. RdpHostControl implements this.
- `ConnectionContext.ResolvedPassword` — property already carries the password between ResolveCredentialsStage and RdpHostControl. Marked "Do not log or serialize."
- `ConnectionContext.Host` — the pipeline slot where CreateHostStage stores the IProtocolHost for subsequent stages.
- `IConnectionPipeline` / `IDisconnectPipeline` — both are stage-ordered (`AddStage` + `Order` property), already implemented with sequential await execution.
- Events: `ConnectionEstablishedEvent`, `ConnectionFailedEvent`, `ConnectionClosedEvent`, `ReconnectingEvent` records exist with the shapes stages need.
- `ICredentialService` from Phase 3 — inheritance walk already implemented; ResolveCredentialsStage just calls into it.
- `IConnectionStore` — UpdateRecentsStage calls `UpdateAsync` to persist `LastUsedAt`.
- `Deskbridge.Protocols.Rdp.csproj` — UseWindowsForms already set; interop DLLs positioned. No project-level changes needed.
- Phase 2 viewport region in MainWindow.xaml — airspace-safe grid cell ready to host the WindowsFormsHost.

### Established Patterns
- DI composition in `App.xaml.cs` — register `RdpHostControl` factory, register the four new connect stages and three disconnect stages
- Stages use `Order` property for pipeline sequencing: Resolve (100), CreateHost (200), Connect (300), UpdateRecents (400)
- Event bus publication via `IEventBus.Send<TEvent>()` — pipeline stages publish, UI layers subscribe
- xUnit v3 + FluentAssertions + NSubstitute test stack (Phase 1 established) — pipeline stage tests mock `IProtocolHost`, `ICredentialService`, `IConnectionStore`
- Serilog error logging pattern (Phase 3 established) — RdpHostControl logs COM exceptions without credentials
- WPF-UI DynamicResource tokens — reconnect overlay uses Phase 2 tokens

### Integration Points
- `MainWindow.xaml` viewport region — single RdpHostControl swaps in/out during Phase 4 (D-12)
- Tree double-click in `ConnectionTreeViewModel` — triggers connect flow (wiring is Claude's Discretion; likely via `ConnectionRequestedEvent` on event bus)
- `App.xaml.cs` DI composition — register new stages + protocol host factory + AirspaceSwapper singleton
- `ConnectionQueryService` — no changes needed; Phase 4 does not touch query surface
- `IEventBus` subscribers — new consumers not needed in Phase 4; Phase 5/6 subscribe to ConnectionEstablishedEvent for tab-open and notification toast respectively
- STATE.md blocker — clear at end of Phase 4 if all four smoke gate criteria pass and no GDI leaks observed across 20 full sessions

</code_context>

<specifics>
## Specific Ideas

- The 20-cycle GDI baseline check is the single most important acceptance test for this phase. It is the leak pattern that destroyed mRemoteNG (issue #1715, 14-connection crash). If the prototype passes this gate cleanly, the rest of the phase is mostly plumbing.
- We deliberately write our own reconnect loop instead of trusting `EnableAutoReconnect` because the pitfalls doc documents multiple mstscax versions with leaks around reconnection, and our own DispatcherTimer-driven loop is trivially testable and logged.
- The AirspaceSwapper is a small helper (~150 lines), copied almost verbatim from `WINFORMS-HOST-AIRSPACE.md` §`ResizeSnapshotManager`. We do not take a NuGet dependency on AirspaceFixer because the code is short, the NuGet's maintenance is uncertain, and we need to own the PrintWindow/WM_ENTERSIZEMOVE hook code anyway.
- `KeyboardHookMode = 0` is the Phase 4 default (keys stay local, app shortcuts work during RDP session). Phase 6 keyboard shortcuts (Ctrl+Tab, Ctrl+W, Ctrl+Shift+P) use `RegisterHotKey` at the Win32 level as the pitfalls doc §5 recommends — Phase 4 just sets the mode correctly.
- `CachePersistenceActive = 0` is set per connection to reduce per-instance GDI usage — direct mitigation for the mRemoteNG 14-connection GDI cliff.
- `SmartSizing = true` by default because our own DPI handling story (per pitfalls §PerMonitorV2) is "let SmartSizing do it" rather than attempting manual scaling which is known-broken in .NET 6+.

</specifics>

<deferred>
## Deferred Ideas

- **AuditStage** — Phase 6 adds this when the audit log sink (LOG-02/LOG-03) is built. Adding a stub now would be throwaway work.
- **`/gsd-ui-phase 4`** — skipped for Phase 4 because the only UI is one reconnection overlay card (trivial, reuses Phase 2 tokens). Phase 6's `/gsd-ui-phase` covers notifications + lock screen + command palette + any overlay refinement in one systematic pass.
- **`ContainerHandledFullScreen` / F11 fullscreen** — listed in CMD-04 (Phase 6). Phase 4 sets RDP for windowed-in-viewport only; fullscreen wiring is Phase 6.
- **Multi-host container and tab-driven Visibility toggle** — Phase 5 (TAB-01, TAB-02, TAB-05). Phase 4 ships single-active-host.
- **15+ GDI-limit warning** — TAB-04, Phase 5. Phase 4 sets `CachePersistenceActive = 0` as groundwork but the user-facing warning is Phase 5.
- **Per-connection COM crash isolation beyond try/catch in RdpHostControl** — RDP-07 is satisfied by wrapping ActiveX calls in try/catch and raising `ErrorOccurred`. True process-level isolation (separate AppDomain or out-of-process host) is out of scope for v1 and not required by the requirement.
- **Input activity tracking during RDP session for auto-lock timer** — Phase 6 (SEC-03). REFERENCE.md notes the timer is NOT reset by RDP input (control consumes it directly), so no Phase 4 work needed.

### Reviewed Todos (not folded)
None.

</deferred>

---

*Phase: 04-rdp-integration*
*Context gathered: 2026-04-12*
