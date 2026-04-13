using Deskbridge.Core.Models;

namespace Deskbridge.Core.Services;

/// <summary>
/// Drives the Plan 04-03 reconnect loop per D-03 (backoff 2/4/8/16 then cap 30)
/// and D-05 (hard 20-attempt cap). Lives in Core (protocol-agnostic per D-10);
/// the injected delay delegate defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// so tests can swap in an instant no-op and real callers get STA-safe scheduling
/// when invoked from the UI dispatcher (D-11, RDP-ACTIVEX-PITFALLS §6).
///
/// <para><b>Not a WPF DispatcherTimer.</b> The pitfalls doc forbids
/// <c>System.Threading.Timer</c>/<c>System.Timers.Timer</c> because their callbacks
/// run on MTA pool threads and cannot touch the RDP COM object. <see cref="Task.Delay"/>
/// with plain <c>await</c> (no <c>ConfigureAwait(false)</c>) respects the WPF
/// <c>DispatcherSynchronizationContext</c> and resumes on the STA thread — the same
/// behaviour <c>DispatcherTimer</c> would provide, without a PresentationCore
/// dependency in Core. See 04-03-PLAN.md Task 1.2 rationale.</para>
///
/// <para><b>Security:</b> this service never logs the <see cref="ConnectionModel"/>
/// itself, only the hostname (no credential material flows through the reconnect
/// surface — <c>ResolvedPassword</c> is re-resolved per attempt by the pipeline).</para>
/// </summary>
public sealed class RdpReconnectCoordinator
{
    /// <summary>
    /// D-05 hard cap. After 20 attempts (~10 minutes once the 30-second cap kicks in)
    /// the loop returns false and the caller switches to the manual overlay.
    /// </summary>
    public const int MaxAttempts = 20;

    /// <summary>
    /// D-03 backoff schedule head: attempts 1-4 delay 2s, 4s, 8s, 16s.
    /// Remaining attempts (5-20) fall back to <see cref="CapDelay"/>.
    /// </summary>
    public static readonly TimeSpan[] BackoffSchedule =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
    ];

    /// <summary>D-03 cap: attempts 5-20 each delay 30 seconds.</summary>
    public static readonly TimeSpan CapDelay = TimeSpan.FromSeconds(30);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="delay">
    /// Optional delay injector. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// for production; tests inject a capture lambda for instant execution. The delegate
    /// MUST observe <paramref name="delay"/>'s CancellationToken and throw
    /// <see cref="OperationCanceledException"/> so the loop short-circuits cleanly.
    /// </param>
    public RdpReconnectCoordinator(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _delay = delay ?? Task.Delay;
    }

    /// <summary>
    /// Runs the reconnect loop. Returns <c>true</c> on the first successful attempt,
    /// <c>false</c> when the 20-attempt cap is hit (caller shows manual overlay) or
    /// when the cancellation token fires during a delay (caller closes overlay).
    /// </summary>
    /// <param name="connection">The connection to reconnect. Passed to each
    /// <paramref name="reconnect"/> invocation so the caller can drive the pipeline.</param>
    /// <param name="reconnect">Returns <c>true</c> if the attempt succeeded. Typically
    /// wraps <c>IConnectionPipeline.ConnectAsync</c>.</param>
    /// <param name="notifyAttempt">Invoked BEFORE each delay with the 1-based attempt
    /// number and the delay about to elapse. The overlay ViewModel consumes this to
    /// update "Reconnecting... attempt N" text.</param>
    /// <param name="ct">Cancellation token wired to the overlay's Cancel button.</param>
    public async Task<bool> RunAsync(
        ConnectionModel connection,
        Func<ConnectionModel, Task<bool>> reconnect,
        Func<int, TimeSpan, Task> notifyAttempt,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(reconnect);
        ArgumentNullException.ThrowIfNull(notifyAttempt);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var delay = attempt - 1 < BackoffSchedule.Length
                ? BackoffSchedule[attempt - 1]
                : CapDelay;

            await notifyAttempt(attempt, delay);

            try
            {
                await _delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            if (ct.IsCancellationRequested) return false;

            if (await reconnect(connection)) return true;

            if (ct.IsCancellationRequested) return false;
        }

        return false;
    }
}
