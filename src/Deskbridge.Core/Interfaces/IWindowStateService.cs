using Deskbridge.Core.Settings;

namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Phase 6 Plan 06-02 (NOTF-04): persistence of window position + sidebar state +
/// security preferences to <c>%AppData%/Deskbridge/settings.json</c>. Consumers:
/// <c>MainWindow.OnSourceInitialized</c> (Load) and <c>MainWindow.OnClosing</c> (Save).
/// Plan 06-04 (app lock) consumes the same surface for Security settings.
/// </summary>
public interface IWindowStateService
{
    /// <summary>
    /// Read <c>settings.json</c> from disk. Returns <see cref="AppSettings.AppSettings()"/>
    /// defaults if the file is missing, malformed, or has an unsupported schema version.
    /// Does NOT throw — all IO errors are logged via Serilog and swallowed to defaults.
    /// </summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically persist <paramref name="settings"/> to <c>settings.json</c> using a
    /// tmp-file-rename pattern (same as <c>JsonConnectionStore.PersistAtomically</c> in Phase 3).
    /// A kill during the write does not corrupt the final file.
    /// </summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
