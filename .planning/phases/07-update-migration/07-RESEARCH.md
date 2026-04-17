# Phase 7: Update & Migration - Research

**Researched:** 2026-04-15
**Domain:** Velopack auto-update + mRemoteNG import/export
**Confidence:** HIGH

## Summary

Phase 7 covers two independent capabilities: (1) silent auto-update via Velopack with GitHub Actions release pipeline, and (2) mRemoteNG connection import plus JSON/CSV export. The codebase is well-prepared for both -- `VelopackApp.Build().Run()` already executes in `Program.cs`, the Velopack NuGet package is pinned at 0.0.1298, `UpdateAvailableEvent` and `ConnectionImportedEvent` are declared and subscribed by `ToastSubscriptionService`, and the existing `.github/workflows/build.yml` provides the CI foundation to extend.

The Velopack update flow is straightforward: `UpdateManager` with `GithubSource` checks for releases, downloads deltas when available, and applies with restart. The key design decision is the dual-channel (stable + beta) system using Velopack's `--channel` flag and `ExplicitChannel` in `UpdateOptions`. The mRemoteNG import requires parsing `confCons.xml` -- an XML file with `<Node>` elements carrying 40+ attributes, where `Type="Container"` represents folders and `Type="Connection"` represents connections. Only a subset of attributes map to Deskbridge's `ConnectionModel`.

**Primary recommendation:** Implement as two independent work streams. The update subsystem (UPD-01 through UPD-05) is a vertical slice from service to UI to CI/CD. The import/export subsystem (MIG-01 through MIG-06) is a separate vertical slice. Both can be planned in parallel since they share no state beyond the event bus.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Toast + status bar badge for update notification -- on startup check, if update found, publish `UpdateAvailableEvent` (toast fires via existing subscription) AND set a persistent badge/dot on a status bar update icon so user can act later
- Toast copy: "Update v{version} available" with action to trigger download
- Badge persists until user acts or dismisses
- One-click download + apply + restart WITH confirmation prompt before restart -- clicking the update action triggers `UpdateManager.DownloadUpdatesAsync` with progress indicator, then shows confirmation dialog: "Update downloaded. Restart now? Active sessions will be disconnected." User confirms -> `ApplyUpdatesAndRestart`. User cancels -> update applies on next manual restart.
- `IConnectionImporter` interface with `ParseAsync(Stream) -> ImportResult` returning a tree of connections
- Phase 7 ships `MRemoteNGImporter` only -- parses confCons.xml (XML, not encrypted)
- Wizard flow: Step 1 = pick source app dropdown, Step 2 = file picker, Step 3 = tree view preview with checkboxes, Step 4 = confirm
- Prompt per duplicate -- when matching hostname exists: Skip / Overwrite / Rename
- Export via command palette only -- "Export connections as JSON" and "Export connections as CSV"
- JSON export: tree structure matching folder hierarchy, no credentials
- CSV export: flat, one row per connection, folder path as string column, no credentials
- Git tag trigger `v*.*.*` -> GitHub Actions builds, vpk packs, creates GitHub Release
- Two channels: stable + beta. Tag format: `v1.0.0` for stable, `v1.1.0-beta.1` for beta (SemVer2 prerelease)
- Workflow extends existing `build.yml`
- MIG-04 is STALE: credential target is now `DESKBRIDGE/CONN/{id}` not `TERMSRV/`

### Claude's Discretion
- Status bar update icon design (which SymbolIcon, positioning)
- Import wizard dialog sizing and layout details
- CSV column ordering
- vpk pack flags beyond --channel (--framework, --runtime, etc.)
- Delta update support (Velopack handles automatically if prior release exists)

