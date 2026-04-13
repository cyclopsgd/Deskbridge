# Phase 5: Tab Management - Research

**Researched:** 2026-04-13
**Domain:** Multi-host WindowsFormsHost coexistence, WPF keyboard routing around ActiveX, ItemsControl drag-reorder, tab lifecycle service (TabHostManager)
**Confidence:** HIGH on primitives (Visibility toggle, GDI mitigation, disposal) / MEDIUM-HIGH on keyboard routing / HIGH on drag-reorder (Phase 3 pattern reusable)

## Summary

Phase 5 is primarily a **coordination refactor**, not a new-technology phase. The dangerous COM primitives are already proven by Phase 4: single-host siting/disposal, WFH leak fixes, AirspaceSwapper, reconnect flow. Phase 5 converts the single-slot `_active` field in `ConnectionCoordinator` into a dict-keyed multi-host service (`TabHostManager`), moves every `WindowsFormsHost` into a persistent `HostContainer` where they live forever until tab close, and toggles `Visibility` on tab switch — the "Option 3" pattern from WINFORMS-HOST-AIRSPACE.md §"Three architectural options." Roughly **80% of Phase 5 is plumbing**. The remaining 20% is four non-obvious items that the planner must get right.

**The four load-bearing items:**

1. **BitmapPeristence on inactive tabs is NOT a runtime toggle.** [VERIFIED: Microsoft Learn docs — IMsRdpClientAdvancedSettings properties "cannot be set when the control is connected, unless otherwise indicated"](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings-interface). It is already set to 0 on every connection at `RdpConnectionConfigurator.Apply` (Phase 4 line 47). TAB-02 is **already satisfied by Phase 4's configure-time defaults** — the Phase 5 work is `Visibility.Collapsed` on inactive WFHs so Windows stops painting them, plus a small `IsEnabled = false` flip so hotkeys don't land on background controls (WINFORMS-HOST-AIRSPACE.md §397). The CONTEXT.md D-04 phrasing "sets BitmapPersistence = 0 on collapsed hosts (TAB-02)" should be read as "this setting is already 0; we rely on that to keep inactive-tab GDI low" — not as runtime mutation. Any plan task that writes `rdp.AdvancedSettings9.BitmapPeristence = 0` on tab switch MUST be rejected.

2. **Ctrl+Tab cannot be reliably caught with `PreviewKeyDown` while the RDP ActiveX has focus.** [CITED: RDP-ACTIVEX-PITFALLS §5 lines 270-282] The RDP control installs a `WH_KEYBOARD_LL` hook when it has focus with `KeyboardHookMode != 0`. Phase 4 sets `KeyboardHookMode = 0` (verified in `RdpConnectionConfigurator.cs:48`), which disables that hook — so **`PreviewKeyDown` on the FluentWindow will fire for Ctrl+Tab when an RDP session is focused, provided `KeyboardHookMode=0`**. D-16 and UI-SPEC line 233 lock code-behind `PreviewKeyDown` as the Phase 5 mechanism. The pitfalls doc's `RegisterHotKey` + `HwndSource.AddHook` approach is deferred to Phase 6 per the D-16 note "Phase 6 may move the handling into a global shortcut service." Phase 5 must **ship with a verification checkpoint** that Ctrl+Tab works while an RDP session is focused — this is the blocker STATE.md §Blockers line 109 references. If it fails, Phase 6 brings forward the `RegisterHotKey` path without waiting.

3. **Drag-reorder on an `ItemsControl` reuses the Phase 3 drag-drop idioms but needs a new `TabReorderBehavior` class.** `TreeViewDragDropBehavior` is TreeView-specific (TreeViewItem header hit-testing, insertion/into/after positions). Tabs are a flat horizontal list, so the Phase 5 behavior is smaller: 2-position drop (before/after — no "into"), insertion adorner is a 2px vertical line (UI-SPEC line 108), drag threshold `3× SystemParameters.MinimumHorizontalDragDistance` per Phase 3 convention, no root-drop adorner. The behavior mutates `MainWindowViewModel.Tabs` ObservableCollection via `Move(oldIndex, newIndex)` (built-in; does not re-instantiate VMs, so RdpHostControl state survives).

4. **Close Others / Close All iteration must snapshot the dict before iterating.** STA dispatcher guarantees no concurrent mutation, but each per-tab `IDisconnectPipeline.DisconnectAsync` awaits — and during that await, `OnDisconnectedAfterConnect` or a stale `HostMountedEvent` may mutate `TabHostManager._hosts`. Snapshot `_hosts.Values.ToList()` at the top of Close Others / Close All, then iterate the snapshot. The test must verify that a reconnect overlay firing mid-close-all does not cause an `InvalidOperationException: Collection was modified`.

**Primary recommendation:** Build `TabHostManager` as a thin singleton in `Deskbridge.Core/Services/` that owns `Dictionary<Guid, IProtocolHost>`, subscribes to existing `HostMounted`/`HostUnmounted`/`ReconnectOverlayRequested` events, and publishes `TabOpenedEvent`/`TabClosedEvent`/`TabSwitchedEvent`. Replace `MainWindow.OnHostMounted` so the WFH mounts inside a new `HostContainer` Grid child of `ViewportGrid`. Every tab switch calls `TabHostManager.SwitchTo(connectionId)` which flips one `Visibility.Visible` + all others `Collapsed`, pokes `IsEnabled=false` on background hosts to defeat keyboard hooks, and publishes `TabSwitchedEvent`. The persistent-container pattern is literally copied from WINFORMS-HOST-AIRSPACE.md §Option 3 (lines 376-431). Every other Phase 5 item (drag reorder, state indicators, shortcuts, Close Others/All, LRU) is a small straightforward addition on top.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Tab ↔ Host Coordination**
- **D-01:** New `TabHostManager` singleton in `Deskbridge.Core/Services/` owns `Dictionary<Guid connectionId, IProtocolHost>`. Subscribes to `IConnectionCoordinator.HostMounted`/`HostUnmounted` (Phase 4 seam) and `ReconnectOverlayRequested`. Publishes `TabOpenedEvent` / `TabClosedEvent` / `TabSwitchedEvent` on `IEventBus`. `ConnectionCoordinator` retains pipeline-runner role; single-slot `_active` field and `(IProtocolHost, ConnectionModel)?` tuple removed.
- **D-02:** One connection per tab (TAB-01 literal). Double-clicking an already-open connection publishes `TabSwitchedEvent` for the existing tab — no second pipeline run. Duplicate-double-click guard at `ConnectionCoordinator.OnConnectionRequested` lines 82-88 is deleted (TabHostManager becomes the authority).
- **D-03:** `TabItemViewModel` stores only `ConnectionId` (Guid). Host resolution via `TabHostManager.GetHost(connectionId)`. ViewModels stay mock-free of COM abstractions.
- **D-04:** Persistent multi-host container — `Grid x:Name="HostContainer"` inside `ViewportGrid`. Every `WindowsFormsHost` added once on tab open, never removed until tab close. Tab switch toggles `Visibility` per host (active=Visible, others=Collapsed) and sets `AdvancedSettings.BitmapPersistence = 0` on collapsed hosts (TAB-02). Confirms Phase 4 D-12.
- **D-05:** `ConnectionCoordinator` continues to marshal STA and runs one `IConnectionPipeline` invocation at a time. No coordinator rework beyond removing `_active`, the replacement-on-different-model branch, and the duplicate-click guard.

**Tab Close Semantics**
- **D-06:** Closing a tab with a live session runs `IDisconnectPipeline`, removes WFH from `HostContainer`, publishes `TabClosedEvent`. **No confirmation dialog** on any close path.
- **D-07:** Tab context menu: **Close / Close Others / Close All**. No Duplicate action. Close Others/All iterate TabHostManager hosts sequentially.
- **D-08:** App window close iterates hosts **sequentially** via `IDisconnectPipeline`. Parallel disconnect rejected (STA re-marshal nullifies parallelism).

**15+ GDI Warning (TAB-04)**
- **D-09:** Non-blocking Snackbar via existing `ISnackbarService`. Fires **once** when active session count crosses from 14 → 15. No re-fire for 16+. Re-arms when count drops below 15 and rises again. **No hard cap.**
- **D-10:** Severity `Caution` (yellow). Text: "15 active sessions reached — performance may degrade beyond this point." WPF-UI default ~6s timeout. Dismissable.

**Tab Visual State**
- **D-11:** Tab label = `ConnectionModel.Name` only. Hostname in status bar and tooltip.
- **D-12:** Per-tab state indicators (Connecting spinner / Reconnecting amber dot / Error red dot) visible in BOTH active and inactive tabs. Background visibility critical.
- **D-13:** **Drag-to-reorder tabs in scope.** ItemsControl drag-drop reordering `MainWindowViewModel.Tabs` ObservableCollection. Session-only persistence.
- **D-14:** Background-tab drop UX: `ReconnectOverlayRequested` for inactive tab → TabHostManager sets Reconnecting state (amber badge) + renders overlay inside that tab's persistent host container. Overlay only visible when tab active. **No auto-switch focus.**

**Status Bar & Shortcuts**
- **D-15:** Status bar binds to active tab via `TabSwitchedEvent`. Phase 5 scope: Hostname, Connection state, Session resolution (`IMsRdpClient.DesktopWidth × DesktopHeight` or fallback `ConnectionModel.DisplaySettings`). Latency/quality stubbed as `—`.
- **D-16:** Phase 5 shortcuts beyond TAB-03:
  1. **Ctrl+1..Ctrl+9** — jump to tab N. Ctrl+9 = LAST tab (Chrome/VS Code convention).
  2. **Ctrl+F4** — close tab alias.
  3. **Ctrl+Shift+T** — reopen last closed tab (bounded LRU ~10, dedupe by ConnectionId).
  All wired via `MainWindow.xaml.cs` `PreviewKeyDown`. Phase 6 may move to global shortcut service.

### Claude's Discretion

- Exact class name (`TabHostManager` vs alternatives), namespace, interface name. Match existing conventions.
- Structure of persistent `HostContainer` — plain Grid with stacked children, `Panel.SetZIndex`, or Grid with Visibility collapses. Any approach works provided WFHs are never removed-and-re-added until tab close.
- Drag-reorder implementation — reuse Phase 3 behavior patterns if cleanly applicable; otherwise small custom behavior in `Deskbridge/Behaviors/`.
- `Ctrl+Shift+T` LRU size (default 10). Dedupe recommended.
- Visual choice for Reconnecting badge / Error dot styling (UI-SPEC locks specifics: 8px Ellipse, `DeskbridgeWarningBrush` / `DeskbridgeErrorBrush`).
- Tab tooltip content (UI-SPEC locks: hostname · state, varies per state).
- `TabSwitchedEvent` signature: `(Guid? PreviousId, Guid ActiveId)` per REFERENCE.md.
- Order conventions for any new pipeline stages. None anticipated.

### Deferred Ideas (OUT OF SCOPE)

**Phase 6:**
- Snackbar on background session drops (NOTF-02). Phase 5 ships badge only.
- Latency / connection-quality probes. Phase 5 stubs `—`.
- Command palette "Switch to tab X" (CMD-01/02).
- Hard connection-count cap.
- Audit stage for tab open/close.
- Global keyboard shortcut service.

**v1.1 backlog:**
- Multi-window / tear-off tab.
- Tab pinning & order persistence across restarts.
- Duplicate-session-to-same-host.
- `Ctrl+Shift+T` LRU persistence.
- Drag-tab-to-tab-groups.

**Explicitly rejected (do not revive silently):**
- Confirmation dialog on tab close (D-06).
- Parallel disconnects on app shutdown (D-08).
- Auto-switch focus to a dropped background tab (D-14).
- Host dict in `MainWindowViewModel` (D-01 — UI layer must not own Core lifecycle state).
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| TAB-01 | One connection per tab, active tab only renders live ActiveX control | D-04 persistent container pattern from WINFORMS-HOST-AIRSPACE §Option 3. `Visibility.Collapsed` on background WFH stops Win32 paint messages (WINFORMS-HOST-AIRSPACE line 433). `IsEnabled=false` on background prevents hotkey capture (WINFORMS-HOST-AIRSPACE line 397). |
| TAB-02 | Inactive tabs set BitmapPersistence = 0 to reduce GDI handle usage | **Already satisfied at Phase 4 connect time** — `RdpConnectionConfigurator.cs:47` sets `BitmapPeristence = 0` on every connection. The Microsoft docs confirm this property cannot be set while connected. Phase 5 relies on Phase 4's initial setting; the TAB-02 intent ("reduce GDI on inactive") is achieved by `Visibility.Collapsed` suppressing paint + the already-zero BitmapPeristence suppressing cache growth. See Common Pitfalls §1 for the resolution. |
| TAB-03 | Ctrl+Tab/Ctrl+Shift+Tab cycle, Ctrl+W close, middle-click close | D-16 code-behind `PreviewKeyDown`. Ctrl+W already bound in `MainWindow.xaml:15` as XAML KeyBinding. Middle-click already bound in tab template (`MainWindow.xaml:216`). KeyboardHookMode=0 (Phase 4 default) lets PreviewKeyDown fire while RDP is focused — see Common Pitfalls §2. |
| TAB-04 | Warning at 15+ simultaneous connections | D-09/D-10 Snackbar with Caution severity. Threshold constant `TabHostManager.GdiWarningThreshold = 15`. Fire-once-per-crossing logic (transition from `< 15 → ≥ 15`) — see Architecture Patterns §5. |
| TAB-05 | Tab opened on ConnectionEstablishedEvent, closed on ConnectionClosedEvent | TabHostManager subscribes to `HostMounted` (raises `TabOpenedEvent`) and `HostUnmounted` (raises `TabClosedEvent`). Status bar and future notifications subscribe to `TabSwitchedEvent`. Canonical signatures in REFERENCE.md:293-295: `record TabOpenedEvent(Guid ConnectionId)`, `record TabClosedEvent(Guid ConnectionId)`, `record TabSwitchedEvent(Guid? PreviousId, Guid ActiveId)`. |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

