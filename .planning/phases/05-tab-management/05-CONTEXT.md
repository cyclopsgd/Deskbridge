# Phase 5: Tab Management - Context

**Gathered:** 2026-04-13
**Status:** Ready for planning

<domain>
## Phase Boundary

Convert Phase 4's single-host `ConnectionCoordinator` into a multi-host, tab-keyed model. Introduce a `TabHostManager` service that owns `Dictionary<Guid connectionId, IProtocolHost>`, subscribes to the existing `HostMounted`/`HostUnmounted`/`ReconnectOverlayRequested` events, and publishes `TabOpenedEvent`/`TabClosedEvent`/`TabSwitchedEvent` on the event bus (TAB-05). Migrate the viewport to a persistent multi-host container where every `WindowsFormsHost` is parented once and never re-parented; tab switching flips `Visibility` per host and sets `AdvancedSettings.BitmapPersistence = 0` on the inactive ones (TAB-01, TAB-02, confirms Phase 4 D-12). Wire the tab bar XAML built in Phase 2 to real lifecycle: close runs the disconnect pipeline, middle-click/Ctrl+W/context-menu all use the same path, Ctrl+Tab/Ctrl+Shift+Tab cycle via code-behind `PreviewKeyDown` (TAB-03), plus Ctrl+1..Ctrl+9 / Ctrl+F4 / Ctrl+Shift+T as in-scope extras. Add the 15+ active-session Snackbar warning (TAB-04), per-tab state indicators (connecting spinner, reconnecting badge, error dot), drag-to-reorder tabs, and wire the status bar to the active tab (hostname + state + resolution; latency/quality stubbed for Phase 6).

Out of scope: latency/quality probes, Snackbar notifications on session drops, multi-window tear-off, command palette tab search, hard connection-count cap, duplicate-session-to-same-host. These belong to Phase 6 or v1.1 (see Deferred Ideas).

</domain>

<decisions>
## Implementation Decisions

### Tab ↔ Host Coordination
- **D-01:** New `TabHostManager` singleton in `Deskbridge.Core/Services/` owns `Dictionary<Guid connectionId, IProtocolHost>`. It subscribes to `IConnectionCoordinator.HostMounted`/`HostUnmounted` (the Phase 4 integration seam) to add/remove hosts, and subscribes to `ReconnectOverlayRequested` to drive per-tab overlay state. It publishes `TabOpenedEvent` / `TabClosedEvent` / `TabSwitchedEvent` on `IEventBus`. `ConnectionCoordinator` retains its pipeline-runner role; its single-slot `_active` field and the `(IProtocolHost, ConnectionModel)?` tuple are removed.
- **D-02:** One connection per tab (TAB-01 literal). Double-clicking a connection that is already open publishes a `TabSwitchedEvent` for the existing tab — no second pipeline run. The rapid-double-click guard currently at `ConnectionCoordinator.OnConnectionRequested` lines 82-88 becomes redundant: D-01 makes `TabHostManager` the authority on "is this connection already open?", so the guard is deleted during the refactor (document the deletion in the plan).
- **D-03:** `TabItemViewModel` stores only `ConnectionId` (Guid). Host resolution goes through `TabHostManager.GetHost(connectionId)`. ViewModels stay free of `IProtocolHost` references — matches the Phase 4 D-10 "protocol-agnostic stages" separation and keeps ViewModel tests mock-free of COM abstractions.
- **D-04:** Persistent multi-host container — a named `Grid` (working name `HostContainer`) inside `ViewportGrid`. Every `WindowsFormsHost` is added once on tab open and never removed until tab close. Tab switch toggles `Visibility` per host (active=Visible, others=Collapsed) and sets `AdvancedSettings.BitmapPersistence = 0` on collapsed hosts (TAB-02). Confirms Phase 4 D-12 ("never re-parent"); see WINFORMS-HOST-AIRSPACE.md for why re-parenting after AxHost init tears down HwndSource.
- **D-05:** `ConnectionCoordinator` continues to marshal STA and runs one `IConnectionPipeline` invocation at a time. Concurrent connect requests (e.g., user fuzzy-opens five connections in quick succession) are still serialized through the existing coordinator logic; `TabHostManager` observes the resulting `HostMounted` events in order. No coordinator rework beyond removing the single-slot `_active` tracking, the replacement-on-different-model branch, and the duplicate-click guard (all made redundant by D-01/D-02).

