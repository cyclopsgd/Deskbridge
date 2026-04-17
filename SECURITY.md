# Security Policy

## Supported Versions

| Version | Supported |
| ------- | --------- |
| 1.0.x   | Yes       |

## Reporting a Vulnerability

If you discover a security vulnerability in Deskbridge, please report it responsibly through one of these channels:

- **GitHub Security Advisories** (preferred): Navigate to the [Security tab](../../security/advisories) of this repository and create a new advisory. This ensures the report is encrypted and visible only to maintainers.
- **Email**: Send details to the repository owner via the contact information on their GitHub profile.

Please include:

- A description of the vulnerability and its potential impact
- Steps to reproduce the issue
- The version of Deskbridge you are using
- Your Windows version

We aim to acknowledge reports within 48 hours and provide an initial assessment within 7 days. Critical vulnerabilities will be patched and released as soon as a fix is verified.

Do not open a public issue for security vulnerabilities.

## Security Model Summary

### Master Password Protection

Deskbridge gates access to the application UI with a master password or 6-digit PIN set by the user on first launch. The password is hashed using:

- **Algorithm**: PBKDF2-HMAC-SHA256 (via `Rfc2898DeriveBytes.Pbkdf2`)
- **Iterations**: 600,000 (per OWASP 2023 guidance for PBKDF2-HMAC-SHA256)
- **Salt**: 256-bit (32 bytes), randomly generated per password via `RandomNumberGenerator.GetBytes`
- **Derived key**: 256-bit (32 bytes)
- **Storage format**: `v1.<base64 salt>.<base64 key>` in `%AppData%\Deskbridge\auth.json`
- **Verification**: Constant-time comparison via `CryptographicOperations.FixedTimeEquals`

The version prefix (`v1`) reserves the envelope shape for future algorithm upgrades (e.g., Argon2id). No plaintext password is ever written to disk.

### Credential Storage

Connection passwords are stored in **Windows Credential Manager** using `CredentialType.Generic` with the following target formats:

- Per-connection: `DESKBRIDGE/CONN/{connectionId}`
- Per-group: `DESKBRIDGE/GROUP/{groupId}`

Windows Credential Manager entries are protected by DPAPI, which is tied to the Windows user login session. Deskbridge does not implement its own encryption for connection passwords -- it delegates to the OS credential store.

The `DESKBRIDGE/` prefix is used instead of `TERMSRV/` to avoid conflicts with Windows Credential Guard, which intercepts `TERMSRV/*` entries for CredSSP delegation.

### What Is NOT Stored as Plaintext Secrets

- **auth.json**: Contains only the PBKDF2 hash envelope and schema version. No plaintext passwords.
- **connections.json**: Contains hostnames, usernames, display settings, and credential mode (Inherit/Own/Prompt). No passwords.
- **Log files**: A Serilog destructuring policy redacts property names matching: Password, Secret, Token, CredentialData, ApiKey, ResolvedPassword, MasterPassword. Redacted values are replaced with `***REDACTED***` before reaching any sink.
- **Exports**: JSON and CSV exports contain connection metadata only. Credentials are excluded.

### What Is Plaintext

- **settings.json**: Window position, sidebar state, auto-lock timeout, update channel preference. Contains no secrets.
- **connections.json**: Hostnames, usernames, port numbers, folder structure, tags. This is metadata -- passwords are in Windows Credential Manager.

### Audit Logging

An append-only JSONL audit log records security-relevant events at `%AppData%\Deskbridge\audit-YYYY-MM.jsonl` (monthly rotation). Each record contains:

- ISO-8601 UTC timestamp
- Event type (Connected, Disconnected, AppLocked, AppUnlocked, ConnectionsImported, CredentialStored, MasterPasswordChanged, etc.)
- Connection ID (when applicable)
- Windows username
- Outcome (success/fail)
- Optional error code

The audit log file is opened with `FileShare.ReadWrite` so it can be tailed by external tools while the application is running. IO failures in the audit writer are swallowed and logged via Serilog -- audit failures never crash the application.

### Auto-Lock

- **Idle timeout**: Configurable (default 15 minutes). A `DispatcherTimer` resets on mouse/keyboard input within the Deskbridge UI. Input originating inside an RDP session (WindowsFormsHost) does NOT reset the timer.
- **Session lock**: Subscribes to `SystemEvents.SessionSwitch`. Triggers lock on SessionLock, ConsoleDisconnect, and RemoteDisconnect events.
- **Lock on minimise**: Optional setting (default off).
- **Manual lock**: Ctrl+L.

When locked, all WindowsFormsHost children are collapsed to prevent RDP session pixels from rendering through the WPF airspace layer. Sessions remain connected behind the lock overlay.

### No Telemetry

Deskbridge makes no network calls except:

- **RDP connections** initiated by the user to their specified hosts
- **HTTPS to GitHub Releases API** for update checks (via Velopack's `GithubSource`)

No analytics, crash reporting, usage metrics, or phone-home functionality exists in the application. Proxy settings are inherited from the system (HttpClient defaults).

## Out of Scope

The following are outside the scope of Deskbridge's security model:

- **RDP protocol security**: The Remote Desktop Protocol encryption, NLA/CredSSP negotiation, TLS transport, and certificate validation are handled entirely by Microsoft's RDP client (mstscax.dll). Deskbridge wraps the ActiveX control; it does not implement or modify the protocol.
- **Windows Credential Manager security**: DPAPI protection of stored credentials is an OS-level feature. Vulnerabilities in DPAPI or Windows Credential Manager should be reported to Microsoft.
- **Host-side security**: Security configuration of the remote machines being connected to (firewall rules, RDP server settings, Group Policy) is the responsibility of the infrastructure team, not Deskbridge.
