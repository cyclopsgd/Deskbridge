# Phase 23: Bulk Operations UX - Pattern Map

**Mapped:** 2026-05-31
**Files analyzed:** 12 (5 new, 3 modified, 4 new test files)
**Analogs found:** 12 / 12 (every file has a strong in-repo analog — this is a reuse-heavy phase)

> **SCOPE (authoritative, from approved 23-UI-SPEC.md):** Connect All (BULK-01), Disconnect All (BULK-02), Bulk Edit (BULK-03).
> Bulk **delete** and **move** already ship (`DeleteSelectedAsync`, `MoveToGroup`) and are OUT of scope — do not map as new work.
>
> **PATH NOTE:** The main app project is `src/Deskbridge/` (not `src/Deskbridge.App/`). All file paths and the line numbers cited below are against `src/Deskbridge/...` exactly as verified in 23-RESEARCH.md. The orchestrator's expected-files list used `Deskbridge.App/Controls/ConnectionTree/...` paths that do not exist; the real locations are corrected here.

---

## File Classification

| New/Modified File (real path) | Role | Data Flow | Closest Analog | Match Quality |
|-------------------------------|------|-----------|----------------|---------------|
| `src/Deskbridge/Dialogs/BulkEditDialog.xaml` (NEW) | dialog (view) | request-response (form) | `src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml` | exact (ContentDialog form) |
| `src/Deskbridge/Dialogs/BulkEditDialog.xaml.cs` (NEW) | dialog code-behind | request-response | `src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml.cs` | exact (OnButtonClick gate + base(host) ctor) |
| `src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml` + `.xaml.cs` (NEW) | dialog (confirm) | request-response | `ConnectionEditorDialog.xaml.cs` (base(host) ctor) + InfoBar usage | role-match (no existing custom confirm-with-InfoBar) |
| `src/Deskbridge/ViewModels/BulkEditViewModel.cs` (NEW) | view-model | transform (field diff) | `src/Deskbridge/ViewModels/ConnectionEditorViewModel.cs` + `GroupEditorViewModel.cs` | role-match (thin VM; no exact field-diff analog) |
| `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` (MODIFIED) | view-model (commands) | event-driven (publish) + CRUD (SaveBatch) | self — existing `DeleteSelectedAsync` / `MoveToGroup` / `ConnectCommand` | exact (same file, copy sibling commands) |
| `src/Deskbridge/Views/ConnectionTreeControl.xaml` (MODIFIED) | view (context menus) | request-response | self — existing `GroupContextMenu` / `MultiSelectContextMenu` items | exact |
| `src/Deskbridge/Views/ConnectionTreeControl.xaml.cs` (MODIFIED) | view code-behind | request-response | self — existing `PopulateMoveToSubmenu` + right-click handler | exact |
| `src/Deskbridge/App.xaml.cs` (MODIFIED) | composition root (config) | n/a | self — existing dialog/VM registrations | exact |
| `tests/Deskbridge.Tests/ViewModels/BulkEditViewModelTests.cs` (NEW) | test (unit, VM) | transform | `tests/.../ViewModels/ConnectionTreeStateTrackingTests.cs` (VM-with-substitutes) | role-match |
| `tests/Deskbridge.Tests/Services/BulkEditPersistenceTests.cs` (NEW) | test (unit, store) | CRUD | `tests/Deskbridge.Tests/Services/BulkDeleteTests.cs` | exact (temp-store round-trip) |
| `tests/Deskbridge.Tests/ViewModels/ConnectAllTests.cs` (NEW) | test (unit, VM) | event-driven | `ConnectionTreeStateTrackingTests.cs` + `SwitchToExistingTabTests.cs` | role-match |
| `tests/Deskbridge.Tests/ViewModels/DisconnectAllTests.cs` (NEW) | test (unit, VM) | event-driven | `ConnectionTreeStateTrackingTests.cs` | role-match |

---

## Pattern Assignments

### `src/Deskbridge/ViewModels/ConnectionTreeViewModel.cs` (MODIFIED — controller/VM, event-driven + CRUD)

**Analog:** self. This 1369-line singleton already contains the closest possible patterns for all three commands. Copy the *shape* of existing siblings; do NOT re-implement delete/move.

