# Deskbridge — Project Reference

This file is the single source of truth for the Deskbridge project. Claude Code should read this before every task.

---

## What Is Deskbridge

A modern Windows desktop RDP connection manager built to replace mRemoteNG. Designed for enterprise infrastructure teams who manage dozens to hundreds of remote connections daily. Tabbed multi-session management with a clean, compact dark UI, proper resource cleanup, and auto-update from GitHub.

This is not a reskin of mRemoteNG. The architecture is built for extensibility — every major subsystem communicates through interfaces, events, and pipelines so that new features (protocols, credential backends, session monitoring) slot in without touching existing code.

### What's Wrong With mRemoteNG
- Freezes and memory leaks from poor ActiveX disposal
- Dated WinForms UI
- Brittle XML configuration
- No auto-update
- Tightly coupled codebase that makes adding features risky
- No keyboard-first workflows
- No session health visibility
- No extensibility model

### Core Goals
- Tabbed RDP sessions with proper COM resource cleanup
- Modern dark UI via WPF-UI (Fluent) — compact chrome, maximum viewport
- Keyboard-first interaction via command palette
- Enterprise-ready architecture with event bus, connection pipeline, and DI
- Auto-update from GitHub Releases via Velopack
- mRemoteNG connection import for painless migration
- Modular protocol architecture for future SSH/VNC

---

## Current State

### Fresh Start
This is a greenfield project. No code exists yet. The only pre-existing assets are the RDP interop assemblies (`MSTSCLib.dll` and `AxMSTSCLib.dll`) generated via `aximp.exe`, which should be placed in the repo root and moved to `src/Deskbridge.Protocols.Rdp/Interop/` during scaffold.

### Build Order
1. Solution scaffold — projects, shared build config, app manifest, interop DLLs positioned
2. Core services — DI container, event bus, notification service, connection pipeline interfaces
3. WPF shell — WPF-UI FluentWindow, dark theme, icon rail, slide-out panel, tab bar, status bar, viewport layout
4. Connection management — model persistence (JSON), TreeView, connection editor dialog, credential storage
5. RDP integration — ActiveX wrapper, siting, disposal, connect/disconnect lifecycle via pipeline
6. Tab management — open/close/switch tabs, active-only rendering, disposal on close
7. Command palette and keyboard shortcuts
8. Notifications, window state persistence, logging, audit log
9. Auto-update via Velopack + GitHub Actions release pipeline
10. mRemoteNG import and export

---

## Technology Stack

| Component | Choice | Version | Notes |
|-----------|--------|---------|-------|
| Framework | .NET 10 | Latest SDK | LTS — supported until November 2028 |
| UI Framework | WPF | (included in SDK) | |
| UI Library | WPF-UI | Latest stable | Fluent dark theme, modern controls, snap layout support. Check NuGet for .NET 10 compatible version. |
| Language | C# 14 | (included with .NET 10) | Partial properties supported natively |
| MVVM | CommunityToolkit.Mvvm | 8.4.2+ | Requires 8.4.1+ for C# 14 / .NET 10 compatibility |
| DI Container | Microsoft.Extensions.DependencyInjection | 10.0.x | Standard .NET DI |
| RDP | AxMSTSCLib | Via aximp.exe | Classic COM interop only |
| Credentials | AdysTech.CredentialManager | 3.1.0+ | Check NuGet for .NET 10 compatible version |
| Auto-update | Velopack | Latest stable | GitHub Releases via GithubSource. Supports .NET 6+. |
| Logging | Serilog + Serilog.Sinks.File | Latest | Rolling file in %AppData% |
| Serialisation | System.Text.Json | (included in .NET 10) | |

### Do NOT Use
- `CredentialManagement` NuGet — targets .NET Framework 3.5 only
- `SecureString` — not encrypted in modern .NET (Microsoft DE0001)
- New COM source generators (`[GeneratedComInterface]`) — don't support ActiveX yet (check if this has changed for .NET 10 before scaffold)
- `Marshal.ReleaseComObject` manually — let AxHost.Dispose() handle it
- .NET 8 or .NET 9 — both reach end of support November 2026

