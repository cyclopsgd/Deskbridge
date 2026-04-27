using BenchmarkDotNet.Attributes;
using Deskbridge.Benchmarks.Config;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Benchmarks.Benchmarks;

[Config(typeof(DeskbridgeBenchmarkConfig))]
public class TreeBuildBenchmarks
{
    [Params(100, 200, 500, 1000)]
    public int ConnectionCount { get; set; }

    private IReadOnlyList<ConnectionModel> _connections = null!;
    private IReadOnlyList<ConnectionGroup> _groups = null!;

    [GlobalSetup]
    public void Setup()
    {
        var (connections, groups) = TestDataGenerator.Generate(ConnectionCount);
        _connections = connections;
        _groups = groups;
    }

    [Benchmark]
    public IReadOnlyList<TreeNode> BuildTree()
    {
        return ConnectionTreeBuilder.Build(_connections, _groups);
    }
}
