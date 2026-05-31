using System.IO;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests.Services;

/// <summary>
/// BULK-03 persistence contract (Phase 23 — bulk-operations-ux).
///
/// Pins the store-side behavior that bulk edit relies on: a single atomic
/// <see cref="JsonConnectionStore.SaveBatch"/> commit (NOT N× per-item Save — IMP-04
/// rationale) persists all edited connections and survives a store reload.
///
/// These tests use ONLY existing production types (IConnectionStore / JsonConnectionStore /
/// SaveBatch) so they compile and pass TODAY (Wave 0). Plans 23-02 / 23-03 layer the
/// BulkEditViewModel diffing/apply logic on top; this file locks the persistence floor.
///
/// Temp-file setup mirrors BulkDeleteTests.cs:14-20; reload assertion mirrors BulkDeleteTests.cs:126.
/// </summary>
[Trait("Category", "Stability")]
public sealed class BulkEditPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;
    private readonly JsonConnectionStore _store;

    public BulkEditPersistenceTests()
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
    public void SaveBatch_PersistsEditedConnections_SurvivesStoreReload()
    {
        // Arrange: seed N=5 connections with distinct hostnames/ports.
        var seeded = new List<ConnectionModel>();
        for (int i = 0; i < 5; i++)
        {
            var conn = new ConnectionModel
            {
                Id = Guid.NewGuid(),
                Name = $"Server{i}",
                Hostname = $"host{i}.local",
                Port = 3389,
                Domain = "CORP",
            };
            _store.Save(conn);
            seeded.Add(conn);
        }

        // Mutate 2 fields (Hostname + Domain) on a SUBSET (first 2 connections).
        // Bulk edit only writes checked fields — Name must be left untouched here.
        var edited = seeded.Take(2).ToList();
        foreach (var c in edited)
        {
            c.Hostname = "edited.example.com";
            c.Domain = "NEWDOMAIN";
        }

        // Act: single atomic SaveBatch commit (no groups changed).
        _store.SaveBatch(edited, Array.Empty<ConnectionGroup>());

        // Assert: a brand-new store over the same file sees the edits and leaves others alone.
        var reloaded = new JsonConnectionStore(_filePath);
        reloaded.Load();
        var all = reloaded.GetAll();
        all.Should().HaveCount(5);

        foreach (var e in edited)
        {
            var persisted = all.Single(c => c.Id == e.Id);
            persisted.Hostname.Should().Be("edited.example.com");
            persisted.Domain.Should().Be("NEWDOMAIN");
            persisted.Name.Should().Be(e.Name, "bulk edit never modifies Name");
        }

        // Untouched connections retain their seeded values.
        var untouched = seeded.Skip(2);
        foreach (var u in untouched)
        {
            var persisted = all.Single(c => c.Id == u.Id);
            persisted.Hostname.Should().Be(u.Hostname);
            persisted.Domain.Should().Be("CORP");
        }
    }

    [Fact]
    public void SaveBatch_SingleWrite_NotPerItemSave()
    {
        // IMP-04 rationale: bulk edit must persist via ONE SaveBatch call, never N× Save,
        // so the on-disk file is mutated exactly once (atomic .tmp rename). We pin the
        // observable contract — one SaveBatch round-trips every edited item — and assert
        // the .tmp file is gone (atomic rename completed), mirroring BulkDeleteTests.cs:126.
        var seeded = new List<ConnectionModel>();
        for (int i = 0; i < 4; i++)
        {
            var conn = new ConnectionModel
            {
                Id = Guid.NewGuid(),
                Name = $"Conn{i}",
                Hostname = $"h{i}",
                Port = 3389,
            };
            _store.Save(conn);
            seeded.Add(conn);
        }

        // Mutate Port on ALL seeded connections, commit in a single call.
        foreach (var c in seeded)
        {
            c.Port = 4000;
        }
        _store.SaveBatch(seeded, Array.Empty<ConnectionGroup>());

        // Assert: one round-trip carried every edit.
        var reloaded = new JsonConnectionStore(_filePath);
        reloaded.Load();
        reloaded.GetAll().Should().HaveCount(4);
        reloaded.GetAll().Should().AllSatisfy(c => c.Port.Should().Be(4000));

        // Atomic rename completed — no lingering .tmp.
        File.Exists(_filePath + ".tmp").Should().BeFalse();
    }
}
