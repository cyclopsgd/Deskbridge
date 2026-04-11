---
phase: 01-foundation
plan: 03
subsystem: testing
tags: [xunit-v3, fluent-assertions, nsubstitute, unit-tests, tdd, event-bus, pipeline, fuzzy-search, di-composition]

# Dependency graph
requires:
  - "01-02: All core service implementations and interfaces"
provides:
  - "Regression test suite for EventBus pub/sub/unsubscribe/weak-ref"
  - "Regression test suite for ConnectionPipeline stage ordering and abort-on-failure"
  - "Regression test suite for DisconnectPipeline stage ordering and abort-on-failure"
  - "Regression test suite for ConnectionQueryService fuzzy search across name/hostname/tags"
  - "Regression test suite for NotificationService event publishing and history"
  - "DI composition smoke test verifying all 5 core services resolve"
  - "xUnit v3 test infrastructure with MTP runner integration"
affects: [02-ui-shell, 03-connections, 04-rdp, 05-tabs, 06-cross-cutting]

# Tech tracking
tech-stack:
  added: []
  patterns: [xUnit v3 with Microsoft Testing Platform, NSubstitute mock stages, FluentAssertions Should() chains, TestingPlatformDotnetTestSupport for dotnet test]

key-files:
  created:
    - tests/Deskbridge.Tests/EventBusTests.cs
    - tests/Deskbridge.Tests/ConnectionPipelineTests.cs
    - tests/Deskbridge.Tests/DisconnectPipelineTests.cs
    - tests/Deskbridge.Tests/ConnectionQueryTests.cs
    - tests/Deskbridge.Tests/NotificationServiceTests.cs
    - tests/Deskbridge.Tests/DiCompositionTests.cs
  modified:
    - tests/Deskbridge.Tests/Deskbridge.Tests.csproj
    - Directory.Packages.props

key-decisions:
  - "xUnit v3 requires OutputType Exe and TestingPlatformDotnetTestSupport for dotnet test integration"
  - "Removed Microsoft.NET.Test.Sdk -- xUnit v3 uses its own MTP runner, not VSTest"
  - "ConnectionQueryService tested with known dataset of 4 synthetic connections using 10.0.0.x and 192.168.x IPs"

requirements-completed: [CORE-01, CORE-02, CORE-03, CORE-04, CORE-05]

# Metrics
duration: 6min
completed: 2026-04-11
---

# Phase 01 Plan 03: Core Service Unit Tests Summary

**33 unit tests across 6 test classes covering event bus pub/sub with weak references, connection and disconnect pipeline ordering with abort-on-failure, fuzzy search across name/hostname/tags with subsequence fallback, notification service event publishing with 50-cap history, and DI composition resolution of all 5 core singletons**

## Performance

- **Duration:** 6 min
- **Started:** 2026-04-11T13:34:54Z
- **Completed:** 2026-04-11T13:41:14Z
- **Tasks:** 1
- **Files modified:** 8

## Accomplishments
- 33 unit tests across 6 test classes, all passing via `dotnet test`
- EventBusTests (5 tests): publish delivery, unsubscribe stops delivery, multiple subscribers, weak reference GC safety, no-subscriber safety
- ConnectionPipelineTests (5 tests): stage ordering (200,100,300 executes as 100,200,300), failure abort (stage 300 never executes), all-success, failure reason preservation, empty pipeline
- DisconnectPipelineTests (3 tests): stage ordering, failure abort with reason, all-success
- ConnectionQueryTests (11 tests): substring in name/hostname/tags via Theory+InlineData, name-rank-over-hostname, subsequence match ("psrv" finds "prod-server-01"), empty query returns all, no match returns empty, GetByGroup, GetByTag, GetRecent ordered by UpdatedAt
- NotificationServiceTests (5 tests): event bus publishing, recent history, NotificationRaised event, ShowError sets Error level with exception message, 50-cap FIFO eviction
- DiCompositionTests (1 test): mirrors App.xaml.cs ConfigureServices, resolves all 5 core interfaces (IEventBus, INotificationService, IConnectionPipeline, IDisconnectPipeline, IConnectionQuery)
- Fixed xUnit v3 test infrastructure: OutputType Exe, TestingPlatformDotnetTestSupport, removed Microsoft.NET.Test.Sdk

