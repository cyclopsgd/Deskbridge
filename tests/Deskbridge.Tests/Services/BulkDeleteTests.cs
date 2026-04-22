using System.IO;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests.Services;

[Trait("Category", "Stability")]
public sealed class BulkDeleteTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;
    private readonly JsonConnectionStore _store;

    public BulkDeleteTests()
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
    public void DeleteBatch_With10Connections_RemovesAll()
    {
        // Arrange: save 10 connections
        var ids = new List<Guid>();
        for (int i = 0; i < 10; i++)
        {
            var conn = new ConnectionModel { Id = Guid.NewGuid(), Name = $"Server{i}" };
            _store.Save(conn);
            ids.Add(conn.Id);
        }
        _store.GetAll().Should().HaveCount(10);

        // Act
        _store.DeleteBatch(ids, []);

        // Assert
        _store.GetAll().Should().BeEmpty();

        // Verify file was written (round-trip)
        var store2 = new JsonConnectionStore(_filePath);
        store2.Load();
        store2.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void DeleteBatch_With3Groups_RemovesGroupsAndOrphansChildren()
    {
        // Arrange: 3 groups, each with 2 connections
        var groupIds = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            var groupId = Guid.NewGuid();
            _store.SaveGroup(new ConnectionGroup { Id = groupId, Name = $"Group{i}" });
            _store.Save(new ConnectionModel { Id = Guid.NewGuid(), Name = $"Conn{i}A", GroupId = groupId });
            _store.Save(new ConnectionModel { Id = Guid.NewGuid(), Name = $"Conn{i}B", GroupId = groupId });
            groupIds.Add(groupId);
        }
        _store.GetGroups().Should().HaveCount(3);
        _store.GetAll().Should().HaveCount(6);

        // Act: delete groups only
        _store.DeleteBatch([], groupIds);

        // Assert: groups gone, connections orphaned (GroupId = null)
        _store.GetGroups().Should().BeEmpty();
        _store.GetAll().Should().HaveCount(6);
        _store.GetAll().Should().AllSatisfy(c => c.GroupId.Should().BeNull());
    }

    [Fact]
    public void DeleteBatch_MixedConnectionsAndGroups_HandlesBoth()
    {
        // Arrange: 1 group with 2 connections, plus 2 standalone connections to delete
        var groupId = Guid.NewGuid();
        _store.SaveGroup(new ConnectionGroup { Id = groupId, Name = "MixedGroup" });
        var groupConn1 = new ConnectionModel { Id = Guid.NewGuid(), Name = "GroupConn1", GroupId = groupId };
        var groupConn2 = new ConnectionModel { Id = Guid.NewGuid(), Name = "GroupConn2", GroupId = groupId };
        _store.Save(groupConn1);
        _store.Save(groupConn2);

        var standalone1 = new ConnectionModel { Id = Guid.NewGuid(), Name = "Standalone1" };
        var standalone2 = new ConnectionModel { Id = Guid.NewGuid(), Name = "Standalone2" };
        var keeper = new ConnectionModel { Id = Guid.NewGuid(), Name = "Keeper" };
        _store.Save(standalone1);
        _store.Save(standalone2);
        _store.Save(keeper);

        // Act: delete the group + standalone connections, keep the keeper
        _store.DeleteBatch(
            [standalone1.Id, standalone2.Id],
            [groupId]);

        // Assert: group gone, standalone conns gone, group children orphaned, keeper intact
        _store.GetGroups().Should().BeEmpty();
        var remaining = _store.GetAll();
        remaining.Should().HaveCount(3); // groupConn1, groupConn2 (orphaned), keeper
        remaining.Should().Contain(c => c.Id == keeper.Id);
        remaining.Where(c => c.Id == groupConn1.Id || c.Id == groupConn2.Id)
            .Should().AllSatisfy(c => c.GroupId.Should().BeNull());
    }

    [Fact]
    public void DeleteBatch_EmptyCollections_IsNoOp()
    {
        // Arrange: save some data
        _store.Save(new ConnectionModel { Id = Guid.NewGuid(), Name = "Untouched" });
        _store.SaveGroup(new ConnectionGroup { Id = Guid.NewGuid(), Name = "StillHere" });

        // Act: empty delete
        _store.DeleteBatch([], []);

        // Assert: nothing changed
        _store.GetAll().Should().HaveCount(1);
        _store.GetGroups().Should().HaveCount(1);
    }

    [Fact]
    public void DeleteBatch_PersistsAtomically_NewStoreSeesResults()
    {
        // Arrange: save 5 connections and 2 groups
        var connIds = new List<Guid>();
        for (int i = 0; i < 5; i++)
        {
            var conn = new ConnectionModel { Id = Guid.NewGuid(), Name = $"Conn{i}" };
            _store.Save(conn);
            connIds.Add(conn.Id);
        }
        var groupId = Guid.NewGuid();
        _store.SaveGroup(new ConnectionGroup { Id = groupId, Name = "ToDelete" });

        // Act
        _store.DeleteBatch(connIds.Take(3).ToList(), [groupId]);

        // Assert: new store from same file sees deletions
        var store2 = new JsonConnectionStore(_filePath);
        store2.Load();
        store2.GetAll().Should().HaveCount(2); // only 2 remaining connections
        store2.GetGroups().Should().BeEmpty();

        // Verify .tmp file doesn't exist (atomic rename completed)
        File.Exists(_filePath + ".tmp").Should().BeFalse();
    }
}
