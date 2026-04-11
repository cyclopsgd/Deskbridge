---
phase: 01-foundation
plan: 01
subsystem: infra
tags: [dotnet-10, wpf-ui, velopack, central-package-management, editorconfig, fluent-window, mica]

# Dependency graph
requires: []
provides:
  - "Compilable 4-project solution targeting net10.0-windows"
  - "Central Package Management with floating version pins"
  - "Velopack custom Main entry point with STAThread"
  - "WPF-UI dark theme bootstrap (ThemesDictionary + ApplicationThemeManager)"
  - "FluentWindow shell with Mica backdrop and TitleBar"
  - "DI composition root in App.xaml.cs"
  - "Interop DLLs positioned at src/Deskbridge.Protocols.Rdp/Interop/"
  - "App manifest with Windows 10/11 + PerMonitorV2 DPI"
  - "Code style enforcement via .editorconfig with nullable warnings as errors"
affects: [01-02, 01-03, 02-ui-shell, 03-connections, 04-rdp, 05-tabs, 06-cross-cutting, 07-release]

# Tech tracking
tech-stack:
  added: [WPF-UI 4.2.x, CommunityToolkit.Mvvm 8.4.x, Microsoft.Extensions.DependencyInjection 10.0.x, Velopack 0.0.1298, Serilog 4.3.x, Serilog.Sinks.File 7.0.x, xunit.v3 3.2.x, FluentAssertions 8.9.x, NSubstitute 5.3.x]
  patterns: [Central Package Management, Velopack custom Main, WPF-UI dark theme bootstrap, FluentWindow + TitleBar, DI composition root, ObservableProperty partial property]

key-files:
  created:
    - Deskbridge.sln
    - Directory.Build.props
    - Directory.Packages.props
    - .editorconfig
    - LICENSE
    - src/Deskbridge/Deskbridge.csproj
    - src/Deskbridge/Program.cs
    - src/Deskbridge/App.xaml
    - src/Deskbridge/App.xaml.cs
    - src/Deskbridge/MainWindow.xaml
    - src/Deskbridge/MainWindow.xaml.cs
    - src/Deskbridge/ViewModels/MainWindowViewModel.cs
    - src/Deskbridge/app.manifest
    - src/Deskbridge/GlobalUsings.cs
    - src/Deskbridge.Core/Deskbridge.Core.csproj
    - src/Deskbridge.Core/GlobalUsings.cs
    - src/Deskbridge.Protocols.Rdp/Deskbridge.Protocols.Rdp.csproj
    - src/Deskbridge.Protocols.Rdp/GlobalUsings.cs
    - tests/Deskbridge.Tests/Deskbridge.Tests.csproj
    - tests/Deskbridge.Tests/GlobalUsings.cs
  modified:
    - .gitignore

key-decisions:
  - "Enabled CentralPackageFloatingVersionsEnabled for major.minor wildcard pins (D-07)"
  - "Stripped forward-looking GlobalUsings to existing namespaces only (Deskbridge.Core.* deferred to Plan 02)"
  - "Used .NET 10 default sln format (classic .sln, not .slnx) for broad tooling compatibility"
  - "DisableFluentThemeWindowBackdrop runtime config option added to prevent .NET built-in theme conflicts"

patterns-established:
  - "Central Package Management: all versions in Directory.Packages.props, csproj uses PackageReference without Version"
  - "Velopack entry: synchronous STAThread Main, VelopackApp.Build().Run() first, then new App() + InitializeComponent() + Run()"
  - "WPF-UI bootstrap: ThemesDictionary Dark + ControlsDictionary in App.xaml, ApplicationThemeManager.Apply in OnStartup"
  - "FluentWindow: ExtendsContentIntoTitleBar=True, WindowBackdropType=Mica, WindowCornerPreference=Round"
  - "DI composition root: ServiceCollection in App.xaml.cs OnStartup, views registered as Transient"
  - "ViewModel pattern: partial class + ObservableObject + [ObservableProperty] on partial properties (C# 14)"
  - "UseWindowsForms only in Protocols.Rdp csproj, never in Directory.Build.props"

requirements-completed: [PROJ-01, PROJ-02, PROJ-03, PROJ-04, PROJ-05, CORE-01]

