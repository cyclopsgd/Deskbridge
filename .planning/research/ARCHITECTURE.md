# Architecture Patterns

**Domain:** WPF desktop application with COM/ActiveX interop (RDP connection manager)
**Researched:** 2026-04-11

---

## Recommended Architecture

The proposed architecture in REFERENCE.md is **well-structured and validated** against real-world WPF desktop application patterns. The design follows established patterns used by professional RDP managers (Devolutions Remote Desktop Manager, Royal TS) and modern WPF applications. Below is the validated component map with boundary analysis.

### High-Level Component Diagram

```
+--------------------------------------------------------------+
|  Deskbridge (WPF App) -- Composition Root                    |
|  +---------------------------+  +-------------------------+  |
|  |  Views / XAML             |  |  ViewModels             |  |
|  |  - FluentWindow shell     |  |  - MainWindowVM         |  |
|  |  - Connection TreeView    |  |  - ConnectionTreeVM     |  |
|  |  - Tab bar                |  |  - TabBarVM             |  |
|  |  - Status bar             |  |  - CommandPaletteVM     |  |
|  |  - Command palette        |  |  - ConnectionEditorVM   |  |
|  |  - Lock overlay           |  +----------+--------------+  |
|  +---------------------------+             |                  |
|              |  binds to                   | uses             |
|              v                             v                  |
|  +-----------------------------------------------------------+
|  |  Deskbridge.Core -- Interfaces, Models, Services          |
|  |  +------------------+  +------------------+               |
|  |  | IEventBus        |  | IConnectionPipeline              |
|  |  | (WeakRefMessenger)|  | (Ordered stages) |              |
|  |  +--------+---------+  +--------+---------+               |
|  |           |                      |                         |
|  |  +--------v---------+  +--------v---------+               |
|  |  | IConnectionStore |  | ICredentialService|              |
|  |  | IConnectionQuery |  | IAppLockService   |              |
|  |  | ISettingsStore   |  | IAuditLog         |              |
|  |  | INotificationSvc |  +------------------+               |
|  |  +------------------+                                     |
|  +-----------------------------------------------------------+
|              |                                                |
|              v                                                |
|  +-----------------------------------------------------------+
|  |  Deskbridge.Protocols.Rdp -- ActiveX Interop              |
|  |  +---------------------------+                            |
|  |  | RdpHostControl            |                            |
|  |  | (IProtocolHost)           |                            |
|  |  |  - WindowsFormsHost       |                            |
|  |  |  - AxMsRdpClient9         |                            |
|  |  |  - COM lifecycle mgmt     |                            |
|  |  +---------------------------+                            |
|  +-----------------------------------------------------------+
+--------------------------------------------------------------+
```

### Component Boundaries

| Component | Responsibility | Communicates With | Boundary Type |
|-----------|---------------|-------------------|---------------|
| **Deskbridge** (app) | Composition root, views, view models, DI wiring | Core (via interfaces), Protocols.Rdp (via DI) | Project reference |
| **Deskbridge.Core** | Domain models, service interfaces, event definitions, pipeline | Nothing upstream -- it is the innermost layer | Referenced by all |
| **Deskbridge.Protocols.Rdp** | RDP ActiveX wrapper, COM lifecycle, WindowsFormsHost management | Core (implements IProtocolHost) | Plugin boundary |
| **Event Bus** | Cross-cutting message delivery | All components publish/subscribe | In-process messaging |
| **Connection Pipeline** | Orchestrates connect/disconnect flow as ordered stages | Core services (credentials, audit), protocol hosts | Pipeline/middleware |
| **JSON Stores** | Persistence of connections, settings, audit | File system (%AppData%) | I/O boundary |
| **Windows Credential Manager** | Secure credential storage | OS API via AdysTech wrapper | OS boundary |

**Boundary validation:** The separation between Core (interfaces + models) and the protocol-specific project is the correct pattern. This prevents WinForms type ambiguity (System.Windows.Application vs System.Windows.Forms.Application) by isolating `UseWindowsForms` to the RDP project only. This is a known and documented requirement for WPF/WinForms interop projects.

---

## Data Flow

### Connection Establishment Flow

