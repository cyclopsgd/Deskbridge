using System.Text.Json;
using Deskbridge.Core.Settings;

namespace Deskbridge.Tests.Settings;

public class UninstallSettingsTests
{
    [Fact]
    public void UninstallRecord_DefaultValues()
    {
        var record = UninstallRecord.Default;
        record.CleanUpOnUninstall.Should().BeFalse();
    }

    [Fact]
    public void AppSettings_Default_Uninstall_IsNull()
    {
        var settings = new AppSettings();
        settings.Uninstall.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UninstallRecord_Roundtrip_PreservesState(bool cleanUp)
    {
        var original = new AppSettings(
            WindowStateRecord.Default,
            SecuritySettingsRecord.Default,
            UpdateSettingsRecord.Default,
            Uninstall: new UninstallRecord(cleanUp));

        var json = JsonSerializer.Serialize(original, AppSettingsContext.Default.AppSettings);
        var deserialized = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);

        deserialized.Should().NotBeNull();
        deserialized!.Uninstall.Should().NotBeNull();
        deserialized.Uninstall!.CleanUpOnUninstall.Should().Be(cleanUp);
    }

    [Fact]
    public void AppSettings_Deserialize_MissingUninstall_ReturnsNull()
    {
        var json = """
        {
          "window": { "x": 100, "y": 100, "width": 1200, "height": 800, "isMaximized": false, "sidebarOpen": true, "sidebarWidth": 240 },
          "security": { "autoLockTimeoutMinutes": 15, "lockOnMinimise": false, "requireMasterPassword": true },
          "update": { "useBetaChannel": false },
          "schemaVersion": 1
        }
        """;
        var settings = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);

        settings.Should().NotBeNull();
        settings!.Uninstall.Should().BeNull();
    }

    [Fact]
    public void UninstallRecord_JsonKeyPath_MatchesPhase24Contract()
    {
        // Phase 24 contract: Velopack uninstall hook reads settings.json via JsonDocument
        // and expects the path uninstall.cleanUpOnUninstall. This test pins that contract.
        var settings = new AppSettings(
            WindowStateRecord.Default,
            SecuritySettingsRecord.Default,
            UpdateSettingsRecord.Default,
            Uninstall: new UninstallRecord(true));

        var json = JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement
            .GetProperty("uninstall")
            .GetProperty("cleanUpOnUninstall")
            .GetBoolean()
            .Should().BeTrue();
    }
}
