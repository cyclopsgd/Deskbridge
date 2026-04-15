using System.IO;
using System.Text;
using System.Text.Json;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Serilog;

namespace Deskbridge.Core.Services;

/// <summary>
/// LOG-02 / LOG-03 / D-10 / D-13 implementation. SemaphoreSlim-serialised append-only
/// monthly-rotating jsonl writer. Opens with <see cref="FileShare.ReadWrite"/> per
/// CONTEXT Q5 (support engineer can tail the file in Notepad++/VS Code while the app
/// is running). IO failures are swallowed and re-emitted via Serilog so a disk-full
/// or permission-denied condition never crashes the caller.
/// </summary>
public sealed class AuditLogger : IAuditLogger, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;
    private bool _disposed;

    /// <summary>
    /// Test-only seam for the monthly-rotation tests. Production code never overrides this.
    /// </summary>
    internal Func<DateTime> UtcNowProvider { get; set; } = () => DateTime.UtcNow;

    public AuditLogger()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Deskbridge"))
    {
    }

    /// <summary>
    /// Test constructor — accepts an arbitrary directory so tests can isolate to a temp path.
    /// </summary>
    internal AuditLogger(string directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex)
        {
            // Don't throw from the ctor — the first LogAsync will fail-soft via Serilog.
            // Tests for "IO failure → Serilog fallback" exercise this path.
            Log.Error(ex, "AuditLogger could not pre-create directory {Directory}", _directory);
        }
    }

    public async Task LogAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(record);

        var now = UtcNowProvider();
        var fileName = $"audit-{now:yyyy-MM}.jsonl";
        var path = Path.Combine(_directory, fileName);
        var json = JsonSerializer.Serialize(record, AuditJsonContext.Default.AuditRecord);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Q5: FileShare.ReadWrite permits a concurrent reader (tail -f, Notepad++,
            // VS Code) without the writer erroring out. FileMode.Append positions the
            // stream at EOF on each open, so the per-call open/close pattern is correct
            // for an append-only file.
            await using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(json).ConfigureAwait(false);
            await writer.WriteAsync('\n').ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Serilog fallback so we never silently lose the fact of the audit attempt,
            // even on disk-full / read-only / permission-denied. We intentionally do
            // NOT embed record fields beyond the Type (an enum name, never sensitive).
            Log.Error(ex, "Failed to append audit record {Type}", record.Type);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}