### Tab Close Semantics
- **D-06:** Closing a tab with a live session runs the `IDisconnectPipeline` (same path as Phase 4 shutdown), then removes the WFH from `HostContainer` and publishes `TabClosedEvent`. **No confirmation dialog** on any close path (X button, middle-click, Ctrl+W, Ctrl+F4, context menu). Rationale: enterprise power users disconnect dozens of times a day; friction outweighs mis-click risk, and server-side session persistence covers accidental closes if policy enables it.
- **D-07:** Tab context menu (right-click on tab): **Close** / **Close Others** / **Close All**. No *Duplicate* action (conflicts with D-02 one-per-tab). Close Others and Close All iterate `TabHostManager`'s hosts and call the D-06 close path for each — still no confirmation, but batched into a single UI pass.
- **D-08:** App window close (`MainWindow.OnClosing`) iterates `TabHostManager`'s hosts **sequentially**, each via `IDisconnectPipeline`, then disposes. Matches the Phase 4 `ConnectionCoordinator.Dispose` pattern (`DisconnectAsync().GetAwaiter().GetResult()`), guarantees the 20-cycle GDI baseline survives app exit, and respects the strict disposal ordering from RDP-ACTIVEX-PITFALLS §3. Parallel disconnect is explicitly rejected — STA re-marshal nullifies the parallelism and the risk is not worth the ~500ms win on a 10-tab shutdown.

### 15+ GDI Warning (TAB-04)
- **D-09:** Non-blocking Snackbar via existing `ISnackbarService`. Fires **once** when the active session count crosses from 14 → 15 on a tab open. Does not re-fire for 16, 17, …; if the count drops below 15 and later rises again, warn once more. **No hard cap** — the user has been warned; machine-specific GDI ceilings (HKLM `GDIProcessHandleQuota`) vary too much to pick a safe limit. Connection proceeds regardless of dismiss.
- **D-10:** Warning severity `Caution` (yellow). Text: "15 active sessions reached — performance may degrade beyond this point." Auto-dismiss after the Snackbar default (~6s). Dismissable.

### Tab Visual State
- **D-11:** Tab label = `ConnectionModel.Name` (user-chosen display name) only. Hostname shown in status bar (D-15) and tab tooltip. Keeps the 30px tab bar compact — hostname inclusion was explicitly rejected for horizontal space reasons.
- **D-12:** Per-tab state indicators visible in both active and inactive tabs:
  1. **Connecting spinner** — WPF-UI `ProgressRing` next to title during ConnectStage execution, faded on `ConnectionEstablishedEvent`.
  2. **Reconnecting badge** — small amber dot overlay while `RdpReconnectCoordinator` backoff loop is running for that connection.
  3. **Error dot** — small red dot (use `#F44747` per REFERENCE.md accent) when the tab has entered the manual-reconnect state (auth failure, retry cap hit). Clears when user actions the overlay or reconnects successfully.
  Background tab visibility of these markers is critical — without them, a dropped session in a background tab is invisible.
- **D-13:** **Drag-to-reorder tabs is in scope.** Implementation via ItemsControl drag-drop reordering the `MainWindowViewModel.Tabs` ObservableCollection. Order persists for the app session only (not across restarts; reorder-persistence is v1.1 backlog). Prefer reusing any drag-drop behavior pattern established by the Phase 3 tree view if cleanly applicable; otherwise a small custom attached behavior.
- **D-14:** Background-tab drop UX: when `ReconnectOverlayRequested` fires for an inactive tab, `TabHostManager` sets the tab's state to Reconnecting (which surfaces the D-12 amber badge) and renders the overlay inside that tab's persistent host container. The overlay is only **visible** when the tab is active (because the whole host container — WFH + overlay — is Collapsed on inactive tabs). **No auto-switch** focus to the dropped tab — preserves the user's concentration on whatever session is active. Auto-retry still runs in background; badge clears if reconnect succeeds before the user switches over.

