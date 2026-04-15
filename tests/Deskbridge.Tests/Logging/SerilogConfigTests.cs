using System.IO;
using Deskbridge.Core.Logging;

namespace Deskbridge.Tests.Logging;

/// <summary>
/// LOG-01 + LOG-05 end-to-end coverage for <see cref="SerilogSetup.Configure"/>.
/// The redaction-via-file test is the canonical canary — drives a full Serilog
/// pipeline including the <see cref="RedactSensitivePolicy"/> and confirms the
/// rendered log file does not contain any credential value.
/// </summary>
public sealed class SerilogConfigTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------
    // Test 1 — full-run end-to-end: log → file → assert no credential leak
    // ------------------------------------------------------------------
    [Fact]
    public async Task Configure_AppliesRedactSensitivePolicy_EndToEnd()
    {
        using var scope = new TempDirScope();
        var logger = SerilogSetup.Configure(scope.Path).CreateLogger();

        // Use a known credential value so we can grep the file for its absence.
        var poco = new { Username = "admin", Password = "hunter2" };
        logger.Information("Login attempt {@Cred}", poco);

        // Force flush + release the file handle. Serilog.Sinks.File has a 1s
        // flushToDiskInterval; calling Dispose drains the buffer synchronously.
        (logger as IDisposable).Dispose();

        var files = Directory.GetFiles(scope.Path, "deskbridge-*.log");
        files.Should().HaveCount(1, "RollingInterval.Day produces exactly one file per UTC date");

        var text = await File.ReadAllTextAsync(files[0], Ct);
        text.Should().NotContain("hunter2",
            "LOG-05 hard rule: no denylisted property value in any log file");
        text.Should().Contain("***REDACTED***",
            "RedactSensitivePolicy must rewrite the Password property to the sentinel");
    }

    // ------------------------------------------------------------------
    // Test 2 — Configure creates the directory if missing
    // ------------------------------------------------------------------
    [Fact]
    public void Configure_CreatesDirectoryIfMissing()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "deskbridge-serilog-cfg-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.Exists(dir).Should().BeFalse("precondition: directory must not exist");
            (SerilogSetup.Configure(dir).CreateLogger() as IDisposable).Dispose();
            Directory.Exists(dir).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Test 3 — App.OnStartup source order: dispatcher hook BEFORE mainWindow.Show()
    // ------------------------------------------------------------------
    [Fact]
    public void App_OnStartup_InstallsDispatcherHookBeforeShowingMainWindow()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var appCs = File.ReadAllText(
            Path.Combine(solutionRoot, "src", "Deskbridge", "App.xaml.cs"));

        var onStartupIdx = appCs.IndexOf(
            "protected override void OnStartup", StringComparison.Ordinal);
        var attachIdx = appCs.IndexOf(
            "CrashHandler.InstallDispatcherHook", onStartupIdx, StringComparison.Ordinal);
        var showIdx = appCs.IndexOf(
            "mainWindow.Show()", attachIdx, StringComparison.Ordinal);

        onStartupIdx.Should().BeGreaterThan(-1);
        attachIdx.Should().BeGreaterThan(onStartupIdx,
            "InstallDispatcherHook must be inside OnStartup, after base.OnStartup(e)");
        showIdx.Should().BeGreaterThan(attachIdx,
            "Pattern 4: dispatcher hook MUST land before mainWindow.Show() so any " +
            "exception during the first frame is captured");
    }

    // ------------------------------------------------------------------
    // Test 4 — App.OnStartup uses SerilogSetup.Configure (no ad-hoc baseline)
    // ------------------------------------------------------------------
    [Fact]
    public void App_OnStartup_ReplacesBaselineWith_SerilogSetupConfigure()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var appCs = File.ReadAllText(
            Path.Combine(solutionRoot, "src", "Deskbridge", "App.xaml.cs"));

        appCs.Should().Contain("SerilogSetup.Configure(",
            "App.OnStartup must wire LOG-01 via the testable SerilogSetup helper");
        appCs.Should().NotContain(
            ".WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 5)",
            "Phase 4 baseline must be fully replaced by the LOG-01 config");
    }

    // ------------------------------------------------------------------
    // Test 5 — SerilogSetup.Configure includes all LOG-01 parameters
    // ------------------------------------------------------------------
    [Fact]
    public void SerilogSetup_Source_IncludesAllLog01Parameters()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var setupCs = File.ReadAllText(
            Path.Combine(solutionRoot, "src", "Deskbridge.Core", "Logging", "SerilogSetup.cs"));

        setupCs.Should().Contain("fileSizeLimitBytes: 10_000_000", "LOG-01: 10 MB cap");
        setupCs.Should().Contain("rollOnFileSizeLimit: true", "LOG-01: roll on size too");
        setupCs.Should().Contain("retainedFileCountLimit: 5", "LOG-01: keep 5 files");
        setupCs.Should().Contain("RedactSensitivePolicy", "LOG-05: redaction policy installed");
    }

    private static string FindSolutionRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Deskbridge.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ??
            throw new InvalidOperationException("Could not find Deskbridge.sln by walking up from " + start);
    }
}