```
User double-clicks connection in TreeView
    |
    v
ConnectionTreeViewModel publishes ConnectionRequestedEvent
    |
    v
MainWindowViewModel receives event, calls IConnectionPipeline.ConnectAsync()
    |
    v
Pipeline Stage 1: ResolveCredentialsStage (Order 100)
    - Checks CredentialMode: Own / Inherit / Prompt
    - If Inherit: walks group tree upward until credentials found
    - If Prompt: raises UI dialog via event bus
    - Sets ConnectionContext.ResolvedPassword
    |
    v
Pipeline Stage 2: CreateHostStage (Order 200)
    - Resolves IProtocolHost from DI based on Protocol enum
    - Creates RdpHostControl instance
    - Sets on ConnectionContext.Host
    |
    v
Pipeline Stage 3: ConnectStage (Order 300)
    - Creates WindowsFormsHost
    - Sites ActiveX control (Child = rdp) BEFORE configuration
    - Configures server, port, credentials via IMsTscNonScriptable
    - Calls rdp.Connect()
    |
    v
Pipeline Stage 4: PublishConnectedStage (Order 400)
    - Publishes ConnectionEstablishedEvent to event bus
    |
    v
Event Bus fans out:
    - TabBarViewModel opens new tab
    - StatusBarViewModel updates hostname display
    - NotificationService shows "Connected" toast
    - AuditLog records connection
```

### Tab Lifecycle Flow

```
Tab opened (ConnectionEstablishedEvent)
    - TabBarViewModel adds tab to collection
    - Active tab shows live WindowsFormsHost + ActiveX
    - Inactive tabs: BitmapPersistence = 0 (reduce memory)

Tab switched (user clicks different tab)
    - Previous tab's WindowsFormsHost hidden (Visibility.Collapsed)
    - New tab's WindowsFormsHost shown
    - StatusBarViewModel updates via TabSwitchedEvent
    - NOTE: Hidden WindowsFormsHost still consumes GDI handles

Tab closed (user clicks X or Ctrl+W)
    - If connected: rdp.Disconnect() -- wait for OnDisconnected
    - rdp.Dispose()
    - host.Child = null
    - host.Dispose()
    - parentPanel.Children.Remove(host)
    - Publish ConnectionClosedEvent
    - TabBarViewModel removes tab
```

### Credential Inheritance Flow

```
Connection with CredentialMode = Inherit
    |
    v
ResolveCredentialsStage.ExecuteAsync()
    |
    v
Walk up group tree:
    Connection -> Parent Group -> Grandparent Group -> Root
    |
    v
For each group:
    ICredentialService.GetForGroup(groupId)
    If found -> return credentials, stop walking
    |
    v
If no group has credentials:
    Prompt user (publish CredentialRequestedEvent)
```

