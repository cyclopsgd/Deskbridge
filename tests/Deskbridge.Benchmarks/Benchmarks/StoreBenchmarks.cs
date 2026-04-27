using System.IO;
using BenchmarkDotNet.Attributes;
using Deskbridge.Benchmarks.Config;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Benchmarks.Benchmarks;

[Config(typeof(DeskbridgeBenchmarkConfig))]
public class StoreBenchmarks
{
    [Params(100, 200, 500, 1000)]
    public int ConnectionCount { get; set; }

    private string _tempDir = null!;
    private string _filePath = null!;
    private IReadOnlyList<ConnectionModel> _connections = null!;
    private IReadOnlyList<ConnectionGroup> _groups = null!;
    private JsonConnectionStore _store = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"deskbridge-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "connections.json");

        var (connections, groups) = TestDataGenerator.Generate(ConnectionCount);
        _connections = connections;
        _groups = groups;
    }

    // --- Load: file pre-written, measure only Load() ---

    [IterationSetup(Target = nameof(Load))]
    public void SetupForLoad()
    {
        var seedStore = new JsonConnectionStore(_filePath);
        seedStore.Load();
        seedStore.SaveBatch(_connections, _groups);
    }

    [Benchmark]
    public void Load()
    {
        var store = new JsonConnectionStore(_filePath);
        store.Load();
    }

    // --- Save: single connection upsert ---

    [IterationSetup(Target = nameof(Save))]
    public void SetupForSave()
    {
        _store = new JsonConnectionStore(_filePath);
        _store.Load();
        _store.SaveBatch(_connections, _groups);
    }

    [Benchmark]
    public void Save()
    {
        _store.Save(_connections[0]);
    }

    // --- SaveBatch: full batch write ---

    [IterationSetup(Target = nameof(SaveBatch))]
    public void SetupForSaveBatch()
    {
        _store = new JsonConnectionStore(_filePath);
        _store.Load();
    }

    [Benchmark]
    public void SaveBatch()
    {
        _store.SaveBatch(_connections, _groups);
    }

    // --- DeleteBatch: 10% deletion ---

    [IterationSetup(Target = nameof(DeleteBatch))]
    public void SetupForDelete()
    {
        _store = new JsonConnectionStore(_filePath);
        _store.Load();
        _store.SaveBatch(_connections, _groups);
    }

    [Benchmark]
    public void DeleteBatch()
    {
        var idsToDelete = _connections.Take(ConnectionCount / 10).Select(c => c.Id);
        _store.DeleteBatch(idsToDelete, []);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
