---
phase: 23
plan: 02
subsystem: ui
tags: [bulk-operations, viewmodel, content-dialog, wpf-ui, tdd-green, BULK-03, BULK-01]
requires: [ConnectionModel, GroupDisplayItem, CredentialModeOption, BulkEditViewModelTests]
provides: [BulkEditViewModel, BulkEditField, BulkEditDialog, BulkConnectConfirmDialog]
affects: [src/Deskbridge/ViewModels, src/Deskbridge/Dialogs, tests/Deskbridge.Tests/ViewModels]
tech-stack:
  added: []
  patterns: [dependency-light-vm, per-field-diff, BulkEditField-wrapper, base-host-contentdialog, ComboBox-ItemTemplate-glyph-fix, OnButtonClick-validation-gate]
key-files:
  created:
    - src/Deskbridge/ViewModels/BulkEditViewModel.cs
    - src/Deskbridge/Dialogs/BulkEditDialog.xaml
    - src/Deskbridge/Dialogs/BulkEditDialog.xaml.cs
    - src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml
    - src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml.cs
  modified:
    - tests/Deskbridge.Tests/ViewModels/BulkEditViewModelTests.cs
decisions:
  - "BulkEditField<T> wrapper (IsShared/Value/IsEnabled/Placeholder) backs each editable field; the VM also exposes flat IsXxxEnabled proxies so XAML binds checkboxes and the CanApply gate has a single source of truth"
  - "Group list passed as an optional IReadOnlyList<GroupDisplayItem> ctor arg (not an injected store) to keep the VM dependency-light and mock-free per 23-VALIDATION.md"
  - "Divergent placeholder 'Multiple values' is produced by the VM (BulkEditField.Placeholder) and bound via PlaceholderText, not hardcoded per-row in XAML — one source of truth"
  - "BulkConnectConfirmDialog renders Warning24 as a standalone ui:SymbolIcon (WPF-UI InfoBar has no Icon setter); InfoBar Severity=Warning carries the canonical 'may degrade performance' copy"
metrics:
  duration: 13min
  completed: 2026-05-31
---

# Phase 23 Plan 02: Bulk Edit ViewModel + Dialogs Summary

Built the dependency-light `BulkEditViewModel` (the only novel logic in Phase 23) plus the two new `ui:ContentDialog`s — `BulkEditDialog` (BULK-03, 520px) and `BulkConnectConfirmDialog` (BULK-01, 420px) — and turned the 9 Wave-0 skipped behavior-pinning tests into real, passing tests (RED→GREEN). No tree-VM wiring or DI registration (that is plan 23-03).

## What Was Built

- **BulkEditViewModel.cs** (BULK-03) — A mock-free VM taking `IReadOnlyList<ConnectionModel>` (+ optional group list). Per editable field (Hostname, Port, CredentialMode, Username, Domain, GroupId — **Name excluded, password never in scope**) it builds a `BulkEditField<T>` by diffing the selection: shared (all equal) → pre-fills the value, placeholder empty; divergent (>1 distinct) → blank value, `Placeholder = "Multiple values"`. `CanApply` is the OR of the six per-field enable flags and recomputes when any flips. `ApplyToModels()` writes ONLY enabled fields to every selected model (never Name, never any credential secret) and stamps `UpdatedAt`. `Validate()` is pure/no-throw: rejects port outside 1..65535 and empty/whitespace hostname for ENABLED fields only.
- **BulkEditField<T>** — Small `ObservableObject` wrapper (`IsShared` / `Value` / `IsEnabled` / `Placeholder`) that the tests bind against and the dialog rows consume. Enabling a field calls back into the VM to raise `CanApply`.
- **BulkEditDialog.xaml + .xaml.cs** (BULK-03, 520px) — 3-column `Auto/72/*` grid, one row per editable field, each with a per-field enable checkbox (tooltip "Apply this field to all selected connections"), input `IsEnabled`-gated by the same flag, and divergent `PlaceholderText` bound from the VM. Credential mode and Group ComboBoxes use explicit `ItemTemplate`s to avoid the "- - -" SelectionBoxItem glyph trap. **No Name row.** `IsPrimaryButtonEnabled="{Binding CanApply}"`; `OnButtonClick` calls `vm.Validate()` and returns without `base.OnButtonClick` on failure (keeps the dialog open, shows a validation message). Enter-swallow `PreviewKeyDown` copied from `ConnectionEditorDialog`. Carries a collapsed `Couldn't apply changes` error `ui:InfoBar` + a `ShowSaveError(failed, total)` method for 23-03's SaveBatch-failure path.
- **BulkConnectConfirmDialog.xaml + .xaml.cs** (BULK-01, 420px) — `Connect all connections?` title, accent `PrimaryButtonAppearance="Primary"` (NOT destructive — these ops are reversible), `DefaultButton="Primary"`. Body sentence `This will open {N} sessions. {threshold}+ active sessions may degrade performance. Continue?` injected from ctor params, beside a Warning24 amber glyph, plus a `Severity="Warning"` `ui:InfoBar`. No validation gate.
- **BulkEditViewModelTests.cs** — All 9 `[Fact(Skip=...)]` placeholders un-skipped and given real Arrange/Act/Assert; added a reflection assertion that neither `ConnectionModel` nor `BulkEditViewModel` exposes any `Password` property (provable T-23-04 mitigation).

