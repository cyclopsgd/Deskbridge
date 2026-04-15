using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Microsoft.Win32;

namespace Deskbridge.Services;

/// <summary>
/// Phase 6 Plan 06-04 (SEC-04, Pattern 9, Pitfall 1, Pitfall 7) Windows-session
/// lock handler. Subscribes to <see cref="SystemEvents.SessionSwitch"/> in the
/// ctor and publishes <see cref="AppLockedEvent"/>(<see cref="LockReason.SessionSwitch"/>)
/// when the Windows session is locked, the console is disconnected, or an RDP
/// session disconnects.
///
/// <para><b>Pitfall 1</b>: <see cref="SystemEvents.SessionSwitch"/> is a STATIC
/// event — the delegate is the only strong root to our handler closure. We keep
/// the <see cref="SessionSwitchEventHandler"/> in <see cref="_handler"/> so the
/// delegate is rooted by THIS instance (not by the static event's invocation
/// list, which would GC us).</para>
///
/// <para><b>Pitfall 7</b>: the event fires on a dedicated SystemEvents thread
/// (NOT the UI dispatcher). Subscribers that update WPF DependencyObjects would
/// throw cross-thread exceptions. We marshal via
/// <see cref="Dispatcher.BeginInvoke(Delegate, object[])"/> so <see cref="IEventBus"/>
/// dispatches on the UI thread.</para>
///
/// <para><b>Dispose unsubscribe</b> (mandatory per learn.microsoft.com): static
/// events retain their invocation list across garbage collections. Without
/// <see cref="Dispose"/>, old instances would keep receiving SessionSwitch
/// events and leak across app restarts.</para>
/// </summary>
public sealed class SessionLockService : IDisposable
{
    private readonly IEventBus _bus;
    private readonly Dispatcher _uiDispatcher;
    private readonly SessionSwitchEventHandler _handler;
    private bool _disposed;

    public SessionLockService(IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
        _uiDispatcher = System.Windows.Application.Current?.Dispatcher
                        ?? Dispatcher.CurrentDispatcher;

        // Store the delegate in a field so we can unsubscribe with the same
        // reference (static-event invocation-list equality is by delegate value,
        // and -= with a re-constructed lambda would silently fail to detach).
        _handler = (_, e) => HandleSessionSwitch(e.Reason);
        SystemEvents.SessionSwitch += _handler;
    }

    /// <summary>
    /// Test seam: exposed as <c>internal</c> so SessionLockServiceTests can
    /// exercise the reason-matching + Dispatcher-marshal paths without waiting
    /// for a real Windows session-switch event (which needs a UAT).
    /// </summary>
    internal void HandleSessionSwitch(SessionSwitchReason reason)
    {
        if (_disposed) return;

        // D-14 + UI-SPEC: only lock on transitions where the user is clearly
        // leaving the Windows session. SessionUnlock / RemoteConnect are NOT
        // triggers — unlock is handled via our own master-password flow.
        if (reason != SessionSwitchReason.SessionLock
            && reason != SessionSwitchReason.ConsoleDisconnect
            && reason != SessionSwitchReason.RemoteDisconnect)
        {
            return;
        }

        // Pitfall 7: marshal to UI. BeginInvoke (not Invoke) so the SystemEvents
        // thread doesn't block on subscribers that touch WPF DependencyObjects.
        _uiDispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            _bus.Publish(new AppLockedEvent(LockReason.SessionSwitch));
        }));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // learn.microsoft.com: "you must detach your event handlers when your
        // application is disposed, or memory leaks will result." Static events
        // retain their invocation list forever otherwise.
        SystemEvents.SessionSwitch -= _handler;
    }
}
