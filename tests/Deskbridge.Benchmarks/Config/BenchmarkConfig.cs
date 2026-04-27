using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;

namespace Deskbridge.Benchmarks.Config;

public class DeskbridgeBenchmarkConfig : ManualConfig
{
    public DeskbridgeBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(3)
            .WithIterationCount(10)
            .WithId("Default"));

        AddDiagnoser(MemoryDiagnoser.Default);

        AddExporter(JsonExporter.Full);
        AddExporter(CsvExporter.Default);
        AddExporter(MarkdownExporter.GitHub);

        AddValidator(JitOptimizationsValidator.FailOnError);
    }
}
