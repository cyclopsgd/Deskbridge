# Privacy Policy

**Deskbridge** is a local desktop application. It does not collect, transmit, or store any personal data beyond what is necessary for its core functionality on your machine.

## What Deskbridge stores locally

All data is stored in `%AppData%\Deskbridge` on your Windows machine. Nothing leaves your computer except RDP connections you initiate and update checks.

| Data | Location | Purpose |
|------|----------|---------|
| Connection metadata | `connections.json` | Hostnames, usernames, folder structure (no passwords) |
| Application settings | `settings.json` | Window position, auto-lock timeout, preferences |
| Master password hash | `auth.json` | PBKDF2 hash of your master password or PIN (not the password itself) |
| RDP credentials | Windows Credential Manager | Stored by Windows, protected by your Windows login |
| Audit log | `audit-YYYY-MM.jsonl` | Lock/unlock events, connection activity, imports (local only) |
| Application logs | `logs/deskbridge-YYYYMMDD.log` | Diagnostic logs with sensitive property values redacted |

## Network activity

Deskbridge makes exactly two types of network calls:

1. **RDP connections** — initiated by you to hosts you configure. Deskbridge does not choose or modify the destination.
2. **Update checks** — on startup, Deskbridge checks GitHub Releases (`api.github.com`) for new versions. No identifying information is sent. This can be observed in the audit log.

## What Deskbridge does NOT do

- No telemetry or analytics
- No crash reporting to external services
- No user tracking or device fingerprinting
- No cloud storage or sync
- No data sharing with third parties
- No advertising

## Credential handling

- RDP passwords are stored in Windows Credential Manager, protected by your Windows user profile (DPAPI)
- The master password/PIN is never stored — only a PBKDF2-derived hash
- Passwords are never written to log files (Serilog redaction policy filters sensitive property names)
- Exported connection files (JSON/CSV) never contain passwords

## Data deletion

Uninstalling Deskbridge removes the application files. To remove all user data, delete `%AppData%\Deskbridge`. RDP credentials in Windows Credential Manager can be removed via Windows Settings > Credential Manager or by deleting entries prefixed with `DESKBRIDGE/`.

## Contact

For privacy questions, open an issue at [github.com/cyclopsgd/Deskbridge](https://github.com/cyclopsgd/Deskbridge/issues).

---

*Last updated: 2026-04-17*
