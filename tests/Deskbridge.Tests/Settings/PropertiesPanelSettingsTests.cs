using System.Text.Json;
using Deskbridge.Core.Settings;

namespace Deskbridge.Tests.Settings;

public class PropertiesPanelSettingsTests
{
    [Fact]
    public void PropertiesPanelRecord_DefaultValues_BothExpanded()
    {
        var record = PropertiesPanelRecord.Default;
        record.IsConnectionCardExpanded.Should().BeTrue();
        record.IsCredentialsCardExpanded.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_Default_PropertiesPanel_IsNull()
    {
        // Pre-Phase-9 settings.json files have no propertiesPanel key.
        // The default ctor must produce null so null-coalescing works.
        var settings = new AppSettings();
        settings.PropertiesPanel.Should().BeNull();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PropertiesPanelRecord_Roundtrip_PreservesState(
        bool connExpanded, bool credExpanded)
    {
        var original = new AppSettings(
            WindowStateRecord.Default,
            SecuritySettingsRecord.Default,
            UpdateSettingsRecord.Default,
            new PropertiesPanelRecord(connExpanded, credExpanded));

        var json = JsonSerializer.Serialize(original, AppSettingsContext.Default.AppSettings);
        var deserialized = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);

        deserialized.Should().NotBeNull();
        deserialized!.PropertiesPanel.Should().NotBeNull();
        deserialized.PropertiesPanel!.IsConnectionCardExpanded.Should().Be(connExpanded);
        deserialized.PropertiesPanel.IsCredentialsCardExpanded.Should().Be(credExpanded);
    }

    [Fact]
    public void AppSettings_Deserialize_MissingPropertiesPanel_ReturnsNull()
    {
        // Simulate a pre-Phase-9 settings.json without propertiesPanel key
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
        // PropertiesPanel should be null (missing from JSON), consumers null-coalesce
        settings!.PropertiesPanel.Should().BeNull();
    }
}
