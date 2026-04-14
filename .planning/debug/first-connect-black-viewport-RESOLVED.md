---
status: resolved
trigger: "first-connect-black-viewport"
created: 2026-04-14T18:51:44Z
updated: 2026-04-14T21:20:00Z
---

## Current Focus

hypothesis: CONFIRMED — ConnectionId was assigned inside ConnectAsync, so it was Guid.Empty at mount time, Tag was stamped Empty, TabSwitchedEvent→SetActiveHostVisibility collapsed the WFH. Fix applied as Option C: make ConnectionId an immutable ctor argument on RdpHostControl and plumb it through the factory interface so it is non-Empty at HostCreatedEvent publish time.
test: User re-repros (cold start → double-click connection). DIAG instrumentation still in place. Expected log shape: post-register Tag= now shows REAL GUID matching ConnId (not 00000000); t+500ms WFH stays Visibility=Visible, IsEnabled=true, non-zero ActualWidth/ActualHeight; viewport renders remote desktop pixels.
expecting: Three DIAG lines ([DIAG/OnHostMounted/post-register], [DIAG/OnLoginComplete], [DIAG/OnHostMounted/t+500ms]) all show a single non-Empty ConnectionId matching across WFH.Tag and host.ConnectionId. Viewport renders.
next_action: Await user repro + pasted DIAG log. If fix holds, follow-up checkpoint removes DIAG instrumentation and commits. If black viewport persists, move to H3/H4/H7 in the remaining hypothesis list.

## Symptoms

expected: After double-clicking a connection for the first time post-launch, the RDP viewport displays the remote desktop.
actual: Viewport renders black on EVERY connect (not just first). Session is alive — status bar shows "Connected" AND the resolution. Session handshake/negotiation completes fully. Pixels never reach the viewport.
errors: None. Connection pipeline reports success. Logs show clean "Connection established".
reproduction: Cold start app. Double-click a previously-working connection. Session establishes. Viewport stays black. Close tab + reopen: STILL BLACK. Window resize: STILL BLACK.
started: During Phase 5 multi-host HostContainer refactor. Did not exist before tabbed multi-session work.

## Eliminated

(All from prior debug session, per HANDOFF table. Do NOT retry these.)

- hypothesis: UpdateLayout doesn't drain to Render priority — pump via Dispatcher.Invoke(() => {}, DispatcherPriority.Render)
  evidence: Commit 214dd73 tried this; no effect on black viewport. Reverted.
  timestamp: 2026-04-14 (prior session)

- hypothesis: DispatcherPriority.Background drain is deeper than Render
  evidence: Same commit 214dd73, tried Background; no effect. Reverted.
  timestamp: 2026-04-14 (prior session)

- hypothesis: HWND needs explicit WM_PAINT — P/Invoke RedrawWindow(RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN) on _rdp.Handle after OnLoginComplete
  evidence: Commit b621dc7 tried this; no effect. Reverted.
  timestamp: 2026-04-14 (prior session)

- hypothesis: Force WPF visual-tree invalidation post-login (InvalidateMeasure/Arrange/Visual + UpdateLayout on the WFH + its container)
  evidence: Commit 638a30e; no effect AND may have regressed reconnect overlay. Reverted.
  timestamp: 2026-04-14 (prior session)

- hypothesis: Visibility.Hidden preserves HwndSource better than Collapsed during tab switch
  evidence: AirspaceSwapper.WndProc research already established Hidden tears down AxHost HWND on xrdp-class servers (discReason=2 / exDiscReasonAPIInitiatedLogoff). Wrong direction. Reverted.
  timestamp: 2026-04-14 (prior session)

- hypothesis: First-mount-only airspace carve-out timing — WPF compositor needs one render-tick after WFH insertion before AxHost pushes frames, fix by waiting for WFH.Loaded or forcing a CompositionTarget.Rendering tick before ConnectAsync.
  evidence: FRESH GROUND-TRUTH CHECKPOINT (2026-04-14T19:25:00Z): user confirms close-and-reopen now also shows black, AND window resize no longer reveals frames. Both workarounds that this hypothesis explained are GONE. The theory predicted "second mount succeeds because airspace region was carved the first time"; reality says second mount also fails. Theory falsified.
  timestamp: 2026-04-14T19:30:00Z

