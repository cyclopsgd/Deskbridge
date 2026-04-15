using System.IO;

namespace Deskbridge.Tests.Logging;

/// <summary>
/// Per-test temporary directory under <c>%TEMP%/deskbridge-tests/&lt;guid&gt;</c>.
/// Cleaned up on dispose. Used by <see cref="AuditLoggerTests"/>,
/// <see cref="SerilogConfigTests"/>, and any other Phase 6 logging test that needs
/// real disk IO without polluting <c>%AppData%/Deskbridge</c>.
/// </summary>
internal sealed class TempDirScope : IDisposable
{
    public string Path { get; }

    public TempDirScope()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "deskbridge-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; leftovers under %TEMP%/deskbridge-tests are fine.
        }
    }
}
