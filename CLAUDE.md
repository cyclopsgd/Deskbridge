## MANDATORY READING (before any code change)

These files are the technical authority for this project. Read them before editing code:

- **`REFERENCE.md`** — Architecture, constraints, feature spec, disposal sequences, DI registrations
- **`DESIGN.md`** — WPF-UI patterns, control usage, colour tokens, layout reference
- **`WPF-UI-PITFALLS.md`** — 8 categories of silent WPF-UI failures with correct patterns. **Read this before writing ANY WPF-UI code.** Covers ContentDialog hosting, TreeView context menus, FluentWindow conflicts, icon resolution, Color vs Brush resource keys, MVVM command generation, and theme override persistence.

Every WPF-UI bug we've hit in this project is documented in `WPF-UI-PITFALLS.md`. If you're about to use ContentDialog, ContextMenu, SymbolIcon, `*Color` resources, or accent theming — consult it first.

<!-- GSD:project-start source:PROJECT.md -->
## Project

**Deskbridge**

A modern Windows desktop RDP connection manager built to replace mRemoteNG. Designed for enterprise infrastructure teams who manage dozens to hundreds of remote connections daily. Tabbed multi-session management with a clean, compact dark UI (WPF-UI Fluent), proper COM resource cleanup, keyboard-first workflows, and auto-update from GitHub Releases.

**Core Value:** Reliable, flicker-free tabbed RDP sessions with proper ActiveX lifecycle management — if connections freeze, leak, or crash on close, nothing else matters.

### Constraints

- **Framework**: .NET 10 LTS (net10.0-windows) with C# 14 — no .NET 8/9
- **UI Library**: WPF-UI (Fluent dark theme) — all colours via DynamicResource tokens, BasedOn for style overrides
- **COM/ActiveX**: Classic aximp.exe interop only — no GeneratedComInterface, no Marshal.ReleaseComObject, site before configure
- **Airspace**: No WPF elements may overlap the RDP viewport (WinForms/ActiveX always renders on top)
- **UseWindowsForms**: Set ONLY in Deskbridge.Protocols.Rdp csproj — not in Directory.Build.props
- **Credentials**: AdysTech.CredentialManager only — no CredentialManagement NuGet, no SecureString
- **Velopack**: Custom Main method required (App.xaml as Page, not ApplicationDefinition), SemVer2, self-contained, user data in %AppData%
- **Sessions**: Practical limit ~15-20 simultaneous RDP sessions (GDI handles)
- **Serialisation**: System.Text.Json only — no XML config, no SQLite
- **Security**: Never store passwords in JSON or logs. Master password hash via PBKDF2.
<!-- GSD:project-end -->

<!-- GSD:stack-start source:research/STACK.md -->
## Technology Stack

