# Requirements: Deskbridge

**Defined:** 2026-04-11
**Core Value:** Reliable, flicker-free tabbed RDP sessions with proper ActiveX lifecycle management

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Project Foundation

- [x] **PROJ-01**: Solution builds on net10.0-windows with all packages restoring successfully
- [x] **PROJ-02**: Directory.Build.props shared config (UseWPF, Nullable, ImplicitUsings) applied to all projects
- [x] **PROJ-03**: App manifest declares Windows 10/11 support and PerMonitorV2 DPI awareness
- [x] **PROJ-04**: Interop DLLs (MSTSCLib.dll, AxMSTSCLib.dll) positioned in src/Deskbridge.Protocols.Rdp/Interop/
- [x] **PROJ-05**: Velopack entry point (custom Main, App.xaml as Page) compiles and runs

### Core Services

- [x] **CORE-01**: DI container registers all services as interfaces in App.xaml.cs composition root
- [x] **CORE-02**: Event bus (WeakReferenceMessenger) publishes and subscribes to typed events without memory leaks
- [x] **CORE-03**: Connection pipeline executes ordered stages (resolve credentials, create host, connect, publish event, update recents, audit)
- [x] **CORE-04**: Notification service raises events consumed by UI for toast display
- [x] **CORE-05**: Connection query interface supports fuzzy search across name, hostname, and tags

### Application Shell

