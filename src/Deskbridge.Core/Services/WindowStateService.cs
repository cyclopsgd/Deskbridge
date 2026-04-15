using System.IO;
using System.Text;
using System.Text.Json;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Settings;
using Serilog;

namespace Deskbridge.Core.Services;

/// <summary>
/// Phase 6 Plan 06-02 (NOTF-04): atomic load/save of
/// <c>%AppData%/Deskbridge/settings.json</c> using the tmp-file-rename pattern
/// established by <see cref="JsonConnectionStore"/> in Phase 3. Defaults on missing /
/// malformed / unknown-schema inputs (logged as warnings, never thrown).
/// </summary>
public sealed class WindowStateService : IWindowStateService
{
    private readonly string _path;

    /// <summary>Production ctor — resolves the canonical <c>%AppData%/Deskbridge/settings.json</c> path.</summary>
    public WindowStateService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Deskbridge",
            "settings.json"))
    {
    }

    /// <summary>Test seam — allows redirection to a temp directory via <c>TempDirScope</c>.</summary>
    internal WindowStateService(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);

            if (loaded is null)
            {
                Log.Warning("settings.json deserialised to null - returning defaults");
                return new AppSettings();
            }

            if (loaded.SchemaVersion != 1)
            {
                Log.Warning("settings.json has unknown SchemaVersion={Version} - returning defaults", loaded.SchemaVersion);
                return new AppSettings();
            }

            return loaded;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load settings.json - returning defaults");
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var json = JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings);

        // UTF-8 without BOM — same convention as JsonConnectionStore.
        var bomless = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var tmp = _path + ".tmp";

        await File.WriteAllTextAsync(tmp, json, bomless, cancellationToken).ConfigureAwait(false);

        // Atomic rename on NTFS — survives a kill-9 between WriteAllText and Move without
        // corrupting the destination file. JsonConnectionStore precedent.
        File.Move(tmp, _path, overwrite: true);
    }
}
