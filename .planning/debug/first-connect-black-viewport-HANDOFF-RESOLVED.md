# Debug Handoff — First-Connect Black Viewport

**Date:** 2026-04-14
**Severity:** BLOCKER for Phase 5 close
**Context:** Pre-clear handoff so `/gsd-debug` doesn't repeat failed approaches.

---

## Symptom

When the user double-clicks a connection for the very first time after app launch, the RDP viewport shows a **black screen** even though the log confirms `Connection established`. The Ax control appears to be pushing frames to its HWND, but the viewport never displays them.

**Workarounds that DO produce a visible session:**

- Close the tab and immediately re-double-click the same connection → second session displays correctly.
- When a session drops unexpectedly → reconnect overlay appears → click "Reconnect" → displays correctly.

So the Ax + pipeline + connection logic all work. The bug is in how the **first-ever** WindowsFormsHost integrates with the WPF compositor.

## Confirmed-Working Evidence

Latest log excerpt (post-cleanup commit `cb1cb03`):

```
2026-04-14 18:21:04.230 [INF] Connecting to 172.21.1.96
2026-04-14 18:21:04.257 [INF] Credentials resolved for 172.21.1.96
2026-04-14 18:21:04.374 [INF] Connecting to 172.21.1.96:3389 as cyclopsgd
2026-04-14 18:21:06.035 [INF] Connection established for 172.21.1.96
```

Single pipeline. No duplicates. Session established cleanly. Black viewport persists.

## Theories Tried (ALL FAILED — do not repeat)

| # | Commit | Hypothesis | Result |
|---|--------|-----------|--------|
| 1 | `214dd73` | UpdateLayout doesn't drain to Render priority — pump via `Dispatcher.Invoke(() => {}, DispatcherPriority.Render)` | No effect |
| 2 | (same) | Try `DispatcherPriority.Background` for deeper drain | No effect |
| 3 | `b621dc7` | HWND needs explicit WM_PAINT — P/Invoke `RedrawWindow(RDW_INVALIDATE \| RDW_UPDATENOW \| RDW_ALLCHILDREN)` on `_rdp.Handle` after OnLoginComplete | No effect |
| 4 | `638a30e` | Force WPF visual-tree invalidation post-login: `InvalidateMeasure/Arrange/Visual` + `UpdateLayout` on the WFH + its container | No effect (and may have regressed reconnect overlay — later reverted) |
| 5 | `214dd73` | Maybe `Visibility.Hidden` keeps HwndSource alive better than `Collapsed` | Wrong direction — `AirspaceSwapper.WndProc` docs already established Hidden tears down AxHost HWND on some servers (xrdp). Reverted. |

**All five of these have been reverted in commit `cb1cb03`.** Current code has none of these hacks.

## Architectural Facts Established Today

1. **Single pipeline runs.** `_pendingConnects` dedupe (`65d3198`) + `OnHostCreated` duplicate-host reject (`1655a8d`) confirmed via logs. Only ONE Ax + WFH is created per double-click.
2. **`Visibility.Collapsed` is the correct inactive state** for WFH on tab switch (per `AirspaceSwapper.WndProc` research: Hidden tears down the AxHost HWND on xrdp-class servers).
3. **`WindowsFormsHost.Handle != 0` when `ConnectStage` runs** — the Ax control IS sited. The "not sited" guard in `RdpHostControl.ConnectAsync` at line 113 never fires.
4. **`OnLoginComplete` fires** — confirmed by log. `_loginTcs.TrySetResult(true)` completes. Pipeline returns success.
5. **`HostContainer.UpdateLayout()` is called** immediately after `HostContainer.Children.Add(rdp.Host)` in `MainWindow.OnHostMounted`. Measure + Arrange pass happens.
6. **The Ax is generating frames** — if you RESIZE the window, the session DOES appear. So frames are being produced; only the compositor is failing to show them on first mount.

## Likely Root Cause (Unconfirmed)

**WPF airspace composition timing.** On the FIRST-EVER mount of a WindowsFormsHost to a Grid that has never hosted one before, the WPF compositor needs to carve out an "airspace hole" in the render tree for the HWND. This carving appears to happen at a priority/timing that's AFTER `UpdateLayout`, possibly tied to an internal `CompositionTarget.Rendering` pass, and may not complete before the Ax starts pushing frames.

On subsequent mounts, the airspace hole persists (or is re-created faster) — the second WFH benefits from an already-composited region.

## Things That Might Actually Work (Untried)

1. **Delay ConnectStage until `WindowsFormsHost.Loaded` event fires** — wait for the WFH to be fully integrated into the visual tree before kicking off `ConnectAsync`. Currently ConnectStage runs immediately after CreateHostStage completes, which may be before Loaded.

2. **Pre-mount a dummy WindowsFormsHost on window show** — force WPF to establish the airspace composition pattern once, then the first REAL mount benefits from it. Ugly but potentially deterministic.

3. **Programmatic size nudge post-login** — simulate a 1×1-pixel resize of the WFH or window to force airspace re-composition. User reported that manual window resize does display the session, so this might work deterministically.

4. **Force a `CompositionTarget.Rendering` tick** — subscribe to the event, wait for 1-2 frames to render, THEN call `ConnectAsync`. This ensures the compositor has run at least once after the WFH was added.

5. **Deeper investigation with tools** — Spy++ (trace WM_PAINT and WS_CLIPCHILDREN on the Ax HWND), WPF Visualizer (verify airspace region is correctly assigned), ETW trace of DWM composition events.

## Files Involved

- `src/Deskbridge/MainWindow.xaml.cs` — `OnHostMounted` (where WFH is parented)
- `src/Deskbridge/MainWindow.xaml` line 317 — `<Grid x:Name="HostContainer" />` (persistent parent)
- `src/Deskbridge.Protocols.Rdp/RdpHostControl.cs` — `ConnectAsync`, `OnLoginComplete`
- `src/Deskbridge.Core/Pipeline/Stages/ConnectStage.cs` — stage that calls `host.ConnectAsync`
- `src/Deskbridge.Core/Pipeline/Stages/CreateHostStage.cs` — stage that publishes `HostCreatedEvent`
- `src/Deskbridge.Core/Services/ConnectionCoordinator.cs` — `OnHostCreated` → raises `HostMounted`
- `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs` — `RegisterHost` (runs after WFH is parented)

## Reproducible Test Steps

1. Close Deskbridge completely if running.
2. `dotnet run --project src/Deskbridge`
3. Connect tree is populated from prior runs — double-click a previously-working connection (e.g. `172.21.1.96`).
4. Session establishes (status bar shows "Connected"), tab shows connection name, spinner clears.
5. Viewport stays black.

**Known-good recovery:** close the tab and double-click again — second attempt renders correctly.

## Canonical Docs to Read

- `WINFORMS-HOST-AIRSPACE.md` at repo root — airspace architecture, PrintWindow pattern, PerMonitorV2 DPI. Especially §Option 3 (persistent container) and §leak-fix.
- `RDP-ACTIVEX-PITFALLS.md` §1 (siting), §6 (STA thread affinity)
- `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs` comments — established research on Visibility.Hidden vs Collapsed on xrdp.
- `.planning/phases/04-rdp-integration/04-RESEARCH.md` — Phase 4 RDP integration research, pattern 5 (PrintWindow usage)
- `.planning/phases/05-tab-management/05-RESEARCH.md` §Validation Architecture — testing approach

---

*Start debug session with fresh context. Do NOT start from the hotfix attempts above.*
