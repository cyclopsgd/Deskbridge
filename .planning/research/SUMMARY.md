# Project Research Summary

**Project:** Deskbridge -- WPF RDP Connection Manager
**Domain:** Windows desktop application with COM/ActiveX interop
**Researched:** 2026-04-11
**Confidence:** HIGH

## Executive Summary

Deskbridge is a WPF-based RDP connection manager targeting sysadmins migrating from mRemoteNG. The recommended stack (.NET 10 LTS, WPF-UI 4.2.0, CommunityToolkit.Mvvm 8.4.2, Velopack, Serilog) is fully validated. All core packages either explicitly target .NET 10 or are forward-compatible via .NET Standard 2.0. The REFERENCE.md architecture is sound -- three-project solution (app, core, protocol), MVVM with source generators, event bus via WeakReferenceMessenger, and an ordered connection pipeline -- matching patterns used by production RDP managers like Royal TS and Devolutions RDM.

The primary technical risk is the WindowsFormsHost/ActiveX interop layer. Six of the eighteen identified pitfalls (siting order, disposal leaks, fluent theme conflict, airspace violations, keyboard focus trapping, DPI mismatch) stem from hosting the RDP ActiveX control in WPF. These are well-documented with known mitigations, but every one must be implemented correctly from the start -- they cannot be retrofitted. The REFERENCE.md already specifies the correct disposal sequence, siting order, and airspace-safe layout. The critical new finding is Pitfall 3: the .NET 9/10 built-in fluent theme (ThemeMode) breaks WindowsFormsHost rendering entirely, which must be disabled explicitly while using WPF-UI separate theming system.

The feature landscape is well-scoped. Deskbridge v1 covers all table stakes (tabbed sessions, folder tree, credential inheritance, search, reconnection) and includes several differentiators absent from free competitors (command palette, silent auto-update, app lock, audit log, modern Fluent UI). The anti-features list is disciplined -- no multi-protocol in v1, no SQL backend, no session recording. Build order follows a strict dependency chain: scaffold, core services, WPF shell, connection management, RDP integration, tabs, then parallel work on palette/shortcuts/notifications/update/import.

## Key Findings

### Recommended Stack

All packages confirmed compatible with .NET 10 (GA since Nov 2025, LTS until Nov 2028). No LangVersion override needed -- C# 14 is the default. The only critical version pin is CommunityToolkit.Mvvm 8.4.2+ (8.4.0 fails to compile on .NET 10 due to Roslyn 4.x). See STACK.md for full compatibility matrix and package reference blocks.

**Core technologies:**
- **.NET 10 + C# 14:** LTS runtime with field keyword, partial properties -- no configuration needed
- **WPF-UI 4.2.0:** Fluent Design with Mica backdrop, explicitly targets net10.0-windows7.0
- **CommunityToolkit.Mvvm 8.4.2:** MVVM source generators with Roslyn 5.0 for C# 14 support
- **AxMSTSCLib (aximp):** Only viable approach for RDP ActiveX in WPF; COM source generators do not support ActiveX
- **Velopack 0.0.1298:** Auto-update via GitHub Releases; successor to Squirrel.Windows
- **AdysTech.CredentialManager 3.1.0:** Windows Credential Manager wrapper via P/Invoke
- **Serilog 4.3.1:** Structured logging with explicit .NET 10 target
- **System.Text.Json (in-box):** JSON serialization, no external dependency

**Do NOT use:** SecureString (deprecated), Marshal.ReleaseComObject (let AxHost handle it), WPF-UI 3.x (incompatible API), BinaryFormatter (removed in .NET 10), MahApps.Metro (stale).

### Expected Features

**Must have (table stakes):**
- Tabbed multi-session interface with Ctrl+Tab/Ctrl+W/middle-click
- Connection tree with nested groups, drag-drop reorder, F2 rename
- Credential storage via Windows Credential Manager (not in config files)
- Credential inheritance from parent folders (Inherit/Own/Prompt)
- Connection search/filter with fuzzy matching
- Reconnection on disconnect with exponential backoff
- Proper RDP disposal with no GDI leaks -- the core value proposition vs mRemoteNG
- Per-connection error isolation (COM exceptions do not crash the app)
- Dark Fluent UI theme
- DPI awareness (PerMonitorV2)