**Descendant-walk pattern** — copy the recursion shape of `CloseTabsForGroupDescendants` (`ConnectionTreeViewModel.cs:1036-1050`) into a new `CollectDescendantConnections(GroupTreeItemViewModel)`:
```csharp
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
For the GDI count, prefer the existing recursive `GroupTreeItemViewModel.ConnectionCount` (`GroupTreeItemViewModel.cs:20-31`) over re-counting.

**Connect All command** — model on existing `ConnectCommand`/connect publish (`ConnectionTreeViewModel.cs:1058-1083`). RDP-05: publish `ConnectionRequestedEvent`, never call the pipeline directly. Skip already-open tabs with `_tabHostManager.TryGetExistingTab(...)` → `SwitchTo`:
```csharp
[RelayCommand]
private async Task ConnectAllAsync(GroupTreeItemViewModel? group)
{
    if (group is null || _isDialogOpen) return;
    var conns = CollectDescendantConnections(group);
    if (conns.Count == 0) return;
    int projected = _tabHostManager.ActiveCount + conns.Count;   // ActiveCount: ITabHostManager
    var bulk = (await _windowState.LoadAsync())?.BulkOperations ?? BulkOperationsRecord.Default;
    if (bulk.ConfirmBeforeBulkOperations && projected > bulk.GdiWarningThreshold)
    {
        // airspace-wrapped BulkConnectConfirmDialog, _isDialogOpen guarded; bail on non-Primary
    }
    foreach (var c in conns)
    {
        var model = _connectionStore.GetById(c.Id);
        if (model is null) continue;
        if (_tabHostManager.TryGetExistingTab(model.Id, out _)) { _tabHostManager.SwitchTo(model.Id); continue; }
        _eventBus.Publish(new ConnectionRequestedEvent(model));
    }
}
```

**Disconnect All command** — reuse the descendant walk + the active-tab close loop from `CloseTabsForGroupDescendants` (`:1036-1050`). No confirm, no persistence, no RefreshTree:
```csharp
[RelayCommand]
private async Task DisconnectAllAsync(GroupTreeItemViewModel? group)
{
    if (group is null) return;
    foreach (var c in CollectDescendantConnections(group))
        if (_tabHostManager.TryGetExistingTab(c.Id, out _))
            await _tabHostManager.CloseTabAsync(c.Id);
}
```
Plus a public helper for the code-behind enable-gate:
```csharp
public bool GroupHasActiveSessions(GroupTreeItemViewModel group) =>
    CollectDescendantConnections(group).Any(c => _tabHostManager.TryGetExistingTab(c.Id, out _));
```

**Bulk Edit command** — model on `DeleteSelectedAsync` (`:918-1030`) for the dialog-open + try/catch + selection-clear scaffold, and on the SaveBatch single-write from `ImportWizardViewModel` (see below).

**Dialog open + airspace wrap (copy VERBATIM)** — every dialog open in this file does this (pattern at `ConnectionTreeViewModel.cs:743-759`), guarded by `_isDialogOpen` (field `:45`, used `:720-768`):
```csharp
_airspace.SnapshotAndHideAll();
try { var result = await dialog.ShowAsync(); /* ... */ }
finally { _airspace.RestoreAll(); }
```

**SaveBatch + event publish (copy from ImportWizardViewModel)** — see Shared Patterns. After Apply, clear selection mirroring `DeleteSelectedAsync:1027-1028` (`SelectedItems.Clear(); PrimarySelectedItem = null;`).

**New ctor dependency:** inject `IWindowStateService` so Connect All can read `settings.BulkOperations.GdiWarningThreshold` / `.ConfirmBeforeBulkOperations` (Pitfall 1; same read shape as `TabHostManager.cs:73-75`). Existing ctor already takes ~10 deps incl. `IConnectionStore`, `ITabHostManager`, `IEventBus`, `IContentDialogService`, `AirspaceSwapper` — add one more.

---

### `src/Deskbridge/Views/ConnectionTreeControl.xaml` (MODIFIED — view, context menus)

**Analog:** self — existing menu items in `GroupContextMenu` and `MultiSelectContextMenu` resources (rooted in `UserControl.Resources`).

**Menu item pattern (copy existing New Connection item, ~`ConnectionTreeControl.xaml:66-73`):** bind `Command` against the VM (set as `menu.DataContext` in code-behind) and pass the right-clicked group via `PlacementTarget.DataContext`. Do NOT use `RelativeSource AncestorType=TreeView` (menus aren't in the TreeView tree):
```xml
<!-- Connect All — GroupContextMenu, above New Connection/New Group, after a Separator -->
<MenuItem Header="Connect All"
          Icon="{ui:SymbolIcon Play24}"
          Command="{Binding ConnectAllCommand}"
          CommandParameter="{Binding PlacementTarget.DataContext,
              RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}}" />
