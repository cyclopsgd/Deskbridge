# Phase 3: Connection Management - Context

**Gathered:** 2026-04-11
**Status:** Ready for planning

<domain>
## Phase Boundary

Connection model persistence (JSON with schema versioning), TreeView with drag-drop and multi-select, quick properties panel for inline editing, full ContentDialog editor for connections and groups, credential storage via AdysTech.CredentialManager with inheritance resolution, and all tree interactions (context menu, F2 rename, copy hostname, duplicate). No RDP connectivity — just "where to connect" data management.

</domain>

<decisions>
## Implementation Decisions

### JSON Persistence
- **D-01:** Flat arrays structure — two top-level arrays: `connections` and `groups`. Connections reference groups via GroupId. IConnectionQuery and IConnectionStore already work with flat models from Phase 1.
- **D-02:** Schema versioning from day one — top-level `"version": 1` in connections.json. Future migrations check version and transform.
- **D-03:** Atomic writes via temp-file-then-rename pattern. Write to `connections.json.tmp`, then `File.Move(overwrite: true)`. Crash during write leaves the old file intact.

### TreeView
- **D-04:** Standard WPF TreeView auto-styled by WPF-UI ControlsDictionary. HierarchicalDataTemplate for groups, DataTemplate for connections.
- **D-05:** Both drag-drop AND context menu "Move to..." for moving connections between groups. Drag-drop with visual drop indicator. Context menu as alternative.
- **D-06:** Multi-select via Ctrl+click (individual) and Shift+click (range). Enables bulk delete, move, export.
- **D-07:** Context menu items: Connect, Edit, Delete, Rename (F2), Move to... (submenu), New Connection, New Group, Copy Hostname, Duplicate. Separators between action groups.

### Quick Properties Panel
- **D-08:** Collapsible quick properties panel below the tree in the Connections slide-out panel. Shows key fields of the selected connection (hostname, port, username, group, credential mode) for inline editing. Like mRemoteNG's properties pane.
- **D-09:** When a group is selected, the quick properties panel shows group-specific fields (name, credential username/domain, connection count).

### Editor Dialog
- **D-10:** WPF-UI ContentDialog (modal overlay inside main window) for full connection editing. Uses IContentDialogService from Phase 2.
- **D-11:** 4 tabs as specified in REFERENCE.md: General (hostname, port, name, group), Credentials (mode selector + "inherited from" InfoBar + disabled fields), Display (resolution, color depth, smart sizing), Notes (free text).
- **D-12:** Group editor uses the SAME dual approach: inline quick edit in properties panel, plus right-click "Edit" opens a full ContentDialog with group name, credentials (username, domain, password), and count of connections that will inherit.
- **D-13:** Credential inheritance indicator: WPF-UI InfoBar at top of Credentials tab: "Credentials inherited from [Group Name]". Username/password fields shown but disabled/grayed. Switch CredentialMode to "Own" to enable fields.

### Credential Service
- **D-14:** Full AdysTech.CredentialManager implementation in Phase 3 (not a stub). Credentials saved to Windows Credential Manager immediately when user sets them. TERMSRV/<hostname> for connections, DESKBRIDGE/GROUP/<guid> for groups.
- **D-15:** Prompt mode shows password dialog at connect time (Phase 4/5 pipeline scope). Phase 3 only stores the CredentialMode; the actual prompt is deferred to ResolveCredentialsStage.

### Claude's Discretion
- Dev path for connections.json: Claude picks what's most practical (likely %AppData% always for consistency, with easy reset via delete)
- Quick properties panel layout — which fields exactly, collapsible mechanism, height
- Drag-drop visual indicator style (line between items vs highlight target group)
- Multi-select implementation pattern (custom SelectionMode or manual tracking)
- Search filter behavior in the tree (type-to-filter or separate search box bound to IConnectionQuery)

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Architecture & Data Model
- `REFERENCE.md` §Connection Management — Connection model fields, group model, credential modes (Inherit/Own/Prompt), credential targets (TERMSRV, DESKBRIDGE/GROUP), inheritance walk
- `REFERENCE.md` §Feature Specification — Connection editor tabs, group editor, tree interactions, credential inheritance description
- `REFERENCE.md` §Architecture — IConnectionStore, IConnectionQuery, ICredentialService interfaces