**Should have (differentiators for v1):**
- Command palette (Ctrl+Shift+P) -- absent from mRemoteNG and RDM
- Auto-update from GitHub Releases -- absent from all free competitors
- App lock with master password and session timeout
- Audit log (local, append-only JSON lines)
- Flicker-free drag/resize via bitmap snapshot
- mRemoteNG import wizard
- Compact chrome (36px icon rail + 30px tab bar, maximum viewport)

**Defer to v1.1+:**
- Quick connect (type hostname, connect without saving)
- Session health monitoring, tagging, smart groups
- Light theme, multi-monitor spanning, RDP Gateway
- Session recording, team sync, plugin API

### Architecture Approach

Three-project solution: Deskbridge (WPF app / composition root), Deskbridge.Core (interfaces, models, events, pipeline), Deskbridge.Protocols.Rdp (ActiveX interop). Communication via WeakReferenceMessenger event bus. Connection establishment orchestrated through an ordered pipeline (resolve credentials, create host, connect, publish events, audit). JSON persistence with atomic writes to %AppData%. Credential inheritance via group tree walk matching industry-standard patterns (RDM, Royal TS). See ARCHITECTURE.md for validated component diagram and data flow.

**Major components:**
1. **Deskbridge.Core** -- domain models, service interfaces, event definitions, pipeline abstractions (innermost layer, no upstream dependencies)
2. **Connection Pipeline** -- ordered stages for connect/disconnect orchestration; extensible via DI
3. **RdpHostControl** -- encapsulates WindowsFormsHost + AxMsRdpClient9, COM lifecycle, siting, disposal (implements IProtocolHost)
4. **Event Bus** -- thin wrapper over WeakReferenceMessenger for cross-cutting messaging
5. **JSON Stores** -- atomic file persistence for connections, settings, audit; schema versioned from day one

**Architecture concerns raised:**
- Disconnect pipeline is missing from REFERENCE.md (connect pipeline is detailed but disconnect is ad-hoc) -- should be a pipeline with stages for future extensibility
- ConnectionContext lifetime ambiguity between connect and disconnect -- session manager should hold a Dictionary mapping Guid to ConnectionContext
- Thread affinity for ActiveX operations -- pipeline stages touching COM must marshal to UI thread
- ResolveCredentialsStage should own the tree-walk logic, not pass IConnectionStore into ICredentialService

### Critical Pitfalls

Top 6 highest-risk items (see PITFALLS.md for all 18):

1. **ActiveX siting order violation** -- Setting properties before siting throws InvalidActiveXStateException. Always: create host, create rdp, set host.Child = rdp, add to visual tree, THEN configure. Encapsulate in RdpHostControl so it is written once. (Phase 5)

2. **Disposal order leaks GDI handles** -- Incorrect disposal accumulates GDI leaks; after ~20-30 tab cycles the app crashes. Follow strict sequence: disconnect, dispose rdp, null child, dispose host, remove from parent. Never call Marshal.ReleaseComObject. (Phases 5-6)

3. **.NET built-in fluent theme breaks WindowsFormsHost** -- Enabling ThemeMode (the .NET 9/10 built-in fluent theme) renders all WindowsFormsHost as black rectangles. Deskbridge uses WPF-UI separate theming. Explicitly disable the built-in backdrop via RuntimeHostConfigurationOption. (Phase 3)

4. **Airspace violations** -- WPF elements overlapping the WindowsFormsHost viewport are invisible. Layout must PUSH the viewport, never overlay it. Notifications, dialogs, and slide-out panels must be positioned outside the viewport grid cell. (Phase 3)

