---
phase: 07-update-migration
plan: 03
subsystem: import-export
tags: [xml-parsing, csv-export, json-export, mremoteng, migration]

# Dependency graph
requires:
  - phase: 01-foundation
    provides: ConnectionModel, ConnectionGroup, Protocol enum, IConnectionStore
provides:
  - IConnectionImporter interface for multi-source import architecture
  - MRemoteNGImporter confCons.xml parser with XXE prevention
  - ImportResult/ImportedNode/ImportNodeType models for import tree
  - ConnectionExporter with JSON tree and CSV flat export
  - 26 unit tests covering import parsing and export formatting
affects: [07-04-import-wizard-ui]

# Tech tracking
tech-stack:
  added: []
  patterns: [IConnectionImporter multi-source interface, string-keyed nullable Guid dictionaries, CsvEscape RFC 4180]

key-files:
  created:
    - src/Deskbridge.Core/Interfaces/IConnectionImporter.cs
    - src/Deskbridge.Core/Models/ImportModels.cs
    - src/Deskbridge.Core/Services/MRemoteNGImporter.cs
    - src/Deskbridge.Core/Services/ConnectionExporter.cs
    - tests/Deskbridge.Tests/Import/MRemoteNGImporterTests.cs
    - tests/Deskbridge.Tests/Import/ConnectionExporterTests.cs
    - tests/Deskbridge.Tests/Fixtures/sample-confcons.xml
  modified:
    - tests/Deskbridge.Tests/Deskbridge.Tests.csproj

key-decisions:
  - "String-keyed dictionaries for nullable Guid grouping in ConnectionExporter (Phase 3 pattern, avoids CS8714 with TreatWarningsAsErrors)"
  - "NullIfEmpty helper converts empty strings to null for optional attributes (Hostname, Username, Domain)"
  - "Password attribute in confCons.xml explicitly skipped -- never read, stored, or logged (MIG-03 compliance)"

patterns-established:
  - "IConnectionImporter: multi-source import interface with SourceName, FileFilter, ParseAsync(Stream)"
  - "ImportedNode tree: recursive record structure for representing imported connection hierarchies"
  - "CsvEscape: RFC 4180 compliant escaping for commas, double quotes, and newlines"

requirements-completed: [MIG-01, MIG-03, MIG-04, MIG-05, MIG-06]

# Metrics
duration: 8min
completed: 2026-04-17
---

# Phase 7 Plan 3: Import/Export Summary

**mRemoteNG confCons.xml parser with XXE prevention, JSON tree export, and CSV flat export -- all credential-free with 26 unit tests**

## Performance

- **Duration:** 8 min
- **Started:** 2026-04-17T08:47:32Z
- **Completed:** 2026-04-17T08:56:31Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments
- IConnectionImporter interface designed for multi-source import architecture (mRemoteNG now, RoyalTS/RDCMan future)
- MRemoteNGImporter parses confCons.xml with XXE prevention (DtdProcessing.Prohibit), encrypted file detection, protocol mapping (RDP/SSH/VNC), and name sanitization
- ConnectionExporter.ExportJson produces nested tree JSON matching folder hierarchy with no credential leakage
- ConnectionExporter.ExportCsv produces flat CSV with folder path column, RFC 4180 escaping, and no credential leakage
- 26 unit tests covering parsing correctness, edge cases, encryption detection, credential exclusion, CSV escaping

## Task Commits

Each task was committed atomically (TDD RED-GREEN):

1. **Task 1: IConnectionImporter + MRemoteNGImporter + tests**
   - `eb03c1b` (test) - RED: 13 failing tests for import parser
   - `f3d3d2c` (feat) - GREEN: implement MRemoteNGImporter with XXE prevention
2. **Task 2: ConnectionExporter + tests**
   - `868c037` (test) - RED: 13 failing tests for JSON/CSV export
   - `0857db5` (feat) - GREEN: implement ConnectionExporter with tree JSON and flat CSV

## Files Created/Modified
- `src/Deskbridge.Core/Interfaces/IConnectionImporter.cs` - Multi-source import abstraction (SourceName, FileFilter, ParseAsync)
- `src/Deskbridge.Core/Models/ImportModels.cs` - ImportResult, ImportedNode, ImportNodeType, ImportException
- `src/Deskbridge.Core/Services/MRemoteNGImporter.cs` - confCons.xml parser with XXE prevention and encryption detection
- `src/Deskbridge.Core/Services/ConnectionExporter.cs` - Static JSON tree + CSV flat export methods
- `tests/Deskbridge.Tests/Import/MRemoteNGImporterTests.cs` - 13 import parser tests
- `tests/Deskbridge.Tests/Import/ConnectionExporterTests.cs` - 13 export tests
- `tests/Deskbridge.Tests/Fixtures/sample-confcons.xml` - Realistic test fixture (2 containers, 5 connections, mixed protocols)
- `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` - Added Content item for fixture file

## Decisions Made
- String-keyed dictionaries for nullable Guid grouping in ConnectionExporter (follows Phase 3 pattern, avoids CS8714 notnull constraint violation with TreatWarningsAsErrors)
- NullIfEmpty helper converts empty XML attribute strings to null for optional fields (Hostname, Username, Domain) -- cleaner than storing empty strings
- Password attribute in confCons.xml is explicitly skipped in MRemoteNGImporter -- never read, stored, or logged per MIG-03 requirement

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added explicit System.IO using to IConnectionImporter and MRemoteNGImporter**
- **Found during:** Task 1 (compilation)
- **Issue:** `Stream` type not resolved despite ImplicitUsings -- WPF SDK has System.Windows.Shapes.Path conflict that suppresses System.IO implicit import
- **Fix:** Added `using System.IO;` to both files (established project pattern)
- **Files modified:** IConnectionImporter.cs, MRemoteNGImporter.cs
- **Verification:** Build succeeds
- **Committed in:** eb03c1b, f3d3d2c

**2. [Rule 3 - Blocking] Used string-keyed dictionaries for nullable Guid in ConnectionExporter**
- **Found during:** Task 2 (compilation)
- **Issue:** `Dictionary<Guid?, ...>` fails CS8714 with TreatWarningsAsErrors (nullable Guid doesn't match notnull constraint)
- **Fix:** Added GuidKey helper converting Guid? to string, used string keys throughout (Phase 3 established pattern)
- **Files modified:** ConnectionExporter.cs
- **Verification:** Build succeeds, all tests pass
- **Committed in:** 0857db5

---

**Total deviations:** 2 auto-fixed (both Rule 3 blocking)
**Impact on plan:** Both auto-fixes necessary for compilation under project constraints. No scope creep.

## Issues Encountered
- Pre-existing test failure in UpdateServiceTests.DownloadUpdatesAsync_ReportsProgress (from Phase 07-01) -- unrelated to this plan, not addressed

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Import parser and export logic are complete and tested
- Plan 07-04 (import wizard UI) can consume IConnectionImporter and ConnectionExporter directly
- MRemoteNGImporter is ready for DI registration in the import wizard

## Self-Check: PASSED

- All 7 created files verified on disk
- All 4 task commits verified in git history (eb03c1b, f3d3d2c, 868c037, 0857db5)

---
*Phase: 07-update-migration*
*Completed: 2026-04-17*