# Metrics
duration: 9min
completed: 2026-04-11
---

# Phase 01 Plan 01: Solution Scaffold Summary

**Net10.0-windows solution with 4 projects, Central Package Management, Velopack entry point, WPF-UI dark FluentWindow with Mica backdrop, and full code style enforcement**

## Performance

- **Duration:** 9 min
- **Started:** 2026-04-11T13:16:33Z
- **Completed:** 2026-04-11T13:25:47Z
- **Tasks:** 2
- **Files modified:** 21

## Accomplishments
- 4-project solution (Deskbridge, Core, Protocols.Rdp, Tests) all building on net10.0-windows with zero errors and zero warnings
- Central Package Management with floating version pins and CentralPackageFloatingVersionsEnabled
- Velopack custom Main with STAThread, WPF-UI dark theme with Mica backdrop FluentWindow
- Full code style enforcement: .editorconfig with nullable warnings as errors, TreatWarningsAsErrors, EnforceCodeStyleInBuild
- Interop DLLs (MSTSCLib.dll, AxMSTSCLib.dll) positioned at src/Deskbridge.Protocols.Rdp/Interop/
- App manifest with Windows 10/11 support and PerMonitorV2 DPI awareness

## Task Commits

Each task was committed atomically:

1. **Task 1: Create solution structure, build config, and all project files** - `bb33e2c` (feat)
2. **Task 2: Create Velopack entry point, WPF-UI dark theme bootstrap, MainWindow, and DI skeleton** - `5d1510a` (feat)

## Files Created/Modified
- `Deskbridge.sln` - Solution file referencing all 4 projects
- `Directory.Build.props` - Shared build config: net10.0-windows, UseWPF, Nullable, TreatWarningsAsErrors
- `Directory.Packages.props` - Central Package Management with all version pins
- `.editorconfig` - C# conventions, nullable warnings as errors (CS8600-CS8625), naming rules
- `.gitignore` - Updated to exclude bin/obj/.vs/.gsd but track .planning/
- `LICENSE` - MIT license, 2026 Deskbridge Contributors
- `src/Deskbridge/Deskbridge.csproj` - WPF app with Velopack custom Main, WPF-UI, DisableFluentThemeWindowBackdrop
- `src/Deskbridge/Program.cs` - Velopack entry point with [STAThread] synchronous Main
- `src/Deskbridge/App.xaml` - WPF-UI dark theme bootstrap (ThemesDictionary + ControlsDictionary)
- `src/Deskbridge/App.xaml.cs` - DI composition root, ApplicationThemeManager.Apply Dark/Mica
- `src/Deskbridge/MainWindow.xaml` - FluentWindow with Mica backdrop, 32px TitleBar
- `src/Deskbridge/MainWindow.xaml.cs` - FluentWindow code-behind with DI constructor
- `src/Deskbridge/ViewModels/MainWindowViewModel.cs` - ObservableObject with partial property Title
- `src/Deskbridge/app.manifest` - Windows 10/11 compat + PerMonitorV2 DPI awareness
- `src/Deskbridge/GlobalUsings.cs` - Common WPF + MVVM + DI namespaces
- `src/Deskbridge.Core/Deskbridge.Core.csproj` - Class library with CommunityToolkit.Mvvm
- `src/Deskbridge.Core/GlobalUsings.cs` - Collections + MVVM namespaces
- `src/Deskbridge.Protocols.Rdp/Deskbridge.Protocols.Rdp.csproj` - UseWindowsForms + interop DLL refs
- `src/Deskbridge.Protocols.Rdp/GlobalUsings.cs` - WindowsFormsHost integration namespace
- `src/Deskbridge.Protocols.Rdp/Interop/MSTSCLib.dll` - RDP COM interop assembly
- `src/Deskbridge.Protocols.Rdp/Interop/AxMSTSCLib.dll` - RDP ActiveX interop assembly
- `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` - xUnit v3 + FluentAssertions + NSubstitute
- `tests/Deskbridge.Tests/GlobalUsings.cs` - Test framework namespaces

