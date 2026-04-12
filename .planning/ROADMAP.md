# Roadmap: Deskbridge

## Overview

Deskbridge is built in 7 phases following a strict dependency chain: foundation services first, then the visual shell, then connection data, then the high-risk RDP integration, then tabs, then all event-bus consumers in parallel (command palette, notifications, logging, audit, app security), and finally tail work (auto-update and import/export). Every phase delivers a coherent, verifiable capability. Phases 1-5 are strictly sequential. Phase 6 contains parallelizable work. Phase 7 is independent tail work.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Foundation** - Solution scaffold, core services, DI, event bus, and pipeline interfaces
- [ ] **Phase 2: Application Shell** - FluentWindow with dark theme, icon rail, slide-out panel, tab bar, status bar, airspace-safe viewport
- [ ] **Phase 3: Connection Management** - Connection model, JSON persistence, tree view, editor dialogs, credential storage with inheritance
- [ ] **Phase 4: RDP Integration** - ActiveX wrapper, siting, disposal, connect/disconnect lifecycle, reconnection, error isolation
- [ ] **Phase 5: Tab Management** - Tab open/close/switch, active-only rendering, keyboard navigation, GDI limit warning
- [ ] **Phase 6: Cross-Cutting Features** - Command palette, keyboard shortcuts, notifications, logging, audit log, app security (master password and lock)
- [ ] **Phase 7: Update & Migration** - Velopack auto-update, GitHub Actions release pipeline, mRemoteNG import, JSON/CSV export

## Phase Details

### Phase 1: Foundation
**Goal**: The solution compiles, all projects reference each other correctly, and the architectural backbone (DI, event bus, pipeline, query interface) exists as working infrastructure that downstream phases build on
**Depends on**: Nothing (first phase)
**Requirements**: PROJ-01, PROJ-02, PROJ-03, PROJ-04, PROJ-05, CORE-01, CORE-02, CORE-03, CORE-04, CORE-05
**Success Criteria** (what must be TRUE):
  1. Running `dotnet build` produces a successful build with zero warnings for all three projects (Deskbridge, Deskbridge.Core, Deskbridge.Protocols.Rdp)
  2. The application launches via the custom Velopack entry point (Program.Main) and shows an empty WPF window without crashing
  3. A test event published on the event bus is received by a subscriber without memory leaks (weak reference verified)
  4. The connection pipeline runner accepts stages and executes them in order, returning success/failure results
  5. The connection query interface returns results from an in-memory test dataset using fuzzy search
**Plans**: 3 plans

Plans:
- [x] 01-01-PLAN.md -- Solution scaffold, build config, Central Package Management, Velopack entry point, WPF-UI dark theme, DI skeleton
- [x] 01-02-PLAN.md -- Core interfaces, models, events, service implementations (event bus, pipelines, notification, query)
- [x] 01-03-PLAN.md -- Comprehensive unit tests for all core services (event bus, pipelines, query, notifications, DI composition)