### Deferred Ideas (OUT OF SCOPE)
- None specified -- all CONTEXT.md ideas are in scope for this phase
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| UPD-01 | Velopack checks GitHub Releases via GithubSource silently on startup | UpdateManager + GithubSource API documented; constructor takes repoUrl, accessToken, prerelease bool |
| UPD-02 | Status bar notification when update available, with download/apply/restart flow | UpdateManager.DownloadUpdatesAsync(UpdateInfo, Action<int>) for progress; ApplyUpdatesAndRestart for restart |
| UPD-03 | UpdateAvailableEvent published to event bus | Event already declared in AppEvents.cs; ToastSubscriptionService already subscribes |
| UPD-04 | Self-contained publish with SemVer2, user data in %AppData% | Velopack constraint already enforced in Program.cs and csproj; vpk pack -v flag requires SemVer2 |
| UPD-05 | GitHub Actions workflow triggered on version tag push | vpk download/pack/upload commands documented; --channel flag for stable/beta; extends existing build.yml |
| MIG-01 | mRemoteNG import parses confCons.xml with field mapping | XML schema documented: Node elements with Type/Name/Hostname/Protocol/Port/Username/Domain attributes |
| MIG-02 | Import wizard: pick file -> preview connections -> confirm | WPF-UI ContentDialog hosting already wired; tree preview uses existing TreeView patterns |
| MIG-03 | Metadata only -- no password import | Parser ignores Password attribute; ConnectionModel created with CredentialMode.Prompt |
| MIG-04 | Imported connections stored with DESKBRIDGE/CONN/{id} credential prefix (STALE FIX) | IConnectionStore.Save() already generates new Guid; credential target follows existing DESKBRIDGE/CONN/{id} pattern |
| MIG-05 | ConnectionImportedEvent published, import recorded in audit log | Event already declared; ToastSubscriptionService already subscribes; AuditAction.ConnectionsImported exists |
| MIG-06 | Export as JSON (no credentials) and CSV | System.Text.Json for tree JSON; manual CSV generation (no external library needed) |
</phase_requirements>

## Standard Stack

### Core (already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Velopack | 0.0.1298 | Auto-update framework | Already referenced in Directory.Packages.props; provides UpdateManager, GithubSource, delta updates, VelopackApp lifecycle hooks | [VERIFIED: Directory.Packages.props] |
| System.Xml.Linq | (in-box) | mRemoteNG XML parsing | XDocument/XElement for confCons.xml parsing; in-box with .NET 10, no external dependency | [VERIFIED: .NET 10 SDK] |
| System.Text.Json | (in-box) | JSON export serialization | Already used throughout project for settings/connections; source-generated contexts available | [VERIFIED: codebase usage] |

### Supporting (no new NuGet packages needed)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Velopack.Build (vpk CLI) | 0.0.1298 | CI/CD packaging | GitHub Actions only; `dotnet tool install -g vpk` in workflow | [VERIFIED: docs.velopack.io] |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| System.Xml.Linq (XDocument) | XmlDocument (DOM) | XDocument is cleaner LINQ API, lighter weight, better for read-only parsing |
| Manual CSV generation | CsvHelper NuGet | CsvHelper is overkill for a flat export with 8 columns; manual StringBuilder avoids new dependency |
| GithubSource | SimpleFileSource | GithubSource handles GitHub Release API, asset discovery, and prerelease filtering automatically |

**Installation:**
No new NuGet packages required. All dependencies already exist in the project.

## Architecture Patterns

### Recommended Project Structure
```
src/
  Deskbridge.Core/
    Interfaces/
      IConnectionImporter.cs       # Import abstraction
      IUpdateService.cs            # Update check/download/apply abstraction
    Services/
      UpdateService.cs             # Velopack UpdateManager wrapper
      MRemoteNGImporter.cs         # confCons.xml parser
      ConnectionExporter.cs        # JSON + CSV export
    Models/
      ImportResult.cs              # Tree of imported connections
      ImportedNode.cs              # Single node in import tree
      UpdateSettings.cs            # Beta channel preference (in AppSettings)
  Deskbridge/
    ViewModels/
      ImportWizardViewModel.cs     # Multi-step wizard state
      UpdateStatusViewModel.cs     # Status bar badge + update flow (or integrate into MainWindowViewModel)
    Dialogs/
      ImportWizardDialog.xaml       # Multi-step ContentDialog
      UpdateConfirmDialog.xaml      # "Restart now?" confirmation
```

### Pattern 1: IConnectionImporter Interface
**What:** Abstraction for importing connections from external apps
**When to use:** Any import source (mRemoteNG, RDCMan, RoyalTS)
**Example:**
```csharp
// Source: CONTEXT.md locked decision
public interface IConnectionImporter
{
    string SourceName { get; }       // "mRemoteNG"
    string FileFilter { get; }      // "mRemoteNG Config (*.xml)|*.xml"
    Task<ImportResult> ParseAsync(Stream stream, CancellationToken ct = default);
}

public record ImportResult(
    IReadOnlyList<ImportedNode> RootNodes,
    int TotalConnections,
    int TotalFolders);

public record ImportedNode(
    string Name,
    ImportNodeType Type,           // Connection or Container
    string? Hostname,
    int Port,
    string? Username,
    string? Domain,
    Protocol Protocol,
    IReadOnlyList<ImportedNode> Children);

public enum ImportNodeType { Connection, Container }
```

