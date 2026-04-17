# Phase 7: Update & Migration - Context

**Gathered:** 2026-04-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Two independent capabilities: (1) silent auto-update via Velopack + GitHub Actions, and (2) mRemoteNG connection import + JSON/CSV export.

Existing assets:
- `VelopackApp.Build().Run()` already in `Program.cs`
- Velopack NuGet package already referenced
- `UpdateAvailableEvent` + `ConnectionImportedEvent` declared in events, subscribed by `ToastSubscriptionService`
- `IConnectionStore` for adding imported connections
- `.github/workflows/build.yml` exists (needs `vpk pack` step)
- Command palette (CMD-02) supports canonical commands — export command slots in naturally

</domain>

<decisions>
## Implementation Decisions

### Update Notification UX
- **Toast + status bar badge** — on startup check, if update found, publish `UpdateAvailableEvent` (toast fires via existing subscription) AND set a persistent badge/dot on a status bar update icon so user can act later
- Toast copy: "Update v{version} available" with an action to trigger download
- Badge persists until user acts or dismisses

### Update Flow
- **One-click download + apply + restart WITH confirmation prompt** — clicking the update action triggers `UpdateManager.DownloadUpdatesAsync` with progress indicator, then shows a confirmation dialog: "Update downloaded. Restart now? Active sessions will be disconnected." User confirms → `ApplyUpdatesAndRestart`. User cancels → update applies on next manual restart.
- This protects against surprise session termination while giving a clear path to immediate update

### Import Wizard — Multi-Source Architecture
- **`IConnectionImporter` interface** — `ParseAsync(Stream) → ImportResult` returning a tree of connections
- **Phase 7 ships `MRemoteNGImporter` only** — parses confCons.xml (XML, not encrypted)
- Wizard flow: Step 1 = pick source app (dropdown — "mRemoteNG" is the only option for now, but the UI supports future additions like RoyalTS, RDCMan), Step 2 = file picker, Step 3 = **tree view preview** matching the source folder structure with checkboxes at folder + connection level, Step 4 = confirm
- Interface is designed for drop-in future importers without UI changes
- **Metadata only — no password import** (MIG-03). Users re-enter credentials after import.

### Import — Duplicate Handling
- **Prompt per duplicate** — when a connection with a matching hostname already exists, show per-item prompt: Skip / Overwrite / Rename (appended suffix). Tedious if many duplicates but gives full control.
- Summary toast after import: "Imported {N} connections from mRemoteNG. {M} skipped, {K} renamed."

### Import — Credential Target Format (STALE REQUIREMENT FIX)
- MIG-04 in REQUIREMENTS.md says "stored with TERMSRV/ credential prefix" — this is STALE. Quick task 260416-9wt changed the canonical target to `DESKBRIDGE/CONN/{connectionId}`. Imported connections use the new format. Update MIG-04 during planning.

### Export
- **Command palette only** — "Export connections as JSON" and "Export connections as CSV" as two palette commands. No menu bar, no context menu. Consistent with app's keyboard-first design.
- **JSON: tree structure** — nested JSON matching the folder hierarchy. Preserves what the user sees. No credentials in output.
- **CSV: flat** — one row per connection, folder path as a string column. Standard CSV for spreadsheet use. No credentials.

### CI/CD Release Pipeline
- **Git tag trigger** — push a tag matching `v*.*.*` → GitHub Actions workflow builds, runs `vpk pack`, creates a GitHub Release with assets (Setup.exe, RELEASES, nupkg, full/delta packages)
- **Two channels: stable + beta** — Velopack `--channel stable` and `--channel beta`. Tag format: `v1.0.0` for stable, `v1.1.0-beta.1` for beta (SemVer2 prerelease suffix). App checks the channel matching its current install. Users opt into beta via a Settings toggle.
- Workflow extends existing `build.yml` rather than creating a new file

### Claude's Discretion
- Status bar update icon design (which SymbolIcon, positioning)
- Import wizard dialog sizing and layout details
- CSV column ordering
- vpk pack flags beyond --channel (--framework, --runtime, etc.)
- Delta update support (Velopack handles this automatically if prior release exists)

</decisions>

<specifics>
## Specific Ideas

- Import wizard should be designed with multi-app import in mind — "Import from: [mRemoteNG ▾]" dropdown even though only one option ships in v1.0. This avoids a UI redesign when RoyalTS/RDCMan importers are added later.
- Update confirmation dialog must warn about active RDP sessions being disconnected on restart
- Beta channel is specifically useful for the user's work environment — test updates on work machine before pushing to stable

</specifics>

<canonical_refs>
## Canonical References

- `REFERENCE.md` — Architecture, DI registrations, Velopack constraints (custom Main, SemVer2)
- `DESIGN.md` — WPF-UI patterns for any new dialogs (import wizard, update confirmation)
- `WPF-UI-PITFALLS.md` — ContentDialog hosting for import wizard and update confirmation dialogs
- `.github/workflows/build.yml` — Existing CI pipeline to extend
- [Velopack docs](https://docs.velopack.io) — UpdateManager API, channel support, vpk pack flags
- [mRemoteNG confCons.xml schema](https://github.com/mRemoteNG/mRemoteNG) — XML structure for import parser

</canonical_refs>
