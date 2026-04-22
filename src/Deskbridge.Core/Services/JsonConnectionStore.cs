using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Serilog;

namespace Deskbridge.Core.Services;

public sealed class JsonConnectionStore : IConnectionStore
{
    private readonly string _filePath;
    private ConnectionsFile _data = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class ConnectionsFile
    {
        public int Version { get; set; } = 1;
        public List<ConnectionModel> Connections { get; set; } = [];
        public List<ConnectionGroup> Groups { get; set; } = [];
    }

    public JsonConnectionStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Deskbridge",
            "connections.json"))
    {
    }

    public JsonConnectionStore(string filePath)
    {
        _filePath = filePath;
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            _data = new ConnectionsFile();
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath, System.Text.Encoding.UTF8);
            _data = JsonSerializer.Deserialize<ConnectionsFile>(json, _jsonOptions) ?? new ConnectionsFile();
            // Version check for future migrations
            if (_data.Version > 1)
            {
                Log.Warning("connections.json version {Version} is newer than supported (1). Data may be incomplete", _data.Version);
            }
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to load connections.json, starting with empty collection");
            _data = new ConnectionsFile();
        }
    }

    public IReadOnlyList<ConnectionModel> GetAll() => _data.Connections.AsReadOnly();

    public ConnectionModel? GetById(Guid id) =>
        _data.Connections.FirstOrDefault(c => c.Id == id);

    public void Save(ConnectionModel connection)
    {
        var existing = _data.Connections.FindIndex(c => c.Id == connection.Id);
        if (existing >= 0)
        {
            connection.UpdatedAt = DateTime.UtcNow;
            _data.Connections[existing] = connection;
        }
        else
        {
            _data.Connections.Add(connection);
        }

        PersistAtomically();
    }

    public void Delete(Guid id)
    {
        _data.Connections.RemoveAll(c => c.Id == id);
        PersistAtomically();
    }

    public IReadOnlyList<ConnectionGroup> GetGroups() => _data.Groups.AsReadOnly();

    public ConnectionGroup? GetGroupById(Guid id) =>
        _data.Groups.FirstOrDefault(g => g.Id == id);

    public void SaveGroup(ConnectionGroup group)
    {
        var existing = _data.Groups.FindIndex(g => g.Id == group.Id);
        if (existing >= 0)
        {
            _data.Groups[existing] = group;
        }
        else
        {
            _data.Groups.Add(group);
        }

        PersistAtomically();
    }

    public void DeleteGroup(Guid groupId)
    {
        _data.Groups.RemoveAll(g => g.Id == groupId);

        // Orphan connections that belonged to this group
        foreach (var conn in _data.Connections.Where(c => c.GroupId == groupId))
        {
            conn.GroupId = null;
        }

        PersistAtomically();
    }

    public void DeleteBatch(IEnumerable<Guid> connectionIds, IEnumerable<Guid> groupIds)
    {
        // Remove groups first (they may orphan connections that are also in connectionIds)
        foreach (var groupId in groupIds)
        {
            _data.Groups.RemoveAll(g => g.Id == groupId);
            // Orphan connections belonging to deleted group
            foreach (var conn in _data.Connections.Where(c => c.GroupId == groupId))
                conn.GroupId = null;
        }

        // Remove connections
        var idSet = connectionIds.ToHashSet();
        if (idSet.Count > 0)
            _data.Connections.RemoveAll(c => idSet.Contains(c.Id));

        PersistAtomically(); // Single write at the end
    }

    private void PersistAtomically()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmpPath = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(_data, _jsonOptions);
        File.WriteAllText(tmpPath, json, System.Text.Encoding.UTF8);
        File.Move(tmpPath, _filePath, overwrite: true);
    }
}
