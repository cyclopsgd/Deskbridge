using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Xunit;

namespace Deskbridge.Tests.Fixtures;

/// <summary>
/// xUnit v3 collection definition that binds tests to the <see cref="StaCollectionFixture"/>.
/// RDP ActiveX tests require STA apartment affinity (RDP-ACTIVEX-PITFALLS §6).
///
/// <para>
/// <b>Why this collection exists</b>: xUnit v3 3.2.2 ships no <c>[STAFact]</c> attribute, no
/// <c>apartmentState</c> in <c>xunit.runner.json</c>, and no way to tell the runner to host a
/// worker on STA. Workers are MTA on Windows (verified against the shipped 3.2.2 extensibility
/// xml docs — no <c>Apartment</c>/<c>STA</c>/<c>MTA</c> references anywhere). Changing the
/// current thread's apartment state at runtime is not allowed after the first COM call, so
/// that path was correctly ruled out by the plan's original note.
/// </para>
/// <para>
/// <b>How STA is enforced</b>: tests in this collection wrap their body in
/// <see cref="StaRunner.Run(Action)"/> (sync) or <see cref="StaRunner.RunAsync(System.Func{Task})"/>
/// (async). Each call spawns a fresh thread with <see cref="ApartmentState.STA"/>, starts a
/// <see cref="Dispatcher"/> message pump on it, executes the test body on that pump, then
/// shuts the pump down. The pump is what <see cref="System.Windows.Forms.Integration.WindowsFormsHost"/>
/// and WPF <see cref="System.Windows.Window"/> instances require — a bare <c>Thread.Start</c>
/// without <c>Dispatcher.Run()</c> deadlocks on any WPF visual-tree operation, which is the
/// pitfall the original fixture comments warned about.
/// </para>
/// </summary>
[CollectionDefinition("RDP-STA")]
public class RdpStaCollection : ICollectionFixture<StaCollectionFixture>
{
    // Marker type — xUnit v3 discovers the collection via its name.
}

/// <summary>
/// Fixture shared across all tests in the <c>RDP-STA</c> collection. Kept as a container for
/// collection-scoped state (none currently needed); the real STA enforcement happens in
/// <see cref="StaRunner"/>.
/// </summary>
public sealed class StaCollectionFixture
{
    public StaCollectionFixture()
    {
        // Intentionally empty. STA enforcement is per-test-body via StaRunner.Run / RunAsync.
    }
}

/// <summary>
/// Runs a test body on a freshly created <see cref="ApartmentState.STA"/> thread with a
/// pumped <see cref="Dispatcher"/>. Required by RDP ActiveX tests (RDP-ACTIVEX-PITFALLS §6)
/// because xUnit v3's worker threads default to MTA.
/// </summary>
/// <remarks>
/// Exceptions raised by the test body (including <c>SkipException</c> from
/// <see cref="Assert.Skip(string)"/>) propagate back to the caller so xUnit v3 records the
/// correct Pass/Fail/Skip outcome. The pump is shut down in a <c>finally</c> block to avoid
/// leaking STA threads across tests.
/// </remarks>
public static class StaRunner
{
    /// <summary>Runs the specified synchronous <paramref name="body"/> on a fresh STA thread with a pumped Dispatcher.</summary>
    public static void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        Exception? captured = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            // Schedule the test body on the pump, then run the pump. The body's completion
            // (or exception) requests pump shutdown so the thread can exit.
            dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            }));
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Deskbridge.Tests STA Runner",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            // Preserve stack trace so the framework sees the original failure/skip.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }

    /// <summary>Runs the specified asynchronous <paramref name="body"/> on a fresh STA thread with a pumped Dispatcher.</summary>
    public static void RunAsync(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        Exception? captured = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            // Install the WPF DispatcherSynchronizationContext so `await` continuations resume
            // on this STA thread (matches the real app's behavior — RDP-ACTIVEX-PITFALLS §6).
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
            {
                try
                {
                    await body();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            }));
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Deskbridge.Tests STA Runner (async)",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }
}
