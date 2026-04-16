---
phase: quick
plan: 260416-9wt
subsystem: credentials
tags: [credential-manager, credential-guard, rdp, migration]

# Dependency graph
requires:
  - phase: 03-connection-management
    provides: WindowsCredentialService, ICredentialService, IConnectionStore
provides:
  - DESKBRIDGE/CONN/{connectionId} credential target format
  - MigrateFromTermsrv startup migration for legacy TERMSRV/* entries
  - Credential Guard compatibility for RDP connections
affects: [rdp-integration, connection-management, mremoteng-import]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "DESKBRIDGE/CONN/{connectionId} target format for connection credentials (CredentialType.Generic)"
    - "Startup migration pattern: idempotent per-connection try/catch with swallowed cleanup failures"

key-files:
  created:
    - tests/Deskbridge.Tests/CredentialMigrationTests.cs
  modified:
    - src/Deskbridge.Core/Services/WindowsCredentialService.cs
    - src/Deskbridge/App.xaml.cs
    - REFERENCE.md

key-decisions:
  - "CredentialType.Generic for connection targets (was Windows/DomainPassword) -- consistent with DESKBRIDGE/GROUP/* pattern"
  - "BuildConnectionTarget/BuildLegacyTarget as internal static helpers for testability"

patterns-established:
  - "DESKBRIDGE/CONN/{connectionId}: canonical credential target format for connection-specific credentials"
  - "Startup migration: one-time idempotent migration runs after DI build, before pipeline wiring"

requirements-completed: [CONN-07]

# Metrics
duration: 3min
completed: 2026-04-16
---

# Quick Task 260416-9wt: Credential Guard Fix Summary

**Changed credential target from TERMSRV/{hostname} to DESKBRIDGE/CONN/{connectionId} to eliminate Credential Guard blocking on RDP connections, with idempotent startup migration of existing entries**

## Performance

- **Duration:** 3 min
- **Started:** 2026-04-16T06:12:42Z
- **Completed:** 2026-04-16T06:16:20Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Connection credentials now use DESKBRIDGE/CONN/{connectionId} target, invisible to Windows CredSSP negotiation
- MigrateFromTermsrv method handles legacy DomainPassword and Generic TERMSRV/* entries with per-connection error isolation
- Startup migration wired into App.OnStartup after DI build, before any connection attempts
- REFERENCE.md updated in all 4 locations to reflect new target format

## Task Commits

Each task was committed atomically:

1. **Task 1 (RED): Add failing tests** - `8d83302` (test)
2. **Task 1 (GREEN): Implement target change and migration** - `bfc36e2` (feat)
3. **Task 2: Wire migration + update REFERENCE.md** - `fc18102` (feat)

_TDD task 1 had RED + GREEN commits. No refactor needed._

## Files Created/Modified
- `src/Deskbridge.Core/Services/WindowsCredentialService.cs` - Changed all connection methods to use DESKBRIDGE/CONN/{id} with CredentialType.Generic; added MigrateFromTermsrv; added BuildConnectionTarget/BuildLegacyTarget helpers
- `tests/Deskbridge.Tests/CredentialMigrationTests.cs` - 5 unit tests for target format and migration method
- `src/Deskbridge/App.xaml.cs` - Added migration call after DI container build
- `REFERENCE.md` - Updated 4 references from TERMSRV/{hostname} to DESKBRIDGE/CONN/{connectionId}

## Decisions Made
- CredentialType.Generic used for connection targets instead of CredentialType.Windows -- consistent with the DESKBRIDGE/GROUP/* pattern already in use, and avoids the Windows-reserved CRED_TYPE_DOMAIN_PASSWORD that TERMSRV/* required
- BuildConnectionTarget/BuildLegacyTarget extracted as internal static methods for testability via InternalsVisibleTo (already configured in Core csproj)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Credential Guard machines will no longer block RDP connections
- Existing TERMSRV/* credentials automatically migrated on first launch
- mRemoteNG import (Phase 7) will use new DESKBRIDGE/CONN/ prefix for imported connections

## Self-Check: PASSED

All 5 files verified present. All 3 commit hashes verified in git log.

---
*Plan: 260416-9wt*
*Completed: 2026-04-16*
