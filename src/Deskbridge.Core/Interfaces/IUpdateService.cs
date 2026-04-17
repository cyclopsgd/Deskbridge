namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Phase 7 Plan 07-01 (UPD-01 / UPD-02 / UPD-03): abstraction for the auto-update
/// lifecycle. Wraps Velopack <c>UpdateManager</c> with a dev-mode guard
/// (<see cref="IsInstalled"/> = false when not installed via Velopack) and publishes
/// <see cref="Events.UpdateAvailableEvent"/> on the event bus when a new version
/// is found.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Returns <c>true</c> when the app was installed via Velopack and update
    /// operations are safe to invoke. In dev mode (running from IDE), this returns
    /// <c>false</c> and all update methods are no-ops.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// The version string of the most recent update found by
    /// <see cref="CheckForUpdatesAsync"/>. <c>null</c> when no update has been
    /// detected yet.
    /// </summary>
    string? PendingVersion { get; }

    /// <summary>
    /// Checks GitHub Releases for a newer version. When found, stores the result
    /// internally, sets <see cref="PendingVersion"/>, and publishes
    /// <see cref="Events.UpdateAvailableEvent"/> on the event bus.
    /// Returns <c>true</c> if an update is available, <c>false</c> otherwise.
    /// Exceptions are caught and logged — this method never throws.
    /// </summary>
    Task<bool> CheckForUpdatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads the previously-discovered update with optional progress reporting.
    /// Requires a prior successful <see cref="CheckForUpdatesAsync"/> call.
    /// </summary>
    Task DownloadUpdatesAsync(IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Applies the downloaded update and restarts the application.
    /// <b>WARNING:</b> This calls <c>Environment.Exit()</c> — all active RDP sessions
    /// will be terminated. The caller MUST show a confirmation dialog first.
    /// </summary>
    void ApplyUpdatesAndRestart();
}