## Evidence

- timestamp: 2026-04-14T18:51:44Z
  checked: HANDOFF doc at .planning/debug/first-connect-black-viewport-HANDOFF.md
  found: Five hypotheses tried and reverted; clean baseline at cb1cb03. Four untried leads documented: (1) wait for WindowsFormsHost.Loaded before ConnectStage, (2) pre-mount dummy WFH on window show, (3) programmatic 1×1 size nudge post-login, (4) CompositionTarget.Rendering tick before ConnectAsync.
  implication: Leads #1-4 all presume "first mount is the problem" — now invalidated.

- timestamp: 2026-04-14T18:51:44Z
  checked: MainWindow.xaml.cs OnHostMounted (lines 129-161)
  found: HostContainer.Children.Add(rdp.Host) -> HostContainer.UpdateLayout() -> AirspaceSwapper.RegisterHost(). All synchronous on STA dispatcher. No wait for Loaded event.
  implication: WFH is added to visual tree, synchronous layout pass runs, then control returns to pipeline -> ConnectStage.ExecuteAsync calls rdp.ConnectAsync.

- timestamp: 2026-04-14T18:51:44Z
  checked: CreateHostStage.cs + ConnectionCoordinator.OnHostCreated (lines 229-276)
  found: CreateHostStage Order=200 publishes HostCreatedEvent synchronously. Coordinator's OnHostCreated raises HostMounted inline. MainWindow.OnHostMounted parents the WFH and calls UpdateLayout. When Publish returns, CreateHostStage completes and pipeline proceeds to ConnectStage Order=300 which calls ConnectAsync.
  implication: Entire siting-layout-connect chain runs inside ONE dispatcher message pump cycle.

- timestamp: 2026-04-14T18:51:44Z
  checked: RdpHostControl constructor (lines 93-112)
  found: Constructor creates WFH and AxHost, sets _host.Child = _rdp IMMEDIATELY in ctor — BEFORE the WFH is parented. Comment confirms this is deliberate for siting. ConnectAsync guards against Handle==0 at line 121.
  implication: By the time ConnectAsync runs, _rdp.Handle != 0 (confirmed in handoff). Siting is correct.

- timestamp: 2026-04-14T18:51:44Z
  checked: HostContainer XAML (MainWindow.xaml line 317)
  found: `<Grid x:Name="HostContainer" />` — persistent, declared at design time. Not created dynamically. No explicit Background (transparent).
  implication: The Grid EXISTS from window load. Its own HwndSource is established long before the first WFH is mounted.

- timestamp: 2026-04-14T19:30:00Z
  checked: FRESH GROUND TRUTH from user reproduction at HEAD (cb1cb03)
  found: (A) First-connect black — YES, still reproduces. BUT the status bar shows "Connected" AND the resolution, so connection and negotiation succeed. (B) Close tab and reopen — ALSO BLACK. (C) Window edge resize — does NOT reveal frames. (D) Nothing else noticed.
  implication: This is a major drift from the handoff. The class of bug has changed: it is no longer "first mount only" — it is "every mount". The previously-reliable workarounds are gone. Fundamental cause must be something that affects every WFH mount in HostContainer, not just the first-ever one.

- timestamp: 2026-04-14T19:30:00Z
  checked: Recent commit graph (git log --oneline -15) since handoff was written
  found: HEAD == cb1cb03 (same as handoff). No new Deskbridge commits. 52f6dfa is the handoff doc commit. So the code did not change between handoff-time and now. What changed is the user's reproduction: previously close-and-reopen worked; now it doesn't.
  implication: Either (a) the handoff's claim that close-and-reopen works was wrong / stale / environmental, or (b) the environment changed (Windows Update, RDP server state, DPI, monitor config) between handoff-time repro and fresh repro. Either way, the actual repro ground truth is "every connect is black" — we investigate from THAT, not from the handoff's claims.

