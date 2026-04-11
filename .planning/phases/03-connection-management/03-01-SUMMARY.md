---
phase: 03-connection-management
plan: 01
subsystem: connection-persistence
tags: [json-store, credentials, atomic-writes, cr-01-fix, di-wiring]
dependency_graph:
  requires: []
  provides: [IConnectionStore-impl, ICredentialService-impl, JsonConnectionStore, WindowsCredentialService, CR-01-fix]
  affects: [ConnectionQueryService, App.xaml.cs-DI]
tech_stack:
  added: [AdysTech.CredentialManager-3.1.*]
  patterns: [atomic-write-via-tmp-rename, credential-inheritance-walk, upsert-by-id]
key_files:
  created:
    - src/Deskbridge.Core/Interfaces/ICredentialService.cs
    - src/Deskbridge.Core/Services/JsonConnectionStore.cs
    - src/Deskbridge.Core/Services/WindowsCredentialService.cs
    - tests/Deskbridge.Tests/JsonConnectionStoreTests.cs
    - tests/Deskbridge.Tests/CredentialInheritanceTests.cs
    - tests/Deskbridge.Tests/ConnectionQueryServiceStoreTests.cs
  modified:
    - src/Deskbridge.Core/Interfaces/IConnectionStore.cs
    - src/Deskbridge.Core/Deskbridge.Core.csproj
    - src/Deskbridge.Core/Services/ConnectionQueryService.cs
    - src/Deskbridge/App.xaml.cs
    - Directory.Packages.props
decisions:
  - Serilog added to Deskbridge.Core.csproj for error logging in JsonConnectionStore (was only in app project)
  - Explicit System.IO using required in files using Path/File/Directory due to WPF UseWPF=true adding System.Windows.Shapes.Path globally
  - CredentialInheritanceTests test the walk-up algorithm via helper method with mocked ICredentialService to avoid hitting real Windows Credential Manager
metrics:
  duration: 6min
  completed: 2026-04-11T17:52:03Z
  tasks: 2
  files: 11
---

# Phase 03 Plan 01: Connection Data Layer Summary

JWT-free connection persistence with atomic JSON writes, Windows Credential Manager integration via AdysTech.CredentialManager, credential inheritance chain resolution, and CR-01 fix wiring ConnectionQueryService to real IConnectionStore data.

## What Was Built

### Task 1: Interface Contracts and NuGet Reference
- Extended `IConnectionStore` with `SaveGroup(ConnectionGroup)`, `DeleteGroup(Guid)`, and `Load()` methods
- Created `ICredentialService` interface with 8 methods: GetForConnection, StoreForConnection, DeleteForConnection, GetForGroup, StoreForGroup, DeleteForGroup, ResolveInherited, HasGroupCredentials
- Added `AdysTech.CredentialManager 3.1.*` to `Directory.Packages.props` (CPM) and `Deskbridge.Core.csproj`

### Task 2: Implementations, CR-01 Fix, DI Wiring (TDD)

**JsonConnectionStore** (`src/Deskbridge.Core/Services/JsonConnectionStore.cs`):
- Inner `ConnectionsFile` class with `Version = 1`, `Connections`, `Groups` arrays (D-01, D-02)
- `Load()` reads from disk, catches `JsonException` for corrupt files (T-03-05 threat mitigation), returns empty collections
- `Save(ConnectionModel)` upserts by Id, sets UpdatedAt, calls `PersistAtomically()`
- `Delete(Guid)` removes connection, persists
- `SaveGroup(ConnectionGroup)` upserts by Id, persists
- `DeleteGroup(Guid)` removes group AND sets `GroupId=null` on orphaned connections, persists
- `PersistAtomically()` writes to `.tmp` file in same directory, then `File.Move(overwrite: true)` for atomic rename on NTFS
- `JsonSerializerOptions` with `WriteIndented=true`, `CamelCase` naming for human-readable config
- Default path: `%AppData%/Deskbridge/connections.json`