Hard rules from the project root CLAUDE.md. Plans MUST comply.

**Framework & stack (locked — same as Phase 4):**
- .NET 10 LTS (net10.0-windows) with C# 14 — no .NET 8/9
- WPF-UI 4.2.0 Fluent dark theme; all colours via `DynamicResource` tokens
- CommunityToolkit.Mvvm 8.4.2+ (MVVM source generators)
- Microsoft.Extensions.DependencyInjection 10.0.x
- System.Text.Json for serialization
- xUnit v3 + FluentAssertions + NSubstitute

**Do NOT use (same as Phase 4):**
- `[GeneratedComInterface]`, `Marshal.ReleaseComObject` manually, `SecureString`, `BinaryFormatter`, WPF-UI 3.x.

**Additional Phase 5 constraints (from CONTEXT.md restated here):**
- **Never re-parent a `WindowsFormsHost` between tab switches** (D-04 + WINFORMS-HOST-AIRSPACE "Option 2 — avoid"). Only `Visibility` changes once a host is parented to `HostContainer`.
- **Never set `BitmapPeristence` after the control is connected** (Microsoft API rule). Phase 4 sets it to 0 at configure time; do not touch it in Phase 5.
- **No modal dialogs on any close path** (D-06). Enterprise UX lock-in.
- **No hardcoded colours** except the three inherited brush definitions in `App.xaml`.
- **UseWindowsForms** remains only in `Deskbridge.Protocols.Rdp.csproj` — no change.

**Mandatory reading:**
- `WINFORMS-HOST-AIRSPACE.md` — full document. The Option 3 "persistent container, Visibility toggle" pattern is the foundation of this phase.
- `RDP-ACTIVEX-PITFALLS.md` §3 (disposal sequence — unchanged from Phase 4), §5 (keyboard hooks — validates the `KeyboardHookMode=0 + PreviewKeyDown` Phase 5 approach), §8 (multi-instance GDI — informs D-09 threshold).
- `WPF-UI-PITFALLS.md` §5 (Color vs Brush resource keys for the badges), §6 (MVVM command generation).
- `REFERENCE.md` §Tab Management + §Architecture §Connection Events (canonical `TabOpenedEvent`/`TabClosedEvent`/`TabSwitchedEvent` records).
- Phase 4's `04-RESEARCH.md` — airspace helper, WFH leak fixes, disposal sequence all carry forward unchanged.

## Standard Stack

### Core (no new packages — all existing)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.WindowsDesktop.App.Wpf | 10.0.x (framework ref) | WPF runtime | [CITED: CLAUDE.md] .NET 10 LTS locked |
| Microsoft.WindowsDesktop.App.WindowsForms | 10.0.x (framework ref) | `WindowsFormsHost`, `AxHost` | [CITED: Phase 4 already references via UseWindowsForms] |
| CommunityToolkit.Mvvm | 8.4.2+ | `[ObservableProperty]` for `TabItemViewModel.TabState`, ViewModels, `[RelayCommand]` for Close/CloseOthers/CloseAll | [CITED: CLAUDE.md] Project convention |
| Microsoft.Extensions.DependencyInjection | 10.0.x | Register `ITabHostManager` as singleton | [CITED: App.xaml.cs existing composition] |
| Microsoft.Extensions.Logging | 10.0.x | `ILogger<TabHostManager>` | Phase 3/4 established pattern |
| Serilog | 4.3.1 | Structured logs | Phase 3/4 established pattern |
| WPF-UI | 4.2.0 | `ProgressRing`, `Snackbar`, `ContextMenu`, `Button`, `SymbolIcon` | [CITED: CLAUDE.md + UI-SPEC §Design System] |
| xUnit v3 / FluentAssertions / NSubstitute | (test stack) | Tests | Phase 1 established |

### Supporting / already-wired

| Library | Used For | Notes |
|---------|----------|-------|
| `ISnackbarService` (WPF-UI) | D-09 15-session warning | Already bound at Phase 2; `ISnackbarService.Show(title, message, ControlAppearance.Caution, icon, timeout)` |
| `IEventBus` (WeakReferenceMessenger) | Publish Tab events, subscribe to Host events | Pattern from `ConnectionCoordinator` — same shape for `TabHostManager` |
| `IConnectionCoordinator.HostMounted` / `HostUnmounted` / `ReconnectOverlayRequested` | Integration seams | Already emit — TabHostManager subscribes |
| `IDisconnectPipeline` | Tab close / Close Others / Close All / app close | Reused as-is (D-08) |
| `DisconnectReasonClassifier` | Decide Reconnecting vs Error state | Classify discReason → if in `Authentication`/`Licensing`/retry-cap, flip to Error; else Reconnecting |

### Alternatives Considered

| Instead of | Could Use | Tradeoff | Why Not |
|------------|-----------|----------|---------|
| Custom `TabReorderBehavior` | `GongSolutions.WPF.DragDrop` NuGet | Mature, battle-tested | [VERIFIED: NuGet scan] Adds a dependency for ~100 lines of reorder logic. Phase 3 built its own `TreeViewDragDropBehavior` for comparable reasons (drop-indicator styling, Move vs Copy semantics). Consistency + zero-new-dependency wins. |
| `ItemsControl` | `TabControl` | Built-in tab UX | [CITED: UI-SPEC §Layout Dimensions + Phase 2 existing XAML] Project deliberately chose `ItemsControl` + `StackPanel` for custom 30px height, custom accent, overflow scroll. `TabControl` ties visuals to theme defaults we don't want. Keep the existing. |
| `HwndSource.AddHook` + `RegisterHotKey` | `PreviewKeyDown` | Works even with KeyboardHookMode=1 | [CITED: D-16 + RDP-ACTIVEX-PITFALLS §5] Phase 4 sets `KeyboardHookMode=0` which disables the RDP hook. `PreviewKeyDown` is sufficient for Phase 5. Phase 6 may add `RegisterHotKey` as a fallback when the command palette needs global shortcuts. |
| Central `TabSwitchedEvent` → all subscribers | Direct ViewModel property bindings | Simpler XAML | [CITED: REFERENCE.md §Architecture] Event-bus decouples: status bar, future notifications, audit log subscribe without holding TabHostManager reference. |
| `Dictionary<Guid, IProtocolHost>` in `MainWindowViewModel` | Core service | One less file | [REJECTED: D-01] UI must not own Core lifecycle. ViewModel unit tests stay COM-free. |

**Installation:** No new packages. No `Directory.Packages.props` change. No `.csproj` change.

**Version verification:** All packages pinned at the project level via Phase 1 Central Package Management. No floating versions resolved in this phase.

## Architecture Patterns

### Recommended Folder Layout

```
src/
├── Deskbridge.Core/
│   ├── Services/
│   │   ├── TabHostManager.cs          # NEW — D-01 singleton, Dictionary<Guid, IProtocolHost>
│   │   └── TabSwitchedEvent.cs        # (may already exist per REFERENCE.md — verify / add)
│   ├── Interfaces/
│   │   └── ITabHostManager.cs         # NEW — GetHost, TryGetTab, SwitchTo, CloseAll, ReopenLastClosed
│   └── Events/
│       └── ConnectionEvents.cs        # EDIT (if TabOpenedEvent/TabClosedEvent/TabSwitchedEvent not yet present)
├── Deskbridge.Protocols.Rdp/
│   └── (no new files — existing AirspaceSwapper / RdpHostControl are reused unchanged)
├── Deskbridge/
│   ├── Behaviors/
│   │   └── TabReorderBehavior.cs      # NEW — D-13 attached property + drag-drop + insertion adorner
│   ├── ViewModels/
│   │   ├── TabItemViewModel.cs        # EDIT — add TabState enum + bindings for ProgressRing/Ellipse visibility
│   │   └── MainWindowViewModel.cs     # EDIT — replace CloseTab/SwitchTab bodies; add CloseOthers/CloseAll/ReopenLastClosed commands
│   ├── Models/
│   │   └── TabState.cs                # NEW — enum: Connecting, Connected, Reconnecting, Error
│   ├── MainWindow.xaml                # EDIT — add HostContainer grid, tab DataTemplate indicators, ContextMenu, drag behavior attached
│   ├── MainWindow.xaml.cs             # EDIT — replace OnHostMounted/OnHostUnmounted to parent under HostContainer; PreviewKeyDown for shortcuts
│   └── App.xaml.cs                    # EDIT — register ITabHostManager singleton
tests/
└── Deskbridge.Tests/
    ├── Tabs/
    │   ├── TabHostManagerTests.cs     # NEW — open, close, switch, 15+ threshold, LRU, close-others iteration safety
    │   └── TabItemViewModelTests.cs   # NEW — TabState transitions
    └── Integration/
        └── KeyboardShortcutTests.cs   # NEW — STA harness; PreviewKeyDown Ctrl+Tab / Ctrl+1..9 / Ctrl+Shift+T routing
```

### Pattern 1: TabHostManager shape (resolves Focus Area #3)

**Decision:** Plain C# class in `Deskbridge.Core.Services`, singleton DI registration, no WPF dependencies, keyed on `ConnectionId` (Guid — per D-02 one-per-connection, the Connection.Id IS the tab identity).

