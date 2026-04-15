---
created: 2026-04-15T19:51:31.669Z
title: Phase 4.1 — RDP credential injection falls back to Windows account on managed machines
area: rdp
files:
  - src/Deskbridge.Protocols.Rdp/
---

## Problem

Reported by user on 2026-04-15: when launching an RDP session from Deskbridge on their **work machine** (managed endpoint, likely under Credential Guard or "Restrict delegation of credentials to remote servers" GPO), the session signs in with the current Windows account even though the connection has explicit username/password configured. Also, Windows Defender / Credential Guard appears to block using Credential-Manager-stored creds.

The bug does NOT reproduce on the user's home machine — repro requires enterprise-policy hardware.

## Hypothesis

Two related but separable causes:

1. **RDP injection order / path bug (most likely fix):** AxMsRdpClient9 credential injection must use `IMsTscNonScriptable.ClearTextPassword` via the classic-interop cast, set AFTER siting and BEFORE `.Connect()`, with explicit `Domain` and `UserName`. If Phase 4's implementation uses `AdvancedSettings9.ClearTextPassword` or relies on Credential Manager target-name lookup, the CredSSP layer will prefer delegated session tokens and saved creds (both of which are policy-blocked on managed endpoints). Pushing creds via the non-scriptable interface is treated the same as typing them into the MSTSC prompt — that path is NOT governed by the delegation GPO.

2. **Credential Guard blocking delegation of saved creds (policy, not code):** Even with the above fix, if the server-side also enforces Restricted Admin or the client GPO outright disables *all* CredSSP delegation, no saved-creds path will work. Mitigation for this is Phase 7 (encrypted in-app credential store feeding directly into `ClearTextPassword`), tracked separately.

## Solution

### Investigation (cannot be done from home — requires work machine)

Add targeted diagnostic logging to the Phase 4 RDP connect path, then test at work:

1. Log at each step of AxHost lifecycle: site / configure / set-credentials / connect.
2. Dump the AxMsRdpClient9 `AdvancedSettings9.ClearTextPassword` length AND the `IMsTscNonScriptable.ClearTextPassword` was-set marker immediately before `.Connect()`.
3. Hook `OnAuthenticationWarningDisplayed` / `OnLogonError` events — capture failure reason.
4. Correlate log timestamps against what actually happened on screen.

### Likely fix (once hypothesis confirmed)

Ensure every connect path in `src/Deskbridge.Protocols.Rdp` calls, in order:

1. `axControl.CreateControl()` + `Show()` (siting)
2. Cast `axControl.GetOcx()` to `IMsTscNonScriptable` (verify cast succeeds — if it fails, interop DLL is wrong)
3. Set `ClearTextPassword` via that interface
4. Set `Domain` and `UserName` on `AdvancedSettings9` (explicit Domain — NOT blank with UPN usernames)
5. Optionally: `AdvancedSettings9.DisableCredentialsDelegation = 0` to force the ActiveX to use provided creds rather than delegated ones
6. `.Connect()`

### Acceptance

Bug no longer reproduces on user's work machine. RDP sessions sign in with explicit connection credentials regardless of the current Windows account. Needs user UAT on work hardware before closure.

## Related

- Phase 7 backlog: encrypted in-app credential store (durable Defender workaround — needed even after 4.1 fix if saved-creds delegation is policy-blocked).
- CLAUDE.md: RDP-ACTIVEX-PITFALLS.md at repo root — consult before touching AxMsRdpClient9 code.

Entry: `/gsd-insert-phase 4.1 "RDP credential injection fix"` when a work-machine test window is available.
