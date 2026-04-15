using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Settings;

namespace Deskbridge.Services;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-03, Pattern 8, Pitfall 6) idle-timer auto-lock.
///
/// <para>Architecture:
/// <list type="number">
/// <item>A <see cref="DispatcherTimer"/> configured with
/// <see cref="SecuritySettingsRecord.AutoLockTimeoutMinutes"/> as interval.</item>
/// <item>A <see cref="PreProcessInputEventHandler"/> registered on
/// <see cref="InputManager"/>.<see cref="InputManager.Current"/> — stored in a
/// strong-reference field (Pitfall 1 principle applied here too, defense-in-depth).</item>
/// <item>Every input event that does NOT originate inside a
/// <see cref="WindowsFormsHost"/> restarts the timer (Stop + Start).</item>
/// <item>On Tick the timer stops itself and publishes
/// <see cref="AppLockedEvent"/>(<see cref="LockReason.Timeout"/>).
/// <see cref="AppLockController"/> subscribes and orchestrates the lock.</item>
/// </list>
/// </para>
///
/// <para><b>Pitfall 6</b> (RDP-ACTIVEX-PITFALLS): the RDP AxHost's
/// <c>KeyboardHookMode=0</c> setting lets WPF see keystrokes BEFORE they reach
/// the remote session. Without the <see cref="FindAncestor{T}"/> filter, typing
/// into an RDP tab would reset the Deskbridge idle timer — violating D-14
/// ("Deskbridge activity only resets the timer"). The filter walks up the
/// visual/logical tree from the input source and bails out when it finds a
/// <see cref="WindowsFormsHost"/> ancestor.</para>
///
/// <para><b>Pitfall 4</b> (DispatcherTimer + sleep): the timer does NOT
/// compensate for wake-from-sleep time. Mitigated implicitly by
/// <see cref="SessionLockService"/> which catches <c>SessionLock</c> on OS lock
/// (which Windows raises on sleep on corporate machines).</para>
/// </summary>
public sealed class IdleLockService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly IEventBus _bus;

    // Strong-ref field so the delegate is unambiguously rooted by THIS instance
    // (same principle as Pattern 9). InputManager is a singleton — if the service
    // is disposed without Dispose(), the handler outlives us. The field makes
    // both the subscribe AND the unsubscribe path trivially symmetric.
    private readonly PreProcessInputEventHandler _handler;

    private bool _disposed;

    public IdleLockService(IEventBus bus, SecuritySettingsRecord security)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(security);

        _bus = bus;

        // DispatcherTimer needs a live Dispatcher. In production this service is
        // eagerly resolved from App.OnStartup which runs on the UI dispatcher.
        // In tests, Application.Current may be null — fall back to the current
        // thread's dispatcher.
        var dispatcher = System.Windows.Application.Current?.Dispatcher
                         ?? Dispatcher.CurrentDispatcher;

        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMinutes(Math.Max(1, security.AutoLockTimeoutMinutes)),
        };
        _timer.Tick += OnTick;

        _handler = (_, e) => HandleInput(e);
        InputManager.Current.PreProcessInput += _handler;
        _timer.Start();
    }

    /// <summary>
    /// Test seam: override the interval to sub-minute values so the
    /// IdleLockServiceTests can exercise the Tick path without blocking for 60+ seconds.
    /// Production code MUST NOT call this — the ctor already applies the correct
    /// interval from <see cref="SecuritySettingsRecord"/>.
    /// </summary>
    internal void SetIntervalForTesting(TimeSpan interval)
    {
        _timer.Stop();
        _timer.Interval = interval;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _bus.Publish(new AppLockedEvent(LockReason.Timeout));
    }

    /// <summary>
    /// Production handler. Unwraps the source from <paramref name="e"/> then
    /// delegates to <see cref="HandleInputFromSource"/>.
    /// </summary>
    private void HandleInput(PreProcessInputEventArgs e)
    {
        var src = e.StagingItem?.Input?.Source as DependencyObject;
        HandleInputFromSource(src);
    }

    /// <summary>
    /// Test seam + Pitfall 6 filter. <see cref="PreProcessInputEventArgs"/>
    /// has internal-only ctors so constructing one under test requires reflection
    /// into WPF internals. Instead we split the handler: tests drive this method
    /// with a fake <see cref="DependencyObject"/> source (e.g. an
    /// <see cref="System.Windows.Controls.Button"/> for the non-WFH case, or a
    /// child of a <see cref="WindowsFormsHost"/> for the WFH case) and assert
    /// the return value.
    ///
    /// Returns <c>true</c> if the input would have reset the timer.
    /// </summary>
    internal bool HandleInputFromSource(DependencyObject? source)
    {
        if (_disposed) return false;

        if (source is not null && FindAncestor<WindowsFormsHost>(source) is not null)
        {
            // Pitfall 6: input came from inside the RDP AxHost — do NOT reset.
            // D-14 intent: "Deskbridge activity only resets the timer".
            return false;
        }

        _timer.Stop();
        _timer.Start();
        return true;
    }

    /// <summary>Exposes the timer's running state to tests without letting them poke Tick intervals.</summary>
    internal bool IsTimerRunning => _timer.IsEnabled;

    /// <summary>
    /// Walks up the visual tree first (for visual-tree descendants like the
    /// AxHost's rendered Win32 content) then the logical tree (for cases where
    /// the focused element is attached logically but not visually). Returns
    /// <c>null</c> if no ancestor of <typeparamref name="T"/> is found.
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        // Cap the walk at 64 levels — defense against accidental cycles or
        // pathological trees. 64 is well above any realistic WPF visual depth.
        for (int i = 0; i < 64 && d is not null; i++)
        {
            if (d is T t) return t;
            var parent = VisualTreeHelper.GetParent(d);
            if (parent is null)
            {
                parent = LogicalTreeHelper.GetParent(d);
            }
            d = parent!;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InputManager.Current.PreProcessInput -= _handler;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
