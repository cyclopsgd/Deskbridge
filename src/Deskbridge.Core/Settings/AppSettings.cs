namespace Deskbridge.Core.Settings;

/// <summary>
/// Phase 6 Plan 06-02 (NOTF-04) + Plan 06-04 (SEC-03 / SEC-05): the single
/// <c>settings.json</c> schema covering window state and security preferences.
/// Plan 06-02 lands and consumes <see cref="Window"/>; Plan 06-04 will read/write
/// <see cref="Security"/> unchanged. <see cref="SchemaVersion"/> reserves shape
/// for future migrations — values other than <c>1</c> cause <see cref="Services.WindowStateService.LoadAsync"/>
/// to return defaults with a warning.
/// </summary>
public sealed record AppSettings(
    WindowStateRecord Window,
    SecuritySettingsRecord Security,
    UpdateSettingsRecord Update,
    int SchemaVersion = 1)
{
    /// <summary>Default-constructed settings — used as the fallback when <c>settings.json</c> is missing or invalid.</summary>
    public AppSettings() : this(WindowStateRecord.Default, SecuritySettingsRecord.Default, UpdateSettingsRecord.Default) { }
}

/// <summary>
/// Window position + dimensions + sidebar state. Captured in <c>MainWindow.OnClosing</c>
/// (using <see cref="System.Windows.Window.RestoreBounds"/> when maximised so the
/// un-maximised coordinates survive across sessions) and applied in
/// <c>MainWindow.OnSourceInitialized</c> before the window renders.
/// </summary>
public sealed record WindowStateRecord(
    double X,
    double Y,
    double Width,
    double Height,
    bool IsMaximized,
    bool SidebarOpen,
    double SidebarWidth)
{
    /// <summary>Default window position / size for a fresh install. Chosen to fit a 1366×768 minimum screen comfortably.</summary>
    public static WindowStateRecord Default { get; } =
        new(X: 100, Y: 100, Width: 1200, Height: 800,
            IsMaximized: false, SidebarOpen: true, SidebarWidth: 240);
}

/// <summary>
/// Security preferences consumed by Plan 06-04 (app lock). Defined here in Plan 06-02
/// so the schema is locked before 06-04 executes — 06-04 only adds the consumer code,
/// not a new settings file or schema migration.
/// </summary>
public sealed record SecuritySettingsRecord(
    int AutoLockTimeoutMinutes,
    bool LockOnMinimise,
    bool RequireMasterPassword = true)
{
    /// <summary>Defaults match UI-SPEC §Settings Panel Additions (auto-lock = 15 minutes, lock-on-minimise = off, require password = on).</summary>
    public static SecuritySettingsRecord Default { get; } =
        new(AutoLockTimeoutMinutes: 15, LockOnMinimise: false, RequireMasterPassword: true);
}

/// <summary>
/// Phase 7 Plan 07-01 (UPD-01): update preferences. <see cref="UseBetaChannel"/>
/// controls whether <see cref="Services.UpdateService"/> checks the beta Velopack
/// channel (prerelease GitHub Releases) instead of the stable channel. Toggled
/// via the Settings panel in a future plan.
/// </summary>
public sealed record UpdateSettingsRecord(
    bool UseBetaChannel = false)
{
    /// <summary>Defaults: stable channel (no beta).</summary>
    public static UpdateSettingsRecord Default { get; } = new();
}