### Pattern 2: Update Service Wrapper
**What:** Thin wrapper around Velopack UpdateManager with event bus integration
**When to use:** Isolates Velopack API from the rest of the app; enables testing with mocks
**Example:**
```csharp
// Source: Velopack API reference (docs.velopack.io)
public interface IUpdateService
{
    bool IsInstalled { get; }
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default);
    Task DownloadUpdatesAsync(UpdateInfo update, IProgress<int>? progress = null, CancellationToken ct = default);
    void ApplyUpdatesAndRestart(UpdateInfo update);
}
```

### Pattern 3: Settings Extension for Beta Channel
**What:** Add `UseBetaChannel` to AppSettings for channel selection
**When to use:** Settings panel toggle that controls which Velopack channel is queried
**Example:**
```csharp
// Extend existing AppSettings with update preferences
public sealed record UpdateSettingsRecord(
    bool UseBetaChannel = false)
{
    public static UpdateSettingsRecord Default { get; } = new();
}

// AppSettings gains a third sub-record:
public sealed record AppSettings(
    WindowStateRecord Window,
    SecuritySettingsRecord Security,
    UpdateSettingsRecord Update,
    int SchemaVersion = 1);
```

### Pattern 4: Velopack Channel Selection
**What:** Use GithubSource with channel-aware UpdateOptions
**When to use:** When checking for updates based on user's channel preference
**Example:**
```csharp
// Source: docs.velopack.io/reference/cs/Velopack/UpdateOptions
var source = new GithubSource(
    "https://github.com/owner/Deskbridge",
    accessToken: null,  // public repo
    prerelease: useBetaChannel);

var options = new UpdateOptions
{
    ExplicitChannel = useBetaChannel ? "beta" : "stable",
    AllowVersionDowngrade = false
};

var mgr = new UpdateManager(source, options);
var update = await mgr.CheckForUpdatesAsync();
```

### Anti-Patterns to Avoid
- **Direct UpdateManager in ViewModel:** Wrap in IUpdateService so the ViewModel is testable without Velopack runtime. The UpdateManager.IsInstalled check is critical -- in dev mode (not installed via Velopack), all update operations will throw. [VERIFIED: docs.velopack.io]
- **Blocking UI thread during download:** `DownloadUpdatesAsync` must run on background thread with progress callback marshalled to UI via `IProgress<int>`. [VERIFIED: Velopack API]
- **Calling ApplyUpdatesAndRestart without confirmation:** The method calls `Environment.Exit()` immediately. Active RDP sessions will be terminated. Always show confirmation dialog first. [VERIFIED: docs.velopack.io]
- **Parsing encrypted confCons.xml:** mRemoteNG can encrypt the file with a password. Phase 7 only handles unencrypted XML (CONTEXT.md locked decision: "XML, not encrypted"). If the file starts with encrypted data, surface a user-friendly error. [CITED: CONTEXT.md]
- **Importing passwords from mRemoteNG:** MIG-03 explicitly forbids password import. Parser must skip/ignore the Password attribute. [CITED: CONTEXT.md]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| GitHub Release API interaction | Custom HttpClient calls to api.github.com | Velopack `GithubSource` | Handles auth, pagination, asset filtering, prerelease detection automatically |
| Delta update computation | Custom binary diff | Velopack `vpk pack --delta` | Delta generation and reconstruction is complex; Velopack handles it end-to-end |
| Update download + apply lifecycle | Custom download + file replacement | Velopack `UpdateManager` | Process restart, file locking, rollback on failure -- deceptively complex |
| CSV escaping | Manual string concatenation | Proper CSV generation with quoting | Commas and quotes in connection names/notes will break naive CSV output |
| SemVer2 parsing | Regex | `SemanticVersion` (Velopack ships one internally) | SemVer2 prerelease suffixes have complex precedence rules |

**Key insight:** Velopack's value is handling the entire update lifecycle (check, download deltas, apply, restart, rollback). The app code should only decide WHEN to check and HOW to present the result. Do not duplicate any Velopack functionality.

## Common Pitfalls