### Pre-Scaffold Verification
Before creating the solution, verify these packages support `net10.0-windows`:
- WPF-UI — check https://www.nuget.org/packages/wpf-ui/
- AdysTech.CredentialManager — check https://www.nuget.org/packages/AdysTech.CredentialManager/
- Velopack — check https://www.nuget.org/packages/velopack/
- CommunityToolkit.Mvvm — check https://www.nuget.org/packages/CommunityToolkit.Mvvm/

If any package doesn't yet target .NET 10 explicitly, `net8.0-windows` compatible packages should work on .NET 10 via forward compatibility. Note this and pin the version.

---

## Critical Compatibility Constraints

These are non-negotiable. Every task must respect them.

### COM / ActiveX
1. RDP interop via classic `aximp.exe` only. The generated DLLs should be placed in `src/Deskbridge.Protocols.Rdp/Interop/`.
2. The RDP ActiveX control MUST be sited (added to a container) BEFORE setting any properties — otherwise `AxHost.InvalidActiveXStateException`.
3. Passwords require casting through `IMsTscNonScriptable` from `GetOcx()`.
4. Never call `Marshal.ReleaseComObject` manually.
5. Disposal order is critical:
   ```csharp
   if (rdp.Connected != 0) rdp.Disconnect();
   rdp.Dispose();
   host.Child = null;
   host.Dispose();
   parentPanel.Children.Remove(host);
   ```

### WPF / WindowsFormsHost
6. Airspace problem: WinForms/ActiveX content always renders on top of WPF. No WPF elements may overlap the RDP viewport.
7. App manifest required declaring Windows 10/11 support and `PerMonitorV2` DPI awareness.
8. `UseWindowsForms` must be set ONLY in the RDP protocol project (not Directory.Build.props) to avoid type ambiguity between System.Windows.Application (WPF) and System.Windows.Forms.Application (WinForms).
9. During window drag/resize: suspend RDP control redraw (handle WM_ENTERSIZEMOVE / WM_EXITSIZEMOVE), show static bitmap snapshot of session, resize on drop.

### Credentials & Security
10. Use `AdysTech.CredentialManager` with `DESKBRIDGE/CONN/<connectionId>` target for connection-specific credentials, and `DESKBRIDGE/GROUP/<guid>` target for group-level credentials.
11. Never store passwords in JSON config files or log files. The JSON stores `CredentialMode` (Own/Inherit/Prompt) but never the actual password.
12. Do NOT use `SecureString`.

### Velopack
13. Requires custom `Main` method — `App.xaml` must be `Page`, not `ApplicationDefinition`.
14. SemVer2 only (e.g., `1.0.0`).
15. User data must be in `%AppData%/Deskbridge`, not alongside the exe.
16. Publish as self-contained.

### Resource Limits
17. Practical limit of ~15-20 simultaneous RDP sessions per process (GDI handles).
18. For inactive tabs, set `AdvancedSettings.BitmapPeristence = 0`.

---

## Required Configuration Files

### Directory.Build.props
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

Note: `UseWindowsForms` is NOT set here. It goes only in the RDP protocol project's `.csproj`.

### App.manifest
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
    </windowsSettings>
  </application>
</assembly>
```

### Velopack Entry Point (add when building auto-update)
```xml
<!-- In Deskbridge.csproj -->
<ItemGroup>
  <ApplicationDefinition Remove="App.xaml" />
  <Page Include="App.xaml" />
