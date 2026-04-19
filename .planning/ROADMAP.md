# Roadmap: Deskbridge

## Milestones

- **v1.0 Core** - Phases 1-7 (complete)
- **v1.1 UI Polish** - Phases 8-12 (in progress)

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

<details>
<summary>v1.0 Core (Phases 1-7)</summary>

- [x] **Phase 1: Foundation** - Solution scaffold, core services, DI, event bus, and pipeline interfaces
- [x] **Phase 2: Application Shell** - FluentWindow with dark theme, icon rail, slide-out panel, tab bar, status bar, airspace-safe viewport
- [x] **Phase 3: Connection Management** - Connection model, JSON persistence, tree view, editor dialogs, credential storage with inheritance
- [x] **Phase 4: RDP Integration** - ActiveX wrapper, siting, disposal, connect/disconnect lifecycle, reconnection, error isolation
- [x] **Phase 5: Tab Management** - Tab open/close/switch, active-only rendering, keyboard navigation, GDI limit warning
- [x] **Phase 6: Cross-Cutting Features** - Command palette, keyboard shortcuts, notifications, logging, audit log, app security (master password and lock)
- [x] **Phase 7: Update & Migration** - Velopack auto-update, GitHub Actions release pipeline, mRemoteNG import, JSON/CSV export

</details>

### v1.1 UI Polish

- [ ] **Phase 8: Resource Foundation** - Centralized style dictionaries, 4px spacing grid, semantic card/layer fill tokens
- [ ] **Phase 9: Quick Properties Panel** - Collapsible card sections, connection state indicator, read-only field distinction
- [ ] **Phase 10: Tree View Polish** - Status dots, hover/selection transitions, vertical indentation guides
- [ ] **Phase 11: Tab Bar Refinement** - Active tab distinction, hover-reveal close buttons, status color accents
- [ ] **Phase 12: General Polish Sweep** - Empty states, hover/press transitions, elevation borders for panel depth

## Phase Details

<details>
<summary>v1.0 Core (Phases 1-7)</summary>

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
- [x] 04-01-PLAN.md -- Smoke-test prototype: RdpSmokeHost + AxSiting helper + 4 gate tests (GDI, IMsTscNonScriptable, siting order, COM error isolation)
- [x] 04-02-PLAN.md -- Production RdpHostControl (IProtocolHost + WFH leak fixes) + 7 pipeline stages + ConnectionCoordinator + AirspaceSwapper + viewport mount
- [x] 04-03-PLAN.md -- Reconnect flow: DisconnectReasonClassifier + RdpReconnectCoordinator (2/4/8/16/cap-30 backoff, 20-attempt cap) + ReconnectOverlay + auth-failure skip

### Phase 5: Tab Management
**Goal**: Users can work with multiple simultaneous RDP sessions via a tabbed interface, switching between them efficiently with keyboard or mouse
**Depends on**: Phase 4
**Requirements**: TAB-01, TAB-02, TAB-03, TAB-04, TAB-05
**Success Criteria** (what must be TRUE):
  1. Connecting to a host opens a new tab with the connection name; disconnecting closes the tab automatically
  2. Switching tabs shows the active RDP session live while inactive tabs reduce GDI usage (BitmapPersistence = 0)
  3. Ctrl+Tab/Ctrl+Shift+Tab cycles through tabs, Ctrl+W closes the active tab, and middle-clicking a tab closes it
  4. Opening a 15th simultaneous connection shows a warning about GDI handle limits
**Plans**: 3 plans

Plans:
- [x] 05-01-PLAN.md -- Core multi-host plumbing: TabHostManager + Tab events + TabState + ConnectionCoordinator cleanup + publisher-side switch-to-existing + AirspaceSwapper per-host visibility fix + unit tests
- [x] 05-02-PLAN.md -- Persistent HostContainer in MainWindow + per-tab reconnect overlays + DI wiring + MainWindowViewModel commands + TabSwitchedEvent status bar + STA integration tests
- [x] 05-03-PLAN.md -- Tab DataTemplate state indicators + ContextMenu + TabReorderBehavior + PreviewKeyDown shortcuts (Ctrl+Tab/1-9/F4/Shift+T) + UAT checkpoint (A1 gate + GDI + drag + state matrix)

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
**Plans**: 4 plans
**UI hint**: yes

