# Security Model

This document describes the security architecture of Deskbridge in detail. For a summary suitable for vulnerability reporters, see [SECURITY.md](../SECURITY.md).

## Master Password / PIN

### Algorithm

Deskbridge uses PBKDF2-HMAC-SHA256 for master password hashing, implemented via `Rfc2898DeriveBytes.Pbkdf2`:

| Parameter | Value | Rationale |
| --- | --- | --- |
| Hash algorithm | SHA-256 | Standard PBKDF2-HMAC target per OWASP and NIST SP 800-132 |
| Iterations | 600,000 | OWASP 2023 minimum recommendation for PBKDF2-HMAC-SHA256 |
| Salt size | 32 bytes (256-bit) | Exceeds NIST SP 800-132 section 5.1 minimum of 16 bytes |
| Derived key size | 32 bytes (256-bit) | Matches SHA-256 output length |

On a typical laptop, 600,000 iterations produces a hash in 200-500 ms.

### Storage Format

The hash is stored in `%AppData%\Deskbridge\auth.json` with the following structure:

```json
{
  "passwordHash": "v1.<base64 salt>.<base64 key>",
  "authMode": "password",
  "schemaVersion": 1
}
```

| Field | Description |
| --- | --- |
| `passwordHash` | Versioned hash envelope. The `v1` prefix reserves the format for future algorithm changes (e.g., Argon2id). The salt and derived key are Base64-encoded. |
| `authMode` | Presentation hint: `"password"` or `"pin"`. Determines whether the lock screen shows a text field or a PIN pad. Does not affect the KDF. |
| `schemaVersion` | Integer version for forward compatibility. Verification is rejected if the version does not match the current schema. |

The file is written atomically (write to `.tmp`, then `File.Move` with overwrite) to prevent corruption from process termination during write.

### Verification

Password verification uses `CryptographicOperations.FixedTimeEquals` to compare the derived key against the stored key. This prevents timing side-channel attacks that could leak the derived key byte-by-byte.

### What the Master Password Protects

The master password gates access to the Deskbridge UI. When locked:

- The connection tree, tabs, settings, and command palette are inaccessible.
- All WindowsFormsHost children (including RDP sessions) are collapsed so their pixels are not visible through the WPF airspace layer.
- RDP sessions remain connected behind the lock -- only the Deskbridge chrome is gated.

The master password does **not** encrypt individual connection credentials. Those are managed by Windows Credential Manager and protected by DPAPI (tied to the Windows user login).

## Credential Storage

### Architecture

Deskbridge uses the Windows Credential Manager via the `AdysTech.CredentialManager` library. All credentials are stored as `CredentialType.Generic` entries.

| Scope | Target format | Example |
| --- | --- | --- |
| Per-connection | `DESKBRIDGE/CONN/{connectionId}` | `DESKBRIDGE/CONN/a1b2c3d4-e5f6-7890-abcd-ef1234567890` |
| Per-group | `DESKBRIDGE/GROUP/{groupId}` | `DESKBRIDGE/GROUP/f0e1d2c3-b4a5-6789-0fed-cba987654321` |

Each credential entry stores: username, password, and domain (optional).

### Credential Inheritance

Connections have a `CredentialMode` property:

- **Inherit** (default): The connection pipeline walks up the group tree (connection -> parent group -> grandparent -> root). The first group with stored credentials wins.
- **Own**: Uses credentials stored under the connection's own `DESKBRIDGE/CONN/{id}` target.
- **Prompt**: Always prompts the user for credentials before connecting.

This allows enterprise teams to set credentials once on a folder and have all connections within inherit them automatically.

### Credential Guard Compatibility

The `DESKBRIDGE/` prefix avoids the `TERMSRV/` namespace that Windows Defender Credential Guard intercepts during CredSSP delegation. Deskbridge injects passwords directly via `IMsTscNonScriptable.ClearTextPassword`, bypassing CredSSP entirely. This eliminates the "Credential Guard does not allow using saved credentials" error that affects tools using `TERMSRV/*` targets.

A one-time migration runs at startup to move any legacy `TERMSRV/{hostname}` entries to the new `DESKBRIDGE/CONN/{id}` format. The migration is idempotent -- connections that already have new-format credentials are skipped.

### What Is Protected by DPAPI

Windows Credential Manager encrypts stored credentials using DPAPI (Data Protection API), which is tied to the Windows user's login credentials. The decryption key is derived from the user's Windows password. This means:

- Credentials are accessible only to the Windows user who stored them.
- Credentials are protected at rest on disk by the OS.
- Credentials do not survive Windows user profile deletion.
- Deskbridge does not implement its own encryption layer for connection passwords.

## Plaintext Data

The following files contain no secrets:

### connections.json

Stores connection metadata: name, hostname, port, username, domain, protocol, group membership, display settings, tags, notes, credential mode, timestamps. **Passwords are never stored in this file.**

### settings.json

Stores application preferences: window position, size, maximised state, sidebar state, auto-lock timeout (in minutes), lock-on-minimise toggle, update channel (stable/beta), and schema version.

## Audit Logging

### Format

Audit events are written as JSON Lines (one JSON object per line) to:

```
%AppData%\Deskbridge\audit-YYYY-MM.jsonl
```

