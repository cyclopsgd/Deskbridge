# Feature Landscape

**Domain:** RDP Connection Manager (Windows desktop)
**Researched:** 2026-04-11
**Competitors analyzed:** mRemoteNG, Royal TS, Remote Desktop Manager (Devolutions), MobaXterm, RDCMan, Terminals

---

## Table Stakes

Features users expect. Missing = product feels incomplete. Users will abandon for alternatives.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Tabbed multi-session interface | Every competitor has this. Users manage 10-50+ servers daily; single-window switching is the core workflow. | Med | Already in REFERENCE.md spec. Core value proposition. |
| Connection tree with groups/folders | Universal pattern across all 6 competitors. Sysadmins organize by environment (Prod/Dev/QA), location, or role. | Med | Nested groups, drag-drop reorder, F2 rename, context menu. |
| Credential storage (not in config files) | mRemoteNG stores encrypted creds in XML (known vulnerability). Royal TS uses encrypted docs. RDM integrates vaults. Users expect credentials NOT in plaintext. | Med | Deskbridge uses Windows Credential Manager via AdysTech -- stronger than most competitors. |
| Credential inheritance from parent folder | Royal TS, RDM, and RDCMan all support this. Set creds on a folder, all children inherit. This is THE enterprise workflow -- one password change propagates everywhere. | Med | Already specified in REFERENCE.md. Three modes: Inherit/Own/Prompt. Critical path. |
| Import from mRemoteNG | Target audience is mRemoteNG refugees. Without import, migration friction kills adoption. | Med | Parse confCons.xml, map fields, import wizard. No password import (security). |
| Connection search/filter | All competitors have search. When managing 100+ connections, scrolling a tree is unusable. | Low | Fuzzy match on name, hostname, tags via IConnectionQuery. |
| Keyboard shortcuts for tab management | Ctrl+Tab cycle, Ctrl+W close, middle-click close. Universal across all tabbed applications, not just RDP managers. | Low | Standard patterns. Non-negotiable. |
| Reconnection on disconnect | mRemoteNG has it, Royal TS has it, RDM has it. RDP sessions drop for network blips. Users expect automatic retry, not manual re-connect. | Med | Overlay with "Reconnect / Close", exponential backoff (2s-30s). |
| Window state persistence | All desktop apps save position/size. Losing layout on restart is jarring. | Low | Position, size, maximized, sidebar state in settings.json. |
| Dark theme | Industry trend since 2020. mRemoteNG's dated light WinForms is a top complaint. Royal TS V7 added dark mode. RDM supports it. Server admins work in dark environments. | Low | WPF-UI Fluent dark theme. Light deferred to v2. |
| Proper RDP disposal (no freezes/leaks) | This is THE reason to leave mRemoteNG. GitHub issues #11, #930, #1198, #1343, #2017, #2090, #2366, #2676 all document memory leaks, crashes on tab close ("Cannot access disposed object"), and hangs after 4+ hours. | High | Core value prop. Strict disposal sequence. Active-only rendering. BitmapPersistence=0 for inactive tabs. |
| Export connections (JSON/CSV) | Royal TS and RDM both support export. Users need to share server lists with colleagues or back up configurations. | Low | JSON (no creds) and CSV. |
| Per-connection error isolation | mRemoteNG: one bad connection can crash the whole app. Users expect that a failed RDP session does not take down other tabs. | Med | COM try/catch around all ActiveX calls. Already in spec. |
| Status bar with connection info | Royal TS shows connection details. RDM shows status. Users need at-a-glance confirmation of what they're connected to. | Low | Hostname, resolution, connection quality. |
| Logging | Enterprise requirement. When something breaks, admins need logs to diagnose. | Low | Serilog rolling file, 10MB cap, 5 files. |
| DPI awareness / high DPI support | Modern multi-monitor setups with mixed DPI are standard. Royal TS V7 supports per-monitor DPI. Apps that render blurry are rejected. | Low | PerMonitorV2 in app manifest. Already specified. |

---

## Differentiators