Plans:
- [x] 06-01-PLAN.md -- Logging & Audit (Wave 1): IAuditLogger + monthly jsonl writer (SemaphoreSlim + FileShare.ReadWrite), RedactSensitivePolicy, CrashHandler 3-hook install, SerilogSetup with 10MB/5-retained config
- [x] 06-02-PLAN.md -- Notifications + Window State (Wave 2): custom ItemsControl ToastStackControl (Q1 Option B -- max 3, newest-on-top, hover pause), ToastSubscriptionService (6 event bindings + UI-SPEC copy), AppSettings schema + atomic WindowStateService
- [x] 06-03-PLAN.md -- Command Palette + Shortcuts (Wave 3): IAppLockState scaffolding, ICommandPaletteService (4 D-04 commands + ScoreCommand parity), CommandPaletteDialog/ViewModel, KeyboardShortcutRouter extensions (Ctrl+Shift+P/Ctrl+N/Ctrl+T/F11/Esc)
- [x] 06-04-PLAN.md -- App Security (Wave 4, non-autonomous): Wave 0 LockOverlayDialog chrome spike, PBKDF2 MasterPasswordService, LockOverlayDialog + ViewModel, IdleLockService (Pitfall 6 filter), SessionLockService (Pattern 9), AppLockController (Pitfall 5 airspace), CrashDialog UI, Ctrl+L, settings panel, 3 UAT files

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
**Plans**: 4 plans
**UI hint**: yes

Plans:
- [x] 07-01-PLAN.md -- Auto-Update Service + Status Bar Badge + Confirmation UI (Wave 1): IUpdateService, UpdateService (Velopack wrapper), AppSettings UpdateSettingsRecord, status bar badge with download progress, UpdateConfirmDialog, DI wiring, startup check trigger
- [x] 07-02-PLAN.md -- GitHub Actions Release Pipeline (Wave 1): Extend build.yml with release job triggered by v*.*.* tags, vpk pack/upload, stable + beta channels, version from tag
- [x] 07-03-PLAN.md -- Import Parser + Export Services (Wave 1): IConnectionImporter, MRemoteNGImporter (confCons.xml with XXE prevention), ConnectionExporter (JSON tree + CSV flat), test fixtures, unit tests
- [x] 07-04-PLAN.md -- Import Wizard UI + Export Commands (Wave 2): ImportWizardDialog 4-step flow, ImportWizardViewModel, duplicate handling, command palette export commands, settings panel import/export buttons, MIG-04 REQUIREMENTS.md fix

</details>

### Phase 8: Resource Foundation
**Goal**: All UI panels share a single set of style dictionaries, spacing constants, and semantic color tokens so that subsequent polish phases (9-12) apply consistently without per-control overrides
**Depends on**: Phase 7 (v1.0 complete)
**Requirements**: FOUND-01, FOUND-02, FOUND-03
**Success Criteria** (what must be TRUE):
  1. User sees all panels (tree, properties, tabs, status bar) using visually consistent typography, padding, and border weights from shared resource dictionaries
  2. User sees spacing between elements snapping to a 4px grid (4, 8, 12, 16, 24, 32) with no ad-hoc pixel values
  3. User sees card-style sections with distinct background fills that create layered depth (panel background differs from card background differs from input background)
**Plans**: 3 plans
**UI hint**: yes

Plans:
- [x] 08-01-PLAN.md -- Create 3 ResourceDictionary files (SpacingResources, TypographyStyles, CardAndPanelStyles) and merge into App.xaml
- [x] 08-02-PLAN.md -- Migrate MainWindow, ConnectionTreeControl, ToastStackControl, ReconnectOverlay to shared resource references
- [x] 08-03-PLAN.md -- Migrate all 9 dialog XAML files to shared resource references

### Phase 9: Quick Properties Panel
**Goal**: The quick properties panel feels like a professionally designed inspector -- organized into collapsible cards with clear visual hierarchy, live connection status, and obvious read-only vs editable field distinction
**Depends on**: Phase 8
**Requirements**: PROP-01, PROP-02, PROP-03
**Success Criteria** (what must be TRUE):
  1. User sees the properties panel divided into collapsible card sections (Connection, Credentials) that remember their expanded/collapsed state
  2. User sees a connection state indicator (green dot for connected, gray for disconnected, red for error) at the top of the properties panel that updates in real-time
  3. User can visually distinguish read-only fields (muted text, no border) from editable fields (standard input styling with border) without clicking