**Validation:** This credential inheritance pattern is identical to how Devolutions Remote Desktop Manager implements it. Their documentation confirms: "The credential resolver climbs the ladder until it finds a Parent folder and pulls in the credentials." The proposed tree-walk approach is industry-standard for connection managers. (Source: [Devolutions inheritance docs](https://docs.devolutions.net/rdm/kb/knowledge-base/inheritance/))

---

## Architecture Decision Validation

### 1. WeakReferenceMessenger as Event Bus

**Verdict: VALIDATED -- correct choice**
**Confidence: HIGH** (verified via [official Microsoft docs](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/messenger))

**Strengths:**
- Already a CommunityToolkit.Mvvm dependency -- zero additional packages
- Weak references prevent memory leaks from forgotten subscriptions (critical for long-running desktop app with dynamic tabs)
- Thread-safe singleton via `WeakReferenceMessenger.Default`
- Supports typed messages, channels (tokens), and request/response patterns
- `ObservableRecipient` base class provides automatic registration/deregistration lifecycle

**Concerns addressed:**
- **Performance:** WeakReferenceMessenger has higher overhead than StrongReferenceMessenger due to weak reference tracking. For a desktop app with ~15-20 connections and moderate event volume, this is irrelevant. Performance matters at thousands of messages per second; connection events fire at human-interaction speed.
- **Message type inheritance not supported:** `IMessenger` implementations do not deliver to base type subscribers. If you publish `RdpConnectionEstablishedEvent`, subscribers to `ConnectionEstablishedEvent` will NOT receive it. The proposed flat event hierarchy (all records in Events/) correctly avoids this trap.
- **Lambda capture:** The docs recommend using the `(r, m)` delegate pattern to avoid capturing `this` in lambdas. The proposed `IEventBus` wrapper should enforce this pattern internally.

**Architectural note:** Wrapping `WeakReferenceMessenger` behind `IEventBus` is good practice for testability, but keep the wrapper thin. Do not re-implement routing logic -- delegate directly to the messenger. The wrapper's value is in DI registration and unit test mocking.

### 2. Connection Pipeline Pattern (Ordered Stages)

**Verdict: VALIDATED -- strong pattern choice**
**Confidence: HIGH** (pipeline/middleware is a well-established .NET pattern)

**Strengths:**
- Directly analogous to ASP.NET Core middleware pipeline, familiar to .NET developers
- Open/closed principle: new stages added without modifying existing ones
- Order property provides deterministic execution sequence
- `ConnectionContext` as the shared bag between stages is clean
- Abort semantics via `PipelineResult.Success = false` allow any stage to short-circuit

**Concerns and recommendations:**

1. **Disconnect pipeline is missing.** The REFERENCE.md defines the connect pipeline in detail but disconnect is only mentioned as a strict disposal sequence. Disconnection should also be a pipeline with stages: PreDisconnect (save state), Disconnect (call rdp.Disconnect), Dispose (cleanup COM), PublishDisconnected, AuditDisconnect. This matters because future features (session recording, state save) need disconnect hooks.

2. **Error handling between stages.** If `ConnectStage` throws (COM exception, network timeout), the pipeline must ensure `CreateHostStage`'s resources are cleaned up. Recommend: pipeline runner wraps each stage in try/catch; on failure, run a compensation/rollback path that disposes any `IProtocolHost` already created on the context. Without this, a failed connection attempt leaks a WindowsFormsHost.

3. **Async cancellation.** The `CancellationToken` on `ConnectionContext` is good. Ensure the pipeline runner checks for cancellation between stages, not just within stages. A user clicking "Cancel" during credential resolution should not proceed to host creation.

4. **Stage ordering gaps.** The 100/200/300/400/500/900 gaps are well-chosen -- they leave room for insertion (150 for health check, 450 for monitor). This is standard practice.

### 3. IProtocolHost Abstraction for Protocol Plugins

**Verdict: VALIDATED -- correct abstraction level**
**Confidence: HIGH**

**Strengths:**
- Clean plugin boundary: each protocol implements one interface
- DI-resolved by protocol type -- no factory switches needed
- RdpHostControl encapsulates all COM/ActiveX complexity behind the interface
- Future SSH (terminal emulator) and VNC plug in without touching core

**Recommendations:**

1. **Define IProtocolHost explicitly in Core.** It should expose at minimum:
   - `ConnectAsync(ConnectionContext context)` -- initiate connection
   - `Disconnect()` -- graceful disconnect
   - `Dispose()` -- full cleanup
   - `UIElement GetVisualElement()` -- returns the WindowsFormsHost (or a WPF control for future protocols)
   - `ConnectionState State { get; }` -- Connected/Disconnected/Connecting/Error
   - `event EventHandler<DisconnectEventArgs> Disconnected` -- so tab manager knows when remote end drops

2. **Transient registration is correct.** Each connection gets its own `RdpHostControl` instance. Singleton would be wrong -- each tab has its own ActiveX control.

### 4. WindowsFormsHost for ActiveX Siting

**Verdict: VALIDATED -- the only viable approach**
**Confidence: HIGH** (verified via [Microsoft docs](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/walkthrough-hosting-an-activex-control-in-wpf))

WindowsFormsHost is the only supported way to host ActiveX controls in WPF. There is no pure-WPF alternative.

**Airspace problem status:** The WPF airspace issue (dotnet/wpf#152) remains **open with a "Future" milestone** and no planned fix. The `IsRedirected` property that was prototyped in .NET 4.5 was **removed before release** due to stability concerns and will not return. The proposed mitigation (bitmap snapshot during drag/resize, no WPF overlays on viewport) is the correct and standard workaround. (Source: [dotnet/wpf#152](https://github.com/dotnet/wpf/issues/152))

**Key implementation rules (all present in REFERENCE.md, validated):**
- Site before configure (add to container BEFORE setting properties)
- Strict disposal order (disconnect -> dispose rdp -> null child -> dispose host -> remove from parent)
- Never call `Marshal.ReleaseComObject` -- let AxHost.Dispose() handle it
- `UseWindowsForms` only in the RDP project csproj

**GDI handle concern:** Windows has a default per-process limit of 10,000 GDI objects. Each RDP ActiveX control consumes multiple GDI handles. The proposed ~15-20 session practical limit is reasonable and conservative. Monitor GDI handle count in debug builds. (Source: [Raymond Chen / The Old New Thing](https://devblogs.microsoft.com/oldnewthing/20210831-00/?p=105624))

**WindowsFormsHost memory leak risk:** A known issue exists where `WindowsFormsHost` can remain in memory after Dispose() due to `HwndSourceKeyboardInputSite` holding references. Mitigation: ensure complete removal from visual tree (not just Dispose), and explicitly null out `Child` before disposing. The proposed disposal sequence in REFERENCE.md already handles this correctly.

### 5. JSON-Only Persistence (No SQLite)

**Verdict: VALIDATED for v1 scope with caveats**
**Confidence: MEDIUM**

**Why it works for v1:**
- Connection count scale: dozens to hundreds, not thousands. JSON handles this.
- Human-readable and diffable -- useful for debugging and version control.
- No external dependency (System.Text.Json is in-box).
- Single-user desktop app -- no concurrent write pressure from multiple processes.

**Caveats and recommendations:**

1. **Atomic writes are mandatory.** Write to a temp file, then rename/move to the target path. A crash during write corrupts the file. This is documented in community patterns and is critical for a desktop app managing connection data.

   ```csharp
   // Write pattern
   var tempPath = path + ".tmp";
   await File.WriteAllTextAsync(tempPath, json);
   File.Move(tempPath, path, overwrite: true);
   ```

2. **Backup on save.** Keep `connections.json.bak` as a copy of the previous version before each save. One corrupted save should not lose all connections.

3. **File locking for audit log.** The append-only `audit.jsonl` file needs `FileShare.Read` locking so external tools can read it while the app writes. Use `FileStream` with explicit sharing flags, not `File.AppendAllText`.

4. **Scale ceiling.** JSON is fine for hundreds of connections. At thousands (unlikely for v1), load/parse time becomes noticeable. This is a v2 concern, not v1.

5. **No schema migration.** JSON has no built-in schema versioning. Add a `"version": 1` field to the root of `connections.json` from day one, so future format changes can be handled with migration logic.

### 6. Credential Inheritance via Group Tree Walk

**Verdict: VALIDATED -- industry-standard pattern**
**Confidence: HIGH** (verified against Devolutions Remote Desktop Manager)

The proposed credential inheritance (connection -> parent group -> grandparent -> root, first match wins) matches exactly how Devolutions RDM and Royal TS implement credential inheritance. The key design decisions are correct:

- **CredentialMode enum (Own/Inherit/Prompt):** Matches RDM's approach. `Inherit` is the correct default.
- **Windows Credential Manager with TERMSRV/ and DESKBRIDGE/GROUP/ prefixes:** Clean separation between connection-level and group-level credentials.
- **Inheritance logic in pipeline stage, not credential service:** Correct separation of concerns. The credential service is a storage wrapper; the pipeline stage contains the business logic.

**One concern:** The tree walk requires access to `IConnectionStore` to traverse parent groups. The `ResolveCredentialsStage` should receive this via DI constructor injection, not by passing `IConnectionStore` as a parameter to `ICredentialService.ResolveInherited()`. The current REFERENCE.md interface shows `ResolveInherited(connection, connectionStore)` on the credential service -- this couples the storage abstraction to the resolution logic. Better: move the tree-walk entirely into the pipeline stage, and have the credential service be a pure get/set wrapper.

---

## Patterns to Follow

### Pattern 1: Composition Root in App.xaml.cs

**What:** All DI registration happens in a single place. No service creates another service via `new`.
**When:** Always -- this is the standard .NET DI pattern.
**Confidence: HIGH** (verified via [Microsoft docs on CommunityToolkit IoC](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/ioc))

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    var services = new ServiceCollection();
    ConfigureServices(services);
    _serviceProvider = services.BuildServiceProvider();
    
    var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
    mainWindow.Show();
}
```

The proposed registration in REFERENCE.md is correct. One addition: register `IServiceProvider` itself so that factories can resolve protocol hosts by type at runtime.

### Pattern 2: MVVM with Source Generators

**What:** Use `[ObservableProperty]`, `[RelayCommand]` from CommunityToolkit.Mvvm to eliminate boilerplate.
**When:** All view models.

```csharp
public partial class ConnectionTreeViewModel : ObservableRecipient
{
    [ObservableProperty]
    private ObservableCollection<ConnectionNode> _connections = new();
    
    [RelayCommand]
    private async Task ConnectAsync(ConnectionModel connection)
    {
        await _pipeline.ConnectAsync(connection);
    }
}
```

**Note:** Use `ObservableRecipient` as the base class for view models that need event bus integration. It provides automatic `IsActive` lifecycle management for message registration.

### Pattern 3: Bitmap Snapshot During Resize

**What:** Capture a static image of the RDP control during window drag/resize to prevent flicker.
**When:** Always when hosting ActiveX via WindowsFormsHost.

```csharp
// Handle WM_ENTERSIZEMOVE
var renderTarget = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
renderTarget.Render(windowsFormsHost);
snapshotImage.Source = renderTarget;
snapshotImage.Visibility = Visibility.Visible;
windowsFormsHost.Visibility = Visibility.Collapsed;

// Handle WM_EXITSIZEMOVE
snapshotImage.Visibility = Visibility.Collapsed;
windowsFormsHost.Visibility = Visibility.Visible;
```

**Confidence: HIGH** -- this is the documented mitigation from Microsoft's own airspace guidance (Dwayne Need / MSFT archived blog post). (Source: [Mitigating Airspace Issues](https://learn.microsoft.com/en-us/archive/blogs/dwayneneed/mitigating-airspace-issues-in-wpf-applications))

### Pattern 4: Per-Connection Error Isolation

**What:** Wrap all ActiveX/COM calls in try/catch. A failure in one RDP session must not crash the application or affect other sessions.
**When:** Every interaction with the RDP ActiveX control.

```csharp
try
{
    rdp.Connect();
}
catch (COMException ex) when (ex.HResult == ...)
{
    _eventBus.Publish(new ConnectionFailedEvent(connection, ex.Message, ex));
    // Clean up this session only
}
```

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Direct ViewModel-to-ViewModel Communication

**What:** ViewModels calling methods on other ViewModels directly.
**Why bad:** Creates tight coupling, makes testing hard, breaks when components are added/removed.
**Instead:** Publish events via IEventBus. TabBarViewModel subscribes to ConnectionEstablishedEvent -- it never knows about ConnectionTreeViewModel.

### Anti-Pattern 2: Marshal.ReleaseComObject on RDP ActiveX

**What:** Manually calling `Marshal.ReleaseComObject()` on the RDP COM objects.
**Why bad:** Can cause use-after-free crashes. The AxHost base class manages COM reference counting internally.
**Instead:** Let `AxHost.Dispose()` handle COM cleanup via the documented disposal sequence.

### Anti-Pattern 3: Overlapping WPF Elements on ActiveX Viewport

**What:** Placing WPF tooltips, popups, or overlays on top of the WindowsFormsHost area.
**Why bad:** Airspace problem -- WinForms/ActiveX always renders on top of WPF content. The overlay will appear behind the RDP session.
**Instead:** Place notifications in a separate panel (bottom-right stack). Use the lock overlay only when the entire viewport is hidden.

### Anti-Pattern 4: Singleton Protocol Hosts

**What:** Registering RdpHostControl as a singleton in DI.
**Why bad:** Each connection needs its own ActiveX control instance. A singleton means one connection at a time.
**Instead:** Register as Transient. Each pipeline execution creates a new instance via DI.

### Anti-Pattern 5: UseWindowsForms in Directory.Build.props

**What:** Setting `<UseWindowsForms>true</UseWindowsForms>` globally.
**Why bad:** Causes type ambiguity between `System.Windows.Application` (WPF) and `System.Windows.Forms.Application` (WinForms) throughout the solution.
**Instead:** Set it only in `Deskbridge.Protocols.Rdp.csproj`.

---

## Structural Concerns Identified

### Concern 1: Missing Disconnect Pipeline

**Severity: MODERATE**
The REFERENCE.md defines a detailed connect pipeline but treats disconnection as a simple sequence. Disconnect should be a pipeline too, because:
- Future features (session state save, recording stop) need hooks
- Error handling during disconnect (COM exceptions) needs structured handling
- Audit logging needs consistent pre/post disconnect events

**Recommendation:** Add `IConnectionPipeline.DisconnectAsync(ConnectionContext context)` with stages: SaveState (100), Disconnect (200), Dispose (300), PublishDisconnected (400), Audit (900).

### Concern 2: ConnectionContext Lifetime Ambiguity

**Severity: LOW**
`ConnectionContext` is created during connect but also needed during disconnect (to carry the `IProtocolHost` reference). Where does it live between connect and disconnect?

**Recommendation:** The tab/session manager should hold a `Dictionary<Guid, ConnectionContext>` mapping connection IDs to their contexts. When disconnect is triggered, look up the context. This also gives you a single place to query "what's currently connected."

### Concern 3: Thread Affinity for ActiveX Operations

**Severity: MODERATE**
All ActiveX/COM operations on the RDP control must happen on the STA UI thread. If any pipeline stage runs on a background thread and then tries to call `rdp.Connect()`, it will fail or deadlock.

**Recommendation:** The `ConnectStage` and any stage that touches the ActiveX control must marshal back to the UI thread (via `Dispatcher.InvokeAsync`). The pipeline runner should document this clearly. Alternatively, run the entire pipeline on the UI thread and only offload I/O-bound work (credential lookup) to background tasks within individual stages.

### Concern 4: No Schema Version in JSON Files

**Severity: LOW (for v1)**
The `connections.json` format will evolve across versions. Without a version field, migration logic has no way to detect format changes.

**Recommendation:** Add `"schemaVersion": 1` to the JSON root from the very first release.

---

## Suggested Build Order (Dependencies)

The build order in REFERENCE.md is correct. Here is the dependency rationale:

```
Phase 1: Solution Scaffold
    - No dependencies. Creates project structure, build config, moves interop DLLs.

Phase 2: Core Services
    - Depends on: Phase 1 (projects exist)
    - Creates: IEventBus, IConnectionPipeline interfaces, models, events
    - WHY FIRST: Everything else depends on Core interfaces

Phase 3: WPF Shell
    - Depends on: Phase 1 (app project), Phase 2 (ViewModels need interfaces)
    - Creates: FluentWindow, icon rail, slide-out, tab bar, status bar
    - WHY NEXT: Need a visual shell to host anything

Phase 4: Connection Management
    - Depends on: Phase 2 (IConnectionStore, models), Phase 3 (TreeView host)
    - Creates: JSON persistence, TreeView, connection editor, credential storage
    - WHY NEXT: Need connections to exist before you can connect to them

Phase 5: RDP Integration
    - Depends on: Phase 2 (IProtocolHost, pipeline), Phase 3 (viewport host),
                  Phase 4 (connections with credentials)
    - Creates: RdpHostControl, siting, disposal, pipeline stages
    - WHY HERE: This is the core value -- but requires all foundations

Phase 6: Tab Management
    - Depends on: Phase 3 (tab bar UI), Phase 5 (RDP sessions to tab)
    - Creates: Tab open/close/switch, active-only rendering
    - WHY AFTER RDP: Tabs are meaningless without sessions to display

Phase 7: Command Palette + Keyboard Shortcuts
    - Depends on: Phase 2 (IConnectionQuery), Phase 4 (connections to search)
    - Creates: Fuzzy search, command registration, keyboard bindings
    - CAN PARALLEL with Phase 6 -- no dependency between them

Phase 8: Notifications, Window State, Logging, Audit
    - Depends on: Phase 2 (events to subscribe to), Phase 3 (UI to show toasts)
    - Creates: Toast stack, Serilog, audit trail, window state persistence
    - CAN PARALLEL with Phase 6/7 -- event bus makes these independent

Phase 9: Auto-Update (Velopack)
    - Depends on: Phase 1 (custom Main for Velopack), Phase 3 (status bar)
    - Creates: Update check, download, apply/restart
    - LATE: Needs working app first

Phase 10: Import/Export
    - Depends on: Phase 4 (connection store to import into)
    - Creates: mRemoteNG XML parser, JSON/CSV export
    - LAST: Migration feature, not core functionality
```

**Critical path:** 1 -> 2 -> 3 -> 4 -> 5 -> 6 (sequential, each depends on prior)
**Parallelizable:** 7 and 8 can run alongside 6. 9 and 10 can run alongside each other after 6.

---

## Alternative Considered: RoyalApps.Community.Rdp.WinForms

Investigated `RoyalApps.Community.Rdp.WinForms` (v1.4.0, targets .NET 10) as a potential replacement for direct aximp interop. This package wraps the RDP ActiveX in a managed WinForms control with a cleaner API.

**Why NOT use it:**
- Still requires WindowsFormsHost in WPF (it is a WinForms control)
- Adds a dependency on MsRdpEx (API hooking library), which is complex and may conflict with enterprise security policies
- Less control over the ActiveX lifecycle -- the wrapper owns disposal
- Direct aximp gives full access to `IMsTscNonScriptable` and all advanced settings
- The project explicitly chose classic aximp, and the generated DLLs are already in the repo

**When to reconsider:** If Deskbridge v2 needs to support both MSTSC and MSRDC (modern RDP client), RoyalApps provides that abstraction. Not needed for v1.

---

## Scalability Considerations

| Concern | At 5 sessions | At 15 sessions | At 20+ sessions |
|---------|---------------|----------------|-----------------|
| GDI handles | No concern | Monitor via Task Manager | Warn user, may hit limits |
| Memory | ~200MB | ~600MB | ~800MB+, depends on session resolution |
| Event bus throughput | Trivial | Trivial | Trivial (human-speed events) |
| JSON load time | Instant | Instant | Instant (hundreds of connections) |
| Tab switching | Immediate | Immediate | Immediate (hide/show, not recreate) |
| COM stability | Stable | Stable | Risk of GDI exhaustion crashes |

The 15-20 session limit is a hardware/OS constraint (GDI handles, memory), not an architecture constraint. The architecture supports it correctly.

---

## Sources

- [Microsoft: Host an ActiveX control in WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/walkthrough-hosting-an-activex-control-in-wpf) -- HIGH confidence
- [Microsoft: WPF and Windows Forms interop](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-windows-forms-interoperation) -- HIGH confidence
- [CommunityToolkit.Mvvm Messenger docs](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/messenger) -- HIGH confidence
- [dotnet/wpf#152: Airspace issue](https://github.com/dotnet/wpf/issues/152) -- HIGH confidence (open issue, no fix planned)
- [Devolutions: Credential inheritance](https://docs.devolutions.net/rdm/kb/knowledge-base/inheritance/) -- HIGH confidence
- [Raymond Chen: GDI handle limits](https://devblogs.microsoft.com/oldnewthing/20210831-00/?p=105624) -- HIGH confidence
- [Mitigating Airspace Issues (MSFT archived)](https://learn.microsoft.com/en-us/archive/blogs/dwayneneed/mitigating-airspace-issues-in-wpf-applications) -- HIGH confidence
- [Pipeline Design Pattern in .NET](https://medium.com/pragmatic-programming/net-things-pipeline-design-pattern-bb27e65e741e) -- MEDIUM confidence
- [WindowsFormsHost memory leak discussion](https://social.msdn.microsoft.com/Forums/en-US/b24d717b-4aee-4e74-b418-766f2da9f67e/) -- MEDIUM confidence
- [mRemoteNG memory issues](https://github.com/mRemoteNG/mRemoteNG/issues/432) -- MEDIUM confidence
- [RoyalApps.Community.Rdp](https://github.com/royalapplications/royalapps-community-rdp) -- MEDIUM confidence