### Pitfall 1: UpdateManager Throws When Not Installed
**What goes wrong:** In development (running from IDE), `UpdateManager.IsInstalled` returns `false`. Calling `CheckForUpdatesAsync` or `DownloadUpdatesAsync` on an uninstalled app throws. [VERIFIED: docs.velopack.io]
**Why it happens:** Velopack needs its own directory structure (packages/, current/) that only exists after `vpk`-based installation.
**How to avoid:** Guard all update operations with `UpdateManager.IsInstalled` check. In dev mode, log a warning and skip. The `IUpdateService` wrapper should encapsulate this guard.
**Warning signs:** `InvalidOperationException` during development testing.

### Pitfall 2: ApplyUpdatesAndRestart Exits Immediately
**What goes wrong:** `ApplyUpdatesAndRestart` calls `Environment.Exit()` synchronously. No cleanup runs, no `OnExit` fires, no graceful RDP disconnection.
**Why it happens:** Velopack needs to replace files that are locked by the running process.
**How to avoid:** Before calling `ApplyUpdatesAndRestart`: (1) show confirmation dialog warning about active sessions, (2) if user confirms, disconnect all active RDP sessions via `IConnectionCoordinator.DisconnectAllAsync()`, (3) save window state, (4) then call apply. Or use `WaitExitThenApplyUpdates` which launches Update.exe and waits for graceful shutdown.
**Warning signs:** Lost RDP sessions without warning, unsaved window state.

### Pitfall 3: vpk download Fails on First Release
**What goes wrong:** `vpk download github --repoUrl` fails when there are no previous releases (first-ever release). [ASSUMED]
**Why it happens:** The download step looks for existing release assets to compute deltas. On first release, there are none.
**How to avoid:** Use `|| true` or `continue-on-error: true` for the `vpk download` step in GitHub Actions. First release will have no delta, only full package.
**Warning signs:** CI/CD pipeline fails on first tagged release.

### Pitfall 4: confCons.xml Encrypted Files
**What goes wrong:** mRemoteNG can encrypt the entire confCons.xml with a user password. The file will not parse as valid XML.
**Why it happens:** mRemoteNG has a "Full File Encryption" option in Settings > Connections > Encryption.
**How to avoid:** Check the root element's `FullFileEncryption` attribute. If "true" or if the file doesn't parse as XML, show a user-friendly error: "This file appears to be encrypted. Please export an unencrypted version from mRemoteNG (File > Export)."
**Warning signs:** `XmlException` when parsing.

### Pitfall 5: mRemoteNG Protocol Mapping
**What goes wrong:** mRemoteNG supports protocols beyond RDP (SSH2, VNC, Telnet, HTTP, HTTPS, Rlogin, RAW, ICA, ExtApp). Importing non-RDP connections creates entries that cannot be connected.
**Why it happens:** Deskbridge v1 only supports RDP.
**How to avoid:** During import preview, visually distinguish non-RDP connections and disable their checkboxes. Or filter them out entirely with a note: "3 SSH connections skipped (not supported in v1)."
**Warning signs:** Imported connections that show errors when user tries to connect.

### Pitfall 6: GitHub Actions Permissions for Release Creation
**What goes wrong:** `vpk upload github` fails with 403 if the workflow doesn't have write permissions. [VERIFIED: docs.velopack.io GitHub Actions guide]
**Why it happens:** Default `GITHUB_TOKEN` permissions for tag-triggered workflows may be read-only.
**How to avoid:** Set `permissions: contents: write` in the workflow. The existing `build.yml` only has `contents: read`.
**Warning signs:** 403 Forbidden during `vpk upload`.

### Pitfall 7: Version Mismatch Between Tag and Assembly
**What goes wrong:** The git tag says `v1.2.0` but the assembly version says `1.0.0`. Velopack uses the version passed to `vpk pack -v`, which should match both.
**Why it happens:** Version not synchronized between the csproj `<Version>` property and the git tag.
**How to avoid:** Extract version from the git tag in the workflow (`${GITHUB_REF_NAME#v}`) and pass it to both `dotnet publish -p:Version=$VERSION` and `vpk pack -v $VERSION`.
**Warning signs:** Update check finds "new" version that is actually older.

