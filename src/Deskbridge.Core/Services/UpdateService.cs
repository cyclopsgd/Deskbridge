using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace Deskbridge.Core.Services;

/// <summary>
/// Phase 7 Plan 07-01 (UPD-01 / UPD-02 / UPD-03): Velopack <see cref="UpdateManager"/>
/// wrapper implementing <see cref="IUpdateService"/>. Publishes
/// <see cref="UpdateAvailableEvent"/> on the event bus when a new version is discovered.
///
/// <para><b>Dev-mode guard:</b> When <see cref="IsInstalled"/> is <c>false</c> (running
/// from IDE, not installed via Velopack), all update operations log a warning and
/// return immediately — they never throw.</para>
///
/// <para><b>Testability:</b> The <c>protected virtual</c> methods
/// (<see cref="CheckForUpdatesInternalAsync"/>, <see cref="DownloadUpdatesInternalAsync"/>,
/// <see cref="ApplyUpdatesInternalAndRestart"/>) provide seams for a test subclass to
/// override Velopack calls without needing a real installation. The protected
/// constructor <see cref="UpdateService(IEventBus)"/> skips <see cref="UpdateManager"/>
/// creation for test use.</para>
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly IEventBus _bus;
    private readonly UpdateManager? _mgr;
    private UpdateInfo? _pendingUpdate;

    /// <summary>
    /// Production constructor. Creates a <see cref="UpdateManager"/> with
    /// <see cref="GithubSource"/> configured for the specified repository.
    /// Requires <see cref="VelopackApp.Build()"/> to have been called in
    /// <c>Program.Main</c> before this constructor is invoked.
    /// </summary>
    public UpdateService(IEventBus bus, string repoUrl, bool useBetaChannel)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoUrl);

        _bus = bus;

        var source = new GithubSource(repoUrl, accessToken: null, prerelease: useBetaChannel);
        var options = new UpdateOptions
        {
            ExplicitChannel = useBetaChannel ? "beta" : "stable",
            AllowVersionDowngrade = false,
        };
        _mgr = new UpdateManager(source, options);
    }

    /// <summary>
    /// Test constructor. Skips <see cref="UpdateManager"/> creation so tests can
    /// run without a Velopack installation. Subclasses MUST override
    /// <see cref="IsInstalled"/>, <see cref="CheckForUpdatesInternalAsync"/>,
    /// <see cref="DownloadUpdatesInternalAsync"/>, and
    /// <see cref="ApplyUpdatesInternalAndRestart"/>.
    /// </summary>
    protected UpdateService(IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
    }

    /// <inheritdoc/>
    public virtual bool IsInstalled => _mgr?.IsInstalled ?? false;

    /// <inheritdoc/>
    public string? PendingVersion { get; private set; }

    /// <inheritdoc/>
    public async Task<bool> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (!IsInstalled)
        {
            Log.Warning("Update check skipped: not installed via Velopack");
            return false;
        }

        try
        {
            var version = await CheckForUpdatesInternalAsync(ct).ConfigureAwait(false);
            if (version is not null)
            {
                PendingVersion = version;
                _bus.Publish(new UpdateAvailableEvent(version));
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed — will retry on next startup");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task DownloadUpdatesAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (!IsInstalled || PendingVersion is null) return;

        await DownloadUpdatesInternalAsync(
            progress is not null ? p => progress.Report(p) : null,
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void ApplyUpdatesAndRestart()
    {
        if (PendingVersion is null) return;
        ApplyUpdatesInternalAndRestart();
    }

    // ----------------------------------------------------------- virtual seams for testing

    /// <summary>
    /// Checks for updates via Velopack. Returns the version string if an update is
    /// available, or <c>null</c> if the current version is latest. Stores the
    /// <see cref="UpdateInfo"/> internally for <see cref="DownloadUpdatesInternalAsync"/>
    /// and <see cref="ApplyUpdatesInternalAndRestart"/> to consume.
    /// Override in tests to avoid Velopack runtime dependency.
    /// </summary>
    protected virtual async Task<string?> CheckForUpdatesInternalAsync(CancellationToken ct)
    {
        if (_mgr is null) return null;
        var update = await _mgr.CheckForUpdatesAsync().ConfigureAwait(false);
        if (update is null) return null;
        _pendingUpdate = update;
        return update.TargetFullRelease?.Version?.ToString();
    }

    /// <summary>
    /// Downloads the pending update. <paramref name="progressCallback"/> receives
    /// 0-100 progress values. Override in tests to simulate download.
    /// </summary>
    protected virtual async Task DownloadUpdatesInternalAsync(Action<int>? progressCallback, CancellationToken ct)
    {
        if (_mgr is null || _pendingUpdate is null) return;
        await _mgr.DownloadUpdatesAsync(_pendingUpdate, progressCallback).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the update and restarts. <b>WARNING:</b> Calls <c>Environment.Exit()</c>.
    /// Override in tests to no-op.
    /// </summary>
    protected virtual void ApplyUpdatesInternalAndRestart()
    {
        _mgr?.ApplyUpdatesAndRestart(_pendingUpdate!);
    }
}