## Verification Results

- `dotnet build src/Deskbridge/Deskbridge.csproj` → exit 0, 0 warnings, 0 errors.
- `dotnet test ... --filter-query "/*/*/BulkEditViewModelTests/*"` → **Passed: 9, Failed: 0, Skipped: 0** (was 9 skipped).
- Acceptance greps all pass: `ApplyToModels`, `Validate`, `CanApply`, `IsHostnameEnabled`/`IsPortEnabled`/`IsGroupEnabled` present in the VM; no `Password` and no `Name =` write in the VM; `DialogMaxWidth="520"` + `PrimaryButtonText="Apply"` + `IsPrimaryButtonEnabled="{Binding CanApply}"` + `FIELDS TO EDIT` in BulkEditDialog.xaml with no Name TextBox row; `OnButtonClick` + `base(host)` in its code-behind; `DialogMaxWidth="420"` + `PrimaryButtonAppearance="Primary"` + `Warning24` + `may degrade performance` in BulkConnectConfirmDialog.xaml; `base(host)` in its code-behind.
- Threat mitigations verified: T-23-03 (Validate gate + OnButtonClick block) and T-23-04 (no Name/password write — grep + reflection test) implemented.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] WPF-UI InfoBar has no `Icon` setter**
- **Found during:** Task 2 build.
- **Issue:** The plan said to put a `Warning24` `SymbolIcon` on the confirm dialog's `ui:InfoBar`; WPF-UI 4.2 `InfoBar` exposes no `Icon` property (`MC3074: The tag 'InfoBar.Icon' does not exist`).
- **Fix:** Rendered `Warning24` as a standalone `ui:SymbolIcon` (amber `DeskbridgeWarningBrush`) in the dialog title row, and kept the `Severity="Warning"` `ui:InfoBar` (which draws its own built-in warning glyph) carrying the canonical "may degrade performance" copy. Satisfies the UI-SPEC verbatim and the `Warning24` acceptance grep.
- **Files modified:** src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml.
- **Commit:** e976ab6.

### Intentional Design Choices (not deviations)

- The 9 scaffold test bodies (authored in 23-01) reference a `XxxField.IsShared/Value/IsEnabled/Placeholder` shape rather than the flat `[ObservableProperty]` props the plan prose described. The locked test contract wins: the VM exposes `BulkEditField<T>` objects AND flat `IsXxxEnabled` proxy properties, satisfying both the tests and the plan's acceptance greps + XAML binding needs.
- The `Multiple values` literal lives in the VM (`BulkEditField.Placeholder`) and is bound into each row via `PlaceholderText`, not hardcoded per-row. The literal also appears in an explanatory comment in BulkEditDialog.xaml.

## Deferred Issues

- **Pre-existing flaky full-suite failures (out of scope).** The wave-boundary full suite (`dotnet test`) showed **763 passed / 11 skipped** with a *non-deterministic* failure count across identical re-runs (Failed: 1, then 2, then 18). The 11 skips are the remaining ConnectAll(8) + DisconnectAll(3) Wave-0 placeholders owned by 23-03. The variance proves STA/UI test-isolation flakiness (the suite has ~17 STA/`StaCollection` UI test files), consistent with the 23-01 baseline note of a pre-existing unrelated failure. This plan's changes are purely additive (new VM + new dialogs; no existing code paths touched) and the 9 new tests pass deterministically in isolation, so they cannot have caused the variance. Per the executor scope boundary, the flaky STA failures were not investigated or fixed. 23-03 should treat the deterministic green of `BulkEditViewModelTests` + the new ConnectAll/DisconnectAll tests as the gate, and run the STA suite enough times to confirm the baseline.

## TDD Gate Compliance

This is the GREEN phase for the BULK-03 VM contract. The RED commit (`test(...)`, 23-01: cc424c4) landed the 9 failing/skipped tests; this plan's `feat(23-02)` commit (592a9f5) adds the production `BulkEditViewModel` and un-skips them to green. No separate refactor commit was needed.

## Known Stubs

- `BulkEditDialog.SaveErrorInfoBar` / `ShowSaveError(...)` are intentionally inert in this plan (collapsed by default). They are the surface the tree VM toggles on SaveBatch failure in **plan 23-03** — documented, not unfinished work for 23-02 (this plan creates UI types only; it wires no persistence).

## Self-Check: PASSED

- FOUND: src/Deskbridge/ViewModels/BulkEditViewModel.cs
- FOUND: src/Deskbridge/Dialogs/BulkEditDialog.xaml
- FOUND: src/Deskbridge/Dialogs/BulkEditDialog.xaml.cs
- FOUND: src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml
- FOUND: src/Deskbridge/Dialogs/BulkConnectConfirmDialog.xaml.cs
- FOUND commit: 592a9f5 (Task 1 — BulkEditViewModel + GREEN tests)
- FOUND commit: e976ab6 (Task 2 — BulkEditDialog + BulkConnectConfirmDialog)