</ItemGroup>
```

```csharp
// Program.cs
[STAThread]
static void Main(string[] args)
{
    VelopackApp.Build().Run();
    var app = new App();
    app.InitializeComponent();
    app.Run();
}
```

### .gitignore
```
bin/
obj/
*.user
.vs/
*.suo
*.DotSettings.user
.gsd/
.planning/
*.db
```

---

## Architecture

### Design Principles
1. **Everything through interfaces** — no concrete dependencies between subsystems. All services registered in DI container.
2. **Event-driven communication** — subsystems communicate via an event bus, never by direct reference. Adding a feature means subscribing to events, not modifying existing code.
3. **Pipeline pattern for connections** — connecting to a host is not a single method call. It's a pipeline with hooks that features plug into.
4. **Queryable data** — connection storage supports rich filtering, tagging, and fuzzy search from day one. The command palette, quick switch, and tree filter all consume the same query interface.

### Project Structure
```
Deskbridge/
├── src/
│   ├── Deskbridge/                  # WPF app — views, view models, DI composition root
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/
│   │   ├── Themes/
│   │   ├── Interop/
│   │   │   └── NativeMethods.cs
│   │   ├── App.xaml
│   │   ├── Program.cs
│   │   ├── MainWindow.xaml
│   │   └── app.manifest
│   ├── Deskbridge.Core/             # Models, interfaces, services, event bus, pipeline
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── Interfaces/
│   │   ├── Events/
│   │   └── Pipeline/
│   ├── Deskbridge.Protocols.Rdp/
│   │   ├── Interop/
│   │   │   ├── MSTSCLib.dll
│   │   │   └── AxMSTSCLib.dll
│   │   └── RdpHostControl.cs
│   └── Deskbridge.Protocols.Ssh/    # Future — empty placeholder
├── .github/
│   └── workflows/
│       └── release.yml
├── Directory.Build.props
├── REFERENCE.md
├── .gitignore
└── Deskbridge.sln
```

### Dependency Injection

Use `Microsoft.Extensions.DependencyInjection` as the composition root in `App.xaml.cs`. All services are registered as interfaces. No service should `new` up another service directly.

```csharp
// App.xaml.cs — composition root
var services = new ServiceCollection();

// Core services
services.AddSingleton<IEventBus, EventBus>();
services.AddSingleton<INotificationService, NotificationService>();
services.AddSingleton<IConnectionStore, JsonConnectionStore>();
services.AddSingleton<IConnectionQuery, ConnectionQueryService>();
services.AddSingleton<ICredentialService, WindowsCredentialService>();
services.AddSingleton<ISettingsStore, JsonSettingsStore>();
services.AddSingleton<IConnectionPipeline, ConnectionPipeline>();
services.AddSingleton<IAuditLog, AuditLogService>();
services.AddSingleton<IAppLockService, AppLockService>();

// Protocol hosts — keyed by protocol type
services.AddTransient<RdpProtocolHost>();

// View models
services.AddTransient<MainWindowViewModel>();
services.AddTransient<ConnectionTreeViewModel>();
services.AddTransient<ConnectionEditorViewModel>();
services.AddTransient<CommandPaletteViewModel>();
services.AddTransient<TabBarViewModel>();
```

### Event Bus

Central message broker. Subsystems publish and subscribe without knowing about each other. Use `CommunityToolkit.Mvvm`'s `WeakReferenceMessenger` as the implementation — it's already a dependency and handles weak references (no memory leaks from forgotten subscriptions).

```csharp
// In Deskbridge.Core/Interfaces/
public interface IEventBus
{
    void Publish<TEvent>(TEvent message) where TEvent : class;
    void Subscribe<TEvent>(object recipient, Action<TEvent> handler) where TEvent : class;
    void Unsubscribe<TEvent>(object recipient) where TEvent : class;
}
```

```csharp
// In Deskbridge.Core/Events/
public record ConnectionRequestedEvent(ConnectionModel Connection);
public record ConnectionEstablishedEvent(ConnectionModel Connection, IProtocolHost Host);
public record ConnectionFailedEvent(ConnectionModel Connection, string Reason, Exception? Exception);
public record ConnectionClosedEvent(ConnectionModel Connection, DisconnectReason Reason);
public record ReconnectingEvent(ConnectionModel Connection, int Attempt, TimeSpan Delay);
public record TabOpenedEvent(Guid ConnectionId);
public record TabClosedEvent(Guid ConnectionId);
public record TabSwitchedEvent(Guid? PreviousId, Guid ActiveId);
public record CredentialRequestedEvent(ConnectionModel Connection);
public record NotificationEvent(string Title, string Message, NotificationLevel Level);
public record ConnectionImportedEvent(int Count, string Source);
public record UpdateAvailableEvent(string Version);
public record SessionHealthUpdateEvent(Guid ConnectionId, int LatencyMs, ConnectionQuality Quality);
public record AppLockedEvent(LockReason Reason);  // Manual, Timeout, SessionSwitch, Minimise
public record AppUnlockedEvent();
```

Any component can publish or subscribe. Examples:
- Tab manager subscribes to `ConnectionEstablishedEvent` to open a tab
- Status bar subscribes to `TabSwitchedEvent` to update displayed hostname
- Notification service subscribes to `ConnectionFailedEvent` to show a toast
- Audit log subscribes to all connection events to record history
- Future session health monitor publishes `SessionHealthUpdateEvent`

### Connection Pipeline

Connecting to a host flows through a pipeline of stages. Each stage can modify, enrich, or abort the connection. New features add stages without changing existing ones.

```csharp
// In Deskbridge.Core/Pipeline/
public interface IConnectionPipelineStage
{
    string Name { get; }
    int Order { get; }  // Lower runs first
    Task<PipelineResult> ExecuteAsync(ConnectionContext context);
}

