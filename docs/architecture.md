# Architecture Overview

This document describes the high-level architecture of Deskbridge, its project structure, dependency flow, and key design patterns.

## Project Structure

Deskbridge is a three-project solution:

```
src/
  Deskbridge/                     # WPF application (UI, ViewModels, dialogs, composition root)
  Deskbridge.Core/                # Business logic, interfaces, services, models, pipeline
  Deskbridge.Protocols.Rdp/       # RDP ActiveX interop (AxMsRdpClient9, WindowsFormsHost)
tests/
  Deskbridge.Tests/               # Unit and integration tests
```

### Deskbridge (WPF Application)

The main executable. Contains views (XAML), view models, dialogs, the DI composition root (`App.xaml.cs`), the Velopack entry point (`Program.cs`), and UI-specific services (toast notifications, airspace swapper, lock orchestration, idle/session lock triggers).

References: `Deskbridge.Core`, `Deskbridge.Protocols.Rdp`

### Deskbridge.Core (Business Logic)

Framework-independent business logic. Defines all interfaces, models, events, pipeline stages, and service implementations. Has no dependency on WPF, WinForms, or UI controls.

References: None (leaf project)

### Deskbridge.Protocols.Rdp (ActiveX Interop)

Contains the RDP ActiveX wrapper (`RdpHostControl`) implementing `IProtocolHost`, the pre-generated COM interop assemblies (`MSTSCLib.dll`, `AxMSTSCLib.dll`), and the protocol host factory. This is the only project with `UseWindowsForms=true` in its `.csproj` to avoid type ambiguity between `System.Windows.Application` (WPF) and `System.Windows.Forms.Application` (WinForms).

References: `Deskbridge.Core`

## Dependency Flow

```
Deskbridge (WPF app)
    |
    +--> Deskbridge.Core (no UI deps)
    |
    +--> Deskbridge.Protocols.Rdp
              |
              +--> Deskbridge.Core
```

Core has no outward dependencies. Both the WPF application and the RDP protocol project depend on Core, but never on each other's internals. The protocol project is consumed by the application through the `IProtocolHost` and `IProtocolHostFactory` interfaces defined in Core.

## Dependency Injection

The DI container is `Microsoft.Extensions.DependencyInjection`, configured as the composition root in `App.xaml.cs`. All services are registered as interfaces. No service constructs another service directly.

### Service Lifetimes

| Lifetime | Examples |
| --- | --- |
| Singleton | `IEventBus`, `IConnectionStore`, `ICredentialService`, `IConnectionPipeline`, `IAuditLogger`, `IMasterPasswordService`, `IAppLockState`, `ITabHostManager`, `IConnectionCoordinator`, `MainWindowViewModel`, `ConnectionTreeViewModel`, `MainWindow` |
| Transient | `ConnectionEditorViewModel`, `GroupEditorViewModel`, `CommandPaletteDialog`, `LockOverlayDialog`, `ImportWizardDialog`, `IProtocolHost` (via factory) |

### Eager Resolution

Several singletons are resolved eagerly in `App.OnStartup` to ensure their event bus subscriptions are active before user interaction:

- `IConnectionCoordinator` -- bridges connection requests from UI to pipeline
- `ITabHostManager` -- manages host mounting/unmounting for tabs
- `ToastSubscriptionService` -- subscribes to 6 bus events for toast notifications
- `IdleLockService` -- starts the idle timer and input listener
- `SessionLockService` -- subscribes to `SystemEvents.SessionSwitch`
- `AppLockController` -- subscribes to `AppLockedEvent` (resolved after `mainWindow.Show()`)

## Event Bus

Cross-cutting communication uses `WeakReferenceMessenger` from CommunityToolkit.Mvvm, wrapped behind an `IEventBus` interface:

```csharp
public interface IEventBus
{
    void Publish<TEvent>(TEvent message) where TEvent : class;
    void Subscribe<TEvent>(object recipient, Action<TEvent> handler) where TEvent : class;
    void Unsubscribe<TEvent>(object recipient) where TEvent : class;
}
```

Subsystems publish and subscribe without direct references. The weak reference mechanism prevents memory leaks from forgotten subscriptions.

### Key Events

