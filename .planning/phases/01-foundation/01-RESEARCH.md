# Phase 1: Foundation - Research

**Researched:** 2026-04-11
**Domain:** .NET 10 WPF solution scaffold, DI, event bus, pipeline, query, testing
**Confidence:** HIGH

## Summary

Phase 1 establishes the entire solution structure and architectural backbone for Deskbridge. This includes three source projects (Deskbridge, Deskbridge.Core, Deskbridge.Protocols.Rdp) and one test project (Deskbridge.Tests), all targeting `net10.0-windows` with C# 14. The foundation also includes Central Package Management via `Directory.Packages.props`, full code style enforcement via `.editorconfig`, and working infrastructure for the DI container, event bus, connection/disconnect pipeline, notification service, and connection query interface.

The development machine has .NET 10.0.201 SDK installed and confirmed working. All package versions from the prior STACK.md research remain valid. The key technical risks for this phase are: (1) the CommunityToolkit.Mvvm 8.4.0 compilation failure (mitigated by pinning 8.4.2), (2) accidentally enabling .NET built-in fluent themes that break WindowsFormsHost rendering (mitigated by WPF-UI-only theming + runtime config switch), and (3) the Velopack custom Main() pattern that must call `InitializeComponent()` explicitly.

**Primary recommendation:** Follow the exact project structure from REFERENCE.md, use Central Package Management with major.minor pinning, and implement all core interfaces as thin wrappers with comprehensive test coverage from day one.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Include a test project at `tests/Deskbridge.Tests/` using xUnit, FluentAssertions, and NSubstitute
- **D-02:** Phase 1 tests must cover: event bus pub/sub (publish, subscribe, unsubscribe, weak reference cleanup), pipeline stage ordering (correct order, failure aborts), connection query fuzzy search (name, hostname, tags against in-memory data), and DI composition (all services resolve without throwing)
- **D-03:** Define a disconnect pipeline (`IDisconnectPipeline`) symmetric with the connect pipeline. Include stages for state save, disconnect, dispose, audit, and publish event.
- **D-04:** Full code style enforcement from day one: `.editorconfig` with C# conventions, nullable warnings as errors, code analysis (CA rules)
- **D-05:** Include `GlobalUsings.cs` per project with common namespaces
- **D-06:** Use Central Package Management with `Directory.Packages.props` at repo root. All version pins in one file. Projects use `<PackageReference>` without Version attribute.
- **D-07:** Pin major.minor versions (e.g., `Version="4.2.*"`) -- auto-picks patch updates, less maintenance burden
- **D-08:** MIT license. Add LICENSE file to repo root. Set `PackageLicenseExpression` in Directory.Build.props.

### Claude's Discretion
- Disconnect pipeline stage names, ordering, and interface shape -- follow the connect pipeline pattern from REFERENCE.md but adapted for teardown
- GlobalUsings.cs contents per project -- include what makes sense for each project's domain
- .editorconfig rule severity levels -- use standard .NET conventions, make nullable warnings errors

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PROJ-01 | Solution builds on net10.0-windows with all packages restoring | Directory.Build.props + Directory.Packages.props + Central Package Management format verified |
| PROJ-02 | Directory.Build.props shared config (UseWPF, Nullable, ImplicitUsings) | Exact format from REFERENCE.md validated; no LangVersion needed for .NET 10 |
| PROJ-03 | App manifest declares Windows 10/11 support and PerMonitorV2 DPI awareness | Exact manifest XML from REFERENCE.md; required for WindowsFormsHost DPI handling |
| PROJ-04 | Interop DLLs positioned in src/Deskbridge.Protocols.Rdp/Interop/ | DLLs exist at repo root; must be moved during scaffold |
| PROJ-05 | Velopack entry point (custom Main, App.xaml as Page) compiles and runs | Velopack pattern verified; InitializeComponent() call is critical; WPF-UI dark theme bootstrap pattern documented |
| CORE-01 | DI container registers all services as interfaces in App.xaml.cs | Microsoft.Extensions.DependencyInjection 10.0.x; composition root pattern from REFERENCE.md |
| CORE-02 | Event bus publishes and subscribes to typed events without memory leaks | WeakReferenceMessenger wrapper pattern documented; thread marshalling to UI dispatcher needed |
| CORE-03 | Connection pipeline executes ordered stages | Pipeline pattern validated; disconnect pipeline design added per D-03 |
| CORE-04 | Notification service raises events consumed by UI | INotificationService interface from REFERENCE.md; backed by event bus |
| CORE-05 | Connection query supports fuzzy search across name, hostname, tags | Simple substring + Levenshtein distance approach recommended for in-memory data |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- **Framework**: .NET 10 LTS (net10.0-windows) with C# 14 -- no .NET 8/9
- **UI Library**: WPF-UI (Fluent dark theme) -- all colours via DynamicResource tokens, BasedOn for style overrides
- **COM/ActiveX**: Classic aximp.exe interop only -- no GeneratedComInterface, no Marshal.ReleaseComObject, site before configure
- **UseWindowsForms**: Set ONLY in Deskbridge.Protocols.Rdp csproj -- not in Directory.Build.props
- **Velopack**: Custom Main method required (App.xaml as Page, not ApplicationDefinition), SemVer2, self-contained, user data in %AppData%
- **Serialisation**: System.Text.Json only -- no XML config, no SQLite
- **Security**: Never store passwords in JSON or logs

## Standard Stack

