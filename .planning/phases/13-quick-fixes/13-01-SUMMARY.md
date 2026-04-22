---
phase: 13-quick-fixes
plan: 01
subsystem: rdp, credentials
tags: [rdp, disconnect-classifier, authentication-level, credential-fallback, tab-lifecycle]

requires:
  - phase: 04-rdp-lifecycle
    provides: RDP disconnect classification, ConnectionCoordinator, reconnect overlay
  - phase: 05-tab-management
    provides: TabHostManager tab lifecycle, ConnectionClosedEvent handling

provides:
  - AuthenticationLevel defaults to 0 (certificate prompts skipped for all new connections)
  - Logoff disconnect category with auto-tab-close behavior
  - Inherit-to-Own credential fallback when inheritance chain exhausted

affects: [rdp-connection-flow, credential-resolution, tab-lifecycle]

tech-stack:
  added: []
  patterns:
    - "Disconnect category with early-exit branch pattern in ConnectionCoordinator"
    - "Credential resolution fallback chain: Inherit -> Own -> Prompt"

key-files:
  created: []
  modified:
    - src/Deskbridge.Core/Models/ConnectionModel.cs
    - src/Deskbridge.Core/Services/DisconnectReasonClassifier.cs
    - src/Deskbridge.Core/Services/ConnectionCoordinator.cs
    - src/Deskbridge.Core/Pipeline/Stages/ResolveCredentialsStage.cs

key-decisions:
  - "AuthenticationLevel=0 matches mRemoteNG default; enterprise users on managed networks trust their targets"
  - "Logoff (discReason 1) publishes ConnectionClosedEvent with RemoteDisconnect reason rather than UserInitiated"
  - "Inherit fallback tries Own stored credential before prompting, preserving the no-prompt experience for root-level connections"

patterns-established:
  - "Disconnect category early-exit: check category before entering reconnect flow"
  - "Credential fallback chain: Inherit -> Own -> Prompt (graceful degradation)"

requirements-completed: [RELY-01, RELY-02, RELY-03]

duration: 2min
completed: 2026-04-22
---

# Phase 13 Plan 01: Connection Reliability Quick Fixes Summary

**Skip certificate prompts via AuthenticationLevel=0, auto-close tabs on remote logoff, and fall back to own credentials when inheritance chain is exhausted**

## Performance

- **Duration:** 2 min
- **Started:** 2026-04-22T17:23:09Z
- **Completed:** 2026-04-22T17:25:29Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- RELY-01: Certificate verification prompts eliminated by defaulting AuthenticationLevel to 0 (propagates through existing RdpConnectionConfigurator.Apply)
- RELY-02: Remote session logoff (discReason 1) now classified as Logoff category, triggering tab auto-close via ConnectionClosedEvent instead of showing reconnect overlay
- RELY-03: Connections with Inherit mode at root level (no parent group) now fall back to stored own credentials, then to prompt dialog, instead of failing immediately

## Task Commits

Each task was committed atomically:

1. **Task 1: Certificate skip + logoff disconnect classification** - `612c4fc` (fix)
2. **Task 2: Credential inheritance fallback to Own mode** - `776688a` (fix)

## Files Created/Modified
- `src/Deskbridge.Core/Models/ConnectionModel.cs` - AuthenticationLevel default changed from 2 to 0
- `src/Deskbridge.Core/Services/DisconnectReasonClassifier.cs` - Added Logoff category, moved code 1 from UserInitiated to LogoffCodes
- `src/Deskbridge.Core/Services/ConnectionCoordinator.cs` - Added Logoff early-exit in OnDisconnectedAfterConnect that disposes host and publishes ConnectionClosedEvent
- `src/Deskbridge.Core/Pipeline/Stages/ResolveCredentialsStage.cs` - Inherit case now falls back to GetForConnection then PromptForCredentialsAsync

## Decisions Made
- AuthenticationLevel=0 matches mRemoteNG default behavior; enterprise users on managed networks already trust RDP targets. Per-connection override to 1 or 2 still available.
- Logoff disconnect publishes with DisconnectReason.RemoteDisconnect (not UserInitiated) because the server ended the session.
- Inherit fallback calls GetForConnection (own stored credential) before PromptForCredentialsAsync, preserving the seamless no-prompt experience for root-level connections that have stored credentials.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All three reliability fixes are in place and the solution builds cleanly
- Ready for Phase 13 Plan 02 (remaining quick fixes)

## Self-Check: PASSED

All 5 files verified present. Both task commits (612c4fc, 776688a) found in git history.

---
*Phase: 13-quick-fixes*
*Completed: 2026-04-22*
