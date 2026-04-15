using System.Windows.Threading;
using Serilog;

namespace Deskbridge;

/// <summary>
/// LOG-04 / D-11 / Pattern 4: global exception handler registered BEFORE any WPF code
/// can throw. Three hooks cover the three exception pathways in a WPF app:
/// <list type="number">
///   <item><c>AppDomain.CurrentDomain.UnhandledException</c> — non-UI-thread + terminating.</item>
///   <item><c>Application.DispatcherUnhandledException</c> — UI-thread (survivable via <c>e.Handled = true</c>).</item>
///   <item><c>TaskScheduler.UnobservedTaskException</c> — fire-and-forget Tasks (survivable via <c>e.SetObserved()</c>).</item>
/// </list>
/// </summary>
/// <remarks>
/// Plan 06-04 extends <see cref="TryShowCrashDialog"/> with the actual ContentDialog UI
/// per UI-SPEC §Crash Dialog. This plan lands the hooks + logging only;
/// <see cref="TryShowCrashDialog"/> is a scaffolding stub that logs and returns true so
/// the dispatcher hook still marks the exception as handled and the app survives.
/// </remarks>
public static class CrashHandler
{
    /// <summary>
    /// Test-introspection state. Lets <c>CrashHandlerTests</c> assert which hooks are
    /// installed without resorting to reflection on the AppDomain / TaskScheduler
    /// internal event invocation lists. Production code should not read these.
    /// </summary>
    internal static class HookState
    {
        public static bool AppDomainInstalled;
        public static bool UnobservedTaskInstalled;
        public static bool DispatcherInstalled;

        public static void Reset()
        {
            AppDomainInstalled = false;
            UnobservedTaskInstalled = false;
            DispatcherInstalled = false;
        }
    }

    /// <summary>
    /// Installs the AppDomain + UnobservedTask hooks. Must be called from
    /// <c>Program.Main</c> immediately after <c>VelopackApp.Build().Run()</c> and
    /// BEFORE constructing the WPF <c>App</c>, so a crash in the App ctor or in
    /// <c>InitializeComponent</c> still hits the logger.
    /// </summary>
    /// <remarks>
    /// The Dispatcher hook cannot be installed here because <c>Application.Current</c>
    /// is null until <c>App</c> is constructed. <see cref="InstallDispatcherHook"/>
    /// handles that step from <c>App.OnStartup</c>.
    ///
    /// Idempotent: safe to call multiple times. Subsequent calls are no-ops.
    /// </remarks>
    public static void Install()
    {
        if (!HookState.AppDomainInstalled)
        {
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
            HookState.AppDomainInstalled = true;
        }
        if (!HookState.UnobservedTaskInstalled)
        {
            TaskScheduler.UnobservedTaskException += OnUnobservedTask;
            HookState.UnobservedTaskInstalled = true;
        }
    }

    /// <summary>
    /// Installs the Dispatcher hook on the supplied <see cref="System.Windows.Application"/>.
    /// MUST be called from <c>App.OnStartup</c> after <c>base.OnStartup(e)</c> (before that
    /// point <c>Application.Current</c> may not be valid).
    /// </summary>
    public static void InstallDispatcherHook(System.Windows.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (HookState.DispatcherInstalled) return;
        application.DispatcherUnhandledException += OnDispatcherUnhandled;
        HookState.DispatcherInstalled = true;
    }

    // ------------------------------------------------------------------
    // Internal handlers — internal so CrashHandlerTests can invoke directly.
    // ------------------------------------------------------------------

    internal static void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex,
                "AppDomainUnhandledException Terminating={Terminating}",
                e.IsTerminating);
        }
        else
        {
            Log.Fatal(
                "AppDomainUnhandledException with non-Exception payload {Payload} Terminating={Terminating}",
                e.ExceptionObject,
                e.IsTerminating);
        }
        // Cannot prevent termination here — when IsTerminating=true the OS will end the
        // process regardless. Logging is the only useful action.
    }

    internal static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "UnobservedTaskException");
        // Survive — a fire-and-forget Task should never crash the whole process.
        e.SetObserved();
    }

    internal static void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "DispatcherUnhandledException");
        if (TryShowCrashDialog(e.Exception))
        {
            // App survives; user can still copy details / restart in Plan 06-04's UI.
            e.Handled = true;
        }
    }

    /// <summary>
    /// STUB — Plan 06-04 replaces this with the actual <c>ui:ContentDialog</c> per
    /// UI-SPEC §Crash Dialog (Copy Details / Restart buttons, no stack trace visible).
    /// For now we log-only and return <c>true</c> so the dispatcher hook still sets
    /// <c>e.Handled = true</c> and the app survives.
    /// </summary>
    /// <remarks>
    /// A11: Velopack restart is NOT used — Plan 06-04 will use a plain
    /// <c>Process.Start(MainModule.FileName)</c>, which is sufficient for the v1 flow.
    /// </remarks>
    private static bool TryShowCrashDialog(Exception ex)
    {
        // TODO Plan 06-04: marshal to UI thread via Application.Current.Dispatcher,
        // show ui:ContentDialog with Copy Details / Restart buttons per UI-SPEC.
        // Reference the exception so the parameter is not "unused" — the production
        // implementation will pass it to the dialog's "Copy Details" payload.
        _ = ex;
        Log.Information("CrashHandler.TryShowCrashDialog stub — Plan 06-04 wires the UI.");
        return true;
    }
}
