---
phase: 03-connection-management
plan: 03
subsystem: connection-editors
tags: [content-dialog, editor, credentials, infobar, credential-inheritance, di-registration]
dependency_graph:
  requires:
    - phase: 03-01
      provides: IConnectionStore-impl, ICredentialService-impl
  provides:
    - ConnectionEditorViewModel
    - GroupEditorViewModel
    - ConnectionEditorDialog
    - GroupEditorDialog
    - GroupDisplayItem-record
    - DI-registrations-all-4-editor-types
  affects: [03-04-tree-interactions, App.xaml.cs-DI]
tech_stack:
  added: []
  patterns: [content-dialog-code-behind-password, observable-validator-dataannotations, credential-mode-switching, group-chain-walk]
key_files:
  created:
    - src/Deskbridge/ViewModels/ConnectionEditorViewModel.cs
    - src/Deskbridge/ViewModels/GroupEditorViewModel.cs
    - src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml
    - src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml.cs
    - src/Deskbridge/Dialogs/GroupEditorDialog.xaml
    - src/Deskbridge/Dialogs/GroupEditorDialog.xaml.cs
  modified:
    - src/Deskbridge/App.xaml.cs
decisions:
  - "IsNewConnection/IsNewGroup use get;set instead of get;init because Initialize() sets them after construction (DI creates instances before initialization)"
  - "GroupDisplayItem record placed in ConnectionEditorViewModel.cs file for shared use by both editor ViewModels"
  - "Dictionary<string, List<ConnectionGroup>> used instead of Dictionary<Guid?, ...> to avoid nullable key notnull constraint"
  - "ui:TextBox used for Notes tab to support PlaceholderText (standard WPF TextBox lacks this property)"
  - "ui:ToggleSwitch requires ui: prefix (not auto-styled by ControlsDictionary like standard WPF controls)"
metrics:
  duration: 5min
  completed: 2026-04-11T18:08:10Z
  tasks: 2
  files: 7
---

# Phase 03 Plan 03: Connection & Group Editor Dialogs Summary

ContentDialog-based editors with 4-tab connection editor (General/Credentials/Display/Notes), credential mode switching with InfoBar inheritance indicator (CONN-09), single-panel group editor with credentials section and dynamic inheritance count, PasswordBox code-behind pattern, and DI registrations for all 4 editor types.

## What Was Built

### Task 1: ConnectionEditorDialog with 4-tab Editor and Credential InfoBar

**ConnectionEditorViewModel** (`src/Deskbridge/ViewModels/ConnectionEditorViewModel.cs`):
- Extends `ObservableValidator` for DataAnnotation-based validation (`[Required]` on Name, Hostname)
- General tab: Name, Hostname, Port (default 3389), GroupId with AvailableGroups populated depth-first from IConnectionStore
- Credentials tab: CredentialMode with `OnCredentialModeChanged` handler, computed booleans (IsCredentialFieldsEnabled, IsCredentialFieldsVisible, IsInheritInfoBarVisible, IsPromptInfoBarVisible)
- `InheritedFromMessage` computed by walking group chain via ICredentialService.ResolveInherited + IConnectionStore.GetGroupById to find providing group name (CONN-09)
- Display tab: DisplayWidth, DisplayHeight (nullable int), SmartSizing (bool, default true)
- Notes tab: Notes string
- `SetPassword(string)` receives password from code-behind PasswordBox (T-03-09: never in ViewModel binding)
- `Validate()` calls ValidateAllProperties, checks Hostname non-empty, Port 1-65535
- `Save()` creates/updates ConnectionModel, stores credentials via ICredentialService.StoreForConnection when mode=Own and password non-empty, persists via IConnectionStore.Save
- `Initialize(ConnectionModel?)` loads existing values for edit mode, populates groups, computes inherited message
- `GroupDisplayItem` record: shared by both editor ViewModels for depth-indented group display

**ConnectionEditorDialog** (`src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml`):
- Extends `ui:ContentDialog` with BasedOn style for WPF-UI theming
- DialogMaxWidth="560", PrimaryButtonText="Save", CloseButtonText="Cancel"
- 4 TabItems: General, Credentials, Display, Notes
- Credentials tab: ComboBox with 3 ComboBoxItems using Tag for CredentialMode enum binding
- Two InfoBars: Inherited (Severity=Informational, CONN-09) and Prompt (Severity=Warning)
- Credential fields StackPanel with BoolToVisibility converter, IsEnabled binding for Own mode
- PasswordBox with x:Name="PasswordBox" (NOT data-bound per Pitfall 5)
- Display tab: side-by-side Width/Height TextBoxes with 8px gap, helper text, ToggleSwitch
- Notes tab: ui:TextBox with AcceptsReturn, TextWrapping, PlaceholderText

**ConnectionEditorDialog.xaml.cs**:
- Constructor takes ContentDialogHost + ConnectionEditorViewModel, sets DataContext
- OnButtonClick override: passes PasswordBox.Password via SetPassword(), validates, prevents close on failure

### Task 2: GroupEditorDialog with Credentials Section and DI Registrations

