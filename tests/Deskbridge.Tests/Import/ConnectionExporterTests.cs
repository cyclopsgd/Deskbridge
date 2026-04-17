using System.Text.Json;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using FluentAssertions;

namespace Deskbridge.Tests.Import;

public class ConnectionExporterTests
{
    private static (List<ConnectionModel> connections, List<ConnectionGroup> groups) BuildSampleData()
    {
        var rootGroup = new ConnectionGroup
        {
            Id = Guid.Parse("aaaa0000-0000-0000-0000-000000000001"),
            Name = "Production",
            ParentGroupId = null,
            SortOrder = 0
        };

        var childGroup = new ConnectionGroup
        {
            Id = Guid.Parse("aaaa0000-0000-0000-0000-000000000002"),
            Name = "Web Servers",
            ParentGroupId = rootGroup.Id,
            SortOrder = 0
        };

        var groups = new List<ConnectionGroup> { rootGroup, childGroup };

        var conn1 = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "Web Server 01",
            Hostname = "web01.prod.local",
            Port = 3389,
            Username = "admin",
            Domain = "PROD",
            Protocol = Protocol.Rdp,
            GroupId = childGroup.Id,
            Notes = "Primary web server",
            Tags = ["web", "production"],
            CredentialMode = CredentialMode.Own
        };

