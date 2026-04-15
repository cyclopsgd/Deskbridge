using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Append-only audit logger for LOG-02 / LOG-03. Writes one JSON object per line
/// to <c>%AppData%/Deskbridge/audit-YYYY-MM.jsonl</c>. Serialised via SemaphoreSlim so
/// concurrent bus-dispatched calls never produce interleaved half-lines (Pitfall 2).
/// Opens with <c>FileShare.ReadWrite</c> so support engineers can tail the file while
/// Deskbridge is running (CONTEXT Q5).
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Append a single audit record as one JSON line to the current month's audit file.
    /// IO failures are caught internally and surfaced via <c>Serilog.Log.Error</c> so
    /// the caller never has to wrap a try/catch around audit emission.
    /// </summary>
    Task LogAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
