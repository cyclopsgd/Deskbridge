# Deskbridge

## What This Is

A modern Windows desktop RDP connection manager built to replace mRemoteNG. Designed for enterprise infrastructure teams who manage dozens to hundreds of remote connections daily. Tabbed multi-session management with a clean, compact dark UI (WPF-UI Fluent), proper COM resource cleanup, keyboard-first workflows, and auto-update from GitHub Releases.

## Core Value

Reliable, flicker-free tabbed RDP sessions with proper ActiveX lifecycle management — if connections freeze, leak, or crash on close, nothing else matters.

## Current Milestone: v1.3 Performance & Customization

**Goal:** Optimize for enterprise-scale usage (hundreds of servers/groups) with performance testing, customization features, bulk operations, and clean uninstall.

**Target features:**
- Performance testing & optimization for large connection trees (hundreds of servers/groups)
- Large import handling (stress testing mRemoteNG imports at scale)
- Settings & customization features (user preferences, appearance options)
- Bulk operations UX (multi-select actions, batch connect/disconnect/edit)
- Uninstall cleanup prompt (offer to remove %AppData% data on uninstall)

## Requirements

### Validated

- ✓ Solution scaffold with shared build config, app manifest, and interop DLLs positioned — Phase 1
- ✓ Core services: DI container, event bus, notification service, connection pipeline interfaces — Phase 1
- ✓ WPF shell: FluentWindow, dark theme, icon rail, slide-out panel, tab bar, status bar, viewport layout — Phase 2
- ✓ Connection management: JSON persistence, TreeView, connection editor, credential storage with inheritance — Phase 3
- ✓ RDP integration: ActiveX wrapper, siting, disposal, connect/disconnect lifecycle via pipeline — Phase 4
- ✓ Tab management: open/close/switch, active-only rendering, disposal on close — Phase 5
- ✓ Command palette and keyboard shortcuts — Phase 6
- ✓ Notifications, window state persistence, Serilog logging, audit log, app-level security (master password, lock overlay, idle/session auto-lock, crash dialog) — Phase 6
- ✓ Auto-update via Velopack + GitHub Actions release pipeline — Phase 7
- ✓ mRemoteNG import and JSON/CSV export — Phase 7
- ✓ UI polish: resource foundation, quick properties, tree view, tab bar refinement — Phases 8-11
- ✓ Certificate skip, logoff tab close, default credentials, import fixes — Phase 13
- ✓ Password field UX, text scaling appearance setting — Phase 14
- ✓ Bulk delete crash fix, tab switch black screen fix, TreeView virtualization — Phase 15
- ✓ RDP resolution/DPI matching, grey VM border investigation — Phase 16
- ✓ SaveBatch API for single-write batch persistence, import wizard migration — Phase 19
- ✓ Performance baselines (BenchmarkDotNet harness, TreeBuild/Store/Search benchmarks) — Phase 20
- ✓ Performance optimizations: pixel-smooth scroll, 250ms search debounce, async startup load, group count badges — Phase 21
- ✓ Large import handling: streaming import executor, progress reporting, failure collection at 500-1000+ scale — Phase 22
- ✓ Bulk operations UX: Connect All / Disconnect All / Bulk Edit for groups and multi-selections (BULK-01/02/03) — Phase 23

### Active
- [ ] Settings & customization features (user preferences, appearance options)
- [ ] Uninstall cleanup prompt (offer to remove %AppData% data on uninstall)

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
- Popout/undock window — deferred from v1.2, future milestone
- App name change — future milestone, existing product name conflict TBD

## Context

**Problem:** mRemoteNG is the de facto open-source RDP manager but suffers from freezes, memory leaks (poor ActiveX disposal), a dated WinForms UI, brittle XML config, no auto-update, and a tightly coupled codebase that makes adding features risky. There is no modern alternative that gets the basics right.

**Architecture philosophy:** Everything through interfaces, event-driven communication via WeakReferenceMessenger, connection pipeline pattern for extensible connect/disconnect flow, queryable connection data (tree, command palette, and quick switch all use the same IConnectionQuery interface). New features slot in without touching existing code.

**Pre-existing assets:** RDP interop assemblies (MSTSCLib.dll, AxMSTSCLib.dll) generated via aximp.exe — classic COM interop only. These live in repo root and must be moved to src/Deskbridge.Protocols.Rdp/Interop/ during scaffold.

**Design authority:** REFERENCE.md (architecture, constraints, feature spec), DESIGN.md (WPF-UI patterns, XAML conventions, control usage), and `.claude/skills/deskbridge-design/` (design system — Windows 11 Fluent tokens, spacing, component specs, visual states). The design system is the visual authority for v1.1 UI Polish.

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
*Last updated: 2026-06-01 after Phase 23 completion*
