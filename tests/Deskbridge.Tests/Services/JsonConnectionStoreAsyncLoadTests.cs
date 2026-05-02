using System.IO;
using System.Threading.Tasks;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests.Services;

/// <summary>
/// Phase 21 (PERF-03): RED-then-GREEN coverage for JsonConnectionStore.LoadAsync.
/// Validates the async-load roundtrip, missing-file fallback, and Task.Run dispatch
/// invariant per D-04/D-05/D-06 in 21-CONTEXT.md.
/// </summary>
public sealed class JsonConnectionStoreAsyncLoadTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;
    private readonly JsonConnectionStore _store;

    public JsonConnectionStoreAsyncLoadTests()
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
    public async Task Save_ThenLoadAsync_RoundtripsConnection()
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

        // Create a new store instance and load from disk via the async path.
        var store2 = new JsonConnectionStore(_filePath);
        await store2.LoadAsync();

        var loaded = store2.GetAll();
        loaded.Should().HaveCount(1);
        loaded[0].Id.Should().Be(conn.Id);
        loaded[0].Name.Should().Be("TestServer");
        loaded[0].Hostname.Should().Be("server01.local");
        loaded[0].Port.Should().Be(3389);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_StartsWithEmptyStore()
    {
        // Ctor created an empty temp dir; do NOT seed the file.
        var store2 = new JsonConnectionStore(Path.Combine(_tempDir, "nonexistent.json"));

        await store2.LoadAsync();

        store2.GetAll().Should().BeEmpty();
        store2.GetGroups().Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_DispatchesViaTaskRun()
    {
        // Seed a non-trivial file so the wrapped Load has work to do.
        _store.Save(new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "X",
            Hostname = "h",
            Port = 3389,
            Protocol = Protocol.Rdp
        });

        var store2 = new JsonConnectionStore(_filePath);

        // Capture the Task immediately — Task.Run schedules to the threadpool, so
        // the synchronously-returned Task is not yet completed at the call site.
        var task = store2.LoadAsync();
        task.IsCompleted.Should().BeFalse(
            "LoadAsync must dispatch off the calling thread via Task.Run (D-05/D-06)");

        await task;
        store2.GetAll().Should().HaveCount(1);
    }
}
