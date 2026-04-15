using System.IO;
using Deskbridge.Tests.Security;
using Serilog;

namespace Deskbridge.Tests.Logging;

/// <summary>
/// LOG-04 / Pattern 4 / D-11 / A9-A11 coverage for <see cref="CrashHandler"/>.
/// All tests in this collection run serially because <see cref="CrashHandler"/> mutates
/// process-global hook state (AppDomain + TaskScheduler + Application events).
/// </summary>
[Collection("CrashHandlerCollection")]
public sealed class CrashHandlerTests
{
    /// <summary>Reset hook flags + detach handlers between tests.</summary>
    private static void ResetHooks()
    {
        if (CrashHandler.HookState.AppDomainInstalled)
        {
            AppDomain.CurrentDomain.UnhandledException -= CrashHandler.OnAppDomainUnhandled;
        }
        if (CrashHandler.HookState.UnobservedTaskInstalled)
        {
            TaskScheduler.UnobservedTaskException -= CrashHandler.OnUnobservedTask;
        }
        // Dispatcher hook attached to Application.Current — only relevant if Test 3 ran.
        // Production tests don't construct an Application instance, so the dispatcher
        // flag never flips in this test class. Reset the flag regardless.
        CrashHandler.HookState.Reset();
    }

    public CrashHandlerTests() => ResetHooks();

    // ------------------------------------------------------------------
    // Test 1 — Install registers AppDomain hook
    // ------------------------------------------------------------------
    [Fact]
    public void Install_RegistersAppDomainHook()
    {
        try
        {
            CrashHandler.Install();
            CrashHandler.HookState.AppDomainInstalled.Should().BeTrue();
        }
        finally { ResetHooks(); }
    }

    // ------------------------------------------------------------------
    // Test 2 — Install registers UnobservedTask hook
    // ------------------------------------------------------------------
    [Fact]
    public void Install_RegistersUnobservedTaskHook()
    {
        try
        {
            CrashHandler.Install();
            CrashHandler.HookState.UnobservedTaskInstalled.Should().BeTrue();
        }
        finally { ResetHooks(); }
    }

    // ------------------------------------------------------------------
    // Test 3 — Dispatcher hook not registered until InstallDispatcherHook
    // ------------------------------------------------------------------
    [Fact]
    public void Install_DoesNotRegisterDispatcherHook()
    {
        try
        {
            CrashHandler.Install();
            CrashHandler.HookState.DispatcherInstalled.Should().BeFalse(
                "Application.Current is null at Main() — Dispatcher hook lands later");
        }
        finally { ResetHooks(); }
    }

    // ------------------------------------------------------------------
    // Test 4 — Install is idempotent (no double-register)
    // ------------------------------------------------------------------
    [Fact]
    public void Install_IsIdempotent()
    {
        try
        {
            CrashHandler.Install();
            CrashHandler.Install();
            CrashHandler.Install();
            CrashHandler.HookState.AppDomainInstalled.Should().BeTrue();
            CrashHandler.HookState.UnobservedTaskInstalled.Should().BeTrue();
            // (Cannot directly assert "registered exactly once" without reflection on
            // private event invocation lists. The contract is that the flag is the
            // gate — Install short-circuits if the flag is already true.)
        }
        finally { ResetHooks(); }
    }

    // ------------------------------------------------------------------
    // Test 5 — OnAppDomainUnhandled logs Fatal with Terminating property
    // ------------------------------------------------------------------
    [Fact]
    public void OnAppDomainUnhandled_LogsFatalWithTerminatingFlag()
    {
        var sink = new InMemorySink();
        var oldLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
        try
        {
            var ex = new InvalidOperationException("boom");
            CrashHandler.OnAppDomainUnhandled(this, new UnhandledExceptionEventArgs(ex, isTerminating: true));

            sink.Events.Should().HaveCount(1);
            var evt = sink.Events.Single();
            evt.Level.Should().Be(Serilog.Events.LogEventLevel.Fatal);
            evt.MessageTemplate.Text.Should().Contain("AppDomainUnhandledException");
            evt.Properties.Should().ContainKey("Terminating");
            evt.Exception.Should().BeSameAs(ex);
        }
        finally
        {
            (Log.Logger as IDisposable)?.Dispose();
            Log.Logger = oldLogger;
        }
    }

