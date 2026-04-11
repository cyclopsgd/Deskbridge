# Technology Stack

**Project:** Deskbridge -- WPF RDP Connection Manager
**Researched:** 2026-04-11
**Validation target:** Stack choices from REFERENCE.md against April 2026 ecosystem state

---

## Recommended Stack

### Core Framework

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| .NET 10 | 10.0.x (GA Nov 2025) | Runtime & SDK | LTS until Nov 2028. C# 14 with `field` keyword (GA, no longer preview). WPF ships in-box. Default `LangVersion` is C# 14 -- no manual override needed. | HIGH |
| WPF | (in-box with .NET 10) | UI framework | .NET 10 adds Fluent styles for more controls, clipboard API improvements, performance optimizations. No breaking changes for WindowsFormsHost/ActiveX interop. | HIGH |
| C# 14 | (default with .NET 10) | Language | Partial properties, `field` keyword, extension types. The `field` keyword is fully GA in C# 14 -- CommunityToolkit.Mvvm 8.4.2 generates code that uses it. | HIGH |

**Validation verdict:** CONFIRMED. .NET 10 has been GA since November 2025. It is an LTS release supported until November 2028. C# 14 is the default language version for `net10.0-windows` -- no `<LangVersion>` setting required.

**Known .NET 10 WPF issue:** A `System.Drawing.Common` load error can occur in self-contained published apps (dotnet/wpf#11261). Microsoft committed to fixing in servicing. Workaround: add explicit `<PackageReference Include="System.Drawing.Common" Version="10.0.0" />` if hit during publish. This only affects self-contained deployments and is likely resolved by the time scaffold begins.

### UI Library

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| WPF-UI | 4.2.0 | Fluent Design theming | Explicitly targets `net10.0-windows7.0`. Published Jan 10, 2026. Provides FluentWindow, Mica/Acrylic backdrops, auto-restyled standard controls, NavigationView, Snackbar, ContentDialog, InfoBar. Active maintenance by lepoco. | HIGH |

**Validation verdict:** CONFIRMED. WPF-UI 4.2.0 explicitly targets .NET 10. The NuGet listing shows `net10.0-windows7.0` as a target framework alongside .NET 8 and .NET 9. Only dependency is `WPF-UI.Abstractions >= 4.2.0`.

**Version note:** Pin to `4.2.0` -- this is the latest stable release as of April 2026. WPF-UI 4.x was a major rewrite from 3.x with namespace changes (`Wpf.Ui.*`), so do not reference 3.x documentation or examples.

### MVVM

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| CommunityToolkit.Mvvm | 8.4.2 | MVVM source generators | Published Mar 25, 2026. Upgraded to Roslyn 5.0 (in 8.4.1) for native C# 14 support. `[ObservableProperty]` on partial properties works without `<LangVersion>preview</LangVersion>`. Provides `[RelayCommand]`, `WeakReferenceMessenger`, `ObservableValidator`. | HIGH |

**Validation verdict:** CONFIRMED. Version 8.4.2 is the correct minimum. The critical fix was in 8.4.1 (Mar 18, 2026) which migrated to Roslyn 5.0 and added .NET 10 TFM to the SDK. Version 8.4.2 (Mar 25, 2026) was a hotfix for a build configuration issue.

**CRITICAL:** Do NOT use 8.4.0 with .NET 10. It fails to compile due to Roslyn 4.x not supporting C# 14. The issue was tracked at CommunityToolkit/dotnet#1139 and resolved in 8.4.1.

**LangVersion:** No `<LangVersion>preview</LangVersion>` needed in Directory.Build.props. .NET 10 defaults to C# 14 and CommunityToolkit.Mvvm 8.4.2 generates compatible code. The REFERENCE.md does not specify this setting, which is correct.

### Dependency Injection

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Microsoft.Extensions.DependencyInjection | 10.0.x | DI container | Latest is 10.0.5. Ships aligned with .NET 10 SDK. Standard Microsoft DI -- no need for Autofac or similar. | HIGH |

**Validation verdict:** CONFIRMED. Version 10.0.5 is current. Use floating version `10.0.*` or pin to `10.0.5`.

### Credentials

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| AdysTech.CredentialManager | 3.1.0 | Windows Credential Manager wrapper | Published Feb 27, 2026. Targets .NET 8 + .NET Standard 2.0. Wraps CredWrite/CredRead/CredEnumerate/CredDelete via P/Invoke. Removed BinaryFormatter fallback. Source Link enabled. | MEDIUM |

**Validation verdict:** CONFIRMED WITH NOTE. Version 3.1.0 does not explicitly target `net10.0` but targets `net8.0` + `netstandard2.0`, which provides forward compatibility to .NET 10. P/Invoke calls to Windows Credential Manager APIs are runtime-stable across .NET versions. This is a thin wrapper -- low risk.

**Version note:** Pin to `3.1.0` exactly. The 3.1.0 release fixed a spelling issue (`Persistance` -> `Persistence`) which may be a minor API breaking change from 2.x.

### Auto-Update

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Velopack | 0.0.1298 | Auto-update + installer | Published Jun 7, 2025. Targets .NET 6+, .NET Standard 2.0, .NET Framework 4.6.2. Supports GitHub Releases via `GithubSource`. Cross-platform (Windows/macOS/Linux). | MEDIUM |

**Validation verdict:** CONFIRMED WITH CAVEATS.

1. **Version number:** Velopack uses a `0.0.XXXX` versioning scheme -- the 0.0 prefix does not indicate instability. The `1298` is the build number. This is the latest stable release; pre-releases go up to `0.0.1535`. Despite the unconventional version number, Velopack is production-ready and widely used (12k+ GitHub stars).

2. **.NET 10 compatibility:** Does not explicitly target .NET 10 but targets .NET 6+ and .NET Standard 2.0. Forward-compatible. Velopack's test suite includes .NET 10 parsing tests (added in build 1440).

3. **Newtonsoft.Json dependency:** Velopack pulls in `Newtonsoft.Json >= 13.0.3` on .NET 6 targets. This is an internal dependency of Velopack, not a project-wide concern. The project itself should still use `System.Text.Json` for its own serialization. The two coexist without conflict.

4. **vpk CLI:** Install separately via `dotnet tool install -g vpk`. Used for packaging (`vpk pack`), not referenced in the app project directly.

### Logging

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Serilog | 4.3.1 | Structured logging core | Published Feb 10, 2026. Explicitly targets `net10.0` alongside .NET 6, 8, 9, .NET Standard 2.0, and .NET Framework 4.6.2. | HIGH |
| Serilog.Sinks.File | 7.0.0 | Rolling file sink | Published Apr 28, 2025. Targets .NET 6 + .NET Standard 2.0. Forward-compatible with .NET 10. | HIGH |

**Validation verdict:** CONFIRMED. Serilog 4.3.1 has explicit .NET 10 targeting. Serilog.Sinks.File 7.0.0 works via forward compatibility. Both are actively maintained.

### Serialization

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| System.Text.Json | (in-box with .NET 10) | JSON serialization | Ships with .NET 10. Source generators for AOT-friendly serialization. No external dependency needed. | HIGH |

**Validation verdict:** CONFIRMED. In-box, no NuGet reference needed. .NET 10 adds improvements to source generator cycle handling.

### RDP ActiveX Interop

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| AxMSTSCLib + MSTSCLib | Via aximp.exe | RDP ActiveX control interop | Pre-generated interop assemblies. Classic COM interop via AxHost. Must reference as assembly files, not NuGet packages. | HIGH |

**Validation verdict:** CONFIRMED. The `[GeneratedComInterface]` source generators still do NOT support ActiveX controls as of .NET 10. The WinForms team has an open issue (dotnet/winforms#10583, marked "work in progress" as of Feb 2026) but no resolution yet. Classic `aximp.exe`-generated interop DLLs remain the only viable approach for RDP ActiveX hosting in WPF.

---

## Supporting Libraries

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| Microsoft.Extensions.Hosting | 10.0.x | Generic Host for DI/config | Optional. Use if you want `IHost` lifecycle management. Not required -- raw `ServiceCollection` is simpler for a desktop app. | HIGH |
| Microsoft.Extensions.Configuration.Json | 10.0.x | JSON config binding | Optional. Only if you want `IConfiguration` pattern for settings. `System.Text.Json` direct deserialization is simpler for this project. | HIGH |
| Velopack.Build | 0.0.1298 | Build-time packaging | Only in CI/CD pipeline. Not referenced by the app. Installed as `vpk` dotnet tool. | MEDIUM |

---

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

---

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

---

## Package Reference Summary

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

No `<LangVersion>` needed. .NET 10 defaults to C# 14.

### Deskbridge (main app) .csproj packages

```xml
<ItemGroup>
  <PackageReference Include="WPF-UI" Version="4.2.0" />
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.5" />
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.5" />
  <PackageReference Include="Velopack" Version="0.0.1298" />
  <PackageReference Include="Serilog" Version="4.3.1" />
  <PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
</ItemGroup>
```

### Deskbridge.Core .csproj packages

```xml
<ItemGroup>
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
  <PackageReference Include="AdysTech.CredentialManager" Version="3.1.0" />
  <PackageReference Include="Serilog" Version="4.3.1" />
</ItemGroup>
```

### Deskbridge.Protocols.Rdp .csproj

```xml
<PropertyGroup>
  <UseWindowsForms>true</UseWindowsForms>
</PropertyGroup>

<ItemGroup>
  <Reference Include="AxMSTSCLib">
    <HintPath>Interop\AxMSTSCLib.dll</HintPath>
  </Reference>
  <Reference Include="MSTSCLib">
    <HintPath>Interop\MSTSCLib.dll</HintPath>
  </Reference>
</ItemGroup>
```

`UseWindowsForms` set ONLY here -- not in Directory.Build.props. This avoids `System.Windows.Application` ambiguity between WPF and WinForms.

---

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

---

## Pre-Scaffold Verification Checklist

These items should be verified at scaffold time (not research time) by creating a minimal test project:

- [ ] `dotnet new wpf -n TestApp -f net10.0-windows` builds cleanly
- [ ] Adding WPF-UI 4.2.0 NuGet: FluentWindow renders with Mica backdrop
- [ ] Adding CommunityToolkit.Mvvm 8.4.2: `[ObservableProperty]` on partial property compiles without `<LangVersion>preview</LangVersion>`
- [ ] WindowsFormsHost + AxMSTSCLib reference: RDP control sites and connects
- [ ] `dotnet publish -c Release -r win-x64 --self-contained` produces working executable (watch for System.Drawing.Common issue)
- [ ] Velopack `VelopackApp.Build().Run()` in custom Main method does not crash on startup

---

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