```csharp
// File: Deskbridge.Core/Services/TabHostManager.cs
// Source: synthesis of D-01..D-03 + WINFORMS-HOST-AIRSPACE §Option 3 + REFERENCE.md §Tab Management
namespace Deskbridge.Core.Services;

public interface ITabHostManager
{
    /// <summary>Host for a given connection id, or null if no tab is open.</summary>
    IProtocolHost? GetHost(Guid connectionId);

    /// <summary>True if a tab exists for the connection (used by D-02 switch-to-existing check).</summary>
    bool TryGetExistingTab(Guid connectionId, out IProtocolHost host);

    /// <summary>Current live tab count (drives D-09 15+ threshold).</summary>
    int ActiveCount { get; }

    /// <summary>All known hosts (for MainWindow Close All and app shutdown).</summary>
    IReadOnlyCollection<IProtocolHost> AllHosts { get; }

    /// <summary>Switch focus to an existing tab. Publishes TabSwitchedEvent.</summary>
    void SwitchTo(Guid connectionId);

    /// <summary>Close a single tab. Runs IDisconnectPipeline, removes from dict, publishes TabClosedEvent.</summary>
    Task CloseTabAsync(Guid connectionId);

    /// <summary>Close every tab except the specified one. D-07.</summary>
    Task CloseOthersAsync(Guid keepConnectionId);

    /// <summary>Close every tab. D-07 context menu + D-08 app shutdown.</summary>
    Task CloseAllAsync();

    /// <summary>Pop the most-recently-closed connection id. Null if LRU empty (D-16 Ctrl+Shift+T).</summary>
    Guid? PopLastClosed();
}

public sealed class TabHostManager : ITabHostManager, IDisposable
{
    public const int GdiWarningThreshold = 15;      // D-09 centralized constant
    public const int LastClosedLruCapacity = 10;    // D-16 bounded LRU

    private readonly Dictionary<Guid, IProtocolHost> _hosts = new();
    private readonly Dictionary<Guid, ConnectionModel> _connections = new();
    private readonly LinkedList<Guid> _lastClosedLru = new();

    private readonly IEventBus _bus;
    private readonly IConnectionCoordinator _coordinator;
    private readonly IDisconnectPipeline _disconnect;
    private readonly ISnackbarService _snackbar;          // D-09 warning
    private readonly Dispatcher _dispatcher;              // STA marshal (mirror ConnectionCoordinator pattern)
    private readonly ILogger<TabHostManager> _logger;
    private Guid? _activeId;
    private bool _warned15;
    private bool _disposed;

    public TabHostManager(
        IEventBus bus,
        IConnectionCoordinator coordinator,
        IDisconnectPipeline disconnect,
        ISnackbarService snackbar,
        ILogger<TabHostManager> logger,
        Dispatcher? dispatcher = null)
    {
        _bus = bus;
        _coordinator = coordinator;
        _disconnect = disconnect;
        _snackbar = snackbar;
        _logger = logger;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;

        _coordinator.HostMounted += OnHostMounted;
        _coordinator.HostUnmounted += OnHostUnmounted;
        _coordinator.ReconnectOverlayRequested += OnReconnectOverlayRequested;
    }

    public int ActiveCount => _hosts.Count;
    public IReadOnlyCollection<IProtocolHost> AllHosts => _hosts.Values;
    public IProtocolHost? GetHost(Guid id) => _hosts.TryGetValue(id, out var h) ? h : null;

    public bool TryGetExistingTab(Guid id, out IProtocolHost host)
    {
        return _hosts.TryGetValue(id, out host!);
    }

    private void OnHostMounted(object? sender, IProtocolHost host)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => OnHostMounted(sender, host));
            return;
        }

        // The Phase 4 coordinator fires HostMounted on HostCreatedEvent — so MainWindow
        // has parented the WFH into HostContainer. All we do is remember the host and
        // publish TabOpenedEvent. MainWindow's OnHostMounted handler also flips Visibility.
        _hosts[host.ConnectionId] = host;
        var previous = _activeId;
        _activeId = host.ConnectionId;

        _bus.Publish(new TabOpenedEvent(host.ConnectionId));
        _bus.Publish(new TabSwitchedEvent(previous, host.ConnectionId));

        // D-09: fire-once-per-crossing warning
        if (!_warned15 && _hosts.Count == GdiWarningThreshold)
        {
            _warned15 = true;
            _snackbar.Show(
                "Approaching session limit",
                "15 active sessions reached — performance may degrade beyond this point.",
                ControlAppearance.Caution,
                new SymbolIcon(SymbolRegular.Warning24),
                TimeSpan.FromSeconds(6));
        }
    }

    private void OnHostUnmounted(object? sender, IProtocolHost host)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => OnHostUnmounted(sender, host));
            return;
        }

        _hosts.Remove(host.ConnectionId);

        // D-09 re-arm: if count drops below threshold, let a future crossing warn again
        if (_hosts.Count < GdiWarningThreshold) _warned15 = false;

        // D-16: push to LRU with dedupe
        PushLru(host.ConnectionId);

        _bus.Publish(new TabClosedEvent(host.ConnectionId));

        // Auto-activate a neighbor if we just closed the active tab (matches mRemoteNG UX)
        if (_activeId == host.ConnectionId)
        {
            var next = _hosts.Keys.LastOrDefault();      // Last-added = right-most tab
            var previous = _activeId;
            _activeId = next == Guid.Empty ? null : next;
            _bus.Publish(new TabSwitchedEvent(previous, _activeId ?? Guid.Empty));
        }
    }

    public void SwitchTo(Guid connectionId)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.Invoke(() => SwitchTo(connectionId)); return; }
        if (!_hosts.ContainsKey(connectionId)) return;       // Silent no-op for stale clicks
        if (_activeId == connectionId) return;

        var previous = _activeId;
        _activeId = connectionId;
        _bus.Publish(new TabSwitchedEvent(previous, connectionId));
    }

    public async Task CloseTabAsync(Guid id)
    {
        if (!_hosts.TryGetValue(id, out var host)) return;

        // Run disconnect pipeline — same path as Phase 4 shutdown (D-06)
        var ctx = new DisconnectContext
        {
            Connection = _connections[id],
            Host = host,
            Reason = DisconnectReason.UserInitiated,
        };
        try { await _disconnect.DisconnectAsync(ctx); }
        catch (Exception ex)
        {
            _logger.LogError(
                "Disconnect for {ConnectionId} threw: {ExceptionType} HResult={HResult:X8}",
                id, ex.GetType().Name, ex.HResult);
        }
        // OnHostUnmounted fires via coordinator; _hosts.Remove happens there.
    }

    public async Task CloseOthersAsync(Guid keep)
    {
        // D-07 + CONTEXT.md Specifics: SNAPSHOT the keys before iterating. STA marshal prevents
        // concurrent mutation but each DisconnectAsync await can re-enter OnHostUnmounted /
        // OnDisconnectedAfterConnect, which mutates _hosts mid-iteration.
        var targets = _hosts.Keys.Where(k => k != keep).ToList();
        foreach (var id in targets)
        {
            await CloseTabAsync(id);
        }
    }

    public async Task CloseAllAsync()
    {
        var targets = _hosts.Keys.ToList();
        foreach (var id in targets)
        {
            await CloseTabAsync(id);
        }
    }

    public Guid? PopLastClosed()
    {
        if (_lastClosedLru.Count == 0) return null;
        var first = _lastClosedLru.First!.Value;
        _lastClosedLru.RemoveFirst();
        return first;
    }

    private void PushLru(Guid id)
    {
        // D-16 dedupe-on-push + cap at 10
        _lastClosedLru.Remove(id);          // O(n) on a 10-entry list — fine
        _lastClosedLru.AddFirst(id);
        while (_lastClosedLru.Count > LastClosedLruCapacity)
            _lastClosedLru.RemoveLast();
    }

    private void OnReconnectOverlayRequested(object? sender, ReconnectUiRequest req)
    {
        // D-14: set TabState=Reconnecting so the amber badge surfaces even if the tab is background.
        // The actual overlay parenting happens in MainWindow (WPF types live there) — this handler
        // just raises a state-change event that the TabItemViewModel binds to.
        // NOTE: this subscription is a thin pass-through; it does not own the UI state.
        // Let MainWindow's ReconnectOverlayRequested handler drive the overlay per existing
        // Phase 4 code; the TabState change is published alongside via a separate event.
        _bus.Publish(new TabStateChangedEvent(req.Connection.Id, TabState.Reconnecting));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coordinator.HostMounted -= OnHostMounted;
        _coordinator.HostUnmounted -= OnHostUnmounted;
        _coordinator.ReconnectOverlayRequested -= OnReconnectOverlayRequested;
    }
}
```

**Key design choices:**
- **Dispatcher injectable** like `ConnectionCoordinator` — makes STA unit testing trivial (tests inject the STA-runner dispatcher).
- **Does NOT manage the visual tree directly.** MainWindow retains `OnHostMounted` / `OnHostUnmounted` handlers for the WFH parenting work; TabHostManager just tracks the dict and publishes events. Keeps WPF types out of Core (D-10 boundary).
- **`_connections` dict** kept in sync alongside `_hosts`: the coordinator's `HostCreatedEvent` carries `ConnectionModel` + `IProtocolHost`; TabHostManager subscribes to `HostCreatedEvent` (same event the coordinator uses) to populate `_connections[id] = evt.Connection`. Needed so `CloseTabAsync` can build a `DisconnectContext` without walking back to the store.
- **Event ordering:** `HostMounted` fires from `ConnectionCoordinator.OnHostCreated` BEFORE `ConnectStage` runs (Phase 4 WR-01 fix — lines 174-211 of the existing coordinator). TabHostManager's subscription happens after the coordinator's — so by the time TabHostManager sees `HostMounted`, MainWindow has already parented the WFH into `HostContainer`. This is the integration seam.

### Pattern 2: Persistent HostContainer layout (resolves Focus Area #1, #2)

**Decision:** A named `Grid` inside `ViewportGrid`. Every mounted WFH is a direct child. Tab switch = flip `Visibility` across all children. `IsEnabled=false` on backgrounded hosts (WINFORMS-HOST-AIRSPACE line 397 — prevents hotkey capture on hidden controls).

**XAML (replaces the current `ViewportGrid` body in `MainWindow.xaml`):**

```xml
<Border Grid.Row="1" Background="{DynamicResource ApplicationBackgroundBrush}">
    <Grid x:Name="ViewportGrid">
        <!-- Empty-state placeholder (Phase 2 — unchanged) -->
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center"
            Visibility="{Binding HasNoTabs, Converter={StaticResource BoolToVisibility}}">
            <TextBlock Text="Deskbridge" FontSize="20" FontWeight="SemiBold"
                Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                HorizontalAlignment="Center" />
            <TextBlock Text="Ctrl+N to create a connection" FontSize="14"
                Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                HorizontalAlignment="Center" Margin="0,8,0,0" />
        </StackPanel>
        <!-- Airspace snapshot (Phase 4 — unchanged) -->
        <Image x:Name="ViewportSnapshot" Stretch="Fill" Visibility="Collapsed" />
        <!-- Phase 5 persistent container — every WFH parented here once, never removed until tab close. -->
        <Grid x:Name="HostContainer" />
    </Grid>
</Border>
```

**Code-behind — replacement for `OnHostMounted` / `OnHostUnmounted`:**

```csharp
// MainWindow.xaml.cs — Phase 5 revision
private void OnHostMounted(object? sender, IProtocolHost host)
{
    if (host is not RdpHostControl rdp) return;
    Dispatcher.Invoke(() =>
    {
        // Close any open reconnect overlay (a successful new mount means a reconnect succeeded OR
        // a fresh tab opened — either way the overlay for THIS tab should clear).
        CloseOverlayFor(host.ConnectionId);

        // D-04: add to persistent HostContainer, not ViewportGrid. Fire the layout pass so the
        // AxHost HWND is realized before ConnectStage runs (Phase 4 siting-order guarantee).
        HostContainer.Children.Add(rdp.Host);
        HostContainer.UpdateLayout();

        // D-04 Visibility on tab switch — new mount becomes active, backgrounds everything else.
        SetActiveHostVisibility(host.ConnectionId);

        _airspace.RegisterHost(rdp.Host, ViewportSnapshot);
    });
}

private void OnHostUnmounted(object? sender, IProtocolHost host)
{
    if (host is not RdpHostControl rdp) return;
    Dispatcher.Invoke(() =>
    {
        _airspace.UnregisterHost(rdp.Host);
        HostContainer.Children.Remove(rdp.Host);      // ONLY removal path — tab close.
        // TabHostManager raises TabClosedEvent + auto-switches to neighbor;
        // SetActiveHostVisibility is triggered by the subsequent TabSwitchedEvent handler.
    });
}

private void OnTabSwitched(TabSwitchedEvent evt)
{
    // Subscribed via _bus in MainWindow constructor. Marshal to dispatcher if needed.
    Dispatcher.Invoke(() => SetActiveHostVisibility(evt.ActiveId));
}

private void SetActiveHostVisibility(Guid activeId)
{
    foreach (var child in HostContainer.Children.OfType<WindowsFormsHost>())
    {
        var isActive = child.Tag is Guid id && id == activeId;
        child.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        child.IsEnabled = isActive;    // WINFORMS-HOST-AIRSPACE line 397 — prevents hotkey capture on hidden WFH
    }
}
```

**Important subtleties:**
- The `child.Tag = connectionId` assignment happens in `OnHostMounted` (or equivalently, `MainWindow` iterates `HostContainer.Children` and matches on host identity — equivalent). Using `Tag` is the simplest way to correlate `WindowsFormsHost` back to `ConnectionId` without a parallel dict.
- `WindowsFormsHost.Visibility = Collapsed` keeps the child Win32 HWND alive (it is NOT destroyed). [CITED: AirspaceSwapper.cs:135 comment] `Hidden` has been observed to cause xrdp servers to see an API-initiated disconnect; use **`Collapsed` always** on background tabs.
- `_airspace.RegisterHost` is called once per host. It stores `Dictionary<WFH, Image>`; multi-host is supported — see Pattern 4.
- Phase 4's `WR-02` defense-in-depth loop (MainWindow.xaml.cs:93-99 that removes all WFH children from ViewportGrid before adding a new one) must be **removed** in Phase 5 because it conflicts with D-04 "never re-parent". In its place, the `HostContainer.Children.Remove` in `OnHostUnmounted` is the only path.

### Pattern 3: Tab keyboard shortcuts (resolves Focus Area #4)

**Decision:** Code-behind `PreviewKeyDown` at the `FluentWindow` root. `KeyboardHookMode = 0` (set by Phase 4 `RdpConnectionConfigurator`) means WPF's keyboard routing is not bypassed by the RDP control.