5. **Keyboard focus trapped in RDP control** -- WindowsFormsHost captures all keyboard input; WPF shortcuts stop working. Must intercept Ctrl+Tab, Ctrl+W, Ctrl+Shift+P at the window level via PreProcessInput or HwndSource.AddHook before they reach the hosted control. (Phases 6-7)

6. **Velopack custom Main() breaks WPF resource loading** -- Converting App.xaml to Page for Velopack requires explicit app.InitializeComponent() call. Must follow exact pattern: VelopackApp.Build().Run(), then new App(), then InitializeComponent(), then Run(). Never make Main async (switches to MTA, crashes ActiveX). (Phases 1, 9)

## Implications for Roadmap

### Phase 1: Solution Scaffold
**Rationale:** Everything depends on project structure existing. Zero-risk foundation work.
**Delivers:** Three-project solution, Directory.Build.props, package references, interop DLLs, custom Main with Velopack stub, AppData path constants.
**Addresses:** Build configuration, project structure.
**Avoids:** Pitfall 4 (CommunityToolkit version), Pitfall 10 (Velopack entry point), Pitfall 13 (UseWindowsForms scope), Pitfall 18 (data path safety).

### Phase 2: Core Services
**Rationale:** All downstream phases depend on Core interfaces and models. Event bus and pipeline are the architectural backbone.
**Delivers:** IEventBus, IConnectionPipeline, IProtocolHost, IConnectionStore, ICredentialService interfaces. Domain models. Event record types. Pipeline runner with thread-affinity awareness.
**Addresses:** Connection model, event infrastructure.
**Avoids:** Pitfall 11 (COM thread affinity in pipeline design), Pitfall 16 (cross-thread event dispatch).

### Phase 3: WPF Shell
**Rationale:** Need a visual host before any features can be built. Airspace-safe layout must be established before RDP integration.
**Delivers:** FluentWindow with Mica backdrop, icon rail (36px), slide-out panel (280px push, not overlay), tab bar (30px), status bar, viewport grid cell. WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE hooks for bitmap snapshot. DI wiring in App.xaml.cs.
**Addresses:** Dark Fluent UI, compact chrome, window state persistence.
**Avoids:** Pitfall 3 (built-in theme conflict), Pitfall 5 (airspace layout), Pitfall 8 (resize flicker hooks).

### Phase 4: Connection Management
**Rationale:** Connections must exist before they can be connected to. Persistence and credential storage are prerequisites for the pipeline.
**Delivers:** JsonConnectionStore with atomic writes and backup, TreeView with groups/drag-drop/rename, ConnectionEditor dialog, WindowsCredentialService with TERMSRV/ prefixes, credential inheritance via tree walk.
**Addresses:** Connection tree, credential storage, credential inheritance, export (JSON/CSV).
**Avoids:** Pitfall 12 (JSON corruption), Pitfall 14 (credential edge cases).

### Phase 5: RDP Integration
**Rationale:** Core value proposition. Requires all foundations (pipeline, shell viewport, connections with credentials).
**Delivers:** RdpHostControl (IProtocolHost), correct siting sequence, strict disposal, pipeline stages (ResolveCredentials, CreateHost, Connect, PublishConnected, Audit), disconnect pipeline, IMsTscNonScriptable password setting, reconnection with exponential backoff, DPI change handling, disconnect reason code lookup.
**Addresses:** Proper RDP lifecycle, per-connection error isolation, reconnection.
**Avoids:** Pitfall 1 (siting order), Pitfall 2 (disposal leaks), Pitfall 7 (DPI mismatch), Pitfall 9 (GetOcx cast), Pitfall 15 (disconnect codes), Pitfall 17 (BitmapPeristence typo).

### Phase 6: Tab Management
**Rationale:** Tabs are meaningless without RDP sessions. Keyboard focus management must be built into the tab architecture.
**Delivers:** Tab open/close/switch, active-only rendering (BitmapPeristence=0 for inactive), Ctrl+Tab cycling, Ctrl+W close, middle-click close, scroll overflow, tab limit warning at 15, window-level keyboard intercept for shortcuts.
**Addresses:** Tabbed multi-session, keyboard shortcuts for tabs.
**Avoids:** Pitfall 2 (disposal on tab close), Pitfall 6 (keyboard focus trapping).

