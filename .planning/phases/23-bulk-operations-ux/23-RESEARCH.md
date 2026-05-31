# Phase 23: Bulk Operations UX - Research

**Researched:** 2026-05-31
**Domain:** WPF .NET 10 / WPF-UI Fluent — TreeView context menus, ContentDialog modals, batch persistence, GDI-safe session management
**Confidence:** HIGH (entirely grounded in the existing codebase; no external library version risk — every dependency is already shipped and in use)

---

> ## ⚠️ SCOPE DISCREPANCY — READ FIRST (planner must resolve)
>
> The **orchestrator task brief** and the **APPROVED 23-UI-SPEC.md** describe two *different* feature sets for the same requirement IDs:
>
> | ID | Orchestrator brief says | APPROVED UI-SPEC + ROADMAP + REQUIREMENTS.md say |
> |----|--------------------------|--------------------------------------------------|
> | BULK-01 | "multi-select delete with confirmation" | **"Connect All"** — open RDP sessions for all connections in a group, with GDI warning |
> | BULK-02 | "bulk move to group" | **"Disconnect All"** — close all active sessions in a group |
> | BULK-03 | "bulk export selected" | **"Bulk Edit"** — edit shared/divergent fields across a multi-selection |
>
> **The UI-SPEC, ROADMAP (`.planning/ROADMAP.md:426-434`), and REQUIREMENTS.md (`:215-217`) all agree with each other** [VERIFIED: read all three]. The orchestrator brief (bulk delete/move/export) does **not** match any of them. Per GSD authority rules, the **APPROVED UI-SPEC is the contract** and this research is written against it (Connect All / Disconnect All / Bulk Edit).
>
> **Additional finding that compounds this:** bulk **delete** and bulk **move** *already exist and ship today*:
> - `DeleteSelectedCommand` → `DeleteSelectedAsync()` [VERIFIED: `ConnectionTreeViewModel.cs:918-1030`] — full multi-select delete with confirmation dialog, active-session close, `DeleteBatch` single-write, selection-clear.
> - `MoveToGroupCommand` → `MoveToGroup(Guid?)` [VERIFIED: `ConnectionTreeViewModel.cs:1136-1172`] — multi-select reparent with cycle prevention, plus a fully-populated "Move to…" submenu [VERIFIED: `ConnectionTreeControl.xaml.cs:283-340`].
> - Bulk **export of a selected subset** does **not** exist — `ConnectionExporter` exports the whole tree only [VERIFIED: file exists at `src/Deskbridge.Core/Services/ConnectionExporter.cs`; no subset overload found].
>
> **Planner action required:** confirm with the user whether Phase 23 implements (a) the UI-SPEC's Connect All / Disconnect All / Bulk Edit, or (b) the orchestrator's delete/move/export. Recommendation: **follow the UI-SPEC** — it is the only signed-off artifact, and delete/move already ship. This research covers BOTH interpretations where they diverge so the planner is not blocked either way, but the primary plan targets the UI-SPEC.

---

## Summary

Phase 23 is almost entirely an **integration-and-reuse** phase, not a greenfield one. Every primitive it needs already exists and is battle-tested in the shipping codebase:

- **Multi-select infrastructure** (`TreeViewMultiSelectBehavior` + VM `SelectedItems` ObservableCollection + per-item `IsSelected`) is complete and drives delete/move today.
- **Batch persistence** (`IConnectionStore.SaveBatch` / `DeleteBatch`, atomic single-write + `.tmp` rename) shipped in Phase 19.
- **The GDI threshold + "performance may degrade" warning** is already implemented in `TabHostManager.FireGdiWarningIfCrossingThreshold` reading `BulkOperationsRecord.GdiWarningThreshold` from settings.
- **The determinate-progress + ContentDialog-close-suppression + `Progress<int>` marshaling pattern** was perfected in Phase 22's `ImportWizardViewModel` / `ImportWizardDialog` and is the template to mirror.
- **The `ContentDialog` hosting + airspace + Danger/Primary button patterns** are established in `ConnectionEditorDialog` and the inline `ShowSimpleDialogAsync` confirm in `DeleteSelectedAsync`.

The genuinely **new** work for the UI-SPEC scope is: (1) two group-context-menu commands (`ConnectAllCommand`, `DisconnectAllCommand`) on `ConnectionTreeViewModel`, (2) a conditional GDI confirmation `ui:ContentDialog`, and (3) a `BulkEditDialog` + `BulkEditViewModel` with per-field enable checkboxes that writes via `SaveBatch`. Connect/Disconnect "all" iterate group descendants and reuse the existing `ConnectRequestedEvent` publish / `ITabHostManager.CloseTabAsync` paths — no new persistence, so **GDI-safe batching with chunked `Progress<T>` is only relevant to Bulk Edit's SaveBatch, which is already a single atomic write and needs no chunking.**

**Primary recommendation:** Add `ConnectAllCommand` + `DisconnectAllCommand` + `BulkEditCommand` to the existing singleton `ConnectionTreeViewModel`; create `BulkEditViewModel` (transient) + `BulkEditDialog : ContentDialog`; reuse `SaveBatch`, the `Progress<int>` UI-thread pattern, the ContentDialog `OnButtonClick`/`Closing` suppression pattern, and the `_airspace.SnapshotAndHideAll()`/`RestoreAll()` wrapper verbatim. Add the three new `MenuItem`s to the existing `GroupContextMenu` and `MultiSelectContextMenu` resources in `ConnectionTreeControl.xaml`.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Multi-select state | `ConnectionTreeViewModel` (App) | `TreeViewMultiSelectBehavior` (View) | VM owns `SelectedItems`/`PrimarySelectedItem`; behavior mutates them on click. Already established. |
| Connect All / Disconnect All commands | `ConnectionTreeViewModel` (App) | `IEventBus` + `ITabHostManager` (Core) | Connect publishes `ConnectionRequestedEvent` (never calls pipeline directly — RDP-05); Disconnect calls `ITabHostManager.CloseTabAsync`. |
| GDI threshold + warning copy | `TabHostManager` / `BulkOperationsRecord` (Core) | confirm dialog (App) | Threshold value + canonical warning string already live in Core; the dialog just reuses them. |
| Bulk edit field diffing + write | `BulkEditViewModel` (App, NEW) | `IConnectionStore.SaveBatch` (Core) | VM computes shared/divergent, builds the mutated `ConnectionModel` list, hands to SaveBatch once. |
| Confirmation / edit dialogs | `*Dialog : ContentDialog` (App, View) | `IContentDialogService` host (App) | All modals use the single `RootContentDialog` host + airspace wrapper. |
| Persistence (atomic batch write) | `JsonConnectionStore` (Core) | — | `SaveBatch`/`DeleteBatch` already atomic with `.tmp` rename. |
| Tree refresh after mutation | `ConnectionTreeViewModel.OnDataChanged` (App) | `ConnectionDataChangedEvent` (Core) | Publishing the event triggers `RefreshTree()` on the UI thread. |

