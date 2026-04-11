using System.IO;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests;

public sealed class JsonConnectionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;
    private readonly JsonConnectionStore _store;

    public JsonConnectionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DeskbridgeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "connections.json");
        _store = new JsonConnectionStore(_filePath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Save_ThenLoad_RoundtripsConnection()
    {
        var conn = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "TestServer",
            Hostname = "server01.local",
            Port = 3389,
            Protocol = Protocol.Rdp
        };

        _store.Save(conn);

        // Create a new store instance and load from disk
        var store2 = new JsonConnectionStore(_filePath);
        store2.Load();

        var loaded = store2.GetAll();
        loaded.Should().HaveCount(1);
        loaded[0].Id.Should().Be(conn.Id);
        loaded[0].Name.Should().Be("TestServer");
        loaded[0].Hostname.Should().Be("server01.local");
        loaded[0].Port.Should().Be(3389);
    }

    [Fact]
    public void Save_ThenDelete_GetAllReturnsEmpty()
    {
        var conn = new ConnectionModel { Id = Guid.NewGuid(), Name = "ToDelete" };
        _store.Save(conn);
        _store.Delete(conn.Id);

        _store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void SaveGroup_ThenGetGroups_RoundtripsCorrectly()
    {
        var group = new ConnectionGroup
        {
            Id = Guid.NewGuid(),
            Name = "Production",
            SortOrder = 1
        };

        _store.SaveGroup(group);

        var store2 = new JsonConnectionStore(_filePath);
        store2.Load();

        var groups = store2.GetGroups();
        groups.Should().HaveCount(1);
        groups[0].Id.Should().Be(group.Id);
        groups[0].Name.Should().Be("Production");
    }

    [Fact]
    public void DeleteGroup_RemovesGroupFromGetGroups()
    {
        var group = new ConnectionGroup { Id = Guid.NewGuid(), Name = "ToRemove" };
        _store.SaveGroup(group);
        _store.DeleteGroup(group.Id);

        _store.GetGroups().Should().BeEmpty();
    }

    [Fact]
    public void DeleteGroup_OrphansConnectionsBySettingGroupIdNull()
    {
        var groupId = Guid.NewGuid();
        var group = new ConnectionGroup { Id = groupId, Name = "Servers" };
        var conn = new ConnectionModel { Id = Guid.NewGuid(), Name = "Server1", GroupId = groupId };

        _store.SaveGroup(group);
        _store.Save(conn);
        _store.DeleteGroup(groupId);

        var loaded = _store.GetAll();
        loaded.Should().HaveCount(1);
        loaded[0].GroupId.Should().BeNull();
    }

    [Fact]
    public void AtomicWrite_UsesTmpFileThenRename()
    {
        var conn = new ConnectionModel { Id = Guid.NewGuid(), Name = "Atomic" };
        _store.Save(conn);

        // After save, the .tmp file should NOT exist (it was renamed)
        File.Exists(_filePath + ".tmp").Should().BeFalse();
        // But the actual file should exist
        File.Exists(_filePath).Should().BeTrue();
    }

    [Fact]
    public void Load_NonexistentFile_ReturnsEmptyCollections()
    {
        var missingPath = Path.Combine(_tempDir, "does_not_exist.json");
        var store = new JsonConnectionStore(missingPath);
        store.Load();

        store.GetAll().Should().BeEmpty();
        store.GetGroups().Should().BeEmpty();
    }

    [Fact]
    public void Save_SetsSchemaVersion1InJson()
    {
        var conn = new ConnectionModel { Id = Guid.NewGuid(), Name = "VersionCheck" };
        _store.Save(conn);

        var json = File.ReadAllText(_filePath);
        json.Should().Contain("\"version\"");
        json.Should().Contain("1");
    }

    [Fact]
    public void Save_PreservesExistingConnections_UpsertById()
    {
        var id = Guid.NewGuid();
        var conn1 = new ConnectionModel { Id = id, Name = "Original", Hostname = "host1" };
        _store.Save(conn1);

        var conn2 = new ConnectionModel { Id = id, Name = "Updated", Hostname = "host2" };
        _store.Save(conn2);

        var all = _store.GetAll();
        all.Should().HaveCount(1);
        all[0].Name.Should().Be("Updated");
        all[0].Hostname.Should().Be("host2");
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmpty()
    {
        // Write invalid JSON to the file
        File.WriteAllText(_filePath, "{{{{not valid json");

        var store = new JsonConnectionStore(_filePath);
        store.Load();

        // Should not throw, should return empty collections
        store.GetAll().Should().BeEmpty();
        store.GetGroups().Should().BeEmpty();
    }

    [Fact]
    public void GetById_ReturnsMatchingConnection()
    {
        var id = Guid.NewGuid();
        var conn = new ConnectionModel { Id = id, Name = "FindMe" };
        _store.Save(conn);

        _store.GetById(id).Should().NotBeNull();
        _store.GetById(id)!.Name.Should().Be("FindMe");
    }

    [Fact]
    public void GetById_ReturnsNull_WhenNotFound()
    {
        _store.GetById(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void GetGroupById_ReturnsMatchingGroup()
    {
        var id = Guid.NewGuid();
        var group = new ConnectionGroup { Id = id, Name = "FindGroup" };
        _store.SaveGroup(group);

        _store.GetGroupById(id).Should().NotBeNull();
        _store.GetGroupById(id)!.Name.Should().Be("FindGroup");
    }

    [Fact]
    public void SaveGroup_UpsertsById()
    {
        var id = Guid.NewGuid();
        _store.SaveGroup(new ConnectionGroup { Id = id, Name = "Original" });
        _store.SaveGroup(new ConnectionGroup { Id = id, Name = "Updated" });

        _store.GetGroups().Should().HaveCount(1);
        _store.GetGroups()[0].Name.Should().Be("Updated");
    }
}