public class ConnectionContext
{
    public ConnectionModel Connection { get; set; }
    public string? ResolvedPassword { get; set; }
    public IProtocolHost? Host { get; set; }
    public Dictionary<string, object> Properties { get; } = new();
    public CancellationToken CancellationToken { get; set; }
}

public record PipelineResult(bool Success, string? FailureReason = null);

public interface IConnectionPipeline
{
    void AddStage(IConnectionPipelineStage stage);
    Task<PipelineResult> ConnectAsync(ConnectionModel connection);
}
```

Default v1 stages (in order):
1. **ResolveCredentialsStage** (Order: 100) — resolves credentials via inheritance chain. If connection CredentialMode is `Own`, look up connection-specific credentials. If `Inherit` (default), walk up the group tree: connection → parent group → grandparent → root. First group with stored credentials wins. If `Prompt` or nothing found, prompt the user. Resolved credentials are set on `ConnectionContext.ResolvedPassword`.
2. **CreateHostStage** (Order: 200) — instantiate the correct `IProtocolHost` based on protocol type
3. **ConnectStage** (Order: 300) — call `Host.ConnectAsync()`, handle errors
4. **PublishConnectedStage** (Order: 400) — publish `ConnectionEstablishedEvent` to event bus
5. **UpdateRecentsStage** (Order: 500) — record connection in recent history
6. **AuditStage** (Order: 900) — log connection attempt to audit trail

Future stages that slot in without touching existing code:
- **HealthCheckStage** (Order: 150) — ping host before connecting, skip if unreachable
- **SessionMonitorStage** (Order: 450) — start latency polling after connection
- **SecureShareStage** — log shared connection access

### Connection Query Interface

Connections are queryable, not just listable. The tree view, command palette, and quick switch all consume the same interface.

```csharp
// In Deskbridge.Core/Interfaces/
public interface IConnectionQuery
{
    IReadOnlyList<ConnectionModel> GetAll();
    IReadOnlyList<ConnectionModel> Search(string query);  // Fuzzy match on name, hostname, tags
    IReadOnlyList<ConnectionModel> GetByGroup(Guid groupId);
    IReadOnlyList<ConnectionModel> GetByTag(string tag);
    IReadOnlyList<ConnectionModel> GetByFilter(ConnectionFilter filter);
    IReadOnlyList<ConnectionModel> GetRecent(int count = 10);
}

public class ConnectionFilter
{
    public string? SearchText { get; set; }
    public string? Tag { get; set; }
    public Protocol? Protocol { get; set; }
    public Guid? GroupId { get; set; }
    public bool? IsConnected { get; set; }
}
```

### Notification Service

Central notification hub. Any component can raise a notification. The UI consumes and displays them.

```csharp
// In Deskbridge.Core/Interfaces/
public enum NotificationLevel { Info, Success, Warning, Error }

public interface INotificationService
{
    void Show(string title, string message, NotificationLevel level = NotificationLevel.Info, TimeSpan? duration = null);
    void ShowError(string title, string message, Exception? exception = null);
    IReadOnlyList<Notification> Recent { get; }
    event EventHandler<Notification> NotificationRaised;
}

public record Notification(string Title, string Message, NotificationLevel Level, DateTime Timestamp);
```

### Audit Log

Enterprise environments need to know who connected where and when. Lightweight, append-only, local.

```csharp
// In Deskbridge.Core/Interfaces/
public interface IAuditLog
{
    void Record(AuditEntry entry);
    IReadOnlyList<AuditEntry> Query(AuditFilter? filter = null);
}

public record AuditEntry(
    DateTime Timestamp,
    AuditAction Action,
    string ConnectionName,
    string Hostname,
    string? Username,
    string? Detail
);