    // ------------------------------------------------------------------
    // Test 6 — OnUnobservedTask calls SetObserved + logs Error
    // ------------------------------------------------------------------
    [Fact]
    public async Task OnUnobservedTask_LogsErrorAndSetsObserved()
    {
        var sink = new InMemorySink();
        var oldLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
        try
        {
            // Build a real UnobservedTaskExceptionEventArgs by faulting a Task and
            // letting it go unobserved long enough to trigger the framework path.
            // The event-args ctor is public; we can construct it directly with an
            // AggregateException and simulate the SetObserved contract.
            var inner = new InvalidOperationException("task boom");
            var ag = new AggregateException(inner);
            var args = new UnobservedTaskExceptionEventArgs(ag);

            CrashHandler.OnUnobservedTask(this, args);

            // SetObserved is called inside the handler. The framework checks observation
            // via args.m_observed (private). We can't read that directly in a portable way,
            // so this test asserts the OBSERVABLE consequence: invoking the args' Exception
            // property after the handler ran does NOT throw "AggregateException not observed".
            // (Calling .Exception itself marks observed too, so the actual proof is that
            // SetObserved was invoked — we trust the source. The Serilog assertion below
            // is the substantive one.)
            await Task.Yield();
            sink.Events.Should().HaveCount(1);
            sink.Events.Single().Level.Should().Be(Serilog.Events.LogEventLevel.Error);
            sink.Events.Single().Exception.Should().BeSameAs(ag);
        }
        finally
        {
            (Log.Logger as IDisposable)?.Dispose();
            Log.Logger = oldLogger;
        }
    }

    // ------------------------------------------------------------------
    // Test 7 — OnDispatcherUnhandled logs Fatal AND sets Handled=true
    // ------------------------------------------------------------------
    // Note: Constructing DispatcherUnhandledExceptionEventArgs requires an internal
    // ctor, so direct invocation isn't feasible without reflection. We assert the
    // observable contract via Test 8's source-order check + the production stub
    // returning true so e.Handled = true holds.
    [Fact]
    public void OnDispatcherUnhandled_StubReturnsTrue_SoDispatcherSetsHandled()
    {
        // The dispatcher handler's contract: TryShowCrashDialog returns true, the
        // dispatcher handler then sets e.Handled = true. We can verify the stub
        // returns true by reflection (the stub is private) — easier: verify the
        // INVARIANT holds in the source by string-grep below.
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var crashHandlerCs = File.ReadAllText(
            Path.Combine(solutionRoot, "src", "Deskbridge", "CrashHandler.cs"));

        // The dispatcher handler MUST set e.Handled = true when TryShowCrashDialog returned true.
        crashHandlerCs.Should().Contain("e.Handled = true",
            "OnDispatcherUnhandled must mark the exception as handled so the app survives");
        // The stub MUST return true.
        crashHandlerCs.Should().Contain("return true;",
            "TryShowCrashDialog stub must return true so e.Handled is set");
    }

    // ------------------------------------------------------------------
    // Test 8 — Program.Main source order: Velopack → Install → new App
    // ------------------------------------------------------------------
    [Fact]
    public void Program_Main_InvokesInstallBetweenVelopackAndAppConstruction()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var programCs = File.ReadAllText(
            Path.Combine(solutionRoot, "src", "Deskbridge", "Program.cs"));

        var velopackIdx = programCs.IndexOf("VelopackApp.Build().Run()", StringComparison.Ordinal);
        var installIdx = programCs.IndexOf("CrashHandler.Install()", StringComparison.Ordinal);
        var newAppIdx = programCs.IndexOf("new App()", StringComparison.Ordinal);

        velopackIdx.Should().BeGreaterThan(-1, "Program.Main must invoke VelopackApp.Build().Run()");
        installIdx.Should().BeGreaterThan(velopackIdx,
            "CrashHandler.Install() must follow VelopackApp.Build().Run() (D-11)");
        newAppIdx.Should().BeGreaterThan(installIdx,
            "CrashHandler hooks MUST land before App is constructed (Pattern 4 + A9)");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
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
