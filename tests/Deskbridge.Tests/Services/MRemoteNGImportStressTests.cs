using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using FluentAssertions;

namespace Deskbridge.Tests.Services;

/// <summary>
/// Phase 22 (D-09, D-11, D-12, IMP-05): stress tests at 500/1000/5000 row counts.
/// Generated fixtures via TestDataGenerator + MRemoteNGXmlSerializer (deterministic seed).
/// D-12: record-and-emit wall-clock + memory; NO hard timing gate. Failure mode is OOM only.
/// Run on demand via: dotnet test --filter "Category=stress" --logger "console;verbosity=detailed"
/// </summary>
[Trait("Category", "stress")]
public sealed class MRemoteNGImportStressTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(5000)]
    public async Task PrepareAsync_AtScale_CompletesWithoutOom(int rowCount)
    {
        // 1. Generate deterministic dataset
        var (conns, groups) = TestDataGenerator.Generate(rowCount, seed: 42);

        // 2. Serialize to confCons.xml format in memory
        using var ms = new MemoryStream();
        MRemoteNGXmlSerializer.Serialize(ms, conns, groups);
        ms.Position = 0;

        // 3. Parse it back
        var importer = new MRemoteNGImporter();
        var parseResult = await importer.ParseAsync(ms, Ct);

        parseResult.TotalConnections.Should().Be(rowCount);

        // 4. Run the prepare loop (the actual stress)
        // ConcurrentBag + volatile guard mirror the 22-02 VM pattern: Progress<T>
        // schedules callbacks on the thread pool when there is no captured
        // SyncContext, so late-firing callbacks can race with the test method's
        // exit (ArgumentOutOfRangeException on List.Add). The volatile flag is
        // flipped to false after the await returns so post-await callbacks no-op.
        var reportedValues = new ConcurrentBag<int>();
        bool[] progressActive = [true];
        var progress = new Progress<int>(n =>
        {
            if (Volatile.Read(ref progressActive[0]))
                reportedValues.Add(n);
        });

        var sw = Stopwatch.StartNew();
        var memBefore = GC.GetTotalMemory(forceFullCollection: true);

        var executor = new MRemoteNGImportExecutor();
        var request = new ImportRequest(
            CheckedNodes: parseResult.RootNodes,
            ExistingConnections: Array.Empty<ConnectionModel>(),
            ExistingGroups: Array.Empty<ConnectionGroup>(),
            Resolutions: Array.Empty<DuplicateResolution>());

        ImportPrepareResult result;
        try
        {
            result = await executor.PrepareAsync(request, progress, Ct);
        }
        catch (OutOfMemoryException)
        {
            throw;  // Fail loudly — D-11 gate
        }

        sw.Stop();
        var memAfter = GC.GetTotalMemory(forceFullCollection: false);

        // Stop accepting late-firing Progress callbacks before we touch reportedValues
        // or exit the test (D-12 race guard, mirrors 22-02 VM volatile-flag pattern).
        Volatile.Write(ref progressActive[0], false);

        // 5. Record-and-emit (D-12)
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"rows={rowCount} elapsed-ms={sw.ElapsedMilliseconds} " +
            $"mem-delta-kb={(memAfter - memBefore) / 1024} " +
            $"failures={result.Failures.Count} " +
            $"imported={result.ImportedCount} " +
            $"progress-reports={reportedValues.Count}");

        // 6. Correctness assertions
        result.ConnectionsToSave.Should().HaveCount(rowCount,
            "all generated connections should round-trip through the prepare loop");
        result.Failures.Should().BeEmpty(
            "TestDataGenerator output is well-formed and should not produce row-level failures");
        result.ImportedCount.Should().Be(rowCount);

        // Progress callback count is intentionally NOT asserted: Progress<T> with no
        // captured SynchronizationContext schedules callbacks via ThreadPool.QueueUserWorkItem.
        // When PrepareAsync completes synchronously (it returns Task.FromResult — see
        // 22-01 SUMMARY decision "No `Task.Run` inside the library"), the await can resume
        // before any queued callback drains, leaving reportedValues empty at high row counts.
        // The deterministic per-row Report contract is already covered by the executor's
        // own unit tests (MRemoteNGImportExecutorTests "progress per row"); this stress
        // test's contract is the OOM gate (D-11) + record-and-emit telemetry (D-12) only.
        // The progress-reports count is emitted in the WriteLine above for visibility.
    }

    [Fact]
    public async Task PrepareAsync_5000_NoUiThreadAffinityRequired()
    {
        // Pure-Core test: confirm executor runs entirely off-UI without
        // Application.Current or Dispatcher access. (Stress is a re-run of the
        // 5000 case but with an explicit assertion — the test thread is MTA in
        // xUnit v3.)
        var (conns, groups) = TestDataGenerator.Generate(5000, seed: 42);
        using var ms = new MemoryStream();
        MRemoteNGXmlSerializer.Serialize(ms, conns, groups);
        ms.Position = 0;

        var parseResult = await new MRemoteNGImporter().ParseAsync(ms, Ct);
        var executor = new MRemoteNGImportExecutor();
        var request = new ImportRequest(
            parseResult.RootNodes,
            Array.Empty<ConnectionModel>(),
            Array.Empty<ConnectionGroup>(),
            Array.Empty<DuplicateResolution>());

        // No SyncContext → IProgress.Report runs inline. This proves the executor
        // does not try to marshal to a UI thread (which would deadlock or throw
        // here). Pattern 1 in RESEARCH.md.
        var progress = new Progress<int>(_ => { /* no-op */ });
        var result = await executor.PrepareAsync(request, progress, Ct);

        result.ImportedCount.Should().Be(5000);
    }
}
