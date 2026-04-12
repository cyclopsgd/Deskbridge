---
created: 2026-04-12
source: Phase 4 live verification
origin_phase: 03-connection-management
affected_file: src/Deskbridge.Core/Services/WindowsCredentialService.cs:32
type: bug
priority: medium
---

# Credential save fails with 0x00000008 for TERMSRV/* targets

## Symptom
`AdysTech.CredentialManager.CredentialAPIException (0x00000008): Unable to save credential`
from `CredentialManager.SaveCredentials(target, cred, CredentialType.Generic)` when target = `TERMSRV/<host>`.

Observed for `TERMSRV/192.168.0.179`. `TERMSRV/127.0.0.1` succeeded (possibly because a prior `cmdkey /generic:TERMSRV/127.0.0.1` from Plan 04-01 testing had already created a Generic-type entry).

## Probable root cause
Windows Credential Manager reserves `TERMSRV/*` target prefix for `CredentialType.DomainPassword` (the RDP SSO convention used by `mstsc`). Writing as `CredentialType.Generic` fails with 0x8 (ERROR_NOT_ENOUGH_MEMORY) when no existing Generic credential is present and Windows rejects the type mismatch. Persistence mode (Enterprise default in AdysTech) may also contribute.

## Fix paths (in order of preference)
1. **Use `CredentialType.DomainPassword` for TERMSRV/* targets** — canonical Windows convention; `mstsc` can also auto-use them for SSO (bonus).
2. Migrate-on-read: if `DomainPassword` empty but `Generic` present, read Generic, re-save as DomainPassword, delete Generic.
3. Alternative: change target prefix to `DESKBRIDGE/RDP/<host>` (own namespace, always Generic works). Loses `mstsc` interop but simplest.

## Scope
Phase 3 service (`WindowsCredentialService`). Affects all new RDP connections for hosts that don't already have a Windows Credential Manager entry. Fold into Plan 04-03 polish pass or a separate 03.1 gap-closure phase.