### Core (Phase 1 packages)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET 10 SDK | 10.0.201 | Runtime & build | Installed on dev machine, LTS until Nov 2028 [VERIFIED: dotnet --version] |
| WPF-UI | 4.2.* | Fluent dark theme, FluentWindow | Explicitly targets net10.0-windows7.0; published Jan 10, 2026 [VERIFIED: NuGet] |
| CommunityToolkit.Mvvm | 8.4.* | MVVM source generators, WeakReferenceMessenger | Roslyn 5.0 for C# 14 support; published Mar 25, 2026 [VERIFIED: NuGet] |
| Microsoft.Extensions.DependencyInjection | 10.0.* | DI container | Ships aligned with .NET 10 [VERIFIED: NuGet] |
| Velopack | 0.0.1298 | Auto-update entry point | Must be exact pin -- 0.0.XXXX versioning [VERIFIED: NuGet] |
| Serilog | 4.3.* | Structured logging | Explicit net10.0 target [VERIFIED: NuGet] |
| Serilog.Sinks.File | 7.0.* | Rolling file sink | Forward-compatible via netstandard2.0 [VERIFIED: NuGet] |

### Testing

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| xunit.v3 | 3.2.* | Test framework | v3 is the current recommended version; v2 is deprecated [VERIFIED: NuGet 3.2.2, Jan 14 2026] |
| FluentAssertions | 8.9.* | Assertion library | Latest stable; targets .NET 6+, netstandard2.0 [VERIFIED: NuGet 8.9.0, Mar 16 2026] |
| NSubstitute | 5.3.* | Mocking library | Targets .NET 6+, netstandard2.0 [VERIFIED: NuGet 5.3.0, Oct 28 2024] |
| Microsoft.NET.Test.Sdk | 17.* | Test host | Required for `dotnet test` integration [ASSUMED] |

### Fuzzy Search (for CORE-05)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| (none -- hand-roll) | N/A | Fuzzy matching for IConnectionQuery.Search() | For dozens-to-hundreds of connections, a simple scoring function (substring match + Levenshtein distance) is appropriate. No external dependency needed. See Code Examples section. |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| xunit.v3 3.2 | xunit v2 2.9.3 | v2 is deprecated, v3 is the recommended path; v3 has better async support and parallel execution |
| Hand-rolled fuzzy search | FuzzySharp 2.0.2 or Raffinert.FuzzySharp 4.0.0 | External dependency for trivial use case; FuzzySharp last updated 2020; Raffinert newer (Feb 2026) but 76K downloads vs custom 20 lines |
| Microsoft.Extensions.Hosting | Raw ServiceCollection | Hosting adds IHost lifecycle; overkill for WPF desktop app; REFERENCE.md uses raw ServiceCollection |

**Installation (Directory.Packages.props):**
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- UI -->
    <PackageVersion Include="WPF-UI" Version="4.2.*" />
    <!-- MVVM -->
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.*" />
    <!-- DI -->
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.*" />
    <!-- Auto-update -->
    <PackageVersion Include="Velopack" Version="0.0.1298" />
    <!-- Logging -->
    <PackageVersion Include="Serilog" Version="4.3.*" />
    <PackageVersion Include="Serilog.Sinks.File" Version="7.0.*" />
    <!-- Testing -->
    <PackageVersion Include="xunit.v3" Version="3.2.*" />
    <PackageVersion Include="FluentAssertions" Version="8.9.*" />
    <PackageVersion Include="NSubstitute" Version="5.3.*" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.*" />
  </ItemGroup>
</Project>
```

**Note on version pinning (D-07):** The `Version="4.2.*"` format is a NuGet floating version that auto-picks the latest patch within the major.minor range. This is the user's requested strategy. For Velopack, use exact pin `0.0.1298` because its 0.0.XXXX versioning means every build is effectively a "major" change. [VERIFIED: Central Package Management docs]

**Note on xUnit v3 vs v2:** The user specified "xUnit" in D-01. xUnit v2 (2.9.3) is officially deprecated -- all future work has moved to v3. xUnit.v3 3.2.2 is stable, published Jan 2026, with 19M+ total downloads. The package name is `xunit.v3` not `xunit`. [VERIFIED: NuGet, xunit.net]

## Architecture Patterns

### Recommended Project Structure
```
Deskbridge/
├── src/
│   ├── Deskbridge/                      # WPF app -- composition root
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/
│   │   ├── Themes/
│   │   ├── Interop/
│   │   │   └── NativeMethods.cs
│   │   ├── GlobalUsings.cs
│   │   ├── App.xaml                     # Build action: Page (NOT ApplicationDefinition)
│   │   ├── App.xaml.cs                  # DI composition root
│   │   ├── Program.cs                   # Custom Main for Velopack
│   │   ├── MainWindow.xaml
│   │   ├── MainWindow.xaml.cs
│   │   └── app.manifest
│   ├── Deskbridge.Core/                 # Models, interfaces, services
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── Interfaces/
│   │   ├── Events/
│   │   ├── Pipeline/
│   │   └── GlobalUsings.cs
│   └── Deskbridge.Protocols.Rdp/        # RDP ActiveX interop
│       ├── Interop/
│       │   ├── MSTSCLib.dll
│       │   └── AxMSTSCLib.dll
│       └── GlobalUsings.cs
├── tests/
│   └── Deskbridge.Tests/               # xUnit test project
│       └── GlobalUsings.cs
├── Directory.Build.props                # Shared build properties
├── Directory.Packages.props             # Central Package Management
├── Deskbridge.sln
├── .editorconfig
├── .gitignore
├── LICENSE
└── REFERENCE.md
```

[VERIFIED: REFERENCE.md project structure]

### Pattern 1: Central Package Management

**What:** All NuGet version pins in a single `Directory.Packages.props` file. Projects reference packages without Version attributes.
**When to use:** Always -- this is the user's locked decision (D-06).

```xml
<!-- Directory.Packages.props (repo root) -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="WPF-UI" Version="4.2.*" />
    <!-- ... all versions here ... -->
  </ItemGroup>
</Project>