- timestamp: 2026-04-14T19:30:00Z
  checked: WindowsFormsHost Background in RdpHostControl ctor (line 103) vs HostContainer Background (MainWindow.xaml 317)
  found: _host = new WindowsFormsHost { Background = Brushes.Black }. HostContainer Grid has no explicit Background (transparent). The parent Border (MainWindow.xaml 294) has Background="{DynamicResource ApplicationBackgroundBrush}" which is a dark near-black.
  implication: A black viewport is indistinguishable between "WFH background showing through because airspace carved but Ax rendered nothing" AND "WPF compositor is drawing on top of the WFH because no airspace carve-out occurred". We need to distinguish these. Diagnostic must detect whether the WFH HWND is actually on-screen and client rect matches the allocated layout rect.

- timestamp: 2026-04-14T20:05:00Z
  checked: Three-site diagnostic instrumentation landed and compiled cleanly
  found: Build succeeded 0W/0E. Sites: (1) MainWindow.OnHostMounted after _airspace.RegisterHost → LogHwndDiagnostics("OnHostMounted/post-register", rdp). (2) MainWindow.OnHostMounted Dispatcher.BeginInvoke + 500ms Task.Delay → LogHwndDiagnostics("OnHostMounted/t+500ms", rdp). (3) RdpHostControl.OnLoginComplete after _loginTcs.TrySetResult → _logger.LogInformation "[DIAG/OnLoginComplete] Handle= DesktopWidth= DesktopHeight= IsConnected= ConnId=". Helper captures parent-chain GetParent loop, AxTracesBackToWindow flag, GetClientRect/GetWindowRect, WFH ActualWidth/Height/IsVisible/Visibility/IsEnabled/Tag, HostContainer ActualWidth/Height/IsVisible/Visibility/Children.Count + per-child type/Tag/Visibility/IsEnabled, Window ActualWidth/Height/IsVisible + PresentationSource/RootVisual null-checks. All Serilog. All wrapped in per-section try/catch so a failure of one field does not suppress the others. Marked with "DIAG (first-connect-black-viewport)" comments for later grep-and-remove. Unstaged.
  implication: Next ground-truth comes from the user's log file. Expected to reveal H1/H2/H3/H5/H6 as confirmed or eliminated in one repro.

- timestamp: 2026-04-14T20:45:00Z
  checked: User-provided repro log (172.21.1.96 first connect). Three DIAG lines captured as designed.
  found: (A) post-register: ConnId=00000000-0000-0000-0000-000000000000, WFH Tag=00000000-0000-0000-0000-000000000000, WFH Visible/Enabled, ActualWidth=824 ActualHeight=616, parent chain traces back to window, GetClientRect=824x616. Host is CORRECTLY sited. (B) OnLoginComplete (+1.6s later): Handle=0x19815AC, DesktopWidth=1920, DesktopHeight=1080, IsConnected=true, ConnId="cf7c375f-ac6c-40a5-85c7-c0c887b01c7a" — real GUID. (C) t+500ms: ConnId now reads cf7c375f... (rdp.ConnectionId property latched late, via ConnectAsync), but WFH Tag is STILL 00000000-0000-0000-0000-000000000000, Visibility=Collapsed, IsEnabled=False, ActualWidth=0, ActualHeight=0.
  implication: CONFIRMED: WFH Tag was stamped with Guid.Empty at mount time (because host.ConnectionId was Guid.Empty until ConnectAsync). Tag never updates because `rdp.Host.Tag = host.ConnectionId` runs ONCE at mount in MainWindow.OnHostMounted line 148. When TabHostManager.OnHostCreated publishes TabSwitchedEvent(previous, realGuid), MainWindow.OnTabSwitched -> SetActiveHostVisibility(realGuid) walks HostContainer.Children and collapses every WFH whose Tag != realGuid. Our only WFH has Tag=Empty, so it gets Collapsed + IsEnabled=false. The black viewport is the Grid/Border background showing through the collapsed (zero-size) WFH.

