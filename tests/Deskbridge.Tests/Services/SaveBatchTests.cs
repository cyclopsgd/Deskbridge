using System.IO;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests.Services;

[Trait("Category", "SaveBatch")]
public sealed class SaveBatchTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;
    private readonly JsonConnectionStore _store;

    public SaveBatchTests()
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
    public void SaveBatch_With100Connections_AllRoundTripThroughLoad()
    {
        // Arrange: build 100 connections
        var connections = Enumerable.Range(0, 100)
            .Select(i => new ConnectionModel { Id = Guid.NewGuid(), Name = $"Server{i}" })
            .ToList();

        // Act
        _store.SaveBatch(connections, []);

        // Assert: round-trip through a fresh store
        var store2 = new JsonConnectionStore(_filePath);
        store2.Load();
        store2.GetAll().Should().HaveCount(100);
        for (int i = 0; i < 100; i++)
        {
            store2.GetAll().Should().Contain(c => c.Name == $"Server{i}");
        }
    }

    [Fact]
    public void SaveBatch_UpsertsById_UpdatesExistingAndAddsNew()
    {
        // Arrange: save 3 connections individually
        var existing1 = new ConnectionModel { Id = Guid.NewGuid(), Name = "Existing1" };
        var existing2 = new ConnectionModel { Id = Guid.NewGuid(), Name = "Existing2" };
        var existing3 = new ConnectionModel { Id = Guid.NewGuid(), Name = "Existing3" };
        _store.Save(existing1);
        _store.Save(existing2);
        _store.Save(existing3);
        _store.GetAll().Should().HaveCount(3);

        // Act: SaveBatch with 2 updated (same Ids, new names) + 2 new
        var updated1 = new ConnectionModel { Id = existing1.Id, Name = "Updated1" };
        var updated2 = new ConnectionModel { Id = existing2.Id, Name = "Updated2" };
        var new1 = new ConnectionModel { Id = Guid.NewGuid(), Name = "New1" };
        var new2 = new ConnectionModel { Id = Guid.NewGuid(), Name = "New2" };
        _store.SaveBatch([updated1, updated2, new1, new2], []);

        // Assert: 5 total (3 original - 2 updated + 2 updated + 2 new = 5)
        var all = _store.GetAll();
        all.Should().HaveCount(5);
        all.Should().Contain(c => c.Name == "Updated1");
        all.Should().Contain(c => c.Name == "Updated2");
        all.Should().Contain(c => c.Name == "Existing3");
        all.Should().Contain(c => c.Name == "New1");
        all.Should().Contain(c => c.Name == "New2");
    }

    [Fact]
    public void SaveBatch_SetsUpdatedAtOnUpdatePath_NotOnInsert()
    {
        // Arrange: save a connection, record its timestamps
        var original = new ConnectionModel { Id = Guid.NewGuid(), Name = "Original" };
        _store.Save(original);

        // Capture a pivot time between the individual Save and the upcoming SaveBatch
        Thread.Sleep(15);
        var pivot = DateTime.UtcNow;
        Thread.Sleep(15);

        // Act: SaveBatch with same Id (update) + new Id (insert)
        var updateConn = new ConnectionModel { Id = original.Id, Name = "Updated" };
        var insertConn = new ConnectionModel { Id = Guid.NewGuid(), Name = "Inserted" };
        var insertCreatedAt = insertConn.CreatedAt;
        _store.SaveBatch([updateConn, insertConn], []);

        // Assert: updated item has UpdatedAt after pivot, inserted item has UpdatedAt before pivot
        var updated = _store.GetById(updateConn.Id)!;
        var inserted = _store.GetById(insertConn.Id)!;

        updated.UpdatedAt.Should().BeOnOrAfter(pivot, "updated items should get a new UpdatedAt timestamp");
        inserted.UpdatedAt.Should().BeBefore(pivot, "inserted items should keep their original UpdatedAt");
    }

    [Fact]
    public void SaveBatch_GroupsBeforeConnections_NoOrphans()
    {
        // Arrange: a new group and a connection referencing that group's Id
        var groupId = Guid.NewGuid();
        var group = new ConnectionGroup { Id = groupId, Name = "NewGroup" };
        var connection = new ConnectionModel { Id = Guid.NewGuid(), Name = "GroupedConn", GroupId = groupId };

        // Act: SaveBatch with group and connection together
        _store.SaveBatch([connection], [group]);

        // Assert: round-trip load shows connection with correct GroupId
        var store2 = new JsonConnectionStore(_filePath);
        store2.Load();
        var loaded = store2.GetAll().First(c => c.Name == "GroupedConn");
        loaded.GroupId.Should().Be(groupId);
        store2.GetGroups().Should().ContainSingle(g => g.Id == groupId);
    }

    [Fact]
    public void SaveBatch_WithGroupUpsert_UpdatesExistingGroup()
    {
        // Arrange: save a group individually
        var groupId = Guid.NewGuid();
        _store.SaveGroup(new ConnectionGroup { Id = groupId, Name = "OriginalName" });
        _store.GetGroups().Should().HaveCount(1);

        // Act: SaveBatch with same group Id but new name
        _store.SaveBatch([], [new ConnectionGroup { Id = groupId, Name = "UpdatedName" }]);

        // Assert: still 1 group with updated name
        var groups = _store.GetGroups();
        groups.Should().HaveCount(1);
        groups[0].Name.Should().Be("UpdatedName");
    }

    [Fact]
    public void SaveBatch_EmptyCollections_IsNoOp()
    {
        // Arrange: save some data first
        _store.Save(new ConnectionModel { Id = Guid.NewGuid(), Name = "Untouched" });
        _store.SaveGroup(new ConnectionGroup { Id = Guid.NewGuid(), Name = "StillHere" });

        // Act: SaveBatch with empty lists
        _store.SaveBatch([], []);

        // Assert: data unchanged
        _store.GetAll().Should().HaveCount(1);
        _store.GetGroups().Should().HaveCount(1);
    }

    [Fact]
    public void SaveBatch_PersistsAtomically_NoTmpFileRemains()
    {
        // Arrange: build 10 connections
        var connections = Enumerable.Range(0, 10)
            .Select(i => new ConnectionModel { Id = Guid.NewGuid(), Name = $"Conn{i}" })
            .ToList();

        // Act
        _store.SaveBatch(connections, []);

        // Assert: .tmp file does not exist after call (atomic rename completed)
        File.Exists(_filePath + ".tmp").Should().BeFalse();

        // Verify data persisted
        var store2 = new JsonConnectionStore(_filePath);
        store2.Load();
        store2.GetAll().Should().HaveCount(10);
    }
}