---

## User Constraints (from CONTEXT.md)

**No CONTEXT.md exists for Phase 23** (`ls .planning/phases/23-bulk-operations-ux/` returns only `23-UI-SPEC.md`). The binding constraints therefore come from the **APPROVED 23-UI-SPEC.md** (treat as locked decisions) and CLAUDE.md.

### Locked Decisions (from 23-UI-SPEC.md — APPROVED 2026-05-31)
- **BULK-01 = Connect All** on `GroupContextMenu`: icon `Play24`, placed above New Connection/New Group after a `Separator`. Acts immediately when `(active sessions + connections in group) ≤ GdiWarningThreshold` OR `ConfirmBeforeBulkOperations == false`; otherwise shows the GDI confirmation dialog first.
- **BULK-02 = Disconnect All** on `GroupContextMenu`: icon `PlugDisconnected24`, **no confirmation** (non-destructive), disabled when the group has no active sessions.
- **BULK-03 = Bulk Edit** on `MultiSelectContextMenu`: `Edit…` (ellipsis), icon `Edit24`, placed above `Move to…`; opens a modal `BulkEditDialog`.
- Bulk-edit editable fields: **Hostname, Port, Credential mode, Username, Domain, Group**. **Name is excluded.**
- Per-field enable: unlabeled `CheckBox` in left column; only checked fields are written. `Apply` disabled until ≥1 field checked.
- Bulk-edit write path: Phase 19 `SaveBatch` (single atomic write), then publish `ConnectionDataChangedEvent`. On failure: error `ui:InfoBar`, persist nothing (all-or-nothing).
- GDI confirm dialog: `ui:ContentDialog` `DialogMaxWidth="420"`, `PrimaryButtonAppearance="Primary"` (accent, **not** Danger — these ops are non-destructive), `DefaultButton="Primary"`, body sentence + `ui:InfoBar Severity="Warning"` + `Warning24`. Primary button `Connect All`, Close `Cancel`.
- `BulkEditDialog`: `DialogMaxWidth="520"`, `Title="Edit {N} connections"`, `PrimaryButtonText="Apply"`, `CloseButtonText="Cancel"`, `IsPrimaryButtonEnabled` bound to `CanApply`.
- Settings come from existing `BulkOperationsRecord` (`GdiWarningThreshold`, `ConfirmBeforeBulkOperations`) — already in `AppSettings`. **Do not add new settings.**
- **No new accent usage. No new design language.** Reuse existing styles (`SectionLabelStyle`, `FieldLabelCompactStyle`, `BodyStyle`, `ErrorTextStyle`, `DialogContentMargin`, `FormFieldSpacing`, `CardContainerStyle`).
- Destructive actions in this phase: **none.** No red destructive confirmation.

