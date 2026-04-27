using BenchmarkDotNet.Attributes;
using Deskbridge.Benchmarks.Config;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Benchmarks.Benchmarks;

[Config(typeof(DeskbridgeBenchmarkConfig))]
public class QueryBenchmarks
{
    [Params(100, 200, 500, 1000)]
    public int ConnectionCount { get; set; }

    private ConnectionQueryService _queryService = null!;
    private ConnectionFilter _tagFilter = null!;
    private ConnectionFilter _protocolFilter = null!;

    [GlobalSetup]
    public void Setup()
    {
        var (connections, groups) = TestDataGenerator.Generate(ConnectionCount);
        _queryService = new ConnectionQueryService(connections);

        // Use non-text filters to avoid measuring Search performance (Pitfall 4)
        _tagFilter = new ConnectionFilter { Tag = "production" };
        _protocolFilter = new ConnectionFilter { Protocol = Protocol.Rdp };
    }

    [Benchmark]
    public IReadOnlyList<ConnectionModel> GetByFilter_Tag()
    {
        return _queryService.GetByFilter(_tagFilter);
    }

    [Benchmark]
    public IReadOnlyList<ConnectionModel> GetByFilter_Protocol()
    {
        return _queryService.GetByFilter(_protocolFilter);
    }
}