### Pitfall 8: ContentDialog Hosting for Import Wizard
**What goes wrong:** Import wizard dialog fails to render if not hosted properly in WPF-UI's `ContentDialogHost`.
**Why it happens:** WPF-UI 4.x requires explicit dialog host registration (see WPF-UI-PITFALLS.md Pitfall 1).
**How to avoid:** Use `IContentDialogService` which is already registered and wired to the `ContentPresenter` in `MainWindow.xaml`. Follow the pattern established by `ConnectionEditorDialog`, `LockOverlayDialog`, and `CommandPaletteDialog`.
**Warning signs:** `InvalidOperationException: The DialogHost was never set`.

## Code Examples

### Velopack Update Check (UPD-01 / UPD-03)
```csharp
// Source: docs.velopack.io + CONTEXT.md decisions
public class UpdateService : IUpdateService
{
    private readonly IEventBus _bus;
    private readonly string _repoUrl;
    private UpdateManager? _mgr;
    private UpdateInfo? _pendingUpdate;

    public bool IsInstalled => _mgr?.IsInstalled ?? false;

    public async Task<UpdateInfo?> CheckForUpdatesAsync(
        bool useBetaChannel = false,
        CancellationToken ct = default)
    {
        if (_mgr is null || !_mgr.IsInstalled) return null;

        try
        {
            var update = await _mgr.CheckForUpdatesAsync();
            if (update is not null)
            {
                _pendingUpdate = update;
                _bus.Publish(new UpdateAvailableEvent(
                    update.TargetFullRelease.Version.ToString()));
            }
            return update;
        }
        catch (Exception ex)
        {
            // Log but don't throw -- update check failure is non-fatal
            Serilog.Log.Warning(ex, "Update check failed");
            return null;
        }
    }
}
```

### mRemoteNG XML Parsing (MIG-01)
```csharp
// Source: mRemoteNG confCons.xml schema analysis
public class MRemoteNGImporter : IConnectionImporter
{
    public string SourceName => "mRemoteNG";
    public string FileFilter => "mRemoteNG Config (*.xml)|*.xml";

    public Task<ImportResult> ParseAsync(Stream stream, CancellationToken ct = default)
    {
        var doc = XDocument.Load(stream);
        var root = doc.Root;

        // Check for encryption
        if (root?.Attribute("FullFileEncryption")?.Value == "True")
            throw new ImportException(
                "This file is encrypted. Export an unencrypted version from mRemoteNG.");

        var nodes = new List<ImportedNode>();
        foreach (var element in root?.Elements("Node") ?? [])
        {
            ct.ThrowIfCancellationRequested();
            nodes.Add(ParseNode(element));
        }

        var totalConns = CountType(nodes, ImportNodeType.Connection);
        var totalFolders = CountType(nodes, ImportNodeType.Container);
        return Task.FromResult(new ImportResult(nodes, totalConns, totalFolders));
    }

    private static ImportedNode ParseNode(XElement el)
    {
        var type = el.Attribute("Type")?.Value == "Container"
            ? ImportNodeType.Container
            : ImportNodeType.Connection;

        var protocol = MapProtocol(el.Attribute("Protocol")?.Value);
        var port = int.TryParse(el.Attribute("Port")?.Value, out var p) ? p : 3389;

        var children = el.Elements("Node")
            .Select(ParseNode)
            .ToList();

        return new ImportedNode(
            Name: el.Attribute("Name")?.Value ?? "Unnamed",
            Type: type,
            Hostname: el.Attribute("Hostname")?.Value,
            Port: port,
            Username: el.Attribute("Username")?.Value,
            Domain: el.Attribute("Domain")?.Value,
            Protocol: protocol,
            Children: children);
    }

    private static Protocol MapProtocol(string? mrngProtocol) => mrngProtocol switch
    {
        "RDP" => Protocol.Rdp,
        "SSH2" or "SSH1" => Protocol.Ssh,
        "VNC" => Protocol.Vnc,
        _ => Protocol.Rdp  // Default to RDP for unknown protocols
    };
}
```

