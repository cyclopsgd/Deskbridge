using BenchmarkDotNet.Attributes;
using Deskbridge.Benchmarks.Config;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Benchmarks.Benchmarks;

[Config(typeof(DeskbridgeBenchmarkConfig))]
public class SearchBenchmarks
{
    [Params(100, 200, 500, 1000)]
    public int ConnectionCount { get; set; }

    private ConnectionQueryService _queryService = null!;

    [GlobalSetup]
    public void Setup()
    {
        var (connections, _) = TestDataGenerator.Generate(ConnectionCount);
        _queryService = new ConnectionQueryService(connections);
    }

    [Benchmark]
    public IReadOnlyList<ConnectionModel> Search()
    {
        // Search for a realistic partial hostname that will match some results
        return _queryService.Search("srv-prod-web");
    }
}