```csharp
// MainWindow.xaml.cs — Phase 5 addition
protected override void OnPreviewKeyDown(KeyEventArgs e)
{
    var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
    var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

    // Ctrl+Tab / Ctrl+Shift+Tab: cycle tabs (TAB-03)
    if (ctrl && e.Key == Key.Tab)
    {
        CycleTab(forward: !shift);
        e.Handled = true;
        return;
    }

    // Ctrl+F4: alias for Close Tab (D-16)
    if (ctrl && e.Key == Key.F4)
    {
        ViewModel.CloseTabCommand.Execute(ViewModel.ActiveTab);
        e.Handled = true;
        return;
    }

    // Ctrl+1..Ctrl+8: jump to tab N. Ctrl+9: jump to LAST tab (D-16 Chrome convention)
    if (ctrl && e.Key >= Key.D1 && e.Key <= Key.D9 && !shift)
    {
        var idx = e.Key - Key.D1;                        // 0-based
        if (e.Key == Key.D9) idx = ViewModel.Tabs.Count - 1;   // Ctrl+9 = last
        if (idx >= 0 && idx < ViewModel.Tabs.Count)
        {
            ViewModel.SwitchTabCommand.Execute(ViewModel.Tabs[idx]);
            e.Handled = true;
        }
        return;
    }

    // Ctrl+Shift+T: reopen last closed tab (D-16 LRU)
    if (ctrl && shift && e.Key == Key.T)
    {
        var id = _tabHostManager.PopLastClosed();
        if (id is Guid connId)
        {
            var connection = _connectionStore.GetById(connId);
            if (connection is not null)
            {
                _eventBus.Publish(new ConnectionRequestedEvent(connection));
            }
            // If connection was deleted from store — silent, no toast (UI-SPEC line 345)
        }
        e.Handled = true;
        return;
    }

    base.OnPreviewKeyDown(e);
}

private void CycleTab(bool forward)
{
    if (ViewModel.Tabs.Count == 0) return;
    var currentIndex = ViewModel.ActiveTab is null
        ? -1
        : ViewModel.Tabs.IndexOf(ViewModel.ActiveTab);
    var nextIndex = forward
        ? (currentIndex + 1) % ViewModel.Tabs.Count
        : (currentIndex - 1 + ViewModel.Tabs.Count) % ViewModel.Tabs.Count;
    ViewModel.SwitchTabCommand.Execute(ViewModel.Tabs[nextIndex]);
}
```

**Why this works with RDP focused:**
- [CITED: RDP-ACTIVEX-PITFALLS §5 lines 277-281] `KeyboardHookMode` value table:
  | Value | Behavior |
  |---|---|
  | 0 | Keys stay local — RDP does NOT install its keyboard hook |
  | 1 | All key combinations sent to remote |
  | 2 | Keys sent to remote only in full-screen mode (default) |
- Phase 4's `RdpConnectionConfigurator.cs:48` sets `KeyboardHookMode = 0` on every connection. The low-level `WH_KEYBOARD_LL` hook is never installed. WPF's normal `Preview*` event routing sees the Ctrl+Tab press.
- The WindowsFormsHost does still receive focus when the user clicks into the RDP viewport, but `PreviewKeyDown` on the `FluentWindow` root **tunnels DOWN** before reaching the WFH's internal message pump — provided the host is focusable, which it is by default. `e.Handled = true` prevents the key from ever reaching the WFH child.
- **Verification checkpoint (blocks phase completion):** One of the UAT items must be "Open two RDP sessions, click into the RDP viewport of session A, press Ctrl+Tab — verify session B is activated (not a TAB character sent to the remote host)." If this fails, Phase 6's `RegisterHotKey` path is pulled forward.

**Ctrl+W already bound as XAML `KeyBinding`** (MainWindow.xaml:15). It continues to work because KeyBindings bubble through the main window input pipeline BEFORE the WFH receives. Keep it.

### Pattern 4: AirspaceSwapper multi-host refactor (resolves Focus Area #2)

**Decision:** `AirspaceSwapper` already supports multiple hosts in its `Dictionary<WindowsFormsHost, Image> _hosts` (verified: `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs:35`). Phase 5 calls `RegisterHost` once per mounted RDP session. On `WM_ENTERSIZEMOVE` the WndProc hook already iterates **all** registered hosts and snapshots/collapses each one (line 121 `foreach (var (host, overlay) in _hosts)`). This is correct for Phase 5 out of the box — **no changes needed to AirspaceSwapper**.

**However, one subtle issue:** on drag-resize, *all* hosts collapse to their snapshot. For background tabs (already `Visibility.Collapsed` by the persistent-container rule) this is a no-op restore path — when `WM_EXITSIZEMOVE` fires, the handler sets `host.Visibility = Visibility.Visible` on every host, which breaks the "only one visible" rule.

**Fix:** `WM_EXITSIZEMOVE` handler in `AirspaceSwapper.WndProc` currently (line 142-147) unconditionally sets `host.Visibility = Visibility.Visible`. For multi-host Phase 5, it must restore each host's *pre-drag* visibility. This is a ~6-line change inside `AirspaceSwapper.cs`:

```csharp
// AirspaceSwapper.cs — replace the WM_EXITSIZEMOVE branch
else if (msg == WM_EXITSIZEMOVE && _inSizeMove)
{
    _inSizeMove = false;
    foreach (var (host, overlay) in _hosts)
    {
        host.Visibility = _preDragVisibility[host];    // Restore captured state, not force Visible
        overlay.Visibility = Visibility.Collapsed;
        overlay.Source = null;
    }
    _preDragVisibility.Clear();
}
```

And in the `WM_ENTERSIZEMOVE` branch, capture visibility before collapsing:

```csharp
// AirspaceSwapper.cs — enhance WM_ENTERSIZEMOVE
if (msg == WM_ENTERSIZEMOVE && !_inSizeMove)
{
    _inSizeMove = true;
    _preDragVisibility = _hosts.Keys.ToDictionary(h => h, h => h.Visibility);
    // ... existing snapshot logic ...
    host.Visibility = Visibility.Collapsed;
}
```

Add `private Dictionary<WindowsFormsHost, Visibility>? _preDragVisibility;` field. This matches the existing `RestoreVisibilityToken` pattern (AirspaceSwapper.cs:200-221) which already does the right thing for `HideWithoutSnapshot`.

**HideWithoutSnapshot for per-tab reconnect overlay (D-14):**
- MainWindow's existing `OnReconnectOverlayRequested` calls `_airspace.HideWithoutSnapshot(rdp.Host)` which returns an `IDisposable` token. The token correctly restores the host's pre-hide Visibility on Dispose (verified at AirspaceSwapper.cs:205-220, WR-03 fix).
- For Phase 5, the overlay is **parented inside the tab's host slot** (D-14), not at ViewportGrid level. Since the host slot itself follows the tab's Collapsed/Visible state, the overlay automatically disappears when the tab is inactive. We still need `HideWithoutSnapshot` for the *active* tab case: when reconnect fires on the currently-focused tab, the WFH collapses so the overlay isn't covered by the Win32 airspace.
- Per-tab overlay rendering means we track `Dictionary<Guid, ReconnectOverlay>` (in MainWindow.xaml.cs, not Core) so we can correlate `OnReconnectOverlayRequested` → the correct host slot.

### Pattern 5: 15+ Snackbar threshold logic (resolves D-09)

**Decision:** Single `bool _warned15` flag on TabHostManager. Flips to true on crossing 14→15, resets to false when count drops below 15. Re-arm on subsequent re-crossing.

**Centralized constant:** `public const int GdiWarningThreshold = 15;` on `TabHostManager`. Phase 6 may promote to a user setting; Phase 5 leaves as const.

**No persistence.** Process-lifetime only. Acceptable because the warning is advisory, not a hard limit.

```csharp
// Inside TabHostManager.OnHostMounted — after _hosts[id] = host
if (!_warned15 && _hosts.Count == GdiWarningThreshold)
{
    _warned15 = true;
    _snackbar.Show(
        "Approaching session limit",
        "15 active sessions reached — performance may degrade beyond this point.",
        ControlAppearance.Caution,
        new SymbolIcon(SymbolRegular.Warning24),
        TimeSpan.FromSeconds(6));
}

// Inside OnHostUnmounted
if (_hosts.Count < GdiWarningThreshold) _warned15 = false;   // Re-arm
```

**Why `ControlAppearance.Caution` (yellow):** [CITED: UI-SPEC line 191 + WPF-UI Snackbar palette] Caution is the yellow/amber severity. `Warning` isn't a WPF-UI Snackbar appearance; `Caution` is the correct enum value. Verify via `Wpf.Ui.Controls.ControlAppearance` — the enum members are `Primary, Secondary, Info, Success, Caution, Danger, Light, Dark, Transparent`.

### Pattern 6: Drag-reorder tab behavior (resolves Focus Area #5)

**Decision:** New `Deskbridge.Behaviors.TabReorderBehavior` attached property. Smaller than `TreeViewDragDropBehavior` because tabs are a flat horizontal list. Uses `DragDrop.DoDragDrop` + `AdornerLayer` 2px vertical line (UI-SPEC line 108).

```csharp
// File: src/Deskbridge/Behaviors/TabReorderBehavior.cs
// Source: simplified from TreeViewDragDropBehavior pattern
namespace Deskbridge.Behaviors;

public static class TabReorderBehavior
{
    private const string DragDataFormat = "DeskbridgeTabItem";
    private static readonly double DragThresholdX = SystemParameters.MinimumHorizontalDragDistance * 3;
    private static Point _startPoint;
    private static bool _dragPending;
    private static TabInsertionAdorner? _adorner;

    public static readonly DependencyProperty EnableReorderProperty = DependencyProperty.RegisterAttached(
        "EnableReorder", typeof(bool), typeof(TabReorderBehavior),
        new PropertyMetadata(false, OnChanged));

    public static bool GetEnableReorder(DependencyObject d) => (bool)d.GetValue(EnableReorderProperty);
    public static void SetEnableReorder(DependencyObject d, bool v) => d.SetValue(EnableReorderProperty, v);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl ic) return;
        if ((bool)e.NewValue)
        {
            ic.AllowDrop = true;
            ic.PreviewMouseLeftButtonDown += OnPreviewDown;
            ic.PreviewMouseMove += OnPreviewMove;
            ic.PreviewMouseLeftButtonUp += OnPreviewUp;
            ic.DragOver += OnDragOver;
            ic.Drop += OnDrop;
            ic.DragLeave += OnDragLeave;
        }
        // else: unregister
    }

    private static void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ItemsControl ic) return;
        if (FindAncestor<FrameworkElement>(e.OriginalSource as DependencyObject) is not { DataContext: TabItemViewModel })
            return;
        _startPoint = e.GetPosition(ic);
        _dragPending = true;
    }

    private static void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ItemsControl ic) return;
        if (Math.Abs((e.GetPosition(ic) - _startPoint).X) < DragThresholdX) return;
        _dragPending = false;

        var container = FindAncestor<ContentPresenter>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not TabItemViewModel tab) return;

        var data = new DataObject();
        data.SetData(DragDataFormat, tab);
        DragDrop.DoDragDrop(ic, data, DragDropEffects.Move);
        ClearAdorner();
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragDataFormat)) { e.Effects = DragDropEffects.None; return; }
        if (sender is not ItemsControl ic) return;

        var targetContainer = FindAncestor<FrameworkElement>(e.OriginalSource as DependencyObject);
        if (targetContainer?.DataContext is not TabItemViewModel) return;

        e.Effects = DragDropEffects.Move;

        // Left half of target = insert before; right half = insert after
        var pos = e.GetPosition(targetContainer);
        var insertBefore = pos.X < targetContainer.ActualWidth / 2.0;
        ShowInsertionAdorner(targetContainer, insertBefore);
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        ClearAdorner();
        if (e.Data.GetData(DragDataFormat) is not TabItemViewModel dragged) return;
        if (sender is not ItemsControl ic) return;
        if (ic.DataContext is not MainWindowViewModel vm) return;
        var targetContainer = FindAncestor<FrameworkElement>(e.OriginalSource as DependencyObject);
        if (targetContainer?.DataContext is not TabItemViewModel target) return;

        var pos = e.GetPosition(targetContainer);
        var insertBefore = pos.X < targetContainer.ActualWidth / 2.0;
        var oldIndex = vm.Tabs.IndexOf(dragged);
        var newIndex = vm.Tabs.IndexOf(target);
        if (!insertBefore) newIndex++;
        if (oldIndex < newIndex) newIndex--;        // Adjust for self-removal
        if (oldIndex == newIndex || newIndex < 0 || newIndex >= vm.Tabs.Count) return;

        // ObservableCollection.Move triggers MoveItem — does not re-instantiate VMs,
        // so the bound WFH in HostContainer is untouched. Critical for D-04 "never re-parent".
        vm.Tabs.Move(oldIndex, newIndex);
        e.Handled = true;
    }

    private static void OnDragLeave(object sender, DragEventArgs e) => ClearAdorner();
    private static void OnPreviewUp(object sender, MouseButtonEventArgs e) => _dragPending = false;
    // ... FindAncestor + ShowInsertionAdorner + ClearAdorner (similar to TreeViewDragDropBehavior)
}
```

**Conflict check with existing MouseBinding (MainWindow.xaml:215-222):**
- Middle-click MouseBinding: fires on `MouseDown` of the middle button. Drag behavior uses `PreviewMouseLeftButtonDown`. No conflict.
- Left-click MouseBinding (switch tab): fires on the `Click` gesture which maps to mouse-up. Drag pending flag only commits on `PreviewMouseMove` past threshold. If user releases without moving (simple click), `_dragPending = false` on `PreviewMouseLeftButtonUp` and the MouseBinding Click fires normally. **No conflict.**
- Risk to validate in UAT: rapid click-drag-back-to-start could cancel drag without the click firing. Low priority; matches Phase 3 tree drag-drop semantics.