        var conn2 = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "DB Server 01",
            Hostname = "db01.prod.local",
            Port = 3389,
            Username = "dbadmin",
            Domain = "PROD",
            Protocol = Protocol.Rdp,
            GroupId = rootGroup.Id,
            Notes = "Database server",
            CredentialMode = CredentialMode.Inherit
        };

        var conn3 = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "Standalone",
            Hostname = "standalone.local",
            Port = 3390,
            Username = "root",
            Protocol = Protocol.Rdp,
            GroupId = null,
            Notes = "Root level connection",
            CredentialMode = CredentialMode.Prompt
        };

        var connections = new List<ConnectionModel> { conn1, conn2, conn3 };
        return (connections, groups);
    }

    // Test 1: ExportJson produces valid JSON string
    [Fact]
    public void ExportJson_ProducesValidJson()
    {
        var (connections, groups) = BuildSampleData();
        var json = ConnectionExporter.ExportJson(connections, groups);

        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    // Test 2: ExportJson output contains connection names but NOT passwords or credential modes
    [Fact]
    public void ExportJson_ContainsNames_ExcludesCredentials()
    {
        var (connections, groups) = BuildSampleData();
        var json = ConnectionExporter.ExportJson(connections, groups);

        json.Should().Contain("Web Server 01");
        json.Should().Contain("DB Server 01");
        json.Should().NotContain("CredentialMode");
        json.Should().NotContain("Password");
        // CredentialMode enum values should not appear
        json.Should().NotContain("\"Own\"");
        json.Should().NotContain("\"Inherit\"");
        json.Should().NotContain("\"Prompt\"");
    }

    // Test 3: ExportJson preserves folder hierarchy (nested JSON objects for groups)
    [Fact]
    public void ExportJson_PreservesFolderHierarchy()
    {
        var (connections, groups) = BuildSampleData();
        var json = ConnectionExporter.ExportJson(connections, groups);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Should have a "connections" array at root
        root.TryGetProperty("connections", out var connectionsArr).Should().BeTrue();
        connectionsArr.ValueKind.Should().Be(JsonValueKind.Array);

        // Find the "Production" group
        var prodGroup = connectionsArr.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("name").GetString() == "Production");
        prodGroup.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        prodGroup.GetProperty("type").GetString().Should().Be("group");

        // Production should have children containing "Web Servers" sub-group
        var children = prodGroup.GetProperty("children");
        children.GetArrayLength().Should().BeGreaterThan(0);
    }

    // Test 4: ExportJson handles root-level connections (no group)
    [Fact]
    public void ExportJson_HandlesRootLevelConnections()
    {
        var (connections, groups) = BuildSampleData();
        var json = ConnectionExporter.ExportJson(connections, groups);

        using var doc = JsonDocument.Parse(json);
        var connectionsArr = doc.RootElement.GetProperty("connections");

        // "Standalone" should be at root level
        var standalone = connectionsArr.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("name").GetString() == "Standalone");
        standalone.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        standalone.GetProperty("type").GetString().Should().Be("connection");
    }

    // Test 5: ExportCsv produces header row with correct columns
    [Fact]
    public void ExportCsv_ProducesHeaderRow()
    {
        var (connections, groups) = BuildSampleData();
        var csv = ConnectionExporter.ExportCsv(connections, groups);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].TrimEnd('\r').Should().Be("Name,Hostname,Port,Username,Domain,Protocol,FolderPath,Notes");
    }

    // Test 6: ExportCsv produces one data row per connection (flat)
    [Fact]
    public void ExportCsv_ProducesOneRowPerConnection()
    {
        var (connections, groups) = BuildSampleData();
        var csv = ConnectionExporter.ExportCsv(connections, groups);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // 1 header + 3 data rows
        lines.Length.Should().Be(4);
    }

    // Test 7: ExportCsv includes folder path as string column (e.g., "Production/Web Servers")
    [Fact]
    public void ExportCsv_IncludesFolderPath()
    {
        var (connections, groups) = BuildSampleData();
        var csv = ConnectionExporter.ExportCsv(connections, groups);

        // Web Server 01 is in Production > Web Servers
        csv.Should().Contain("Production/Web Servers");
    }

    // Test 8: ExportCsv escapes commas in connection names
    [Fact]
    public void ExportCsv_EscapesCommasInNames()
    {
        var connections = new List<ConnectionModel>
        {
            new()
            {
                Name = "Server, Primary",
                Hostname = "server.local",
                Port = 3389,
                Protocol = Protocol.Rdp
            }
        };
        var groups = new List<ConnectionGroup>();

        var csv = ConnectionExporter.ExportCsv(connections, groups);

        // Comma in name should be quoted
        csv.Should().Contain("\"Server, Primary\"");
    }

    // Test 9: ExportCsv escapes double quotes in notes
    [Fact]
    public void ExportCsv_EscapesQuotesInNotes()
    {
        var connections = new List<ConnectionModel>
        {
            new()
            {
                Name = "Server",
                Hostname = "server.local",
                Port = 3389,
                Protocol = Protocol.Rdp,
                Notes = "Has a \"special\" config"
            }
        };
        var groups = new List<ConnectionGroup>();

        var csv = ConnectionExporter.ExportCsv(connections, groups);

        // Quotes in notes should be escaped as ""
        csv.Should().Contain("\"Has a \"\"special\"\" config\"");
    }

    // Test 10: ExportCsv escapes newlines in notes
    [Fact]
    public void ExportCsv_EscapesNewlinesInNotes()
    {
        var connections = new List<ConnectionModel>
        {
            new()
            {
                Name = "Server",
                Hostname = "server.local",
                Port = 3389,
                Protocol = Protocol.Rdp,
                Notes = "Line one\nLine two"
            }
        };
        var groups = new List<ConnectionGroup>();

        var csv = ConnectionExporter.ExportCsv(connections, groups);

        // Newline in notes should cause the field to be quoted
        csv.Should().Contain("\"Line one\nLine two\"");
    }

    // Test 11: ExportCsv does NOT include passwords or credential modes
    [Fact]
    public void ExportCsv_ExcludesCredentials()
    {
        var (connections, groups) = BuildSampleData();
        var csv = ConnectionExporter.ExportCsv(connections, groups);

        csv.Should().NotContain("CredentialMode");
        csv.Should().NotContain("Password");
        csv.Should().NotContain("Own");
        csv.Should().NotContain("Inherit");
        csv.Should().NotContain("Prompt");
    }

    // Test 12: ExportJson with empty connection list returns valid empty JSON
    [Fact]
    public void ExportJson_EmptyList_ReturnsValidJson()
    {
        var json = ConnectionExporter.ExportJson([], []);

        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("connections").GetArrayLength().Should().Be(0);
    }

    // Test 13: ExportCsv with empty connection list returns header only
    [Fact]
    public void ExportCsv_EmptyList_ReturnsHeaderOnly()
    {
        var csv = ConnectionExporter.ExportCsv([], []);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(1);
        lines[0].TrimEnd('\r').Should().Be("Name,Hostname,Port,Username,Domain,Protocol,FolderPath,Notes");
    }
}
