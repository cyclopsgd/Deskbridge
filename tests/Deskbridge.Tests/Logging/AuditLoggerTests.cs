using System.IO;
using System.Text.Json;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using Deskbridge.Tests.Security;
using Serilog;

namespace Deskbridge.Tests.Logging;

/// <summary>
/// LOG-02 / LOG-03 / D-10 / D-13 / Pitfall 2 / CONTEXT Q5 coverage for
/// <see cref="AuditLogger"/>. Every test isolates to a fresh temp directory via
/// <see cref="TempDirScope"/> so concurrent test runs cannot stomp on each other.
/// </summary>
public sealed class AuditLoggerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------
    // Test 1 — schema round-trip
    // ------------------------------------------------------------------
    [Fact]
    public async Task LogAsync_SerialisesAuditRecordWithAllFields()
    {
        using var scope = new TempDirScope();
        var logger = new AuditLogger(scope.Path);

        var connectionId = Guid.NewGuid();
        var record = new AuditRecord(
            Ts: "2026-04-14T12:00:00Z",
            Type: "Connected",
            ConnectionId: connectionId,
            User: "george",
            Outcome: "success");

        await logger.LogAsync(record, Ct);
        logger.Dispose();

        var file = Path.Combine(scope.Path, $"audit-{DateTime.UtcNow:yyyy-MM}.jsonl");
        var text = await File.ReadAllTextAsync(file, Ct);
        var doc = JsonDocument.Parse(text.TrimEnd('\n'));

        doc.RootElement.GetProperty("ts").GetString().Should().Be("2026-04-14T12:00:00Z");
        doc.RootElement.GetProperty("type").GetString().Should().Be("Connected");
        doc.RootElement.GetProperty("connectionId").GetGuid().Should().Be(connectionId);
        doc.RootElement.GetProperty("user").GetString().Should().Be("george");
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("success");
        doc.RootElement.TryGetProperty("errorCode", out _).Should().BeFalse(
            "ErrorCode is null and JsonIgnoreCondition.WhenWritingNull should omit it (D-10)");
    }

    // ------------------------------------------------------------------
    // Test 2 — append, not overwrite
    // ------------------------------------------------------------------
    [Fact]
    public async Task LogAsync_AppendsSecondCall_DoesNotOverwriteFirst()
    {
        using var scope = new TempDirScope();
        var logger = new AuditLogger(scope.Path);

        await logger.LogAsync(new AuditRecord("t1", "Connected", Guid.NewGuid(), "u", "success"), Ct);
        await logger.LogAsync(new AuditRecord("t2", "Disconnected", Guid.NewGuid(), "u", "success"), Ct);
        logger.Dispose();

        var file = Path.Combine(scope.Path, $"audit-{DateTime.UtcNow:yyyy-MM}.jsonl");
        var lines = (await File.ReadAllTextAsync(file, Ct)).TrimEnd('\n').Split('\n');
        lines.Should().HaveCount(2);
        JsonDocument.Parse(lines[0]).RootElement.GetProperty("ts").GetString().Should().Be("t1");
        JsonDocument.Parse(lines[1]).RootElement.GetProperty("ts").GetString().Should().Be("t2");
    }

    // ------------------------------------------------------------------
    // Test 3 — line terminator is exactly one '\n'
    // ------------------------------------------------------------------
    [Fact]
    public async Task LogAsync_TerminatesEachLineWithSingleLF()
    {
        using var scope = new TempDirScope();
        var logger = new AuditLogger(scope.Path);

        await logger.LogAsync(new AuditRecord("t", "Connected", null, "u", "success"), Ct);
        logger.Dispose();

        var file = Path.Combine(scope.Path, $"audit-{DateTime.UtcNow:yyyy-MM}.jsonl");
        var bytes = await File.ReadAllBytesAsync(file, Ct);
        bytes[^1].Should().Be((byte)'\n', "every audit line must terminate with a single LF");
        // No CR before the LF — pure LF, not CRLF.
        if (bytes.Length >= 2)
            bytes[^2].Should().NotBe((byte)'\r', "must be LF-only, not CRLF");
    }

    // ------------------------------------------------------------------
    // Test 4 — monthly rotation (D-13)
    // ------------------------------------------------------------------
    [Fact]
    public async Task LogAsync_RotatesFile_AtUtcMonthBoundary()
    {
        using var scope = new TempDirScope();
        var logger = new AuditLogger(scope.Path);

        // Freeze clock to last minute of April 2026.
        var clock = new DateTime(2026, 4, 30, 23, 59, 0, DateTimeKind.Utc);
        logger.UtcNowProvider = () => clock;
        await logger.LogAsync(new AuditRecord("t-april", "Connected", null, "u", "success"), Ct);

        // Tick into May 2026.
        clock = new DateTime(2026, 5, 1, 0, 1, 0, DateTimeKind.Utc);
        await logger.LogAsync(new AuditRecord("t-may", "Connected", null, "u", "success"), Ct);
        logger.Dispose();

        var aprilFile = Path.Combine(scope.Path, "audit-2026-04.jsonl");
        var mayFile = Path.Combine(scope.Path, "audit-2026-05.jsonl");
        File.Exists(aprilFile).Should().BeTrue();
        File.Exists(mayFile).Should().BeTrue();

        var aprilLines = (await File.ReadAllTextAsync(aprilFile, Ct)).TrimEnd('\n').Split('\n');
        var mayLines = (await File.ReadAllTextAsync(mayFile, Ct)).TrimEnd('\n').Split('\n');
        aprilLines.Should().HaveCount(1, "April file MUST NOT be re-touched after rotation");
        mayLines.Should().HaveCount(1);
        JsonDocument.Parse(aprilLines[0]).RootElement.GetProperty("ts").GetString().Should().Be("t-april");
        JsonDocument.Parse(mayLines[0]).RootElement.GetProperty("ts").GetString().Should().Be("t-may");
    }

    // ------------------------------------------------------------------
    // Test 5 — concurrency (Pitfall 2): 1000 records from 20 tasks all parse
    // ------------------------------------------------------------------
    [Fact]
    public async Task LogAsync_1000ConcurrentWrites_AllLinesParse()
    {
        using var scope = new TempDirScope();
        var logger = new AuditLogger(scope.Path);

        var tasks = Enumerable.Range(0, 20).Select(_ =>
            Task.Run(async () =>
            {
                for (var j = 0; j < 50; j++)
                {
                    await logger.LogAsync(new AuditRecord(
                        DateTime.UtcNow.ToString("O"),
                        "Connected",
                        Guid.NewGuid(),
                        "u",
                        "success"));
                }
            })).ToArray();
        await Task.WhenAll(tasks);
        logger.Dispose();

        var file = Path.Combine(scope.Path, $"audit-{DateTime.UtcNow:yyyy-MM}.jsonl");
        var lines = (await File.ReadAllTextAsync(file, Ct))
            .TrimEnd('\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1000);
        foreach (var line in lines)
        {
            var act = () => JsonDocument.Parse(line);
            act.Should().NotThrow("interleaved writes would produce torn JSON (Pitfall 2)");
        }
    }

    // ------------------------------------------------------------------
    // Test 6 — FileShare.ReadWrite (CONTEXT Q5): a reader can open the file
    // ------------------------------------------------------------------
    [Fact]
    public async Task LogAsync_AllowsConcurrentReader_ViaFileShareReadWrite()
    {
        using var scope = new TempDirScope();
        var logger = new AuditLogger(scope.Path);
        await logger.LogAsync(new AuditRecord("t", "Connected", null, "u", "success"), Ct);

        var file = Path.Combine(scope.Path, $"audit-{DateTime.UtcNow:yyyy-MM}.jsonl");

        // While the next LogAsync runs, a reader opening the file with
        // FileShare.ReadWrite (matching the writer's share mode) MUST succeed.
        var writeTask = logger.LogAsync(new AuditRecord("t2", "Disconnected", null, "u", "success"), Ct);
        var act = () =>
        {
            using var reader = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(reader);
            _ = sr.ReadToEnd();
        };
        act.Should().NotThrow("Q5 — support engineer can tail the file while writer is mid-append");
        await writeTask;
        logger.Dispose();
    }

    // ------------------------------------------------------------------
    // Test 7 — every AuditAction enum value round-trips
    // ------------------------------------------------------------------
    public static IEnumerable<object[]> AllAuditActions() =>
        Enum.GetValues<AuditAction>().Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(AllAuditActions))]
    public async Task LogAsync_RoundTripsAllAuditActionValues(AuditAction action)
    {
        using var scope = new TempDirScope();
        var logger = new AuditLogger(scope.Path);

        await logger.LogAsync(new AuditRecord(
            "t", action.ToString(), null, "u", "success"), Ct);
        logger.Dispose();

        var file = Path.Combine(scope.Path, $"audit-{DateTime.UtcNow:yyyy-MM}.jsonl");
        var line = (await File.ReadAllTextAsync(file, Ct)).TrimEnd('\n');
        var doc = JsonDocument.Parse(line);
        doc.RootElement.GetProperty("type").GetString().Should().Be(action.ToString());
    }

    // ------------------------------------------------------------------
    // Test 8 — IO failure → Serilog fallback (T-06-03 mitigation)
    // ------------------------------------------------------------------
    [Fact]
    public async Task LogAsync_OnIOFailure_FallsBackToSerilog_DoesNotThrow()
    {
        var sink = new InMemorySink();
        var oldLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            // Pass a path that resolves to an existing FILE rather than a directory.
            // The ctor's Directory.CreateDirectory will throw IOException (already a file
            // exists at that path); the AuditLogger ctor swallows that. Then the FileStream
            // open in LogAsync throws IOException because the parent path is a file, not
            // a directory — LogAsync's try/catch routes that to Serilog (T-06-03).
            var bogusParent = Path.Combine(
                Path.GetTempPath(),
                "deskbridge-tests-io-fail",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(bogusParent)!);
            await File.WriteAllTextAsync(bogusParent, "i am a file masquerading as a dir", Ct);

            var logger = new AuditLogger(bogusParent);
            var act = async () => await logger.LogAsync(
                new AuditRecord("t", "Connected", null, "u", "success"), Ct);
            await act.Should().NotThrowAsync(
                "AuditLogger MUST swallow IO failures and fall back to Serilog (T-06-03)");

            sink.Events.Should()
                .Contain(e => e.RenderMessage().Contains("Failed to append audit record"),
                    "Serilog fallback line MUST be emitted on IO failure");
            logger.Dispose();
        }
        finally
        {
            (Log.Logger as IDisposable)?.Dispose();
            Log.Logger = oldLogger;
        }
    }

    // ------------------------------------------------------------------
    // Test 9 — Dispose contract: subsequent LogAsync throws ObjectDisposedException
    // ------------------------------------------------------------------
    [Fact]
    public async Task LogAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var scope = new TempDirScope();
        var logger = new AuditLogger(scope.Path);
        logger.Dispose();

        var act = async () => await logger.LogAsync(
            new AuditRecord("t", "Connected", null, "u", "success"), Ct);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
