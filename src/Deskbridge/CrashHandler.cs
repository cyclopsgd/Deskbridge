using System.Windows.Threading;
using Deskbridge.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Wpf.Ui;

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
    /// Plan 06-04 (LOG-04, D-11) real CrashDialog UI. Marshals to the WPF UI
    /// dispatcher (the dispatcher hook fires on the UI thread, but AppDomain /
    /// TaskScheduler hooks can fire from anywhere), resolves
    /// <see cref="IContentDialogService"/> from the app's DI container, and
    /// shows a <see cref="CrashDialog"/>. Returns <c>true</c> when the dialog
    /// opened so the dispatcher hook can set <c>e.Handled = true</c> and the
    /// app survives; <c>false</c> on any failure in the marshal / dialog path
    /// so the caller can fall back to logging + exit.
    /// </summary>
    /// <remarks>
    /// A11: Velopack restart is NOT used — <see cref="CrashDialog"/> uses a plain
    /// <c>Process.Start(MainModule.FileName)</c>, sufficient for the v1 flow.
    /// </remarks>
    private static bool TryShowCrashDialog(Exception ex)
    {
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                // Application hasn't been constructed yet (crash during Main).
                // Logging already happened; nothing more we can do.
                return false;
            }

            var shown = false;
            dispatcher.Invoke(() =>
            {
                try
                {
                    if (System.Windows.Application.Current is not App app)
                    {
                        Log.Warning("CrashHandler: Application.Current is not the Deskbridge.App instance");
                        return;
                    }

                    var dialogService = app.Services?.GetService<IContentDialogService>();
                    if (dialogService is null)
                    {
                        Log.Warning("CrashHandler: IContentDialogService not available — dialog not shown");
                        return;
                    }

                    var dialog = new CrashDialog(ex, dialogService);
                    // Fire-and-forget ShowAsync — the Task completes when the user clicks
                    // Restart (ContentDialogButton.Close). Copy Details cancels the close so
                    // the user can paste + then click Restart. We do NOT await because the
                    // dispatcher hook must return before the dialog can render.
                    _ = dialog.ShowAsync();
                    shown = true;
                }
                catch (Exception inner)
                {
                    Log.Error(inner, "CrashDialog itself failed to show");
                }
            });
            return shown;
        }
        catch (Exception outer)
        {
            Log.Error(outer, "Dispatcher.Invoke failed in TryShowCrashDialog");
            return false;
        }
    }
}