## Task Commits

Each task was committed atomically:

1. **Task 1: Create all unit tests for core services** - `03bb9ef` (test)

## Files Created/Modified
- `tests/Deskbridge.Tests/EventBusTests.cs` - 5 [Fact] tests for EventBus pub/sub/unsubscribe/weak-ref/no-subscriber
- `tests/Deskbridge.Tests/ConnectionPipelineTests.cs` - 5 [Fact] tests for ConnectionPipeline ordering/abort/success/failure-reason/empty
- `tests/Deskbridge.Tests/DisconnectPipelineTests.cs` - 3 [Fact] tests for DisconnectPipeline ordering/abort/success
- `tests/Deskbridge.Tests/ConnectionQueryTests.cs` - 11 tests (mix of [Theory]+[InlineData] and [Fact]) for fuzzy search, substring, subsequence, group, tag, recent
- `tests/Deskbridge.Tests/NotificationServiceTests.cs` - 5 [Fact] tests for notification publishing/history/event/error/cap
- `tests/Deskbridge.Tests/DiCompositionTests.cs` - 1 [Fact] test resolving all 5 core singletons from ServiceCollection
- `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` - Added OutputType Exe, TestingPlatformDotnetTestSupport; removed Microsoft.NET.Test.Sdk
- `Directory.Packages.props` - Removed Microsoft.NET.Test.Sdk PackageVersion entry

## Decisions Made
- **xUnit v3 test runner:** xUnit v3 (3.2.2) does not use the traditional VSTest adapter (Microsoft.NET.Test.Sdk). Instead, it requires `OutputType Exe` and `TestingPlatformDotnetTestSupport` to integrate with `dotnet test` via the Microsoft Testing Platform (MTP). The old `Microsoft.NET.Test.Sdk` package was removed from both the csproj and Directory.Packages.props.
- **Synthetic test data:** ConnectionQueryTests use a fixed dataset with synthetic IPs (10.0.0.x, 192.168.x) and names (prod-server-01, dev-server-01, web-server, staging-db) per T-01-09 accept disposition.
- **Subsequence test pattern:** "psrv" chosen as subsequence query for "prod-server-01" -- matches p, s, r, v in order within the name string.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] xUnit v3 test discovery failure with dotnet test**
- **Found during:** Task 1 (test verification)
- **Issue:** `dotnet test` reported "No test is available" because xUnit v3 uses Microsoft Testing Platform (MTP), not VSTest. The `Microsoft.NET.Test.Sdk` package was conflicting with xUnit v3's own runner.
- **Fix:** Added `<OutputType>Exe</OutputType>` and `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>` to Deskbridge.Tests.csproj. Removed `Microsoft.NET.Test.Sdk` PackageReference and its PackageVersion entry from Directory.Packages.props.
- **Files modified:** tests/Deskbridge.Tests/Deskbridge.Tests.csproj, Directory.Packages.props
- **Verification:** `dotnet test tests/Deskbridge.Tests/` discovers and runs all 33 tests
- **Committed in:** 03bb9ef

---

**Total deviations:** 1 auto-fixed (1 blocking issue)
**Impact on plan:** Required infrastructure fix for xUnit v3 compatibility. No scope creep.

## Threat Mitigations Applied
- **T-01-09 (Test data):** All test data uses synthetic connection names (prod-server-01, dev-server-01, web-server, staging-db) and synthetic IPs (10.0.0.1, 192.168.1.100, 10.0.0.5, 10.0.1.50). No real hostnames or credentials in test fixtures.

## Known Stubs

None - all test files contain complete, passing test implementations.

## Issues Encountered

None beyond the xUnit v3 test runner fix documented in Deviations.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Phase 01 (Foundation) is now complete: solution scaffold, core services, and regression tests all in place
- All 5 core service contracts have test coverage that will catch regressions as downstream phases build on them
- `dotnet test tests/Deskbridge.Tests/` serves as the regression command for CI or local verification
- Phase 02 (UI Shell) can proceed knowing the architectural backbone is tested and verified

## Self-Check: PASSED