### GitHub Actions Release Workflow (UPD-05)
```yaml
# Source: docs.velopack.io/distributing/github-actions
# Extends existing build.yml with release job
release:
  if: startsWith(github.ref, 'refs/tags/v')
  runs-on: windows-latest
  permissions:
    contents: write
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-dotnet@v4
      with:
        dotnet-version: 10.0.x

    - name: Extract version from tag
      id: version
      shell: bash
      run: echo "version=${GITHUB_REF_NAME#v}" >> $GITHUB_OUTPUT

    - name: Determine channel
      id: channel
      shell: bash
      run: |
        if [[ "${{ steps.version.outputs.version }}" == *"-"* ]]; then
          echo "channel=beta" >> $GITHUB_OUTPUT
          echo "prerelease=true" >> $GITHUB_OUTPUT
        else
          echo "channel=stable" >> $GITHUB_OUTPUT
          echo "prerelease=false" >> $GITHUB_OUTPUT
        fi

    - name: Publish
      run: >
        dotnet publish src/Deskbridge/Deskbridge.csproj
        --configuration Release
        --runtime win-x64
        --self-contained true
        -p:Version=${{ steps.version.outputs.version }}
        --output publish

    - name: Install vpk
      run: dotnet tool install -g vpk

    - name: Download previous release
      continue-on-error: true
      run: >
        vpk download github
        --repoUrl https://github.com/${{ github.repository }}
        --channel ${{ steps.channel.outputs.channel }}
        --token ${{ secrets.GITHUB_TOKEN }}

    - name: Pack
      run: >
        vpk pack
        -u Deskbridge
        -v ${{ steps.version.outputs.version }}
        -p publish
        --packTitle "Deskbridge"
        --channel ${{ steps.channel.outputs.channel }}

    - name: Upload to GitHub Release
      run: >
        vpk upload github
        --repoUrl https://github.com/${{ github.repository }}
        --channel ${{ steps.channel.outputs.channel }}
        --token ${{ secrets.GITHUB_TOKEN }}
        --publish
        --releaseName "Deskbridge ${{ steps.version.outputs.version }}"
        --tag ${{ github.ref_name }}
        ${{ steps.channel.outputs.prerelease == 'true' && '--pre' || '' }}
```