- timestamp: 2026-04-14T20:50:00Z
  checked: Code path that sets ConnectionId. RdpHostControl.cs line 44: `public Guid ConnectionId { get; private set; }`. Line 117 (inside ConnectAsync): `ConnectionId = context.Connection.Id;`. Nowhere else assigned.
  found: The only place ConnectionId becomes non-Empty is INSIDE ConnectAsync. Factory (RdpProtocolHostFactory.Create) returns `new RdpHostControl(logger)` — no ConnectionId. CreateHostStage calls factory then publishes HostCreatedEvent — ConnectionId still Empty. Coordinator.OnHostCreated stores (Host, Model) then calls HostMounted?.Invoke — ConnectionId still Empty when MainWindow.OnHostMounted runs. ConnectStage (Order=300) is the first place ConnectAsync is invoked, which sets ConnectionId.
  implication: Fix must assign ConnectionId BEFORE HostMounted fires. Smallest fix: set it in CreateHostStage after factory.Create and before bus.Publish. One new line. No pipeline reorder, no interface change, no constructor change.

- timestamp: 2026-04-14T20:50:00Z
  checked: Downstream dependency on the Empty-Tag: MainWindow.OnHostMounted line 148 (`rdp.Host.Tag = host.ConnectionId`), SetActiveHostVisibility lines 354-374 (`wfh.Tag is Guid id && id == activeId`), TabHostManager.OnHostCreated line 336 (`_bus.Publish(new TabSwitchedEvent(previous, evt.Connection.Id))`). Also OnTabClosedSync line 395 (`wfh.Tag is Guid id && id == evt.ConnectionId`).
  found: Tag comparison is required for multi-tab routing. If Tag is Guid.Empty for any WFH, it NEVER matches any tab's active id → stays permanently Collapsed. This is the exact mechanism producing the black viewport. The architecture is correct; only the TIMING of ConnectionId assignment is wrong.
  implication: Setting ConnectionId one step earlier (in CreateHostStage) propagates to all these sites automatically — no other changes needed.

## Hypotheses to investigate (new, derived from fresh ground truth)

H1. **AxHost HWND is not a descendant of the Window's HwndSource.** Something about the mount path leaves _rdp.Handle in a state where GetParent() doesn't trace back to the window. Possible if the WFH's own internal HwndSource is disconnected from the WPF window tree (airspace region is per-HwndSource; orphaned HwndSource = no carve).

H2. **AxHost HWND exists and is parented correctly but has a 0-width or 0-height client rect.** Layout claims the Grid has size X×Y, but the WFH's hosted HWND was SetWindowPos'd to 0×0. Possible if HostContainer's own actualWidth/Height is 0 at mount time, or if IsEnabled=false + Visibility races.

H3. **WFH HWND is z-order'd UNDER another HWND.** Some sibling HWND in the viewport covers the WFH. Could be a prior WFH that wasn't removed, the AirspaceSwapper's snapshot overlay, or a FluentWindow backdrop HWND at the wrong z-order. Each HostContainer.Children.Add adds a new WFH — check if stale WFHs survive.

H4. **AxHost pushes frames to a back-buffer that is never presented.** SmartSizing + DesktopWidth/Height apply AFTER connect begins; if the control negotiates at one size and the HWND is later resized, the internal bitmap may be drawn to a region that WPF's airspace region clips out. Mitigation: capture Ax ClientRect vs internal DesktopWidth/Height ratio.

H5. **HostContainer.IsEnabled is false or HostContainer.IsVisible is false at the moment of mount.** Parent controls' state could be collapsing the grid before WFH gets its first size message. Check HostContainer.ActualWidth/Height and IsVisible at OnLoginComplete + 500ms.

H6. **The fire-and-forget close refactor (cb1cb03) broke host lifecycle** — the user-facing repro pattern is "open tab A, close it, open tab A" and the close path's fire-and-forget disconnect races the next open. _userInitiatedClose flag suppresses DisconnectedAfterConnect, but does something else in the refactor leave a dead WFH parented or a dead HwndSource in the airspace swapper's dict? Check if HostContainer.Children.Count is > 1 at mount time.