Features that set Deskbridge apart. Not universally expected, but valued. These create competitive advantage against the open-source field.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Command palette (Ctrl+Shift+P) | Royal TS added a "global search palette" in V7. RDM does NOT have this. mRemoteNG does NOT have this. IDE-style command palettes (VS Code, JetBrains) are now expected by developer-adjacent users. This is a major UX differentiator against mRemoteNG. | Med | Fuzzy search connections + commands. Consumes IConnectionQuery. |
| Auto-update from GitHub Releases | mRemoteNG has NO auto-update (manual download). RDCMan has NO auto-update. Terminals is abandoned. Royal TS checks for updates but requires manual download for free tier. Seamless background update is a clear win. | Med | Velopack + GitHub Actions. Silent check, status bar notification, download/apply/restart. |
| App lock with master password | RDM Enterprise has 2FA/lock. Royal TS has document passwords. mRemoteNG has basic encryption. A dedicated app-lock with timeout is unusual in the free/open-source tier and addresses enterprise security policy requirements. | Med | PBKDF2 hash, session timeout, lock on Win session switch, Ctrl+L. |
| Audit log (local) | RDM Enterprise has audit trails. Royal TS has basic logging. mRemoteNG has NONE. A local, append-only audit trail for connection events is uncommon in free tools and satisfies compliance-oriented teams. | Low | JSON lines in %AppData%, monthly rotation. Subscribe to event bus. |
| Flicker-free drag/resize | mRemoteNG flickers horribly during resize (WinForms airspace). RDCMan has the same issue. This is a known pain point with ActiveX-in-WPF. The bitmap snapshot technique during WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE is a genuine polish differentiator. | Med | Capture bitmap, show WPF Image overlay, hide WindowsFormsHost during move. |
| Connection pipeline architecture | No open-source competitor has an extensible connect pipeline. mRemoteNG's connection logic is a monolithic method. Deskbridge's pipeline pattern (stages with Order) means features like health checks, monitoring, or gateway support slot in without touching existing code. Not user-visible directly, but enables rapid feature development. | Med | Already architected in REFERENCE.md. 6 default stages. |
| Modern Fluent UI (WPF-UI) | mRemoteNG: dated WinForms (2010-era). RDCMan: dated Win32 (2008-era). Terminals: abandoned WinForms. Only Royal TS and RDM have modern UIs, but both are paid. A modern, polished, free alternative fills a clear gap. | High | WPF-UI FluentWindow, Mica/Acrylic, compact chrome. |
| Compact chrome / maximum viewport | mRemoteNG wastes significant screen space on toolbars and panels. Royal TS has a full Office ribbon. Deskbridge's 36px icon rail + slide-out panel + 30px tab bar maximizes the RDP viewport, which is what users actually look at. | Low | Already designed. Icon rail pushes content, no overlay. |
| Quick connect (type hostname, connect) | Only available in paid RDM and Royal TS. mRemoteNG requires creating a saved connection first. Typing a hostname and immediately connecting is a power-user flow. | Low | Deferred to v1.1 in PROJECT.md, but low complexity. Consider for v1. |

---

## Anti-Features