| Event | Publisher | Subscribers |
| --- | --- | --- |
| `ConnectionRequestedEvent` | UI (tree double-click, command palette) | `ConnectionCoordinator` |
| `ConnectionEstablishedEvent` | Pipeline (PublishConnectedStage) | Tab manager, status bar, toast |
| `ConnectionClosedEvent` | Pipeline (PublishClosedEventStage) | Tab manager, toast |
| `ConnectionFailedEvent` | Pipeline | Toast, audit |
| `ReconnectingEvent` | `RdpReconnectCoordinator` | Reconnect overlay |
| `TabSwitchedEvent` | Tab manager | Status bar, airspace swapper |
| `AppLockedEvent` | Idle timer, session lock, Ctrl+L | `AppLockController` |
| `AppUnlockedEvent` | `AppLockController` | Idle timer restart |
| `UpdateAvailableEvent` | `UpdateService` | `MainWindowViewModel` (status bar badge) |
| `ConnectionImportedEvent` | Import wizard | Toast |
| `NotificationEvent` | Various | Toast stack |

## Connection Pipeline

Connecting to a host flows through an ordered pipeline of stages. Each stage can modify, enrich, or abort the connection. New features add stages without changing existing code.

### Connect Pipeline

```csharp
public interface IConnectionPipeline
{
    void AddStage(IConnectionPipelineStage stage);
    Task<PipelineResult> ConnectAsync(ConnectionModel connection);
}
```

Registered stages (in execution order):

| Order | Stage | Responsibility |
| --- | --- | --- |
| 100 | `ResolveCredentialsStage` | Resolves credentials via inheritance chain or prompts the user |
| 200 | `CreateHostStage` | Instantiates the correct `IProtocolHost` via `IProtocolHostFactory` |
| 300 | `ConnectStage` | Calls `Host.ConnectAsync()` and handles errors |
| 500 | `UpdateRecentsStage` | Records the connection in recent history |

### Disconnect Pipeline

```csharp
public interface IDisconnectPipeline
{
    void AddStage(IDisconnectPipelineStage stage);
    Task<PipelineResult> DisconnectAsync(DisconnectContext context);
}
```

Registered stages:

| Stage | Responsibility |
| --- | --- |
| `DisconnectStage` | Calls `Host.DisconnectAsync()` |
| `DisposeStage` | Disposes the `IProtocolHost` (COM resource cleanup) |
| `PublishClosedEventStage` | Publishes `ConnectionClosedEvent` to the event bus |

### Connection Context

```csharp
public class ConnectionContext
{
    public ConnectionModel Connection { get; set; }
    public string? ResolvedPassword { get; set; }
    public IProtocolHost? Host { get; set; }
    public Dictionary<string, object> Properties { get; }
    public CancellationToken CancellationToken { get; set; }
}
```

The context flows through all stages, allowing each stage to read or enrich it. The `ResolvedPassword` property is set by `ResolveCredentialsStage` and consumed by `ConnectStage`.

## Key Interfaces

| Interface | Implementation | Purpose |
| --- | --- | --- |
| `IConnectionStore` | `JsonConnectionStore` | CRUD operations for connections and groups. Persists to `connections.json`. |
| `ICredentialService` | `WindowsCredentialService` | Store/retrieve/delete credentials in Windows Credential Manager. |
| `IProtocolHost` | `RdpProtocolHost` | Wraps an RDP ActiveX control with connect/disconnect/dispose lifecycle. |
| `IProtocolHostFactory` | `RdpProtocolHostFactory` | Creates `IProtocolHost` instances by protocol type. |
| `IUpdateService` | `UpdateService` | Checks for updates, downloads, and applies via Velopack. |
| `IMasterPasswordService` | `MasterPasswordService` | PBKDF2 hash/verify for master password. Reads/writes `auth.json`. |
| `IAppLockState` | `AppLockState` | Observable boolean tracking whether the app is locked. |
| `IAuditLogger` | `AuditLogger` | Appends audit records to monthly-rotating JSONL files. |
| `IEventBus` | `EventBus` | Publish/subscribe via `WeakReferenceMessenger`. |
| `IConnectionQuery` | `ConnectionQueryService` | Fuzzy search and filtering across connections. |
| `INotificationService` | `NotificationService` | Central notification hub consumed by UI. |
| `IConnectionPipeline` | `ConnectionPipeline` | Ordered stage pipeline for connection establishment. |
| `IDisconnectPipeline` | `DisconnectPipeline` | Ordered stage pipeline for disconnection and cleanup. |
| `IConnectionCoordinator` | `ConnectionCoordinator` | Bridges UI connection requests to the pipeline with STA thread marshaling. |
| `ITabHostManager` | `TabHostManager` | Manages host mounting/unmounting for tab lifecycle. |
| `IWindowStateService` | `WindowStateService` | Persists/loads window position and application settings. |
| `ICommandPaletteService` | `CommandPaletteService` | Registers and provides commands for the command palette. |
| `IConnectionImporter` | `MRemoteNGImporter` | Parses connection files from external tools. |