**Plans**: 2 plans
**UI hint**: yes

Plans:
- [x] 09-01-PLAN.md -- ViewModel + Settings: PropertiesPanelRecord in AppSettings, card expand properties, connection state tracking via TabStateChangedEvent, save/load in MainWindow, unit tests
- [x] 09-02-PLAN.md -- XAML rewrite: CardHeaderToggleStyle, ReadOnlyFieldStyle, collapsible card sections, status dot in header, read-only field distinction, scrollbar fix, visual checkpoint

### Phase 10: Tree View Polish
**Goal**: The connection tree communicates connection state at a glance, feels responsive to interaction, and shows structural hierarchy through visual guides
**Depends on**: Phase 8
**Requirements**: TREE-01, TREE-02, TREE-03
**Success Criteria** (what must be TRUE):
  1. User sees a colored status dot (green = connected, gray = disconnected, red = error) next to each connection name in the tree that updates when connection state changes
  2. User sees smooth hover highlights (150ms fade-in) and selection backgrounds that clearly distinguish hovered, selected, and hovered+selected states
  3. User sees thin vertical lines at each indentation level showing parent-child relationships in the tree hierarchy
**Plans**: 2 plans
**UI hint**: yes

Plans:
- [x] 10-01-PLAN.md -- Per-item ConnectionState property, status dot in connection DataTemplate, animated hover/selection transitions in ControlTemplate
- [ ] 10-02-PLAN.md -- DepthToGuideLinesConverter, vertical indent guide lines in ControlTemplate via ItemsControl

### Phase 11: Tab Bar Refinement
**Goal**: The tab bar clearly communicates which tab is active, what state each connection is in, and provides smooth interaction feedback on close buttons
**Depends on**: Phase 8
**Requirements**: TAB-01v2, TAB-02v2, TAB-03v2
**Success Criteria** (what must be TRUE):
  1. User can immediately identify the active tab through a distinct visual treatment (bottom accent border or background shift) that differentiates it from inactive tabs
  2. User sees tab close buttons only when hovering over a tab, with a smooth fade-in transition (no layout shift when the button appears)
  3. User sees subtle color accents on tabs reflecting their connection state (accent for connected, muted for disconnected, red tint for error)
**Plans**: TBD
**UI hint**: yes

### Phase 12: General Polish Sweep
**Goal**: Every interaction feels intentional and every empty state communicates next steps -- the UI transitions from "functional" to "finished"
**Depends on**: Phase 9, Phase 10, Phase 11
**Requirements**: POLISH-01, POLISH-02, POLISH-03
**Success Criteria** (what must be TRUE):
  1. User sees a helpful empty state (illustration or icon + message + action button) when the connection tree is empty and when no tab is open in the viewport
  2. User sees consistent hover/press feedback (150ms fade transitions) on all interactive elements: buttons, tree rows, tabs, icon rail items, and context menu items
  3. User sees panel edges defined by WPF-UI gradient border brushes that create subtle elevation separation between the tree panel, viewport, and properties panel
**Plans**: TBD
**UI hint**: yes

## Progress

**Execution Order:**
Phases execute in numeric order: 8 -> 9 -> 10 -> 11 -> 12
Phase 9, 10, 11 can run in parallel after Phase 8. Phase 12 depends on 9, 10, 11 completing.

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Foundation | v1.0 | 3/3 | Complete | - |
| 2. Application Shell | v1.0 | 2/2 | Complete | - |
| 3. Connection Management | v1.0 | 4/4 | Complete | - |
| 4. RDP Integration | v1.0 | 3/3 | Complete | - |
| 5. Tab Management | v1.0 | 3/3 | Complete | - |
| 6. Cross-Cutting Features | v1.0 | 4/4 | Complete | - |
| 7. Update & Migration | v1.0 | 4/4 | Complete | - |
| 8. Resource Foundation | v1.1 | 0/3 | Planning complete | - |
| 9. Quick Properties Panel | v1.1 | 0/2 | Planning complete | - |
| 10. Tree View Polish | v1.1 | 1/2 | In Progress|  |
| 11. Tab Bar Refinement | v1.1 | 0/? | Not started | - |
| 12. General Polish Sweep | v1.1 | 0/? | Not started | - |