### Claude's Discretion
- Whether `ConnectAllCommand`/`DisconnectAllCommand` live on `ConnectionTreeViewModel` (recommended — matches every other tree command) or a new sub-VM.
- Disconnect All success toast: silent (default) vs. reuse existing per-session toasts. UI-SPEC says "planner decides; default: silent."
- Whether `BulkEditViewModel` field-diff logic lives in the VM or a small pure helper in Core (recommend VM — it's view-tier diffing of view-tier selection).

### Deferred Ideas (OUT OF SCOPE)
- Bulk delete and bulk move — **already shipped** (see scope-discrepancy box). Do not re-implement.
- Bulk export of a subset — not in the UI-SPEC; out of scope unless the user redirects Phase 23 to the orchestrator interpretation.

---

## Phase Requirements

| ID | Description (from REQUIREMENTS.md `:215-217`) | Research Support |
|----|------------------------------------------------|------------------|
| BULK-01 | "right-click a group and select 'Connect All' to open RDP sessions for all connections, with a warning if the count exceeds the GDI limit" | Reuse `ConnectCommand` publish pattern (`ConnectionTreeViewModel.cs:1058-1083`); GDI threshold from `BulkOperationsRecord.GdiWarningThreshold`; warning copy mirrors `TabHostManager.FireGdiWarningIfCrossingThreshold` (`TabHostManager.cs:362-388`). New `ui:ContentDialog` confirm modal. |
| BULK-02 | "right-click a group or use a command to disconnect all active sessions in the group" | Iterate group descendants (reuse `CloseTabsForGroupDescendants`, `ConnectionTreeViewModel.cs:1036-1050`); call `ITabHostManager.CloseTabAsync` per active tab; `TryGetExistingTab` gate. No new persistence. |
| BULK-03 | "select multiple connections and open a bulk edit dialog that shows shared/divergent fields with per-field enable checkboxes, applying changes to all selected" | New `BulkEditViewModel` + `BulkEditDialog`; diff fields across `SelectedItems`; write via `IConnectionStore.SaveBatch` (`IConnectionStore.cs:16`); mirror `ImportWizardViewModel.ImportSelectedAsync` SaveBatch pattern (`ImportWizardViewModel.cs:374-465`). |

---

## Standard Stack

All dependencies are **already referenced and shipping** — no installs, no version risk. [VERIFIED: `Directory.Packages.props`]

### Core (already in use)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF-UI | 4.2.* | `ui:ContentDialog`, `ui:TextBox`, `ui:InfoBar`, `ui:SymbolIcon`, `ContentDialogHost` | Project UI library; all dialogs already extend `ContentDialog`. |
| CommunityToolkit.Mvvm | 8.4.* | `[ObservableProperty]` partial props, `[RelayCommand]` | Every VM uses it; `BulkEditViewModel` follows the same generator pattern. |
| Microsoft.Extensions.DependencyInjection | 10.0.* | DI registration of new VM/dialog | New types registered in `App.xaml.cs` `ConfigureServices`. |
| System.Text.Json (in-box) | — | Persistence via `JsonConnectionStore` | No new serialization; SaveBatch already handles it. |

### No new packages required.
`npm view` / `dotnet add package` not applicable — this is a closed .NET solution with Central Package Management. [VERIFIED: `Directory.Packages.props` pins all versions with floating patch ranges]

---

## Architecture Patterns

### System Architecture Diagram (data flow for the three features)

```
                         ┌──────────────────────────────────────────────┐
  right-click group ───► │ ConnectionTreeControl.xaml.cs                  │
                         │  TreeView_PreviewMouseRightButtonDown          │
                         │  - selects clicked item if not in selection    │
                         │  - FindResource("GroupContextMenu")            │
                         │  - menu.DataContext = _viewModel               │
                         │  - PopulateMoveToSubmenu(menu)  [existing]      │
                         └───────────────┬──────────────────────────────┘
                                         │ MenuItem.Command (plain {Binding})
                                         ▼
       ┌─────────────────────────────────────────────────────────────────┐
       │ ConnectionTreeViewModel  (singleton)                              │
       │                                                                   │
       │  BULK-01 ConnectAllAsync(group)                                   │
       │    count = activeSessions + descendantConnections                 │
       │    if (count > threshold && ConfirmBeforeBulkOps)                 │
       │        ──► show GDI confirm ui:ContentDialog (airspace wrapped)   │
       │    foreach conn in descendants:                                   │
       │        if !TryGetExistingTab: _eventBus.Publish(                  │
       │                                ConnectionRequestedEvent(model))   │
       │                                                                   │
       │  BULK-02 DisconnectAll(group)         [no confirm]                │
       │    foreach descendant with active tab:                           │
       │        await _tabHostManager.CloseTabAsync(id)                    │
       │                                                                   │
       │  BULK-03 BulkEditAsync()                                          │
       │    vm = new BulkEditViewModel(SelectedItems → models)            │
       │    dialog = new BulkEditDialog(host, vm) ; airspace wrap         │
       │    if Primary: models = vm.ApplyToModels()                       │
       │               _store.SaveBatch(models, [])                       │
       │               _eventBus.Publish(ConnectionDataChangedEvent())    │
       └───────────────┬──────────────────────────┬──────────────────────┘
                       │                          │
        ConnectionRequestedEvent          IConnectionStore.SaveBatch
                       │                          │ (atomic .tmp rename)
                       ▼                          ▼
         ConnectionCoordinator → pipeline   ConnectionDataChangedEvent
         (STA marshal, RDP connect)               │
                       │                          ▼
                       ▼                  OnDataChanged → RefreshTree()
            TabHostManager (GDI warn @ threshold)   (UI thread, rebuilds tree)
```

### Recommended file layout (new files only)
```
src/Deskbridge/
├── ViewModels/
│   └── BulkEditViewModel.cs          # NEW — field-diff + per-field-enable + ApplyToModels()
├── Dialogs/
│   ├── BulkEditDialog.xaml           # NEW — ui:ContentDialog, 3-col grid (checkbox/label/input)
│   └── BulkEditDialog.xaml.cs        # NEW — OnButtonClick validation gate (mirrors ConnectionEditorDialog)
│   └── (GDI confirm uses ShowSimpleDialogAsync OR a tiny BulkConnectConfirmDialog — see Pitfall 4)
└── Views/
    └── ConnectionTreeControl.xaml    # EDIT — add 3 MenuItems to existing menus
    └── ConnectionTreeControl.xaml.cs # EDIT — enable/disable DisconnectAll based on active sessions
```

### Pattern 1: Group-context-menu command wiring (reuse existing contract)
**What:** New menu items bind `Command` against the tree VM (set as `menu.DataContext`) and pass the group VM via `PlacementTarget.DataContext`.
**When to use:** Connect All / Disconnect All on `GroupContextMenu`.
**Example (existing contract — copy it):**
```xml
<!-- Source: ConnectionTreeControl.xaml:66-73 (existing New Connection item) -->
<MenuItem Header="Connect All"
          Icon="{ui:SymbolIcon Play24}"
          Command="{Binding ConnectAllCommand}"
          CommandParameter="{Binding PlacementTarget.DataContext,
              RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}}" />
```
The command parameter is the `GroupTreeItemViewModel` itself (its `.Children` gives descendants). [VERIFIED: `ConnectionTreeControl.xaml.cs:248-274` sets `menu.DataContext = _viewModel`; `:283` PopulateMoveToSubmenu shows the established dynamic-submenu approach.]

### Pattern 2: Iterate group descendants (already written — reuse verbatim)
**What:** Recursive walk over `GroupTreeItemViewModel.Children` collecting connections (incl. nested groups).
**Source:** `CloseTabsForGroupDescendants` at `ConnectionTreeViewModel.cs:1036-1050`. For Connect All, write a sibling `CollectDescendantConnections(group)` returning `List<ConnectionTreeItemViewModel>` using the identical recursion shape. Note `GroupTreeItemViewModel.ConnectionCount` (`GroupTreeItemViewModel.cs:20-31`) already counts recursively — use it for the GDI threshold count.

### Pattern 3: GDI threshold + canonical warning copy (reuse the shipped string)
**What:** The "performance may degrade" wording is locked and already used by the 15-session snackbar.
**Source:** `TabHostManager.cs:373-378`:
```csharp
_snackbar.Show(
    "Approaching session limit",
    $"{_gdiWarningThreshold} active sessions reached — performance may degrade beyond this point.",
    ControlAppearance.Caution,
    new SymbolIcon { Symbol = SymbolRegular.Warning24 },
    TimeSpan.FromSeconds(6));
```
UI-SPEC confirm body: `"This will open {N} sessions. {threshold}+ active sessions may degrade performance. Continue?"` — mirror "may degrade performance". The threshold value: read `BulkOperationsRecord.GdiWarningThreshold` from settings. **Note:** `TabHostManager._gdiWarningThreshold` is private; the tree VM does not currently inject `IWindowStateService`/settings. The planner must thread the threshold to the VM — either inject `IWindowStateService` into `ConnectionTreeViewModel` (it already takes 10 ctor deps) and read `settings.BulkOperations`, or expose the threshold from `ITabHostManager`. **Recommendation: inject `IWindowStateService`** (consistent with how `TabHostManager` reads it at `TabHostManager.cs:73-75`) and also read `ConfirmBeforeBulkOperations` from the same record.

### Pattern 4: ContentDialog confirmation hosting + airspace wrap (locked pattern)
**What:** Every modal must use the single `RootContentDialog` host, be airspace-wrapped, and (for a custom-content confirm with an `InfoBar`) extend `ContentDialog`.
**Two options for the GDI confirm:**
- **Option A (simplest):** `IContentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions{...})` exactly like `DeleteSelectedAsync` (`ConnectionTreeViewModel.cs:960-967`). Downside: `SimpleContentDialog` cannot host a `ui:InfoBar`; the warning is plain text only.
- **Option B (matches UI-SPEC's `InfoBar Severity="Warning"`):** a tiny `BulkConnectConfirmDialog : ContentDialog` taking the host in ctor (mirror `ConnectionEditorDialog.cs:11-20`) with `DialogMaxWidth="420"`, `PrimaryButtonAppearance="Primary"`, `DefaultButton="Primary"`, content = sentence + `ui:InfoBar`.
**Recommendation: Option B** — the UI-SPEC explicitly specifies `ui:InfoBar Severity="Warning"` + `Warning24`, which `ShowSimpleDialogAsync` can't render.
**Airspace wrap (mandatory — copy verbatim):**
```csharp
// Source: ConnectionTreeViewModel.cs:743-759 (every dialog open does this)
_airspace.SnapshotAndHideAll();
try { var result = await dialog.ShowAsync(); /* ... */ }
finally { _airspace.RestoreAll(); }
```
Guard with the `_isDialogOpen` flag (`ConnectionTreeViewModel.cs:45`, `:720-768`) — `ShowAsync` throws if the host is already busy.
[CITED: docs/WPF-UI-PITFALLS.md §1 — host is `RootContentDialog`/`ContentDialogHost`, dialog ctor takes host, calls `base(host)`; airspace fix from quick task 260419-efs.]

### Pattern 5: Bulk-edit ContentDialog with validation gate (mirror ConnectionEditorDialog)
**What:** Override `OnButtonClick`; on Primary, validate and `return` (without `base.OnButtonClick`) to keep the dialog open on failure.
**Source:** `ConnectionEditorDialog.cs:35-71` (validation gate) + the `Dialog_PreviewKeyDown` Enter-swallow (`:26-33`). The `BulkEditDialog` should:
- `IsPrimaryButtonEnabled="{Binding CanApply}"` (≥1 checkbox checked).
- On Primary: no validation needed beyond CanApply (or port-range check like `SaveConnectionFromQuickEdit` `:579`); call `vm.Validate()` then `base.OnButtonClick`.
- Swallow Enter in text inputs (Pitfall S1, `ConnectionEditorDialog.cs:26-33`).

### Pattern 6: SaveBatch single atomic write + event publish (Phase 19/22 pattern)
**What:** Build the mutated model list, call `SaveBatch` exactly once, publish `ConnectionDataChangedEvent` (caller-publishes pattern, per STATE.md decision).
**Source:** `ImportWizardViewModel.cs:441-442`:
```csharp
_store.SaveBatch(result.ConnectionsToSave, result.GroupsToSave);
_bus.Publish(new ConnectionDataChangedEvent());
```
For bulk edit, groups are unchanged → `SaveBatch(editedConnections, [])`. `OnDataChanged` (`ConnectionTreeViewModel.cs:402-405`) marshals to UI thread and calls `RefreshTree()`. **All-or-nothing:** build the full list first; if any field-apply throws, abort before SaveBatch and show the error InfoBar (UI-SPEC error-state contract). Wrap in try/catch like `DeleteSelectedAsync:982-1025`.

### Anti-Patterns to Avoid
- **Do NOT call `IConnectionStore.Save` in a loop** for bulk edit — that is per-item write amplification SaveBatch was built to eliminate (IMP-04 / Phase 19). Use `SaveBatch` once.
- **Do NOT call the connection pipeline directly** from Connect All — publish `ConnectionRequestedEvent` (RDP-05; `ConnectionTreeViewModel.cs:1061-1063`). The publisher-side `TryGetExistingTab` check prevents duplicate tabs and breaks the coordinator↔tab-manager circular DI.
- **Do NOT use `EnableCollectionSynchronization`** for cross-thread tree updates — this project marshals via `Progress<T>`/`Dispatcher.InvokeAsync` (`SetOnUiThread`, `ConnectionTreeViewModel.cs:407-414`). [VERIFIED: no `EnableCollectionSynchronization` anywhere in the source.]
- **Do NOT add red/Danger styling** to Connect/Disconnect — UI-SPEC: these are non-destructive (Primary accent button + Warning InfoBar only).
- **Do NOT bind `RelativeSource AncestorType=TreeView` in context menus** — menus are rooted in `UserControl.Resources`, not the TreeView; use `PlacementTarget.DataContext` (`ConnectionTreeControl.xaml.cs:244-247`).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Atomic multi-connection write | Manual file write / loop of `Save` | `IConnectionStore.SaveBatch` | Already atomic (.tmp rename), already publishes nothing (caller publishes), tested in `SaveBatchTests.cs`. |
| Cross-thread tree refresh | `EnableCollectionSynchronization` / manual locks | Publish `ConnectionDataChangedEvent` → `OnDataChanged` → `RefreshTree` | Established marshaling; rebuilds the whole tree on UI thread. |
| Group-descendant traversal | New recursion | `CloseTabsForGroupDescendants` shape + `GroupTreeItemViewModel.ConnectionCount` | Both exist and are cycle-aware via model walk. |
| Move-to-group submenu | New flyout TreeView | `PopulateMoveToSubmenu` + `GetAvailableGroupsForMove` | Already builds an indented, cycle-safe group list with current-parent disabling. (Only relevant if planner pivots to the orchestrator "bulk move" interpretation — it already works.) |
| Cycle detection on move | New algorithm | `IsDescendantOrSelf` (`ConnectionTreeViewModel.cs:1359-1368`) + `GetAvailableGroupsForMove` visited-set guard (`:691-709`) | Two layers already prevent group-into-descendant. |
| GDI threshold value & warning copy | New constant / new string | `BulkOperationsRecord.GdiWarningThreshold` + the shipped snackbar phrasing | Single source of truth; user-configurable in Settings (Phase 18). |
| ContentDialog host lookup | New host element | `IContentDialogService.GetDialogHostEx()` + `base(host)` ctor | The single `RootContentDialog` host; airspace-correct. |
| Determinate progress marshaling | New threading code | `new Progress<int>(...)` constructed on UI thread (Phase 22) | Auto-marshals via captured `DispatcherSynchronizationContext`. (Only needed if a long batch is added — see Pitfall 3.) |

**Key insight:** This phase is ~80% wiring existing primitives. The plan-checker should flag any task that re-implements delete, move, batch-write, descendant traversal, or the GDI warning string.

---

## Common Pitfalls

### Pitfall 1: GDI threshold not reachable from the tree VM
**What goes wrong:** `_gdiWarningThreshold` is private to `TabHostManager`; `ConnectionTreeViewModel` has no settings dependency, so Connect All can't compute "over threshold".
**Why it happens:** Settings were read once in `TabHostManager` ctor (`TabHostManager.cs:73-75`), not exposed.
**How to avoid:** Inject `IWindowStateService` into `ConnectionTreeViewModel` and read `settings.BulkOperations.GdiWarningThreshold` + `.ConfirmBeforeBulkOperations` (same call shape as `TabHostManager`). Count = `tabHostManager.ActiveCount + group.ConnectionCount` (`ITabHostManager.ActiveCount` exists, `TabHostManager.cs:84`).
**Warning signs:** plan introduces a hardcoded `15` — that ignores the user's Phase 18 setting.

### Pitfall 2: Bulk edit "divergent vs shared" field semantics
**What goes wrong:** Naively pre-filling a field with the first connection's value silently overwrites the others when the user didn't intend to touch that field.
**Why it happens:** Per-field enable checkbox is the gate — only **checked** fields are written. A divergent field must show the `Multiple values` placeholder and stay blank/unchecked.
**How to avoid:** `BulkEditViewModel` computes, per editable field (Hostname, Port, Credential mode, Username, Domain, Group): `IsShared = SelectedItems.Select(f).Distinct().Count() == 1`. Shared → pre-fill value, checkbox unchecked. Divergent → blank + `PlaceholderText="Multiple values"`, checkbox unchecked. `ApplyToModels()` writes a field to ALL selected models **only if its enable-checkbox is checked.** Name is excluded entirely.
**Warning signs:** a test where editing one field clobbers an untouched divergent field across the selection.

### Pitfall 3: Assuming Bulk Edit needs chunked progress batching
**What goes wrong:** Over-engineering a `Progress<int>` + chunk loop for bulk edit when SaveBatch is already one write.
**Why it happens:** The orchestrator brief mentions "GDI-safe progress batching for large selections" — but that concern applies to opening *RDP sessions* (Connect All, which is bounded by the GDI threshold and intentionally warns/refuses past it), not to a data edit.
**How to avoid:** Bulk Edit = build list → `SaveBatch` once → publish event. No chunking, no `Progress<int>`. The "GDI-safe" requirement is satisfied by the **Connect All threshold gate**, which is the only place GDI handles grow. If the planner wants progress feedback for Connect All opening many sessions, mirror the `ImportWizardViewModel` determinate-progress + close-suppression pattern (`ImportWizardViewModel.cs:396-430`, `ImportWizardDialog.cs:53-99`) — but the UI-SPEC does **not** require it (Connect All "acts immediately" once confirmed).
**Warning signs:** a `Progress<int>` denominator or chunk-size constant in the Bulk Edit plan.

### Pitfall 4: ContentDialog with InfoBar needs a real ContentDialog subclass
**What goes wrong:** `ShowSimpleDialogAsync` renders the warning as plain text; the UI-SPEC's `ui:InfoBar Severity="Warning"` + `Warning24` never appears.
**Why it happens:** `SimpleContentDialogCreateOptions.Content` takes a string/object but the Simple template doesn't host rich content well.
**How to avoid:** For the GDI confirm, create `BulkConnectConfirmDialog : ContentDialog` (ctor takes `ContentDialogHost`, calls `base(host)`) with the InfoBar in XAML — mirror `ConnectionEditorDialog`. Register it transient in DI like the other dialogs (`App.xaml.cs:450`).
**Warning signs:** plan uses `ShowSimpleDialogAsync` for the GDI confirm while also claiming the Warning InfoBar.

### Pitfall 5: Selection semantics after each op
**What goes wrong:** Selection state desyncs (orphaned `IsSelected=true` on VMs no longer in `SelectedItems`, or stale after RefreshTree rebuilds VMs).
**Why it happens:** `RefreshTree` creates **new** VM instances (`BuildTree` `:428-450`), so old `SelectedItems` point at dead objects.
**How to avoid:**
- **Connect All:** no selection change (operates on a group, not the multi-selection) — leave selection alone.
- **Disconnect All:** no data mutation, no RefreshTree, no selection change.
- **Bulk Edit:** after SaveBatch → `ConnectionDataChangedEvent` → RefreshTree rebuilds. Clear stale selection then optionally re-select by ID using `RestoreSelectionById` (`:210-224`) — but multi-select restore isn't supported by that single-id helper. UI-SPEC says move "preserves" selection and delete "clears" it; for edit, simplest correct behavior is **clear selection after Apply** (mirror `DeleteSelectedAsync:1027-1028`: `SelectedItems.Clear(); PrimarySelectedItem = null;`) before RefreshTree. Confirm with user if preservation is desired.
**Warning signs:** test asserting `SelectedItems` still references live tree VMs after a RefreshTree.

### Pitfall 6: Disconnect All enable/disable state
**What goes wrong:** Menu item is always enabled even when the group has zero active sessions (UI-SPEC requires disable).
**Why it happens:** Native `MenuItem.IsEnabled` needs a value at menu-build time; commands' `CanExecute` isn't re-evaluated for dynamically-assigned context menus reliably.
**How to avoid:** Set `IsEnabled` imperatively when building the menu in `TreeView_PreviewMouseRightButtonDown` (like `PopulateMoveToSubmenu` disables items, `:312`/`:333`): compute `group.descendants.Any(c => _tabHostManager.TryGetExistingTab(c.Id, out _))` and set the Disconnect All item's `IsEnabled`. This requires the code-behind to reach the tab manager — expose a `bool GroupHasActiveSessions(GroupTreeItemViewModel)` helper on the VM.
**Warning signs:** Disconnect All clickable on a group with no live tabs.

### Pitfall 7: `_isDialogOpen` re-entrancy guard
**What goes wrong:** Opening the GDI confirm then the (non-existent) follow-up, or rapid double-invoke, throws because the ContentDialog host is busy.
**How to avoid:** Reuse the `_isDialogOpen` guard pattern (`:720-768`) around every `ShowAsync`. Connect All: set guard around the confirm dialog only; the connect loop runs after the dialog closes.

---

## Code Examples

### Counting for the GDI threshold (new helper on the VM)
```csharp
// Mirror CloseTabsForGroupDescendants (ConnectionTreeViewModel.cs:1036) + ConnectionCount (GroupTreeItemViewModel.cs:20)
private List<ConnectionTreeItemViewModel> CollectDescendantConnections(GroupTreeItemViewModel group)
{
    var result = new List<ConnectionTreeItemViewModel>();
    foreach (var child in group.Children)
    {
        if (child is ConnectionTreeItemViewModel conn) result.Add(conn);
        else if (child is GroupTreeItemViewModel nested)
            result.AddRange(CollectDescendantConnections(nested));
    }
    return result;
}
```

### Connect All command shape (publish, never call pipeline)
```csharp
// Source pattern: ConnectionTreeViewModel.cs:1058-1083 (Connect) + :1036 (descendant walk)
[RelayCommand]
private async Task ConnectAllAsync(GroupTreeItemViewModel? group)
{
    if (group is null || _isDialogOpen) return;
    var conns = CollectDescendantConnections(group);
    if (conns.Count == 0) return;

    int projected = _tabHostManager.ActiveCount + conns.Count;
    var bulk = (await _windowState.LoadAsync())?.BulkOperations ?? BulkOperationsRecord.Default;
    if (bulk.ConfirmBeforeBulkOperations && projected > bulk.GdiWarningThreshold)
    {
        // show BulkConnectConfirmDialog (airspace-wrapped, _isDialogOpen guarded); bail on Cancel
    }
    foreach (var c in conns)
    {
        var model = _connectionStore.GetById(c.Id);
        if (model is null) continue;
        if (_tabHostManager.TryGetExistingTab(model.Id, out _)) { _tabHostManager.SwitchTo(model.Id); continue; }
        _eventBus.Publish(new ConnectionRequestedEvent(model));   // RDP-05: never call pipeline directly
    }
}
```

### Bulk edit apply (single SaveBatch, all-or-nothing)
```csharp
// Source pattern: ImportWizardViewModel.cs:441-442 + DeleteSelectedAsync try/catch:982-1025
var edited = bulkEditVm.ApplyToModels(); // throws nothing; returns List<ConnectionModel> with only checked fields changed
try
{
    _connectionStore.SaveBatch(edited, []);          // single atomic write (IMP-04)
    _eventBus.Publish(new ConnectionDataChangedEvent()); // → OnDataChanged → RefreshTree (UI thread)
    SelectedItems.Clear(); PrimarySelectedItem = null;
}
catch (Exception ex)
{
    Serilog.Log.Error(ex, "Bulk edit failed");
    // surface error InfoBar; persist nothing (SaveBatch is atomic — partial writes impossible)
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Per-item `Save` in delete/import loops | `DeleteBatch` / `SaveBatch` single atomic write | Phase 15 / Phase 19 | Bulk edit MUST use SaveBatch, not a Save loop. |
| Threshold hardcoded `15` in `TabHostManager` | `BulkOperationsRecord.GdiWarningThreshold` from settings | Phase 18 | Connect All confirm must read the setting, not a literal. |
| Spinner-only import | Determinate `Progress<int>` + close-suppression | Phase 22 | The reference pattern if Connect All ever needs progress (not required by UI-SPEC). |

**Deprecated/outdated:** none relevant. No WPF-UI 3.x patterns, no `SecureString`, no `EnableCollectionSynchronization` — all consistent with CLAUDE.md.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Phase 23 follows the **UI-SPEC** (Connect/Disconnect/Bulk-Edit), not the orchestrator brief (delete/move/export) | Scope box | HIGH — wrong scope = wrong phase. Planner MUST confirm with user. Mitigated: research covers both. |
| A2 | The VM should inject `IWindowStateService` to read the GDI threshold/confirm flag | Pitfall 1, Pattern 3 | LOW — alternative is exposing threshold via `ITabHostManager`; either works. |
| A3 | Bulk Edit clears selection after Apply (vs. preserving) | Pitfall 5 | LOW — UI-SPEC doesn't specify edit selection behavior; clearing is simplest-correct. Confirm with user. |
| A4 | GDI confirm uses a custom `ContentDialog` subclass (Option B) to host the Warning `InfoBar` | Pattern 4, Pitfall 4 | LOW — driven directly by UI-SPEC's `ui:InfoBar Severity="Warning"` requirement. |
| A5 | Disconnect All success is silent (no toast) | Discretion | LOW — UI-SPEC default is "silent". |

---

## Open Questions (RESOLVED)

> Resolved 2026-05-31: (1) Scope = UI-SPEC (Connect/Disconnect/Bulk-Edit; delete+move already ship); (2) Bulk Edit Group sets GroupId directly, no cycle check; (3) Connect All has no progress UI (threshold gate is the safety mechanism).

1. **Which feature set does Phase 23 implement?** (See scope box.)
   - What we know: UI-SPEC + ROADMAP + REQUIREMENTS all say Connect/Disconnect/Bulk-Edit; orchestrator brief says delete/move/export; delete + move already ship.
   - What's unclear: why the orchestrator brief diverges.
   - Recommendation: **discuss-phase / planner must confirm with the user.** Default to the UI-SPEC.

2. **Bulk Edit "Group" field — is moving connections to a different group part of bulk edit?**
   - What we know: UI-SPEC lists "Group" as an editable bulk field; `MoveToGroup` already does this for the multi-selection.
   - What's unclear: whether editing Group in the dialog should reuse `MoveToGroup`'s cycle logic (only relevant for connections, which have no descendants, so no cycle risk — connections only).
   - Recommendation: in Bulk Edit, the Group field only applies to connections (the editable set is connection fields); set `model.GroupId` directly. No cycle check needed (connections can't contain groups).

3. **Should Connect All show progress for very large groups?**
   - What we know: UI-SPEC says "acts immediately" after the threshold confirm; no progress UI specified.
   - Recommendation: no progress UI (the threshold gate is the safety mechanism). Document the Phase 22 progress pattern as available if the user later wants it.

---

## Environment Availability

> Skipped — Phase 23 is pure in-solution C#/XAML against already-referenced packages. No external tools, services, runtimes, or CLIs are introduced. (Build/test use the existing `dotnet` SDK + xUnit.v3 already configured.)

---

## Validation Architecture

> `workflow.nyquist_validation = true` (config.json `:18`) — section included.

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit.v3 3.2.* + FluentAssertions 8.9.* + NSubstitute 5.3.* [VERIFIED: `Directory.Packages.props:25-27`, `Deskbridge.Tests.csproj:13-15`] |
| Config file | none — Microsoft.Testing.Platform via `TestingPlatformDotnetTestSupport` (`Deskbridge.Tests.csproj:6`) |
| Quick run command | `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj --filter "FullyQualifiedName~BulkEdit\|FullyQualifiedName~ConnectAll\|FullyQualifiedName~DisconnectAll"` |
| Full suite command | `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj` |

Fixture convention: **per-file `<Content Include>` with `CopyToOutputDirectory=PreserveNewest`**, OR a glob for a directory (`Fixtures\large\*.xml`) [VERIFIED: `Deskbridge.Tests.csproj:25-32`]. Bulk-ops tests need **no XML fixtures** — they construct `ConnectionModel`/`ConnectionGroup` in-memory against a temp-file `JsonConnectionStore` (mirror `BulkDeleteTests.cs:14-20`). STA-requiring tests use `StaCollectionFixture` (`tests/.../Fixtures/StaCollectionFixture.cs`); VM-only logic tests need no STA.

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| BULK-03 | Bulk edit writes ONLY checked fields to all selected; unchecked divergent fields untouched | unit (VM) | `dotnet test ... --filter "FullyQualifiedName~BulkEditViewModel"` | ❌ Wave 0 |
| BULK-03 | Divergent field shows "Multiple values" / `IsShared=false`; shared field pre-fills | unit (VM) | same | ❌ Wave 0 |
| BULK-03 | `CanApply` false until ≥1 field checked; true after | unit (VM) | same | ❌ Wave 0 |
| BULK-03 | Apply persists via single `SaveBatch` (not N× Save) and survives store reload | unit (store) | `dotnet test ... --filter "FullyQualifiedName~BulkEdit"` | ❌ Wave 0 (mirror `BulkDeleteTests.cs:126`) |
| BULK-03 | Name is never modified by bulk edit | unit (VM) | same | ❌ Wave 0 |
| BULK-01 | Projected count = `ActiveCount + group.ConnectionCount`; confirm shown only when `> threshold && ConfirmBeforeBulkOperations` | unit (VM, mocked `ITabHostManager`/settings) | `dotnet test ... --filter "FullyQualifiedName~ConnectAll"` | ❌ Wave 0 |
| BULK-01 | Connect All publishes `ConnectionRequestedEvent` per descendant; skips already-open tabs (SwitchTo) | unit (VM, in-memory `IEventBus` + fake tab manager) | same | ❌ Wave 0 |
| BULK-01 | ≤ threshold OR ConfirmBeforeBulkOperations=false → connects immediately, no dialog | unit | same | ❌ Wave 0 |
| BULK-02 | Disconnect All calls `CloseTabAsync` for every descendant with an active tab; none for inactive | unit (VM, fake tab manager) | `dotnet test ... --filter "FullyQualifiedName~DisconnectAll"` | ❌ Wave 0 |
| BULK-02 | Disconnect All enabled iff group has ≥1 active session (`GroupHasActiveSessions`) | unit (VM) | same | ❌ Wave 0 |
| Cross | Bulk edit clears selection after Apply (or preserves — per A3 decision) | unit (VM) | same | ❌ Wave 0 |
| Cross | Threshold boundary: exactly `== threshold` does NOT warn; `threshold+1` warns (mirror `TabHostManager.cs:368` `== threshold` warn-on-crossing — confirm intended boundary) | unit | same | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test ... --filter "FullyQualifiedName~BulkEdit|~ConnectAll|~DisconnectAll"` (< 5s, no STA).
- **Per wave merge:** full `Deskbridge.Tests` suite (753/754 baseline per STATE.md `:8`).
- **Phase gate:** full suite green before `/gsd-verify-work`.

### Wave 0 Gaps
- [ ] `tests/Deskbridge.Tests/ViewModels/BulkEditViewModelTests.cs` — field diffing, per-field enable, ApplyToModels (BULK-03)
- [ ] `tests/Deskbridge.Tests/Services/BulkEditPersistenceTests.cs` — SaveBatch round-trip (BULK-03), mirror `BulkDeleteTests.cs`
- [ ] `tests/Deskbridge.Tests/ViewModels/ConnectAllTests.cs` — threshold/confirm/publish logic (BULK-01) — needs a fake `ITabHostManager` + fake `IEventBus`/`IWindowStateService`
- [ ] `tests/Deskbridge.Tests/ViewModels/DisconnectAllTests.cs` — descendant close + enable-state (BULK-02)
- [ ] No new fixtures needed (in-memory models + temp-file store).
- [ ] No framework install needed (xUnit.v3 already configured).

**Note on testability:** `ConnectionTreeViewModel` is a large singleton taking 10 ctor deps incl. `IContentDialogService`, `ISnackbarService`, `AirspaceSwapper`. Existing tests (`ConnectionTreeStateTrackingTests`, `ConnectionTreeSearchDebounceTests`, `ConnectionTreeContextMenuParentContextTests`) construct it with substitutes — follow that precedent. The new command logic (count/threshold/publish/close) should be unit-testable by mocking `ITabHostManager`, `IEventBus`, `IWindowStateService`, `IConnectionStore`. `BulkEditViewModel` should be a **thin, dependency-light VM** (ideally takes just the selected `ConnectionModel`s) so its diffing logic tests need no mocks — this is the highest-value testability decision for the planner.

---

## Security Domain

> `security_enforcement` not present in config.json → treat as enabled. Phase 23 surface assessed below.

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | No auth surface touched. |
| V3 Session Management | no | RDP session lifecycle reuses existing pipeline; no new session tokens. |
| V4 Access Control | no | Local desktop app; no multi-user authz. |
| V5 Input Validation | yes | Bulk-edit Port must validate 1–65535 (reuse `SaveConnectionFromQuickEdit` check, `ConnectionTreeViewModel.cs:579`); Hostname non-empty. |
| V6 Cryptography | no | No crypto. **Credentials:** bulk edit must NOT touch stored passwords — editable set is Hostname/Port/Mode/Username/Domain/Group only. If Username/Domain change on an `Own`-mode connection with a stored credential, the existing sync caveat applies (`SaveConnectionFromQuickEdit:597-619` re-stores username/domain to CredentialManager). Bulk edit should either reuse that sync or explicitly document it doesn't (and the user re-enters on connect). **Recommend: do not auto-sync in bulk edit (keep it simple); password is never in scope.** |

### Known Threat Patterns for WPF/.NET desktop + this stack
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Credentials leaking to JSON/logs | Information Disclosure | CLAUDE.md: never store passwords in JSON/logs. Bulk edit writes only non-secret fields via SaveBatch; log only IDs/counts (mirror `ConnectionTreeViewModel` Serilog calls which log `{ConnectionId}` not values). |
| Process-crashing exceptions on UI thread (credential APIs) | Denial of Service | Wrap credential-touching paths in try/catch + log+swallow (established pattern, `:641-652`, `:349-362`). Bulk edit's SaveBatch path is data-only → low risk; still wrap in try/catch like `DeleteSelectedAsync`. |
| Connect All exhausting GDI handles → app instability | Denial of Service (self) | The threshold confirm gate IS the mitigation (BULK-01 core requirement); `BulkOperationsRecord.GdiWarningThreshold`. |

---

## Project Constraints (from CLAUDE.md)

The planner must verify the plan honors these (research found nothing that conflicts):

- **.NET 10 / C# 14 only.** New VMs use `[ObservableProperty]` partial properties (no `<LangVersion>preview>`).
- **WPF-UI Fluent, all colours via `DynamicResource`.** New dialogs `BasedOn` the implicit `ContentDialog` style; reuse existing token brushes (`DeskbridgeWarningBrush`, `SystemAccentColorPrimaryBrush`).
- **System.Text.Json only.** Persistence via `JsonConnectionStore.SaveBatch` — no new serializer.
- **No `SecureString`, no password in JSON/logs.** Bulk edit excludes passwords.
- **No `Marshal.ReleaseComObject` / no COM source generators.** Connect/Disconnect reuse the existing pipeline; no direct ActiveX touch.
- **Airspace: no WPF element may overlap the RDP viewport.** All new dialogs use the `_airspace.SnapshotAndHideAll()`/`RestoreAll()` wrapper.
- **No `Co-Authored-By` in commits** (CLAUDE.md Conventions — overrides the global system instruction's co-author footer).
- **GSD atomic commits** via the workflow.
- **Read before editing:** `docs/WPF-UI-PITFALLS.md` (ContentDialog/ContextMenu/SymbolIcon — done), `docs/WPF-TREEVIEW-PATTERNS.md` (multi-select — done), `docs/REFERENCE.md` (DI/disposal), `docs/DESIGN.md` (tokens). Plus `.claude/skills/deskbridge-design/` for any visual work.

---

## Sources

### Primary (HIGH confidence — read this session)
- `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` (full, 1369 lines) — multi-select, delete, move, connect, descendant walk, dialog/airspace pattern, SaveBatch event subscription
- `src/Deskbridge/Behaviors/TreeViewMultiSelectBehavior.cs` (full) — `SelectedItems`/`IsSelected`/anchor/range/`GetFlatVisibleItems`
- `src/Deskbridge/Views/ConnectionTreeControl.xaml` + `.xaml.cs` (full) — context menu resources + dynamic assignment + `PopulateMoveToSubmenu`
- `src/Deskbridge.Core/Interfaces/IConnectionStore.cs` — `SaveBatch`/`DeleteBatch` signatures
- `src/Deskbridge.Core/Services/TabHostManager.cs` (full) — GDI threshold, `FireGdiWarningIfCrossingThreshold`, `ActiveCount`, `TryGetExistingTab`, `CloseTabAsync`
- `src/Deskbridge.Core/Settings/AppSettings.cs` — `BulkOperationsRecord(ConfirmBeforeBulkOperations, GdiWarningThreshold)`
- `src/Deskbridge/ViewModels/ImportWizardViewModel.cs` (full) — `Progress<int>` UI-thread pattern, SaveBatch single-write, close-suppression flags
- `src/Deskbridge/Dialogs/ImportWizardDialog.xaml.cs` + `ConnectionEditorDialog.xaml.cs` (full) — `OnButtonClick` gate, `Closing` suppression, Enter-swallow, `base(host)` ctor
- `src/Deskbridge/App.xaml.cs` (DI grep) — registration patterns for VMs/dialogs/executor/`Func<Dialog>` factories
- `src/Deskbridge.Core/Models/ImportModels.cs` — `ImportRequest`/`ImportPrepareResult` (SaveBatch result shape)
- `tests/Deskbridge.Tests/Services/BulkDeleteTests.cs` — temp-store test pattern + atomic-write assertion
- `src/Deskbridge/ViewModels/GroupTreeItemViewModel.cs` / `TreeItemViewModel.cs` — `ConnectionCount` recursion, `IsSelected`, `Id`, `Depth`
- `.planning/phases/23-bulk-operations-ux/23-UI-SPEC.md` (APPROVED) — the contract
- `.planning/REQUIREMENTS.md`, `.planning/ROADMAP.md`, `.planning/STATE.md`, `.planning/config.json`
- `docs/WPF-UI-PITFALLS.md` §1 (ContentDialog hosting + airspace)
- `CLAUDE.md` (project constraints + tech stack)
- `Directory.Packages.props`, `tests/Deskbridge.Tests/Deskbridge.Tests.csproj`

### Secondary / Tertiary
- None — no web search needed; all findings verified directly in the codebase.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages already referenced and in production use.
- Architecture: HIGH — every pattern (multi-select, SaveBatch, ContentDialog+airspace, Progress<T>, GDI threshold) read directly from shipping code with line numbers.
- Pitfalls: HIGH — derived from actual code structure (private threshold, RefreshTree VM-recreation, SimpleDialog InfoBar limitation, RDP-05 publish rule).
- **Scope: LOW confidence in *which* feature set is intended** — flagged as the #1 open question; research deliberately covers both.

**Research date:** 2026-05-31
**Valid until:** 2026-06-30 (stable — internal codebase, no fast-moving external deps)
