using System.Text.Json;
using Deskbridge.Core.Settings;

namespace Deskbridge.Tests.Settings;

public class BulkOperationsSettingsTests
{
    [Fact]
    public void BulkOperationsRecord_DefaultValues()
    {
        var record = BulkOperationsRecord.Default;
        record.ConfirmBeforeBulkOperations.Should().BeTrue();
        record.GdiWarningThreshold.Should().Be(15);
    }

    [Fact]
    public void AppSettings_Default_BulkOperations_IsNull()
    {
        var settings = new AppSettings();
        settings.BulkOperations.Should().BeNull();
    }

    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 30)]
    [InlineData(true, 15)]
    [InlineData(false, 20)]
    public void BulkOperationsRecord_Roundtrip_PreservesState(
        bool confirm, int threshold)
    {
        var original = new AppSettings(
            WindowStateRecord.Default,
            SecuritySettingsRecord.Default,
            UpdateSettingsRecord.Default,
            BulkOperations: new BulkOperationsRecord(confirm, threshold));

        var json = JsonSerializer.Serialize(original, AppSettingsContext.Default.AppSettings);
        var deserialized = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);

        deserialized.Should().NotBeNull();
        deserialized!.BulkOperations.Should().NotBeNull();
        deserialized.BulkOperations!.ConfirmBeforeBulkOperations.Should().Be(confirm);
        deserialized.BulkOperations.GdiWarningThreshold.Should().Be(threshold);
    }

    [Fact]
    public void AppSettings_Deserialize_MissingBulkOperations_ReturnsNull()
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
        settings!.BulkOperations.Should().BeNull();
    }
}
