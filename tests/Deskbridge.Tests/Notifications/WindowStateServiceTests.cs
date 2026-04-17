using System.IO;
using System.Text;
using Deskbridge.Core.Services;
using Deskbridge.Core.Settings;
using Deskbridge.Tests.Logging;

namespace Deskbridge.Tests.Notifications;

/// <summary>
/// Phase 6 Plan 06-02 (NOTF-04) coverage for <see cref="WindowStateService"/>:
/// defaults-on-missing, round-trip, atomic tmp-rename write, malformed fallback,
/// schema-version gating, and no-BOM UTF-8 output. Uses <see cref="TempDirScope"/>
/// so real disk IO runs under <c>%TEMP%/deskbridge-tests/&lt;guid&gt;</c> without
/// touching <c>%AppData%/Deskbridge</c>.
/// </summary>
public sealed class WindowStateServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------
    // Test 1 — LoadAsync returns defaults when settings.json does not exist
    // ------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_WhenFileMissing_ReturnsDefaults()
    {
        using var scope = new TempDirScope();
        var path = Path.Combine(scope.Path, "settings.json");
        var svc = new WindowStateService(path);

        var loaded = await svc.LoadAsync(Ct);

        loaded.Should().NotBeNull();
        loaded.SchemaVersion.Should().Be(1);
        loaded.Window.Should().Be(WindowStateRecord.Default);
        loaded.Security.Should().Be(SecuritySettingsRecord.Default);
    }

    // ------------------------------------------------------------------
    // Test 2 — SaveAsync + LoadAsync round-trip preserves every field
    // ------------------------------------------------------------------
    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsAllFields()
    {
        using var scope = new TempDirScope();
        var path = Path.Combine(scope.Path, "settings.json");
        var svc = new WindowStateService(path);

        var settings = new AppSettings(
            new WindowStateRecord(X: 42, Y: 84, Width: 1024, Height: 768,
                IsMaximized: true, SidebarOpen: false, SidebarWidth: 320),
            new SecuritySettingsRecord(AutoLockTimeoutMinutes: 30, LockOnMinimise: true),
            UpdateSettingsRecord.Default);

        await svc.SaveAsync(settings, Ct);
        var roundTrip = await svc.LoadAsync(Ct);

        roundTrip.Should().Be(settings);
    }

    // ------------------------------------------------------------------
    // Test 3 — SaveAsync writes atomically (no .tmp remaining afterwards)
    // ------------------------------------------------------------------
    [Fact]
    public async Task SaveAsync_WritesAtomically_NoTmpFileRemaining()
    {
        using var scope = new TempDirScope();
        var path = Path.Combine(scope.Path, "settings.json");
        var svc = new WindowStateService(path);

        var settings = new AppSettings(
            new WindowStateRecord(10, 20, 800, 600, true, false, 260),
            new SecuritySettingsRecord(30, true),
            UpdateSettingsRecord.Default);

        await svc.SaveAsync(settings, Ct);

        File.Exists(path).Should().BeTrue("settings.json must exist after SaveAsync");
        File.Exists(path + ".tmp").Should().BeFalse(".tmp must be moved, not left behind");

        // And the file parses back.
        var roundTrip = await svc.LoadAsync(Ct);
        roundTrip.Should().Be(settings);
    }

    // ------------------------------------------------------------------
    // Test 4 — Malformed JSON falls back to defaults (no exception)
    // ------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_MalformedJson_ReturnsDefaults()
    {
        using var scope = new TempDirScope();
        var path = Path.Combine(scope.Path, "settings.json");
        await File.WriteAllTextAsync(path, "this is not json {{{", Ct);

        var svc = new WindowStateService(path);
        var loaded = await svc.LoadAsync(Ct);

        loaded.Should().NotBeNull();
        loaded.Window.Should().Be(WindowStateRecord.Default);
        loaded.Security.Should().Be(SecuritySettingsRecord.Default);
    }

    // ------------------------------------------------------------------
    // Test 5 — Unknown SchemaVersion falls back to defaults (v2+ reserved for future migrator)
    // ------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_UnknownSchemaVersion_ReturnsDefaults()
    {
        using var scope = new TempDirScope();
        var path = Path.Combine(scope.Path, "settings.json");

        const string FutureJson = """
        {
          "window": {
            "x": 10, "y": 10, "width": 800, "height": 600,
            "isMaximized": false, "sidebarOpen": true, "sidebarWidth": 240
          },
          "security": {
            "autoLockTimeoutMinutes": 15,
            "lockOnMinimise": false
          },
          "schemaVersion": 99
        }
        """;
        await File.WriteAllTextAsync(path, FutureJson, Ct);

        var svc = new WindowStateService(path);
        var loaded = await svc.LoadAsync(Ct);

        loaded.Window.Should().Be(WindowStateRecord.Default);
        loaded.Security.Should().Be(SecuritySettingsRecord.Default);
    }

    // ------------------------------------------------------------------
    // Test 6 — Saved file has no UTF-8 BOM (first byte is '{')
    // ------------------------------------------------------------------
    [Fact]
    public async Task SaveAsync_WritesUtf8WithoutBom()
    {
        using var scope = new TempDirScope();
        var path = Path.Combine(scope.Path, "settings.json");
        var svc = new WindowStateService(path);

        await svc.SaveAsync(new AppSettings(), Ct);

        var bytes = await File.ReadAllBytesAsync(path, Ct);
        bytes.Should().NotBeEmpty();
        // Reject BOM (EF BB BF). First meaningful byte should be '{'.
        bytes[0].Should().Be((byte)'{', "settings.json must be UTF-8 without BOM so first byte is the JSON open-brace");
    }
}