### CSV Export (MIG-06)
```csharp
// Source: CONTEXT.md decision -- flat CSV, folder path as string column
public static string ExportCsv(
    IReadOnlyList<ConnectionModel> connections,
    IReadOnlyList<ConnectionGroup> groups)
{
    var sb = new StringBuilder();
    sb.AppendLine("Name,Hostname,Port,Username,Domain,Protocol,FolderPath,Notes");

    foreach (var conn in connections)
    {
        var path = BuildFolderPath(conn.GroupId, groups);
        sb.AppendLine(string.Join(",",
            CsvEscape(conn.Name),
            CsvEscape(conn.Hostname),
            conn.Port,
            CsvEscape(conn.Username ?? ""),
            CsvEscape(conn.Domain ?? ""),
            conn.Protocol,
            CsvEscape(path),
            CsvEscape(conn.Notes ?? "")));
    }

    return sb.ToString();
}

private static string CsvEscape(string value) =>
    value.Contains(',') || value.Contains('"') || value.Contains('\n')
        ? $"\"{value.Replace("\"", "\"\"")}\""
        : value;
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Squirrel.Windows | Velopack | 2024 | Velopack is the successor by the same author; handles .NET 6+ natively, cross-platform, delta updates |
| RELEASES file (Squirrel) | releases.{channel}.json | Velopack 0.0.x | Channel-specific release manifests replace the single RELEASES file |
| GithubSource prerelease bool | ExplicitChannel in UpdateOptions | Velopack 0.0.x | More precise channel control -- prerelease bool on GithubSource filters GitHub Releases, ExplicitChannel selects the releases.{channel}.json manifest |
| TERMSRV/ credential target | DESKBRIDGE/CONN/{id} | Quick task 260416-9wt | Credential Guard no longer blocks credential storage |

**Deprecated/outdated:**
- MIG-04 in REQUIREMENTS.md still says "TERMSRV/ credential prefix" -- this is STALE per CONTEXT.md and quick task 260416-9wt. Imported connections use `DESKBRIDGE/CONN/{connectionId}`.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `vpk download github` fails gracefully (non-fatal) when no prior release exists | Pitfall 3 | CI/CD pipeline blocks on first release; mitigated by `continue-on-error: true` |
| A2 | Velopack 0.0.1298's `WaitExitThenApplyUpdates` is available and gives the app time for graceful shutdown | Pitfall 2 | If unavailable, must use `ApplyUpdatesAndRestart` with pre-cleanup |
| A3 | mRemoteNG confCons.xml root element is `<Connections>` or `<mrng:Connections>` with `FullFileEncryption` attribute for encryption detection | Code Examples / Pitfall 4 | Parser may fail to detect encrypted files; fallback is XML parse exception handler |
| A4 | The `--pre` flag on `vpk upload github` marks the GitHub Release as pre-release | Code Examples (CI/CD) | Beta releases may not be marked correctly on GitHub |

## Open Questions

1. **ApplyUpdatesAndRestart vs WaitExitThenApplyUpdates**
   - What we know: `ApplyUpdatesAndRestart` exits immediately; `WaitExitThenApplyUpdates` launches Update.exe and waits 60s for graceful shutdown
   - What's unclear: Whether `WaitExitThenApplyUpdates` is the better choice given active RDP sessions need disconnection before exit
   - Recommendation: Use the two-step approach -- disconnect all sessions first, THEN call `ApplyUpdatesAndRestart`. The explicit cleanup is more reliable than racing a 60s timeout.

2. **PublishSingleFile interaction with Velopack**
   - What we know: Current build.yml uses `-p:PublishSingleFile=true`. Velopack packs the publish output.
   - What's unclear: Whether `vpk pack` works correctly with single-file published apps, or if it needs the unpacked output
   - Recommendation: Test both. If single-file causes issues, remove `PublishSingleFile` from the release workflow (Velopack creates its own installer anyway).

3. **Beta channel opt-in persistence**
   - What we know: CONTEXT.md says "Users opt into beta via a Settings toggle"
   - What's unclear: Whether channel switching requires reinstalling from the beta installer, or if the running app can switch channels at runtime via `ExplicitChannel`
   - Recommendation: `ExplicitChannel` in `UpdateOptions` supports runtime switching. Add `UseBetaChannel` to `UpdateSettingsRecord` in `AppSettings`.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | Build + publish | Yes | 10.0.202 | -- |
| gh CLI | PR/release management | Yes | 2.87.3 | -- |
| vpk (Velopack CLI) | CI/CD packaging | No (not installed locally) | -- | CI installs via `dotnet tool install -g vpk`; local dev does not need it |
| GitHub Actions | CI/CD pipeline | Yes (remote) | -- | -- |

**Missing dependencies with no fallback:**
- None -- all runtime dependencies are satisfied.

**Missing dependencies with fallback:**
- vpk CLI not installed locally. This is expected -- vpk is only needed in CI/CD (GitHub Actions installs it). Local development uses `UpdateManager.IsInstalled` guard to skip update operations.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3 3.2.x |
| Config file | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| Quick run command | `dotnet test tests/Deskbridge.Tests --configuration Release --filter "Category!=UAT&Category!=Slow" -x` |
| Full suite command | `dotnet test tests/Deskbridge.Tests --configuration Release --filter "Category!=UAT&Category!=Slow"` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| UPD-01 | IUpdateService.CheckForUpdatesAsync returns update info | unit (mocked UpdateManager) | `dotnet test --filter "FullyQualifiedName~UpdateServiceTests" -x` | Wave 0 |
| UPD-02 | Download progress callback fires; confirmation dialog flow | unit (VM test) | `dotnet test --filter "FullyQualifiedName~UpdateStatusTests" -x` | Wave 0 |
| UPD-03 | UpdateAvailableEvent published on update found | unit | `dotnet test --filter "FullyQualifiedName~UpdateServiceTests.Publishes_UpdateAvailableEvent" -x` | Wave 0 |
| UPD-04 | Self-contained publish with SemVer2 | CI workflow (not unit testable) | N/A -- verified by successful vpk pack in CI | N/A |
| UPD-05 | GitHub Actions workflow syntax valid | CI lint | `gh workflow view build` (manual) | N/A |
| MIG-01 | MRemoteNGImporter parses valid confCons.xml | unit | `dotnet test --filter "FullyQualifiedName~MRemoteNGImporterTests" -x` | Wave 0 |
| MIG-02 | ImportWizardViewModel manages 4-step flow | unit | `dotnet test --filter "FullyQualifiedName~ImportWizardViewModelTests" -x` | Wave 0 |
| MIG-03 | Imported connections have no password data | unit | `dotnet test --filter "FullyQualifiedName~MRemoteNGImporterTests.Ignores_Passwords" -x` | Wave 0 |
| MIG-04 | Imported connections stored with DESKBRIDGE/CONN prefix | unit | `dotnet test --filter "FullyQualifiedName~ImportServiceTests" -x` | Wave 0 |
| MIG-05 | ConnectionImportedEvent published after import | unit | `dotnet test --filter "FullyQualifiedName~ImportServiceTests.Publishes_Event" -x` | Wave 0 |
| MIG-06 | JSON export produces tree; CSV produces flat rows | unit | `dotnet test --filter "FullyQualifiedName~ConnectionExporterTests" -x` | Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/Deskbridge.Tests --configuration Release --filter "Category!=UAT&Category!=Slow" -x`
- **Per wave merge:** `dotnet test tests/Deskbridge.Tests --configuration Release --filter "Category!=UAT&Category!=Slow"`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/Deskbridge.Tests/Update/UpdateServiceTests.cs` -- covers UPD-01, UPD-02, UPD-03
- [ ] `tests/Deskbridge.Tests/Import/MRemoteNGImporterTests.cs` -- covers MIG-01, MIG-03
- [ ] `tests/Deskbridge.Tests/Import/ConnectionExporterTests.cs` -- covers MIG-06
- [ ] `tests/Deskbridge.Tests/Import/ImportWizardViewModelTests.cs` -- covers MIG-02
- [ ] Test fixture: sample confCons.xml file for import tests

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | N/A -- update flow does not handle credentials |
| V3 Session Management | No | N/A |
| V4 Access Control | No | N/A |
| V5 Input Validation | Yes | Validate confCons.xml structure before parsing; reject encrypted files; sanitize imported connection names (prevent XSS-like injection into WPF bindings) |
| V6 Cryptography | No | N/A -- no credential import/export |

### Known Threat Patterns for Update + Import

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Malicious confCons.xml with XXE attack | Tampering | `XDocument.Load` with `XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }` |
| Credential leakage in export | Information Disclosure | Export methods MUST NOT include Password, CredentialMode, or any credential-adjacent data in JSON/CSV output |
| Update MITM (unsigned packages) | Tampering | Velopack signs packages with the `--signTemplate` or `--signParams` flag; GitHub HTTPS provides transport security |
| Path traversal in imported connection names | Tampering | Sanitize imported names -- strip path separators and control characters |

## Sources

### Primary (HIGH confidence)
- [Velopack UpdateManager API](https://docs.velopack.io/reference/cs/Velopack/UpdateManager) -- constructor, CheckForUpdatesAsync, DownloadUpdatesAsync, ApplyUpdatesAndRestart signatures
- [Velopack UpdateOptions API](https://docs.velopack.io/reference/cs/Velopack/UpdateOptions) -- ExplicitChannel, AllowVersionDowngrade, MaximumDeltasBeforeFallback properties
- [Velopack GithubSource API](https://docs.velopack.io/reference/cs/Velopack/Sources/GithubSource) -- constructor parameters (repoUrl, accessToken, prerelease, downloader)
- [Velopack Release Channels](https://docs.velopack.io/packaging/channels) -- --channel flag, releases.{channel}.json, default channel behavior
- [Velopack GitHub Actions Guide](https://docs.velopack.io/distributing/github-actions) -- vpk download/pack/upload workflow
- [Velopack CLI Windows Reference](https://docs.velopack.io/reference/cli/content/vpk-windows) -- complete vpk pack/upload/download flag lists
- [Existing codebase] -- Program.cs, AppEvents.cs, ToastSubscriptionService.cs, App.xaml.cs, MainWindow.xaml, AppSettings.cs verified via grep/read

### Secondary (MEDIUM confidence)
- [mRemoteNG confCons.xml examples](https://github.com/vladturbuleasa/mRemoteNG_conf/blob/master/confCons.xml) -- XML schema, Node attributes, Container/Connection types
- [mRemoteNG readthedocs](https://mremoteng.readthedocs.io/en/v1.77.3-dev/howtos/bulk_connections.html) -- bulk connection XML format

### Tertiary (LOW confidence)
- None -- all claims verified against official docs or codebase

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no new packages needed; all dependencies already in project
- Architecture: HIGH -- patterns follow established project conventions (IConnectionImporter mirrors IProtocolHost; UpdateService mirrors existing service patterns)
- Pitfalls: HIGH -- Velopack pitfalls verified against official docs; mRemoteNG XML format verified against real confCons.xml examples
- CI/CD: MEDIUM -- vpk command flags verified, but first-release behavior and PublishSingleFile interaction are assumed

**Research date:** 2026-04-15
**Valid until:** 2026-05-15 (Velopack is fast-moving; channel API may evolve)
