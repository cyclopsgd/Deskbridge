# Deskbridge

## What This Is

A modern Windows desktop RDP connection manager built to replace mRemoteNG. Designed for enterprise infrastructure teams who manage dozens to hundreds of remote connections daily. Tabbed multi-session management with a clean, compact dark UI (WPF-UI Fluent), proper COM resource cleanup, keyboard-first workflows, and auto-update from GitHub Releases.

## Core Value

Reliable, flicker-free tabbed RDP sessions with proper ActiveX lifecycle management — if connections freeze, leak, or crash on close, nothing else matters.

## Requirements

### Validated

- ✓ Solution scaffold with shared build config, app manifest, and interop DLLs positioned — Phase 1
- ✓ Core services: DI container, event bus, notification service, connection pipeline interfaces — Phase 1

### Active
- [ ] WPF shell: FluentWindow, dark theme, icon rail, slide-out panel, tab bar, status bar, viewport layout
- [ ] Connection management: JSON persistence, TreeView, connection editor, credential storage with inheritance
- [ ] RDP integration: ActiveX wrapper, siting, disposal, connect/disconnect lifecycle via pipeline
- [ ] Tab management: open/close/switch, active-only rendering, disposal on close
- [ ] Command palette and keyboard shortcuts
- [ ] Notifications, window state persistence, Serilog logging, audit log
- [ ] Auto-update via Velopack + GitHub Actions release pipeline
- [ ] mRemoteNG import and JSON/CSV export

### Out of Scope

- SSH/VNC protocol support — deferred to v2.0, architecture supports it via IProtocolHost
- Light theme — deferred to v2.0, dark-only for v1
- Session health monitoring / latency polling — deferred to v1.1
- Smart connect (type hostname, connect without saving) — deferred to v1.1
- Connection tagging and smart groups — deferred to v1.1
- Secure credential sharing — deferred to v1.2
- Connection profiles (preset display/audio/redirect) — deferred to v1.2
- DPAPI encryption for connections.json — deferred to v2.0
- Multi-monitor spanning — deferred to v2.0
- Session recording — deferred to v2.0
- RDP gateway support — deferred to v2.0
- OAuth/SSO login — not applicable, this is a local desktop app
- SQLite or XML config — JSON only, by design

## Context

**Problem:** mRemoteNG is the de facto open-source RDP manager but suffers from freezes, memory leaks (poor ActiveX disposal), a dated WinForms UI, brittle XML config, no auto-update, and a tightly coupled codebase that makes adding features risky. There is no modern alternative that gets the basics right.

**Architecture philosophy:** Everything through interfaces, event-driven communication via WeakReferenceMessenger, connection pipeline pattern for extensible connect/disconnect flow, queryable connection data (tree, command palette, and quick switch all use the same IConnectionQuery interface). New features slot in without touching existing code.

**Pre-existing assets:** RDP interop assemblies (MSTSCLib.dll, AxMSTSCLib.dll) generated via aximp.exe — classic COM interop only. These live in repo root and must be moved to src/Deskbridge.Protocols.Rdp/Interop/ during scaffold.

**Design authority:** REFERENCE.md (architecture, constraints, feature spec) and DESIGN.md (WPF-UI patterns, XAML conventions, control usage). Both are the technical source of truth for all implementation decisions.

## Constraints

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

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| WPF-UI over HandyControl/MahApps | Native Fluent Design, Windows 11 Mica/Acrylic, active maintenance, auto-restyled standard controls | -- Pending |
| WeakReferenceMessenger as event bus | Already a CommunityToolkit.Mvvm dependency, handles weak references, no leak risk | -- Pending |
| Connection pipeline pattern | Extensible connect flow — new features add stages, don't modify existing ones | -- Pending |
| JSON over SQLite for config | Simpler, human-readable, diffable, sufficient for connection count scale | -- Pending |
| CommunityToolkit.Mvvm 8.4.2+ | Requires 8.4.1+ for C# 14 / .NET 10 partial property support | -- Pending |
| Classic COM interop (aximp) | .NET source generators don't support ActiveX yet | -- Pending |
| App lock with master password | Gates UI access for enterprise environments — doesn't encrypt individual credentials | -- Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? -> Move to Out of Scope with reason
2. Requirements validated? -> Move to Validated with phase reference
3. New requirements emerged? -> Add to Active
4. Decisions to log? -> Add to Key Decisions
5. "What This Is" still accurate? -> Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-04-11 after Phase 1 completion*