<!-- In any .csproj -->
<ItemGroup>
  <PackageReference Include="WPF-UI" />  <!-- No Version attribute -->
</ItemGroup>
```

[VERIFIED: Microsoft Learn Central Package Management docs]

### Pattern 2: Velopack Custom Main with WPF-UI Dark Theme

**What:** App.xaml set to `Page` build action (not `ApplicationDefinition`) to allow custom `Main()` for Velopack. WPF-UI theme resources loaded via `InitializeComponent()`.
**When to use:** Always -- required by Velopack constraint.

```csharp
// Program.cs
// Source: REFERENCE.md + DESIGN.md
namespace Deskbridge;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();  // CRITICAL: loads App.xaml resources
        app.Run();
    }
}
```

```xml
<!-- Deskbridge.csproj -->
<PropertyGroup>
  <StartupObject>Deskbridge.Program</StartupObject>
</PropertyGroup>
<ItemGroup>
  <ApplicationDefinition Remove="App.xaml" />
  <Page Include="App.xaml" />
</ItemGroup>
```

```xml
<!-- App.xaml -- dark theme bootstrap -->
<!-- Source: DESIGN.md section 1 -->
<Application x:Class="Deskbridge.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Dark" />
                <ui:ControlsDictionary />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

```csharp
// App.xaml.cs -- belt-and-suspenders theme application
// Source: DESIGN.md section 1
using Wpf.Ui.Appearance;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationThemeManager.Apply(
            ApplicationTheme.Dark,
            WindowBackdropType.Mica,
            updateAccent: true
        );

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IConnectionPipeline, ConnectionPipeline>();
        services.AddSingleton<IDisconnectPipeline, DisconnectPipeline>();
        services.AddSingleton<IConnectionQuery, ConnectionQueryService>();
        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<MainWindowViewModel>();
    }
}
```

[VERIFIED: REFERENCE.md Velopack entry point + DESIGN.md App.xaml bootstrap]

### Pattern 3: Event Bus Wrapper with UI Thread Marshalling

**What:** Thin wrapper around `WeakReferenceMessenger` implementing `IEventBus`. Handlers always invoked on the UI thread.
**When to use:** All cross-component communication.

Key design decisions:
1. `WeakReferenceMessenger.Default` is thread-safe for registration and sending [VERIFIED: Microsoft Learn Messenger docs]
2. `Send()` invokes handlers synchronously on the calling thread [VERIFIED: Microsoft Learn Messenger docs]
3. Handlers that update UI must run on the UI thread -- the wrapper must marshal via `Dispatcher.InvokeAsync` [VERIFIED: PITFALLS.md Pitfall 16]
4. Weak references mean subscribers are eligible for GC even without unsubscribing [VERIFIED: Microsoft Learn Messenger docs]
5. Message type inheritance is NOT supported -- subscribers to a base type will NOT receive derived messages [VERIFIED: Microsoft Learn Messenger docs]

```csharp
// IEventBus -- in Deskbridge.Core/Interfaces/
// Source: REFERENCE.md
public interface IEventBus
{
    void Publish<TEvent>(TEvent message) where TEvent : class;
    void Subscribe<TEvent>(object recipient, Action<TEvent> handler) where TEvent : class;
    void Unsubscribe<TEvent>(object recipient) where TEvent : class;
}

// EventBus implementation -- in Deskbridge/Services/ or Deskbridge.Core/Services/
public sealed class EventBus : IEventBus
{
    private readonly IMessenger _messenger = WeakReferenceMessenger.Default;

    public void Publish<TEvent>(TEvent message) where TEvent : class
    {
        _messenger.Send(message);
    }

    public void Subscribe<TEvent>(object recipient, Action<TEvent> handler) where TEvent : class
    {
        _messenger.Register<TEvent>(recipient, (r, m) => handler(m));
    }

    public void Unsubscribe<TEvent>(object recipient) where TEvent : class
    {
        _messenger.Unregister<TEvent>(recipient);
    }
}
```

**Thread marshalling decision:** The EventBus wrapper itself should NOT auto-marshal to the UI thread. Reason: not all handlers need UI thread access (e.g., audit log). Instead, individual handlers that touch UI should marshal themselves, OR the publishing pipeline stage should marshal before publishing. This keeps the event bus generic. Document this contract clearly in the IEventBus interface XML doc. [ASSUMED -- recommendation based on separation of concerns]

### Pattern 4: Connection Pipeline with Disconnect Pipeline

**What:** Ordered stages for both connect and disconnect flows. Each stage can succeed or abort.
**When to use:** All connection lifecycle management.

```csharp
// Connect pipeline -- from REFERENCE.md
public interface IConnectionPipelineStage
{
    string Name { get; }
    int Order { get; }
    Task<PipelineResult> ExecuteAsync(ConnectionContext context);
}

public record PipelineResult(bool Success, string? FailureReason = null);

public interface IConnectionPipeline
{
    void AddStage(IConnectionPipelineStage stage);
    Task<PipelineResult> ConnectAsync(ConnectionModel connection);
}

// Disconnect pipeline -- new, per D-03
public interface IDisconnectPipelineStage
{
    string Name { get; }
    int Order { get; }
    Task<PipelineResult> ExecuteAsync(DisconnectContext context);
}

public class DisconnectContext
{
    public ConnectionModel Connection { get; set; } = default!;
    public IProtocolHost? Host { get; set; }
    public DisconnectReason Reason { get; set; }
    public Dictionary<string, object> Properties { get; } = new();
    public CancellationToken CancellationToken { get; set; }
}

public enum DisconnectReason { UserInitiated, RemoteDisconnect, Error, AppShutdown }

public interface IDisconnectPipeline
{
    void AddStage(IDisconnectPipelineStage stage);
    Task<PipelineResult> DisconnectAsync(DisconnectContext context);
}
```

