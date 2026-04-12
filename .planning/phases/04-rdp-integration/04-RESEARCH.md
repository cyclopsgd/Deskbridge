# Phase 4: RDP Integration - Research

**Researched:** 2026-04-12
**Domain:** RDP ActiveX (mstscax) hosted in WindowsFormsHost inside WPF-UI FluentWindow on .NET 10, with connection pipeline orchestration
**Confidence:** HIGH on primitives / MEDIUM on .NET 10 leak fix status

## Summary

Phase 4 sits at the peak of the project's risk curve: it brings `AxMsRdpClient9NotSafeForScripting` (a 20+ year old COM/ActiveX control with known silent-failure modes at every layer) under `WindowsFormsHost` inside WPF-UI's `FluentWindow` on .NET 10 — a combination that is documented to have unresolved framework bugs (dotnet/wpf #10171 infinite recursion on close, dotnet/winforms #13499 AxContainer parent leak, dotnet/wpf #6294 cross-monitor DPI). The project's response is correct: split into a smoke-test prototype (Plan 4-01) that proves four dangerous primitives in isolation before Plan 4-02 builds the real implementation.

The research confirms every decision in CONTEXT.md is defensible and grounded in the pitfalls docs. The single contradiction to flag is **D-11 (plain `await` + apartment assertion for STA affinity)** — it is correct for the *pipeline runner's* path when invoked from the UI thread, but stages that wait on COM events (OnLoginComplete, OnDisconnected) need an explicit `TaskCompletionSource` + event-handler pattern that completes on the STA thread. Plain `await` on a `Task.Delay` polling loop also works but is less clean. Both variants are documented below.

The research also surfaces one item CONTEXT.md does not yet address: **the two WindowsFormsHost reflection-based leak fixes** (`HwndSourceKeyboardInputSite._sinkElement` null-out and `WinFormsAdapter` dispose via `HostContainerInternal` reflection). WINFORMS-HOST-AIRSPACE.md treats these as mandatory for any production hosting of WFH on .NET 8+. Plan 4-02 should bake these into `RdpHostControl.Dispose()`.

**Primary recommendation:** Build Plan 4-01 as described; verify the 20-cycle GDI gate on the actual Microsoft.WindowsDesktop.App.WindowsForms 10.0.x build before proceeding, because the `AxContainer` leak fix PR #13532 is known-incomplete per a June 2025 report against .NET 10 preview 6. If the gate fails cleanly, the rest of Phase 4 is plumbing.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Plan Split & Prototype Gate**
- **D-01:** Plan 4-01 = smoke-test prototype only. A bare-minimum RdpHostControl that connects and disposes cleanly. No pipeline integration, no reconnection, no event bus.
- **D-02:** The prototype must pass all four gate checks before 4-02 begins:
  1. GDI handle count returns to baseline after 20 connect/disconnect cycles (probed via GetGuiResources)
  2. IMsTscNonScriptable cast + ClearTextPassword succeeds against a real RDP target (localhost RDP enabled or throwaway VM)
  3. Siting-before-configure order is enforced by a helper that throws if a property is set before the control is added to its container (no silent AxHost.InvalidActiveXStateException)
  4. Intentional COM error (bad hostname, auth failure) does not tear down the app and is surfaced cleanly (ConnectionFailedEvent in the real plan; console trace in the prototype)

**Reconnection Flow**
- **D-03:** Automatic retry with visible attempt counter. Backoff 2s → 4s → 8s → capped 30s.
- **D-04:** Disable the RDP control's built-in auto-reconnect; dispose + recreate the host per attempt.
- **D-05:** Hard cap at 20 attempts (~10 minutes). After cap, swap to manual `Connection lost — Reconnect / Close`.
- **D-06:** Skip auto-retry for auth failures (2055, 2567, 2823, 3335, 3591, 3847) and licensing (2056, 2312).
- **D-07:** Overlay renders as a WPF panel on top of a hidden WindowsFormsHost (no snapshot needed during disconnect).

**Pipeline Stages**
- **D-08:** Four connect-pipeline stages: `ResolveCredentialsStage`, `CreateHostStage`, `ConnectStage`, `UpdateRecentsStage`. AuditStage deferred to Phase 6.
- **D-09:** Disconnect pipeline: `DisconnectStage`, `DisposeStage`, `PublishClosedEventStage`.
- **D-10:** Protocol-agnostic stages live in `Deskbridge.Core/Pipeline/Stages/`. `Deskbridge.Protocols.Rdp` contains only `RdpHostControl`, the airspace helper, and the RDP factory registration.
- **D-11:** STA thread affinity enforced by running `ConnectionPipeline.ConnectAsync` from the UI dispatcher and using plain `await` (no `ConfigureAwait(false)`) inside stages. `RdpHostControl` asserts apartment state at entry points.
- **D-12:** Phase 4 = single live host in viewport. Swaps a single `RdpHostControl` directly into `MainWindow.Viewport.Content` on connect, removes on disconnect. Phase 5 builds the multi-host container.

**Airspace Strategy**
- **D-13:** Manual PrintWindow-based `AirspaceSwapper` helper, in-tree (no NuGet dependency on AirspaceFixer). Handles drag/resize (RDP-09) via `WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` hooks.
- **D-14:** AirspaceSwapper and ResizeSnapshotManager land in Plan 4-02 alongside `RdpHostControl`. Prototype 4-01 does not need them. Plan 4-03 wires them into the reconnection flow.

### Claude's Discretion

- Connect trigger wiring — `ConnectionRequestedEvent` on event bus vs. direct `IConnectionPipeline.ConnectAsync` call from tree ViewModel. Claude picks what composes cleanest.
- `ConnectionModel.DisplaySettings` → `AxMsRdpClient9` property mapping. Follow REFERENCE.md §RDP ActiveX Reference as default template.
- Default RDP properties: `SmartSizing = true`, `EnableCredSspSupport = true`, `CachePersistenceActive = 0`, `KeyboardHookMode = 0`, `BitmapPeristence = 0`, `RDPPort = connection.Port`.
- Reconnect timer implementation — `DispatcherTimer` (STA-safe).
- Testing strategy — mock `IProtocolHost` with NSubstitute for stage tests; prototype 4-01 is the only place live RDP gets exercised.
- Overlay visual styling — reuse Phase 2 WPF-UI card/button tokens.
- Disconnect reason code classification helper — utility method on an enum/extension.

### Deferred Ideas (OUT OF SCOPE)

- **AuditStage** — Phase 6 adds this.
- **`/gsd-ui-phase 4`** — skipped for Phase 4.
- **`ContainerHandledFullScreen` / F11 fullscreen** — Phase 6 (CMD-04).
- **Multi-host container and tab-driven Visibility toggle** — Phase 5.
- **15+ GDI-limit warning** — Phase 5 (TAB-04).
- **Per-connection COM crash isolation beyond try/catch** — process-level isolation out of scope for v1.
- **Input activity tracking for auto-lock timer** — Phase 6 (SEC-03).
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RDP-01 | RdpHostControl wraps AxMsRdpClient9NotSafeForScripting in WindowsFormsHost implementing IProtocolHost | RDP-ACTIVEX-PITFALLS §1 (siting), WINFORMS-HOST-AIRSPACE §leaks — `RdpHostControl` shape documented in Architecture Patterns below |
| RDP-02 | ActiveX control sited (added to container) before any property configuration | RDP-ACTIVEX-PITFALLS §1 (state machine) — Siting-order guard pattern below (`SiteAndConfigure<T>` helper) |
| RDP-03 | Password set via IMsTscNonScriptable cast from GetOcx() | RDP-ACTIVEX-PITFALLS §4 — pattern documented; `ClearTextPassword` is write-only, requires sited control + `Connected == 0` |
| RDP-04 | Strict disposal sequence: disconnect → dispose rdp → null child → dispose host → remove from tree | RDP-ACTIVEX-PITFALLS §3 + WINFORMS-HOST-AIRSPACE §leaks — canonical sequence below plus two reflection-based WFH leak fixes |
| RDP-05 | Connect/disconnect lifecycle managed through IConnectionPipeline, never called directly from UI | D-08, D-09 stage list; D-11 STA affinity via UI-dispatcher invocation; Coordinator service pattern below |
| RDP-06 | Reconnection overlay with exponential backoff (2s, 4s, 8s, max 30s) | D-03 through D-07; reconnect loop pattern + DispatcherTimer (RDP-ACTIVEX-PITFALLS §6); D-06 reason code classification below |
| RDP-07 | COM try/catch around all ActiveX calls for per-connection error isolation | RDP-ACTIVEX-PITFALLS §7 (event firing); `ErrorOccurred` event on IProtocolHost — pattern below wraps every COM call |
| RDP-08 | All lifecycle events published to IEventBus | Existing `ConnectionEstablishedEvent`, `ConnectionFailedEvent`, `ConnectionClosedEvent`, `ReconnectingEvent` records; `ConnectStage` is the publisher |
| RDP-09 | During window drag/resize: bitmap snapshot shown, WindowsFormsHost hidden, resize on drop | D-13; `ResizeSnapshotManager` reference implementation in WINFORMS-HOST-AIRSPACE — copy verbatim into `AirspaceSwapper` |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

These are hard rules from the project root CLAUDE.md. Plans MUST comply.

**Framework & stack (locked):**
- .NET 10 LTS (net10.0-windows) with C# 14 — no .NET 8/9
- WPF-UI 4.2.0 Fluent dark theme; all colours via `DynamicResource` tokens
- CommunityToolkit.Mvvm 8.4.2+ (MVVM source generators)
- Microsoft.Extensions.DependencyInjection 10.0.x
- AdysTech.CredentialManager 3.1.0 (Phase 3 already uses)
- Serilog 4.3.1 (Phase 3 already uses)
- xUnit v3 + FluentAssertions + NSubstitute (test stack, already wired)

**Do NOT use:**
- `[GeneratedComInterface]` / COM source generators — don't support ActiveX
- `Marshal.ReleaseComObject` manually — let `AxHost.Dispose()` handle it (EXCEPT: see Architecture Patterns §Disposal — WINFORMS-HOST-AIRSPACE.md documents `Marshal.FinalReleaseComObject(GetOcx())` as part of the full-cleanup sequence; this is the single documented exception, and it comes *before* `rdp.Dispose()`)
- `SecureString` — deprecated
- `CredentialManagement` NuGet — .NET Framework 3.5 only
- BinaryFormatter — removed in .NET 10
- WPF-UI 3.x — different namespace structure
- XML config, SQLite (JSON + `System.Text.Json` only)

**Architectural invariants:**
- `UseWindowsForms` lives ONLY in `Deskbridge.Protocols.Rdp.csproj` — never in `Directory.Build.props` (avoids WPF/WinForms `Application` type ambiguity)
- Classic `aximp.exe` interop only; `AxMSTSCLib.dll` + `MSTSCLib.dll` in `src/Deskbridge.Protocols.Rdp/Interop/` (already positioned)
- App manifest declares PerMonitorV2 DPI awareness (already done in Phase 1)
- Airspace: no WPF elements may overlap the RDP viewport
- Session limit: ~15–20 simultaneous RDP sessions per process (GDI handles) — Phase 4 ships single-host, Phase 5 enforces the limit
- Credentials: AdysTech.CredentialManager only; no passwords in JSON or logs; master password is PBKDF2 (Phase 6)
- Velopack: custom `Main`, `App.xaml` is `Page`, SemVer2, self-contained, user data in `%AppData%`

**Mandatory reading before touching code (enforced by CLAUDE.md):**
- `RDP-ACTIVEX-PITFALLS.md` — all 8 sections
- `WINFORMS-HOST-AIRSPACE.md` — full document
- `WPF-UI-PITFALLS.md` — relevant sections for overlay styling
- `REFERENCE.md` §RDP Integration + §RDP ActiveX Reference + §Drag/Resize Smoothness

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| AxMSTSCLib | (interop DLL, in-tree) | RDP ActiveX wrapper | [CITED: src/Deskbridge.Protocols.Rdp/Interop/] Only classic aximp interop supports `AxMsRdpClient9NotSafeForScripting`. [CITED: CLAUDE.md "Do NOT Use"] COM source generators do not support ActiveX. |
| MSTSCLib | (interop DLL, in-tree) | RDP COM type library | Paired with AxMSTSCLib; provides `IMsTscNonScriptable`, `IMsRdpClientAdvancedSettings9`, disconnect reason constants. |
| Microsoft.WindowsDesktop.App.WindowsForms | 10.0.x (framework ref) | `WindowsFormsHost`, `AxHost` base | [VERIFIED: dotnet/winforms #13499] Contains PR #13532 (AxContainer parent Form leak fix), **but** a June 2025 report against preview 6 says the leak persists — see Common Pitfalls §.NET 10 Framework Risks below. |
| Microsoft.WindowsDesktop.App.Wpf | 10.0.x (framework ref) | `WindowsFormsHost`, WPF runtime | [CITED: CLAUDE.md] .NET 10 LTS locked. |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| CommunityToolkit.Mvvm | 8.4.2+ | `[ObservableProperty]`, `[RelayCommand]` in reconnect overlay ViewModel | Reconnection overlay ViewModel (attempt count, delay). |
| Serilog | 4.3.1 | Structured logging of connection lifecycle (no credentials) | `RdpHostControl` logs events and COM exceptions; `ResolveCredentialsStage` logs success/failure with hostname only. |
| Microsoft.Extensions.DependencyInjection | 10.0.x | Register stages, `IProtocolHostFactory`, `AirspaceSwapper` singleton, `ConnectionCoordinator` | `App.xaml.cs` composition root. |
| NSubstitute | (test stack) | Mock `IProtocolHost`, `ICredentialService`, `IConnectionStore`, `IEventBus` in stage unit tests | Pipeline stage tests — NEVER instantiate real `RdpHostControl` in unit tests. |
| xUnit v3 | (test stack) | Integration + unit test runner | Prototype's 20-cycle GDI gate runs as xUnit `[Fact]` under `[Collection("RDP-STA")]` with STA fixture. |

### Alternatives Considered

| Instead of | Could Use | Tradeoff | Why Not |
|------------|-----------|----------|---------|
| In-tree `AirspaceSwapper` | AirspaceFixer NuGet | Less code to maintain, community-maintained | [VERIFIED: D-13 locked] NuGet maintenance is uncertain; `PrintWindow`/`WM_ENTERSIZEMOVE` hook code is ~150 lines and copied verbatim from WINFORMS-HOST-AIRSPACE.md. We need to own it. |
| Own reconnect loop | `EnableAutoReconnect = true` on the control | Less code | [VERIFIED: D-04 locked] Multiple mstscax versions documented with leaks during auto-reconnect; our `DispatcherTimer` loop is logged, tested, and disposes + recreates the host each attempt. |
| `RegisterHotKey` for app shortcuts during RDP session | WPF `KeyBinding` | Cleaner markup | [CITED: RDP-ACTIVEX-PITFALLS §5] RDP control installs `WH_KEYBOARD_LL` hook; WPF `PreviewKeyDown` never fires. Phase 4 avoids this by setting `KeyboardHookMode = 0` (keys stay local); Phase 6 does the `RegisterHotKey` work for Ctrl+Tab/Ctrl+W/Ctrl+Shift+P. |
| Out-of-process host per session (MsRdpEx-style) | In-process hosting | True crash isolation | [VERIFIED: deferred to v2] Complex window reparenting via `SetParent`; not needed at Deskbridge's 15–20 session target. |

**Installation:** No new packages — all dependencies already wired by prior phases.

**Version verification:**
- Microsoft.WindowsDesktop.App.WindowsForms 10.0.x is a framework reference resolved by `UseWindowsForms=true`; no NuGet pin needed. [CITED: src/Deskbridge.Protocols.Rdp/Deskbridge.Protocols.Rdp.csproj]
- AxMSTSCLib / MSTSCLib are in-tree DLLs; do NOT regenerate (D-02 from Phase 1 locks aximp-generated assemblies). [CITED: CONTEXT.md canonical_refs]

## Architecture Patterns

### Recommended Folder Layout

```
src/
├── Deskbridge.Core/
│   └── Pipeline/
│       └── Stages/                     # NEW — protocol-agnostic stages (D-10)
│           ├── ResolveCredentialsStage.cs
│           ├── CreateHostStage.cs      # Uses IProtocolHostFactory
│           ├── ConnectStage.cs
│           ├── UpdateRecentsStage.cs
│           ├── DisconnectStage.cs
│           ├── DisposeStage.cs
│           └── PublishClosedEventStage.cs
│   └── Interfaces/
│       ├── IProtocolHostFactory.cs     # NEW — resolves host by Protocol enum
│       └── IConnectionCoordinator.cs   # NEW — event-bus bridge for pipeline invocation
│   └── Services/
│       ├── ConnectionCoordinator.cs    # NEW — subscribes to ConnectionRequestedEvent, runs pipeline
│       └── DisconnectReasonClassifier.cs  # NEW — D-06 classification helper
├── Deskbridge.Protocols.Rdp/
│   ├── RdpHostControl.cs               # NEW — IProtocolHost implementation
│   ├── RdpProtocolHostFactory.cs       # NEW — registered for Protocol.Rdp
│   ├── AirspaceSwapper.cs              # NEW — Plan 4-02 (per D-13, D-14)
│   ├── RdpConnectionConfigurator.cs    # NEW — DisplaySettings → AxMsRdpClient9 properties
│   └── Interop/                        # existing (AxMSTSCLib.dll, MSTSCLib.dll)
├── Deskbridge/
│   ├── ViewModels/
│   │   └── ReconnectOverlayViewModel.cs  # NEW — attempt count, delay, cancel command
│   ├── Views/
│   │   └── ReconnectOverlay.xaml       # NEW — WPF-UI card panel, Phase 2 tokens
│   └── App.xaml.cs                     # EDIT — register new stages, factory, coordinator
tests/
└── Deskbridge.Tests/
    ├── Pipeline/
    │   ├── ResolveCredentialsStageTests.cs
    │   ├── CreateHostStageTests.cs
    │   ├── ConnectStageTests.cs
    │   ├── UpdateRecentsStageTests.cs
    │   ├── DisconnectStageTests.cs
    │   ├── DisposeStageTests.cs
    │   └── PublishClosedEventStageTests.cs
    ├── Rdp/
    │   └── DisconnectReasonClassifierTests.cs
    └── Smoke/
        └── RdpHostControlSmokeTests.cs  # [Collection("RDP-STA")] — Plan 4-01 gate
```

### Pattern 1: IProtocolHost implementation (RdpHostControl)

**What:** `RdpHostControl` wraps `AxMsRdpClient9NotSafeForScripting` inside `WindowsFormsHost`, exposes `IProtocolHost`, and owns the strict-order COM lifecycle. It is a plain class (not a WPF UserControl) that exposes its `WindowsFormsHost` via a public property so the viewport grid can add it to `Children`.

**When to use:** Once per connection. The `CreateHostStage` in the pipeline resolves the factory and instantiates exactly one `RdpHostControl` per connection attempt. Reconnection attempts (D-04) dispose and recreate.

**Shape:**

```csharp
// Source: synthesis of IProtocolHost.cs + RDP-ACTIVEX-PITFALLS §1/§3/§4/§6 + WINFORMS-HOST-AIRSPACE §leaks
namespace Deskbridge.Protocols.Rdp;

public sealed class RdpHostControl : IProtocolHost
{
    private readonly ILogger<RdpHostControl> _logger;
    private WindowsFormsHost? _host;
    private AxMsRdpClient9NotSafeForScripting? _rdp;
    private TaskCompletionSource<bool>? _loginTcs;       // Completed by OnLoginComplete or OnDisconnected
    private TaskCompletionSource<bool>? _disconnectTcs;  // Completed by OnDisconnected
    private bool _disposed;

    public Guid ConnectionId { get; private set; }
    public bool IsConnected => _rdp?.Connected != 0;
    public WindowsFormsHost Host => _host ?? throw new ObjectDisposedException(nameof(RdpHostControl));
    public event EventHandler<string>? ErrorOccurred;

    public RdpHostControl(ILogger<RdpHostControl> logger)
    {
        // [CITED: RDP-ACTIVEX-PITFALLS §6] Apartment state assertion — defensive guard (D-11)
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException(
                "RdpHostControl must be created on the STA UI thread. See RDP-ACTIVEX-PITFALLS §6.");
        _logger = logger;
        _host = new WindowsFormsHost { Background = System.Windows.Media.Brushes.Black };
        _rdp = new AxMsRdpClient9NotSafeForScripting();
    }

    public Task ConnectAsync(ConnectionContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ConnectionId = context.Connection.Id;

        // [CITED: RDP-ACTIVEX-PITFALLS §1] Site BEFORE configure. Host must be in visual tree.
        // The caller (CreateHostStage or the coordinator) is responsible for Viewport.Children.Add(_host)
        // BEFORE calling ConnectAsync. If not in tree, Handle == 0 and GetOcx() returns null.
        if (_rdp!.Handle == IntPtr.Zero)
            throw new InvalidOperationException(
                "RdpHostControl.ConnectAsync called before host was added to the visual tree. " +
                "The control has not been sited. See RDP-ACTIVEX-PITFALLS §1.");

        // Wire events BEFORE configuring properties — OnDisconnected must fire even if Connect() throws
        _loginTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _rdp.OnLoginComplete += OnLoginComplete;
        _rdp.OnDisconnected   += OnDisconnectedDuringConnect;
        _rdp.OnLogonError     += OnLogonError;

        RdpConnectionConfigurator.Apply(_rdp, context);  // Sets Server, Port, UserName, Domain, DisplaySettings

        // [CITED: RDP-ACTIVEX-PITFALLS §4] IMsTscNonScriptable password — sited + not-connected
        if (!string.IsNullOrEmpty(context.ResolvedPassword))
        {
            try
            {
                var ocx = _rdp.GetOcx() as IMsTscNonScriptable
                    ?? throw new InvalidOperationException("GetOcx() did not return IMsTscNonScriptable.");
                ocx.ClearTextPassword = context.ResolvedPassword;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or NullReferenceException)
            {
                ErrorOccurred?.Invoke(this, $"Password set failed: {ex.GetType().Name}");
                throw;
            }
            // IMPORTANT: do NOT log context.ResolvedPassword. [CITED: ConnectionContext.cs XML doc]
        }

        try
        {
            _rdp.Connect();  // Returns immediately; OnLoginComplete/OnDisconnected fires async
        }
        catch (Exception ex) when (ex is COMException or AxHost.InvalidActiveXStateException)
        {
            ErrorOccurred?.Invoke(this, $"Connect threw: {ex.GetType().Name}: {ex.Message}");
            _loginTcs.TrySetException(ex);
        }

        return _loginTcs.Task;  // ConnectStage awaits this with a timeout
    }

    private void OnLoginComplete(object? s, EventArgs e)
    {
        _rdp!.OnDisconnected -= OnDisconnectedDuringConnect;
        _rdp.OnDisconnected  += OnDisconnectedAfterConnect;  // Swap handler: now triggers reconnect flow
        _loginTcs?.TrySetResult(true);
    }

    private void OnDisconnectedDuringConnect(object? s, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        var reason = DisconnectReasonClassifier.Describe(e.discReason, _rdp!.ExtendedDisconnectReason, _rdp);
        _loginTcs?.TrySetException(new RdpConnectFailedException(e.discReason, reason));
    }
    // ... OnDisconnectedAfterConnect publishes ConnectionClosedEvent / triggers reconnect
}
```

**Canonical siting-order guard (D-02 gate criterion #3):**

```csharp
// Source: RDP-ACTIVEX-PITFALLS §1 + D-02 criterion 3
namespace Deskbridge.Protocols.Rdp;

public static class AxSiting
{
    /// <summary>
    /// Forces correct site-before-configure order. Use wherever the prototype and
    /// real implementation need to configure an AxHost-derived control.
    /// </summary>
    public static void SiteAndConfigure<T>(
        Panel viewport,
        WindowsFormsHost host,
        T rdp,
        Action<T> configure) where T : AxHost
    {
        host.Child = rdp;                   // (1) Child assignment triggers CreateControl() inside WFH
        viewport.Children.Add(host);         // (2) Add to visual tree — triggers handle creation
        if (rdp.Handle == IntPtr.Zero)
            throw new InvalidOperationException(
                "AxHost not sited after adding to visual tree. " +
                "Parent container may be collapsed or have no layout. See RDP-ACTIVEX-PITFALLS §1.");
        configure(rdp);                      // (3) Now safe to set properties
    }
}
```

**Recommendation:** Use the runtime `Handle == 0` assertion inside `RdpHostControl.ConnectAsync` (shown above) instead of a static helper for the real implementation — the helper is nicer for the prototype's gate test where the order is explicit and linear. The assertion path works everywhere.

### Pattern 2: Awaiting a COM event from a pipeline stage (resolves Focus Area #2)

**Problem:** `ConnectStage` calls `host.ConnectAsync(context)` — which returns a `Task` that completes on `OnLoginComplete` — and needs a timeout. The stage is running on the STA thread (D-11), so the continuation must stay on the STA.

**Canonical pattern:**

```csharp
// Source: synthesis of D-11 + RDP-ACTIVEX-PITFALLS §6 + TAP guidelines
// File: Deskbridge.Core/Pipeline/Stages/ConnectStage.cs
public sealed class ConnectStage(IEventBus bus, ILogger<ConnectStage> logger) : IConnectionPipelineStage
{
    public string Name => "Connect";
    public int Order => 300;

    public async Task<PipelineResult> ExecuteAsync(ConnectionContext context)
    {
        if (context.Host is null)
            return new PipelineResult(false, "Host not created — CreateHostStage must run first.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));  // Connect-timeout budget

        try
        {
            // host.ConnectAsync returns a Task completed by OnLoginComplete (success) or
            // OnDisconnectedDuringConnect (throw RdpConnectFailedException).
            // Plain await — continuation resumes on the STA dispatcher context (D-11).
            var connectTask = context.Host.ConnectAsync(context);
            var finished = await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, cts.Token));
            if (finished != connectTask)
                throw new TimeoutException("RDP connect exceeded 30s timeout.");
            await connectTask;  // Propagate exception if any

            bus.Publish(new ConnectionEstablishedEvent(context.Connection, context.Host));
            return new PipelineResult(true);
        }
        catch (RdpConnectFailedException ex)
        {
            bus.Publish(new ConnectionFailedEvent(context.Connection, ex.HumanReason, ex));
            return new PipelineResult(false, ex.HumanReason);
        }
        catch (Exception ex) when (ex is TimeoutException or COMException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Connect failed for {Hostname}", context.Connection.Hostname);
            bus.Publish(new ConnectionFailedEvent(context.Connection, ex.Message, ex));
            return new PipelineResult(false, ex.Message);
        }
    }
}
```

**Why this shape:** The `RdpHostControl` owns its own `TaskCompletionSource` (completed on the STA by the `OnLoginComplete`/`OnDisconnected` callbacks, which fire on the STA's message loop — see RDP-ACTIVEX-PITFALLS §6). `ConnectStage` uses `Task.WhenAny` to combine the TCS with a cancellation token for the timeout. Because this runs on the STA thread and uses plain `await` (no `ConfigureAwait(false)`), continuations are captured by WPF's `DispatcherSynchronizationContext` and stay on the STA. [VERIFIED: RDP-ACTIVEX-PITFALLS §6 "SAFE — WPF's SynchronizationContext resumes on the UI thread by default"]

**On unsubscription / timeout cleanup:** Event handlers are unsubscribed inside `RdpHostControl.Dispose()` in bulk. For timeout scenarios where `Dispose()` is not called immediately, the `TaskCompletionSource.TrySetException(new TimeoutException(...))` pattern ensures the awaiter unwinds even if the COM event never arrives; the subsequent `DisposeStage` runs the full cleanup sequence.

### Pattern 3: Connect-trigger wiring (resolves Focus Area #6)

**Decision:** Use `ConnectionRequestedEvent` on the event bus with a `ConnectionCoordinator` singleton subscriber. This composes more cleanly than a direct pipeline call from the ViewModel because:

1. Phase 3's `ConnectionTreeViewModel` already has a `ConnectCommand` (stubbed with a snackbar — see src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs line 651). The ViewModel doesn't need a reference to `IConnectionPipeline` — it just publishes.
2. Phase 6's command palette Quick Connect feature will also need a connect trigger. Having both routes feed the same coordinator avoids duplication.
3. Testing: `ConnectionCoordinator` can be unit-tested with a mocked pipeline; the ViewModel only needs to verify it publishes the event.

**Shape:**

```csharp
// File: Deskbridge.Core/Services/ConnectionCoordinator.cs
public sealed class ConnectionCoordinator : IConnectionCoordinator, IDisposable
{
    private readonly IEventBus _bus;
    private readonly IConnectionPipeline _connect;
    private readonly IDisconnectPipeline _disconnect;
    private readonly Dispatcher _dispatcher;    // Captured from UI thread
    private readonly Dictionary<Guid, IProtocolHost> _active = new();
    private readonly ILogger<ConnectionCoordinator> _logger;

    public ConnectionCoordinator(IEventBus bus, IConnectionPipeline connect,
        IDisconnectPipeline disconnect, ILogger<ConnectionCoordinator> logger)
    {
        _bus = bus;
        _connect = connect;
        _disconnect = disconnect;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;  // Must be constructed on UI thread
        _bus.Subscribe<ConnectionRequestedEvent>(this, OnConnectionRequested);
    }

    private void OnConnectionRequested(ConnectionRequestedEvent evt)
    {
        // Marshal to STA (D-11) — coordinator may receive events from any thread (bus is thread-agnostic)
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => OnConnectionRequested(evt));
            return;
        }
        // Fire-and-forget: pipeline runs async, failures go to ConnectionFailedEvent via ConnectStage
        _ = _connect.ConnectAsync(evt.Connection);
    }

    public void Dispose() => _bus.Unsubscribe<ConnectionRequestedEvent>(this);
}
```

**ConnectionTreeViewModel update (~5 lines):**

```csharp
// src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs — around line 651
[RelayCommand]
private void Connect(TreeItemViewModel? item = null)
{
    var target = item as ConnectionTreeItemViewModel
                 ?? PrimarySelectedItem as ConnectionTreeItemViewModel;
    if (target is null) return;
    var model = _connectionStore.GetById(target.Id);
    if (model is null) return;
    _eventBus.Publish(new ConnectionRequestedEvent(model));  // Coordinator handles the rest
}
```

### Pattern 4: DisplaySettings → AxMsRdpClient9 property mapping (resolves Focus Area #8)

**Default configuration table** — per-connection settings override these. [VERIFIED: ConnectionModel.cs + REFERENCE.md §RDP ActiveX Reference + CONTEXT.md Specifics]

| AxMsRdpClient9 property | Default | Source of override | Notes |
|---|---|---|---|
| `Server` | — | `connection.Hostname` | Required |
| `AdvancedSettings9.RDPPort` | — | `connection.Port` (default 3389) | Required |
| `UserName` | "" | `connection.Username` (empty string if null) | Never null — COM treats null as `E_POINTER` |
| `Domain` | "" | `connection.Domain` (empty string if null) | |
| `AdvancedSettings9.SmartSizing` | `true` | `connection.DisplaySettings?.SmartSizing ?? true` | [CITED: CONTEXT.md Specifics] DPI story = "let SmartSizing handle it" |
| `DesktopWidth` | 0 (control default) | `connection.DisplaySettings?.Width ?? 0` | 0 = auto-detect |
| `DesktopHeight` | 0 (control default) | `connection.DisplaySettings?.Height ?? 0` | |
| `ColorDepth` | 32 | — | Fixed at 32bpp; DisplaySettings has no ColorDepth field yet |
| `AdvancedSettings9.EnableCredSspSupport` | `true` | — | [CITED: CONTEXT.md Specifics] Required for NLA |
| `AdvancedSettings9.CachePersistenceActive` | `0` | — | [CITED: RDP-ACTIVEX-PITFALLS §2] Directly mitigates mRemoteNG #1715 14-connection GDI cliff |
| `AdvancedSettings9.BitmapPeristence` | `0` | — | [CITED: REFERENCE.md constraint 18] Reduces GDI; Phase 5 toggles this per active/inactive tab |
| `SecuredSettings3.KeyboardHookMode` | `0` | — | [CITED: CONTEXT.md Specifics + RDP-ACTIVEX-PITFALLS §5] Keep app shortcuts local for Phase 4; Phase 6 uses RegisterHotKey |
| `AdvancedSettings9.GrabFocusOnConnect` | `false` | — | [CITED: RDP-ACTIVEX-PITFALLS §8] Don't steal focus during multi-tab connects (Phase 5 benefit) |
| `AdvancedSettings9.EnableAutoReconnect` | `false` | — | [CITED: D-04] We own the reconnect loop |
| `AdvancedSettings9.ContainerHandledFullScreen` | `0` | — | Phase 6 (CMD-04) sets this for F11 |

**Configurator class:**

```csharp
// File: Deskbridge.Protocols.Rdp/RdpConnectionConfigurator.cs
public static class RdpConnectionConfigurator
{
    public static void Apply(AxMsRdpClient9NotSafeForScripting rdp, ConnectionContext ctx)
    {
        var c = ctx.Connection;
        rdp.Server = c.Hostname;
        rdp.AdvancedSettings9.RDPPort = c.Port;
        rdp.UserName = c.Username ?? "";
        rdp.Domain = c.Domain ?? "";

        rdp.AdvancedSettings9.SmartSizing = c.DisplaySettings?.SmartSizing ?? true;
        if (c.DisplaySettings?.Width is > 0) rdp.DesktopWidth = c.DisplaySettings.Width.Value;
        if (c.DisplaySettings?.Height is > 0) rdp.DesktopHeight = c.DisplaySettings.Height.Value;
        rdp.ColorDepth = 32;

        rdp.AdvancedSettings9.EnableCredSspSupport = true;
        rdp.AdvancedSettings9.CachePersistenceActive = 0;
        rdp.AdvancedSettings9.BitmapPeristence = 0;       // Note misspelled property name in COM type lib
        rdp.SecuredSettings3.KeyboardHookMode = 0;
        rdp.AdvancedSettings9.GrabFocusOnConnect = false;
        rdp.AdvancedSettings9.EnableAutoReconnect = false;
        rdp.AdvancedSettings9.ContainerHandledFullScreen = 0;
    }
}
```

### Pattern 5: AirspaceSwapper API (resolves Focus Area #5)

**Decision:** `AirspaceSwapper` is a singleton service registered in DI. It tracks *one* active host at a time (Phase 4 ships single-host), but the API is designed so Phase 5 can register multiple hosts without breaking the shape.

**Shape:**

```csharp
// File: Deskbridge.Protocols.Rdp/AirspaceSwapper.cs
// Source: copied and adapted from WINFORMS-HOST-AIRSPACE.md §ResizeSnapshotManager
public sealed class AirspaceSwapper : IDisposable
{
    // --- Hosts registered for drag/resize snapshots ---
    public void RegisterHost(WindowsFormsHost host, Image overlay);
    public void UnregisterHost(WindowsFormsHost host);

    // --- Window hook (call once in MainWindow.OnSourceInitialized) ---
    public void AttachToWindow(Window window);

    // --- Overlay-only mode (D-07: hide WFH for reconnect overlay, no bitmap capture) ---
    public IDisposable HideWithoutSnapshot(WindowsFormsHost host);
    //   Returns a disposable; calling Dispose() restores Visibility.Visible.

    public void Dispose();
}
```

**Usage in Plan 4-02 / 4-03:**

```csharp
// App.xaml.cs
services.AddSingleton<AirspaceSwapper>();

// MainWindow.xaml.cs
protected override void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);
    _airspace = _services.GetRequiredService<AirspaceSwapper>();
    _airspace.AttachToWindow(this);
}

// When mounting an RdpHostControl in the viewport
_airspace.RegisterHost(rdpHostControl.Host, _viewportSnapshotImage);

// When showing the reconnect overlay (D-07)
_hideToken = _airspace.HideWithoutSnapshot(rdpHostControl.Host);
// ... show overlay ...
_hideToken.Dispose();  // Restores WFH visibility
```

### Pattern 6: Disconnect reason code classification (resolves Focus Area #7)

**Shape — static helper with a `DisconnectCategory` enum:**

```csharp
// File: Deskbridge.Core/Services/DisconnectReasonClassifier.cs
namespace Deskbridge.Core.Services;

public enum DisconnectCategory
{
    UserInitiated,    // 1, 2          — do not retry, do not show overlay
    ServerInitiated,  // 3             — retry with overlay
    NetworkLost,      // 264, 516, 772, 1028, 2308 — retry with overlay
    DnsFailure,       // 260, 520      — retry with overlay
    Authentication,   // 2055, 2567, 2823, 3335, 3591, 3847 — D-06 SKIP retry, manual overlay only
    Licensing,        // 2056, 2312    — D-06 SKIP retry, manual overlay only
    Protocol,         // 3334          — retry with overlay (may be transient resource pressure)
    Unknown
}

public static class DisconnectReasonClassifier
{
    private static readonly HashSet<int> UserInitiated = [1, 2];
    private static readonly HashSet<int> ServerInitiated = [3];
    private static readonly HashSet<int> NetworkLost = [264, 516, 772, 1028, 2308];
    private static readonly HashSet<int> DnsFailure = [260, 520];
    private static readonly HashSet<int> Authentication = [2055, 2567, 2823, 3335, 3591, 3847];
    private static readonly HashSet<int> Licensing = [2056, 2312];
    private static readonly HashSet<int> Protocol = [3334];

    public static DisconnectCategory Classify(int discReason) => discReason switch
    {
        _ when UserInitiated.Contains(discReason)    => DisconnectCategory.UserInitiated,
        _ when ServerInitiated.Contains(discReason)  => DisconnectCategory.ServerInitiated,
        _ when NetworkLost.Contains(discReason)      => DisconnectCategory.NetworkLost,
        _ when DnsFailure.Contains(discReason)       => DisconnectCategory.DnsFailure,
        _ when Authentication.Contains(discReason)   => DisconnectCategory.Authentication,
        _ when Licensing.Contains(discReason)        => DisconnectCategory.Licensing,
        _ when Protocol.Contains(discReason)         => DisconnectCategory.Protocol,
        _ => DisconnectCategory.Unknown
    };

    public static bool ShouldAutoRetry(DisconnectCategory cat) => cat is
        DisconnectCategory.ServerInitiated or
        DisconnectCategory.NetworkLost or
        DisconnectCategory.DnsFailure or
        DisconnectCategory.Protocol or
        DisconnectCategory.Unknown;  // Default: try once

    /// <summary>
    /// Human-readable description via GetErrorDescription on the COM object.
    /// </summary>
    public static string Describe(int discReason, int extendedReason, AxMsRdpClient9NotSafeForScripting rdp)
    {
        try { return rdp.GetErrorDescription((uint)discReason, (uint)extendedReason); }
        catch { return $"Disconnect reason {discReason} (extended {extendedReason})"; }
    }
}
```

**Source for reason codes:** [CITED: RDP-ACTIVEX-PITFALLS §7 "Key disconnect reason codes by category"] — verbatim from the pitfalls doc.

### Pattern 7: Reconnect loop with DispatcherTimer

```csharp
// Source: D-03 through D-05 + RDP-ACTIVEX-PITFALLS §6 (DispatcherTimer is STA-safe)
public sealed class RdpReconnectCoordinator
{
    private static readonly TimeSpan[] BackoffSchedule =
    [
        TimeSpan.FromSeconds(2),  TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),  TimeSpan.FromSeconds(16),
        // Remaining attempts cap at 30s
    ];
    private const int MaxAttempts = 20;

    public async Task<bool> RunAsync(ConnectionModel connection,
        Func<ConnectionModel, Task<bool>> reconnect, Func<int, TimeSpan, Task> notifyAttempt,
        CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var delay = attempt - 1 < BackoffSchedule.Length
                ? BackoffSchedule[attempt - 1]
                : TimeSpan.FromSeconds(30);

            await notifyAttempt(attempt, delay);   // Publishes ReconnectingEvent → overlay updates
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { return false; }

            if (await reconnect(connection)) return true;
        }
        return false;  // Cap hit — caller switches to manual "Reconnect / Close" overlay
    }
}
```

### Anti-Patterns to Avoid

- **Setting properties before siting** — throws `AxHost.InvalidActiveXStateException`. [CITED: RDP-ACTIVEX-PITFALLS §1]
- **`Marshal.ReleaseComObject(rdp)` manually** — the AxHost `Dispose()` handles this. Exception: `Marshal.FinalReleaseComObject(rdp.GetOcx())` is part of the documented full-cleanup reference (WINFORMS-HOST-AIRSPACE.md `RdpHostWrapper`) and runs *before* `rdp.Dispose()`.
- **`ConfigureAwait(false)` in pipeline stages** — moves continuation to thread pool (MTA); COM access throws. [CITED: RDP-ACTIVEX-PITFALLS §6 + D-11]
- **Re-parenting `WindowsFormsHost` between tabs** — may trigger `UCEERR_RENDERTHREADFAILURE`. Phase 4 single-host; Phase 5 hides/shows without re-parenting. [CITED: WINFORMS-HOST-AIRSPACE.md "Option 2 — Move a single WFH between tab content areas"]
- **Logging `context.ResolvedPassword` or including it in exception messages** — see Security Domain below.
- **Using `System.Threading.Timer` or `System.Timers.Timer` for reconnect** — thread pool callbacks, COM access from MTA. Always `DispatcherTimer`. [CITED: RDP-ACTIVEX-PITFALLS §6]
- **Calling `rdp.Dispose()` while connected** — `ReleaseAxControl()` blocks indefinitely. Always `Disconnect()` + wait for `OnDisconnected` (30s timeout) first. [CITED: RDP-ACTIVEX-PITFALLS §3]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| RDP connection lifecycle | Custom COM interop wrapper | Classic aximp `AxMsRdpClient9NotSafeForScripting` (already in `Interop/`) | [CITED: CLAUDE.md] COM source generators don't support ActiveX |
| Credential Manager access | Win32 P/Invoke | `AdysTech.CredentialManager` (already used by `WindowsCredentialService` in Phase 3) | Phase 3 locked this |
| Event bus | Custom publisher/subscriber | `WeakReferenceMessenger` wrapped by `IEventBus` (Phase 1) | Avoids memory leaks from forgotten subscriptions |
| Password storage in-memory | `SecureString` | Plain `string` in `ConnectionContext.ResolvedPassword` | [CITED: Microsoft DE0001 + CLAUDE.md] SecureString not encrypted in modern .NET |
| PrintWindow bitmap capture | WPF `RenderTargetBitmap` | `PrintWindow` via P/Invoke in `AirspaceSwapper` | [CITED: WINFORMS-HOST-AIRSPACE.md] `RenderTargetBitmap` cannot capture WFH content; it renders through WPF's DirectX pipeline which has no access to child HWND |
| Reconnect loop | `EnableAutoReconnect = true` | Own `DispatcherTimer` loop (D-04) | mstscax auto-reconnect has documented leak history |
| MVVM boilerplate | Hand-written `INotifyPropertyChanged` | `[ObservableProperty]` from CommunityToolkit.Mvvm | Project convention |
| Logging sink | Custom file writer | Serilog rolling file (already configured in Phase 3) | Phase 3 locked this |

**Key insight:** Every COM/ActiveX operation in this phase has a documented failure mode in RDP-ACTIVEX-PITFALLS.md. The AxHost wrapper itself has exotic edge cases (8.0.11 regression, 8.0.11 PR #13532 follow-up still failing in .NET 10 preview 6). Layering additional custom abstractions on top of AxHost is the exact mistake mRemoteNG made and pays for with #1715, #1671, #1535, #1749, #1831. Stay as close to the reference implementation in RDP-ACTIVEX-PITFALLS.md and WINFORMS-HOST-AIRSPACE.md as possible.

## Runtime State Inventory

*Not applicable. Phase 4 is greenfield implementation (no rename, no refactor, no migration). There is no pre-existing Deskbridge RDP state stored anywhere — this phase creates it.*

Verified: no stored data, no live service config, no OS-registered state, no secrets/env vars, no build artifacts carry forward for this phase. The one pre-existing asset is the aximp-generated interop DLLs in `src/Deskbridge.Protocols.Rdp/Interop/` — these are checked in and do NOT need regeneration (PROJ-04 locked).

## Common Pitfalls

### Pitfall 1: .NET 10 AxHost parent-form leak fix may not be fully shipped

[VERIFIED: dotnet/winforms #13499]

**What goes wrong:** `AxHost` keeps its parent Form alive via `AxContainer`. Every connect/disconnect cycle leaks the entire `WindowsFormsHost` plus its child Form reference chain plus all GDI handles. Symptom: GDI handle count does not return to baseline after the 20-cycle gate.

**Why it happens:** PR #13532 was merged May 29, 2025 to fix issue #13499, but a June 18, 2025 comment on the same issue reports the fix is **still not working** in `Microsoft.WindowsDesktop.App.WindowsForms` version `10.0.0-preview.6.25304.106`. No follow-up comments after that date are visible in the public issue tracker.

**How to avoid:**
1. Apply the two reflection-based WFH leak fixes documented in WINFORMS-HOST-AIRSPACE.md `RdpHostWrapper.PerformFullCleanup()` — `HwndSourceKeyboardInputSite._sinkElement` null-out and `HostContainerInternal` dispose — unconditionally, regardless of whether the framework fix works.
2. Measure. Plan 4-01's gate (D-02 criterion #1) is load-bearing. If GDI doesn't return to baseline, escalate before Plan 4-02 begins.
3. Document the observed GDI delta per cycle. If delta > 0 but < 50 handles, Phase 5's 15+ connection warning is sufficient. If delta is substantial, consider whether the out-of-process Devolutions approach must be pulled forward.

**Warning signs:** GDI handle count grows monotonically during the 20-cycle test. After 20 cycles, `GetGuiResources(proc, GR_GDIOBJECTS)` shows a value higher than baseline + small-constant-tolerance (acceptable delta: ≤ 50 handles total, since GC may not have collected all managed wrappers; unacceptable: ≥ 200 handles).

### Pitfall 2: dotnet/wpf #10171 — infinite recursion crash on window close

[VERIFIED: dotnet/wpf #10171 — status open as of January 11, 2025, no fix documented]

**What goes wrong:** Closing a `FluentWindow` that contains a `WindowsFormsHost` may crash with "Encountered infinite recursion while looking up resource in System.Private.CoreLib."

**Why it happens:** Regression introduced in .NET 8, reproducible on some machines but not others. Still open.

**How to avoid:** Explicitly dispose every `WindowsFormsHost` in the window's `OnClosing` (not `OnClosed`). Plan 4-02's `RdpHostControl` dispose path must be callable from `MainWindow.OnClosing`. Phase 5 will expand this to iterate all hosts in the multi-host container.

```csharp
// MainWindow.xaml.cs — add in Plan 4-02
protected override void OnClosing(CancelEventArgs e)
{
    // Dispose any live RdpHostControl before the window closes.
    // Phase 4: single host; Phase 5: iterate _tabManager.GetAll().
    _activeRdpHost?.Dispose();
    _activeRdpHost = null;
    base.OnClosing(e);
}
```

### Pitfall 3: PerMonitorV2 DPI is broken cross-monitor for WFH

[VERIFIED: dotnet/wpf #6294 + #9803, both open]

**What goes wrong:** Dragging the window between monitors with different DPI. WPF updates its logical dimensions, hosted WFH's physical pixel size stays fixed. Control appears the wrong size or clipped.

**How to avoid:** Set `SmartSizing = true` on every connection (already in the default property table). Accept that manual cross-monitor scaling is not going to be fully right. If it becomes user-visible, document in release notes.

### Pitfall 4: IMsTscNonScriptable cast silently fails if control isn't sited

[CITED: RDP-ACTIVEX-PITFALLS §4]

**What goes wrong:** `rdp.GetOcx()` returns `null` if the control hasn't been sited. Casting null doesn't throw — the subsequent `ClearTextPassword = ...` throws `NullReferenceException`.

**How to avoid:** The `RdpHostControl.ConnectAsync` code above explicitly checks `rdp.Handle == IntPtr.Zero` before the cast. The pattern uses pattern matching (`_rdp.GetOcx() as IMsTscNonScriptable ?? throw ...`) so a null OCX surfaces as an informative `InvalidOperationException`.

### Pitfall 5: Logging ResolvedPassword leaks credentials

[CITED: ConnectionContext.cs XML doc "Do not log or serialize"]

**What goes wrong:** Developer adds `_logger.LogDebug("Connecting {@Context}", context)` during debugging. Serilog structured logging serializes the whole object including `ResolvedPassword`.

**How to avoid:** Never pass `ConnectionContext` directly to structured logging. Log individual safe fields only: `_logger.LogInformation("Connecting to {Hostname}:{Port} as {Username}", c.Hostname, c.Port, c.Username)`. Add unit test: serialize a `ConnectionContext` with a password and assert the password string is not present in the output. See Security Domain §Threat Model below.

### Pitfall 6: OnDisconnected fires during connect (connection refused)

[CITED: RDP-ACTIVEX-PITFALLS §7 + §3]

**What goes wrong:** Bad hostname. `Connect()` returns immediately; shortly after, `OnDisconnected(discReason=516)` fires. If `ConnectAsync`'s `TaskCompletionSource` was only wired to `OnLoginComplete`, it hangs forever until the timeout.

**How to avoid:** Wire BOTH `OnLoginComplete` AND `OnDisconnected` to the same TCS with different completion semantics (success / exception). The `RdpHostControl` code above does this with `OnLoginComplete` → `TrySetResult(true)` and `OnDisconnectedDuringConnect` → `TrySetException(RdpConnectFailedException)`. After login succeeds, swap `OnDisconnected` to the "during session" handler.

### Pitfall 7: Disposing while events are still subscribed

[CITED: RDP-ACTIVEX-PITFALLS §3]

**What goes wrong:** COM event fires during `Dispose()`, handler tries to access already-disposed fields, AccessViolationException takes down the process.

**How to avoid:** Unsubscribe all events BEFORE calling `rdp.Dispose()`. The sequence is: `Disconnect()` → poll for `OnDisconnected` (30s) → unsubscribe all events → `rdp.Dispose()` → `host.Child = null` → WFH leak-fix reflection → `host.Dispose()` → `parent.Children.Remove(host)`.

### Pitfall 8: Coordinator subscribed to event bus but pipeline invocation happens on wrong thread

[CITED: D-11 + RDP-ACTIVEX-PITFALLS §6]

**What goes wrong:** `WeakReferenceMessenger` delivers synchronously on the publisher's thread. If `ConnectionRequestedEvent` is published from a background thread (Phase 6 command palette search might), the coordinator tries to start the pipeline from MTA, COM calls fail.

**How to avoid:** The `ConnectionCoordinator.OnConnectionRequested` code above captures the UI `Dispatcher` in its constructor and marshals to it if `!CheckAccess()`. This is the load-bearing piece of D-11.

## Code Examples

### Example 1: Canonical safe disposal sequence (consolidated)

```csharp
// Source: RDP-ACTIVEX-PITFALLS §3 + WINFORMS-HOST-AIRSPACE §leaks
// File: Deskbridge.Protocols.Rdp/RdpHostControl.cs (Dispose path)
public async Task DisconnectAsync()
{
    if (_disposed || _rdp is null) return;
    AssertSta();

    if (_rdp.Connected != 0)
    {
        try { _rdp.Disconnect(); } catch { /* may throw mid-teardown */ }
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (_rdp.Connected != 0 && DateTime.UtcNow < deadline)
            await Task.Delay(100);   // Plain await — stays on STA
        if (_rdp.Connected != 0)
            _logger.LogWarning("RDP disconnect timed out after 30s — force disposing");
    }
}

public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    AssertSta();

    DisconnectAsync().GetAwaiter().GetResult();  // Synchronous disposal is ok on STA (pumped)

    // --- Event unsubscribe ---
    if (_rdp is not null)
    {
        try
        {
            _rdp.OnLoginComplete  -= OnLoginComplete;
            _rdp.OnDisconnected   -= OnDisconnectedDuringConnect;
            _rdp.OnDisconnected   -= OnDisconnectedAfterConnect;
            _rdp.OnLogonError     -= OnLogonError;
        }
        catch { }
    }

    // --- WFH leak fix #1: HwndSourceKeyboardInputSite ---
    if (_host is not null)
    {
        try
        {
            var site = ((IKeyboardInputSink)_host).KeyboardInputSite;
            if (site is not null)
            {
                site.Unregister();
                var siteType = site.GetType();
                siteType.GetField("_sinkElement", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(site, null);
                siteType.GetField("_sink", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(site, null);
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "KeyboardInputSite leak-fix failed (non-fatal)"); }
    }

    // --- WFH leak fix #2: WinFormsAdapter via HostContainerInternal ---
    if (_host is not null)
    {
        try
        {
            var adapterProp = typeof(WindowsFormsHost).GetProperty(
                "HostContainerInternal", BindingFlags.NonPublic | BindingFlags.Instance);
            (adapterProp?.GetValue(_host) as IDisposable)?.Dispose();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "WinFormsAdapter leak-fix failed (non-fatal)"); }
    }

    // --- Release COM + dispose AxHost ---
    if (_rdp is not null)
    {
        try
        {
            if (_host is not null) _host.Child = null;
            try
            {
                var ocx = _rdp.GetOcx();
                if (ocx is not null) Marshal.FinalReleaseComObject(ocx);
            }
            catch { }
            _rdp.Dispose();
        }
        catch (Exception ex) when (ex is AccessViolationException
                                    or InvalidComObjectException or COMException)
        {
            _logger.LogError(ex, "AxHost dispose threw — continuing teardown");
        }
        _rdp = null;
    }

    // --- Dispose WFH ---
    try { _host?.Dispose(); } catch { }
    _host = null;
}

private static void AssertSta()
{
    if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        throw new InvalidOperationException("RdpHostControl operation must run on STA thread.");
}
```

### Example 2: Prototype smoke test (Plan 4-01 gate)

```csharp
// Source: D-02 gate criteria + RDP-ACTIVEX-PITFALLS §2 (GDI probe)
// File: tests/Deskbridge.Tests/Smoke/RdpHostControlSmokeTests.cs
[Collection("RDP-STA")]  // STA collection fixture sets ApartmentState.STA on the xunit worker
public class RdpHostControlSmokeTests
{
    // Real RDP target — pulled from env var so CI can skip with [SkippableFact]
    // e.g. DESKBRIDGE_SMOKE_RDP_HOST=localhost DESKBRIDGE_SMOKE_RDP_USER=Admin DESKBRIDGE_SMOKE_RDP_PASS=...
    private static readonly string? Host = Environment.GetEnvironmentVariable("DESKBRIDGE_SMOKE_RDP_HOST");

    [SkippableFact]
    public async Task Gate1_20CycleGdiBaseline_HandleCountReturnsToBaseline()
    {
        Skip.If(Host is null, "Set DESKBRIDGE_SMOKE_RDP_HOST to run");

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int baseline = GetGdi();

        for (int i = 0; i < 20; i++)
        {
            await RunOneCycleAsync();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }

        int final = GetGdi();
        int delta = final - baseline;
        // [VERIFIED: dotnet/winforms #13499 reported still leaking in .NET 10 preview 6]
        // If this assertion fails, escalate before Plan 4-02 begins.
        Assert.True(delta < 50, $"GDI handle leak detected: baseline={baseline}, final={final}, delta={delta}");
    }

    [SkippableFact]
    public async Task Gate2_IMsTscNonScriptable_PasswordSetSucceeds() { /* ... */ }

    [Fact]
    public void Gate3_SitingOrderGuard_ThrowsWhenConfigureBeforeSite()
    {
        // Verify SiteAndConfigure helper throws if Handle == 0 after presumed siting
        var panel = new Grid();
        // Do NOT add panel to a visual tree (no window) — Handle stays 0
        var host = new WindowsFormsHost();
        var rdp = new AxMsRdpClient9NotSafeForScripting();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            AxSiting.SiteAndConfigure(panel, host, rdp, r => r.Server = "ignored"));
        Assert.Contains("not sited", ex.Message);
    }

    [SkippableFact]
    public async Task Gate4_ComError_DoesNotTearDownApp()
    {
        // Use deliberately bad hostname; OnDisconnected fires with discReason=516 or similar
        // Verify ErrorOccurred event fires, Dispose completes cleanly, process still alive
    }

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);
    private static int GetGdi()
        => GetGuiResources(Process.GetCurrentProcess().Handle, 0 /*GR_GDIOBJECTS*/);

    private async Task RunOneCycleAsync() { /* create → connect → disconnect → dispose */ }
}
```

### Example 3: ResolveCredentialsStage

```csharp
// File: Deskbridge.Core/Pipeline/Stages/ResolveCredentialsStage.cs
// Source: Phase 3 §D-15 deferral + CONTEXT.md D-08
public sealed class ResolveCredentialsStage(
    ICredentialService creds,
    IConnectionStore store,
    IEventBus bus,
    ILogger<ResolveCredentialsStage> log) : IConnectionPipelineStage
{
    public string Name => "ResolveCredentials";
    public int Order => 100;

    public Task<PipelineResult> ExecuteAsync(ConnectionContext ctx)
    {
        var c = ctx.Connection;
        switch (c.CredentialMode)
        {
            case CredentialMode.Own:
                {
                    var cred = creds.GetForConnection(c);
                    if (cred is null)
                    {
                        log.LogInformation("No own credential for {Hostname} — prompting", c.Hostname);
                        bus.Publish(new CredentialRequestedEvent(c));
                        // Phase 4 scope: treat prompt as failure. Phase 6 adds interactive credential prompt.
                        return Task.FromResult(new PipelineResult(false, "Credentials not found (own)"));
                    }
                    ApplyCredential(ctx, cred);
                    return Task.FromResult(new PipelineResult(true));
                }
            case CredentialMode.Inherit:
                {
                    var cred = creds.ResolveInherited(c, store);
                    if (cred is null)
                    {
                        bus.Publish(new CredentialRequestedEvent(c));
                        return Task.FromResult(new PipelineResult(false, "No inherited credential found"));
                    }
                    ApplyCredential(ctx, cred);
                    return Task.FromResult(new PipelineResult(true));
                }
            case CredentialMode.Prompt:
            default:
                bus.Publish(new CredentialRequestedEvent(c));
                return Task.FromResult(new PipelineResult(false, "Credentials require prompt"));
        }
    }

    private static void ApplyCredential(ConnectionContext ctx, NetworkCredential cred)
    {
        // Apply username/domain to the model copy — RDP control reads from ConnectionContext.Connection
        ctx.Connection.Username = cred.UserName;
        ctx.Connection.Domain = cred.Domain;
        ctx.ResolvedPassword = cred.Password;    // Do NOT log
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `Marshal.ReleaseComObject(rdp._instance)` | `Marshal.FinalReleaseComObject(rdp._instance)` inside AxHost | PR #12281 shipped in .NET 8.0.11 | Destructor now runs; fix forward-ported to .NET 9, .NET 10 |
| Hand-rolled event bus | `WeakReferenceMessenger` (CommunityToolkit.Mvvm) | Phase 1 | Avoids leaks from forgotten subscriptions |
| `SecureString` for passwords | Plain `string` + Windows Credential Manager | Microsoft DE0001 | `SecureString` has no encryption in modern .NET |
| `EnableAutoReconnect = true` | Own `DispatcherTimer` loop with dispose-and-recreate | D-04 locked | mstscax auto-reconnect has documented leak history |
| `WindowChrome` (WPF built-in) | WPF-UI FluentWindow's own chrome | Phase 2 locked | Avoids dotnet/wpf #5892 WS_VISIBLE flicker with WFH |
| Per-tab WFH re-parent | One WFH per connection, toggle `Visibility` | Phase 5 scope | Re-parent triggers `UCEERR_RENDERTHREADFAILURE` |

**Deprecated / still-broken:**
- `dotnet/wpf #152` airspace: architectural, "Future" milestone, no concrete fix. Still requires bitmap-swap workaround in 2026.
- `dotnet/wpf #6294` + `#9803` cross-monitor DPI: still open. `SmartSizing` is the answer.
- `dotnet/wpf #10171` infinite recursion on close: open, workaround is explicit WFH dispose before close.
- `dotnet/winforms #13499` AxContainer parent Form leak: marked closed with PR #13532 (May 2025), but reported still leaking in .NET 10 preview 6 (June 2025). **Status uncertain for .NET 10 GA — must measure.**

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | PR #13532 shipped in .NET 10 GA SDK | Pitfall 1 + Common Pitfalls §.NET 10 Framework Risks | Framework-level leak may be larger than the smoke test can absorb; would force reflection-based fixes to do more heavy lifting |
| A2 | CommunityToolkit.Mvvm 8.4.2+ `WeakReferenceMessenger` delivers synchronously on the publisher's thread (hence the `ConnectionCoordinator` dispatcher marshal) | Pattern 3 | If delivery is async, the `Dispatcher.CheckAccess` guard may be unnecessary but is harmless |
| A3 | `Microsoft.WindowsDesktop.App.WindowsForms` shipped with .NET 10 SDK auto-activates on `UseWindowsForms=true` and exposes `AxMSTSCLib`-compatible `AxHost` ABI | Standard Stack | Near-zero risk — classic COM interop has been ABI-stable since .NET Framework 2.0 |
| A4 | `rdp.Handle == IntPtr.Zero` is a reliable indicator of "not sited" (equivalent to `!IsHandleCreated`) | Pattern 1 guard | If `Handle` is lazily created on first access, the guard may accidentally allocate a handle it shouldn't. Safer alternative: `(_rdp as System.Windows.Forms.Control).IsHandleCreated` — recommend planner adopts this during implementation |
| A5 | `discReason` values 1, 2, 3, 264, 516, 772, 1028, 2308, 260, 520, 2055, 2056, 2312, 2567, 2823, 3334, 3335, 3591, 3847 are canonical for RDP 8.1+ | Pattern 6 | RDP-ACTIVEX-PITFALLS §7 is the authoritative source; Microsoft docs [VERIFIED via Microsoft Q&A references in pitfalls doc] use the same codes |
| A6 | Serilog is already registered in DI with a file sink and propagates to `Deskbridge.Core` | All stages | [CITED: STATE.md Phase 03 decision "Serilog added to Deskbridge.Core.csproj for error logging"] — verified |
| A7 | NSubstitute works with records (e.g., `bus.Received().Publish(Arg.Is<ConnectionEstablishedEvent>(...))`) | Testing strategy | Widely-used pattern, low risk |
| A8 | `DispatcherTimer` with `Interval` set from a `TimeSpan[]` array executes reliably at the stated intervals on Windows 11 24H2 | Pattern 7 | Well-established; single-digit-millisecond drift only under UI thread pressure |

**Critical assumption for gate verification:** A1 is the single most important assumption in this phase. If it's wrong, the 20-cycle gate in Plan 4-01 will fail and the whole phase strategy must change. This is why D-01 (prototype first) is the right structure.

## Open Questions

1. **Does PR #13532 actually work in .NET 10 GA?**
   - What we know: Merged May 2025. Reported not working in preview 6 (June 2025). No public update since.
   - What's unclear: Whether a second fix shipped before GA in November 2025.
   - Recommendation: The planner should include a "measure on the actual target SDK" task as an explicit step in Plan 4-01, and treat the gate as authoritative over this research. If the 20-cycle delta is > 50 handles and the reflection-based leak fixes don't close the gap, escalate to the user before Plan 4-02.

2. **Should `ConnectionCoordinator` track the active host so the tree's Connect command can short-circuit re-connects?**
   - What we know: Phase 4 is single-host. A second `ConnectionRequestedEvent` while one is active needs a decision: queue, replace, reject?
   - What's unclear: User intent. Not covered in CONTEXT.md.
   - Recommendation: Phase 4 **replaces** (dispose current, connect new) — simplest behavior, keeps single-host invariant. Phase 5 will switch to open-new-tab. Document in Plan 4-03.

3. **Where should `ConnectionCoordinator`'s dispatcher come from under tests?**
   - What we know: Constructor captures `Dispatcher.CurrentDispatcher`. If tests instantiate on a non-UI thread, they get a different dispatcher.
   - Recommendation: Accept `Dispatcher` as a constructor parameter (default: `Dispatcher.CurrentDispatcher`) so tests can inject a test dispatcher. Include in Plan 4-02.

4. **Does the reconnect loop's `reconnect` callback create a fresh `RdpHostControl` per attempt (D-04 "dispose + recreate")?**
   - D-04 says yes. The `IConnectionCoordinator` owns this: on `OnDisconnectedAfterConnect`, it classifies the reason, and if `ShouldAutoRetry` is true, it runs the reconnect loop which calls `_connect.ConnectAsync(connection)` each iteration — and that pipeline creates a new host via `CreateHostStage`.
   - Recommendation: Document this flow explicitly in Plan 4-03 so the planner doesn't accidentally pass the same host through.

## Environment Availability

*Skipped — Phase 4 introduces no new external dependencies. All tools needed are already available from prior phases (dotnet SDK 10, WPF-UI 4.2.0, xUnit v3 via CPM). The smoke test needs a real RDP target but that is a test environment concern, not a dev-time dependency — covered via `DESKBRIDGE_SMOKE_RDP_HOST` env var with `[SkippableFact]` so absence is tolerable.*

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit v3 + FluentAssertions + NSubstitute [CITED: tests/Deskbridge.Tests/Deskbridge.Tests.csproj] |
| Config file | `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` (no xunit.runner.json needed) |
| Quick run command | `dotnet test --filter "FullyQualifiedName!~Smoke"` (excludes live-RDP gate) |
| Full suite command | `dotnet test` (requires `DESKBRIDGE_SMOKE_RDP_HOST` env var for gate tests to run; SkippableFact otherwise) |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| RDP-01 | RdpHostControl implements IProtocolHost with correct shape | unit | `dotnet test --filter "FullyQualifiedName~RdpHostControlShapeTests"` | Wave 0 (new file) |
| RDP-02 | SiteAndConfigure helper throws if Handle == 0 after add | unit | `dotnet test --filter "FullyQualifiedName~SitingGuardTests"` | Wave 0 |
| RDP-02 | Prototype smoke Gate 3: order enforcement | smoke (live) | `dotnet test --filter "FullyQualifiedName~Gate3_SitingOrderGuard"` | Wave 0 |
| RDP-03 | IMsTscNonScriptable cast + ClearTextPassword works | smoke (live) | `dotnet test --filter "FullyQualifiedName~Gate2_IMsTscNonScriptable"` | Wave 0 |
| RDP-04 | Full dispose sequence runs without exception | unit (mocked) + smoke | `dotnet test --filter "FullyQualifiedName~DisposeStageTests"` + Gate1 | Wave 0 |
| RDP-04 | GDI returns to baseline after 20 cycles | smoke (live, gate) | `dotnet test --filter "FullyQualifiedName~Gate1_20CycleGdiBaseline"` | Wave 0 |
| RDP-05 | Pipeline executes stages in order Resolve→Create→Connect→Recents | unit | `dotnet test --filter "FullyQualifiedName~ConnectionPipelineIntegrationTests"` | Wave 0 |
| RDP-05 | Direct pipeline call from UI code would fail review; coordinator is the only connect path | manual (code review) | Code review checklist entry | n/a — process, not test |
| RDP-06 | Reconnect coordinator backoff schedule matches 2,4,8,cap-30 for 20 attempts | unit | `dotnet test --filter "FullyQualifiedName~RdpReconnectCoordinatorTests"` | Wave 0 |
| RDP-06 | DisconnectReasonClassifier maps all documented codes to correct category | unit | `dotnet test --filter "FullyQualifiedName~DisconnectReasonClassifierTests"` | Wave 0 |
| RDP-06 | Overlay shows attempt counter and cancel button | manual (visual checkpoint, Plan 4-03) | Screenshot compare against DESIGN.md tokens | n/a — visual |
| RDP-07 | COM exception in one session is caught, ErrorOccurred fires, process alive | unit (mocked) + smoke | `dotnet test --filter "FullyQualifiedName~ErrorIsolationTests"` + Gate4 | Wave 0 |
| RDP-08 | Each pipeline stage publishes the correct event | unit (mock IEventBus) | `dotnet test --filter "FullyQualifiedName~ConnectStageTests"` etc. | Wave 0 |
| RDP-09 | AirspaceSwapper.WM_ENTERSIZEMOVE captures bitmap, hides WFH | integration | `dotnet test --filter "FullyQualifiedName~AirspaceSwapperTests"` (requires STA + minimal WPF app) | Wave 0 |
| RDP-09 | During drag/resize, no black flicker | manual (visual) | Manual test: open session, drag window edge rapidly | n/a — visual |

### Sampling Rate

- **Per task commit:** `dotnet test --filter "FullyQualifiedName!~Smoke"` (unit + integration, ~5 seconds)
- **Per wave merge:** `dotnet test` with `DESKBRIDGE_SMOKE_RDP_HOST` set (includes the 4 gate tests)
- **Phase gate:** Full suite green + manual visual checkpoints for RDP-06 overlay and RDP-09 drag smoothness; GDI delta < 50 across 20-cycle gate before `/gsd-verify-work`

### Wave 0 Gaps

- [ ] `tests/Deskbridge.Tests/Smoke/RdpHostControlSmokeTests.cs` — Plan 4-01 gate tests (RDP-04 GDI + RDP-03 password + RDP-02 order + RDP-07 error isolation)
- [ ] `tests/Deskbridge.Tests/Fixtures/StaCollectionFixture.cs` — xUnit v3 collection fixture setting apartment state STA for smoke tests
- [ ] `tests/Deskbridge.Tests/Pipeline/` — 7 new stage test files (see Recommended Folder Layout)
- [ ] `tests/Deskbridge.Tests/Rdp/DisconnectReasonClassifierTests.cs` — table-driven tests for all code categories
- [ ] `tests/Deskbridge.Tests/Rdp/RdpReconnectCoordinatorTests.cs` — backoff schedule + cap + cancel
- [ ] `tests/Deskbridge.Tests/Rdp/AirspaceSwapperTests.cs` — WndProc hook + hide/show + snapshot capture (integration — must instantiate an HwndSource)
- [ ] `tests/Deskbridge.Tests/Coordinator/ConnectionCoordinatorTests.cs` — bus subscription, dispatcher marshal, active-host replacement
- [ ] A `[SkippableFact]` attribute from a new test helper (xunit v3 doesn't ship one out of the box; Xunit.SkippableFact does, or implement with `Assert.Skip("...")` — recommend planner picks a path in Plan 4-01)

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes | Windows Credential Manager via `AdysTech.CredentialManager` (Phase 3), passwords forwarded through `ConnectionContext.ResolvedPassword` to `IMsTscNonScriptable.ClearTextPassword`. NLA via `EnableCredSspSupport = true` |
| V3 Session Management | partial | Phase 6 owns app-lock (SEC-01–05). Phase 4 does NOT touch session management — RDP sessions remain live behind the lock |
| V4 Access Control | no | Out of scope for RDP host; app-lock controls access to the tool itself |
| V5 Input Validation | minimal | `connection.Hostname` / `Username` / `Domain` are COM-validated by mstscax; no additional validation layer needed |
| V6 Cryptography | yes (delegated) | RDP channel encryption is the control's responsibility. Master password PBKDF2 is Phase 6 |
| V7 Error Handling | yes | Pipeline stages must log errors WITHOUT credentials; COM exceptions sanitized |
| V8 Data Protection | yes | `ConnectionContext.ResolvedPassword` is plain string, in-memory only, never logged or serialized [CITED: ConnectionContext.cs XML doc] |
| V10 Malicious Code | no | No user-supplied code paths |
| V14 Configuration | yes | `connections.json` must not contain passwords [CITED: REFERENCE.md constraint 11] — `ConnectionModel` has no password field, audit with test |

### Known Threat Patterns for RDP ActiveX hosted in WPF

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Credential leak via Serilog structured logging (`Log.Information("{@Context}", ctx)` serializes ResolvedPassword) | Information Disclosure | Never pass `ConnectionContext` to structured logging. Log individual safe fields. Add unit test asserting password never appears in log output. |
| Credential leak via exception message (COM exception contains plaintext password in `.ToString()`) | Information Disclosure | Catch COM exceptions, log typed summary only (`ex.GetType().Name + ex.HResult`), never `ex.Message` or `ex.ToString()` |
| Credential leak via crash dump (ResolvedPassword in heap at crash time) | Information Disclosure | Clear `ResolvedPassword = null` in `ConnectStage` after password is handed to `IMsTscNonScriptable`. Document as defense-in-depth — not a full mitigation since the password still lived in memory briefly |
| Password stored in JSON on disk | Tampering / Disclosure | `ConnectionModel` has no `Password` field — verified by reading the class [CITED: ConnectionModel.cs]. Add a regression test that serializes a full `ConnectionModel` and asserts no "password"-like substring appears |
| Man-in-the-middle during RDP handshake | Tampering / Spoofing | `EnableCredSspSupport = true` forces NLA/TLS; control handles cert validation internally |
| Shoulder-surfing ClearTextPassword via debugger | Information Disclosure | Accepted risk — SecureString is deprecated for a reason; master password gate (Phase 6 SEC-01/02) is the layer that protects credentials at rest |
| COM object spoofing (cast to unexpected interface) | Spoofing | `GetOcx() as IMsTscNonScriptable` with null-throw — any unexpected type surfaces immediately |
| ActiveX control crash propagates to app | Denial of Service | Try/catch around every COM call (RDP-07). Process-level isolation (out-of-process host) is v2 scope |

### Phase 4 Security Checklist for Plans

The planner should include these as tasks / code-review items:

- [ ] `ConnectionContext.ResolvedPassword` is never passed to Serilog, ever. Verified by regex grep: no `Log*.*ResolvedPassword` or `{@Context}` or `{@Connection}` in any stage.
- [ ] All COM exceptions caught in stages log `ex.GetType().Name` and `ex.HResult` only — never `ex.Message` or `ex.ToString()`.
- [ ] `ResolvedPassword` is cleared (`ctx.ResolvedPassword = null`) immediately after `RdpHostControl.ConnectAsync` hands it to `IMsTscNonScriptable`. Documented as defense in depth.
- [ ] Unit test: serialize `ConnectionModel` via `System.Text.Json` and assert no "Password" / "Credential" substring in output.
- [ ] Unit test: serialize `ConnectionContext` with a known password via `System.Text.Json` and assert the password value is not in the output (the `ResolvedPassword` property should not be in `DefaultJsonSerializerOptions` — if it is, apply `[JsonIgnore]`).

## Sources

### Primary (HIGH confidence)

- **RDP-ACTIVEX-PITFALLS.md** (repo root) — all 8 sections used throughout; canonical source for siting, disposal, password casting, STA affinity, disconnect codes, multi-instance thresholds
- **WINFORMS-HOST-AIRSPACE.md** (repo root) — airspace architecture, PrintWindow capture code, WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE pattern, `RdpHostWrapper` reference implementation, `HwndSourceKeyboardInputSite` + `WinFormsAdapter` leak fixes, dotnet/wpf #10171 / #6294 / #9803 / #5892 status
- **REFERENCE.md** §RDP Integration, §RDP ActiveX Reference, §Drag/Resize Smoothness (repo root) — project spec, canonical siting + disposal pattern, wrapper definition
- **CLAUDE.md** (repo root) — tech stack (locked), "Do NOT Use" list, "Do NOT Use" for CredentialManagement/SecureString/GeneratedComInterface/Marshal.ReleaseComObject
- **.planning/phases/04-rdp-integration/04-CONTEXT.md** — all user decisions D-01 through D-14
- `src/Deskbridge.Core/Interfaces/IProtocolHost.cs`, `IConnectionPipeline.cs`, `IDisconnectPipeline.cs`, `ICredentialService.cs`, `IConnectionStore.cs`, `IEventBus.cs`
- `src/Deskbridge.Core/Pipeline/ConnectionContext.cs`, `ConnectionPipeline.cs`, `DisconnectContext.cs`, `DisconnectPipeline.cs`, `PipelineResult.cs`
- `src/Deskbridge.Core/Events/ConnectionEvents.cs`
- `src/Deskbridge.Core/Models/ConnectionModel.cs`, `Enums.cs`
- `src/Deskbridge.Protocols.Rdp/Deskbridge.Protocols.Rdp.csproj` + `Interop/*.dll`
- `src/Deskbridge/App.xaml.cs`, `MainWindow.xaml`, `ViewModels/ConnectionTreeViewModel.cs`, `Views/ConnectionTreeControl.xaml.cs`
- `tests/Deskbridge.Tests/Deskbridge.Tests.csproj`

### Secondary (MEDIUM confidence)

- [dotnet/winforms #13499 AxHost keeping parent alive](https://github.com/dotnet/winforms/issues/13499) — PR #13532 status; June 2025 report that fix does NOT work in .NET 10 preview 6
- [dotnet/winforms #12056 AxHost Dispose destructor regression](https://github.com/dotnet/winforms/issues/12056) — fixed by PR #12281 in .NET 8.0.11
- [dotnet/wpf #10171 Infinite recursion on window close](https://github.com/dotnet/wpf/issues/10171) — still open as of January 2025
- [dotnet/wpf #152 Airspace issue](https://github.com/dotnet/wpf/issues/152) — architectural, milestoned "Future"
- [NuGet Gallery — WPF-UI 4.2.0](https://www.nuget.org/packages/WPF-UI/) — .NET 10 target confirmed
- [WPF UI FluentWindow class reference](https://wpfui.lepo.co/api/Wpf.Ui.Controls.FluentWindow.html)
- [Marshal.FinalReleaseComObject Method — .NET 10 reference](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshal.finalreleasecomobject?view=net-10.0)
- [AxHost Class — .NET 10 reference](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.axhost?view=windowsdesktop-10.0)

### Tertiary (LOW confidence)

- [MsRdpClient10NotSafeForScripting — Win32 docs](https://learn.microsoft.com/en-us/windows/win32/termserv/msrdpclient10notsafeforscripting) — generic class reference, not version-specific
- [Troubleshooting Hybrid Applications — WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/troubleshooting-hybrid-applications) — general guidance, mentions WFH/ElementHost disposal

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries locked by prior phases, no new dependencies
- Architecture patterns: HIGH — RdpHostControl, Disposal, ConnectStage, ConnectionCoordinator, DisconnectReasonClassifier, Reconnect loop, AirspaceSwapper all derive from primary sources (pitfalls docs + CONTEXT.md)
- Pitfalls: MEDIUM — .NET 10 PR #13532 fix status is uncertain (reported failing in preview 6, no public update after June 2025); the gate in Plan 4-01 is designed to catch this
- Validation architecture: HIGH — test framework and surfaces are all established; gap is test files, not infrastructure
- Security domain: HIGH — threat model is dominated by the well-understood "don't log the password" single concern; delegated crypto, minimal attack surface

**Research date:** 2026-04-12
**Valid until:** 2026-05-12 (30 days for stable areas) / 2026-04-19 (7 days for the .NET 10 leak status — verify at gate)

---

**Note to planner:** CONTEXT.md decisions are user-authoritative; this research does not contradict any of them. One item worth calling out explicitly in Plan 4-03's scope: **D-11's "plain await" decision is correct but subtle**. The `RdpHostControl.ConnectAsync` pattern above relies on `TaskCompletionSource.RunContinuationsAsynchronously` + WPF's default `DispatcherSynchronizationContext` to keep continuations on the STA. If the planner or implementer has any doubt, re-reading RDP-ACTIVEX-PITFALLS §6 "The async/await trap" is mandatory before writing the stages — the three documented patterns there (DANGEROUS, SAFE, SAFEST) map 1:1 to choices the plan must make.
