# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - Unreleased

### Added

- Tabbed RDP sessions with proper ActiveX lifecycle management (AxMsRdpClient9 via classic COM interop)
- Connection management with folders, drag-and-drop reordering, and credential inheritance (Inherit/Own/Prompt modes)
- Credential inheritance: connections resolve credentials by walking up the group tree until a match is found
- Group-level credentials stored in Windows Credential Manager under `DESKBRIDGE/GROUP/{guid}` targets
- Connection-specific credentials stored under `DESKBRIDGE/CONN/{connectionId}` targets
- Connection editor dialog with General, Credentials, Display, and Notes tabs
- Group editor with credential assignment and inheritance count display
- Command palette (Ctrl+Shift+P) with fuzzy search across connections and commands
- Quick connect (Ctrl+T) for ad-hoc hostname connections
- Global keyboard shortcuts: Ctrl+N (new connection), Ctrl+W (close tab), Ctrl+Tab/Ctrl+Shift+Tab (cycle tabs), F11 (fullscreen), Ctrl+L (lock), Esc (exit fullscreen/close palette)
- Master password and 6-digit PIN lock with PBKDF2-HMAC-SHA256 (600,000 iterations, 256-bit salt, 256-bit derived key)
- Idle auto-lock with configurable timeout (default 15 minutes)
- Windows session-lock detection (SessionLock, ConsoleDisconnect, RemoteDisconnect triggers)
- Lock overlay that collapses WindowsFormsHost children to prevent RDP pixel leakage through WPF airspace
- Toast notification stack for connection events, imports, and update availability
- Auto-update via Velopack with stable and beta channel support (GitHub Releases)
- Silent update check on startup with status bar badge notification
- mRemoteNG import wizard with tree preview and per-duplicate resolution (metadata only, no password import)
- JSON and CSV export (credentials excluded from all exports)
- Structured logging via Serilog with daily rolling files (10 MB cap, 5 retained files)
- Append-only audit log in JSONL format with monthly rotation (records lock/unlock, connect/disconnect, import/export, credential changes)
- Log redaction policy denylisting Password, Secret, Token, CredentialData, ApiKey, ResolvedPassword, and MasterPassword property names
- Dark Fluent UI via WPF-UI 4.2.0 with Mica backdrop and custom #007ACC accent colour
- VS Code-style layout: 36px icon rail, 240px slide-out panel, single-row tab bar, 22px status bar
- RDP reconnection overlay with exponential backoff (2s, 4s, 8s, max 30s)
- Airspace flicker mitigation: bitmap snapshot swap during window drag/resize (WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE)
- Window state persistence (position, size, maximised, sidebar state) in settings.json
- Global exception handler with crash dialog
- Credential Guard compatibility: DESKBRIDGE/CONN/ target format avoids TERMSRV/ CredSSP delegation conflicts
- One-time migration from legacy TERMSRV/ credential targets to DESKBRIDGE/CONN/ format
- Change password/PIN dialog for updating master password at runtime
- CI/CD pipeline via GitHub Actions (build, test, publish, release with Velopack packaging)

### Security

- Master password hash stored using PBKDF2-HMAC-SHA256 with 600,000 iterations (OWASP 2023 guidance) and 256-bit random salt
- Hash verification uses constant-time comparison (CryptographicOperations.FixedTimeEquals)
- auth.json stores only the versioned hash envelope (`v1.<base64 salt>.<base64 key>`), never plaintext passwords
- Connection passwords managed by Windows Credential Manager (DPAPI-protected by the OS)
- Serilog destructuring policy redacts sensitive property names before values reach any log sink
- No credentials included in JSON or CSV exports
- No telemetry, analytics, or cloud services (only outbound call is HTTPS to GitHub Releases API for update checks)
- Audit log records all security-relevant events (lock/unlock, credential changes, imports)
- Per-connection error isolation prevents one failed RDP session from affecting others