<!-- Disconnect All — GroupContextMenu; IsEnabled set imperatively in code-behind -->
<MenuItem Header="Disconnect All" x:Name="DisconnectAllMenuItem"
          Icon="{ui:SymbolIcon PlugDisconnected24}"
          Command="{Binding DisconnectAllCommand}"
          CommandParameter="{Binding PlacementTarget.DataContext,
              RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}}" />
<!-- Edit… — MultiSelectContextMenu, top, above "Move to…" -->
<MenuItem Header="Edit…"
          Icon="{ui:SymbolIcon Edit24}"
          Command="{Binding EditSelectedCommand}" />
```
Icons/copy locked by UI-SPEC: `Play24` / `PlugDisconnected24` / `Edit24`; ellipsis on `Edit…` only. No Danger styling (non-destructive).

---

### `src/Deskbridge/Views/ConnectionTreeControl.xaml.cs` (MODIFIED — view code-behind)

**Analog:** self — `TreeView_PreviewMouseRightButtonDown` (sets `menu.DataContext = _viewModel`, `~:248-274`) + `PopulateMoveToSubmenu` (dynamic per-item enable, `~:283-340`, disables items at `:312`/`:333`).

**Disconnect All enable-gate (Pitfall 6):** native `MenuItem.IsEnabled` won't re-evaluate `CanExecute` reliably for dynamically-assigned context menus. Set it imperatively while building the menu, exactly like `PopulateMoveToSubmenu` disables items:
```csharp
// inside the GroupContextMenu branch of TreeView_PreviewMouseRightButtonDown
if (DisconnectAllMenuItem is not null && menu.PlacementTarget is FrameworkElement fe
    && fe.DataContext is GroupTreeItemViewModel g)
    DisconnectAllMenuItem.IsEnabled = _viewModel.GroupHasActiveSessions(g);
```

---

### `src/Deskbridge/Dialogs/BulkEditDialog.xaml` + `.xaml.cs` (NEW — dialog, request-response form)

**Analog:** `src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml` (form layout, ComboBox `ItemTemplate` to avoid the "- - -" SelectionBoxItem glyph trap) and `GroupEditorDialog.xaml` (smaller single-purpose dialog).

**XAML (mirror ConnectionEditorDialog header + GroupEditorDialog scale):**
- `ui:ContentDialog` with `DialogMaxWidth="520"`, `Title="Edit {N} connections"`, `PrimaryButtonText="Apply"`, `CloseButtonText="Cancel"`, `IsPrimaryButtonEnabled="{Binding CanApply}"`, `BasedOn` the implicit ContentDialog style.
- Field rows: `Grid` with 3 columns `Auto` (CheckBox) / `72` (label) / `*` (input) — mirrors the quick-properties `72`/`*` grid plus a checkbox column.
- Inputs: `ui:TextBox` (Hostname/Port/Username/Domain), `ComboBox` w/ explicit `ItemTemplate` (Credential mode, Group). `PlaceholderText="Multiple values"` on divergent fields.
- Per-field `CheckBox.IsChecked` two-way to a VM `IsXxxEnabled` flag; the row's input `IsEnabled` bound to the same flag. CheckBox tooltip `Apply this field to all selected connections`.
- Error: `ui:InfoBar Severity="Error"` (title `Couldn't apply changes`), reuse `ErrorTextStyle`/`SectionLabelStyle`/`FieldLabelCompactStyle`/`DialogContentMargin`/`FormFieldSpacing`.

**Code-behind (copy ConnectionEditorDialog.xaml.cs structure):**
- ctor takes the `ContentDialogHost` and calls `base(host)` (analog: `ConnectionEditorDialog` ctor pattern), plus the `BulkEditViewModel`.
- Override `OnButtonClick`: on Primary, run `vm.Validate()` (port range 1–65535 like `SaveConnectionFromQuickEdit:579`, non-empty hostname when that field is enabled); on failure `return` WITHOUT calling `base.OnButtonClick` to keep the dialog open. (Analog: ConnectionEditorDialog `OnButtonClick` validation gate.)
- `Dialog_PreviewKeyDown` Enter-swallow in text inputs (Pitfall S1; analog: ConnectionEditorDialog `PreviewKeyDown`).

---

### `src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml` + `.xaml.cs` (NEW — confirm dialog with Warning InfoBar)

**Analog:** `ConnectionEditorDialog.xaml.cs` for the `base(host)` ctor; InfoBar usage borrowed from existing dialog XAML. (Option B in research — `ShowSimpleDialogAsync` CANNOT host the `ui:InfoBar` the UI-SPEC requires; Pitfall 4.)

