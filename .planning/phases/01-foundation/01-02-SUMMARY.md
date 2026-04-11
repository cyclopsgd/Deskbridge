---
phase: 01-foundation
plan: 02
subsystem: core
tags: [event-bus, pipeline, notification, query, fuzzy-search, di-composition, weakreferencemessenger]

# Dependency graph
requires:
  - "01-01: Compilable 4-project solution with DI composition root"
provides:
  - "IEventBus contract and WeakReferenceMessenger implementation"
  - "IConnectionPipeline with ordered stage execution and abort-on-failure"
  - "IDisconnectPipeline symmetric disconnect pipeline per D-03"
  - "INotificationService with event bus publishing"
  - "IConnectionQuery with fuzzy substring + subsequence search"
  - "IProtocolHost and IConnectionStore contracts for downstream phases"
  - "All shared models: ConnectionModel, ConnectionGroup, ConnectionFilter, DisplaySettings"
  - "All shared enums: Protocol, CredentialMode, DisconnectReason, ConnectionQuality, LockReason, AuditAction"
  - "All event records: ConnectionEvents and AppEvents"
  - "Pipeline types: ConnectionContext, DisconnectContext, PipelineResult"
  - "DI registrations for all 5 core services as singletons"
affects: [01-03, 02-ui-shell, 03-connections, 04-rdp, 05-tabs, 06-cross-cutting]

# Tech tracking
tech-stack:
  added: []
  patterns: [WeakReferenceMessenger wrapper, ordered pipeline with abort-on-failure, fuzzy search with substring + subsequence scoring, event bus publish-subscribe, notification service with event bus bridge]

key-files:
  created:
    - src/Deskbridge.Core/Interfaces/IEventBus.cs
    - src/Deskbridge.Core/Interfaces/IConnectionPipeline.cs
    - src/Deskbridge.Core/Interfaces/IDisconnectPipeline.cs
    - src/Deskbridge.Core/Interfaces/IConnectionQuery.cs
    - src/Deskbridge.Core/Interfaces/INotificationService.cs
    - src/Deskbridge.Core/Interfaces/IProtocolHost.cs
    - src/Deskbridge.Core/Interfaces/IConnectionStore.cs
    - src/Deskbridge.Core/Models/Enums.cs
    - src/Deskbridge.Core/Models/ConnectionModel.cs
    - src/Deskbridge.Core/Models/ConnectionGroup.cs
    - src/Deskbridge.Core/Models/ConnectionFilter.cs
    - src/Deskbridge.Core/Pipeline/PipelineResult.cs
    - src/Deskbridge.Core/Pipeline/ConnectionContext.cs
    - src/Deskbridge.Core/Pipeline/DisconnectContext.cs
    - src/Deskbridge.Core/Pipeline/ConnectionPipeline.cs
    - src/Deskbridge.Core/Pipeline/DisconnectPipeline.cs
    - src/Deskbridge.Core/Events/ConnectionEvents.cs
    - src/Deskbridge.Core/Events/AppEvents.cs
    - src/Deskbridge.Core/Services/EventBus.cs
    - src/Deskbridge.Core/Services/NotificationService.cs
    - src/Deskbridge.Core/Services/ConnectionQueryService.cs
  modified:
    - src/Deskbridge/App.xaml.cs

key-decisions:
  - "ResolvedPassword field on ConnectionContext includes XML doc warning per T-01-05 threat mitigation"
  - "NotificationService.ShowError includes exception.Message but not stack traces per T-01-07"
  - "ConnectionQueryService uses dual scoring: substring match (100/80/60) and subsequence fallback (40/30)"
  - "Recent notifications capped at 50 entries with FIFO eviction"

requirements-completed: [CORE-02, CORE-03, CORE-04, CORE-05]

# Metrics
duration: 3min
completed: 2026-04-11
---

# Phase 01 Plan 02: Core Services Summary

**Event bus (WeakReferenceMessenger wrapper), connection and disconnect pipelines with ordered stage execution, notification service bridging to event bus, and fuzzy connection query -- all wired as singletons in DI composition root**

## Performance

- **Duration:** 3 min
- **Started:** 2026-04-11T13:29:15Z
- **Completed:** 2026-04-11T13:32:14Z
- **Tasks:** 2
- **Files modified:** 22

## Accomplishments
- 7 interfaces defined in Deskbridge.Core/Interfaces/ matching REFERENCE.md contracts exactly
- 4 model classes (ConnectionModel, ConnectionGroup, ConnectionFilter, DisplaySettings) with all fields from spec
- 6 enums covering protocol types, credential modes, disconnect reasons, connection quality, lock reasons, and audit actions
- 3 pipeline types (ConnectionContext, DisconnectContext, PipelineResult) with proper namespace separation
- 10 event records across ConnectionEvents.cs and AppEvents.cs
- 5 service implementations: EventBus, ConnectionPipeline, DisconnectPipeline, NotificationService, ConnectionQueryService
- All 5 services registered as singletons in App.xaml.cs DI composition root
- Full solution builds with 0 errors and 0 warnings

## Task Commits

Each task was committed atomically:

1. **Task 1: Create all interfaces, models, events, and enums in Deskbridge.Core** - `97f99f4` (feat)
2. **Task 2: Implement all core services and wire into DI composition root** - `2617201` (feat)

