using System.IO;
using Serilog;

namespace Deskbridge.Core.Logging;

/// <summary>
/// LOG-01 + LOG-05 baseline Serilog configuration. Extracted from <c>App.xaml.cs</c>
/// so unit tests can drive the same configuration end-to-end (write a log line, read
/// the file back, assert no credential leaked) without instantiating a WPF Application.
/// </summary>
/// <remarks>
/// Replaces the Phase 4 baseline which had neither the 10MB size cap nor the
/// <see cref="RedactSensitivePolicy"/> destructuring policy.
/// </remarks>
public static class SerilogSetup
{
    /// <summary>
    /// Returns a <see cref="LoggerConfiguration"/> wired with:
    /// <list type="bullet">
    ///   <item>Information minimum level + <c>Enrich.FromLogContext()</c></item>
    ///   <item><see cref="RedactSensitivePolicy"/> destructuring policy (LOG-05)</item>
    ///   <item>Rolling file at <paramref name="logDirectory"/>/<c>deskbridge-.log</c>:
    ///         <c>RollingInterval.Day</c>, 10 MB cap, <c>rollOnFileSizeLimit</c>,
    ///         5 retained files, <c>shared=false</c>, 1s flush interval (LOG-01)</item>
    /// </list>
    /// Caller is responsible for invoking <see cref="LoggerConfiguration.CreateLogger"/>
    /// and assigning to <see cref="Log.Logger"/>.
    /// </summary>
    public static LoggerConfiguration Configure(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Destructure.With<RedactSensitivePolicy>()           // LOG-05 redaction
            .WriteTo.File(
                path: Path.Combine(logDirectory, "deskbridge-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10_000_000,                    // LOG-01: 10 MB cap
                rollOnFileSizeLimit: true,                         // LOG-01: roll on size too
                retainedFileCountLimit: 5,                         // LOG-01: keep 5 files
                shared: false,                                     // single writer per process
                flushToDiskInterval: TimeSpan.FromSeconds(1));
    }
}