public enum AuditAction
{
    Connected, Disconnected, FailedConnect, Reconnected,
    ConnectionCreated, ConnectionEdited, ConnectionDeleted,
    ConnectionsImported, ConnectionsExported,
    CredentialStored, CredentialDeleted,
    AppStarted, AppClosed, UpdateApplied,
    AppLocked, AppUnlocked, MasterPasswordChanged
}
```

Stored as append-only JSON lines in `%AppData%/Deskbridge/audit.jsonl`. Rotated monthly. Subscribes to events on the event bus — zero coupling to the rest of the codebase.

### Credential Service

Handles storage and retrieval of credentials for both individual connections and groups. The inheritance logic lives in `ResolveCredentialsStage` (pipeline), not here — this service is a clean wrapper around Windows Credential Manager.

```csharp
// In Deskbridge.Core/Interfaces/
public enum CredentialMode { Inherit, Own, Prompt }

public interface ICredentialService
{
    // Connection-specific credentials (DESKBRIDGE/CONN/<connectionId>)
    NetworkCredential? GetForConnection(ConnectionModel connection);
    void StoreForConnection(ConnectionModel connection, string username, string? domain, string password);
    void DeleteForConnection(ConnectionModel connection);

    // Group-level credentials (DESKBRIDGE/GROUP/<guid>)
    NetworkCredential? GetForGroup(Guid groupId);
    void StoreForGroup(Guid groupId, string username, string? domain, string password);
    void DeleteForGroup(Guid groupId);

    // Inheritance resolution — walks up the group tree
    NetworkCredential? ResolveInherited(ConnectionModel connection, IConnectionStore connectionStore);
}
```

### App Lock Service

Controls master password setup, verification, and session timeout. The lock state is consumed by the MainWindow to show/hide a full-window lock overlay.

```csharp
// In Deskbridge.Core/Interfaces/
public enum LockReason { Manual, Timeout, SessionSwitch, Minimise }

public interface IAppLockService
{
    bool IsConfigured { get; }          // Has a master password been set?
    bool IsLocked { get; }
    event EventHandler<LockReason> Locked;
    event EventHandler Unlocked;
    void SetMasterPassword(string password);
    void ChangeMasterPassword(string currentPassword, string newPassword);
    bool TryUnlock(string password);
    void Lock(LockReason reason);
    void ResetInactivityTimer();        // Called on mouse/keyboard input in Deskbridge UI
    TimeSpan InactivityTimeout { get; set; }  // Default 15 minutes, persisted in settings
}
```

The `auth.json` file stores only `{ "hash": "<PBKDF2 hash>", "salt": "<random salt>" }`. No plaintext passwords.

### Patterns Summary
- MVVM with CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`)
- DI container (`Microsoft.Extensions.DependencyInjection`) as composition root
- Event bus (`WeakReferenceMessenger`) for all cross-cutting communication
- Connection pipeline for extensible connect/disconnect flow
- Query interface for all connection filtering/searching
- Protocol plugin system: each protocol implements `IProtocolHost`
- All colours via WPF-UI theme + custom ResourceDictionary overrides
- JSON for all config (connections, settings, audit) — no XML config, no SQLite

---

## UI Design

### Philosophy
Compact chrome, maximum viewport. Modern and clean — not claustrophobic like Windows 11 Fluent defaults, not dated like mRemoteNG. Keyboard-first for power users, mouse-friendly for everyone else.

### Layout
- **Title bar:** 32px. Custom via WindowChrome or WPF-UI's FluentWindow. Min/max/close buttons right-aligned. Snap layout support.
- **Left icon rail:** 36px wide. Vertical icons: Connections, Search, Settings. Click toggles slide-out panel.
- **Slide-out panel:** 240px wide. Connection tree, search, or settings. Pushes viewport — no overlay.
- **Tab bar:** Single row, 30px tall. Connection name + close button. Active tab accent border. Middle-click to close. Scroll on overflow.
- **Viewport:** Everything remaining. RDP control fills this. No WPF elements may overlap (airspace).
- **Status bar:** 22px. Hostname, resolution, latency, connection quality indicator.