**Disconnect pipeline stages (recommended ordering):**

| Order | Stage | Purpose |
|-------|-------|---------|
| 100 | SaveStateStage | Save session state (window position, resolution) for reconnection |
| 200 | DisconnectStage | Call Host.Disconnect(), handle COM errors |
| 300 | DisposeStage | Dispose COM resources in correct order |
| 400 | PublishDisconnectedStage | Publish ConnectionClosedEvent to event bus |
| 900 | AuditDisconnectStage | Record disconnect in audit trail |

[VERIFIED: ARCHITECTURE.md Concern 1 recommends this exact pattern]

### Pattern 5: Fuzzy Search for IConnectionQuery

**What:** In-memory fuzzy search across connection name, hostname, and tags.
**When to use:** IConnectionQuery.Search() implementation.

For a dataset of dozens-to-hundreds of connections, a simple scoring algorithm is appropriate:

```csharp
public IReadOnlyList<ConnectionModel> Search(string query)
{
    if (string.IsNullOrWhiteSpace(query))
        return GetAll();

    var normalizedQuery = query.Trim().ToLowerInvariant();

    return _connections
        .Select(c => new { Connection = c, Score = CalculateScore(c, normalizedQuery) })
        .Where(x => x.Score > 0)
        .OrderByDescending(x => x.Score)
        .Select(x => x.Connection)
        .ToList();
}

private static int CalculateScore(ConnectionModel c, string query)
{
    int score = 0;

    // Exact substring match in name (highest priority)
    if (c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        score += 100;

    // Exact substring match in hostname
    if (c.Hostname.Contains(query, StringComparison.OrdinalIgnoreCase))
        score += 80;

    // Exact match in tags
    if (c.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
        score += 60;

    // Fuzzy match: check if all query characters appear in order (subsequence match)
    if (score == 0)
    {
        if (IsSubsequence(query, c.Name.ToLowerInvariant()))
            score += 40;
        if (IsSubsequence(query, c.Hostname.ToLowerInvariant()))
            score += 30;
    }

    return score;
}

private static bool IsSubsequence(string query, string target)
{
    int qi = 0;
    for (int ti = 0; ti < target.Length && qi < query.Length; ti++)
    {
        if (query[qi] == target[ti])
            qi++;
    }
    return qi == query.Length;
}
```

This approach avoids external dependencies and provides: (1) exact substring matching with priority weighting, (2) subsequence matching for fuzzy "typing initials" style queries (e.g., "psrv" matches "prod-server-01"), (3) results sorted by relevance. [ASSUMED -- simple approach sufficient for scale]

### Anti-Patterns to Avoid

- **UseWindowsForms in Directory.Build.props:** Causes System.Windows.Application type ambiguity. Set ONLY in Deskbridge.Protocols.Rdp.csproj. [VERIFIED: PITFALLS.md Pitfall 13]
- **CommunityToolkit.Mvvm 8.4.0:** Fails to compile on .NET 10 / C# 14. Must use 8.4.2+. [VERIFIED: PITFALLS.md Pitfall 4]
- **Enabling .NET built-in fluent theme:** Setting `ThemeMode` on Application/Window breaks WindowsFormsHost rendering. WPF-UI has its OWN theme system. Do not confuse them. [VERIFIED: PITFALLS.md Pitfall 3]
- **Missing InitializeComponent() in custom Main:** WPF-UI theme resources fail to load silently. [VERIFIED: PITFALLS.md Pitfall 10]
- **Async Main method:** Silently changes thread to MTA, crashes ActiveX controls. Main MUST be synchronous with [STAThread]. [VERIFIED: PITFALLS.md Pitfall 11]
- **Message type inheritance in event bus:** WeakReferenceMessenger does NOT deliver to base type subscribers. Use flat event types. [VERIFIED: Microsoft Learn Messenger docs]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| MVVM boilerplate | Property changed, command implementations | CommunityToolkit.Mvvm source generators | [ObservableProperty], [RelayCommand] eliminate hundreds of lines |
| Event bus | Custom pub/sub with dictionaries | WeakReferenceMessenger wrapper | Handles weak references, GC cleanup, thread safety |
| DI container | Service locator or manual wiring | Microsoft.Extensions.DependencyInjection | Standard, tested, familiar to all .NET developers |
| WPF Fluent theme | Custom styles from scratch | WPF-UI ThemesDictionary + ControlsDictionary | Auto-restyled standard controls, Mica backdrop, snap layouts |
| Central version management | Manual version sync across csprojs | Directory.Packages.props (CPM) | Built into NuGet, single source of truth for versions |

**Key insight:** Phase 1 is about establishing the backbone correctly. Every pattern hand-rolled here will be used by every subsequent phase. Getting DI, event bus, and pipeline right matters more than getting them fast.

## Common Pitfalls