**WindowsCredentialService** (`src/Deskbridge.Core/Services/WindowsCredentialService.cs`):
- Uses `CredentialType.Generic` for ALL targets (not DomainPassword, per Pitfall 4)
- Connection targets: `TERMSRV/<hostname>`
- Group targets: `DESKBRIDGE/GROUP/<guid>`
- `ResolveInherited` walks up group chain via `ParentGroupId`, returns first group with stored credentials
- `HasGroupCredentials` delegates to `GetForGroup`
- Delete operations wrapped in try/catch (credential may not exist)

**CR-01 Fix** (`src/Deskbridge.Core/Services/ConnectionQueryService.cs`):
- Added `ConnectionQueryService(IConnectionStore store)` constructor
- All query methods delegate to `_store.GetAll()` when store is present, fallback to `_connections` for legacy constructor
- Kept `ConnectionQueryService(IEnumerable<ConnectionModel>)` constructor for backward test compatibility (Pitfall 8)

**DI Wiring** (`src/Deskbridge/App.xaml.cs`):
- `IConnectionStore` registered as singleton with factory lambda that calls `Load()` after construction
- `ICredentialService` registered as `WindowsCredentialService` singleton
- `IConnectionQuery` registered via factory using `IConnectionStore` constructor

**Tests** (13 new tests, 83 total passing):
- `JsonConnectionStoreTests`: 13 tests covering save/load roundtrip, delete, group CRUD, atomic write verification, empty file handling, schema version, corrupt JSON graceful handling, upsert by Id, orphan connections on group delete
- `CredentialInheritanceTests`: 5 tests covering direct group credentials, grandparent walk-up, null when no creds, ungrouped connection, HasGroupCredentials
- `ConnectionQueryServiceStoreTests`: 5 tests covering search delegation, GetAll delegation, legacy constructor compat, GetByGroup, GetRecent

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added Serilog to Deskbridge.Core.csproj**
- **Found during:** Task 2
- **Issue:** Plan calls for `Serilog.Log.Error(ex, ...)` in JsonConnectionStore but Serilog was only referenced in the app project, not Core
- **Fix:** Added `<PackageReference Include="Serilog" />` to Deskbridge.Core.csproj
- **Files modified:** `src/Deskbridge.Core/Deskbridge.Core.csproj`

**2. [Rule 1 - Bug] Added explicit System.IO using to resolve Path/File/Directory ambiguity**
- **Found during:** Task 2
- **Issue:** `UseWPF=true` in Directory.Build.props globally imports `System.Windows.Shapes.Path`, causing `Path`, `File`, `Directory` to be ambiguous with `System.IO` types
- **Fix:** Added `using System.IO;` to `JsonConnectionStore.cs` and `JsonConnectionStoreTests.cs`
- **Files modified:** `src/Deskbridge.Core/Services/JsonConnectionStore.cs`, `tests/Deskbridge.Tests/JsonConnectionStoreTests.cs`

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build Deskbridge.sln` | 0 errors, 0 warnings |
| `dotnet test tests/Deskbridge.Tests/` | 83 passed, 0 failed, 0 skipped |
| JsonConnectionStore roundtrip | Save -> Load from disk -> data matches |
| Corrupt JSON handling (T-03-05) | Load_CorruptJson_ReturnsEmpty passes |
| Atomic write pattern | .tmp file written then File.Move(overwrite:true) |
| CredentialType.Generic | All targets in WindowsCredentialService use Generic |
| CR-01 fix | ConnectionQueryService with IConnectionStore returns real data |
| Credential inheritance | Walk-up chain tested with mocked dependencies |
| DI registrations | IConnectionStore, ICredentialService, IConnectionQuery all registered |

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 | e989c70 | feat(03-01): define interface contracts and add AdysTech.CredentialManager NuGet |
| 2 (RED) | 4b6fa47 | test(03-01): add failing tests for store, credential inheritance, and CR-01 fix |
| 2 (GREEN) | 8a6f435 | feat(03-01): implement JsonConnectionStore, WindowsCredentialService, fix CR-01, wire DI |

## Self-Check: PASSED

All 6 created files verified on disk. All 3 commit hashes found in git log.
