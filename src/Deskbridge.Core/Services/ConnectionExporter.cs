using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Services;

public static class ConnectionExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string GuidKey(Guid? id) => id?.ToString() ?? string.Empty;

    public static string ExportJson(IReadOnlyList<ConnectionModel> connections, IReadOnlyList<ConnectionGroup> groups)
    {
        var connectionsByGroup = connections
            .GroupBy(c => GuidKey(c.GroupId))
            .ToDictionary(g => g.Key, g => g.ToList());
        var childGroups = groups
            .GroupBy(g => GuidKey(g.ParentGroupId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var rootNodes = new List<object>();
        var rootKey = GuidKey(null);

        // Add root-level groups (ParentGroupId == null) as tree nodes
        if (childGroups.TryGetValue(rootKey, out var rootGroups))
        {
            foreach (var group in rootGroups.OrderBy(g => g.SortOrder))
            {
                rootNodes.Add(BuildGroupNode(group, connectionsByGroup, childGroups));
            }
        }

        // Add root-level connections (GroupId == null)
        if (connectionsByGroup.TryGetValue(rootKey, out var rootConnections))
        {
            foreach (var conn in rootConnections.OrderBy(c => c.SortOrder))
            {
                rootNodes.Add(BuildConnectionNode(conn));
            }
        }

        var export = new
        {
            exportDate = DateTime.UtcNow.ToString("O"),
            version = "1.0",
            connections = rootNodes
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }

    public static string ExportCsv(IReadOnlyList<ConnectionModel> connections, IReadOnlyList<ConnectionGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Hostname,Port,Username,Domain,Protocol,FolderPath,Notes");

        foreach (var conn in connections)
        {
            var folderPath = BuildFolderPath(conn.GroupId, groups);
            sb.Append(CsvEscape(conn.Name));
            sb.Append(',');
            sb.Append(CsvEscape(conn.Hostname ?? string.Empty));
            sb.Append(',');
            sb.Append(conn.Port);
            sb.Append(',');
            sb.Append(CsvEscape(conn.Username ?? string.Empty));
            sb.Append(',');
            sb.Append(CsvEscape(conn.Domain ?? string.Empty));
            sb.Append(',');
            sb.Append(CsvEscape(conn.Protocol.ToString()));
            sb.Append(',');
            sb.Append(CsvEscape(folderPath));
            sb.Append(',');
            sb.Append(CsvEscape(conn.Notes ?? string.Empty));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static object BuildGroupNode(
        ConnectionGroup group,
        Dictionary<string, List<ConnectionModel>> connectionsByGroup,
        Dictionary<string, List<ConnectionGroup>> childGroups)
    {
        var children = new List<object>();
        var key = GuidKey(group.Id);

        // Add sub-groups
        if (childGroups.TryGetValue(key, out var subGroups))
        {
            foreach (var sub in subGroups.OrderBy(g => g.SortOrder))
            {
                children.Add(BuildGroupNode(sub, connectionsByGroup, childGroups));
            }
        }

        // Add connections in this group
        if (connectionsByGroup.TryGetValue(key, out var conns))
        {
            foreach (var conn in conns.OrderBy(c => c.SortOrder))
            {
                children.Add(BuildConnectionNode(conn));
            }
        }

        return new
        {
            type = "group",
            name = group.Name,
            children
        };
    }

    private static object BuildConnectionNode(ConnectionModel conn)
    {
        return new
        {
            type = "connection",
            name = conn.Name,
            hostname = conn.Hostname,
            port = conn.Port,
            username = conn.Username,
            domain = conn.Domain,
            protocol = conn.Protocol.ToString(),
            notes = conn.Notes,
            tags = conn.Tags.Count > 0 ? conn.Tags : null
        };
    }

    private static string BuildFolderPath(Guid? groupId, IReadOnlyList<ConnectionGroup> groups)
    {
        if (groupId is null)
            return string.Empty;

        var groupLookup = groups.ToDictionary(g => g.Id);
        var segments = new List<string>();
        var currentId = groupId;

        while (currentId is not null && groupLookup.TryGetValue(currentId.Value, out var group))
        {
            segments.Add(group.Name);
            currentId = group.ParentGroupId;
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
