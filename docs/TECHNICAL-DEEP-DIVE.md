<!-- generated-by: gsd-doc-writer -->
# Deskbridge Technical Deep-Dive

A comprehensive technical reference for the Deskbridge RDP connection manager. This document covers the full architecture, runtime behavior, security model, and operational characteristics of the system.

**Target audience:** Technical Design Authorities, senior developers onboarding to the codebase, and enterprise architects evaluating the tool for deployment.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Architecture Overview](#2-architecture-overview)
3. [Startup Sequence](#3-startup-sequence)
4. [Connection Pipeline](#4-connection-pipeline)
5. [RDP ActiveX Integration](#5-rdp-activex-integration)
6. [Security Architecture](#6-security-architecture)
7. [UI Architecture](#7-ui-architecture)
8. [Data Layer](#8-data-layer)
9. [Auto-Update](#9-auto-update)
10. [Import/Export](#10-importexport)
11. [CI/CD Pipeline](#11-cicd-pipeline)
12. [Testing Strategy](#12-testing-strategy)
13. [Enterprise Considerations](#13-enterprise-considerations)

---

## 1. Executive Summary

Deskbridge is a Windows desktop RDP connection manager built to replace mRemoteNG. It targets enterprise infrastructure teams managing dozens to hundreds of remote connections daily. The application provides tabbed multi-session management with a dark Fluent UI, proper COM/ActiveX lifecycle management, keyboard-first workflows, and auto-update from GitHub Releases.

### The Problem

mRemoteNG suffers from freezes and memory leaks caused by incorrect ActiveX disposal, a dated WinForms UI, brittle XML configuration, no auto-update mechanism, a tightly coupled codebase that makes feature work risky, and no keyboard-first workflows.

### Key Technical Decisions

| Decision | Rationale |
| --- | --- |
| .NET 10 (C# 14) | LTS until November 2028. `field` keyword and partial properties are GA. WPF ships in-box. |
| Classic `aximp.exe` COM interop | `[GeneratedComInterface]` does not support ActiveX controls (dotnet/winforms#10583). |
| WPF-UI 4.2.0 (Fluent) | First-class `net10.0-windows7.0` target. Provides FluentWindow, Mica backdrop, auto-restyled standard controls. |
| `WeakReferenceMessenger` event bus | Zero-coupling cross-cutting communication. Weak references prevent memory leaks from forgotten subscriptions. |
| Pipeline pattern for connections | Extensible connect/disconnect flow. New features add stages without modifying existing code. |
| Windows Credential Manager | DPAPI-protected at rest. `DESKBRIDGE/CONN/` namespace avoids Credential Guard conflicts. |
| Velopack auto-update | Successor to Squirrel.Windows. Per-user install (no admin), GitHub Releases as source. |
| System.Text.Json | In-box, no external dependency, source generator support. |

---

## 2. Architecture Overview

### 2.1 Three-Project Structure

```
src/
  Deskbridge/                     # WPF application — views, ViewModels, DI composition root
  Deskbridge.Core/                # Business logic — interfaces, services, models, pipeline
  Deskbridge.Protocols.Rdp/       # RDP ActiveX interop — AxMsRdpClient9, WindowsFormsHost
tests/
  Deskbridge.Tests/               # Unit and integration tests (473 test methods across 59 files)
```

### 2.2 Dependency Diagram

```
Deskbridge (WPF app)
    |
    +--> Deskbridge.Core (no UI deps — leaf project)
    |
    +--> Deskbridge.Protocols.Rdp
              |
              +--> Deskbridge.Core
```

Core has no outward dependencies. Both the WPF application and the RDP protocol project depend on Core, but never on each other's internals. The protocol project is consumed through the `IProtocolHost` and `IProtocolHostFactory` interfaces defined in Core.

**Critical constraint:** `UseWindowsForms=true` is set ONLY in `Deskbridge.Protocols.Rdp.csproj` — never in `Directory.Build.props`. This prevents type ambiguity between `System.Windows.Application` (WPF) and `System.Windows.Forms.Application` (WinForms).

### 2.3 Dependency Injection Container

The DI container is `Microsoft.Extensions.DependencyInjection`, configured in `App.xaml.cs` as the composition root. Every service is registered as an interface. No service constructs another service directly.

#### Singleton Registrations

```csharp
// Core services
services.AddSingleton<IEventBus, EventBus>();
services.AddSingleton<INotificationService, NotificationService>();
services.AddSingleton<IAuditLogger, AuditLogger>();
services.AddSingleton<IConnectionPipeline, ConnectionPipeline>();
services.AddSingleton<IDisconnectPipeline, DisconnectPipeline>();
services.AddSingleton<IConnectionStore>(sp => { var s = new JsonConnectionStore(); s.Load(); return s; });
services.AddSingleton<ICredentialService, WindowsCredentialService>();
services.AddSingleton<IConnectionQuery>(sp => new ConnectionQueryService(...));
services.AddSingleton<IProtocolHostFactory, RdpProtocolHostFactory>();
services.AddSingleton<IConnectionCoordinator, ConnectionCoordinator>();
services.AddSingleton<ITabHostManager, TabHostManager>();
services.AddSingleton<ISnackbarService, SnackbarService>();
services.AddSingleton<IContentDialogService, ContentDialogService>();
services.AddSingleton<ICredentialPromptService, CredentialPromptService>();
services.AddSingleton<IWindowStateService, WindowStateService>();
services.AddSingleton<IUpdateService>(sp => new UpdateService(...));
services.AddSingleton<IAppLockState, AppLockState>();
services.AddSingleton<ICommandPaletteService>(sp => new CommandPaletteService(...));
services.AddSingleton<IMasterPasswordService>(sp => new MasterPasswordService(...));
services.AddSingleton<RdpReconnectCoordinator>();
services.AddSingleton<AirspaceSwapper>();
services.AddSingleton<IdleLockService>(sp => new IdleLockService(...));
services.AddSingleton<SessionLockService>(sp => new SessionLockService(...));
services.AddSingleton<AppLockController>(sp => new AppLockController(...));
services.AddSingleton<ToastStackViewModel>();
services.AddSingleton<ToastSubscriptionService>();

// ViewModels (singletons)
services.AddSingleton<MainWindowViewModel>(sp => new MainWindowViewModel(...));
services.AddSingleton<ConnectionTreeViewModel>();

// Views (singletons)
services.AddSingleton<ConnectionTreeControl>();
services.AddSingleton<MainWindow>(sp => new MainWindow(...));
```

#### Transient Registrations

```csharp
services.AddTransient<ConnectionEditorViewModel>();
services.AddTransient<GroupEditorViewModel>();
services.AddTransient<CommandPaletteViewModel>();
services.AddTransient<CommandPaletteDialog>();
services.AddTransient<LockOverlayViewModel>();
services.AddTransient<LockOverlayDialog>();
services.AddTransient<ChangePasswordViewModel>();
services.AddTransient<ChangePasswordDialog>();
services.AddTransient<ImportWizardViewModel>();
services.AddTransient<ImportWizardDialog>();
services.AddTransient<ReconnectOverlayViewModel>();
services.AddTransient<ConnectionEditorDialog>();
services.AddTransient<GroupEditorDialog>();
```

#### Pipeline Stage Registrations

```csharp
// Connect stages
services.AddSingleton<IConnectionPipelineStage, ResolveCredentialsStage>();
services.AddSingleton<IConnectionPipelineStage, CreateHostStage>();
services.AddSingleton<IConnectionPipelineStage, ConnectStage>();
services.AddSingleton<IConnectionPipelineStage, UpdateRecentsStage>();

// Disconnect stages
services.AddSingleton<IDisconnectPipelineStage, DisconnectStage>();
services.AddSingleton<IDisconnectPipelineStage, DisposeStage>();
services.AddSingleton<IDisconnectPipelineStage, PublishClosedEventStage>();
```

#### Factory Registrations

```csharp
services.AddTransient<Func<CommandPaletteDialog>>(sp => () => sp.GetRequiredService<CommandPaletteDialog>());
services.AddTransient<Func<LockOverlayDialog>>(sp => () => sp.GetRequiredService<LockOverlayDialog>());
services.AddTransient<Func<ChangePasswordDialog>>(sp => () => sp.GetRequiredService<ChangePasswordDialog>());
services.AddTransient<Func<ImportWizardDialog>>(sp => () => sp.GetRequiredService<ImportWizardDialog>());
```

### 2.4 Event Bus Topology

Cross-cutting communication uses `WeakReferenceMessenger` from CommunityToolkit.Mvvm, wrapped behind an `IEventBus` interface:

```csharp
public interface IEventBus
{
    void Publish<TEvent>(TEvent message) where TEvent : class;
    void Subscribe<TEvent>(object recipient, Action<TEvent> handler) where TEvent : class;
    void Unsubscribe<TEvent>(object recipient) where TEvent : class;
}
```

#### Complete Event Registry

| Event | Publisher(s) | Subscriber(s) |
| --- | --- | --- |
| `ConnectionRequestedEvent` | UI (tree double-click, command palette) | `ConnectionCoordinator` |
| `ConnectionEstablishedEvent` | Pipeline (`PublishConnectedStage`) | TabHostManager, status bar, toast |
| `ConnectionClosedEvent` | Pipeline (`PublishClosedEventStage`) | TabHostManager, toast |
| `ConnectionFailedEvent` | Pipeline | Toast, audit |
| `ReconnectingEvent` | `RdpReconnectCoordinator` | Reconnect overlay |
| `TabOpenedEvent` | TabHostManager | `MainWindowViewModel` (adds tab VM) |
| `TabClosedEvent` | TabHostManager | `MainWindowViewModel` (removes tab VM) |
| `TabSwitchedEvent` | TabHostManager | `MainWindowViewModel` (status bar, active tab), `AirspaceSwapper` |
| `TabStateChangedEvent` | TabHostManager | `MainWindowViewModel` (state indicators) |
| `AppLockedEvent` | `IdleLockService`, `SessionLockService`, `MainWindowViewModel` (Ctrl+L) | `AppLockController` |
| `AppUnlockedEvent` | `AppLockController` | `IdleLockService` (timer restart) |
| `UpdateAvailableEvent` | `UpdateService` | `MainWindowViewModel` (status bar badge) |
| `ConnectionImportedEvent` | `ImportWizardViewModel` | Toast |
| `NotificationEvent` | Various | Toast stack |

---

## 3. Startup Sequence

The application follows a carefully ordered startup sequence. Several ordering constraints are load-bearing.

### 3.1 Program.Main (Entry Point)

```csharp
[STAThread]
static void Main(string[] args)
{
    VelopackApp.Build()
        .OnBeforeUninstallFastCallback(v =>
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Deskbridge");
            if (Directory.Exists(appData))
                Directory.Delete(appData, recursive: true);
        })
        .Run();
    CrashHandler.Install();
    var app = new App();
    app.InitializeComponent();
    app.Run();
}
```

**Step 1: Velopack bootstrap** -- `VelopackApp.Build().Run()` must be the first call. It handles Velopack install/update/uninstall hooks before the WPF application starts. The `OnBeforeUninstallFastCallback` deletes user data from `%AppData%\Deskbridge` on uninstall.

**Step 2: CrashHandler.Install()** -- Installs `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` hooks BEFORE constructing the WPF `App`. A crash inside the `App` constructor or `InitializeComponent` still hits the logger. The `Dispatcher` hook cannot be installed yet because `Application.Current` is null.

**Step 3: App construction and run** -- Standard WPF lifecycle.

### 3.2 App.OnStartup

1. **Dispatcher crash hook** -- `CrashHandler.InstallDispatcherHook(this)` installs `Application.DispatcherUnhandledException`. Now all three exception pathways are covered.

2. **Serilog configuration** -- Disposes any previous logger, creates rolling file logger at `%AppData%\Deskbridge\logs\` with `RedactSensitivePolicy`.

3. **Theme application** -- Applies dark Fluent theme with Mica backdrop and `#007ACC` accent colour.

4. **DI container build** -- Calls `ConfigureServices(services)` then `services.BuildServiceProvider()`.

5. **Credential migration** -- One-time idempotent migration from legacy `TERMSRV/*` entries to `DESKBRIDGE/CONN/*` targets. Must run after `IConnectionStore.Load()` and before any connection attempts.

6. **Pipeline wiring** -- Resolves all `IConnectionPipelineStage` and `IDisconnectPipelineStage` instances and adds them to the respective pipelines.

7. **Eager singleton resolution** -- The following services are resolved eagerly to ensure their event bus subscriptions are active before user interaction:
   - `IConnectionCoordinator` -- bridges connection requests to the pipeline
   - `ITabHostManager` -- manages host mounting/unmounting for tabs
   - `ToastSubscriptionService` -- subscribes to 6 bus events for toast notifications
   - `IdleLockService` -- starts idle timer and `InputManager.PreProcessInput` listener
   - `SessionLockService` -- subscribes to `SystemEvents.SessionSwitch`

8. **Window display** -- `mainWindow.Show()` realizes the visual tree.

9. **AppLockController resolution** -- Resolved AFTER `Show()` because it takes `IHostContainerProvider` (the MainWindow) and needs the visual tree realized before capturing children. Subscribes to `AppLockedEvent` in its constructor.

10. **Import wizard factory wiring** -- Sets the import wizard factory on MainWindow.

11. **Startup lock** -- `lockController.EnsureLockedOnStartupAsync()` is fire-and-forget. Handles both returning-user (unlock mode) and first-run (setup mode) flows.

12. **Update check** -- If the app is installed via Velopack (`updateService.IsInstalled`), a background task checks for updates. In dev mode (running from IDE), the check is skipped.

### 3.3 Exception Handling Architecture

`CrashHandler` installs three hooks covering the three exception pathways in a WPF application:

| Hook | Pathway | Survivable? |
| --- | --- | --- |
| `AppDomain.CurrentDomain.UnhandledException` | Non-UI thread + terminating | No -- `IsTerminating=true` means the OS will end the process |
| `Application.DispatcherUnhandledException` | UI thread | Yes -- `e.Handled = true` survives |
| `TaskScheduler.UnobservedTaskException` | Fire-and-forget Tasks | Yes -- `e.SetObserved()` survives |

For dispatcher exceptions, `TryShowCrashDialog` marshals to the WPF dispatcher, resolves `IContentDialogService` from the DI container via `App.Services`, and shows a `CrashDialog` with copy-details and restart functionality. If the dialog cannot be shown (e.g., crash during app construction), the exception is logged to Serilog and the process exits.

---

## 4. Connection Pipeline

### 4.1 Pipeline Architecture

Connecting to a host flows through an ordered pipeline of stages. Each stage can modify, enrich, or abort the connection. The design follows the chain-of-responsibility pattern -- new features add stages without changing existing code.

```csharp
public sealed class ConnectionPipeline : IConnectionPipeline
{
    private readonly List<IConnectionPipelineStage> _stages = [];

    public void AddStage(IConnectionPipelineStage stage) => _stages.Add(stage);

    public async Task<PipelineResult> ConnectAsync(ConnectionModel connection)
    {
        var context = new ConnectionContext { Connection = connection };
        foreach (var stage in _stages.OrderBy(s => s.Order))
        {
            var result = await stage.ExecuteAsync(context);
            if (!result.Success)
                return result;
        }
        return new PipelineResult(true);
    }
}
```

### 4.2 Connect Pipeline Stages

| Order | Stage | Responsibility |
| --- | --- | --- |
| 100 | `ResolveCredentialsStage` | Resolves credentials via the inheritance chain or prompts the user |
| 200 | `CreateHostStage` | Instantiates the correct `IProtocolHost` via `IProtocolHostFactory` |
| 300 | `ConnectStage` | Calls `Host.ConnectAsync()` and handles COM exceptions |
| 500 | `UpdateRecentsStage` | Records the connection in recent history |

### 4.3 Disconnect Pipeline Stages

| Stage | Responsibility |
| --- | --- |
| `DisconnectStage` | Calls `Host.DisconnectAsync()` with 30-second timeout |
| `DisposeStage` | Disposes the `IProtocolHost` (COM resource cleanup + WFH leak fixes) |
| `PublishClosedEventStage` | Publishes `ConnectionClosedEvent` to the event bus |

### 4.4 Connection Context

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

The context flows through all stages. `ResolvedPassword` is set by `ResolveCredentialsStage` and consumed by `ConnectStage`. After the password is written to the ActiveX control via `IMsTscNonScriptable.ClearTextPassword`, it is immediately nulled from the context as a defense-in-depth measure.

### 4.5 Credential Resolution Flow

```
User initiates connection
    |
    v
ResolveCredentialsStage
    |
    +--> CredentialMode == Own?     --> ICredentialService.GetForConnection()
    +--> CredentialMode == Inherit? --> ICredentialService.ResolveInherited()
    |                                  (walks group tree: connection -> parent -> grandparent -> root)
    |                                  (cycle detection via HashSet<Guid>)
    +--> CredentialMode == Prompt?  --> UI credential prompt (ContentDialog)
    |
    v
ConnectionContext.ResolvedPassword set
    |
    v
ConnectStage --> IProtocolHost.ConnectAsync()
```

Credential inheritance walks up the group parent chain. The first group with stored credentials wins. Cycle detection prevents infinite loops from malformed group data.

---

## 5. RDP ActiveX Integration

### 5.1 COM Interop Approach

Deskbridge uses classic `aximp.exe`-generated interop assemblies (`MSTSCLib.dll` and `AxMSTSCLib.dll`). The newer `[GeneratedComInterface]` COM source generators do not support ActiveX controls (dotnet/winforms#10583). The interop DLLs are placed in `src/Deskbridge.Protocols.Rdp/Interop/` and referenced as assembly files, not NuGet packages.

The ActiveX control class is `AxMsRdpClient9NotSafeForScripting`, hosted inside a `WindowsFormsHost` for WPF integration.

### 5.2 Siting Lifecycle

The RDP ActiveX control follows a strict lifecycle where ordering is load-bearing. Violating the order causes `AxHost.InvalidActiveXStateException` or silent property-set failures.

**Phase 1: Siting (before any property access)**

```csharp
var host = new WindowsFormsHost { Background = Brushes.Black };
var rdp = new AxMsRdpClient9NotSafeForScripting();
host.Child = rdp;                    // (1) Triggers CreateControl() inside WFH
viewport.Children.Add(host);         // (2) Realizes the Win32 handle
if (rdp.Handle == IntPtr.Zero)       // (3) Guard -- throw if not sited
    throw new InvalidOperationException("not sited");
```

The `AxSiting.SiteAndConfigure<T>()` helper enforces this sequence. The guard on `Handle == IntPtr.Zero` catches cases where the parent container is collapsed or has no layout.

**Phase 2: Configuration (after siting)**

`RdpConnectionConfigurator.Apply()` sets all RDP properties after siting:

```csharp
rdp.Server = connection.Hostname;
rdp.AdvancedSettings9.RDPPort = connection.Port;
rdp.UserName = connection.Username ?? "";
rdp.Domain = connection.Domain ?? "";
rdp.AdvancedSettings9.SmartSizing = true;      // Scale to viewport
rdp.DesktopWidth = 1920;                        // Default; SmartSizing scales
rdp.DesktopHeight = 1080;
rdp.ColorDepth = 32;
rdp.AdvancedSettings9.EnableCredSspSupport = true;
rdp.AdvancedSettings9.CachePersistenceActive = 0;
rdp.AdvancedSettings9.BitmapPeristence = 0;     // COM typelib misspelling -- intentional
rdp.SecuredSettings3.KeyboardHookMode = 0;
rdp.AdvancedSettings9.GrabFocusOnConnect = false;
rdp.AdvancedSettings9.EnableAutoReconnect = false; // App owns reconnect
rdp.AdvancedSettings9.ContainerHandledFullScreen = 0;
rdp.AdvancedSettings9.RedirectClipboard = true;
```

**Phase 3: Password injection**

Passwords require casting through `IMsTscNonScriptable` from `GetOcx()`. This must happen after siting and before `Connect()`:

```csharp
var ocx = (IMsTscNonScriptable)rdp.GetOcx();
ocx.ClearTextPassword = password;
```

**Phase 4: Connection**

```csharp
rdp.Connect();
```

### 5.3 Disposal Sequence

Disposal follows a strict order. `Marshal.ReleaseComObject` is never called manually -- `AxHost.Dispose()` handles COM reference release. Manual release causes access violations and use-after-free bugs.

```csharp
// 1. Disconnect if connected (with 30s timeout)
if (rdp.Connected != 0) rdp.Disconnect();

// 2. Unsubscribe event handlers
rdp.OnLoginComplete -= OnLoginComplete;
rdp.OnDisconnected -= OnDisconnectedAfterConnectHandler;
rdp.OnLogonError -= OnLogonError;

// 3. WFH leak fix #1: null out HwndSourceKeyboardInputSite fields
var site = ((IKeyboardInputSink)host).KeyboardInputSite;
site?.Unregister();
// Reflection: null out _sinkElement and _sink fields

// 4. WFH leak fix #2: dispose HostContainerInternal (WinFormsAdapter)
// Without this, InputManager retains the host, leaking GDI handles
var adapterProp = typeof(WindowsFormsHost).GetProperty(
    "HostContainerInternal", BindingFlags.NonPublic | BindingFlags.Instance);
(adapterProp?.GetValue(host) as IDisposable)?.Dispose();

// 5. Detach and dispose AxHost
host.Child = null;
var ocx = rdp.GetOcx();
if (ocx is not null) Marshal.FinalReleaseComObject(ocx);
rdp.Dispose();

// 6. Dispose WindowsFormsHost
host.Dispose();
```

Both WFH leak fixes use reflection to clear internal fields that the framework does not clean up on its own. These are required to prevent GDI handle leaks across tab close cycles.

### 5.4 Airspace Problem and Mitigation

The WPF/WinForms airspace problem causes WindowsFormsHost content to always render on top of WPF elements in the same z-order. This means:

1. No WPF elements may overlap the RDP viewport.
2. WPF overlays (including `ContentDialog`) cannot hide RDP session pixels.
3. During window drag/resize, the RDP control flickers.

**Drag/resize mitigation (`AirspaceSwapper`):**

On `WM_ENTERSIZEMOVE`:
1. Snapshot each registered WFH's current `Visibility` state.
2. Capture a bitmap of each WFH child via `PrintWindow(PW_CLIENTONLY)`.
3. Show the bitmap as a WPF `Image` overlay.
4. Collapse all WFH elements (using `Visibility.Collapsed`, not `Hidden` -- `Hidden` can tear down the hosted AxHost HWND on some servers like xrdp).

On `WM_EXITSIZEMOVE`:
1. Restore each WFH to its pre-drag visibility (NOT unconditionally to `Visible` -- background tabs stay `Collapsed`).
2. Hide the snapshot overlays.

**Lock overlay mitigation:** See [Section 6.3](#63-lock-orchestration).

### 5.5 Thread Affinity

All RDP ActiveX operations must run on the STA UI thread. `RdpHostControl` asserts this at construction time and before disposal:

```csharp
if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
    throw new InvalidOperationException(
        "RdpHostControl must be created on the STA UI thread.");
```

`ConnectionCoordinator` handles STA thread marshaling for connection requests that may originate from background threads.

### 5.6 Session Limits and GDI Handles

Each RDP session consumes GDI handles. The practical limit is approximately 15-20 simultaneous sessions per process. A warning is displayed when approaching this limit. Inactive tabs set `BitmapPeristence = 0` (COM typelib misspelling -- intentional) and `CachePersistenceActive = 0` to reduce GDI handle consumption.

### 5.7 Reconnection

On unexpected disconnect (network drop, server reboot, session timeout), `RdpReconnectCoordinator` drives an exponential backoff reconnection loop:

- Initial delay: 2 seconds
- Subsequent delays: 4s, 8s, capped at 30 seconds maximum
- A reconnection overlay is shown with "Reconnect" and "Close" options
- User-initiated disconnects (tab close) suppress the reconnect overlay by setting `_userInitiatedClose = true` before calling `rdp.Disconnect()`

---

## 6. Security Architecture

### 6.1 Master Password / PIN

Deskbridge gates access to the UI with a master password or 6-digit PIN set by the user on first launch.

#### PBKDF2 Parameters (from `MasterPasswordService`)

```csharp
private const int Iterations = 600_000;  // OWASP 2023 for PBKDF2-HMAC-SHA256
private const int SaltBytes = 32;        // 256-bit (exceeds NIST SP 800-132 §5.1 minimum of 16)
private const int KeyBytes = 32;         // 256-bit (matches SHA-256 output length)
private static readonly HashAlgorithmName Alg = HashAlgorithmName.SHA256;
```

| Parameter | Value | Rationale |
| --- | --- | --- |
| Algorithm | PBKDF2-HMAC-SHA256 via `Rfc2898DeriveBytes.Pbkdf2` | Standard per OWASP and NIST SP 800-132 |
| Iterations | 600,000 | OWASP 2023 minimum for PBKDF2-HMAC-SHA256 |
| Salt size | 32 bytes (256-bit) | Exceeds NIST SP 800-132 section 5.1 minimum of 16 bytes |
| Derived key size | 32 bytes (256-bit) | Matches SHA-256 output length |
| Hashing time | 200-500 ms on a typical laptop | Test 10 enforces a 100 ms floor |

#### Storage Format

The hash is stored in `%AppData%\Deskbridge\auth.json`:

```json
{
  "passwordHash": "v1.<base64 salt>.<base64 key>",
  "authMode": "password",
  "schemaVersion": 1
}
```

The `v1` prefix reserves the envelope shape for future algorithm upgrades (e.g., Argon2id). The file is written atomically (write to `.tmp`, then `File.Move` with overwrite) to prevent corruption from process termination.

#### Verification

Password verification uses `CryptographicOperations.FixedTimeEquals` to compare the derived key against the stored key, preventing timing side-channel attacks.

#### PIN Mode

PIN mode accepts exactly 6 digits. The same PBKDF2 algorithm is used regardless of whether the user sets a password or PIN -- `authMode` is a presentation hint only. The lock screen reads `authMode` to determine whether to show a text field or a custom 6-cell PIN input control.

#### Minimum Length Enforcement

- Password: minimum 8 characters
- PIN: exactly 6 digits (digits only, enforced by `Password.All(char.IsDigit)`)

### 6.2 Credential Storage

Connection passwords are stored in Windows Credential Manager using `CredentialType.Generic`:

| Scope | Target format | Example |
| --- | --- | --- |
| Per-connection | `DESKBRIDGE/CONN/{connectionId}` | `DESKBRIDGE/CONN/a1b2c3d4-...` |
| Per-group | `DESKBRIDGE/GROUP/{groupId}` | `DESKBRIDGE/GROUP/f0e1d2c3-...` |

The `DESKBRIDGE/` prefix is used instead of `TERMSRV/` to avoid conflicts with Windows Defender Credential Guard, which intercepts `TERMSRV/*` entries for CredSSP delegation. Deskbridge injects passwords directly via `IMsTscNonScriptable.ClearTextPassword`, bypassing CredSSP entirely.

**Legacy migration:** A one-time idempotent migration runs at startup to move any legacy `TERMSRV/{hostname}` entries to `DESKBRIDGE/CONN/{id}` format. The migration checks both `CredentialType.Windows` (canonical for TERMSRV/) and `CredentialType.Generic` (legacy fallback), writes to the new target, and removes the old entries.

**Inheritance resolution:** `WindowsCredentialService.ResolveInherited()` walks the group parent chain with cycle detection:

```csharp
var visited = new HashSet<Guid>();
var groupId = connection.GroupId;
while (groupId.HasValue)
{
    if (!visited.Add(groupId.Value)) break; // Cycle detected
    var cred = GetForGroup(groupId.Value);
    if (cred is not null) return cred;
    var group = connectionStore.GetGroupById(groupId.Value);
    groupId = group?.ParentGroupId;
}
```

### 6.3 Lock Orchestration

The lock system has three components, each with a distinct responsibility:

#### AppLockController (Orchestrator)

Subscribes to `AppLockedEvent` on the bus. All lock triggers (idle timer, session switch, Ctrl+L, minimize-to-lock) fan in here.

**Lock flow:**
1. Capture every `HostContainer` child's `Visibility` into a dictionary.
2. Set all children to `Visibility.Collapsed` (including WindowsFormsHost elements, solving the airspace problem).
3. Flip `IAppLockState.IsLocked`.
4. Write audit record.
5. Show `LockOverlayDialog` (ContentDialog).

**Unlock flow:**
1. Restore each child to its pre-lock visibility state (NOT all-Visible -- inactive tab WFH elements that were already `Collapsed` stay `Collapsed`).
2. Flip `IAppLockState.IsLocked`.
3. Publish `AppUnlockedEvent`.
4. Write audit record.

#### IdleLockService

A `DispatcherTimer` configured with the user's auto-lock timeout (default 15 minutes, minimum 1 minute). Resets on mouse/keyboard input within the Deskbridge WPF UI.

Input originating inside a `WindowsFormsHost` (i.e., from an active RDP session) does NOT reset the idle timer. The filter walks up the visual/logical tree from the input source using `FindAncestor<WindowsFormsHost>()` and skips the reset if a WindowsFormsHost ancestor is found. This is intentional -- activity in a remote session is not Deskbridge UI activity.

#### SessionLockService

Subscribes to `SystemEvents.SessionSwitch`. Triggers lock on:
- `SessionLock` -- user locks the Windows session (Win+L)
- `ConsoleDisconnect` -- console session detached (fast user switching)
- `RemoteDisconnect` -- inbound RDP session to this machine disconnected

The event fires on a SystemEvents thread, so the handler marshals to the WPF dispatcher via `BeginInvoke` (not `Invoke`, to avoid blocking the SystemEvents thread).

The `SessionSwitchEventHandler` delegate is stored in a field to ensure the static event does not GC the service. `Dispose()` unsubscribes from the static event to prevent leaks.

### 6.4 Audit Logging

An append-only JSONL audit log records security-relevant events at `%AppData%\Deskbridge\audit-YYYY-MM.jsonl` (monthly rotation).

#### Record Schema

```json
{
  "ts": "2026-04-15T14:30:00.0000000Z",
  "type": "Connected",
  "connectionId": "a1b2c3d4-...",
  "user": "jsmith",
  "outcome": "success",
  "errorCode": null
}
```

#### Recorded Event Types

| Event | Trigger |
| --- | --- |
| `Connected` | RDP session established |
| `Disconnected` | RDP session ended |
| `FailedConnect` | Connection attempt failed |
| `Reconnected` | Automatic reconnection succeeded |
| `ConnectionCreated` | New connection added |
| `ConnectionEdited` | Connection properties modified |
| `ConnectionDeleted` | Connection removed |
| `ConnectionsImported` | Connections imported from file |
| `ConnectionsExported` | Connections exported to file |
| `CredentialStored` | Credential saved to Windows Credential Manager |
| `CredentialDeleted` | Credential removed |
| `AppStarted` | Application launched |
| `AppClosed` | Application exited |
| `UpdateApplied` | Application update installed |
| `AppLocked` | Application locked (manual, timeout, session switch, or minimise) |
| `AppUnlocked` | Application unlocked |
| `MasterPasswordChanged` | Master password or PIN changed |

#### Resilience

The audit logger uses a `SemaphoreSlim` for thread-safe appends. Files are opened with `FileShare.ReadWrite` so external tools (Splunk forwarders, log viewers) can tail the file while the application is running. IO failures are caught and re-emitted via Serilog -- audit failures never crash the application or block the operation that triggered the audit event.

### 6.5 Log Redaction

`RedactSensitivePolicy` is a custom `IDestructuringPolicy` registered with Serilog. When Serilog destructures any object, the policy checks each public property name against a denylist. Matching properties have their values replaced with `***REDACTED***` before the log event reaches any sink.

#### Denylist (from source)

```csharp
internal static readonly HashSet<string> Denylist = new(StringComparer.OrdinalIgnoreCase)
{
    "Password",
    "Secret",
    "Token",
    "CredentialData",
    "ApiKey",
    "ResolvedPassword",
    "MasterPassword",
};
```

The policy applies to all types -- including types added in future development -- without requiring per-type registration. A new POCO with a `Password` property will be automatically redacted.

---

## 7. UI Architecture

### 7.1 WPF-UI Fluent Theming

The application uses WPF-UI 4.2.0 for Windows 11 Fluent Design:

- **Window:** `FluentWindow` with Mica backdrop, rounded corners, and `ExtendsContentIntoTitleBar="True"`
- **Theme:** Dark theme applied via `ApplicationThemeManager.Apply()` with `#007ACC` accent colour override
- **Controls:** Standard WPF controls (TreeView, TabControl, etc.) are automatically restyled by `ControlsDictionary`
- **All colour references** use `DynamicResource` tokens (never `StaticResource`) to support runtime theme switching

### 7.2 Layout Structure

The layout follows a VS Code-inspired pattern:

```
+--[TitleBar 32px]----------------------------------+
|  [Icon Rail 36px] [Panel 240px] [Editor Area]     |
|  Connections      CONNECTIONS   [Tab Bar 30px]    |
|  Search           <TreeView>    [Viewport]        |
|  ---                                              |
|  Settings                                         |
+--[Status Bar 22px]--------------------------------+
```

| Element | Size | Implementation |
| --- | --- | --- |
| TitleBar | 32px | `ui:TitleBar` with explicit `Height="32"` |
| Icon rail | 36px | `Border` with vertical `StackPanel` of `ui:Button`s |
| Slide-out panel | 240px | `Border` with visibility-switched content areas |
| Tab bar | 30px | `ItemsControl` with horizontal `StackPanel` and `ScrollViewer` |
| Status bar | 22px | `Border` with `DockPanel`, accent-coloured background |

### 7.3 MVVM Pattern

CommunityToolkit.Mvvm 8.4.2 source generators:

- `[ObservableProperty]` on partial properties for change notification (C# 14 `field` keyword support)
- `[RelayCommand]` for `ICommand` generation (sync and async variants)
- `[NotifyPropertyChangedFor]` for dependent property notifications
- `WeakReferenceMessenger` as the event bus implementation

ViewModels are injected via DI. `MainWindowViewModel` is a singleton with the following key properties:

```csharp
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] public partial PanelMode ActivePanelMode { get; set; }
    [ObservableProperty] public partial ObservableCollection<TabItemViewModel> Tabs { get; set; }
    [ObservableProperty] public partial TabItemViewModel? ActiveTab { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; }
    [ObservableProperty] public partial bool UpdateAvailable { get; set; }
    [ObservableProperty] public partial bool IsFullscreen { get; set; }
    [ObservableProperty] public partial int AutoLockTimeoutMinutes { get; set; }
    [ObservableProperty] public partial bool LockOnMinimise { get; set; }
    [ObservableProperty] public partial bool RequireMasterPassword { get; set; }
}
```

### 7.4 Command Palette

The command palette (Ctrl+Shift+P) provides fuzzy search across connections and commands:

**Empty state:** top 5 recent connections, then the registered commands (New Connection, Open Settings, Disconnect All, Quick Connect, Import Connections, Export JSON, Export CSV).

**Search state:** `IConnectionQuery.Search()` is called exactly once per keystroke. Results are scored: connection scores are approximated as `100 - index` from the search result. Commands are scored via `ICommandPaletteService.ScoreCommand()`. The merge sorts by score descending, with commands winning ties.

The `CommandPaletteViewModel` is transient -- a fresh instance per palette session so `SearchText` starts blank each time.

### 7.5 Toast Notification Stack

A custom `ToastStackControl` replaces WPF-UI's single-snackbar FIFO `SnackbarPresenter`:

- Maximum 3 visible items -- a 4th push evicts the oldest
- Newest items appear at the top (index 0)
- Each non-sticky item owns a `DispatcherTimer` for auto-dismiss
- Hover pauses all timers; mouse leave resumes them
- Explicit dismiss removes immediately regardless of sticky state

`ToastSubscriptionService` subscribes to 6 bus events and pushes formatted toasts into the shared `ToastStackViewModel`.

### 7.6 Tab Management

- One connection per tab; active tab renders a live ActiveX control
- Inactive tabs have their `WindowsFormsHost` visibility set to `Collapsed` (WFH elements are NEVER re-parented after AxHost init -- this would tear down HwndSource and break the RDP session)
- Persistent multi-host container (`HostContainer` Grid in MainWindow): every WFH is parented here exactly once on mount and removed only on tab close
- Tab reorder via drag-and-drop (`TabReorderBehavior` attached behavior)
- Tab state indicators: connecting (ProgressRing), reconnecting (amber dot), error (red dot)
- Tab close: middle-click, Ctrl+W, close button, context menu (Close / Close Others / Close All)
- Last-closed LRU for Ctrl+Shift+T reopening

---

## 8. Data Layer

### 8.1 connections.json

Stored at `%AppData%\Deskbridge\connections.json`. Contains connection metadata only -- never passwords.

Fields per connection: `Id` (GUID), `Name`, `Hostname`, `Port` (default 3389), `Username`, `Domain`, `Protocol` (enum), `GroupId`, `Notes`, `CreatedAt`, `UpdatedAt`, `DisplaySettings` (Width, Height, SmartSizing), `Tags`, `CredentialMode` (Own/Inherit/Prompt), `SortOrder`, `EnableCredSspSupport`, `AuthenticationLevel`.

Groups: `Id`, `Name`, `ParentGroupId`, `SortOrder`.

Written atomically (tmp + rename) by `JsonConnectionStore`.

### 8.2 settings.json

Stored at `%AppData%\Deskbridge\settings.json`. Contains application preferences:

- Window position, size, maximised state, sidebar state
- Auto-lock timeout (in minutes)
- Lock-on-minimise toggle
- Require-master-password toggle
- Update channel preference (stable/beta)
- Schema version

Written atomically by `WindowStateService`.

### 8.3 auth.json

Stored at `%AppData%\Deskbridge\auth.json`. Contains the PBKDF2 hash envelope:

```json
{
  "passwordHash": "v1.<base64 salt>.<base64 key>",
  "authMode": "password",
  "schemaVersion": 1
}
```

Written atomically by `MasterPasswordService`. The `authMode` field (`"password"` or `"pin"`) is a presentation hint only and does not affect the KDF algorithm.

### 8.4 Audit Log (JSONL)

Stored at `%AppData%\Deskbridge\audit-YYYY-MM.jsonl`. Monthly rotation. One JSON object per line. See [Section 6.4](#64-audit-logging) for the full schema and event types.

### 8.5 Serilog Rolling Logs

Stored at `%AppData%\Deskbridge\logs\deskbridge-YYYYMMDD.log`.

| Setting | Value |
| --- | --- |
| Rolling interval | Daily |
| Size limit per file | 10 MB |
| Roll on size limit | Yes |
| Retained file count | 5 |
| Flush interval | 1 second |
| Minimum level | Information |
| Shared mode | No (single writer per process) |

---

## 9. Auto-Update

### 9.1 Velopack Integration

Deskbridge uses Velopack 0.0.1298 for auto-update, with `GithubSource` configured for the project's GitHub repository. The `UpdateService` wraps `UpdateManager` behind `IUpdateService`:

```csharp
public UpdateService(IEventBus bus, string repoUrl, bool useBetaChannel)
{
    var source = new GithubSource(repoUrl, accessToken: null, prerelease: useBetaChannel);
    var options = new UpdateOptions
    {
        ExplicitChannel = useBetaChannel ? "beta" : "stable",
        AllowVersionDowngrade = false,
    };
    _mgr = new UpdateManager(source, options);
}
```

### 9.2 Channel Support

- **Stable:** Default. Receives only releases tagged without a prerelease suffix (e.g., `v1.0.1`).
- **Beta:** Opt-in via Settings. Receives prerelease versions (e.g., `v1.1.0-beta.1`). Channel preference is stored in `settings.json`.

### 9.3 Update Flow

1. **Check:** On startup, a background task calls `CheckForUpdatesAsync()`. If an update is found, `UpdateAvailableEvent` is published to the event bus.
2. **Notify:** `MainWindowViewModel` subscribes and shows a badge in the status bar (download icon + version text).
3. **Download:** User clicks the badge. `DownloadUpdatesAsync()` runs with progress reporting (0-100% displayed in the status bar).
4. **Confirm:** After download, a confirmation dialog asks whether to restart now.
5. **Restart:** `ApplyUpdatesAndRestart()` calls `UpdateManager.ApplyUpdatesAndRestart()`.

**Dev-mode guard:** When `IsInstalled` is `false` (running from IDE), all update operations log a warning and return immediately.

**Failure tolerance:** Update check failures are caught, logged, and ignored. The application continues normally. A `SemaphoreSlim` prevents concurrent update checks.

---

## 10. Import/Export

### 10.1 Import Architecture

The `IConnectionImporter` interface supports multiple import sources:

```csharp
public interface IConnectionImporter
{
    string SourceName { get; }
    string FileFilter { get; }
    Task<ImportResult> ParseAsync(Stream stream, CancellationToken ct = default);
}
```

Phase 1 ships `MRemoteNGImporter` only. Future importers (Royal TS, RDCMan, etc.) add registrations in `ConfigureServices` without modifying existing code.

### 10.2 mRemoteNG Parser

Parses `confCons.xml` (mRemoteNG's configuration format):

- **XXE prevention:** `XmlReaderSettings.DtdProcessing = DtdProcessing.Prohibit`
- **Encrypted file detection:** Checks `FullFileEncryption="True"` attribute and surfaces a user-friendly error message
- **Protocol mapping:** RDP, SSH2/SSH1 (mapped to SSH), VNC; defaults to RDP for unknown protocols
- **Name sanitization:** Strips path separators (`/`, `\`) and control characters to prevent path traversal or UI injection
- **Password handling:** The `Password` attribute is explicitly skipped -- never read, stored, or logged. Imported connections have `CredentialMode = Prompt`.

### 10.3 Import Wizard Flow

4-step wizard: source selection, file picker, tree preview with checkboxes, confirm.

- Tree preview shows imported nodes with per-item checkboxes
- Folder toggle cascades to all children
- Non-RDP connections are imported but marked unsupported
- Duplicate detection by hostname (auto-rename with "(imported)" suffix)
- Groups are created or reused by name + parent path

### 10.4 Export Formats

`ConnectionExporter` provides two formats:

**JSON export:** Tree structure with nested groups and connections. Includes export date, version, and full connection metadata. No credentials.

```json
{
  "exportDate": "2026-04-15T14:30:00.0000000Z",
  "version": "1.0",
  "connections": [
    {
      "type": "group",
      "name": "Production",
      "children": [
        {
          "type": "connection",
          "name": "DB Server",
          "hostname": "db01.example.com",
          "port": 3389,
          "username": "admin",
          "protocol": "Rdp"
        }
      ]
    }
  ]
}
```

**CSV export:** Flat list with columns: Name, Hostname, Port, Username, Domain, Protocol, FolderPath, Notes. No credentials. Proper CSV escaping for fields containing commas, quotes, or newlines.

---

## 11. CI/CD Pipeline

### 11.1 GitHub Actions Workflow

The `build.yml` workflow handles build, test, and release:

**Triggers:**
- Push to `main` branch
- Pull requests to `main`
- Tag push matching `v*.*.*`
- Manual dispatch

### 11.2 Build Job

```yaml
steps:
  - actions/checkout@v4
  - actions/setup-dotnet@v4 (dotnet-version: 10.0.x)
  - dotnet restore Deskbridge.sln
  - dotnet build Deskbridge.sln --configuration Release --no-restore
  - dotnet test --filter "Category!=UAT&Category!=Slow"
  - dotnet publish --runtime win-x64 --self-contained true -p:PublishSingleFile=true
```

Tests exclude `UAT` and `Slow` categories. Test results are uploaded as artifacts (TRX format, 14-day retention).

The published artifact is a self-contained single-file executable for win-x64 (30-day retention).

### 11.3 Release Job

Triggered only on tag push (`v*.*.*`). Channel detection:

```bash
if [[ "$VERSION" == *"-"* ]]; then
  channel=beta; prerelease=true
else
  channel=stable; prerelease=false
fi
```

Release steps:
1. Publish with version from tag
2. Install `vpk` dotnet tool
3. Download previous release (for delta updates, `continue-on-error: true`)
4. `vpk pack` with channel flag
5. `vpk upload github` with `--publish` flag

---

## 12. Testing Strategy

### 12.1 Test Metrics

- **473 test methods** across **59 test files**
- Frameworks: xunit.v3 3.2.*, FluentAssertions 8.9.*, NSubstitute 5.3.*
- CI filter: `Category!=UAT&Category!=Slow` (excludes manual verification and long-running tests)

### 12.2 Test Categories

| Category | Location | Examples |
| --- | --- | --- |
| Unit | Root + subfolders | `MasterPasswordServiceTests` (17 tests), `MainWindowViewModelTests` (27 tests) |
| Pipeline | `Pipeline/` | `ConnectStageTests`, `DisposeStageTests`, `ResolveCredentialsStageTests` |
| Security | `Security/` | `AppLockControllerTests`, `IdleLockServiceTests`, `PasswordLeakTests` |
| RDP | `Rdp/` | `SitingGuardTests`, `AirspaceSwapperTests`, `ErrorIsolationTests` |
| Integration | `Integration/` | `HostContainerPersistenceTests`, `KeyboardShortcutTests` (30 tests) |
| Import | `Import/` | `MRemoteNGImporterTests` (13 tests), `ConnectionExporterTests` (13 tests) |
| Notifications | `Notifications/` | `ToastStackViewModelTests`, `ToastSubscriptionServiceTests` |
| Palette | `Palette/` | `CommandPaletteViewModelTests` (15 tests), `CommandPaletteServiceTests` (13 tests) |
| Update | `Update/` | `UpdateServiceTests` (7 tests) |
| DI | Root | `DiCompositionTests` (14 tests) |
| Tabs | `Tabs/` | `TabHostManagerTests` (15 tests), `TabHostManagerLruTests` |
| ViewModels | `ViewModels/` | `TabItemViewModelTests` (20 tests), `ReconnectOverlayViewModelTests` |
| Controls | `Controls/` | `PinInputControlTests` (10 tests) |
| Smoke | `Smoke/` | `RdpHostControlSmokeTests` (4 tests, likely Category=Slow or UAT) |

### 12.3 Source-Grep Structural Tests

`DiCompositionTests` (14 tests) validates the DI container composition by verifying that all expected singletons and transients resolve without exception. This catches missing registrations, circular dependencies, and factory lambda errors at test time rather than runtime.

### 12.4 TempDirScope Isolation Pattern

File-system-touching tests use `TempDirScope`, a disposable helper that creates a unique temporary directory per test and cleans it up on disposal:

```csharp
using var scope = new TempDirScope();
var service = new MasterPasswordService(scope.Path);
// Tests run against isolated temp files
```

Used by: `MasterPasswordServiceTests`, `WindowStateServiceTests`, `SerilogConfigTests`, `AuditLoggerTests`.

### 12.5 Key Test Patterns

- **Security tests:** `PasswordLeakTests` verifies that passwords do not appear in log output (Serilog redaction). `MasterPasswordServiceTests` includes a stopwatch assertion (Test 10) enforcing that 600,000 PBKDF2 iterations take at least 100 ms.
- **RDP tests:** `SitingGuardTests` verifies that `AxSiting.SiteAndConfigure` throws `InvalidOperationException` with "not sited" when the handle is zero. `ErrorIsolationTests` verifies COM exception handling paths.
- **UpdateService tests:** Use a test subclass that overrides the virtual seams (`CheckForUpdatesInternalAsync`, `DownloadUpdatesInternalAsync`, `ApplyUpdatesInternalAndRestart`) to avoid requiring a real Velopack installation.
- **UAT checklists:** Smoke tests marked with `Category=UAT` are excluded from CI and used for manual verification of end-to-end flows.

---

## 13. Enterprise Considerations

### 13.1 Credential Guard Compatibility

Deskbridge stores connection credentials under `DESKBRIDGE/CONN/{connectionId}` with `CredentialType.Generic`. This deliberately avoids the `TERMSRV/{hostname}` format that triggers the "Windows Defender Credential Guard does not allow using saved credentials" error. Passwords are injected directly into the RDP ActiveX control via `IMsTscNonScriptable.ClearTextPassword`, bypassing CredSSP entirely.

### 13.2 Per-User Installation

Deskbridge installs per-user to `%LocalAppData%\Deskbridge` via Velopack. No administrator rights are required. Multiple Windows users on the same machine have independent installations with separate connection databases and credentials.

- **Application files:** `%LocalAppData%\Deskbridge` (~150 MB self-contained)
- **User data:** `%AppData%\Deskbridge` (connections, settings, auth, logs, audit)

### 13.3 No Telemetry, No Cloud Dependencies

Deskbridge makes no network calls except:

1. **RDP connections** initiated by the user to their specified hosts
2. **HTTPS to GitHub Releases API** for update checks (via Velopack's `GithubSource`)

No analytics, crash reporting, usage metrics, or phone-home functionality exists. Proxy settings are inherited from the system (`HttpClient` defaults). To disable update checks in an enterprise environment, block outbound HTTPS to `api.github.com` at the network level.

### 13.4 Group Policy Non-Interference

Deskbridge does not read from or write to the Windows Registry. No Group Policy Administrative Templates (ADMX) are provided. RDP-specific Group Policies (NLA requirements, etc.) are handled by Microsoft's RDP client component (mstscax.dll), not by Deskbridge.

### 13.5 Data Locations Summary

| Path | Contents | Sensitive? |
| --- | --- | --- |
| `%AppData%\Deskbridge\connections.json` | Connection metadata (hostnames, usernames, folders, display settings) | No passwords |
| `%AppData%\Deskbridge\settings.json` | Window position, sidebar state, timeouts, update channel | No secrets |
| `%AppData%\Deskbridge\auth.json` | PBKDF2 hash envelope | Hash only, no plaintext |
| `%AppData%\Deskbridge\logs\` | Serilog rolling files | Redacted (no passwords) |
| `%AppData%\Deskbridge\audit-YYYY-MM.jsonl` | Audit trail | Event types + usernames, no passwords |
| Windows Credential Manager | Connection and group passwords | DPAPI-protected by OS |

### 13.6 Backup and Restore

To back up: copy the entire `%AppData%\Deskbridge` directory. Connection passwords are not included -- they are in Windows Credential Manager, tied to the Windows user profile. After restore, users re-enter connection passwords.

### 13.7 Build Configuration

All projects share `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

Central package management via `Directory.Packages.props` with floating patch versions:

| Package | Version |
| --- | --- |
| WPF-UI | 4.2.* |
| CommunityToolkit.Mvvm | 8.4.* |
| Microsoft.Extensions.DependencyInjection | 10.0.* |
| Velopack | 0.0.1298 (pinned) |
| Serilog | 4.3.* |
| Serilog.Sinks.File | 7.0.* |
| Serilog.Extensions.Logging | 9.0.* |
| AdysTech.CredentialManager | 3.1.* |
| xunit.v3 | 3.2.* |
| FluentAssertions | 8.9.* |
| NSubstitute | 5.3.* |

### 13.8 Session Limits

Each Deskbridge instance supports approximately 15-20 simultaneous RDP sessions. This is a practical limit imposed by GDI handle consumption, not a licensing or configuration restriction. A Snackbar warning is displayed when approaching this limit. Inactive tabs reduce consumption with `BitmapPeristence = 0` and `CachePersistenceActive = 0`.