**GroupEditorViewModel** (`src/Deskbridge/ViewModels/GroupEditorViewModel.cs`):
- Extends `ObservableValidator` with `[Required]` Name
- ParentGroupId with AvailableParentGroups that excludes current group and descendants (prevents self-parenting, T-03-11)
- CredentialUsername, CredentialDomain with ShowInheritanceCount computed from username presence
- InheritanceCount: walks all connections in IConnectionStore, counts those with CredentialMode=Inherit whose group chain includes this group
- `Save()`: stores group credentials via ICredentialService.StoreForGroup when username non-empty, deletes via DeleteForGroup when username cleared (was previously set)
- `Initialize(ConnectionGroup?)` loads existing values, credential from GetForGroup, computes inheritance count

**GroupEditorDialog** (`src/Deskbridge/Dialogs/GroupEditorDialog.xaml`):
- Extends `ui:ContentDialog` with BasedOn style
- DialogMaxWidth="480" (narrower than connection editor, no tabs per D-12)
- Single StackPanel: Group Name, Parent Group ComboBox, separator Border, "Group Credentials" header, helper text, Username/Domain TextBoxes, PasswordBox, inheritance count TextBlock
- PasswordBox with x:Name="GroupPasswordBox"
- Inheritance count shown only when ShowInheritanceCount=true via BoolToVisibility

**GroupEditorDialog.xaml.cs**:
- Same pattern as ConnectionEditorDialog: ContentDialogHost constructor, SetPassword on primary click, validation gate

**DI Registrations** (`src/Deskbridge/App.xaml.cs`):
- Added 4 transient registrations: ConnectionEditorViewModel, GroupEditorViewModel, ConnectionEditorDialog, GroupEditorDialog
- Required for Plan 04 which resolves editor dialogs via IServiceProvider.GetRequiredService<T>()

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] ToggleSwitch requires ui: prefix**
- **Found during:** Task 1
- **Issue:** `ToggleSwitch` is a WPF-UI control, not auto-styled standard WPF. XAML compiler error MC3074.
- **Fix:** Changed `<ToggleSwitch>` to `<ui:ToggleSwitch>`
- **Files modified:** `src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml`

**2. [Rule 1 - Bug] Standard WPF TextBox lacks PlaceholderText**
- **Found during:** Task 1
- **Issue:** Notes tab used `<TextBox PlaceholderText=...>` but standard WPF TextBox doesn't have PlaceholderText. XAML compiler error MC3072.
- **Fix:** Changed to `<ui:TextBox>` which supports PlaceholderText
- **Files modified:** `src/Deskbridge/Dialogs/ConnectionEditorDialog.xaml`

**3. [Rule 1 - Bug] IsNewConnection init-only property assigned in Initialize()**
- **Found during:** Task 1
- **Issue:** Property declared as `{ get; init; }` but `Initialize()` method sets it after construction (DI creates the instance, then caller initializes). CS8852 error.
- **Fix:** Changed to `{ get; set; }` for both IsNewConnection and IsNewGroup
- **Files modified:** `src/Deskbridge/ViewModels/ConnectionEditorViewModel.cs`

**4. [Rule 1 - Bug] Dictionary<Guid?, ...> violates notnull constraint**
- **Found during:** Task 1
- **Issue:** `Dictionary<Guid?, List<ConnectionGroup>>` causes CS8714 because nullable Guid doesn't satisfy the notnull constraint on TKey.
- **Fix:** Used `Dictionary<string, ...>` with `parentId?.ToString() ?? string.Empty` as key
- **Files modified:** `src/Deskbridge/ViewModels/ConnectionEditorViewModel.cs`, `src/Deskbridge/ViewModels/GroupEditorViewModel.cs`

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build src/Deskbridge/Deskbridge.csproj` | 0 errors, 0 warnings |
| `dotnet test tests/Deskbridge.Tests/` | 83 passed, 0 failed, 0 skipped |
| ConnectionEditorDialog 4 tabs | General, Credentials, Display, Notes |
| Credential InfoBar (CONN-09) | IsInheritInfoBarVisible=true when Inherit, shows group name |
| Prompt InfoBar | IsPromptInfoBarVisible=true when Prompt, Severity=Warning |
| Credential mode switching | Inherit=InfoBar+disabled, Own=enabled, Prompt=hidden+warning |
| PasswordBox code-behind | SetPassword() called in OnButtonClick, never data-bound |
| GroupEditorDialog single panel | No tabs, DialogMaxWidth=480, separator, helper text |
| Inheritance count | Computed via group chain walk, shown when credentials set |
| DI registrations | All 4 types registered as transient in App.xaml.cs |

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 | e7b2465 | feat(03-03): build ConnectionEditorDialog with 4-tab editor and credential InfoBar |
| 2 | 06958f9 | feat(03-03): build GroupEditorDialog with credentials section and register all editor types in DI |

## Self-Check: PASSED

All 7 created/modified files verified on disk. Both commit hashes (e7b2465, 06958f9) found in git log.

---
*Phase: 03-connection-management*
*Completed: 2026-04-11*