Features to explicitly NOT build. These are traps that add complexity without proportional value, or contradict Deskbridge's design philosophy.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| Multi-protocol in v1 (SSH, VNC, Telnet) | Royal TS supports 15+ protocols. RDM supports 100+. Attempting multi-protocol in v1 would delay launch by months and dilute focus. mRemoteNG's multi-protocol support is cited as a strength but its RDP quality is terrible -- proving that doing one thing well beats doing many things poorly. | Architecture supports IProtocolHost for future protocols. Ship SSH placeholder project. Do RDP perfectly first. |
| SQL/cloud database backend | RDM Enterprise uses SQL Server, MariaDB for team sharing. This adds massive complexity (migrations, conflict resolution, server infrastructure). Deskbridge targets individual sysadmins and small teams, not 500-person IT departments. | JSON file in %AppData%. Human-readable, diffable, zero infrastructure. |
| Built-in password manager / vault | RDM integrates with 1Password, LastPass, KeePass, Dashlane. Building a password manager is a separate product. Users already have credential solutions. | Use Windows Credential Manager. Clean, OS-level, already secured by Windows login. |
| Session recording | RDM Enterprise and Royal TS offer session recording. This requires video encoding, massive storage, playback UI -- entire subsystems. Compliance teams that need recording already have dedicated tools (CyberArk, BeyondTrust). | Audit log covers who-connected-where-when. Session recording deferred to v2.0. |
| Mobile app / cross-platform | RDM has iOS/Android. Royal TS has macOS (Royal TSX). MobaXterm is Windows-only. Deskbridge is a WPF desktop app -- going cross-platform contradicts the technology choice. Mobile RDP clients already exist (Microsoft's Windows App). | Windows desktop only. WPF-UI is Windows-only by design. |
| AI/MCP integration | RDM 2026.1 added AI-driven MCP automation. This is bleeding-edge feature bloat that most sysadmins will not use in daily workflow. Premature to add. | Focus on keyboard-first efficiency. Command palette is the "smart" interface. |
| Plugin/extension system | Royal TS has a plugin architecture. mRemoteNG technically supports plugins. Building a public plugin API adds significant maintenance burden and versioning complexity. | Internal extensibility via IProtocolHost, pipeline stages, and DI. Not a public API. |
| Active Directory browser / network discovery | RDM and Royal TS can browse AD for computers. This requires LDAP integration, network scanning, and enterprise networking code. Scope creep. | Import from mRemoteNG (which has AD import). Manual connection creation. Dynamic folders deferred. |
| Light theme in v1 | Royal TS V7 supports light and dark. Supporting both themes doubles the testing surface and requires careful token management. Dark is the expected default for server admin tools. | Dark-only for v1. Light theme deferred to v2.0. |
| XML configuration | mRemoteNG uses XML and it is a source of bugs and corruption. RDCMan uses .rdg files (XML). XML is harder to parse, harder to diff, and more fragile than JSON. | JSON only, by design. System.Text.Json. No XML config. |
| Multi-monitor spanning | RDM and Royal TS support spanning RDP across multiple monitors. This is complex (multi-monitor RDP negotiation, display settings) and only relevant for a subset of users who RDP to workstations (not servers). | Deferred to v2.0. SmartSizing handles most use cases. |
| RDP Gateway support | Royal TS and RDM support RD Gateway. This is an enterprise networking feature that adds connection complexity. | Deferred to v2.0. Direct connections cover the majority use case. |
| File transfer (SFTP/SCP/FTP) | MobaXterm has built-in SFTP. Royal TS has file transfer connections. This is a separate product domain. Users have WinSCP, FileZilla, etc. | Not planned. Out of scope entirely. |
| Team sync / shared documents | Royal TS syncs via network shares/Dropbox. RDM uses SQL databases. Multi-user conflict resolution is hard. | Single-user JSON. Export/import for sharing. Team sync deferred to v2.0. |

---

## Feature Dependencies

```
Connection Model          --> Connection Tree (needs model to display)
Connection Model          --> Connection Editor (needs model to edit)
Connection Model          --> JSON Persistence (needs model to serialize)
JSON Persistence          --> mRemoteNG Import (needs storage to import into)
Credential Service        --> Credential Inheritance (needs storage backend)
Credential Inheritance    --> Connection Pipeline (ResolveCredentialsStage)
Connection Pipeline       --> RDP Integration (ConnectStage needs pipeline)
RDP Integration           --> Tab Management (tabs host RDP controls)
Tab Management            --> Command Palette (palette connects/switches tabs)
Event Bus                 --> Notification Service (consumes events)
Event Bus                 --> Audit Log (consumes events)
Event Bus                 --> Tab Management (ConnectionEstablished opens tab)
DI Container              --> Everything (composition root)
WPF Shell                 --> All UI features (host for views)
App Lock Service          --> Master Password (gates UI access)
```

**Critical path:** DI Container --> Event Bus --> Connection Model --> JSON Persistence --> Credential Service --> Connection Pipeline --> RDP Integration --> Tab Management

**Independent of critical path (can be built in parallel):**
- WPF Shell (layout, theme, icon rail)
- Command Palette (once IConnectionQuery exists)
- Notification Service (once Event Bus exists)
- Audit Log (once Event Bus exists)
- mRemoteNG Import (once JSON Persistence exists)
- Auto-update (independent subsystem)
- App Lock (independent subsystem)

---

## MVP Recommendation

### Must ship in v1.0 (ordered by dependency):

1. **Proper RDP lifecycle with no leaks** -- the entire value proposition. If this is broken, nothing else matters. This means correct siting, disposal sequence, per-connection error isolation, active-only rendering.

2. **Connection tree with folder groups** -- users must be able to organize 50-200+ connections. Tree with drag-drop, context menu, F2 rename.

3. **Credential inheritance** -- set credentials on a folder, everything inside inherits. This is the enterprise workflow that makes managing 100+ servers with shared credentials practical.

4. **Tabbed multi-session** -- Ctrl+Tab, Ctrl+W, middle-click close, scroll overflow, active tab accent. Tab limit warning at 15.

5. **Connection search** -- fuzzy match across name, hostname, tags. Both in tree filter and command palette.

6. **Command palette** -- Ctrl+Shift+P. Differentiator vs mRemoteNG. Search connections, execute commands.

7. **mRemoteNG import** -- removes migration friction. Parse XML, preview, confirm. No passwords.

8. **Auto-update** -- Velopack + GitHub Releases. Without this, users are stuck on old versions forever (the mRemoteNG problem).

9. **App lock** -- master password, session timeout. Addresses enterprise security requirements.

10. **Dark Fluent UI** -- the visual hook that signals "this is modern, not another WinForms app."

### Defer to v1.1:

- **Quick connect** (type hostname, connect without saving)
- **Session health monitoring** (latency polling, quality indicator)
- **Connection tagging and smart groups**
- **Quick switch** (Ctrl+P fuzzy jump to tab)
- **Session snapshots** (screenshot with hotkey)

### Defer to v1.2+:

- Connection profiles (preset display/audio/redirect settings)
- Audit log viewer UI (log exists, viewer is later)
- Secure credential sharing
- Group Policy / managed configuration

---

## Competitor Matrix

| Feature | mRemoteNG | Royal TS | RDM (Devolutions) | RDCMan | MobaXterm | **Deskbridge v1** |
|---------|-----------|----------|-------------------|--------|-----------|-------------------|
| Tabbed sessions | Yes | Yes | Yes | Thumbnails only | Yes | **Yes** |
| Folder/group tree | Yes | Yes | Yes | Yes | Yes (sidebar) | **Yes** |
| Credential inheritance | No (per-connection) | Yes | Yes | Yes (groups) | No | **Yes** |
| Search/filter | Basic | Instant search | Yes | No | Basic | **Fuzzy search** |
| Command palette | No | Yes (V7) | No | No | No | **Yes** |
| Dark theme | No | Yes (V7) | Yes | No | Yes | **Yes** |
| Auto-update | No | Check only | Yes | No | Check only | **Yes (silent)** |
| App lock / master pw | Basic encryption | Doc password | 2FA (Enterprise) | None | None | **Yes** |
| Audit log | No | Basic | Yes (Enterprise) | No | No | **Yes** |
| Session recording | No | Yes | Yes (Enterprise) | No | No | No (v2) |
| Multi-protocol | Yes (many) | Yes (many) | Yes (100+) | RDP only | Yes (many) | **RDP only (v1)** |
| Modern UI | No (WinForms) | Yes (V7) | Yes | No (Win32) | No | **Yes (Fluent)** |
| Free | Yes | 10 connections | Free edition | Yes | Free (home) | **Yes** |
| Import from mRemoteNG | N/A | Yes | Yes | No | No | **Yes** |
| Stable RDP lifecycle | No (major bugs) | Yes | Yes | Mostly | N/A (uses mstsc) | **Yes (core goal)** |
| Reconnection | Basic | Yes | Yes | No | No | **Yes (exponential)** |
| Keyboard shortcuts | Basic | Extensive | Extensive | Basic | Basic | **Yes + palette** |

---

## Sources

- [mRemoteNG Official Site](https://mremoteng.org/)
- [mRemoteNG GitHub Issues](https://github.com/mRemoteNG/mRemoteNG/issues) -- Issues #11, #930, #1198, #1343, #2017, #2090, #2366, #2676
- [Royal TS Features](https://www.royalapps.com/ts/win/features)
- [Royal TS All Features](https://www.royalapps.com/ts/win/features-all)
- [Remote Desktop Manager (Devolutions)](https://devolutions.net/remote-desktop-manager/)
- [RDM 2026.1 Release Notes](https://devolutions.net/blog/whats-new-in-devolutions-remote-desktop-manager-20261/)
- [RDM Edition Comparison](https://devolutions.net/remote-desktop-manager/compare/)
- [RDM Credential Inheritance](https://docs.devolutions.net/rdm/concepts/intermediate-concepts/inheritance/)
- [RDCMan (Microsoft Sysinternals)](https://learn.microsoft.com/en-us/sysinternals/downloads/rdcman)
- [MobaXterm Features](https://mobaxterm.mobatek.net/features.html)
- [Terminals GitHub](https://github.com/Terminals-Origin/Terminals)
- [Best Remote Desktop Connection Managers 2025 (Comparitech)](https://www.comparitech.com/net-admin/remote-desktop-connection-managers/)
- [5 Best RDP Connection Managers (Active Directory Pro)](https://activedirectorypro.com/rdp-connection-manager/)
- [MSRDC Deprecation Notice](https://techcommunity.microsoft.com/blog/windows-itpro-blog/prepare-for-the-remote-desktop-client-for-windows-end-of-support/4397724)
- [Devolutions 2025 Wishlist Poll](https://devolutions.net/blog/2025/01/december-poll-results-whats-on-your-wish-list-for-remote-desktop-manager-and-devolutions-pam-in-2025/)
