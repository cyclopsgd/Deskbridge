using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.ViewModels;
using Wpf.Ui.Controls;

namespace Deskbridge.Services;

/// <summary>
/// Phase 6 Plan 06-02 (NOTF-01 / NOTF-02 / NOTF-03): subscribes to six bus events
/// and maps each to a <see cref="ToastStackViewModel.Push"/> call using the exact
/// strings and durations from 06-UI-SPEC.md §Toast Copywriting.
///
/// <para>Registered as a singleton and eagerly resolved from <c>App.OnStartup</c>
/// so the subscriptions land BEFORE the first <see cref="ConnectionRequestedEvent"/>
/// fires (same pattern as Phase 5 <c>ITabHostManager</c>).</para>
///
/// <para>NOTF-02 compliance: this service NEVER calls <see cref="Wpf.Ui.IContentDialogService"/> —
/// bus events surface through toasts only, matching D-08 "no modals for non-critical events".</para>
///
/// <para>Reconnect disambiguation: <see cref="ReconnectingEvent"/> adds the connection id to
/// <see cref="_reconnectingIds"/>. A subsequent <see cref="ConnectionEstablishedEvent"/> with an
/// id in that set produces the "Reconnected" copy (3s Info) instead of "Connected" (2s Info),
/// matching UI-SPEC line 392.</para>
/// </summary>
public sealed class ToastSubscriptionService
{
    private readonly ToastStackViewModel _stack;
    private readonly HashSet<Guid> _reconnectingIds = new();

    public ToastSubscriptionService(IEventBus bus, ToastStackViewModel stack)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(stack);
        _stack = stack;

        bus.Subscribe<ConnectionEstablishedEvent>(this, OnConnected);
        bus.Subscribe<ConnectionClosedEvent>(this, OnClosed);
        bus.Subscribe<ReconnectingEvent>(this, OnReconnecting);
        bus.Subscribe<ConnectionFailedEvent>(this, OnFailed);
        bus.Subscribe<UpdateAvailableEvent>(this, OnUpdateAvailable);
        bus.Subscribe<ConnectionImportedEvent>(this, OnImported);
    }

    private void OnConnected(ConnectionEstablishedEvent e)
    {
        var host = e.Connection.Hostname;
        if (_reconnectingIds.Remove(e.Connection.Id))
        {
            // UI-SPEC line 392: successful auto-reconnect = Info 3s "Reconnected".
            _stack.Push(
                "Reconnected",
                $"Reconnection to {host} succeeded.",
                ControlAppearance.Info,
                SymbolRegular.Info24,
                TimeSpan.FromSeconds(3));
        }
        else
        {
            // UI-SPEC line 372: Info 2s "Connected".
            _stack.Push(
                "Connected",
                $"Connected to {host}.",
                ControlAppearance.Info,
                SymbolRegular.Info24,
                TimeSpan.FromSeconds(2));
        }
    }

    private void OnClosed(ConnectionClosedEvent e)
    {
        // UI-SPEC line 373 + Toast Copywriting §Deliberate silence:
        // user-initiated disconnect emits NO toast (D-08 extends to "no toasts
        // for user-directed actions" — closing a tab is self-evident).
        if (e.Reason == DisconnectReason.UserInitiated) return;

        _stack.Push(
            "Disconnected",
            $"Disconnected from {e.Connection.Hostname}.",
            ControlAppearance.Info,
            SymbolRegular.Info24,
            TimeSpan.FromSeconds(3));
    }

    private void OnReconnecting(ReconnectingEvent e)
    {
        _reconnectingIds.Add(e.Connection.Id);

        // UI-SPEC line 375: Warning sticky "Reconnecting" — sticky until
        // Established/Failed fires, or user dismisses.
        _stack.Push(
            "Reconnecting",
            $"Connection to {e.Connection.Hostname} lost — attempt {e.Attempt}/20.",
            ControlAppearance.Caution,
            SymbolRegular.ArrowClockwise24,
            duration: null);
    }

    private void OnFailed(ConnectionFailedEvent e)
    {
        // Clear reconnect state — a Failed event terminates the reconnect loop, so a later
        // ConnectionEstablishedEvent (user retry) should not appear as "Reconnected".
        _reconnectingIds.Remove(e.Connection.Id);

        // UI-SPEC line 376: Error sticky "Connection failed".
        _stack.Push(
            "Connection failed",
            $"Could not connect to {e.Connection.Hostname}. {e.Reason}.",
            ControlAppearance.Danger,
            SymbolRegular.ErrorCircle24,
            duration: null);
    }

    private void OnUpdateAvailable(UpdateAvailableEvent e)
    {
        // UI-SPEC line 377: Info sticky "Update available".
        _stack.Push(
            "Update available",
            $"Deskbridge {e.Version} is ready to install.",
            ControlAppearance.Info,
            SymbolRegular.Info24,
            duration: null);
    }

    private void OnImported(ConnectionImportedEvent e)
    {
        // UI-SPEC line 378: Info 3s "Import complete".
        _stack.Push(
            "Import complete",
            $"Imported {e.Count} connection(s) from {e.Source}.",
            ControlAppearance.Info,
            SymbolRegular.Info24,
            TimeSpan.FromSeconds(3));
    }
}