### UI Patterns
- `DESIGN.md` §3 — ContentDialog pattern (for connection/group editor)
- `DESIGN.md` §3 — TreeView auto-styled by ControlsDictionary
- `DESIGN.md` §6 — Colour tokens for tree item styling

### WPF-UI Pitfalls (MANDATORY — read before editing any WPF-UI code)
- `WPF-UI-PITFALLS.md` §1 — ContentDialog host registration and custom dialog subclass base style
- `WPF-UI-PITFALLS.md` §2 — TreeView context menu separate visual tree; shared resources + DataContext pattern
- `WPF-UI-PITFALLS.md` §5 — Icon validation, Color vs Brush resource keys (*Brush suffix required for Foreground/Background)
- `WPF-UI-PITFALLS.md` §7 — MVVM command generation (strips Async/On prefix), ComboBoxItem in ContentDialog trap
- `WPF-UI-PITFALLS.md` §8 — Theme override persistence with SystemThemeWatcher

### Existing Code
- `src/Deskbridge.Core/Models/ConnectionModel.cs` — Existing model (Phase 1)
- `src/Deskbridge.Core/Models/ConnectionGroup.cs` — Existing model (Phase 1)
- `src/Deskbridge.Core/Models/ConnectionFilter.cs` — Existing filter model
- `src/Deskbridge.Core/Models/Enums.cs` — Protocol, CredentialMode, etc.
- `src/Deskbridge.Core/Interfaces/IConnectionStore.cs` — Interface to implement
- `src/Deskbridge.Core/Interfaces/IConnectionQuery.cs` — Already implemented (ConnectionQueryService)
- `src/Deskbridge.Core/Services/ConnectionQueryService.cs` — Needs to consume IConnectionStore (CR-01 from code review)
- `src/Deskbridge/ViewModels/MainWindowViewModel.cs` — Panel state, needs tree binding
- `src/Deskbridge/MainWindow.xaml` — Panel placeholder to replace with real tree

### Prior Phase Context
- `.planning/phases/02-application-shell/02-CONTEXT.md` — Panel layout decisions (240px, instant snap, VS Code toggle)
- `.planning/phases/01-foundation/01-CONTEXT.md` — Test infrastructure, Central Package Management

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ConnectionModel.cs` — Already has Id, Name, Hostname, Port, Username, Domain, Protocol, GroupId, Notes, Tags, CredentialMode, DisplaySettings, timestamps
- `ConnectionGroup.cs` — Already has Id, Name, ParentGroupId, SortOrder
- `ConnectionFilter.cs` — Already has SearchText, Tag, Protocol, GroupId, IsConnected
- `IConnectionStore` — Interface defined, needs implementation (JsonConnectionStore)
- `IConnectionQuery` / `ConnectionQueryService` — Fuzzy search implemented but currently uses empty in-memory list (CR-01 bug). Phase 3 fixes this by injecting IConnectionStore.
- `ICredentialService` — Not yet defined. Needs to be created in Phase 3.
- `IContentDialogService` — Registered in DI from Phase 2. ContentDialogHost placed in MainWindow.xaml.
- Panel placeholder in MainWindow.xaml — "Connection tree will appear here" TextBlock to replace.

### Established Patterns
- MVVM with CommunityToolkit.Mvvm ([ObservableProperty], [RelayCommand])
- DI via Microsoft.Extensions.DependencyInjection
- WPF-UI DynamicResource tokens for all colours
- Event bus for cross-cutting communication
- Tests with xUnit v3, FluentAssertions, NSubstitute

### Integration Points
- MainWindow.xaml Connections panel → replace placeholder with TreeView + properties panel
- App.xaml.cs → register JsonConnectionStore, ICredentialService, new ViewModels
- ConnectionQueryService → inject IConnectionStore to fix CR-01 (empty at runtime)
- Event bus → publish ConnectionCreated, ConnectionEdited, ConnectionDeleted events (for audit log in Phase 6)

</code_context>

<specifics>
## Specific Ideas

- Quick properties panel below the tree is inspired by mRemoteNG's properties pane — user specifically requested this for efficient inline editing
- Dual editing approach: quick properties for fast edits, ContentDialog for full editor with all tabs — both for connections AND groups
- Multi-select + drag-drop + context menu "Move to..." gives maximum flexibility for organizing connections
- Copy Hostname context menu item is a power-user shortcut for scripts/tools
- Duplicate context menu item creates a copy with "(Copy)" suffix

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 03-connection-management*
*Context gathered: 2026-04-11*