H7. **WPF-UI FluentWindow Mica backdrop regression in .NET 10** — Mica requires transparent content and DWM composition. If the backdrop is clipping the viewport, the WFH's airspace hole may be painted over. dotnet/wpf #10044 (logged in WINFORMS-HOST-AIRSPACE.md) says Fluent themes break WFH rendering in .NET 9. This is a .NET 10 app — check whether disabling Mica (WindowBackdropType="None" at XAML 10) changes the symptom.

## Resolution

root_cause: `IProtocolHost.ConnectionId` is assigned inside `RdpHostControl.ConnectAsync` (line 117), which runs in `ConnectStage` (Order=300). But `HostCreatedEvent` is published in `CreateHostStage` (Order=200) BEFORE ConnectAsync — and the coordinator's `OnHostCreated` handler synchronously raises `HostMounted`, which MainWindow handles by stamping `rdp.Host.Tag = host.ConnectionId`. At that moment `ConnectionId` is still `Guid.Empty` (default). Moments later `TabHostManager.OnHostCreated` publishes `TabSwitchedEvent(previous, realGuid)`, MainWindow's `OnTabSwitched` calls `SetActiveHostVisibility(realGuid)`, which walks `HostContainer.Children` and collapses every WFH whose `Tag != realGuid`. The sole WFH has `Tag=Guid.Empty`, so it's collapsed (`Visibility=Collapsed`, `IsEnabled=false`, `ActualWidth=ActualHeight=0`). The remote session is alive and negotiated, but its HWND is zero-sized and the Grid/Border background shows through — perceived as a black viewport.
fix: Applied Option C (immutable ConnectionId plumbed through factory). (1) `IProtocolHostFactory.Create` signature changed to `Create(Protocol protocol, Guid connectionId)`. (2) `RdpProtocolHostFactory.Create` forwards `connectionId` into `new RdpHostControl(logger, connectionId)`. (3) `RdpHostControl` ctor now takes `Guid connectionId` and assigns it to `ConnectionId { get; }` (immutable — setter removed). (4) The `ConnectionId = context.Connection.Id` line was removed from `ConnectAsync` (previously line 117). (5) `CreateHostStage` now passes `ctx.Connection.Id` into the factory call. Test sites updated: `CreateHostStageTests` (3 substitute setups + 1 verification), `ConnectionPipelineIntegrationTests` (1 substitute setup), `ErrorIsolationTests` (1 direct ctor), `RdpHostControlShapeTests` (4 direct ctors). `TabHostManagerTests` untouched — it stubs `IProtocolHost` directly via `Substitute.For<IProtocolHost>()` + `.ConnectionId.Returns(id)`, which still works since NSubstitute handles get-only properties. DIAG instrumentation left in place for verification repro.
verification: `dotnet build` clean: 0 warnings, 0 errors across Deskbridge.Core, Deskbridge.Protocols.Rdp, Deskbridge main app, Deskbridge.Tests. User confirmed fix works ("perfect") in real repro — first connect renders remote desktop pixels, not black. DIAG instrumentation stripped from MainWindow.xaml.cs (LogHwndDiagnostics helper, RECT struct, GetParent/GetClientRect/GetWindowRect P/Invokes, the two OnHostMounted DIAG call-sites, and the diag-only usings System.Runtime.InteropServices/System.Windows.Interop/System.Windows.Media) and from RdpHostControl.cs (the [DIAG/OnLoginComplete] try/catch block). Post-strip build: 0W/0E.
files_changed:
  - src/Deskbridge.Core/Interfaces/IProtocolHostFactory.cs
  - src/Deskbridge.Protocols.Rdp/RdpProtocolHostFactory.cs
  - src/Deskbridge.Protocols.Rdp/RdpHostControl.cs
  - src/Deskbridge.Core/Pipeline/Stages/CreateHostStage.cs
  - tests/Deskbridge.Tests/Pipeline/CreateHostStageTests.cs
  - tests/Deskbridge.Tests/Pipeline/ConnectionPipelineIntegrationTests.cs
  - tests/Deskbridge.Tests/Rdp/ErrorIsolationTests.cs
  - tests/Deskbridge.Tests/Rdp/RdpHostControlShapeTests.cs