### Status Bar & Shortcuts
- **D-15:** Status bar binds to active tab via `TabSwitchedEvent`. `MainWindowViewModel.StatusText`/`StatusSecondary` are updated when `TabSwitchedEvent` fires. Phase 5 scope:
  - **Hostname** (active tab's `ConnectionModel.Hostname`)
  - **Connection state** ("Connected" / "Connecting…" / "Reconnecting attempt N/20" / "Disconnected")
  - **Session resolution** (read from `IMsRdpClient.DesktopWidth` × `DesktopHeight` on the active host, or fall back to `ConnectionModel.DisplaySettings` if the live value is 0)
  Latency and connection-quality fields are stubbed ("—" or blank). Phase 6 observability work adds the DispatcherTimer probe loop + `IMsRdpClientNonScriptable` / `OnNetworkStatusChanged` wiring.
- **D-16:** Keyboard shortcuts in Phase 5 beyond the TAB-03 requirement (Ctrl+Tab, Ctrl+Shift+Tab, Ctrl+W, middle-click):
  1. **Ctrl+1..Ctrl+9** — jump to tab N. Ctrl+9 jumps to the LAST tab (Chrome / VS Code convention), not literally the ninth. Even with >9 tabs, Ctrl+9 always hits the last.
  2. **Ctrl+F4** — close tab alias (wires to the same `CloseTabCommand`).
  3. **Ctrl+Shift+T** — reopen last closed tab. Backed by a bounded LRU of ~10 recently-closed `ConnectionId` values held in `TabHostManager` (in-memory, session-local, not persisted across app restarts — persistence is v1.1 backlog). Reopen publishes `ConnectionRequestedEvent` for the popped ID.
  All wired via `MainWindow.xaml.cs` `PreviewKeyDown` (Ctrl+Tab cannot be a XAML KeyBinding — WPF intercepts for tab navigation, per the comment at MainWindow.xaml line 17). Phase 6 may move the handling into a global shortcut service; the bindings stay identical.

### Claude's Discretion
- Exact class name (`TabHostManager` vs `TabSessionStore` vs `TabManager`), namespace (`Deskbridge.Core.Services` vs `Deskbridge.Core.Tabs`), and interface name (`ITabHostManager` vs `ITabSessionStore`). Match existing conventions where they exist.
- Structure of the persistent `HostContainer` — plain `Grid` with stacked children, `Panel.SetZIndex` on the active one, custom `HostsPanel`, or a `Grid` with `Visibility` collapses. Any approach works provided WFHs are never removed-and-re-added until tab close.
- Drag-reorder implementation detail — reuse a Phase 3 drag-drop attached behavior if one exists and is cleanly applicable; otherwise a small custom `ItemsControl` drag-reorder behavior in `Deskbridge/Behaviors/`.
- `Ctrl+Shift+T` LRU size (default recommendation: 10). Whether closing a tab replaces an existing LRU entry for the same `ConnectionId` or allows duplicates — recommend dedupe (push to front, remove existing).
- Visual choice for the reconnecting badge (amber ring vs dot vs small `ProgressRing`) and error dot styling. Match existing DynamicResource tokens from DESIGN.md §6 — avoid hardcoded colors except `#F44747` per REFERENCE.md.
- Tab tooltip content — likely hostname + state + resolution, matching the status bar payload. Or just hostname. Pick whichever is consistent with other tooltips in the app.
- Whether `TabSwitchedEvent` carries just the new tab ID or both (previous, new). REFERENCE.md signature is `(Guid? PreviousId, Guid ActiveId)` — follow that.
- Order (100/200/…) conventions for any new disconnect pipeline stages needed for close-all/close-others. None anticipated; the existing disconnect pipeline should be reusable as-is.

### Folded Todos
None — no pending todos matched Phase 5.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements & Roadmap
- `.planning/ROADMAP.md` §Phase 5 — phase goal, success criteria, scope boundary
- `.planning/REQUIREMENTS.md` §Tab Management — TAB-01..TAB-05 locked requirements

### Architecture & UI Spec
- `REFERENCE.md` §Tab Management — one-connection-per-tab, inactive `BitmapPersistence=0`, keyboard shortcuts, subscription to `ConnectionEstablishedEvent`/`ConnectionClosedEvent`
- `REFERENCE.md` §Architecture §Connection Events — canonical `TabOpenedEvent` / `TabClosedEvent` / `TabSwitchedEvent` record shapes
- `REFERENCE.md` §UI Design §Tab bar — height 30px, accent border on active tab, middle-click close, horizontal scroll on overflow
- `REFERENCE.md` §Resource Limits #17, #18 — 15-20 simultaneous session practical limit, `BitmapPersistence = 0` on inactive (TAB-02 / D-04 basis)
- `REFERENCE.md` §Keyboard Shortcuts — Ctrl+W close tab, Ctrl+Tab cycle, Ctrl+F4 not listed but consistent with convention

### WPF-UI / Airspace / RDP Pitfalls
- `WINFORMS-HOST-AIRSPACE.md` — full document. **Mandatory** before touching the multi-host container. The "never re-parent" pattern is the entire foundation of D-04; re-parenting a WFH after AxHost init tears down HwndSource, breaks the RDP session, and can leak GDI.
- `WINFORMS-HOST-AIRSPACE.md` §PerMonitorV2 DPI — informs why inactive tabs keep SmartSizing rather than re-scaling on Visibility changes
- `RDP-ACTIVEX-PITFALLS.md` §2 — GDI handle cliff (mRemoteNG 14-connection crash), basis for TAB-04 warning at 15
- `RDP-ACTIVEX-PITFALLS.md` §3 — strict disposal sequence invoked by `IDisconnectPipeline` during tab close / app shutdown (D-06, D-08)
- `RDP-ACTIVEX-PITFALLS.md` §8 — multi-instance failure thresholds, basis for the "soft warning, no hard cap" decision in D-09
- `WPF-UI-PITFALLS.md` §1 — `ContentDialogHost` (not needed in Phase 5 because we reject modal close confirmation, but flagged so tab-specific dialogs in future can use the correct host)
- `WPF-UI-PITFALLS.md` §5 — Color vs Brush resource keys (applies to tab badges in D-12)
- `WPF-UI-PITFALLS.md` §6 — MVVM command generation (tab Close/CloseOthers/CloseAll commands)

### Design System
- `DESIGN.md` §4 — VS Code-style layout, tab bar in editor column
- `DESIGN.md` §5 — Spacing, sizing conventions (tab height, badge size)
- `DESIGN.md` §6 — DynamicResource color tokens for badges and accent borders

### Prior Phase Context
- `.planning/phases/01-foundation/01-CONTEXT.md` — event bus (`IEventBus.Send<T>` / `Subscribe`), pipeline interfaces (`IConnectionPipeline` / `IDisconnectPipeline`)
- `.planning/phases/02-application-shell/02-CONTEXT.md` — tab bar XAML (D-06, D-07), viewport grid (D-08 empty state), panel layout
- `.planning/phases/03-connection-management/03-CONTEXT.md` — drag-drop behavior patterns (potential reuse for D-13 tab reorder)
- `.planning/phases/04-rdp-integration/04-CONTEXT.md` — D-12 (single-host Phase 4 / multi-host Phase 5), D-13 (AirspaceSwapper), D-07 (reconnect overlay)
- `.planning/phases/04-rdp-integration/04-RESEARCH.md` — WFH parenting, PrintWindow, airspace details

### Existing Code — integration surface
- `src/Deskbridge/MainWindow.xaml` §ViewportGrid (lines 250-269) — current single-child mount; becomes the host of a persistent `HostContainer`
- `src/Deskbridge/MainWindow.xaml` §Tab bar (lines 186-248) — ItemsControl + ScrollViewer + middle-click binding; extend with state-indicator template and context menu
- `src/Deskbridge/MainWindow.xaml` line 17-19 — comment locking Ctrl+Tab / Ctrl+Shift+Tab to code-behind `PreviewKeyDown`
- `src/Deskbridge/MainWindow.xaml.cs` `OnHostMounted`/`OnHostUnmounted`/`OnReconnectOverlayRequested` — refactor to add/remove inside `HostContainer` rather than directly in `ViewportGrid`
- `src/Deskbridge/ViewModels/MainWindowViewModel.cs` `Tabs`/`ActiveTab`/`CloseTabCommand`/`SwitchTabCommand` — placeholder logic; replace with `TabHostManager`-backed commands
- `src/Deskbridge/ViewModels/TabItemViewModel.cs` — add `TabState` enum property (Connecting | Connected | Reconnecting | Error) and bind indicators
- `src/Deskbridge.Core/Services/ConnectionCoordinator.cs` — remove single-slot `_active` and lines 82-88 duplicate-click guard (per D-02)
- `src/Deskbridge.Core/Interfaces/IProtocolHost.cs` — exposes `DisconnectedAfterConnect` event already consumed by reconnect; TabHostManager uses it for state transitions
- `src/Deskbridge.Core/Events/ConnectionEvents.cs` — verify `TabOpenedEvent`/`TabClosedEvent`/`TabSwitchedEvent` records exist (per Phase 1); if not, add them
- `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs` — `HideWithoutSnapshot` per-tab invocation for D-14
- `src/Deskbridge.Protocols.Rdp/RdpHostControl.cs` — `AdvancedSettings.BitmapPersistence` setter for D-04 on Visibility change
- `src/Deskbridge/App.xaml.cs` — register `TabHostManager` as singleton, register any new ViewModels

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `MainWindowViewModel.Tabs` (ObservableCollection<TabItemViewModel>), `ActiveTab`, `CloseTabCommand`, `SwitchTabCommand` — all exist but the command logic is a placeholder and does NOT currently disconnect. Phase 5 replaces the bodies while keeping the XAML bindings intact.
- `TabItemViewModel` (Title, IsActive, ConnectionId) — extend with `TabState` and indicator visibility bindings; do not break the existing API.
- `ConnectionCoordinator.HostMounted` / `HostUnmounted` / `ReconnectOverlayRequested` events — the three integration seams `TabHostManager` subscribes to.
- `IProtocolHost.DisconnectedAfterConnect` — per-host event that drives reconnect state; TabHostManager hooks it alongside coordinator to update tab state.
- `IDisconnectPipeline` — same pipeline used by Phase 4 for single-host shutdown; reused as-is for tab close / close-all / app close.
- `ISnackbarService` (bound at Phase 2) — used for D-09 warning.
- `IEventBus` (`WeakReferenceMessenger`) — TabHostManager publishes `TabOpenedEvent` / `TabClosedEvent` / `TabSwitchedEvent`; status bar / notifications (Phase 6) subscribe.
- `AirspaceSwapper.HideWithoutSnapshot` / `RegisterHost` / `UnregisterHost` — extended to manage per-tab WFH Visibility (currently assumes one active host; refactor to multi-host dict keyed by `HwndSource`).
- `DisconnectReasonClassifier` (Phase 4) — categorizes discReason codes; TabHostManager consumes to decide Reconnecting vs Error badge state.

### Established Patterns
- MVVM via CommunityToolkit.Mvvm: `[ObservableProperty]` partial properties, `[RelayCommand]`. TabHostManager itself is a plain service, not a ViewModel.
- DI in `App.xaml.cs`: `services.AddSingleton<ITabHostManager, TabHostManager>()` alongside the existing `ConnectionCoordinator` singleton.
- Event bus subscription pattern: subscribe in constructor with `this` as the owner, unsubscribe in `Dispose()`. Match `ConnectionCoordinator`'s shape.
- STA affinity: pipelines run on the dispatcher via `Dispatcher.Invoke`/`InvokeAsync` as in Phase 4 D-11. TabHostManager reads/writes its dict from the UI dispatcher; internal locks are not required because all mutations happen on STA.
- Serilog logging via `ILogger<TabHostManager>` injected through DI. Redact nothing — `ConnectionId` is a Guid, hostname is non-sensitive by project policy (passwords alone are redacted per T-01-05).
- xUnit v3 + FluentAssertions + NSubstitute test stack; mock `IEventBus`, `IConnectionCoordinator`, `IDisconnectPipeline`, and `IProtocolHost` for TabHostManager unit tests.

### Integration Points
- **`MainWindow.xaml.cs.OnHostMounted`**: add the WFH to the **persistent `HostContainer`** child of `ViewportGrid`, not directly to `ViewportGrid`. Update layout once per add. The empty-state placeholder remains a direct `ViewportGrid` child.
- **`MainWindow.xaml.cs.OnHostUnmounted`**: remove the WFH from `HostContainer`. Only called on full tab close (not on tab switch) under the D-04 persistence model.
- **`MainWindow.xaml` Tab bar**: extend `DataTemplate` with a `Grid`-stacked badge overlay bound to `TabState`. Add right-click context menu with Close / Close Others / Close All bindings. Ensure drag-drop reorder does not conflict with the existing middle-click close `MouseBinding`.
- **`MainWindowViewModel`**: replace `CloseTab` / `SwitchTab` command bodies to delegate to `TabHostManager`. Keep the `Tabs` / `ActiveTab` surface identical so XAML bindings don't change.
- **`ConnectionCoordinator.OnConnectionRequested`**: remove the rapid-double-click guard (lines 82-88) and the single-host replacement branch (lines 93-104) as part of the D-01 refactor. Add a prior check via `TabHostManager.TryGetExistingTab(connectionId)` to switch-to-existing (D-02) before dispatching to the pipeline.
- **`App.xaml.cs`**: register `TabHostManager` as singleton with its dependencies (`IEventBus`, `IConnectionCoordinator`, `IDisconnectPipeline`, `ILogger`). Register any new ViewModels (drag-drop behavior? probably static attached behavior, no DI needed).
- **Status bar TextBlocks** in `MainWindow.xaml` lines 273-286: already bound to `StatusText` / `StatusSecondary`; no XAML change. The ViewModel sets the fields on `TabSwitchedEvent`.
- **STATE.md**: the Phase 5 blocker ("Keyboard focus management between WPF and WindowsFormsHost needs validation with actual RDP control") clears when the D-04 persistent container + D-16 code-behind shortcuts are validated against multiple live sessions. Note this in the VERIFICATION step.

</code_context>

<specifics>
## Specific Ideas

- D-02's switch-to-existing eliminates `ConnectionCoordinator`'s rapid-double-click guard, but the deletion must be explicit in the refactor task (with a commit message that references this CONTEXT decision) so there's no silent overlap between the old guard and the new TabHostManager check.
- D-04's "never re-parent" constraint is the single most important architectural rule for Phase 5. Any future refactor that tries to add/remove a WFH from `HostContainer` during tab switching re-introduces the airspace class of bugs that WINFORMS-HOST-AIRSPACE.md was written to prevent. Document this in the `HostContainer` class XML doc and `MainWindow.xaml` comment.
- D-09 threshold should be centralized as a single constant in `TabHostManager` (recommend `TabHostManager.GdiWarningThreshold = 15`). Phase 6 may promote this to a user setting; Phase 5 keeps it a hardcoded constant.
- D-12 amber/red badge colors should reuse existing `DynamicResource` tokens when possible (check DESIGN.md §6 for the closest amber — `SolidBackgroundFillColorQuarternary` may not match; `#FFB900` / `SystemAccentColorSecondary` probably does). Red error dot uses `#F44747` per REFERENCE.md accent colours.
- D-14 renders the reconnect overlay inside the host's own container (tab-scoped), not at the `ViewportGrid` level. This means `MainWindow.xaml.cs.OnReconnectOverlayRequested` resolves the target `HostContainer` slot via `TabHostManager.GetHost(req.Connection.Id)` and parents the overlay there. The overlay's visibility therefore follows the tab's Visibility automatically — no extra show/hide on tab switch.
- D-15 status bar resolution read happens **after** `OnLoginComplete` fires on the RDP control — before that, `DesktopWidth/Height` are 0. Fall back to `ConnectionModel.DisplaySettings` width × height during the Connecting state.
- D-16 `Ctrl+Shift+T` LRU: dedupe by `ConnectionId` on push (remove-if-present, then push to front). Prevents spam entries from opening-closing the same tab repeatedly. Cap 10; oldest drops when exceeded.
- Reconnect overlay in a collapsed tab is cheap to construct (tiny WPF panel) but if Plan feels the allocation is wasted, lazy-construct per-tab on first reconnect. Either way, single overlay-per-tab maximum (replace on rapid successive drops — same replacement pattern as Phase 4's `CloseOverlay` path).
- `TabHostManager` naturally lives in `Deskbridge.Core` (matches `ConnectionCoordinator`). ViewModels referencing it stay on the `ITabHostManager` interface, consistent with the Phase 4 D-10 core/RDP boundary.

</specifics>

<deferred>
## Deferred Ideas

### Phase 6 scope
- **Snackbar notification on background session drops** — NOTF-02. Adds a toast on top of D-12's badge for a second channel. Phase 5 implements the badge; Phase 6 adds the toast.
- **Latency & connection-quality status bar probes** — Phase 6 observability. Needs a `DispatcherTimer` + `IMsRdpClientNonScriptable` / `OnNetworkStatusChanged` wiring on the active host, plus a quality-indicator visual.
- **Command palette "Switch to tab X"** — CMD-01 / CMD-02. Fuzzy-searches open tabs; publishes `TabSwitchedEvent`.
- **Hard connection-count cap (configurable)** — if the soft warning in D-09 turns out to be insufficient, a Phase 6 setting could add a cap. For v1, no.
- **Audit stage for tab open/close** — Phase 6 audit log sink subscribes to `TabOpenedEvent` / `TabClosedEvent`.
- **Global keyboard shortcut service** — D-16 wires shortcuts in MainWindow code-behind. Phase 6 may extract into a dedicated `IShortcutService` with user-configurable bindings; the XAML/code-behind shape in Phase 5 must remain compatible with that future refactor.

### v1.1 backlog
- **Multi-window / tear-off tab** — drag a tab out to create a second window hosting that session. Complex HwndSource and airspace work; explicitly out of v1.
- **Tab pinning & order persistence across app restarts** — currently session-local (D-13).
- **Duplicate-session-to-same-host** — rejected in D-07. Reconsider if multi-session-to-same-target workflows surface in feedback.
- **`Ctrl+Shift+T` last-closed LRU persistence** — in-memory only in Phase 5.
- **Drag-tab-to-specific-tab-group / tab groups** — not a v1 concern.

### Explicitly rejected (do not revive silently)
- **Confirmation dialog on tab close** — rejected in D-06. Enterprise users have made this clear in mRemoteNG / RDCMan feedback; don't re-add.
- **Parallel disconnects on app shutdown** — rejected in D-08. STA marshal nullifies the parallelism.
- **Auto-switch focus to a dropped background tab** — rejected in D-14. Intrusive; badge is sufficient.
- **Host dict in MainWindowViewModel** — rejected in D-01. UI layer must not own Core lifecycle state.

### Reviewed Todos (not folded)
None — no pending todos were reviewed.

</deferred>

---

*Phase: 05-tab-management*
*Context gathered: 2026-04-13*