**Applied in XAML:**
```xml
<ItemsControl ItemsSource="{Binding Tabs}"
    behaviors:TabReorderBehavior.EnableReorder="True">
    <!-- existing items panel + template -->
</ItemsControl>
```

### Pattern 7: Per-tab reconnect overlay placement (resolves Focus Area #7)

**Decision:** Overlay is parented inside the tab's slot in `HostContainer`. `MainWindow.xaml.cs` maintains `Dictionary<Guid, ReconnectOverlay>` so `OnReconnectOverlayRequested` routes to the correct slot.

**Shape (replaces current OnReconnectOverlayRequested in MainWindow.xaml.cs):**

```csharp
private readonly Dictionary<Guid, (ReconnectOverlay Control, ReconnectOverlayViewModel Vm, IDisposable? Token)> _overlays = new();

private void OnReconnectOverlayRequested(object? sender, ReconnectUiRequest req)
{
    Dispatcher.Invoke(() =>
    {
        var id = req.Connection.Id;
        // Replace existing overlay for this tab (rapid successive drops)
        CloseOverlayFor(id);

        var vm = new ReconnectOverlayViewModel { ConnectionName = req.Connection.Hostname };
        var ctrl = new ReconnectOverlay { DataContext = vm };

        // Wire core ↔ UI same as Phase 4 (no change to the handle mechanism)
        req.Handle.UpdateAttempt = (a, d) => Dispatcher.Invoke(() => vm.Update(a, d));
        req.Handle.SwitchToManual = () => Dispatcher.Invoke(() => vm.SwitchToManual());
        req.Handle.Close = () => Dispatcher.Invoke(() => CloseOverlayFor(id));
        vm.Cancelled += (_, _) => req.Handle.RaiseCancel();
        vm.ReconnectRequested += (_, _) => req.Handle.RaiseManualReconnect();
        vm.CloseRequested += (_, _) => req.Handle.RaiseManualClose();

        // D-14: find the host slot for this tab. If host already exists (live reconnect),
        // parent overlay in HostContainer alongside it; if Reconnecting state triggered
        // before host mount (rare), parent in HostContainer root with ZIndex.
        IDisposable? token = null;
        var host = _tabHostManager.GetHost(id);
        if (host is RdpHostControl rdp)
        {
            try { token = _airspace.HideWithoutSnapshot(rdp.Host); } catch { }
        }
        // Overlay mounts directly in HostContainer — its Visibility follows the tab's
        // active state automatically because background tabs have their whole slot
        // Collapsed (Pattern 2).
        HostContainer.Children.Add(ctrl);
        System.Windows.Controls.Panel.SetZIndex(ctrl, 1000);
        ctrl.Tag = id;      // Correlate back to connection for visibility switching

        _overlays[id] = (ctrl, vm, token);

        // The overlay is only *visible* when this tab is active. Phase 5 achieves that
        // because the overlay's sibling WFH follows the same tab visibility rules.
    });
}

private void CloseOverlayFor(Guid id)
{
    if (!_overlays.TryGetValue(id, out var entry)) return;
    _overlays.Remove(id);
    try { entry.Token?.Dispose(); } catch { }
    if (HostContainer.Children.Contains(entry.Control))
        HostContainer.Children.Remove(entry.Control);
}
```

**Key points:**
- Timer / coordinator keep running even when tab is inactive — `RdpReconnectCoordinator.RunAsync` runs in Core and is unaware of tab state. The amber badge in the tab header (D-12) is the only user-visible Phase 5 signal for background drops.
- When user switches to the Reconnecting tab, the overlay becomes visible because its parent (HostContainer slot for that tab) flips to Visible via the `SetActiveHostVisibility` logic. No extra show/hide needed.
- `ReconnectOverlayRequested` dispatching to the correct slot uses `_tabHostManager.GetHost(id)` — clean, matches the D-01 data model.

### Pattern 8: TabState enum and tab DataTemplate indicators (D-12)

**TabState enum (new file `src/Deskbridge/Models/TabState.cs`):**

```csharp
namespace Deskbridge.Models;

public enum TabState
{
    Connecting,    // Pipeline running, awaiting OnLoginComplete
    Connected,     // Live session, no overlay
    Reconnecting,  // Drop detected, backoff loop running
    Error          // Auth failure OR retry cap hit — manual reconnect needed
}
```

**TabItemViewModel extension:**

```csharp
public partial class TabItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(IsReconnecting))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    public partial TabState State { get; set; } = TabState.Connecting;

    // Derived visibility helpers for XAML (UI-SPEC Mutual exclusion rule)
    public bool IsConnecting => State == TabState.Connecting;
    public bool IsReconnecting => State == TabState.Reconnecting;
    public bool IsError => State == TabState.Error;

    public Guid ConnectionId { get; init; }
}
```

**XAML DataTemplate additions (UI-SPEC §Per-Tab State Visual Contract):**

```xml
<StackPanel Orientation="Horizontal" VerticalAlignment="Center">
    <!-- Connecting spinner — WPF-UI ProgressRing -->
    <ui:ProgressRing IsIndeterminate="True" Width="12" Height="12" Margin="0,0,8,0"
        Visibility="{Binding IsConnecting, Converter={StaticResource BoolToVisibility}}" />
    <!-- Reconnecting dot — amber Ellipse -->
    <Ellipse Width="8" Height="8" Margin="0,0,8,0"
        Fill="{DynamicResource DeskbridgeWarningBrush}"
        Visibility="{Binding IsReconnecting, Converter={StaticResource BoolToVisibility}}" />
    <!-- Error dot — red Ellipse -->
    <Ellipse Width="8" Height="8" Margin="0,0,8,0"
        Fill="{DynamicResource DeskbridgeErrorBrush}"
        Visibility="{Binding IsError, Converter={StaticResource BoolToVisibility}}" />
    <!-- Title (existing) -->
    <TextBlock Text="{Binding Title}" FontSize="14" TextTrimming="CharacterEllipsis" MaxWidth="200">
        <TextBlock.ToolTip>
            <!-- Tooltip copy per UI-SPEC §Copywriting Contract -->
            <TextBlock Text="{Binding TooltipText}" />
        </TextBlock.ToolTip>
        <!-- Foreground style (existing) -->
    </TextBlock>
    <!-- Close button (existing) -->
    <ui:Button Appearance="Transparent" Icon="{ui:SymbolIcon Dismiss16}" .../>
</StackPanel>
```

Tooltip text is a derived property on `TabItemViewModel` that composes hostname + state string per the UI-SPEC §Tooltip table.

**Context menu (right-click on tab) — add `Border.ContextMenu` in the tab DataTemplate:**

```xml
<Border ... >
    <Border.ContextMenu>
        <ContextMenu>
            <MenuItem Header="Close"
                Command="{Binding DataContext.CloseTabCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                CommandParameter="{Binding}" />
            <MenuItem Header="Close Others"
                Command="{Binding DataContext.CloseOthersCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                CommandParameter="{Binding}" />
            <MenuItem Header="Close All"
                Command="{Binding DataContext.CloseAllCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}" />
        </ContextMenu>
    </Border.ContextMenu>
    <!-- existing content -->
</Border>
```

### Pattern 9: ConnectionCoordinator refactor (D-01, D-02, D-05)

**Decision:** Delete ~20 lines from `ConnectionCoordinator.cs` — the single-host state becomes TabHostManager's job.

**Lines to remove from `ConnectionCoordinator.cs`:**