**XAML:** `ui:ContentDialog` `DialogMaxWidth="420"`, `Title="Connect all connections?"`, `PrimaryButtonText="Connect All"` with `PrimaryButtonAppearance="Primary"` (accent, NOT Danger), `CloseButtonText="Cancel"`, `DefaultButton="Primary"`. Body = the sentence (`This will open {N} sessions. {threshold}+ active sessions may degrade performance. Continue?` — mirror the shipped phrasing from `TabHostManager.cs:373-378`) + `ui:InfoBar Severity="Warning"` with `Warning24`.
**Code-behind:** ctor `base(host)` + properties/ctor params for `{N}` and `{threshold}`. No validation gate needed.

---

### `src/Deskbridge/ViewModels/BulkEditViewModel.cs` (NEW — thin VM, transform/field-diff)

**Analog:** `ConnectionEditorViewModel.cs` / `GroupEditorViewModel.cs` for `[ObservableProperty]` partial-property + `[RelayCommand]` conventions. No exact field-diff analog exists — this is the genuinely new logic. Keep it **dependency-light** (take just the selected `ConnectionModel`s) so tests need no mocks (research's highest-value testability call).

**Shape:**
- Ctor: `BulkEditViewModel(IReadOnlyList<ConnectionModel> selected)`.
- Per editable field (Hostname, Port, CredentialMode, Username, Domain, GroupId — Name EXCLUDED): an `[ObservableProperty]` value + an `[ObservableProperty] bool IsXxxEnabled`. Compute `IsShared = selected.Select(f).Distinct().Count() == 1`; shared → pre-fill value (checkbox unchecked), divergent → blank + `Multiple values` placeholder (checkbox unchecked). (Pitfall 2.)
- `CanApply` = any `IsXxxEnabled` true (drives `IsPrimaryButtonEnabled`); raise change notification when any enable flag flips.
- `ApplyToModels()` returns `List<ConnectionModel>` = each selected model with ONLY the checked fields overwritten; never touches Name or passwords. Group field applies to connections only (no cycle check needed — connections have no descendants).
- `Validate()` pure method (no throw on apply): port 1–65535 if Port enabled; hostname non-empty if Hostname enabled.

Field names verified against `ConnectionModel.cs` (Hostname, Port, CredentialMode/Mode, Username, Domain, GroupId, Name).

---

### `src/Deskbridge/App.xaml.cs` (MODIFIED — composition root)

**Analog:** self — existing dialog `Func<...>` factory + VM registrations and the `RootContentDialog`/`GetDialogHostEx()` host lookup used by the other dialogs.
- Register `BulkConnectConfirmDialog` and `BulkEditDialog` transient (mirror `ConnectionEditorDialog`/`ImportWizardDialog` registration).
- Register `BulkEditViewModel` transient (or construct inline in the tree VM since it only needs the selection — research recommends keeping it dependency-light).
- Ensure `IWindowStateService` is resolvable into `ConnectionTreeViewModel`'s ctor (already registered; just add the parameter to the existing tree-VM registration).

---

### Test files (NEW)

| New test | Analog | Copy what |
|----------|--------|-----------|
| `tests/.../Services/BulkEditPersistenceTests.cs` | `tests/Deskbridge.Tests/Services/BulkDeleteTests.cs` | temp-file `JsonConnectionStore` setup (`BulkDeleteTests.cs:14-20`), `SaveBatch` round-trip + reload assertion (`:126`), in-memory `ConnectionModel`/`ConnectionGroup` construction. No XML fixtures, no STA. |
| `tests/.../ViewModels/BulkEditViewModelTests.cs` | `ConnectionTreeStateTrackingTests.cs` (VM-with-substitutes precedent) | Construct the thin `BulkEditViewModel` directly from in-memory models (no mocks). Assert: divergent → `IsShared=false` + placeholder; shared → pre-filled; `CanApply` toggles on enable; `ApplyToModels` writes only checked fields; Name never modified. |
| `tests/.../ViewModels/ConnectAllTests.cs` | `ConnectionTreeStateTrackingTests.cs` + `SwitchToExistingTabTests.cs` | Build `ConnectionTreeViewModel` with substitutes for `ITabHostManager`/`IEventBus`/`IWindowStateService`/`IConnectionStore`. Assert: projected count = `ActiveCount + group.ConnectionCount`; confirm only when `> threshold && ConfirmBeforeBulkOperations`; publishes `ConnectionRequestedEvent` per descendant; skips open tabs via `SwitchTo`; boundary `==threshold` no warn / `+1` warns. |
| `tests/.../ViewModels/DisconnectAllTests.cs` | `ConnectionTreeStateTrackingTests.cs` | Fake `ITabHostManager`; assert `CloseTabAsync` called for active descendants only, and `GroupHasActiveSessions` gating. |

Framework: xUnit.v3 + FluentAssertions + NSubstitute (already configured). Quick filter: `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj --filter "FullyQualifiedName~BulkEdit|FullyQualifiedName~ConnectAll|FullyQualifiedName~DisconnectAll"`.

---

## Shared Patterns

### Atomic batch persistence (Bulk Edit only)
**Source:** `IConnectionStore.SaveBatch` (`src/Deskbridge.Core/Interfaces/IConnectionStore.cs`); usage at `ImportWizardViewModel.cs:441-442`.
**Apply to:** `ConnectionTreeViewModel.EditSelectedAsync`.
```csharp
_connectionStore.SaveBatch(edited, []);              // single atomic .tmp-rename write; groups unchanged
_eventBus.Publish(new ConnectionDataChangedEvent()); // → OnDataChanged (:402-405) → RefreshTree on UI thread
```
All-or-nothing: build the full `edited` list first; wrap in try/catch like `DeleteSelectedAsync:982-1025`; on failure show error InfoBar, persist nothing. NEVER call `Save` in a loop (IMP-04).

### ContentDialog hosting + airspace wrap
**Source:** `ConnectionTreeViewModel.cs:743-759` (`SnapshotAndHideAll`/`RestoreAll`) + `_isDialogOpen` guard (`:45`, `:720-768`); host via `IContentDialogService` + `base(host)` ctor (analog: `ConnectionEditorDialog.xaml.cs`).
**Apply to:** every `ShowAsync` in this phase (`BulkConnectConfirmDialog`, `BulkEditDialog`). Mandatory per CLAUDE.md airspace constraint + `docs/WPF-UI-PITFALLS.md` §1.

### GDI threshold + canonical warning copy
**Source:** `BulkOperationsRecord.GdiWarningThreshold` / `.ConfirmBeforeBulkOperations` in `src/Deskbridge.Core/Settings/AppSettings.cs`; warning string from `TabHostManager.cs:373-378`. Read via injected `IWindowStateService` (same shape as `TabHostManager.cs:73-75`).
**Apply to:** Connect All only. Never hardcode `15`.

### Context-menu command wiring
**Source:** existing items in `ConnectionTreeControl.xaml` + `menu.DataContext=_viewModel` (`ConnectionTreeControl.xaml.cs:~248-274`). Use `PlacementTarget.DataContext`, never `AncestorType=TreeView`.
**Apply to:** all three new menu items.

### Validation gate + Enter-swallow on dialogs
**Source:** `ConnectionEditorDialog.xaml.cs` `OnButtonClick` (return-without-base on failure) + `PreviewKeyDown` Enter-swallow; port range like `SaveConnectionFromQuickEdit` (`ConnectionTreeViewModel.cs:579`).
**Apply to:** `BulkEditDialog.xaml.cs`.

---

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `BulkEditViewModel.cs` field-diff logic | view-model | transform | No existing VM diffs shared/divergent values across a selection. The VM *skeleton* (`[ObservableProperty]`/`[RelayCommand]`) copies `ConnectionEditorViewModel`/`GroupEditorViewModel`, but the per-field `IsShared`/enable/`ApplyToModels` logic is genuinely new (only ~1 of 12 files). |
| `BulkConnectConfirmDialog` InfoBar confirm | dialog | request-response | No existing custom `ContentDialog` confirm that hosts a `ui:InfoBar` (existing confirms use `ShowSimpleDialogAsync`, which can't). Ctor pattern from `ConnectionEditorDialog`; InfoBar markup from existing dialog XAML. |

---

## Metadata

**Analog search scope:** `src/Deskbridge/Dialogs/`, `src/Deskbridge/ViewModels/`, `src/Deskbridge/Views/`, `src/Deskbridge.Core/Interfaces|Settings|Models|Services/`, `tests/Deskbridge.Tests/Services|ViewModels/`.
**Files scanned:** ~140 source files + ~90 test files (git ls-files).
**Key correction:** orchestrator's `src/Deskbridge.App/Controls/ConnectionTree/...` paths do not exist; real paths are `src/Deskbridge/...` (Dialogs/, ViewModels/, Views/). Line numbers verified in 23-RESEARCH.md against the real paths.
**Pattern extraction date:** 2026-05-31