### Pitfall 1: CommunityToolkit.Mvvm 8.4.0 Build Failure
**What goes wrong:** Source generators emit C# 14 code but use Roslyn 4.x which doesn't support C# 14.
**Why it happens:** CommunityToolkit.Mvvm 8.4.0 shipped before Roslyn 5.0. Fixed in 8.4.1 (Mar 18, 2026).
**How to avoid:** Pin to `8.4.*` (picks up 8.4.2+). Verify build succeeds with `[ObservableProperty]` on a partial property.
**Warning signs:** Build errors MVVMTK0041, CS9248, CS8050.
[VERIFIED: PITFALLS.md Pitfall 4, CommunityToolkit/dotnet#1139]

### Pitfall 2: .NET Built-in Fluent Theme Breaks WindowsFormsHost
**What goes wrong:** If the .NET built-in fluent theme is enabled (ThemeMode = Light/Dark on Application), WindowsFormsHost renders as black rectangles.
**Why it happens:** .NET 9+ built-in fluent theme modifies DwmExtendFrameIntoClientArea which interferes with HWND-based rendering.
**How to avoid:** Never set ThemeMode. Use WPF-UI's ThemesDictionary and ApplicationThemeManager.Apply() instead. Add safety net in csproj:
```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop" Value="true" />
</ItemGroup>
```
**Warning signs:** RDP viewport renders as black/garbled rectangle.
[VERIFIED: PITFALLS.md Pitfall 3, dotnet/wpf#10044]

### Pitfall 3: Velopack Entry Point Resource Loading
**What goes wrong:** App.xaml resources not loaded when using custom Main(). All styles resolve to null.
**Why it happens:** When App.xaml is Page (not ApplicationDefinition), WPF does not auto-generate Main() that calls InitializeComponent().
**How to avoid:** Always call `app.InitializeComponent()` after `new App()` and before `app.Run()`. Set `<StartupObject>` in csproj.
**Warning signs:** Application launches with default Windows styling instead of dark Fluent theme.
[VERIFIED: PITFALLS.md Pitfall 10]

### Pitfall 4: WeakReferenceMessenger Handlers Run on Publisher's Thread
**What goes wrong:** If a background thread publishes an event, handlers updating UI will throw cross-thread exceptions.
**Why it happens:** `Send()` invokes handlers synchronously on the calling thread.
**How to avoid:** Either (a) ensure all event publishing happens on the UI thread, or (b) have UI-bound handlers marshal to dispatcher. Document the contract.
**Warning signs:** InvalidOperationException about cross-thread access in event handlers.
[VERIFIED: PITFALLS.md Pitfall 16, Microsoft Learn Messenger docs]

### Pitfall 5: Directory.Packages.props Version Pinning Format
**What goes wrong:** Using incorrect version range syntax. `4.2.*` is a floating version (auto-picks latest patch). `[4.2.0, 4.3.0)` is a range. `4.2.0` is exact.
**Why it happens:** NuGet version syntax has multiple formats.
**How to avoid:** Use `Version="4.2.*"` for major.minor pinning per D-07. Use exact pin for Velopack (`0.0.1298`).
**Warning signs:** Unexpected version resolution or restore failures.
[VERIFIED: Microsoft Learn Central Package Management docs]

## Code Examples

### Directory.Build.props (shared build configuration)
```xml
<!-- Source: REFERENCE.md Required Configuration Files -->
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
</Project>
```

Note: No `<LangVersion>` needed -- .NET 10 defaults to C# 14. No `<UseWindowsForms>` -- that goes only in Protocols.Rdp csproj.
[VERIFIED: REFERENCE.md, STACK.md]

### App.manifest
```xml
<!-- Source: REFERENCE.md Required Configuration Files -->
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
[VERIFIED: REFERENCE.md]

### GlobalUsings.cs per project

**Deskbridge (WPF app):**
```csharp
global using System.Windows;
global using System.Windows.Controls;
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;
global using Microsoft.Extensions.DependencyInjection;
global using Deskbridge.Core.Interfaces;
global using Deskbridge.Core.Models;
global using Deskbridge.Core.Events;
```

**Deskbridge.Core:**
```csharp
global using System.Collections.ObjectModel;
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Messaging;
```

**Deskbridge.Protocols.Rdp:**
```csharp
global using System.Windows.Forms.Integration;
global using Deskbridge.Core.Interfaces;
global using Deskbridge.Core.Models;
```

**Deskbridge.Tests:**
```csharp
global using Xunit;
global using FluentAssertions;
global using NSubstitute;
global using Deskbridge.Core.Interfaces;
global using Deskbridge.Core.Models;
global using Deskbridge.Core.Events;
global using Deskbridge.Core.Services;
global using Deskbridge.Core.Pipeline;
```

[ASSUMED -- contents based on project domain, per Claude's discretion]

### xUnit v3 Test Pattern with FluentAssertions + NSubstitute
```csharp
// Source: xunit.net v3 docs + FluentAssertions + NSubstitute patterns
public class EventBusTests
{
    [Fact]
    public void Publish_InvokesSubscribedHandler()
    {
        // Arrange
        var bus = new EventBus();
        ConnectionRequestedEvent? received = null;
        var recipient = new object();

        bus.Subscribe<ConnectionRequestedEvent>(recipient, e => received = e);

        var connection = new ConnectionModel { Name = "Test" };
        var evt = new ConnectionRequestedEvent(connection);

        // Act
        bus.Publish(evt);

        // Assert
        received.Should().NotBeNull();
        received!.Connection.Name.Should().Be("Test");
    }

    [Fact]
    public void Unsubscribe_StopsReceivingEvents()
    {
        var bus = new EventBus();
        int callCount = 0;
        var recipient = new object();

        bus.Subscribe<ConnectionRequestedEvent>(recipient, _ => callCount++);
        bus.Publish(new ConnectionRequestedEvent(new ConnectionModel()));

        bus.Unsubscribe<ConnectionRequestedEvent>(recipient);
        bus.Publish(new ConnectionRequestedEvent(new ConnectionModel()));

        callCount.Should().Be(1);
    }
}

public class ConnectionPipelineTests
{
    [Fact]
    public async Task Pipeline_ExecutesStagesInOrder()
    {
        var executionOrder = new List<int>();
        var pipeline = new ConnectionPipeline();

        pipeline.AddStage(CreateStage("Second", 200, executionOrder));
        pipeline.AddStage(CreateStage("First", 100, executionOrder));
        pipeline.AddStage(CreateStage("Third", 300, executionOrder));

        await pipeline.ConnectAsync(new ConnectionModel());

        executionOrder.Should().ContainInOrder(100, 200, 300);
    }

    [Fact]
    public async Task Pipeline_AbortsOnFailure()
    {
        var executionOrder = new List<int>();
        var pipeline = new ConnectionPipeline();

        pipeline.AddStage(CreateStage("First", 100, executionOrder));
        pipeline.AddStage(CreateFailingStage("Fails", 200));
        pipeline.AddStage(CreateStage("Never", 300, executionOrder));

        var result = await pipeline.ConnectAsync(new ConnectionModel());

        result.Success.Should().BeFalse();
        executionOrder.Should().ContainSingle().Which.Should().Be(100);
    }

    private static IConnectionPipelineStage CreateStage(string name, int order, List<int> tracker)
    {
        var stage = Substitute.For<IConnectionPipelineStage>();
        stage.Name.Returns(name);
        stage.Order.Returns(order);
        stage.ExecuteAsync(Arg.Any<ConnectionContext>())
            .Returns(ci =>
            {
                tracker.Add(order);
                return Task.FromResult(new PipelineResult(true));
            });
        return stage;
    }

    private static IConnectionPipelineStage CreateFailingStage(string name, int order)
    {
        var stage = Substitute.For<IConnectionPipelineStage>();
        stage.Name.Returns(name);
        stage.Order.Returns(order);
        stage.ExecuteAsync(Arg.Any<ConnectionContext>())
            .Returns(Task.FromResult(new PipelineResult(false, "Simulated failure")));
        return stage;
    }
}

public class ConnectionQueryTests
{
    [Theory]
    [InlineData("prod", "prod-server-01")]
    [InlineData("192.168", "192.168.1.100")]
    [InlineData("web", "web-server")]
    public void Search_FindsSubstringMatches(string query, string expectedName)
    {
        var connections = new List<ConnectionModel>
        {
            new() { Name = "prod-server-01", Hostname = "10.0.0.1" },
            new() { Name = "dev-server-01", Hostname = "192.168.1.100" },
            new() { Name = "web-server", Hostname = "10.0.0.5", Tags = ["web", "prod"] }
        };
        var queryService = new ConnectionQueryService(connections);

        var results = queryService.Search(query);

        results.Should().Contain(c => c.Name == expectedName || c.Hostname.Contains(query));
    }
}

public class DiCompositionTests
{
    [Fact]
    public void AllServices_ResolveWithoutThrowing()
    {
        var services = new ServiceCollection();
        // Register all services as App.xaml.cs does
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IConnectionPipeline, ConnectionPipeline>();
        services.AddSingleton<IDisconnectPipeline, DisconnectPipeline>();
        services.AddSingleton<IConnectionQuery, ConnectionQueryService>();

        var provider = services.BuildServiceProvider();

        // Verify each service resolves
        provider.GetRequiredService<IEventBus>().Should().NotBeNull();
        provider.GetRequiredService<INotificationService>().Should().NotBeNull();
        provider.GetRequiredService<IConnectionPipeline>().Should().NotBeNull();
        provider.GetRequiredService<IDisconnectPipeline>().Should().NotBeNull();
        provider.GetRequiredService<IConnectionQuery>().Should().NotBeNull();
    }
}
```

[VERIFIED: xunit v3 API, FluentAssertions API, NSubstitute API -- all from official NuGet/docs]

### .editorconfig (key sections)

```ini
# Source: Microsoft .NET coding conventions + RehanSaeed/EditorConfig template
root = true

[*]
charset = utf-8
indent_style = space
indent_size = 4
end_of_line = crlf
trim_trailing_whitespace = true
insert_final_newline = true

[*.{csproj,props,targets,xml,xaml}]
indent_size = 2

[*.json]
indent_size = 2

[*.cs]
# Organize usings
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false

# this. preferences
dotnet_style_qualification_for_field = false:warning
dotnet_style_qualification_for_property = false:warning
dotnet_style_qualification_for_method = false:warning
dotnet_style_qualification_for_event = false:warning

# Type keywords vs framework type names
dotnet_style_predefined_type_for_locals_parameters_members = true:warning
dotnet_style_predefined_type_for_member_access = true:warning

# var preferences
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion

# Expression-bodied members
csharp_style_expression_bodied_methods = when_on_single_line:suggestion
csharp_style_expression_bodied_constructors = false:suggestion
csharp_style_expression_bodied_properties = true:suggestion
csharp_style_expression_bodied_accessors = true:suggestion

# Pattern matching
csharp_style_pattern_matching_over_is_with_cast_check = true:warning
csharp_style_pattern_matching_over_as_with_null_check = true:warning

# Null checking
csharp_style_throw_expression = true:suggestion
csharp_style_conditional_delegate_call = true:warning

# Namespace declarations
csharp_style_namespace_declarations = file_scoped:warning

# Nullable reference types -- ERRORS per D-04
dotnet_diagnostic.CS8600.severity = error
dotnet_diagnostic.CS8601.severity = error
dotnet_diagnostic.CS8602.severity = error
dotnet_diagnostic.CS8603.severity = error
dotnet_diagnostic.CS8604.severity = error
dotnet_diagnostic.CS8618.severity = error
dotnet_diagnostic.CS8625.severity = error

# Code analysis rules
dotnet_diagnostic.CA1050.severity = warning  # Declare types in namespaces
dotnet_diagnostic.CA1051.severity = warning  # Do not declare visible instance fields
dotnet_diagnostic.CA1062.severity = none     # Validate arguments of public methods (redundant with nullable)
dotnet_diagnostic.CA1303.severity = none     # Do not pass literals as localized parameters
dotnet_diagnostic.CA1812.severity = warning  # Avoid uninstantiated internal classes
dotnet_diagnostic.CA1822.severity = suggestion  # Mark members as static
dotnet_diagnostic.CA1848.severity = suggestion  # Use LoggerMessage delegates
dotnet_diagnostic.CA2007.severity = none     # ConfigureAwait (not applicable for WPF)
dotnet_diagnostic.CA2211.severity = warning  # Non-constant fields should not be visible

# Naming conventions
dotnet_naming_rule.private_fields_should_be_camel_case.severity = warning
dotnet_naming_rule.private_fields_should_be_camel_case.symbols = private_fields
dotnet_naming_rule.private_fields_should_be_camel_case.style = camel_case_underscore_prefix

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private

dotnet_naming_style.camel_case_underscore_prefix.required_prefix = _
dotnet_naming_style.camel_case_underscore_prefix.capitalization = camel_case

dotnet_naming_rule.interfaces_should_begin_with_i.severity = warning
dotnet_naming_rule.interfaces_should_begin_with_i.symbols = interfaces
dotnet_naming_rule.interfaces_should_begin_with_i.style = begins_with_i

dotnet_naming_symbols.interfaces.applicable_kinds = interface
dotnet_naming_symbols.interfaces.applicable_accessibilities = *

dotnet_naming_style.begins_with_i.required_prefix = I
dotnet_naming_style.begins_with_i.capitalization = pascal_case

dotnet_naming_rule.async_methods_should_end_with_async.severity = warning
dotnet_naming_rule.async_methods_should_end_with_async.symbols = async_methods
dotnet_naming_rule.async_methods_should_end_with_async.style = ends_with_async

dotnet_naming_symbols.async_methods.applicable_kinds = method
dotnet_naming_symbols.async_methods.required_modifiers = async

dotnet_naming_style.ends_with_async.required_suffix = Async
dotnet_naming_style.ends_with_async.capitalization = pascal_case
```

[VERIFIED: Microsoft Learn .NET code style docs for syntax; severity levels per D-04 are Claude's discretion]

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| xUnit v2 (2.9.3) | xUnit v3 (3.2.2) | v3 stable Jan 2026 | v2 deprecated, security-only; v3 has better async, parallelism |
| CommunityToolkit.Mvvm 8.4.0 | 8.4.2 | Mar 2026 | 8.4.0 fails on .NET 10; Roslyn 5.0 fix in 8.4.1+ |
| `<LangVersion>preview</LangVersion>` | Not needed | .NET 10 GA Nov 2025 | C# 14 is default language version for net10.0 |
| FuzzySharp 2.0.2 (2020) | Raffinert.FuzzySharp 4.0.0 (Feb 2026) | Feb 2026 | Bit-parallel accelerated, 46x faster; but still unnecessary for this scale |
| PackageReference with Version | Central Package Management | Built-in since .NET 6 | Single file for all version pins |

**Deprecated/outdated:**
- xUnit v2: All future work on v3 only. Use `xunit.v3` package. [VERIFIED: NuGet]
- `CredentialManagement` NuGet: Targets .NET Framework 3.5 only. [VERIFIED: STACK.md]
- `SecureString`: Deprecated by Microsoft (DE0001). [VERIFIED: STACK.md]
- WPF-UI 3.x: Major API changes in 4.x. Different namespace structure. [VERIFIED: STACK.md]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Microsoft.NET.Test.Sdk 17.* is the correct test host for xUnit v3 on .NET 10 | Standard Stack / Testing | Test project won't run; easily fixed by checking xunit v3 docs |
| A2 | EventBus should NOT auto-marshal to UI thread | Pattern 3 | UI handlers crash with cross-thread exception; fix is to add Dispatcher.InvokeAsync in wrapper |
| A3 | Simple substring + subsequence matching is sufficient for fuzzy search at this scale | Pattern 5 | Inaccurate results for typo-heavy queries; upgrade to Levenshtein or FuzzySharp later |
| A4 | GlobalUsings.cs contents per project are appropriate | Code Examples | Minor -- adjust as needed during implementation |
| A5 | .editorconfig CA rule severity levels are appropriate defaults | Code Examples | Build may produce unexpected warnings/errors; tune during first build |

## Open Questions (RESOLVED)

1. **xUnit v3 test host package**
   - What we know: xunit.v3 3.2.2 depends on xunit.v3.mtp-v1. v2 required Microsoft.NET.Test.Sdk.
   - RESOLVED: xUnit v3 ships its own test host via xunit.v3.mtp-v1. Try without Microsoft.NET.Test.Sdk first; add if `dotnet test` fails to discover tests.

2. **Velopack version pinning**
   - What we know: Velopack uses 0.0.XXXX versioning. Pre-release builds go up to 0.0.1535.
   - RESOLVED: Pin to 0.0.1298 (verified working via netstandard2.0 forward compatibility). Upgrade later if needed.

3. **FluentAssertions v8 breaking changes**
   - What we know: FluentAssertions 8.9.0 is latest. v7-to-v8 had significant API changes.
   - RESOLVED: Use FluentAssertions 8.x documentation exclusively. Core `Should().Be()` patterns remain stable.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | All projects | Yes | 10.0.201 | -- |
| NuGet (nuget.org) | Package restore | Yes | Registered | -- |
| git | Source control | Yes | (in use) | -- |
| dotnet CLI | Build/test | Yes | 10.0.201 | -- |

**Missing dependencies:** None. All required tooling is present.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3 3.2.* |
| Config file | None needed for xUnit v3 (convention-based) |
| Quick run command | `dotnet test tests/Deskbridge.Tests/ --filter "Category!=Integration" -v q` |
| Full suite command | `dotnet test tests/Deskbridge.Tests/ -v n` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PROJ-01 | Solution builds with all packages | smoke | `dotnet build Deskbridge.sln` | N/A (build verification) |
| PROJ-02 | Shared build config applied | smoke | `dotnet build Deskbridge.sln` | N/A (build verification) |
| PROJ-03 | App manifest present | unit | Check file exists in test | Wave 0 |
| PROJ-04 | Interop DLLs positioned | smoke | Verify files exist at expected path | Wave 0 |
| PROJ-05 | Velopack entry point compiles | smoke | `dotnet build src/Deskbridge/` | N/A (build verification) |
| CORE-01 | DI resolves all services | unit | `dotnet test --filter DiCompositionTests` | Wave 0 |
| CORE-02 | Event bus pub/sub + weak ref cleanup | unit | `dotnet test --filter EventBusTests` | Wave 0 |
| CORE-03 | Pipeline stage ordering + failure abort | unit | `dotnet test --filter ConnectionPipelineTests` | Wave 0 |
| CORE-04 | Notification service raises events | unit | `dotnet test --filter NotificationServiceTests` | Wave 0 |
| CORE-05 | Fuzzy search on name/hostname/tags | unit | `dotnet test --filter ConnectionQueryTests` | Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet build Deskbridge.sln && dotnet test tests/Deskbridge.Tests/ -v q`
- **Per wave merge:** `dotnet test tests/Deskbridge.Tests/ -v n`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` -- test project with xunit.v3, FluentAssertions, NSubstitute references
- [ ] `tests/Deskbridge.Tests/GlobalUsings.cs` -- shared using statements for test project
- [ ] `tests/Deskbridge.Tests/EventBusTests.cs` -- covers CORE-02
- [ ] `tests/Deskbridge.Tests/ConnectionPipelineTests.cs` -- covers CORE-03
- [ ] `tests/Deskbridge.Tests/DisconnectPipelineTests.cs` -- covers CORE-03 (disconnect)
- [ ] `tests/Deskbridge.Tests/ConnectionQueryTests.cs` -- covers CORE-05
- [ ] `tests/Deskbridge.Tests/NotificationServiceTests.cs` -- covers CORE-04
- [ ] `tests/Deskbridge.Tests/DiCompositionTests.cs` -- covers CORE-01

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No (Phase 1 scope) | Deferred to Phase 6 (SEC-01 through SEC-05) |
| V3 Session Management | No (Phase 1 scope) | N/A for desktop app |
| V4 Access Control | No (Phase 1 scope) | Deferred to later phase |
| V5 Input Validation | Minimal | Nullable reference types enabled; ConnectionModel validation deferred |
| V6 Cryptography | No (Phase 1 scope) | No credential handling in Phase 1 |

### Known Threat Patterns for Phase 1

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Credential leakage in logs | Information Disclosure | IEventBus contract: never include passwords in event payloads. ConnectionContext.ResolvedPassword must not serialize to logs. |
| User data in app directory | Information Disclosure | All paths use %AppData%/Deskbridge/, never AppDomain.BaseDirectory (Velopack overwrites) |

Phase 1 has minimal security surface area. The key security decisions are: (1) establishing the %AppData% path convention that prevents data loss during auto-updates, and (2) ensuring the event/model types never carry raw credentials in serializable form.

## Sources

### Primary (HIGH confidence)
- REFERENCE.md -- Project structure, DI registrations, pipeline interfaces, disposal patterns, configuration files [read in full]
- DESIGN.md -- WPF-UI App.xaml bootstrap, dark theme configuration, FluentWindow pattern [read in full]
- .planning/research/STACK.md -- All package versions validated for .NET 10 compatibility [read in full]
- .planning/research/ARCHITECTURE.md -- Component boundaries, data flow, disconnect pipeline recommendation [read in full]
- .planning/research/PITFALLS.md -- 18 domain pitfalls catalogued with phase assignments [read in full]
- [Microsoft Learn: Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) -- CPM format, rules, overrides
- [Microsoft Learn: CommunityToolkit.Mvvm Messenger](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/messenger) -- WeakReferenceMessenger API, threading behavior, registration patterns
- [NuGet: xunit.v3 3.2.2](https://www.nuget.org/packages/xunit.v3) -- Published Jan 14, 2026; 19M+ downloads
- [NuGet: FluentAssertions 8.9.0](https://www.nuget.org/packages/fluentassertions/) -- Published Mar 16, 2026; targets .NET 6+
- [NuGet: NSubstitute 5.3.0](https://www.nuget.org/packages/NSubstitute) -- Published Oct 28, 2024; targets .NET 6+

### Secondary (MEDIUM confidence)
- [Microsoft Learn: .NET code style rule options](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/code-style-rule-options) -- .editorconfig rule syntax and severity levels
- [Microsoft Learn: WPF config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/wpf) -- DisableFluentThemeWindowBackdrop runtime switch
- [GitHub: RehanSaeed/EditorConfig](https://github.com/RehanSaeed/EditorConfig) -- Comprehensive .editorconfig template reference
- [GitHub: dotnet/wpf#10044](https://github.com/dotnet/wpf/issues/10044) -- WindowsFormsHost rendering broken with fluent themes
- [antondevtips.com: How to Start a New .NET Project in 2026](https://antondevtips.com/blog/how-to-start-a-new-dotnet-project-in-2026) -- Modern project setup guidance

### Tertiary (LOW confidence)
- [Raffinert.FuzzySharp 4.0.0](https://www.nuget.org/packages/Raffinert.FuzzySharp) -- Considered but not recommended (external dep for trivial scale)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all versions verified against NuGet registry and prior STACK.md research
- Architecture: HIGH -- patterns from REFERENCE.md validated in ARCHITECTURE.md research
- Pitfalls: HIGH -- 18 pitfalls catalogued in PITFALLS.md with cross-references
- Testing: MEDIUM -- xUnit v3 package name and test host interaction needs verification at scaffold time
- .editorconfig: MEDIUM -- rule severities are reasonable defaults but may need tuning

**Research date:** 2026-04-11
**Valid until:** 2026-05-11 (30 days -- stable ecosystem, no fast-moving dependencies)