### Phase 7: Command Palette and Keyboard Shortcuts (parallel with Phase 6)
**Rationale:** Independent of tab management once IConnectionQuery exists. Major differentiator.
**Delivers:** Ctrl+Shift+P fuzzy search across connections and commands, global keyboard shortcut registration, low-level hook for Escape (exit fullscreen).
**Addresses:** Command palette, connection search/filter.
**Avoids:** Pitfall 6 (focus recovery from WindowsFormsHost).

### Phase 8: Notifications, Logging, Audit, Window State (parallel with Phases 6-7)
**Rationale:** All event-driven features that subscribe to the event bus. Independent of critical path.
**Delivers:** Serilog rolling file logging, toast notification stack (positioned outside viewport), audit trail (JSON lines in %AppData%), window state persistence (position, size, maximized, sidebar state).
**Addresses:** Logging, audit log, status bar, window state persistence.

### Phase 9: Auto-Update (Velopack)
**Rationale:** Needs a working application first. Integrates with existing Main() stub from Phase 1.
**Delivers:** Background update check on launch, status bar notification for available updates, download/apply/restart flow via VelopackApp + GithubSource. GitHub Actions packaging with vpk CLI.
**Addresses:** Auto-update from GitHub Releases.
**Avoids:** Pitfall 10 (InitializeComponent), Pitfall 18 (app directory replacement).

### Phase 10: Import/Export
**Rationale:** Migration feature -- important for adoption but not core functionality. Depends on connection store.
**Delivers:** mRemoteNG confCons.xml parser, import wizard with preview/confirm (no password import), JSON export (no credentials), CSV export.
**Addresses:** mRemoteNG import, export connections.

### Phase Ordering Rationale

- Phases 1-6 are strictly sequential. Each depends on its predecessor: scaffold enables core, core enables shell, shell enables connections, connections enable RDP, RDP enables tabs.
- Phases 7 and 8 run parallel to Phase 6. Command palette needs IConnectionQuery (Phase 2) and connections (Phase 4). Notifications/logging need the event bus (Phase 2) and UI host (Phase 3). Neither depends on tabs.
- Phases 9 and 10 are independent tail work. Auto-update and import can run in parallel after Phase 6.
- The airspace-safe layout in Phase 3 prevents costly rework in Phases 5-6. If the grid layout allows overlapping, every subsequent phase builds on a broken foundation.
- RDP integration (Phase 5) is the highest-risk phase. It concentrates 7 of 18 pitfalls. All prior phases exist to make this phase succeed.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 5 (RDP Integration):** Highest pitfall density. COM interop, ActiveX lifecycle, DPI handling, and disconnect pipeline need careful spike/prototype work. Recommend building a minimal connect-one-session-and-dispose-cleanly prototype before full implementation.
- **Phase 6 (Tab Management):** Keyboard focus management between WPF and WindowsFormsHost is notoriously tricky. The PreProcessInput / HwndSource.AddHook approach needs validation with the actual RDP control.
- **Phase 9 (Auto-Update):** Velopack WPF integration (custom Main, App.xaml as Page) has subtle interactions with resource loading and Fluent theming that need end-to-end verification.