## Decisions Made
- **CentralPackageFloatingVersionsEnabled:** Required for `Version="4.2.*"` style pins in Central Package Management. The property `<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>` must be explicitly set.
- **GlobalUsings trimmed to existing namespaces:** Forward-looking usings for `Deskbridge.Core.Interfaces`, `Deskbridge.Core.Models`, etc. were removed since those namespaces have no types yet. They will be added by Plan 02 when the types exist. Empty namespace files don't export namespaces in C#.
- **Classic .sln format:** .NET 10 defaults to `.slnx` (XML solution format). Used `--format sln` to create classic `.sln` for broad tooling compatibility.
- **DisableFluentThemeWindowBackdrop:** Added as RuntimeHostConfigurationOption to prevent .NET built-in fluent theme from interfering with WindowsFormsHost rendering (per PITFALLS.md).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] CentralPackageFloatingVersionsEnabled required for floating versions**
- **Found during:** Task 1 (dotnet restore verification)
- **Issue:** `dotnet restore` failed with NU1011 because Central Package Management does not allow floating versions (e.g., `4.2.*`) by default
- **Fix:** Added `<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>` to Directory.Packages.props
- **Files modified:** Directory.Packages.props
- **Verification:** dotnet restore succeeds
- **Committed in:** bb33e2c (Task 1 commit)

**2. [Rule 3 - Blocking] GlobalUsings referenced non-existent namespaces**
- **Found during:** Task 2 (dotnet build verification)
- **Issue:** GlobalUsings.cs in Deskbridge, Protocols.Rdp, and Tests referenced `Deskbridge.Core.Interfaces`, `Deskbridge.Core.Models`, etc. but those namespaces have no types yet. C# only exports namespaces that contain at least one public type, so empty namespace placeholder files do not resolve the imports.
- **Fix:** Trimmed GlobalUsings.cs in all three projects to only reference namespaces that currently exist. Forward references will be restored in Plan 02 when types are created.
- **Files modified:** src/Deskbridge/GlobalUsings.cs, src/Deskbridge.Protocols.Rdp/GlobalUsings.cs, tests/Deskbridge.Tests/GlobalUsings.cs
- **Verification:** dotnet build succeeds with zero errors
- **Committed in:** 5d1510a (Task 2 commit)

**3. [Rule 3 - Blocking] Missing Velopack using directive**
- **Found during:** Task 2 (dotnet build verification)
- **Issue:** Program.cs used `VelopackApp.Build().Run()` but lacked `using Velopack;` directive
- **Fix:** Added `using Velopack;` at top of Program.cs
- **Files modified:** src/Deskbridge/Program.cs
- **Verification:** dotnet build succeeds
- **Committed in:** 5d1510a (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (3 blocking issues)
**Impact on plan:** All auto-fixes necessary for compilation. No scope creep.

## Issues Encountered
- `.NET 10 defaults to .slnx format` - `dotnet new sln` creates `.slnx` by default in .NET 10. Resolved by regenerating with classic `.sln` format.
- `Transient MSB3492 file lock warnings` - `dotnet clean` occasionally triggers MSB3492 warnings about file access that become errors under TreatWarningsAsErrors. These are transient and resolve on retry. Not a code issue.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Solution builds cleanly with all 4 projects. Ready for Plan 02 (Core services: event bus, pipeline, query interface).
- Interop DLLs are positioned for Plan 03 / Phase 04 RDP integration.
- GlobalUsings.cs files will need `Deskbridge.Core.*` namespaces added once Plan 02 creates the types.
- The empty Core directory structure (Interfaces/, Models/, Events/, Services/, Pipeline/) will be populated by Plan 02.

## Threat Mitigations Applied
- **T-01-01 (Package version pinning):** All packages pinned with major.minor range. Velopack exact-pinned to 0.0.1298.
- **T-01-03 (.gitignore):** Excludes .vs/, *.user, *.suo. Does NOT exclude .planning/.
- **T-01-04 (App manifest):** Standard user execution, PerMonitorV2 DPI awareness.

## Self-Check: PASSED

- All 23 key files verified present on disk
- Both task commits (bb33e2c, 5d1510a) verified in git log
- `dotnet build Deskbridge.sln` succeeds with 0 errors, 0 warnings

---
*Phase: 01-foundation*
*Completed: 2026-04-11*
