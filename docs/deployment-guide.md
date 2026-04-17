# Deployment Guide

This guide covers installation, configuration, network requirements, and enterprise considerations for deploying Deskbridge.

## System Requirements

- **Operating system**: Windows 10 version 1809 or later, Windows 11
- **Runtime**: .NET 10 (included in self-contained builds)
- **Architecture**: x64
- **Privileges**: Standard user (no administrator rights required)
- **Disk space**: Approximately 150 MB for the self-contained installation

## Installation

### Per-User Installation

Deskbridge installs per-user to:

```
%LocalAppData%\Deskbridge
```

No administrator rights are required. The installer is packaged with [Velopack](https://velopack.io/) and places the application files in the user's local application data directory.

To install:

1. Download the latest release from [GitHub Releases](../../releases).
2. Run the installer (e.g., `Deskbridge-Setup.exe`).
3. The application launches automatically after installation.

### Silent Installation

Velopack supports silent installation via the `--silent` flag on the setup executable:

```
Deskbridge-Setup.exe --silent
```

This installs without displaying any UI prompts.

### Uninstall

Deskbridge can be uninstalled through:

- **Windows Settings**: Settings > Apps > Installed Apps > Deskbridge > Uninstall
- **Velopack uninstaller**: Located at `%LocalAppData%\Deskbridge`

Uninstalling removes the application files from `%LocalAppData%\Deskbridge`. User data in `%AppData%\Deskbridge` is preserved. To remove all user data, manually delete the `%AppData%\Deskbridge` directory after uninstalling.

## Data Locations

All user data is stored under `%AppData%\Deskbridge`:

| File/Directory | Purpose |
| --- | --- |
| `connections.json` | Connection definitions (hostnames, usernames, folders, display settings). No passwords. |
| `settings.json` | Window position, sidebar state, auto-lock timeout, update channel preference. No secrets. |
| `auth.json` | Master password hash (PBKDF2 envelope). No plaintext passwords. |
| `logs/` | Serilog rolling log files (`deskbridge-YYYYMMDD.log`). Daily rotation, 10 MB cap per file, 5 files retained. |
| `audit-YYYY-MM.jsonl` | Append-only audit log. Monthly rotation. Records lock/unlock, connect/disconnect, import/export events. |

Connection passwords are stored in **Windows Credential Manager** (not on the filesystem). Each connection uses a `DESKBRIDGE/CONN/{connectionId}` target with `CredentialType.Generic`. Group-level credentials use `DESKBRIDGE/GROUP/{groupId}`.

### Backup

To back up Deskbridge configuration:

1. Copy the entire `%AppData%\Deskbridge` directory.
2. Note that connection passwords are not included in this backup -- they are stored in Windows Credential Manager and are tied to the Windows user profile.

To restore, copy the backed-up directory to `%AppData%\Deskbridge` on the target machine. Users will need to re-enter connection passwords.

## Network Requirements

Deskbridge makes two categories of network calls:

### RDP Connections

Outbound TCP connections to remote hosts on the configured port (default 3389). These are standard Microsoft RDP connections initiated by the user. Firewall rules should allow outbound TCP to the target hosts and ports your infrastructure uses.

### Update Checks

Outbound HTTPS (port 443) to the GitHub Releases API:

```
https://api.github.com/repos/{owner}/{repo}/releases
```

This check runs silently on application startup. If the network is unavailable or the request fails, the application continues normally -- update checks are non-blocking and failure-tolerant.

No other network calls are made. There is no telemetry, analytics, crash reporting, or license server communication.

### Proxy Configuration

Deskbridge uses `HttpClient` defaults for update checks, which means it inherits the system proxy settings configured in Windows (Settings > Network & Internet > Proxy). No application-level proxy configuration is required.

## Credential Guard Compatibility

Deskbridge stores connection credentials in Windows Credential Manager using the target format `DESKBRIDGE/CONN/{connectionId}` with `CredentialType.Generic`.

This is deliberately different from the `TERMSRV/{hostname}` format used by the native Windows RDP client. On machines with **Windows Defender Credential Guard** enabled, `TERMSRV/*` entries trigger the error "Windows Defender Credential Guard does not allow using saved credentials" because Credential Guard intercepts these targets during CredSSP delegation.

Deskbridge avoids this conflict entirely because:

1. The `DESKBRIDGE/*` namespace is invisible to CredSSP negotiation.
2. Passwords are injected directly into the RDP ActiveX control via `IMsTscNonScriptable.ClearTextPassword`, bypassing CredSSP.

If upgrading from an earlier version that used `TERMSRV/` targets, Deskbridge performs a one-time automatic migration at startup, moving credentials from `TERMSRV/{hostname}` to `DESKBRIDGE/CONN/{connectionId}` and removing the old entries.

## Group Policy Interactions

Deskbridge does not read from or write to the Windows Registry. There are no Group Policy Administrative Templates (ADMX) provided. The application has no known conflicts with standard enterprise Group Policy configurations.

RDP-specific Group Policies (such as "Allow connections only from computers running Remote Desktop with Network Level Authentication") are handled by the Microsoft RDP client component (mstscax.dll), not by Deskbridge.

## Auto-Update

Deskbridge checks for updates on startup via the GitHub Releases API. The behavior is:

1. On launch, a background task checks for a newer version.
2. If an update is available, a badge appears in the status bar.
3. The user can choose to download and apply the update, which triggers a restart.

Update channels:

- **Stable**: Default. Receives only releases tagged without a prerelease suffix (e.g., `v1.0.1`).
- **Beta**: Opt-in via Settings. Receives prerelease versions (e.g., `v1.1.0-beta.1`).

The channel preference is stored in `settings.json` and can be changed in the application settings panel.

To disable update checks in an enterprise environment, block outbound HTTPS to `api.github.com` at the network level. The application handles update check failures gracefully and continues to function normally.

## Multi-User Machines

Because Deskbridge installs per-user and stores data per-user, multiple Windows users on the same machine can each have independent Deskbridge installations with separate connection databases and credentials. There is no shared state between users.

## Session Limits

Each Deskbridge instance supports approximately 15-20 simultaneous RDP sessions. This is a practical limit imposed by GDI handle consumption of the RDP ActiveX control, not a licensing or configuration restriction. A warning is displayed when approaching this limit.