## Files Created/Modified

### Interfaces (7 files)
- `src/Deskbridge.Core/Interfaces/IEventBus.cs` - Event bus contract: Publish, Subscribe, Unsubscribe
- `src/Deskbridge.Core/Interfaces/IConnectionPipeline.cs` - Connection pipeline + stage contracts
- `src/Deskbridge.Core/Interfaces/IDisconnectPipeline.cs` - Disconnect pipeline + stage contracts (D-03)
- `src/Deskbridge.Core/Interfaces/IConnectionQuery.cs` - Connection query with Search, GetByGroup, GetByTag, GetByFilter, GetRecent
- `src/Deskbridge.Core/Interfaces/INotificationService.cs` - Notification service with Show, ShowError, Recent, NotificationRaised
- `src/Deskbridge.Core/Interfaces/IProtocolHost.cs` - Protocol host contract with ConnectAsync, DisconnectAsync, ErrorOccurred
- `src/Deskbridge.Core/Interfaces/IConnectionStore.cs` - Connection persistence contract (implementation deferred to Phase 3)

### Models (4 files)
- `src/Deskbridge.Core/Models/Enums.cs` - Protocol, CredentialMode, DisconnectReason, ConnectionQuality, LockReason, AuditAction
- `src/Deskbridge.Core/Models/ConnectionModel.cs` - Full connection model with all REFERENCE.md fields + DisplaySettings
- `src/Deskbridge.Core/Models/ConnectionGroup.cs` - Group model with ParentGroupId for hierarchy
- `src/Deskbridge.Core/Models/ConnectionFilter.cs` - Multi-criteria filter for query service

### Pipeline (5 files)
- `src/Deskbridge.Core/Pipeline/PipelineResult.cs` - Success/failure record for pipeline stages
- `src/Deskbridge.Core/Pipeline/ConnectionContext.cs` - Connection pipeline context with ResolvedPassword (T-01-05 doc)
- `src/Deskbridge.Core/Pipeline/DisconnectContext.cs` - Disconnect pipeline context with DisconnectReason
- `src/Deskbridge.Core/Pipeline/ConnectionPipeline.cs` - Ordered stage execution with abort-on-failure
- `src/Deskbridge.Core/Pipeline/DisconnectPipeline.cs` - Symmetric disconnect pipeline

### Events (2 files)
- `src/Deskbridge.Core/Events/ConnectionEvents.cs` - 8 connection lifecycle event records
- `src/Deskbridge.Core/Events/AppEvents.cs` - 7 application event records

### Services (3 files)
- `src/Deskbridge.Core/Services/EventBus.cs` - WeakReferenceMessenger.Default wrapper
- `src/Deskbridge.Core/Services/NotificationService.cs` - Publishes NotificationEvent to event bus, thread-safe recent list
- `src/Deskbridge.Core/Services/ConnectionQueryService.cs` - Fuzzy search with CalculateScore (substring 100/80/60, subsequence 40/30)

### DI Composition Root (1 file modified)
- `src/Deskbridge/App.xaml.cs` - Added 5 singleton registrations: IEventBus, INotificationService, IConnectionPipeline, IDisconnectPipeline, IConnectionQuery

## Decisions Made
- **ResolvedPassword XML doc (T-01-05):** Added "Do not log or serialize this value" documentation to ConnectionContext.ResolvedPassword to mitigate information disclosure threat.
- **ShowError exception handling (T-01-07):** NotificationService.ShowError includes exception.Message but never stack traces in notification text. Full exception logging deferred to Serilog integration (Phase 6).
- **Dual scoring strategy:** ConnectionQueryService uses substring matching first (name=100, hostname=80, tags=60) and falls back to subsequence matching (name=40, hostname=30) only if no substring match found. This ensures relevance: name match > hostname match > tag match.
- **Recent notification cap:** NotificationService caps _recent list at 50 entries with FIFO eviction to prevent unbounded memory growth.

## Deviations from Plan

None - plan executed exactly as written.

## Threat Mitigations Applied
- **T-01-05 (ResolvedPassword):** XML doc on ConnectionContext.ResolvedPassword states "Do not log or serialize this value." Event payloads (ConnectionEstablishedEvent, etc.) do NOT include passwords.
- **T-01-07 (NotificationService):** ShowError includes exception.Message but NOT stack traces in notification text. Full exception logging deferred to Serilog integration (Phase 6).

## Known Stubs

None - all interfaces have complete method signatures, all implementations are fully functional.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All core contracts are defined for downstream phases to consume
- IConnectionStore and IProtocolHost are defined but not implemented -- Phase 3 (connections) and Phase 4 (RDP) will implement them
- Pipeline stages (ResolveCredentials, CreateHost, ConnectStage, etc.) are not yet created -- they will be added as Phase 3+ implements the connect flow
- GlobalUsings.cs in Deskbridge and Tests projects may want Deskbridge.Core.Interfaces/Models/Events/Pipeline namespaces added when those projects start consuming them

## Self-Check: PASSED

- All 22 key files verified present on disk
- Both task commits (97f99f4, 2617201) verified in git log
- `dotnet build Deskbridge.sln` succeeds with 0 errors, 0 warnings

---
*Phase: 01-foundation*
*Completed: 2026-04-11*