Phases with standard patterns (skip additional research):
- **Phase 1 (Scaffold):** Standard .NET solution setup. All package versions validated.
- **Phase 2 (Core Services):** CommunityToolkit.Mvvm and pipeline pattern are well-documented.
- **Phase 3 (WPF Shell):** WPF-UI documentation and samples cover FluentWindow setup.
- **Phase 4 (Connection Management):** JSON persistence and TreeView are standard WPF patterns.
- **Phase 8 (Notifications/Logging):** Serilog and event-driven architecture are well-documented.
- **Phase 10 (Import/Export):** XML parsing and JSON serialization are standard.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All packages verified on NuGet with .NET 10 compatibility confirmed. Version pins documented with rationale. |
| Features | HIGH | Six competitors analyzed. Table stakes validated against mRemoteNG, Royal TS, RDM. Clear differentiation strategy. |
| Architecture | HIGH | Three-project structure, pipeline pattern, event bus, and credential inheritance validated against Microsoft docs and competitor implementations. |
| Pitfalls | HIGH | 18 pitfalls identified with specific dotnet/wpf issue numbers, reproduction conditions, and mitigations. COM/ActiveX pitfalls validated against mRemoteNG bug reports. |

**Overall confidence:** HIGH

### Gaps to Address

- **Disconnect pipeline design:** REFERENCE.md defines connect pipeline but not disconnect. Must be designed during Phase 5 planning with stages for state save, disconnect, dispose, publish, and audit.
- **ConnectionContext lifetime:** Where does the context live between connect and disconnect? Session manager needs a Dictionary mapping Guid to ConnectionContext. Design during Phase 2.
- **Interop assembly compatibility:** The pre-generated AxMSTSCLib/MSTSCLib DLLs must be verified against the target Windows version at scaffold time. If the GetOcx() cast fails, assemblies need regeneration.
- **GDI handle monitoring:** No automated mechanism defined. Add a debug-build health check that logs GDI handle count and warns at 8,000. Design during Phase 5.
- **Velopack + WPF-UI integration:** End-to-end verification needed that the custom Main() pattern works with WPF-UI FluentWindow resource loading. Verify during Phase 1 scaffold.
- **Multi-monitor DPI transitions:** dotnet/wpf#6294 documents WindowsFormsHost DPI bugs. Manual resize handling needed. Test on mixed-DPI setup during Phase 5.

## Sources

### Primary (HIGH confidence)
- [NuGet package listings](https://www.nuget.org/) -- version verification for all packages
- [Microsoft Learn: WPF and WinForms interop](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-windows-forms-interoperation) -- ActiveX hosting patterns
- [Microsoft Learn: CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) -- MVVM source generators, messenger
- [dotnet/wpf#152](https://github.com/dotnet/wpf/issues/152) -- Airspace issue (open, no fix planned)
- [dotnet/wpf#10044](https://github.com/dotnet/wpf/issues/10044) -- WindowsFormsHost broken with fluent themes
- [dotnet/wpf#11261](https://github.com/dotnet/wpf/issues/11261) -- System.Drawing.Common publish issue
- [Devolutions inheritance docs](https://docs.devolutions.net/rdm/kb/knowledge-base/inheritance/) -- Credential inheritance pattern
- [Velopack docs](https://docs.velopack.io/) -- Update integration and preserved files

### Secondary (MEDIUM confidence)
- [mRemoteNG GitHub issues](https://github.com/mRemoteNG/mRemoteNG/issues) -- Bug patterns (#11, #930, #1198, #1343, #2017, #2090, #2366, #2676)
- [dotnet/wpf#6294](https://github.com/dotnet/wpf/issues/6294) -- WindowsFormsHost DPI scaling bug
- [dotnet/wpf#5892](https://github.com/dotnet/wpf/issues/5892) -- WindowChrome flicker
- [WindowsFormsHost memory leak discussion](https://social.msdn.microsoft.com/Forums/en-US/b24d717b-4aee-4e74-b418-766f2da9f67e/) -- HwndSourceKeyboardInputSite leak
- [RoyalApps.Community.Rdp](https://github.com/royalapplications/royalapps-community-rdp) -- Alternative RDP wrapper (not used)

### Tertiary (LOW confidence)
- Velopack 0.0.1298 .NET 10 forward compatibility -- inferred from netstandard2.0 target, not explicitly tested
- AdysTech.CredentialManager 3.1.0 on .NET 10 -- inferred from P/Invoke stability, not explicitly tested

---
*Research completed: 2026-04-11*
*Ready for roadmap: yes*
