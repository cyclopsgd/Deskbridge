---
phase: 07-update-migration
plan: 04
subsystem: import-wizard
tags: [import, export, wizard, command-palette, mremoteng, duplicate-handling]
dependency_graph:
  requires:
    - "Plan 07-03 (IConnectionImporter, ImportModels, MRemoteNGImporter, ConnectionExporter)"
  provides:
    - "Deskbridge.ViewModels.ImportWizardViewModel (4-step wizard flow with tree preview and per-duplicate resolution)"
    - "Deskbridge.Dialogs.ImportWizardDialog (ContentDialog with 4-step navigation)"
    - "CommandPaletteService export-json and export-csv commands"
    - "MainWindow import/export menu entries in Settings panel"
    - "REQUIREMENTS.md MIG-04 updated from TERMSRV/ to DESKBRIDGE/CONN/{connectionId}"
  affects:
    - "src/Deskbridge/App.xaml.cs (DI: ImportWizardViewModel, ImportWizardDialog factory, IConnectionImporter → MRemoteNGImporter, command palette export commands)"
    - "src/Deskbridge/MainWindow.xaml (Settings panel import/export buttons)"
    - "src/Deskbridge/MainWindow.xaml.cs (import wizard launch, export handlers)"
    - "src/Deskbridge.Core/Services/CommandPaletteService.cs (export-json, export-csv canonical commands)"
    - ".planning/REQUIREMENTS.md (MIG-04 stale requirement fix)"
key-files:
  created:
    - "src/Deskbridge/ViewModels/ImportWizardViewModel.cs"
    - "src/Deskbridge/Dialogs/ImportWizardDialog.xaml"
    - "src/Deskbridge/Dialogs/ImportWizardDialog.xaml.cs"
    - "src/Deskbridge/Converters/NullToBoolConverter.cs"
  modified:
    - "src/Deskbridge/App.xaml.cs"
    - "src/Deskbridge/App.xaml"
    - "src/Deskbridge/MainWindow.xaml"
    - "src/Deskbridge/MainWindow.xaml.cs"
    - "src/Deskbridge.Core/Services/CommandPaletteService.cs"
    - "tests/Deskbridge.Tests/DiCompositionTests.cs"
    - ".planning/REQUIREMENTS.md"
metrics:
  duration_minutes: 8
  completed_date: "2026-04-17"
  tasks: 1
  files_created: 4
  files_modified: 7
  tests_added: 9
---

# Phase 7 Plan 04: Import Wizard + Export Commands Summary

**4-step ContentDialog import wizard (pick source → file picker → tree preview → confirm), per-duplicate resolution (Skip/Overwrite/Rename), command palette export commands (export-json, export-csv), DI wiring, and REQUIREMENTS.md MIG-04 stale reference fix.**

## What Was Built

### ImportWizardViewModel (4-step flow)
- Step 1: Source selection dropdown (mRemoteNG only in v1, extensible via IConnectionImporter)
- Step 2: File picker via OpenFileDialog, async parsing via selected importer
- Step 3: Tree preview of parsed connections with per-node checkboxes, folder-level select/deselect, duplicate detection + per-item DuplicateAction (Skip/Overwrite/Rename)
- Step 4: Confirmation with count summary, async import into IConnectionStore, ConnectionImportedEvent published, audit log entry

### ImportWizardDialog
- ContentDialog subclass with BasedOn style (WPF-UI Pitfall 1)
- 4-panel visibility-switching navigation (no wizard framework)
- IsFooterVisible with dynamic Primary/Close button text per step

### Command Palette Export
- `export-json` and `export-csv` canonical commands added to CommandPaletteService
- Commands call ConnectionExporter.ExportJson / ExportCsv via SaveFileDialog

### MIG-04 Fix
- REQUIREMENTS.md updated: "stored with TERMSRV/ credential prefix" → "stored with DESKBRIDGE/CONN/{connectionId} credential target"

## Self-Check: PASSED

- [x] ImportWizardViewModel.cs exists with 4-step flow
- [x] ImportWizardDialog.xaml + .cs exist
- [x] CommandPaletteService has export-json and export-csv commands
- [x] REQUIREMENTS.md MIG-04 updated
- [x] Build: 0 warnings, 0 errors
- [x] Tests: 534 passed, 0 failed, 3 skipped