| Line range | What it does | Why remove |
|------------|--------------|------------|
| 28 | `private (IProtocolHost Host, ConnectionModel Model)? _active;` | State moves to TabHostManager `_hosts` dict |
| 56-60 | `ActiveHost` property + `HostMounted`/`HostUnmounted` events | Phase 5 replaces; events stay on `IConnectionCoordinator` **interface** (they're the seam TabHostManager subscribes to) — DO NOT DELETE FROM THE INTERFACE |
| 82-88 | Rapid-double-click guard | D-02 makes TabHostManager the authority; guard becomes redundant. **Deletion must be explicit in plan task with commit-message cite of D-02.** |
| 93-104 | Single-host replacement branch (`if (_active is { } active) { ... RunDisconnectSafely ... }`) | D-01: replacement semantics belong to TabHostManager |
| 486-506 | `if (_active is { } active) { ... DisconnectAsync ... }` in `Dispose()` | Replaced by `TabHostManager.CloseAllAsync()` invoked from `MainWindow.OnClosing` (D-08) |

**What STAYS in ConnectionCoordinator:**
- `HostCreatedEvent` subscription (Phase 4 WR-01 fix — still needed for siting-order guarantee)
- `ConnectionEstablishedEvent`, `ConnectionFailedEvent`, `ConnectionClosedEvent`, `CredentialRequestedEvent` subscriptions (still publish via bus, just not tied to `_active` after Phase 5)
- `DisconnectedAfterConnect` reconnect coordination (the whole Plan 04-03 machinery) — unchanged
- `OnHostCreated` STILL raises `HostMounted` event (interface seam for TabHostManager + MainWindow)
- `OnConnectionClosed` STILL raises `HostUnmounted` event

**New behavior — Switch-to-existing (D-02):**

```csharp
// Inside ConnectionCoordinator.OnConnectionRequested — REPLACE the removed lines with:
if (_tabHostManager.TryGetExistingTab(evt.Connection.Id, out var existingHost))
{
    _logger.LogDebug("Switching to existing tab for {Hostname} (D-02)", evt.Connection.Hostname);
    _tabHostManager.SwitchTo(evt.Connection.Id);
    // Publish TabSwitchedEvent is TabHostManager's job; no additional event needed here.
    return;
}

// Fall through — no existing tab, start pipeline
_ = RunConnectSafely(evt.Connection);
```

**Dependency cycle risk:** ConnectionCoordinator → TabHostManager AND TabHostManager subscribes to ConnectionCoordinator's events. Resolve via DI: TabHostManager constructor-injects `IConnectionCoordinator`; ConnectionCoordinator constructor-injects `ITabHostManager`. Both are singletons. Circular constructor-injection works in Microsoft DI **only if both are resolved lazily**; otherwise it throws.

**Fix:** ConnectionCoordinator stays as-is (no dependency on TabHostManager). Instead, the switch-to-existing check moves UP the stack: `ConnectionTreeViewModel.Connect` (or wherever `ConnectionRequestedEvent` originates) queries `ITabHostManager.TryGetExistingTab` BEFORE publishing the event. If a tab exists, publish `TabSwitchedEvent` directly; otherwise publish `ConnectionRequestedEvent` as today.

Alternative: inject `ITabHostManager` via `IServiceProvider.GetRequiredService` lazily inside the handler (property lookup). Simpler but masks the dep. **Recommendation:** move the check to the publisher side — cleaner separation, no DI surgery needed.

### Anti-Patterns to Avoid

- **Re-parenting a `WindowsFormsHost` between tab switches.** Flips DestroyHandle, loses AxHost state, may trigger UCEERR_RENDERTHREADFAILURE (dotnet/wpf #3719). Use `Visibility` only. [CITED: WINFORMS-HOST-AIRSPACE line 374]
- **Setting `BitmapPeristence` after Connect().** Microsoft docs: AdvancedSettings properties cannot be set while connected. Phase 4 already sets to 0 at configure time; don't mutate in Phase 5. [VERIFIED: Microsoft Learn IMsRdpClientAdvancedSettings-Interface]
- **Using `Visibility.Hidden` on inactive WFH.** Documented in `AirspaceSwapper.cs:131` — some servers (xrdp) see `Hidden` as an API-initiated disconnect (discReason=2) and end the session. **Always `Visibility.Collapsed`.**
- **Iterating `_hosts` directly during Close Others / Close All.** Each `await DisconnectAsync` yields; during the await, `OnHostUnmounted` and `OnDisconnectedAfterConnect` can fire and mutate `_hosts`. **Always snapshot to a `List<Guid>` first.**
- **Confirmation dialog on any close path.** Rejected in D-06. A plan task introducing `ContentDialog` on close fails checker.
- **Parallel `Task.WhenAll(hosts.Select(DisconnectAsync))` on app shutdown.** Rejected in D-08. STA re-marshal nullifies parallelism.
- **Auto-focus-switch to a dropped background tab.** Rejected in D-14.
- **Host dict in `MainWindowViewModel`.** Rejected in D-01.
- **Hardcoded `#F44747` / `#FFCC02` in new XAML.** Use `{DynamicResource DeskbridgeErrorBrush}` / `{DynamicResource DeskbridgeWarningBrush}`. The three hex literals already exist in App.xaml brush definitions — do not duplicate.
- **`ConfigureAwait(false)` anywhere in TabHostManager or stages that touch hosts.** Marshals continuation to MTA, COM access throws. Same rule as Phase 4 D-11.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Tab event bus | Custom pub/sub | Existing `IEventBus` (WeakReferenceMessenger) | Phase 1 locked; weak refs avoid leaks |
| Drag-drop framework | Full drag-drop primitives | `System.Windows.DragDrop.DoDragDrop` (built-in) | Standard WPF API, already used by TreeViewDragDropBehavior |
| Disconnect pipeline | Custom teardown | Existing `IDisconnectPipeline` from Phase 4 | D-06/D-07/D-08 all reuse this; wraps strict disposal sequence |
| STA marshal helper | Custom dispatcher utility | `Dispatcher.CheckAccess()` + `Dispatcher.Invoke/InvokeAsync` | Mirror `ConnectionCoordinator` pattern (lines 67-71) |
| ObservableCollection reorder | Remove+Insert | `ObservableCollection.Move(oldIdx, newIdx)` | Built-in; raises single MoveItem event; UI doesn't rebind |
| Snackbar | Custom toast | `ISnackbarService.Show(..., ControlAppearance.Caution, icon, timeout)` | Phase 2 registered; WPF-UI's `SnackbarPresenter` already in MainWindow.xaml:289 |
| LRU cache | Custom data structure | `LinkedList<Guid>` with manual dedupe | 10-entry cap; no need for `LruCache<T>` generic |
| Context menu | Custom popup | `ContextMenu` + `MenuItem` (auto-styled by WPF-UI) | UI-SPEC Registry Safety: all controls from locked stack |
| Multi-host visibility switching | Custom stacking panel | `Grid` with child `Visibility = Collapsed` | WINFORMS-HOST-AIRSPACE §Option 3 (lines 376-431) is the reference architecture |

**Key insight:** Phase 5 is a service composition, not new technology. Every hard problem (airspace, disposal, GDI, STA) was solved in Phase 4 or earlier. The temptation is to "modernize" things — introduce a new DI lifetime, write a new drag-drop framework, switch to a different event bus — but CLAUDE.md and CONTEXT.md lock every primitive. Stay on the rails.

## Runtime State Inventory

Not applicable. Phase 5 is greenfield (no rename, no refactor of existing data, no migration). No pre-existing Deskbridge tab state to inventory.

**Verified by category:**
- **Stored data:** None — tabs are process-lifetime only. Session restoration across app restarts is explicitly v1.1 backlog (D-13).
- **Live service config:** None — no external services. Tab state is purely in-memory.
- **OS-registered state:** None — no Windows Task Scheduler tasks, no pm2, no launchd, no system services.
- **Secrets/env vars:** None — no new env var consumption. Existing `ConnectionContext.ResolvedPassword` handling unchanged from Phase 4.
- **Build artifacts:** None — no new interop assemblies, no regenerated DLLs, no egg-info equivalents.

The one checked-in asset touched by this phase is `src/Deskbridge.Protocols.Rdp/Interop/AxMSTSCLib.dll` + `MSTSCLib.dll` — but Phase 5 **does not invoke new COM interfaces**. All RDP settings are already applied at connect time in Phase 4's `RdpConnectionConfigurator`.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | Build | ✓ | 10.0.x | — |
| WPF-UI (NuGet) | UI controls | ✓ (NuGet) | 4.2.0 | — |
| CommunityToolkit.Mvvm (NuGet) | MVVM | ✓ (NuGet) | 8.4.2+ | — |
| Windows (net10.0-windows) | Runtime | ✓ | Windows 10/11 | — |
| STA thread pump (dispatcher) | Tests | ✓ | `StaCollectionFixture.cs` + `StaRunner` already exist | — |
| Live RDP target (optional) | Multi-host UAT | User-supplied | — | Skip UAT item if no target; unit/integration tests do not require real RDP |

**Nothing blocks Phase 5.** Only code + config changes. No new external dependencies.

## Common Pitfalls

### Pitfall 1: BitmapPeristence treated as a runtime toggle

[VERIFIED: [Microsoft Learn — IMsRdpClientAdvancedSettings Interface](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings-interface) Remarks section: "This property cannot be set when the control is connected, unless otherwise indicated."]

**What goes wrong:** Developer reads CONTEXT.md D-04 "sets `AdvancedSettings.BitmapPersistence = 0` on collapsed hosts (TAB-02)" as "write this setting at tab switch time." Any runtime write to `AdvancedSettings9.BitmapPeristence` on a connected session throws `COMException` (HResult=0x80004005 `E_FAIL` or `E_UNEXPECTED`) or returns silently with no effect depending on the version.

**Why it happens:** The Microsoft documentation is explicit: AdvancedSettings are configure-time-only. The property is writable (no `HRESULT_FROM_WIN32(ERROR_INVALID_STATE)` in the declaration) but the underlying implementation checks `Connected` state and fails the write. Phase 4's `RdpConnectionConfigurator.cs:47` already sets it to 0 at connect time; the property stays 0 for the life of the session — **inactive tabs satisfy TAB-02 by configure-time precondition, not by runtime mutation**.

**How to avoid:**
1. The Phase 5 plan task for D-04 implementation must NOT include `rdp.AdvancedSettings9.BitmapPeristence = 0` anywhere. The verification step must grep for this write and fail if present outside `RdpConnectionConfigurator.Apply`.
2. Document the resolution in the code comment at `SetActiveHostVisibility` — mention that GDI reduction on inactive tabs comes from two sources: (a) `Visibility.Collapsed` suppressing paint messages, (b) Phase 4's configure-time `BitmapPeristence=0` preventing cache growth. Neither requires runtime mutation.
3. If the user asks "why do we not set BitmapPersistence per-tab?", the answer is the Microsoft API rule + the configure-time fact.

**Warning signs:** A plan task with text like "on Visibility.Collapsed, set rdp.AdvancedSettings9.BitmapPeristence = 0" is incorrect and must be rewritten.

### Pitfall 2: Assuming `KeyboardHookMode` is higher than 0 breaks Ctrl+Tab

[CITED: RDP-ACTIVEX-PITFALLS §5 lines 277-281 + verified in `src/Deskbridge.Protocols.Rdp/RdpConnectionConfigurator.cs:48`]

**What goes wrong:** A reviewer looks at the mRemoteNG-derived advice in RDP-ACTIVEX-PITFALLS §5 ("users cannot use Ctrl+Tab to switch tabs while an RDP session has focus") and concludes Phase 5 needs `RegisterHotKey` + `HwndSource.AddHook`. They then wrap the keyboard handling in complex Win32 plumbing that isn't necessary.

**Why it happens:** The pitfalls doc's mRemoteNG complaint assumes `KeyboardHookMode=1` (redirect all keys). Deskbridge locks `KeyboardHookMode=0` in Phase 4 defaults. With 0, the low-level hook is never installed — WPF's standard routed events work. `PreviewKeyDown` on the FluentWindow tunnels down past the WFH child.

**How to avoid:** The Phase 5 plan for TAB-03 / D-16 shortcuts uses ONLY `PreviewKeyDown`. No `RegisterHotKey`, no `SetWindowsHookEx`. Verify at task time by running the Phase 5 keyboard-shortcut integration test with a real RDP session open and focused.

**Warning signs:** A plan introducing `RegisterHotKey` or `HwndSource.AddHook` for the Phase 5 TAB-03 shortcuts is over-engineering. Phase 6's global shortcut service MAY use these if command-palette shortcuts need global reach; Phase 5 does not.

**Fallback trigger:** If Phase 5 UAT shows Ctrl+Tab not working with RDP focused (despite KeyboardHookMode=0), that is a blocker. Diagnosis order: (1) confirm `KeyboardHookMode=0` is actually written to the control (log the value after Apply), (2) confirm WPF tunnel event fires (add a temporary `OnPreviewKeyDown` log), (3) if both pass, the issue is WFH focus acquisition — the WFH doesn't implement `IKeyboardInputSink` bridge correctly for this scenario, and Phase 6's `RegisterHotKey` path must be pulled forward.

### Pitfall 3: Drag-reorder triggers re-parenting

**What goes wrong:** Developer reads D-13 "drag-to-reorder tabs" and implements it by removing the `TabItemViewModel` from `Tabs` collection, computing new index, re-inserting. `ObservableCollection<T>.Remove + Insert` fires two events (Remove + Add); the XAML `ItemsControl` re-templates the removed item at the new position; but **the `WindowsFormsHost` in `HostContainer` is untouched** — good, right? Wrong: the developer implements this in `MainWindowViewModel` and accidentally also re-parents the WFH between slots "to match the new order." Disaster.

**Why it happens:** The tab DataTemplate renders small chrome around the title. The *actual* RDP viewport lives in `HostContainer` — a different visual subtree, correlated by `ConnectionId` via `Tag`. Tab ORDER in the `Tabs` ObservableCollection is independent of `HostContainer.Children` ordering. They must NEVER be synchronized.

**How to avoid:** Drag reorder mutates ONLY `Tabs` (the ObservableCollection). Use `ObservableCollection.Move(oldIndex, newIndex)` — raises a single `MoveItem` event that `ItemsControl` handles without full re-rendering. `HostContainer.Children` is never touched. Plan task must include a grep guard: "no call to `HostContainer.Children.Remove` outside of `OnHostUnmounted`."

### Pitfall 4: Close Others / Close All mutation race

**What goes wrong:** Naive implementation: `foreach (var kvp in _hosts) { await CloseTabAsync(kvp.Key); }`. Awaiting inside the foreach with a live collection throws `InvalidOperationException: Collection was modified` because each `await` yields the STA thread back to the dispatcher, and during that yield, `OnHostUnmounted` (triggered by the just-completed disconnect) calls `_hosts.Remove(id)` mid-iteration.

**Why it happens:** STA single-threaded affinity prevents *parallel* mutation, but not *re-entrant* mutation. `await` is re-entrant — the dispatcher pumps other queued work during the delay.

**How to avoid:** Snapshot first. `var targets = _hosts.Keys.ToList(); foreach (var id in targets) await CloseTabAsync(id);` The snapshot `List<Guid>` is immutable during iteration; `_hosts.Remove(id)` mid-loop is harmless because the loop iterates the snapshot, not the dict.

**Warning signs:** Any `foreach` over `_hosts.Keys` / `_hosts.Values` / `_hosts` directly with `await` inside MUST be rewritten to snapshot first.

### Pitfall 5: ConnectionCoordinator / TabHostManager circular DI

**What goes wrong:** Both are singletons. Both want to reference each other via constructor injection. Microsoft DI throws `InvalidOperationException: A circular dependency was detected`.

**Why it happens:** TabHostManager subscribes to ConnectionCoordinator events → needs ICC in ctor. ConnectionCoordinator wants to ask "is tab already open?" before dispatching pipeline → needs ITabHostManager in ctor.

**How to avoid:** Break the cycle at the *publisher* of `ConnectionRequestedEvent`. The tree VM / command palette publishes the event; make them ask TabHostManager first: "`TryGetExistingTab(id)` → if true, publish `TabSwitchedEvent` and return; else publish `ConnectionRequestedEvent`." ConnectionCoordinator no longer needs to know about TabHostManager. The duplicate-click guard (ConnectionCoordinator.cs:82-88) is deleted because the publisher-side check subsumes it.

**Alternative:** If breaking at publisher is awkward, resolve TabHostManager lazily via `IServiceProvider` property lookup inside ConnectionCoordinator's handler. Less clean; reserve as backup.

### Pitfall 6: WFH Visibility.Visible on multiple hosts simultaneously

**What goes wrong:** Tab switch transitions are not atomic. If `SetActiveHostVisibility(newId)` sets new host Visible BEFORE setting old host Collapsed, there's a frame where both are Visible. Since only one can occupy the airspace, the Win32 z-order decides — usually "last painted wins" which is undefined.

**How to avoid:** In `SetActiveHostVisibility`, iterate all children once: for each, compute `isActive` and assign. The order within a single `foreach` is fast enough that the user doesn't see a flash. Layout pass happens after the loop completes. If flash is observed in UAT, explicitly invoke `UpdateLayout()` after the loop.

### Pitfall 7: TabHostManager publishes events before MainWindow subscribes

**What goes wrong:** DI composition resolves TabHostManager eagerly (`services.AddSingleton + GetRequiredService in OnStartup`). TabHostManager's constructor wires up subscriptions to ConnectionCoordinator immediately. But `MainWindow` is not yet constructed when TabHostManager starts processing events. A synchronous `HostMounted` delivery from ConnectionCoordinator during app startup (if a stale connection is auto-reopened, Phase 6) would reach TabHostManager → raise `TabOpenedEvent` → but MainWindow hasn't subscribed yet, so the event is dropped.

**How to avoid:** WeakReferenceMessenger delivers inline but late subscribers DO get events if Subscribe happens before the next Publish. For Phase 5, startup ordering is: App.xaml.cs OnStartup → build services → resolve MainWindow (constructor wires subscriptions) → Show window → user interaction triggers first `ConnectionRequestedEvent`. No events fire before MainWindow is constructed in Phase 5 scope (v1.1 session restore would break this, but that's deferred).

**Hedge:** Document the ordering requirement in `App.xaml.cs` OnStartup with a comment. Phase 6 session-restore must be planned accordingly.

### Pitfall 8: Snackbar fires every tab open once count is at 15+

**What goes wrong:** Naive threshold check: `if (_hosts.Count >= 15) Show();` → fires at 15, 16, 17, ... every tab open.

**How to avoid:** `if (!_warned15 && _hosts.Count == GdiWarningThreshold)` — note `==` not `>=`, and gated by `_warned15` flag. Reset to false when count drops below. This gives the D-09 "fire once per threshold crossing" semantics.

## Code Examples

### Example 1: TabState-driven badge rendering (verified against UI-SPEC)

```xml
<!-- Source: 05-UI-SPEC.md §Per-Tab State Visual Contract + D-12 -->
<StackPanel Orientation="Horizontal" VerticalAlignment="Center">
    <ui:ProgressRing IsIndeterminate="True" Width="12" Height="12" Margin="0,0,8,0"
        Visibility="{Binding IsConnecting, Converter={StaticResource BoolToVisibility}}" />
    <Ellipse Width="8" Height="8" Margin="0,0,8,0"
        Fill="{DynamicResource DeskbridgeWarningBrush}"
        Visibility="{Binding IsReconnecting, Converter={StaticResource BoolToVisibility}}" />
    <Ellipse Width="8" Height="8" Margin="0,0,8,0"
        Fill="{DynamicResource DeskbridgeErrorBrush}"
        Visibility="{Binding IsError, Converter={StaticResource BoolToVisibility}}" />
    <TextBlock Text="{Binding Title}" FontSize="14" MaxWidth="200" TextTrimming="CharacterEllipsis" />
</StackPanel>
```

### Example 2: Snapshot-then-iterate for Close All

```csharp
// Source: Pitfall 4 + D-07 + D-08
public async Task CloseAllAsync()
{
    // SNAPSHOT before iterating — mutation during await must not throw
    var targets = _hosts.Keys.ToList();
    foreach (var id in targets)
    {
        try { await CloseTabAsync(id); }
        catch (Exception ex)
        {
            _logger.LogError(
                "Close for {Id} threw: {Type} HResult={HResult:X8}",
                id, ex.GetType().Name, ex.HResult);
            // Continue — don't let one host's disposal failure block the others
        }
    }
}
```

### Example 3: PreviewKeyDown tab shortcuts

```csharp
// Source: D-16 + Pattern 3
protected override void OnPreviewKeyDown(KeyEventArgs e)
{
    var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
    var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

    if (ctrl && e.Key == Key.Tab)                { CycleTab(!shift); e.Handled = true; return; }
    if (ctrl && e.Key == Key.F4)                 { ViewModel.CloseTabCommand.Execute(ViewModel.ActiveTab); e.Handled = true; return; }
    if (ctrl && shift && e.Key == Key.T)         { ReopenLastClosed(); e.Handled = true; return; }
    if (ctrl && !shift && e.Key is >= Key.D1 and <= Key.D9)
    {
        var count = ViewModel.Tabs.Count;
        if (count == 0) return;
        int idx = e.Key == Key.D9 ? count - 1 : (int)(e.Key - Key.D1);
        if (idx < count) ViewModel.SwitchTabCommand.Execute(ViewModel.Tabs[idx]);
        e.Handled = true;
        return;
    }
    base.OnPreviewKeyDown(e);
}
```

### Example 4: Persistent HostContainer + Visibility switching

```csharp
// Source: Pattern 2 + WINFORMS-HOST-AIRSPACE §Option 3
private void SetActiveHostVisibility(Guid activeId)
{
    foreach (var wfh in HostContainer.Children.OfType<WindowsFormsHost>())
    {
        var isActive = wfh.Tag is Guid id && id == activeId;
        wfh.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        wfh.IsEnabled = isActive;   // WINFORMS-HOST-AIRSPACE line 397
    }
    // Overlays parented inside HostContainer correlate via Tag the same way (if used)
    foreach (var ov in HostContainer.Children.OfType<ReconnectOverlay>())
    {
        var isActive = ov.Tag is Guid id && id == activeId;
        ov.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Single-slot active host (Phase 4) | Dict-keyed multi-host (Phase 5 D-01) | 2026-04-13 | TabHostManager owns lifecycle state; coordinator loses `_active` |
| Remove WFH + add new on tab switch | Persistent `HostContainer`, `Visibility` toggle | — (locked by WINFORMS-HOST-AIRSPACE) | RDP sessions survive tab switches; no re-parenting leaks |
| `RegisterHotKey` for Ctrl+Tab (mRemoteNG) | `PreviewKeyDown` + `KeyboardHookMode=0` | Phase 4 default | Simpler plumbing; works because KeyboardHookMode locks keys local |
| XML/`TabControl` built-ins | Custom `ItemsControl` + attached drag behavior | Phase 2 XAML | 30px tab height, custom accent, full visual control |
| Hard connection cap | Soft advisory Snackbar at 15 | D-09 | Users warned but not blocked; machine-specific GDI ceilings vary |

**Deprecated/outdated:**
- mRemoteNG's `KeyboardHookMode=1` + no Ctrl+Tab support — rejected in favor of `KeyboardHookMode=0`.
- `TabControl` with `ContentTemplate` approach — rejected in Phase 2 for pixel-perfect chrome control.
- Single-host replacement-on-new-connect (Phase 4 interim) — superseded by multi-host dict in Phase 5.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `KeyboardHookMode=0` is sufficient for `PreviewKeyDown` to fire on Ctrl+Tab while RDP viewport is focused | Pitfall 2, Pattern 3 | HIGH — if wrong, Phase 6 shortcut service must be pulled forward. Mitigation: UAT verification gate with real RDP target. |
| A2 | `ObservableCollection.Move(oldIdx, newIdx)` triggers `ItemsControl` re-render without re-templating items (so bound visuals survive) | Pattern 6 Pitfall 3 | MEDIUM — if wrong, TabItemViewModel bindings flicker during drag. Mitigation: UAT visual check; fallback to explicit Remove+Insert with Tab preserved by reference. |
| A3 | `WindowsFormsHost.IsEnabled = false` on a hidden WFH prevents mouse/keyboard events reaching its child AxHost | Pattern 2 | LOW — cited in WINFORMS-HOST-AIRSPACE line 397 but may be superstition. Even if ineffective, `Visibility.Collapsed` removes the WFH from hit-testing, so IsEnabled is belt-and-braces. Not load-bearing. |
| A4 | `AirspaceSwapper`'s `Dictionary<WFH, Image>` multi-host iteration works correctly with 10+ hosts under WM_ENTERSIZEMOVE | Pattern 4 | LOW — code already iterates `foreach`; stress-test in UAT with 10 real sessions during a drag-resize. |
| A5 | Circular dependency between ConnectionCoordinator and TabHostManager can be avoided by moving the switch-to-existing check to the publisher | Pitfall 5 | LOW — publishers (tree VM, command palette) already have access to DI; trivially injectable. |
| A6 | `ControlAppearance.Caution` is the correct enum member for a yellow/amber Snackbar in WPF-UI 4.2.0 | Pattern 5 | LOW — UI-SPEC §Snackbar Severity Mapping confirms; double-check at plan time by looking at `Wpf.Ui.Controls.ControlAppearance` enum. |
| A7 | `Panel.SetZIndex` on the reconnect overlay inside HostContainer guarantees it renders above the WFH slot | Pattern 7 | MEDIUM — WPF z-order + Win32 airspace interplay is imperfect. Phase 4 already uses this pattern at ViewportGrid level; Phase 5 re-uses the same. Verify UAT shows overlay visually on top when tab is active. |
| A8 | Phase 4 `WR-02` loop that removes all WFH from ViewportGrid before adding a new one (MainWindow.xaml.cs:93-99) must be deleted in Phase 5 | Pattern 2 | LOW — D-04 "never re-parent" contradicts the defense-in-depth; WR-02 becomes redundant because the removal path is only the explicit `OnHostUnmounted` tab-close. |

**Items in this table that appear in `[ASSUMED]` tags in the research body:** None above — the claims here derive from cited docs or existing code reads; they are labeled "Assumption" because they have residual empirical risk (usually "need UAT to confirm"), not because they lack source attribution.

## Open Questions (RESOLVED)

1. **Does Ctrl+Tab reliably fire `PreviewKeyDown` on the FluentWindow when the AxHost has focus?** (Assumption A1) — RESOLVED: Deferred to UAT at Plan 03 Task 3. Fallback documented: if A1 fails, Phase 6 global shortcut service pulls forward (RegisterHotKey-based).
   - What we know: `KeyboardHookMode=0` disables the RDP low-level hook (RDP-ACTIVEX-PITFALLS §5 docs).
   - What's unclear: Whether the WFH's IKeyboardInputSink bridge correctly tunnels Ctrl+Tab to the WPF input manager.
   - Recommendation: Add a UAT acceptance check specifically for "Ctrl+Tab with RDP focused." If it fails, Phase 6 shortcut service pulls forward (2-day slip).

2. **What happens to auto-reconnect when the user closes the tab during a backoff delay?** — RESOLVED: New `IConnectionCoordinator.CancelReconnect(Guid)` API added; `TabHostManager.CloseTabAsync` / `CloseOthersAsync` / `CloseAllAsync` all invoke it before `DisconnectAsync`. Wired in Plan 01 Task 2 (TabHostManager close paths) and Plan 01 Task 3 (ConnectionCoordinator + IConnectionCoordinator interface).
   - What we know: Phase 4 `RdpReconnectCoordinator.RunAsync` takes a `CancellationToken` which is cancelled on user Cancel.
   - What's unclear: Does `CloseTabAsync` from TabHostManager cancel any in-flight reconnect token?
   - Recommendation: `TabHostManager.CloseTabAsync` must find the active reconnect CTS for that connection and cancel it before `DisconnectAsync`. Plumb via `ConnectionCoordinator.CancelReconnect(Guid)` helper or expose the CTS dict on the coordinator.

3. **If the user clicks Reopen Last Closed (Ctrl+Shift+T) while the connection has been deleted from the store, what happens?** — RESOLVED: Plan 02 Task 2 step 2d implements null-check via `IConnectionStore.GetById`; silent no-op if missing, no re-surfaced entry in LRU.
   - What we know: UI-SPEC line 345 locks silent failure ("no toast, no beep").
   - What's unclear: Where the null-check happens — in MainWindow's PreviewKeyDown handler (consult store) or in TabHostManager.PopLastClosed (which only returns Guid, no store lookup).
   - Recommendation: Null-check in MainWindow: `_connectionStore.GetById(id) ?? return;`. Matches the D-16 spec and keeps TabHostManager dependency-light.

4. **Does `HostContainer.Children.Add(wfh)` from multiple incoming HostCreatedEvents in quick succession cause ordering issues?** — RESOLVED: STA dispatcher serializes all `MainWindow.OnHostMounted` invocations; verified in `tests/Deskbridge.Tests/Integration/HostContainerPersistenceTests.cs` (Plan 02 Task 3).
   - What we know: STA single-thread prevents parallel Add.
   - What's unclear: If MainWindow's `OnHostMounted` fires while a previous Add is mid-layout, does the second Add see a stale `HostContainer.Children.Count`?
   - Recommendation: No — the dispatcher serializes all handlers; the second handler sees the post-first-Add state. Verify in unit test "rapid 3-open" scenario.

## Environment Availability

Addressed above — all dependencies available, no external services needed.

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit v3 3.2.2 (Phase 1 established) |
| Config file | `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` (xUnit v3 uses OutputType=Exe) |
| Quick run command | `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj --filter "FullyQualifiedName~Tabs"` |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| TAB-01 | One connection per tab; switch-to-existing on re-connect request | unit | `dotnet test --filter "TabHostManagerTests.SecondRequestForSameConnection_SwitchesExistingTab"` | ❌ Wave 0 |
| TAB-01 | WFH parented once into HostContainer; never re-parented until tab close | integration (STA) | `dotnet test --filter "HostContainerPersistenceTests"` | ❌ Wave 0 |
| TAB-02 | Inactive tabs have `Visibility=Collapsed`; `BitmapPeristence=0` verified at configure time (Phase 4 carryover) | integration (STA) | `dotnet test --filter "InactiveTabVisibilityTests"` | ❌ Wave 0 |
| TAB-03 | Ctrl+Tab / Ctrl+Shift+Tab cycle; Ctrl+W closes; middle-click closes | integration (STA) | `dotnet test --filter "KeyboardShortcutTests"` | ❌ Wave 0 |
| TAB-03 | Ctrl+1..Ctrl+9 jump; Ctrl+F4 close; Ctrl+Shift+T reopen LRU | integration (STA) | `dotnet test --filter "KeyboardShortcutTests"` | ❌ Wave 0 |
| TAB-04 | Crossing 14→15 fires Snackbar once; 16 does not re-fire | unit (mock `ISnackbarService`) | `dotnet test --filter "TabHostManagerTests.FifteenSessionWarning"` | ❌ Wave 0 |
| TAB-05 | `HostMounted` → `TabOpenedEvent` published; `HostUnmounted` → `TabClosedEvent` published | unit (mock `IEventBus`) | `dotnet test --filter "TabHostManagerTests.PublishesTabEvents"` | ❌ Wave 0 |
| D-02 | Duplicate `ConnectionRequestedEvent` publishes `TabSwitchedEvent` (not second pipeline run) | unit | `dotnet test --filter "TabHostManagerTests.DuplicateRequestSwitchesInsteadOfOpening"` | ❌ Wave 0 |
| D-07 Close Others | Iterating during mutation does not throw `InvalidOperationException` | unit | `dotnet test --filter "TabHostManagerTests.CloseOthersSnapshotIteration"` | ❌ Wave 0 |
| D-08 App close | `CloseAllAsync` disposes hosts sequentially; GDI returns to baseline | integration (STA) + live RDP | `dotnet test --filter "AppCloseGdiBaselineTests"` (SkippableFact — requires `DESKBRIDGE_SMOKE_RDP_HOST` env var) | ❌ Wave 0 |
| D-13 drag-reorder | `Tabs.Move(old, new)` does not re-parent `HostContainer` WFH children | integration (STA) | `dotnet test --filter "TabReorderTests.MoveDoesNotReparentHost"` | ❌ Wave 0 |
| D-14 background drop | `ReconnectOverlayRequested` for inactive tab sets `TabState=Reconnecting` without auto-switch | unit | `dotnet test --filter "TabHostManagerTests.InactiveTabDropSetsReconnectingState"` | ❌ Wave 0 |
| D-16 LRU | Push/dedupe/cap-10 behavior | unit | `dotnet test --filter "TabHostManagerTests.LastClosedLruDedupesAndCaps"` | ❌ Wave 0 |
| D-16 LRU miss | Reopen with deleted connection silently no-ops | integration | `dotnet test --filter "ReopenLastClosedTests.DeletedConnectionSilent"` | ❌ Wave 0 |
| **UAT only** | Ctrl+Tab works while RDP session has focus (Assumption A1 verification) | manual | `tests/uat/phase-05-keyboard.md` human checklist | ❌ Wave 0 |
| **UAT only** | 14-host GDI stability (same Phase 4 gate extended) | manual with live RDP | `tests/uat/phase-05-multihost-gdi.md` human checklist | ❌ Wave 0 |
| **UAT only** | Drag-reorder visual feedback (2px insertion line) | manual | `tests/uat/phase-05-drag.md` human checklist | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** `dotnet test --filter "FullyQualifiedName~Tabs|FullyQualifiedName~TabReorder|FullyQualifiedName~KeyboardShortcut"` (<15s)
- **Per wave merge:** `dotnet test` full suite (<60s excluding SkippableFact live-RDP tests)
- **Phase gate:** Full suite green AND manual UAT items checked AND Phase 4 GDI baseline re-verified on 14 concurrent sessions

### Wave 0 Gaps

- [ ] `tests/Deskbridge.Tests/Tabs/TabHostManagerTests.cs` — unit coverage for TAB-01 / TAB-04 / TAB-05 / D-02 / D-07 / D-14 / D-16 (~10 tests)
- [ ] `tests/Deskbridge.Tests/Tabs/TabItemViewModelTests.cs` — TabState transitions, IsConnecting/IsReconnecting/IsError derived properties
- [ ] `tests/Deskbridge.Tests/Integration/HostContainerPersistenceTests.cs` — [Collection("RDP-STA")] integration using STAFixture — WFH parenting invariants, Visibility switching
- [ ] `tests/Deskbridge.Tests/Integration/KeyboardShortcutTests.cs` — [Collection("RDP-STA")] — simulate KeyDown, verify routed commands
- [ ] `tests/Deskbridge.Tests/Integration/TabReorderTests.cs` — [Collection("RDP-STA")] — drag gesture simulation via ItemsControl + DataObject
- [ ] `tests/Deskbridge.Tests/Smoke/AppCloseGdiBaselineTests.cs` — [SkippableFact] requiring DESKBRIDGE_SMOKE_RDP_HOST, opens 14 real RDP sessions, closes app, verifies GDI returns to baseline
- [ ] `tests/uat/phase-05-keyboard.md` — human checklist: Ctrl+Tab with RDP focused, Ctrl+Shift+T, Ctrl+1..9, Ctrl+F4
- [ ] `tests/uat/phase-05-multihost-gdi.md` — human checklist: open 14 real sessions sequentially, observe GDI via Process Explorer, close, verify baseline return
- [ ] `tests/uat/phase-05-drag.md` — human checklist: drag reorder visual feedback + ensure drag of last session does not tear down RDP connection
- [ ] `tests/uat/phase-05-state-matrix.md` — human checklist: TabState transitions (Connecting → Connected → Reconnecting → Error) produce exactly one visible indicator (mutual exclusion per UI-SPEC)

## Security Domain

Security enforcement is NOT disabled in config.json (no `security_enforcement: false`), so this section is required.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|------------------|
| V2 Authentication | no | No new auth surface — credentials handled by Phase 3 and 4 stages |
| V3 Session Management | partial | RDP session lifecycle — already covered by IDisconnectPipeline (Phase 4); TabHostManager invokes unchanged |
| V4 Access Control | no | No new access control surface |
| V5 Input Validation | no | Keyboard shortcut Key enum is value-checked; no untrusted input |
| V6 Cryptography | no | No crypto in this phase; credentials written to IMsTscNonScriptable (Phase 4) |
| V7 Error Handling & Logging | yes | Log `ex.GetType().Name` + `HResult:X8` only — never `ex.Message` for COM exceptions (Phase 4 T-04-EXC pattern); never log ConnectionId or Hostname with credential material |
| V8 Data Protection | yes | `ConnectionContext.ResolvedPassword` cleared after use (Phase 4 RdpHostControl.cs:130 pattern stays); no new password-carrying fields |
| V11 Business Logic | no | Threshold logic (15+ session warning) is advisory; no policy enforcement |

### Known Threat Patterns for WPF/WindowsFormsHost multi-host

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Logging credentials in structured logs (e.g., `_logger.LogDebug("Closing tab {@Context}")` where `@Context` serializes `ConnectionContext` including `ResolvedPassword`) | Information Disclosure | Log only `ConnectionId` (Guid) and `Hostname` (non-sensitive). Never pass `ConnectionContext` to structured logging. Phase 4 Pitfall 5 pattern — extended to TabHostManager. Unit test: serialize any logged object and assert no `ResolvedPassword` substring. |
| Exception message leak (COM `ex.Message` may contain network target detail or credential state) | Information Disclosure | `ex.GetType().Name` + `ex.HResult:X8` only. Phase 4 WR-05 pattern; applies to all TabHostManager catch blocks |
| Host dict enumeration exposes all connections to XAML (e.g., `ICommand.CanExecute` iterating `_hosts`) | Information Disclosure (minor) | TabHostManager exposes `IReadOnlyCollection<IProtocolHost>` not `Dictionary`; VMs consume only `Guid` IDs and call `GetHost` on demand |
| Tab state in memory across Unlock (SEC-02 / SEC-04) — when Phase 6 lock activates, tab state must not leak through the lock overlay | Information Disclosure | Phase 6 concern; Phase 5 just ensures TabItemViewModel.Title can be cleared/blanked if needed. Add `TabItemViewModel.ClearForLock()` method — Phase 6 scope. |

**Additional notes:**
- No new IPC or process boundaries — TabHostManager lives in the same process as Phase 4 hosts.
- No new file I/O — TabHostManager is fully in-memory (LRU is in-memory; D-16 locks this).
- Drag-drop `DataObject` carries only `TabItemViewModel` references — in-process move only, no clipboard serialization. Matches Phase 3 TreeViewDragDropBehavior DataObject pattern (no BinaryFormatter).

## Sources

### Primary (HIGH confidence)

- [Microsoft Learn — IMsRdpClientAdvancedSettings::BitmapPersistence property](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings-bitmappersistence) — confirms typo spelling and R/W constraint
- [Microsoft Learn — IMsRdpClientAdvancedSettings interface](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings-interface) — confirms "properties cannot be set when the control is connected, unless otherwise indicated" — foundation of Pitfall 1
- `RDP-ACTIVEX-PITFALLS.md` §1 (siting order), §3 (disposal sequence), §5 (keyboard hooks and `KeyboardHookMode=0`), §8 (multi-instance GDI thresholds) — repo root, mandatory reading
- `WINFORMS-HOST-AIRSPACE.md` §Option 3 (persistent container pattern, lines 376-431), §leaks (reflection fixes, lines 449-456), §PerMonitorV2 DPI — repo root, the entire foundation of D-04
- `REFERENCE.md` §Tab Management (lines 550-555), §Architecture §Connection Events (lines 293-295 canonical event records), §Resource Limits (#17 15-20 session limit, #18 `BitmapPeristence=0`) — repo root
- `WPF-UI-PITFALLS.md` §5 (Color vs Brush), §6 (MVVM command generation) — repo root
- `src/Deskbridge.Protocols.Rdp/RdpConnectionConfigurator.cs:48` — `KeyboardHookMode=0` verified
- `src/Deskbridge.Protocols.Rdp/RdpConnectionConfigurator.cs:47` — `BitmapPeristence=0` set at configure time
- `src/Deskbridge.Protocols.Rdp/AirspaceSwapper.cs:35, 121, 131` — multi-host dictionary, iteration pattern, Collapsed-not-Hidden rationale
- `src/Deskbridge.Core/Services/ConnectionCoordinator.cs:28, 82-88, 93-104, 486-506` — single-host state members to remove
- `.planning/phases/04-rdp-integration/04-RESEARCH.md` — Pattern 1-7 carry forward (airspace, disposal, reconnect)
- `.planning/phases/04-rdp-integration/04-CONTEXT.md` D-12 — Phase 5 multi-host foundation
- `.planning/phases/05-tab-management/05-UI-SPEC.md` — locked visual contract
- `.planning/phases/05-tab-management/05-CONTEXT.md` — D-01..D-16 locked

### Secondary (MEDIUM confidence — verified with primary sources)

- [codeblitz.wordpress.com — WPF: ItemsControl Drag Drop Behavior](https://codeblitz.wordpress.com/2009/06/26/wpf-itemscontrol-drag-drop-behavior/) — confirms `ObservableCollection` reorder via `Remove/Insert` or `Move`, cross-verified against WPF docs and Phase 3's `TreeViewDragDropBehavior.cs`
- [Jerry Nixon — Reordering items in a GridView with Drag and Drop](https://blog.jerrynixon.com/2013/06/walkthrough-reordering-items-in.html) — drop-zone pattern reference
- [punker76/gong-wpf-dragdrop GitHub](https://github.com/punker76/gong-wpf-dragdrop) — mature drag-drop library (considered as alternative, rejected per "no new dependencies")

### Tertiary (LOW confidence — needs validation)

- Actipro WindowsFormsHost keyboard focus forum threads (cited in web search) — anecdotal reports that PreviewKeyDown can fail while WFH has focus in DockSite scenarios. **Flagged as Assumption A1** — must be UAT-validated. If it fails, the Phase 6 `RegisterHotKey` path is pulled forward.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages, all Phase 4 stack carries forward
- Architecture (TabHostManager shape, persistent container, Visibility switching): HIGH — direct translation of WINFORMS-HOST-AIRSPACE §Option 3 + CONTEXT.md locked decisions
- BitmapPeristence semantics: HIGH — verified against Microsoft Learn docs
- Keyboard routing (PreviewKeyDown with KeyboardHookMode=0): MEDIUM-HIGH — textually cited in RDP-ACTIVEX-PITFALLS §5, but the empirical "it works while AxHost is focused in WFH" claim is Assumption A1. UAT gate mitigates.
- Drag-reorder: HIGH — direct reuse of Phase 3 idioms; behavior pattern well-established in WPF
- Pitfalls: HIGH — 8 items, each grounded in citations or existing code
- 15-session threshold mechanics: HIGH — simple fire-once flag; verified by unit test design

**Research date:** 2026-04-13
**Valid until:** 2026-05-13 (30 days — WPF / WPF-UI / mstscax surface is stable; CommunityToolkit.Mvvm 8.4.x line remains Roslyn-5 based).

Sources:
- [Microsoft Learn — IMsRdpClientAdvancedSettings::BitmapPersistence property](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings-bitmappersistence)
- [Microsoft Learn — IMsRdpClientAdvancedSettings interface](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings-interface)
- [codeblitz.wordpress.com — WPF: ItemsControl Drag Drop Behavior](https://codeblitz.wordpress.com/2009/06/26/wpf-itemscontrol-drag-drop-behavior/)
- [punker76/gong-wpf-dragdrop GitHub](https://github.com/punker76/gong-wpf-dragdrop)