### Phase 2: Application Shell
**Goal**: Users see a complete, polished dark Fluent UI shell with all layout regions (title bar, icon rail, slide-out panel, tab bar, status bar, viewport) that is airspace-safe and ready to host RDP controls
**Depends on**: Phase 1
**Requirements**: SHEL-01, SHEL-02, SHEL-03, SHEL-04, SHEL-05, SHEL-06, SHEL-07, SHEL-08
**Success Criteria** (what must be TRUE):
  1. The application launches with a dark Fluent window showing Mica backdrop, custom title bar, and snap layout support
  2. Clicking icons in the left rail toggles a 240px slide-out panel that pushes the viewport area (no overlay)
  3. The tab bar displays placeholder tabs with close buttons, active accent, and horizontal scroll on overflow
  4. The viewport area fills all remaining space with no WPF elements overlapping it (verified visually -- airspace-safe)
  5. Custom accent colours (#007ACC, #F44747, #89D185) are applied throughout the shell
**Plans**: 2 plans
**UI hint**: yes

Plans:
- [x] 02-01-PLAN.md -- ViewModels, PanelMode enum, App.xaml resources, accent colour, DI registrations, unit tests
- [x] 02-02-PLAN.md -- MainWindow.xaml full VS Code-style layout (icon rail, panel, tab bar, viewport, status bar, overlay hosts)

### Phase 3: Connection Management
**Goal**: Users can create, organise, and edit connections and groups with full credential storage and inheritance -- everything needed to describe "where to connect" before actually connecting
**Depends on**: Phase 2
**Requirements**: CONN-01, CONN-02, CONN-03, CONN-04, CONN-05, CONN-06, CONN-07, CONN-08, CONN-09, CONN-10
**Success Criteria** (what must be TRUE):
  1. User can create a connection with hostname, port, username, domain, and notes, and see it appear in the connection tree
  2. User can create nested groups, drag-drop connections between groups, and rename items with F2
  3. Closing and reopening the application preserves all connections and groups (JSON persistence at %AppData%)
  4. User can set credentials on a group and see child connections display "inherited from: [group name]" in the credential tab
  5. Groups with stored credentials display a key icon in the connection tree
**Plans**: 4 plans
**UI hint**: yes

Plans:
- [x] 03-01-PLAN.md -- Data layer: IConnectionStore extension, JsonConnectionStore, ICredentialService, WindowsCredentialService, CR-01 fix, unit tests
- [x] 03-02-PLAN.md -- Tree ViewModels and ConnectionTreeControl XAML: TreeView with hierarchical templates, search filter, quick properties panel, MainWindow integration
- [x] 03-03-PLAN.md -- Editor dialogs: ConnectionEditorDialog with 4-tab editor and credential InfoBar, GroupEditorDialog with credentials and inheritance count
- [x] 03-04-PLAN.md -- Tree interactions: multi-select, drag-drop, context menu, F2 rename, all ViewModel commands, Ctrl+N shortcut, visual checkpoint

### Phase 4: RDP Integration
**Goal**: Users can double-click a connection and establish a live RDP session rendered in the viewport, with proper COM lifecycle, reconnection on failure, and per-connection error isolation
**Depends on**: Phase 3
**Requirements**: RDP-01, RDP-02, RDP-03, RDP-04, RDP-05, RDP-06, RDP-07, RDP-08, RDP-09
**Success Criteria** (what must be TRUE):
  1. Double-clicking a connection in the tree establishes an RDP session displayed in the viewport with no flickering or black rectangles
  2. Disconnecting a session and reconnecting 20+ times produces no GDI handle leaks (handle count returns to baseline)
  3. When a connection drops, a reconnection overlay appears offering "Reconnect" (with exponential backoff) and "Close"
  4. A COM error in one RDP session does not crash the application or affect other sessions
  5. During window drag/resize, the viewport shows a smooth bitmap snapshot instead of flickering
**Plans**: 3 plans

Plans:
- [ ] 04-01-PLAN.md -- Smoke-test prototype: RdpSmokeHost + AxSiting helper + 4 gate tests (GDI, IMsTscNonScriptable, siting order, COM error isolation)
- [ ] 04-02-PLAN.md -- Production RdpHostControl (IProtocolHost + WFH leak fixes) + 7 pipeline stages + ConnectionCoordinator + AirspaceSwapper + viewport mount
- [ ] 04-03-PLAN.md -- Reconnect flow: DisconnectReasonClassifier + RdpReconnectCoordinator (2/4/8/16/cap-30 backoff, 20-attempt cap) + ReconnectOverlay + auth-failure skip

### Phase 5: Tab Management
**Goal**: Users can work with multiple simultaneous RDP sessions via a tabbed interface, switching between them efficiently with keyboard or mouse
**Depends on**: Phase 4
**Requirements**: TAB-01, TAB-02, TAB-03, TAB-04, TAB-05
**Success Criteria** (what must be TRUE):
  1. Connecting to a host opens a new tab with the connection name; disconnecting closes the tab automatically
  2. Switching tabs shows the active RDP session live while inactive tabs reduce GDI usage (BitmapPersistence = 0)
  3. Ctrl+Tab/Ctrl+Shift+Tab cycles through tabs, Ctrl+W closes the active tab, and middle-clicking a tab closes it
  4. Opening a 15th simultaneous connection shows a warning about GDI handle limits
**Plans**: TBD

Plans:
- [ ] 05-01: TBD
- [ ] 05-02: TBD

### Phase 6: Cross-Cutting Features
**Goal**: Users have keyboard-first workflows (command palette, global shortcuts), visual feedback (toast notifications), operational visibility (logging, audit trail), and security controls (master password, auto-lock) -- all features that consume the event bus
**Depends on**: Phase 5
**Requirements**: CMD-01, CMD-02, CMD-03, CMD-04, NOTF-01, NOTF-02, NOTF-03, NOTF-04, LOG-01, LOG-02, LOG-03, LOG-04, LOG-05, SEC-01, SEC-02, SEC-03, SEC-04, SEC-05
**Success Criteria** (what must be TRUE):
  1. Ctrl+Shift+P opens a command palette that fuzzy-searches across connections and commands (New Connection, Settings, Disconnect All, Quick Connect)
  2. Connection events (connected, disconnected, failed, reconnecting) produce toast notifications in the bottom-right without modal dialogs
  3. Application logs are written to %AppData%/Deskbridge/logs/ with rolling file rotation, and credentials never appear in logs
  4. An audit trail at %AppData%/Deskbridge/audit.jsonl records all connection events, credential changes, and app lock/unlock events
  5. On first run the user sets a master password; on every subsequent launch a lock overlay blocks access until the password is entered; the app auto-locks after 15 minutes of inactivity
**Plans**: TBD
**UI hint**: yes

Plans:
- [ ] 06-01: TBD
- [ ] 06-02: TBD
- [ ] 06-03: TBD
- [ ] 06-04: TBD

### Phase 7: Update & Migration
**Goal**: The application silently checks for updates and offers one-click upgrade, and users can import their existing mRemoteNG connections and export connection data
**Depends on**: Phase 5 (can run in parallel with Phase 6)
**Requirements**: UPD-01, UPD-02, UPD-03, UPD-04, UPD-05, MIG-01, MIG-02, MIG-03, MIG-04, MIG-05, MIG-06
**Success Criteria** (what must be TRUE):
  1. On startup the application silently checks GitHub Releases for updates; when one is available a notification appears in the status bar
  2. Clicking the update notification downloads, applies, and restarts the application to the new version
  3. A GitHub Actions workflow triggered by a version tag push builds, packages (vpk), and uploads the release
  4. User can import an mRemoteNG confCons.xml file via a wizard that previews connections before confirming (metadata only, no passwords)
  5. User can export connections as JSON (no credentials) or CSV
**Plans**: TBD
**UI hint**: yes

Plans:
- [ ] 07-01: TBD
- [ ] 07-02: TBD
- [ ] 07-03: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7
(Phase 7 can begin before Phase 6 completes -- both depend on Phase 5)

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation | 0/3 | Planning complete | - |
| 2. Application Shell | 0/2 | Planning complete | - |
| 3. Connection Management | 0/4 | Planning complete | - |
| 4. RDP Integration | 0/3 | Planning complete | - |
| 5. Tab Management | 0/2 | Not started | - |
| 6. Cross-Cutting Features | 0/4 | Not started | - |
| 7. Update & Migration | 0/3 | Not started | - |
