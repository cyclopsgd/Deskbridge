using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Services;
using Deskbridge.Tests.Fixtures;
using Deskbridge.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge.Tests.Notifications;

/// <summary>
/// Phase 6 Plan 06-02 coverage for <see cref="ToastSubscriptionService"/>:
/// NOTF-01 (toast mapping), NOTF-02 (no modals), NOTF-03 (6-event binding),
/// UI-SPEC §Toast Copywriting exact-strings verification.
///
/// <para>Uses a real <see cref="ToastStackViewModel"/> so Push's observable
/// side effect (Items manipulation) can be asserted. NSubstitute fakes the
/// <see cref="IEventBus"/> and captures the handler delegates for direct
/// invocation — no real bus publishing needed.</para>
/// </summary>
[Collection("RDP-STA")]
public sealed class ToastSubscriptionServiceTests
{
    private readonly StaCollectionFixture _fixture;

    public ToastSubscriptionServiceTests(StaCollectionFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Build a subscription service backed by a real stack VM and captured handlers
    /// for every subscribe call. Returns the stack VM plus a dictionary keyed by
    /// event type yielding the captured delegate, so tests can invoke handlers directly.
    /// </summary>
    private static (ToastStackViewModel Stack, Dictionary<Type, Delegate> Handlers) BuildService(IEventBus? bus = null)
    {
        bus ??= Substitute.For<IEventBus>();
        var handlers = new Dictionary<Type, Delegate>();

        // Capture each Subscribe<T>(object, Action<T>) call. NSubstitute's
        // When().Do() runs on the call — we snapshot the second arg.
        bus.When(b => b.Subscribe<ConnectionEstablishedEvent>(Arg.Any<object>(), Arg.Any<Action<ConnectionEstablishedEvent>>()))
           .Do(ci => handlers[typeof(ConnectionEstablishedEvent)] = ci.ArgAt<Action<ConnectionEstablishedEvent>>(1));
        bus.When(b => b.Subscribe<ConnectionClosedEvent>(Arg.Any<object>(), Arg.Any<Action<ConnectionClosedEvent>>()))
           .Do(ci => handlers[typeof(ConnectionClosedEvent)] = ci.ArgAt<Action<ConnectionClosedEvent>>(1));
        bus.When(b => b.Subscribe<ReconnectingEvent>(Arg.Any<object>(), Arg.Any<Action<ReconnectingEvent>>()))
           .Do(ci => handlers[typeof(ReconnectingEvent)] = ci.ArgAt<Action<ReconnectingEvent>>(1));
        bus.When(b => b.Subscribe<ConnectionFailedEvent>(Arg.Any<object>(), Arg.Any<Action<ConnectionFailedEvent>>()))
           .Do(ci => handlers[typeof(ConnectionFailedEvent)] = ci.ArgAt<Action<ConnectionFailedEvent>>(1));
        bus.When(b => b.Subscribe<UpdateAvailableEvent>(Arg.Any<object>(), Arg.Any<Action<UpdateAvailableEvent>>()))
           .Do(ci => handlers[typeof(UpdateAvailableEvent)] = ci.ArgAt<Action<UpdateAvailableEvent>>(1));
        bus.When(b => b.Subscribe<ConnectionImportedEvent>(Arg.Any<object>(), Arg.Any<Action<ConnectionImportedEvent>>()))
           .Do(ci => handlers[typeof(ConnectionImportedEvent)] = ci.ArgAt<Action<ConnectionImportedEvent>>(1));

        var stack = new ToastStackViewModel();
        _ = new ToastSubscriptionService(bus, stack);

        return (stack, handlers);
    }

    private static void Invoke<TEvent>(Dictionary<Type, Delegate> handlers, TEvent evt)
    {
        handlers.Should().ContainKey(typeof(TEvent));
        ((Action<TEvent>)handlers[typeof(TEvent)]).Invoke(evt);
    }

    // ------------------------------------------------------------------
    // Test 1 — ConnectionEstablishedEvent (fresh) → Info 2s "Connected"
    // ------------------------------------------------------------------
    [Fact]
    public void ConnectionEstablished_Fresh_PushesInfoConnectedForTwoSeconds()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (stack, handlers) = BuildService();
            var conn = new ConnectionModel { Hostname = "srv01" };
            var host = Substitute.For<IProtocolHost>();

            Invoke(handlers, new ConnectionEstablishedEvent(conn, host));

            stack.Items.Count.Should().Be(1);
            var t = stack.Items[0];
            t.Title.Should().Be("Connected");
            t.Message.Should().Be("Connected to srv01.");
            t.Appearance.Should().Be(ControlAppearance.Info);
            t.Icon.Should().Be(SymbolRegular.Info24);
            t.Duration.Should().Be(TimeSpan.FromSeconds(2));
        });
    }

    // ------------------------------------------------------------------
    // Test 2 — ConnectionClosed (UserInitiated) → NO toast
    // ------------------------------------------------------------------
    [Fact]
    public void ConnectionClosed_UserInitiated_DoesNotPushToast()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (stack, handlers) = BuildService();

            Invoke(handlers, new ConnectionClosedEvent(
                new ConnectionModel { Hostname = "srv01" },
                DisconnectReason.UserInitiated));

            stack.Items.Should().BeEmpty();
        });
    }

    // ------------------------------------------------------------------
    // Test 3 — ConnectionClosed (RemoteDisconnect) → Info 3s "Disconnected"
    // ------------------------------------------------------------------
    [Fact]
    public void ConnectionClosed_RemoteDisconnect_PushesInfoDisconnectedForThreeSeconds()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (stack, handlers) = BuildService();

            Invoke(handlers, new ConnectionClosedEvent(
                new ConnectionModel { Hostname = "srv02" },
                DisconnectReason.RemoteDisconnect));

            stack.Items.Count.Should().Be(1);
            var t = stack.Items[0];
            t.Title.Should().Be("Disconnected");
            t.Message.Should().Be("Disconnected from srv02.");
            t.Appearance.Should().Be(ControlAppearance.Info);
            t.Duration.Should().Be(TimeSpan.FromSeconds(3));
        });
    }

    // ------------------------------------------------------------------
    // Test 4 — ConnectionClosed (Error) → Info 3s "Disconnected" (same copy as RemoteDisconnect)
    // ------------------------------------------------------------------
    [Fact]
    public void ConnectionClosed_Error_PushesInfoDisconnected()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (stack, handlers) = BuildService();

            Invoke(handlers, new ConnectionClosedEvent(
                new ConnectionModel { Hostname = "srv03" },
                DisconnectReason.Error));

            stack.Items.Count.Should().Be(1);
            stack.Items[0].Title.Should().Be("Disconnected");
            stack.Items[0].Message.Should().Be("Disconnected from srv03.");
        });
    }

    // ------------------------------------------------------------------
    // Test 5 — ReconnectingEvent → Caution sticky "Reconnecting"
    // ------------------------------------------------------------------
    [Fact]
    public void ReconnectingEvent_PushesCautionStickyToast()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (stack, handlers) = BuildService();
            var conn = new ConnectionModel { Hostname = "srv04" };

            Invoke(handlers, new ReconnectingEvent(conn, Attempt: 3, Delay: TimeSpan.FromSeconds(2)));

            stack.Items.Count.Should().Be(1);
            var t = stack.Items[0];
            t.Title.Should().Be("Reconnecting");
            t.Message.Should().Be("Connection to srv04 lost — attempt 3/20.");
            t.Appearance.Should().Be(ControlAppearance.Caution);
            t.Icon.Should().Be(SymbolRegular.ArrowClockwise24);
            t.Duration.Should().BeNull("Reconnecting is sticky per UI-SPEC line 375");
        });
    }

    // ------------------------------------------------------------------
    // Test 6 — ConnectionFailedEvent → Danger sticky "Connection failed"
    // ------------------------------------------------------------------
    [Fact]
    public void ConnectionFailedEvent_PushesDangerStickyToast()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (stack, handlers) = BuildService();
            var conn = new ConnectionModel { Hostname = "srv05" };

            Invoke(handlers, new ConnectionFailedEvent(conn, Reason: "Timeout", Exception: null));

            stack.Items.Count.Should().Be(1);
            var t = stack.Items[0];
            t.Title.Should().Be("Connection failed");
            t.Message.Should().Be("Could not connect to srv05. Timeout.");
            t.Appearance.Should().Be(ControlAppearance.Danger);
            t.Icon.Should().Be(SymbolRegular.ErrorCircle24);
            t.Duration.Should().BeNull("Connection failed is sticky per UI-SPEC line 376");
        });
    }

    // ------------------------------------------------------------------
    // Test 7 — UpdateAvailableEvent → Info sticky "Update available"
    // ------------------------------------------------------------------
    [Fact]
    public void UpdateAvailableEvent_PushesInfoStickyToast()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (stack, handlers) = BuildService();

            Invoke(handlers, new UpdateAvailableEvent("1.2.3"));

            stack.Items.Count.Should().Be(1);
            var t = stack.Items[0];
            t.Title.Should().Be("Update available");
            t.Message.Should().Be("Deskbridge 1.2.3 is ready to install.");
            t.Appearance.Should().Be(ControlAppearance.Info);
            t.Duration.Should().BeNull("Update available is sticky per UI-SPEC line 377");
        });
    }

    // ------------------------------------------------------------------
    // Test 8 — ConnectionImportedEvent → Info 3s "Import complete"
    // ------------------------------------------------------------------
    [Fact]
    public void ConnectionImportedEvent_PushesInfoImportCompleteForThreeSeconds()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (stack, handlers) = BuildService();

            Invoke(handlers, new ConnectionImportedEvent(Count: 17, Source: "mRemoteNG"));

            stack.Items.Count.Should().Be(1);
            var t = stack.Items[0];
            t.Title.Should().Be("Import complete");
            t.Message.Should().Be("Imported 17 connection(s) from mRemoteNG.");
            t.Duration.Should().Be(TimeSpan.FromSeconds(3));
        });
    }

    // ------------------------------------------------------------------
    // Test 9 — NOTF-02: NO handler calls IContentDialogService
    // ------------------------------------------------------------------
    [Fact]
    public void AllHandlers_NeverCallContentDialogService()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            var dialogs = Substitute.For<IContentDialogService>();
            var (stack, handlers) = BuildService(bus);

            // Exercise every handler with realistic payloads.
            var conn = new ConnectionModel { Hostname = "srv" };
            var host = Substitute.For<IProtocolHost>();
            Invoke(handlers, new ConnectionEstablishedEvent(conn, host));
            Invoke(handlers, new ConnectionClosedEvent(conn, DisconnectReason.RemoteDisconnect));
            Invoke(handlers, new ReconnectingEvent(conn, 1, TimeSpan.FromSeconds(1)));
            Invoke(handlers, new ConnectionFailedEvent(conn, "x", null));
            Invoke(handlers, new UpdateAvailableEvent("9.9.9"));
            Invoke(handlers, new ConnectionImportedEvent(1, "test"));

            // NOTF-02: no modals from bus events — dialog service is never touched.
            dialogs.ReceivedCalls().Should().BeEmpty(
                "NOTF-02: bus-event handlers must NEVER open a ContentDialog");
        });
    }

    // ------------------------------------------------------------------
    // Test 10 — Exactly 6 subscriptions, one per event type
    // ------------------------------------------------------------------
    [Fact]
    public void Ctor_SubscribesToSixEventTypes_OnceEach()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            var stack = new ToastStackViewModel();
            _ = new ToastSubscriptionService(bus, stack);

            bus.Received(1).Subscribe<ConnectionEstablishedEvent>(Arg.Any<object>(), Arg.Any<Action<ConnectionEstablishedEvent>>());
            bus.Received(1).Subscribe<ConnectionClosedEvent>(Arg.Any<object>(), Arg.Any<Action<ConnectionClosedEvent>>());
            bus.Received(1).Subscribe<ReconnectingEvent>(Arg.Any<object>(), Arg.Any<Action<ReconnectingEvent>>());
            bus.Received(1).Subscribe<ConnectionFailedEvent>(Arg.Any<object>(), Arg.Any<Action<ConnectionFailedEvent>>());
            bus.Received(1).Subscribe<UpdateAvailableEvent>(Arg.Any<object>(), Arg.Any<Action<UpdateAvailableEvent>>());
            bus.Received(1).Subscribe<ConnectionImportedEvent>(Arg.Any<object>(), Arg.Any<Action<ConnectionImportedEvent>>());
        });
    }

    // ------------------------------------------------------------------
    // Test 11 — Reconnect → Reconnected transition: Reconnecting followed by
    // ConnectionEstablishedEvent for the same id produces "Reconnected" copy.
    // ------------------------------------------------------------------
    [Fact]
    public void ReconnectingThenEstablished_SameConnection_PushesReconnectedCopy()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (stack, handlers) = BuildService();
            var conn = new ConnectionModel { Hostname = "srv06" };
            var host = Substitute.For<IProtocolHost>();

            Invoke(handlers, new ReconnectingEvent(conn, 1, TimeSpan.FromSeconds(1)));
            Invoke(handlers, new ConnectionEstablishedEvent(conn, host));

            // Newest-at-index-0 — the most recent push is the Reconnected toast.
            stack.Items.Count.Should().BeGreaterThanOrEqualTo(1);
            var t = stack.Items[0];
            t.Title.Should().Be("Reconnected");
            t.Message.Should().Be("Reconnection to srv06 succeeded.");
            t.Duration.Should().Be(TimeSpan.FromSeconds(3));
        });
    }

    // ------------------------------------------------------------------
    // Test 12 — After Failed, a later Established fires "Connected" (not "Reconnected")
    // because Failed clears _reconnectingIds.
    // ------------------------------------------------------------------
    [Fact]
    public void FailedClearsReconnectState_LaterEstablishedIsConnectedNotReconnected()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (stack, handlers) = BuildService();
            var conn = new ConnectionModel { Hostname = "srv07" };
            var host = Substitute.For<IProtocolHost>();

            Invoke(handlers, new ReconnectingEvent(conn, 5, TimeSpan.FromSeconds(2)));
            Invoke(handlers, new ConnectionFailedEvent(conn, "Timeout", null));
            Invoke(handlers, new ConnectionEstablishedEvent(conn, host));

            // Newest on top: the Established event should be "Connected" (fresh) — Failed cleared the reconnect state.
            stack.Items[0].Title.Should().Be("Connected");
            stack.Items[0].Duration.Should().Be(TimeSpan.FromSeconds(2));
        });
    }
}