## Data Flow

### Connection Data

```
connections.json
    |
    v
IConnectionStore (JsonConnectionStore)
    |
    +--> ConnectionTreeViewModel (TreeView binding)
    +--> IConnectionQuery (command palette, fuzzy search)
    +--> ConnectionExporter (JSON/CSV export)
    +--> IConnectionImporter (mRemoteNG import)
```

### Credential Resolution

```
User initiates connection
    |
    v
ResolveCredentialsStage
    |
    +--> CredentialMode == Own?  --> ICredentialService.GetForConnection()
    +--> CredentialMode == Inherit? --> ICredentialService.ResolveInherited()
    |                                  (walks group tree via IConnectionStore)
    +--> CredentialMode == Prompt? --> UI prompt
    |
    v
ConnectionContext.ResolvedPassword set
    |
    v
ConnectStage --> IProtocolHost.ConnectAsync()
```

## RDP ActiveX Lifecycle

The RDP control (`AxMsRdpClient9NotSafeForScripting`) follows a strict lifecycle:

### Siting (before any property access)

```
1. Create WindowsFormsHost
2. Create AxMsRdpClient9NotSafeForScripting
3. Set host.Child = rdp  (sites the control)
4. Add host to WPF visual tree
```

The control MUST be sited (step 3) before setting any properties. Accessing properties before siting throws `AxHost.InvalidActiveXStateException`.

### Configuration (after siting)

```
5. Set Server, Port, Username, Domain
6. Configure AdvancedSettings9 (SmartSizing, CredSSP, BitmapPersistence)
7. Cast GetOcx() to IMsTscNonScriptable
8. Set ClearTextPassword via the non-scriptable interface
```

### Connection

```
9. Call rdp.Connect()
```

### Disposal (strict order)

```
10. if (rdp.Connected != 0) rdp.Disconnect()
11. rdp.Dispose()            // AxHost handles COM release
12. host.Child = null
13. host.Dispose()
14. parentPanel.Children.Remove(host)
```

`Marshal.ReleaseComObject` is never called manually. `AxHost.Dispose()` handles COM reference release. Manual release causes access violations and use-after-free bugs.

### Session Limits

Each RDP session consumes GDI handles. The practical limit is approximately 15-20 simultaneous sessions per process. Inactive tabs set `BitmapPersistence = 0` to reduce resource consumption.

## MVVM Pattern

Deskbridge uses CommunityToolkit.Mvvm source generators:

- `[ObservableProperty]` on partial properties for change notification
- `[RelayCommand]` for ICommand generation
- `WeakReferenceMessenger` as the event bus implementation

View models are injected via DI. Views receive their view model through constructor injection or DataContext binding.

## Build Configuration

### Directory.Build.props (shared)

All projects target `net10.0-windows` with WPF enabled, nullable reference types, implicit usings, warnings-as-errors, and code style enforcement.

### Directory.Packages.props (central package management)

Package versions are managed centrally with floating versions for patch-level updates:

| Package | Version |
| --- | --- |
| WPF-UI | 4.2.* |
| CommunityToolkit.Mvvm | 8.4.* |
| Microsoft.Extensions.DependencyInjection | 10.0.* |
| Velopack | 0.0.1298 |
| Serilog | 4.3.* |
| Serilog.Sinks.File | 7.0.* |
| AdysTech.CredentialManager | 3.1.* |

### Velopack Entry Point

`App.xaml` is configured as a `Page` (not `ApplicationDefinition`) to support Velopack's custom `Main` method requirement:

```csharp
[STAThread]
static void Main(string[] args)
{
    VelopackApp.Build().Run();
    CrashHandler.Install();
    var app = new App();
    app.InitializeComponent();
    app.Run();
}
```

`VelopackApp.Build().Run()` must be the first call in `Main` to handle Velopack install/update/uninstall hooks before the WPF application starts.