### Theme
Use WPF-UI's dark theme as the base. Override with these accent tokens where needed:
```
Accent:      #007ACC
AccentHover: #1A8AD4
Error:       #F44747
Success:     #89D185
```

Light theme deferred to post-v1.

### Drag/Resize Smoothness
During window drag or resize, the RDP ActiveX control flickers due to the WPF/WinForms airspace problem. To mitigate:
- Handle `WM_ENTERSIZEMOVE` in window proc — capture a bitmap snapshot of the RDP control, show it as a WPF Image overlay, hide the WindowsFormsHost
- Handle `WM_EXITSIZEMOVE` — hide the snapshot, show the WindowsFormsHost, call `UpdateSessionDisplaySettings` if SmartSizing is on
- This gives a smooth visual during drag with no flicker

---

## Feature Specification

### Connection Management
- Connection model: Id (GUID), Name, Hostname, Port (3389 default), Username, Domain, Protocol (enum), GroupId, Notes, CreatedAt, UpdatedAt, DisplaySettings, Tags, CredentialMode (Own / Inherit / Prompt)
- Connection groups: Id, Name, ParentGroupId, SortOrder. Groups can have their own credentials stored in Windows Credential Manager (keyed by group GUID).
- Storage: JSON at `%AppData%/Deskbridge/connections.json`
- Credentials: `AdysTech.CredentialManager` with `DESKBRIDGE/CONN/<connectionId>` target for connections, `DESKBRIDGE/GROUP/<guid>` target for group-level credentials.
- **Credential inheritance:** Credentials resolve recursively up the group tree. If a connection's CredentialMode is `Inherit` (the default), the ResolveCredentialsStage walks up: connection → parent group → grandparent group → root. First match wins. Connections with CredentialMode `Own` use their own stored credentials. CredentialMode `Prompt` always asks the user. Setting credentials on a folder applies to everything inside it unless overridden — this is the default enterprise workflow.
- Connection tree: TreeView in slide-out panel. Context menu, drag-drop, F2 rename, search filter. Consumes `IConnectionQuery`. Groups show a key icon when they have credentials set.
- Connection editor: Modal dialog. Tabs: General, Credentials (with Inherit/Own/Prompt selector and "inherited from: [group name]" indicator), Display, Notes.
- Group editor: Right-click group → Edit. Set group-level credentials (username, domain, password). Shows count of connections that will inherit.

### RDP Integration
- Wrapper: `RdpHostControl` implementing `IProtocolHost`, wrapping `AxMsRdpClient9NotSafeForScripting` in `WindowsFormsHost`
- Siting: add to container BEFORE setting properties
- Password: via `IMsTscNonScriptable` cast from `GetOcx()`
- Lifecycle: managed through `IConnectionPipeline` — never called directly from UI code
- Reconnection: overlay "Connection lost — Reconnect / Close", exponential backoff (2s, 4s, 8s, max 30s)
- Strict disposal sequence on tab close
- Publishes all lifecycle events to `IEventBus`

### Tab Management
- One connection per tab, active tab only renders live ActiveX control
- Inactive tabs: store state, `BitmapPeristence = 0`
- Ctrl+Tab / Ctrl+Shift+Tab to cycle, Ctrl+W to close, middle-click to close
- Warn at 15+ simultaneous connections
- Subscribes to `ConnectionEstablishedEvent` to open tabs, `ConnectionClosedEvent` to close them

### Command Palette
- Ctrl+Shift+P: floating search box, fuzzy match connections and commands
- Consumes `IConnectionQuery.Search()` for connection matching
- Commands: New Connection, Settings, Disconnect All, Quick Connect (type hostname, connect immediately)
- Extensible: commands registered via a simple `ICommand` collection in DI

### Keyboard Shortcuts
- Ctrl+N: New connection
- Ctrl+T: Quick connect
- Ctrl+W: Close tab
- Ctrl+P: Quick switch (fuzzy search connections, jump to tab or connect)
- F11: Fullscreen toggle
- Escape: Exit fullscreen

### Notifications
- Toast stack (bottom-right): connected, disconnected, reconnecting
- No modals for non-critical events
- Consumed via `INotificationService`
- Auto-generated from event bus subscriptions (connection failures, updates)

### Window State Persistence
- Save/restore: position, size, maximised, sidebar state
- Store in `%AppData%/Deskbridge/settings.json`