Files rotate monthly. A new file is created at the start of each calendar month.

### Record Schema

Each record is a JSON object with the following fields:

| Field | Type | Description |
| --- | --- | --- |
| `ts` | string | ISO-8601 UTC timestamp (e.g., `2026-04-15T14:30:00.0000000Z`) |
| `type` | string | Event type from the `AuditAction` enum |
| `connectionId` | string (GUID) or null | Connection identifier. Null for app-scope events. |
| `user` | string | Windows username (`Environment.UserName`) at the time of the event |
| `outcome` | string | `"success"` or `"fail"` |
| `errorCode` | string or absent | Optional structured error code. Omitted from JSON when null. |

### Recorded Events

The following `AuditAction` types are recorded:

| Event | Trigger |
| --- | --- |
| `Connected` | RDP session established |
| `Disconnected` | RDP session ended (user or remote) |
| `FailedConnect` | Connection attempt failed |
| `Reconnected` | Automatic reconnection succeeded |
| `ConnectionCreated` | New connection added |
| `ConnectionEdited` | Connection properties modified |
| `ConnectionDeleted` | Connection removed |
| `ConnectionsImported` | Connections imported from file |
| `ConnectionsExported` | Connections exported to file |
| `CredentialStored` | Credential saved to Windows Credential Manager |
| `CredentialDeleted` | Credential removed from Windows Credential Manager |
| `AppStarted` | Application launched |
| `AppClosed` | Application exited |
| `UpdateApplied` | Application update installed |
| `AppLocked` | Application locked (manual, timeout, session switch, or minimise) |
| `AppUnlocked` | Application unlocked |
| `MasterPasswordChanged` | Master password or PIN changed |

### Resilience

The audit logger opens files with `FileShare.ReadWrite` so external tools (log viewers, Splunk forwarders, etc.) can tail the file while the application is running. The writer is serialized with a `SemaphoreSlim` to ensure thread-safe appends.

IO failures (disk full, permission denied) are caught and re-emitted via Serilog. An audit write failure never crashes the application or blocks the operation that triggered the audit event.

## Log Redaction

### Serilog Destructuring Policy

A custom `IDestructuringPolicy` (`RedactSensitivePolicy`) is registered with Serilog. When Serilog destructures any object for structured logging, the policy checks each public property name against a denylist. Matching properties have their values replaced with `***REDACTED***` before the log event reaches any sink.

### Denylisted Property Names

The following property names are redacted (case-insensitive matching):

- `Password`
- `Secret`
- `Token`
- `CredentialData`
- `ApiKey`
- `ResolvedPassword`
- `MasterPassword`

This policy applies to all types -- including types added in future development -- without requiring per-type registration. A new POCO with a `Password` property will be automatically redacted.

### Log File Configuration

Serilog writes to rolling files at `%AppData%\Deskbridge\logs\`:

| Setting | Value |
| --- | --- |
| File pattern | `deskbridge-YYYYMMDD.log` |
| Rolling interval | Daily |
| Size limit per file | 10 MB |
| Roll on size limit | Yes |
| Retained file count | 5 |
| Flush interval | 1 second |
| Minimum level | Information |

## Auto-Lock

### Idle Timeout

A `DispatcherTimer` fires after a configurable period of inactivity (default: 15 minutes, minimum: 1 minute). The timer resets on mouse and keyboard input within the Deskbridge WPF UI.

Input originating inside a `WindowsFormsHost` (i.e., from an active RDP session) does NOT reset the idle timer. This is intentional: activity in a remote session is not Deskbridge UI activity. The filter walks up the visual/logical tree from the input source and skips the reset if a `WindowsFormsHost` ancestor is found.

### Windows Session Lock

Deskbridge subscribes to `SystemEvents.SessionSwitch` and triggers a lock on:

- `SessionLock` -- user locks the Windows session (Win+L)
- `ConsoleDisconnect` -- console session detached (e.g., fast user switching)
- `RemoteDisconnect` -- inbound RDP session to this machine disconnected

The event fires on a SystemEvents thread, so the handler marshals to the WPF dispatcher via `BeginInvoke` before publishing the lock event.

### Lock Overlay

When the application locks, the `AppLockController`:

1. Snapshots the `Visibility` state of every child element in the host container.
2. Sets all children to `Visibility.Collapsed` (including WindowsFormsHost elements).
3. Displays a `ContentDialog`-based lock overlay requiring the master password.
4. On successful unlock, restores each child to its pre-lock visibility state.

This prevents RDP session content from being visible through the WPF overlay, which would otherwise occur due to the WPF/WinForms airspace problem (WindowsFormsHost content always renders on top of WPF elements in the same z-order).

## No Telemetry

Deskbridge contains no telemetry, analytics, crash reporting, or usage tracking. The only outbound network calls are:

1. RDP connections initiated by the user.
2. HTTPS requests to the GitHub Releases API for update checks.

Both are user-initiated or explicitly configured. No data is sent to any third-party service.

## Export Safety

JSON and CSV exports contain connection metadata only (name, hostname, port, username, domain, protocol, group path). **Credentials are never included in exports.** The export functions read from `IConnectionStore`, which does not expose password data.