- [x] **SHEL-01**: FluentWindow with WPF-UI dark theme, Mica backdrop, and snap layout support
- [x] **SHEL-02**: 32px custom title bar with min/max/close buttons
- [x] **SHEL-03**: 36px left icon rail with Connections, Search, and Settings icons
- [x] **SHEL-04**: 240px slide-out panel that pushes viewport (no overlay)
- [x] **SHEL-05**: 30px tab bar with connection name, close button, active tab accent, scroll overflow
- [x] **SHEL-06**: 22px status bar showing hostname, resolution, and connection quality
- [x] **SHEL-07**: Viewport area fills remaining space with no WPF elements overlapping (airspace-safe)
- [x] **SHEL-08**: Custom accent colours applied (#007ACC accent, #F44747 error, #89D185 success)

### Connection Management

- [x] **CONN-01**: Connection model with Id, Name, Hostname, Port, Username, Domain, Protocol, GroupId, Notes, Tags, CredentialMode, DisplaySettings, timestamps
- [x] **CONN-02**: Connection groups with Id, Name, ParentGroupId, SortOrder supporting arbitrary nesting
- [x] **CONN-03**: JSON persistence at %AppData%/Deskbridge/connections.json with atomic writes
- [x] **CONN-04**: Connection tree in slide-out panel with context menu, drag-drop reorder, F2 rename, and search filter
- [x] **CONN-05**: Connection editor modal dialog with tabs: General, Credentials, Display, Notes
- [x] **CONN-06**: Group editor for setting group-level credentials with inheritance count indicator
- [x] **CONN-07**: Credential storage via AdysTech.CredentialManager (TERMSRV/<hostname> for connections, DESKBRIDGE/GROUP/<guid> for groups)
- [x] **CONN-08**: Credential inheritance resolves recursively up the group tree (Inherit/Own/Prompt modes)
- [x] **CONN-09**: Connection editor shows "inherited from: [group name]" indicator when CredentialMode is Inherit
- [x] **CONN-10**: Groups display key icon when they have credentials set

### RDP Integration

- [ ] **RDP-01**: RdpHostControl wraps AxMsRdpClient9NotSafeForScripting in WindowsFormsHost implementing IProtocolHost
- [ ] **RDP-02**: ActiveX control sited (added to container) before any property configuration
- [ ] **RDP-03**: Password set via IMsTscNonScriptable cast from GetOcx()
- [ ] **RDP-04**: Strict disposal sequence: disconnect -> dispose rdp -> null child -> dispose host -> remove from tree
- [ ] **RDP-05**: Connect/disconnect lifecycle managed through IConnectionPipeline, never called directly from UI
- [ ] **RDP-06**: Reconnection overlay ("Connection lost -- Reconnect / Close") with exponential backoff (2s, 4s, 8s, max 30s)
- [ ] **RDP-07**: COM try/catch around all ActiveX calls for per-connection error isolation
- [ ] **RDP-08**: All lifecycle events published to IEventBus (connected, disconnected, failed, reconnecting)
- [ ] **RDP-09**: During window drag/resize: bitmap snapshot shown, WindowsFormsHost hidden, resize on drop

### Tab Management

- [x] **TAB-01**: One connection per tab, active tab only renders live ActiveX control
- [x] **TAB-02**: Inactive tabs set BitmapPersistence = 0 to reduce GDI handle usage
- [ ] **TAB-03**: Ctrl+Tab / Ctrl+Shift+Tab to cycle tabs, Ctrl+W to close, middle-click tab to close
- [x] **TAB-04**: Warning shown at 15+ simultaneous connections (GDI handle limit)
- [x] **TAB-05**: Tab opened on ConnectionEstablishedEvent, closed on ConnectionClosedEvent via event bus

### Command Palette & Shortcuts

- [x] **CMD-01**: Ctrl+Shift+P opens floating search box with fuzzy match across connections and commands
- [x] **CMD-02**: Commands available: New Connection, Settings, Disconnect All, Quick Connect
- [x] **CMD-03**: Connection results consume IConnectionQuery.Search() for consistent matching
- [x] **CMD-04**: Ctrl+N new connection, Ctrl+T quick connect, Ctrl+W close tab, F11 fullscreen, Escape exit fullscreen

### Notifications & Status

- [x] **NOTF-01**: Toast notification stack (bottom-right) for connection events: connected, disconnected, reconnecting, errors
- [x] **NOTF-02**: No modal dialogs for non-critical events
- [x] **NOTF-03**: Notifications auto-generated from event bus subscriptions (connection failures, updates available)
- [x] **NOTF-04**: Window state persistence: position, size, maximised, sidebar state saved to %AppData%/Deskbridge/settings.json

### Logging & Audit

- [x] **LOG-01**: Serilog rolling file logging at %AppData%/Deskbridge/logs/ with 10MB cap and 5 file rotation
- [x] **LOG-02**: Audit log at %AppData%/Deskbridge/audit.jsonl as append-only JSON lines with monthly rotation
- [x] **LOG-03**: Audit log records all connection events, credential changes, imports/exports, app lock/unlock
- [x] **LOG-04**: Global exception handler with per-connection error isolation
- [x] **LOG-05**: Credentials never appear in log files

### App Security

- [ ] **SEC-01**: Master password prompt on first run to set PBKDF2-hashed password (stored in %AppData%/Deskbridge/auth.json)
- [ ] **SEC-02**: Full-window lock overlay on app launch requiring master password -- no access to connections or settings without it
- [ ] **SEC-03**: Auto-lock after configurable inactivity timeout (default 15 minutes), timer reset by Deskbridge mouse/keyboard input only
- [ ] **SEC-04**: Ctrl+L to manually lock, auto-lock on Windows session lock (SystemEvents.SessionSwitch)
- [ ] **SEC-05**: Option to lock on minimise (configurable in settings)

### Auto-Update

- [x] **UPD-01**: Velopack checks GitHub Releases via GithubSource silently on startup
- [x] **UPD-02**: Status bar notification when update available, with download/apply/restart flow
- [x] **UPD-03**: UpdateAvailableEvent published to event bus
- [x] **UPD-04**: Self-contained publish with SemVer2 versioning, user data in %AppData% (not alongside exe)
- [x] **UPD-05**: GitHub Actions workflow triggered on version tag push: build, vpk pack, upload to release

### Import & Export

- [x] **MIG-01**: mRemoteNG import parses confCons.xml with field mapping
- [x] **MIG-02**: Import wizard: pick file -> preview connections -> confirm import
- [x] **MIG-03**: Metadata only -- no password import, users re-enter credentials
- [x] **MIG-04**: Imported connections stored with DESKBRIDGE/CONN/{connectionId} credential target (changed from TERMSRV/ in quick task 260416-9wt)
- [x] **MIG-05**: ConnectionImportedEvent published to event bus, import recorded in audit log
- [x] **MIG-06**: Export as JSON (no credentials) and CSV

## v2 Requirements

Deferred to future releases. Tracked but not in current roadmap.

### v1.1 -- Quality of Life

- **QOL-01**: Smart connect -- type hostname in command palette, connect without saving
- **QOL-02**: Session health monitoring -- latency polling, connection quality indicator
- **QOL-03**: Quick switch (Ctrl+P) -- fuzzy search, jump to tab or connect
- **QOL-04**: Connection tagging and smart groups (auto-groups by domain/subnet)
- **QOL-05**: Session snapshots -- screenshot with hotkey

### v1.2 -- Enterprise

- **ENT-01**: Secure credential sharing -- export with one-time encrypted link
- **ENT-02**: Connection profiles -- pre-configured display/audio/redirect settings
- **ENT-03**: Group Policy / managed configuration
- **ENT-04**: Audit log viewer UI

### v2.0 -- Multi-Protocol & Beyond

- **FUT-01**: SSH tab support (terminal emulator + SSH.NET)
- **FUT-02**: VNC support
- **FUT-03**: Light theme
- **FUT-04**: DPAPI encryption for connections.json at rest
- **FUT-05**: RDP gateway support
- **FUT-06**: Multi-monitor spanning
- **FUT-07**: Session recording

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Multi-protocol in v1 (SSH, VNC, Telnet) | Doing RDP perfectly beats doing many protocols poorly. IProtocolHost supports future expansion. |
| SQL/cloud database backend | Adds massive complexity. JSON file sufficient for individual/small-team scale. |
| Built-in password manager/vault | Separate product domain. Windows Credential Manager is clean and OS-secured. |
| Session recording | Requires video encoding, storage, playback UI. Deferred to v2.0. |
| Mobile app / cross-platform | WPF is Windows-only by design. Microsoft's Windows App covers mobile RDP. |
| AI/MCP integration | Premature feature bloat. Command palette is the "smart" interface. |
| Public plugin/extension API | Internal extensibility via IProtocolHost and pipeline. Not a public API. |
| Active Directory browser | Scope creep. Import from mRemoteNG handles existing AD-sourced lists. |
| XML configuration | Source of bugs in mRemoteNG. JSON only by design. |
| File transfer (SFTP/SCP/FTP) | Separate product domain. Users have WinSCP, FileZilla. |
| Team sync / shared documents | Multi-user conflict resolution is hard. Export/import for sharing. |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| PROJ-01 | Phase 1 | Complete |
| PROJ-02 | Phase 1 | Complete |
| PROJ-03 | Phase 1 | Complete |
| PROJ-04 | Phase 1 | Complete |
| PROJ-05 | Phase 1 | Complete |
| CORE-01 | Phase 1 | Complete |
| CORE-02 | Phase 1 | Complete |
| CORE-03 | Phase 1 | Complete |
| CORE-04 | Phase 1 | Complete |
| CORE-05 | Phase 1 | Complete |
| SHEL-01 | Phase 2 | Complete |
| SHEL-02 | Phase 2 | Complete |
| SHEL-03 | Phase 2 | Complete |
| SHEL-04 | Phase 2 | Complete |
| SHEL-05 | Phase 2 | Complete |
| SHEL-06 | Phase 2 | Complete |
| SHEL-07 | Phase 2 | Complete |
| SHEL-08 | Phase 2 | Complete |
| CONN-01 | Phase 3 | Complete |
| CONN-02 | Phase 3 | Complete |
| CONN-03 | Phase 3 | Complete |
| CONN-04 | Phase 3 | Complete |
| CONN-05 | Phase 3 | Complete |
| CONN-06 | Phase 3 | Complete |
| CONN-07 | Phase 3 | Complete |
| CONN-08 | Phase 3 | Complete |
| CONN-09 | Phase 3 | Complete |
| CONN-10 | Phase 3 | Complete |
| RDP-01 | Phase 4 | Pending |
| RDP-02 | Phase 4 | Pending |
| RDP-03 | Phase 4 | Pending |
| RDP-04 | Phase 4 | Pending |
| RDP-05 | Phase 4 | Pending |
| RDP-06 | Phase 4 | Pending |
| RDP-07 | Phase 4 | Pending |
| RDP-08 | Phase 4 | Pending |
| RDP-09 | Phase 4 | Pending |
| TAB-01 | Phase 5 | Complete |
| TAB-02 | Phase 5 | Complete |
| TAB-03 | Phase 5 | Pending |
| TAB-04 | Phase 5 | Complete |
| TAB-05 | Phase 5 | Complete |
| CMD-01 | Phase 6 | Complete |
| CMD-02 | Phase 6 | Complete |
| CMD-03 | Phase 6 | Complete |
| CMD-04 | Phase 6 | Complete |
| NOTF-01 | Phase 6 | Complete |
| NOTF-02 | Phase 6 | Complete |
| NOTF-03 | Phase 6 | Complete |
| NOTF-04 | Phase 6 | Complete |
| LOG-01 | Phase 6 | Complete |
| LOG-02 | Phase 6 | Complete |
| LOG-03 | Phase 6 | Complete |
| LOG-04 | Phase 6 | Complete |
| LOG-05 | Phase 6 | Complete |
| SEC-01 | Phase 6 | Pending |
| SEC-02 | Phase 6 | Pending |
| SEC-03 | Phase 6 | Pending |
| SEC-04 | Phase 6 | Pending |
| SEC-05 | Phase 6 | Pending |
| UPD-01 | Phase 7 | Complete |
| UPD-02 | Phase 7 | Complete |
| UPD-03 | Phase 7 | Complete |
| UPD-04 | Phase 7 | Complete |
| UPD-05 | Phase 7 | Complete |
| MIG-01 | Phase 7 | Complete |
| MIG-02 | Phase 7 | Complete |
| MIG-03 | Phase 7 | Complete |
| MIG-04 | Phase 7 | Complete |
| MIG-05 | Phase 7 | Complete |
| MIG-06 | Phase 7 | Complete |

**Coverage:**
- v1 requirements: 71 total
- Mapped to phases: 71
- Unmapped: 0

---
*Requirements defined: 2026-04-11*
*Last updated: 2026-04-11 after roadmap creation*