## Recommended Stack
### Core Framework
| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| .NET 10 | 10.0.x (GA Nov 2025) | Runtime & SDK | LTS until Nov 2028. C# 14 with `field` keyword (GA, no longer preview). WPF ships in-box. Default `LangVersion` is C# 14 -- no manual override needed. | HIGH |
| WPF | (in-box with .NET 10) | UI framework | .NET 10 adds Fluent styles for more controls, clipboard API improvements, performance optimizations. No breaking changes for WindowsFormsHost/ActiveX interop. | HIGH |
| C# 14 | (default with .NET 10) | Language | Partial properties, `field` keyword, extension types. The `field` keyword is fully GA in C# 14 -- CommunityToolkit.Mvvm 8.4.2 generates code that uses it. | HIGH |
### UI Library
| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| WPF-UI | 4.2.0 | Fluent Design theming | Explicitly targets `net10.0-windows7.0`. Published Jan 10, 2026. Provides FluentWindow, Mica/Acrylic backdrops, auto-restyled standard controls, NavigationView, Snackbar, ContentDialog, InfoBar. Active maintenance by lepoco. | HIGH |
### MVVM
| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| CommunityToolkit.Mvvm | 8.4.2 | MVVM source generators | Published Mar 25, 2026. Upgraded to Roslyn 5.0 (in 8.4.1) for native C# 14 support. `[ObservableProperty]` on partial properties works without `<LangVersion>preview</LangVersion>`. Provides `[RelayCommand]`, `WeakReferenceMessenger`, `ObservableValidator`. | HIGH |
### Dependency Injection
| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Microsoft.Extensions.DependencyInjection | 10.0.x | DI container | Latest is 10.0.5. Ships aligned with .NET 10 SDK. Standard Microsoft DI -- no need for Autofac or similar. | HIGH |
### Credentials
| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| AdysTech.CredentialManager | 3.1.0 | Windows Credential Manager wrapper | Published Feb 27, 2026. Targets .NET 8 + .NET Standard 2.0. Wraps CredWrite/CredRead/CredEnumerate/CredDelete via P/Invoke. Removed BinaryFormatter fallback. Source Link enabled. | MEDIUM |
### Auto-Update
| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Velopack | 0.0.1298 | Auto-update + installer | Published Jun 7, 2025. Targets .NET 6+, .NET Standard 2.0, .NET Framework 4.6.2. Supports GitHub Releases via `GithubSource`. Cross-platform (Windows/macOS/Linux). | MEDIUM |
### Logging
| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Serilog | 4.3.1 | Structured logging core | Published Feb 10, 2026. Explicitly targets `net10.0` alongside .NET 6, 8, 9, .NET Standard 2.0, and .NET Framework 4.6.2. | HIGH |
| Serilog.Sinks.File | 7.0.0 | Rolling file sink | Published Apr 28, 2025. Targets .NET 6 + .NET Standard 2.0. Forward-compatible with .NET 10. | HIGH |
### Serialization
| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| System.Text.Json | (in-box with .NET 10) | JSON serialization | Ships with .NET 10. Source generators for AOT-friendly serialization. No external dependency needed. | HIGH |
### RDP ActiveX Interop
| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| AxMSTSCLib + MSTSCLib | Via aximp.exe | RDP ActiveX control interop | Pre-generated interop assemblies. Classic COM interop via AxHost. Must reference as assembly files, not NuGet packages. | HIGH |
## Supporting Libraries
| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| Microsoft.Extensions.Hosting | 10.0.x | Generic Host for DI/config | Optional. Use if you want `IHost` lifecycle management. Not required -- raw `ServiceCollection` is simpler for a desktop app. | HIGH |
| Microsoft.Extensions.Configuration.Json | 10.0.x | JSON config binding | Optional. Only if you want `IConfiguration` pattern for settings. `System.Text.Json` direct deserialization is simpler for this project. | HIGH |
| Velopack.Build | 0.0.1298 | Build-time packaging | Only in CI/CD pipeline. Not referenced by the app. Installed as `vpk` dotnet tool. | MEDIUM |
## Alternatives Considered
| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| UI Library | WPF-UI 4.2.0 | MahApps.Metro | MahApps last significant release was years ago. Less aligned with Windows 11 Fluent Design. WPF-UI provides Mica/Acrylic and auto-restyled controls. |
| UI Library | WPF-UI 4.2.0 | HandyControl | Chinese-origin library, less documentation in English, different design language from Windows 11 Fluent. |
| UI Library | WPF-UI 4.2.0 | iNKORE.UI.WPF.Modern | Newer alternative (v0.10.2.1 on NuGet). Less mature, lower adoption. WPF-UI has larger community and more production usage. |
| MVVM | CommunityToolkit.Mvvm | Prism | Heavier framework. Includes navigation, modules, regions -- overkill for this project. CommunityToolkit is leaner and Microsoft-maintained. |
| MVVM | CommunityToolkit.Mvvm | ReactiveUI | Reactive programming paradigm adds complexity without clear benefit for this use case. Higher learning curve. |
| Auto-update | Velopack | Squirrel.Windows | Velopack is the successor to Squirrel.Windows, built by the same author. Squirrel is unmaintained. |
| Auto-update | Velopack | ClickOnce | Poor UX, limited customization, tied to Visual Studio tooling. Velopack is more modern and flexible. |
| Logging | Serilog | NLog | Both are excellent. Serilog's structured logging and sink ecosystem is slightly larger. Either works. Serilog chosen for consistency with .NET ecosystem trends. |
| Logging | Serilog | Microsoft.Extensions.Logging only | MEL is an abstraction. Serilog as provider gives rolling file, structured output, and enrichment out of the box. |
| Credentials | AdysTech.CredentialManager | CredentialManagement NuGet | Targets .NET Framework 3.5 only. Not compatible with .NET 10. Do NOT use. |
| Credentials | AdysTech.CredentialManager | Raw P/Invoke | Possible but unnecessary duplication of effort. AdysTech wraps the same APIs cleanly. |
| Serialization | System.Text.Json | Newtonsoft.Json | In-box, no external dependency, better performance, source generator support for AOT. Newtonsoft is legacy for new projects. |
## Do NOT Use
| Package/Technology | Reason |
|---|---|
| `CredentialManagement` NuGet | Targets .NET Framework 3.5 only. Will not load on .NET 10. |
| `SecureString` | Deprecated by Microsoft (DE0001). Not encrypted in modern .NET. Use plain `string` for credential handling in memory, rely on Windows Credential Manager for storage. |
| `[GeneratedComInterface]` / COM source generators | Do not support ActiveX controls. The WinForms team is working on it (dotnet/winforms#10583) but it's not ready. Use classic aximp.exe interop. |
| `Marshal.ReleaseComObject` | Let `AxHost.Dispose()` handle COM release. Manual release causes access violations and use-after-free bugs. |
| CommunityToolkit.Mvvm 8.4.0 | Fails to compile on .NET 10 with C# 14 due to Roslyn 4.x. Use 8.4.2+. |
| `<LangVersion>preview</LangVersion>` | Not needed with .NET 10 + CommunityToolkit.Mvvm 8.4.2. C# 14 is the default. Adding this is harmless but misleading. |
| MahApps.Metro | Stale maintenance, not aligned with Windows 11 Fluent Design. |
| Squirrel.Windows | Unmaintained predecessor to Velopack. |
| WPF-UI 3.x | Major API changes in 4.x. Different namespace structure. Do not reference 3.x docs or samples. |
| BinaryFormatter | Removed in .NET 10. Affects clipboard/drag-drop operations. Use JSON serialization for clipboard data. |
## Package Reference Summary
### Directory.Build.props
### Deskbridge (main app) .csproj packages
### Deskbridge.Core .csproj packages
### Deskbridge.Protocols.Rdp .csproj
## .NET 10 Compatibility Matrix
| Package | Explicit .NET 10 Target? | Forward Compatible? | Verified Working? | Notes |
|---------|--------------------------|--------------------|--------------------|-------|
| WPF-UI 4.2.0 | YES (`net10.0-windows7.0`) | N/A | YES | First-class .NET 10 support |
| CommunityToolkit.Mvvm 8.4.2 | NO (targets `net8.0`) | YES (via netstandard2.0) | YES | Roslyn 5.0 source gen works with C# 14 |
| Microsoft.Extensions.DI 10.0.5 | YES | N/A | YES | Ships with .NET 10 |
| AdysTech.CredentialManager 3.1.0 | NO (targets `net8.0`) | YES (via netstandard2.0) | LIKELY | P/Invoke wrapper, runtime-stable |
| Velopack 0.0.1298 | NO (targets `net6.0`/`net8.0`/`net9.0`) | YES (via netstandard2.0) | LIKELY | .NET 10 parsing tests in pre-release |
| Serilog 4.3.1 | YES (`net10.0`) | N/A | YES | First-class .NET 10 support |
| Serilog.Sinks.File 7.0.0 | NO (targets `net6.0`) | YES (via netstandard2.0) | LIKELY | Stable file I/O, no breaking APIs |
| System.Text.Json | YES (in-box) | N/A | YES | Ships with .NET 10 |
| AxMSTSCLib / MSTSCLib | N/A (raw COM interop) | YES | YES | Classic COM interop unchanged in .NET 10 |
## Pre-Scaffold Verification Checklist
- [ ] `dotnet new wpf -n TestApp -f net10.0-windows` builds cleanly
- [ ] Adding WPF-UI 4.2.0 NuGet: FluentWindow renders with Mica backdrop
- [ ] Adding CommunityToolkit.Mvvm 8.4.2: `[ObservableProperty]` on partial property compiles without `<LangVersion>preview</LangVersion>`
- [ ] WindowsFormsHost + AxMSTSCLib reference: RDP control sites and connects
- [ ] `dotnet publish -c Release -r win-x64 --self-contained` produces working executable (watch for System.Drawing.Common issue)
- [ ] Velopack `VelopackApp.Build().Run()` in custom Main method does not crash on startup
## Sources
- [NuGet: WPF-UI 4.2.0](https://www.nuget.org/packages/wpf-ui/) -- Confirmed .NET 10 target
- [NuGet: CommunityToolkit.Mvvm 8.4.2](https://www.nuget.org/packages/CommunityToolkit.Mvvm) -- Published Mar 25, 2026
- [CommunityToolkit/dotnet#1139](https://github.com/CommunityToolkit/dotnet/issues/1139) -- .NET 10 compilation failure in 8.4.0
- [CommunityToolkit/dotnet Releases](https://github.com/CommunityToolkit/dotnet/releases) -- 8.4.1 adds Roslyn 5.0
- [NuGet: Velopack 0.0.1298](https://www.nuget.org/packages/Velopack/) -- Latest stable
- [NuGet: AdysTech.CredentialManager 3.1.0](https://www.nuget.org/packages/AdysTech.CredentialManager/) -- Published Feb 27, 2026
- [NuGet: Serilog 4.3.1](https://www.nuget.org/packages/serilog/) -- Explicit .NET 10 target
- [NuGet: Serilog.Sinks.File 7.0.0](https://www.nuget.org/packages/Serilog.Sinks.File/)
- [NuGet: Microsoft.Extensions.DependencyInjection 10.0.5](https://www.nuget.org/packages/microsoft.extensions.dependencyinjection)
- [dotnet/winforms#10583](https://github.com/dotnet/winforms/issues/10583) -- ActiveX COM source generators still in progress
- [dotnet/wpf#11261](https://github.com/dotnet/wpf/issues/11261) -- System.Drawing.Common publish issue in .NET 10
- [What's new in WPF for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) -- WPF .NET 10 enhancements
- [Announcing .NET 10](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/) -- GA release Nov 2025
- [C# 14 field keyword](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/field) -- GA in C# 14
- [Velopack GitHub](https://github.com/velopack/velopack) -- .NET 10 parsing tests in build 1440+
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

Conventions not yet established. Will populate as patterns emerge during development.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

Architecture not yet mapped. Follow existing patterns found in the codebase.
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->
## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, or `.github/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->



<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
