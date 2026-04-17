# Deskbridge

A modern Windows desktop RDP connection manager built to replace mRemoteNG. Designed for infrastructure teams who manage dozens to hundreds of remote connections daily.

## Features

- **Tabbed RDP sessions** with proper ActiveX lifecycle management -- no freezes, no memory leaks
- **Connection management** with folders, drag-and-drop, credential inheritance
- **Command palette** (Ctrl+Shift+P) with fuzzy search across connections and commands
- **Global shortcuts** -- Ctrl+N (new connection), Ctrl+T (quick connect), F11 (fullscreen), Ctrl+L (lock)
- **Master password / PIN lock** with PBKDF2 encryption, idle auto-lock, and session-lock detection
- **Toast notifications** for connection events, imports, and updates
- **Auto-update** via Velopack with stable and beta channels
- **mRemoteNG import** with tree preview and per-duplicate resolution
- **JSON and CSV export** (no credentials in output)
- **Structured logging** with Serilog (rolling file + monthly audit log)
- **Dark Fluent UI** via WPF-UI with Mica backdrop

## Screenshots

*Coming soon*

## Requirements

- Windows 10 version 1809 or later
- .NET 10 runtime (included in self-contained builds)

## Installation

Download the latest release from [GitHub Releases](../../releases). Run the installer -- it installs per-user to `%LocalAppData%\Deskbridge` (no admin rights required).

Updates are checked silently on startup. When available, a badge appears in the status bar.

## Building from Source

```bash
# Clone
git clone https://github.com/cyclopsgd/Deskbridge.git
cd Deskbridge

# Build
dotnet build Deskbridge.sln

# Run
dotnet run --project src/Deskbridge/Deskbridge.csproj

# Test
dotnet test Deskbridge.sln
```

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10+ (WPF dependency)

## Project Structure

```
src/
  Deskbridge/                 # WPF application (UI, ViewModels, dialogs)
  Deskbridge.Core/            # Business logic, interfaces, services, models
  Deskbridge.Protocols.Rdp/   # RDP ActiveX interop (AxMsRdpClient9)
tests/
  Deskbridge.Tests/           # Unit and integration tests
```

## Technology

| Component | Technology |
|-----------|-----------|
| Framework | .NET 10 (C# 14) |
| UI | WPF with [WPF-UI](https://github.com/lepoco/wpfui) Fluent theme |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| RDP | AxMsRdpClient9 via classic COM interop |
| Logging | [Serilog](https://serilog.net/) with rolling file sink |
| Auto-update | [Velopack](https://velopack.io/) |
| Credentials | Windows Credential Manager via [AdysTech.CredentialManager](https://github.com/nickvdyck/adystech-credentialmanager) |
| CI/CD | GitHub Actions |

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+Shift+P | Command palette |
| Ctrl+N | New connection |
| Ctrl+T | Quick connect |
| Ctrl+L | Lock application |
| Ctrl+W / Ctrl+F4 | Close active tab |
| Ctrl+Tab | Next tab |
| Ctrl+Shift+Tab | Previous tab |
| F11 | Toggle fullscreen |
| Esc | Exit fullscreen / close palette |

## Security

- Master password or 6-digit PIN protects the UI at rest (PBKDF2-HMAC-SHA256, 600k iterations)
- Credentials stored in Windows Credential Manager (not in config files)
- Idle auto-lock with configurable timeout
- Windows session-lock auto-locks the app
- Audit log records lock/unlock events, imports, and connection activity
- No passwords in logs (Serilog redaction policy)
- No credentials in JSON/CSV exports

## Importing from mRemoteNG

1. Open the command palette (Ctrl+Shift+P)
2. Type "Import" and select "Import connections"
3. Select your `confCons.xml` file
4. Preview connections in the tree view, uncheck any you don't want
5. Click Import

Note: passwords are not imported. You will need to re-enter credentials for each connection after import.

## License

This project is licensed under the GNU General Public License v3.0 -- see [LICENSE](LICENSE) for details.