### Security & Logging
- **App lock:** Master password required on app launch. Password hash (PBKDF2 with random salt) stored in `%AppData%/Deskbridge/auth.json`. On first run, prompt user to set a master password. The lock screen is a full-window overlay — no access to connections, credentials, or settings without the password. This does not encrypt individual credentials (they remain in Windows Credential Manager, protected by Windows login). It gates access to the Deskbridge UI itself.
- **Session timeout:** After a configurable period of inactivity (default 15 minutes, configurable in settings), the app auto-locks and shows the master password prompt. RDP sessions remain connected behind the lock — only the Deskbridge UI is gated. Mouse movement and keyboard input within Deskbridge reset the timer. Timer is not reset by activity inside an RDP session (the RDP control consumes input directly).
- **Lock controls:** Ctrl+L to manually lock. Lock on Windows session lock (subscribe to `SystemEvents.SessionSwitch`). Option to lock on minimise.
- Serilog rolling file: `%AppData%/Deskbridge/logs/`, 10MB cap, 5 files
- Audit log: `%AppData%/Deskbridge/audit.jsonl`, append-only, monthly rotation
- Global exception handler
- Per-connection error isolation
- COM try/catch around all ActiveX calls
- Never log credentials
- Audit log records all connection events, credential changes, imports/exports, app lock/unlock events

### Auto-Update (Velopack)
- GitHub Releases via `GithubSource`
- Silent check on startup, status bar notification, download/apply/restart
- Publishes `UpdateAvailableEvent` to event bus
- Self-contained publish, SemVer2

### GitHub Actions Release Pipeline
- Triggered on version tag push (v1.0.0)
- Build self-contained, `vpk pack`, upload to GitHub Release

### mRemoteNG Import
- Parse `confCons.xml`, map fields, import wizard (pick → preview → confirm)
- Metadata only — no password import, users re-enter credentials
- Store with `DESKBRIDGE/CONN/` prefix
- Publishes `ConnectionImportedEvent` to event bus
- Audit log records import

### Export
- JSON (no credentials), CSV

---

## Future Scope (Post-v1)

### v1.1 — Quality of Life
- Smart connect — type a hostname in command palette, connect immediately without saving. Offer to save afterwards.
- Session health monitoring — latency polling, connection quality indicator in status bar and tab tooltip
- Quick switch (Ctrl+P) — fuzzy search across all connections, jump to existing tab or connect
- Connection tagging and smart groups — auto-groups by domain/subnet, saved filter presets
- Session snapshots — screenshot a session with hotkey, saved with timestamp and connection name

### v1.2 — Enterprise
- Secure credential sharing — export connection with one-time encrypted link
- Connection profiles — pre-configured display/audio/redirect settings (e.g., "Server Admin", "Dev Desktop")
- Group Policy / managed configuration — read connection sources and settings from registry or managed JSON
- Audit log viewer — searchable UI for the audit trail

### v2.0 — Multi-Protocol
- SSH tab support (terminal emulator control + SSH.NET)
- VNC support
- Light theme
- DPAPI encryption for connection JSON file (encrypt the entire connections.json at rest, decrypt on app unlock)
- Connection sharing / team sync
- RDP gateway support
- Multi-monitor spanning
- Session recording

---

## RDP ActiveX Reference

### Correct Siting Pattern
```csharp
var host = new WindowsFormsHost();
var rdp = new AxMsRdpClient9NotSafeForScripting();

// Site FIRST
host.Child = rdp;
parentGrid.Children.Add(host);

// THEN configure
rdp.Server = connection.Hostname;
rdp.AdvancedSettings9.RDPPort = connection.Port;
rdp.UserName = connection.Username;
rdp.Domain = connection.Domain;
rdp.AdvancedSettings9.SmartSizing = true;
rdp.AdvancedSettings9.EnableCredSspSupport = true;
rdp.AdvancedSettings9.BitmapPeristence = 0;

var secured = (IMsTscNonScriptable)rdp.GetOcx();
secured.ClearTextPassword = password;

rdp.Connect();
```

### Correct Disposal
```csharp
if (rdp.Connected != 0) rdp.Disconnect();
rdp.Dispose();
host.Child = null;
host.Dispose();
parentPanel.Children.Remove(host);
```
