---
created: 2026-04-15T19:51:31.669Z
title: Phase 7 — encrypted in-app credential store (Defender / Credential Guard workaround)
area: security
files:
  - src/Deskbridge.Core/Interfaces/
  - src/Deskbridge.Core/Services/
  - src/Deskbridge.Core/Settings/AppSettings.cs
  - src/Deskbridge.Protocols.Rdp/
---

## Problem

Reported by user on 2026-04-15: on managed work machines with Windows Defender Credential Guard (or the "Restrict delegation of credentials to remote servers" GPO) enabled, Windows Credential Manager-stored RDP credentials are blocked from CredSSP delegation. This is intentional Microsoft security policy to prevent pass-the-hash attacks, and is persistent on enterprise endpoints.

Current state (Phase 4 shipped): Deskbridge stores RDP credentials in Windows Credential Manager via AdysTech.CredentialManager (per CLAUDE.md constraint "Credentials: AdysTech.CredentialManager only"). On policy-locked machines those creds are unusable.

## Why moving storage in-app actually helps

The misconception: "Defender is blocking locally-stored creds, so moving them elsewhere won't help."

The actual mechanism: Credential Guard blocks `CredSSP` from delegating creds it knows came from Credential Manager. It does NOT block creds typed in at connect time via `IMsTscNonScriptable.ClearTextPassword` — that path is treated the same as the user typing them into the MSTSC prompt dialog.

If Deskbridge stores creds in its own encrypted file and injects them via `ClearTextPassword` at connect time, the Defender policy no longer applies — because we're no longer flowing through the CredMan + CredSSP-saved-creds delegation path that the GPO polices.

## Solution

### Architecture

- New `ICredentialStore` interface in `Deskbridge.Core.Interfaces`:
  - `Save(id, user, password, domain)`, `Get(id)`, `Delete(id)`, `Exists(id)`
- Two implementations:
  - `CredentialManagerStore` — existing AdysTech.CredentialManager path (for home / non-managed users).
  - `MasterPasswordDerivedStore` — encrypted in-app store (recommended primary):
    - Derive an AES-256-GCM key from the master password using a fresh PBKDF2 salt (different from auth.json's salt) at unlock time.
    - Hold the key in memory while unlocked; zero it on `AppLockedEvent`.
    - Encrypt credential records as JSON → AES-GCM → base64 → `%AppData%/Deskbridge/credentials.dat`.
    - Creds readable ONLY while app is unlocked, which is the security property an enterprise environment actually wants.
  - (Optional third) `DpapiProtectedFileStore` — `ProtectedData.Protect` with `DataProtectionScope.CurrentUser`, as a fallback that doesn't require a master password. Weaker than MasterPasswordDerived (readable by anyone with the Windows profile) but stronger than CredMan on policy-locked machines.
- `AppSettings.Security.CredentialStorageMode` enum: `CredentialManager | EncryptedFile | DpapiProtected`.

### RDP integration

`Phase 4.1` (RDP credential injection fix) funnels everything through `IMsTscNonScriptable.ClearTextPassword`. This phase just swaps the *source* of those credentials from one ICredentialStore to another. The injection code does not change.

### Migration

- On first launch of Phase 7 code, detect existing `TERMSRV/*` entries in Credential Manager.
- Ask user: "Move Deskbridge credentials to encrypted storage? (Recommended for managed machines.)"
- On consent: decrypt each CredMan entry, re-encrypt via the new store, delete the CredMan entry. Atomic per-entry to survive interruptions.

### Coordination with master-password disable

If the user disables the master password (Phase 6.1 todo), `MasterPasswordDerivedStore` breaks — no password, no key. Options:
- **A (recommended):** refuse to disable master password while `MasterPasswordDerivedStore` has entries.
- **B:** silent migration to `DpapiProtectedFileStore` on disable.
- **C:** explicit user dialog offering A or B.

Decide this in Phase 6.1 design, even if Phase 7 ships later.

### Scope estimate

- ~500 lines of new code across 3-4 files + tests.
- 1.5-2 days of focused work.
- No new NuGet deps — uses `System.Security.Cryptography` (Rfc2898DeriveBytes, AesGcm, ProtectedData) which are in-box.

### Acceptance

- User can connect to RDP on their work machine with Credential Guard enabled without the session falling back to the Windows account or prompting for credentials.
- Encrypted store round-trips correctly (save → lock → unlock → get returns original).
- Key zeroed from memory on lock (verify with a managed heap inspector test).
- Migration from CredMan is atomic and reversible.
- Works with existing CLAUDE.md constraint `SecureString is banned` — we use plain `string` in memory with tight lifetime scope, same pattern as Phase 6 `LockOverlayViewModel`.

## Dependencies

- **Blocked by Phase 4.1** — RDP credential injection must use `ClearTextPassword` first. Without that, storage changes have no effect.
- **Coordinates with Phase 6.1** — master-password disable needs a design decision about encrypted creds.

Entry: `/gsd-insert-phase 7 "encrypted credential store"` once Phase 4.1 ships and a user-machine repro confirms it's needed. If Phase 4.1 alone solves the work issue, this phase may be deferred to v1.1 or descoped.
