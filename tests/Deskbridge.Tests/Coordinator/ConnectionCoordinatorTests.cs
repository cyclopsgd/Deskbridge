using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Services;
using Deskbridge.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deskbridge.Tests.Coordinator;

/// <summary>
/// Tests for <see cref="ConnectionCoordinator"/> — the event-bus bridge that owns
/// STA dispatch and the single-host replacement policy (Open Question #2, D-11).
/// Constructor accepts optional <see cref="Dispatcher"/> (Open Question #3) so tests
/// can inject the STA-runner's dispatcher.
/// </summary>
[Collection("RDP-STA")]
public sealed class ConnectionCoordinatorTests
{
    private readonly StaCollectionFixture _fixture;
    public ConnectionCoordinatorTests(StaCollectionFixture fixture) => _fixture = fixture;

    [Fact]
    public void Subscribes_ToConnectionRequestedEvent_OnConstruction()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            var connect = Substitute.For<IConnectionPipeline>();
            var disconnect = Substitute.For<IDisconnectPipeline>();

            using var coord = new ConnectionCoordinator(
                bus, connect, disconnect, NullLogger<ConnectionCoordinator>.Instance);

            bus.Received().Subscribe(
                Arg.Any<object>(), Arg.Any<Action<ConnectionRequestedEvent>>());
        });
    }

    /// <summary>
    /// Siting-order regression guard: coordinator MUST subscribe to
    /// <see cref="HostCreatedEvent"/> on construction so it can raise HostMounted
    /// BEFORE ConnectStage runs (RDP-ACTIVEX-PITFALLS §1).
    /// </summary>
    [Fact]
    public void Subscribes_ToHostCreatedEvent_OnConstruction()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var bus = Substitute.For<IEventBus>();
            var connect = Substitute.For<IConnectionPipeline>();
            var disconnect = Substitute.For<IDisconnectPipeline>();

            using var coord = new ConnectionCoordinator(
                bus, connect, disconnect, NullLogger<ConnectionCoordinator>.Instance);

            bus.Received().Subscribe(
                Arg.Any<object>(), Arg.Any<Action<HostCreatedEvent>>());
        });
    }

    [Fact]
    public void Marshals_ToDispatcher_WhenPublished_FromWorkerThread()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            // Capture the bus handler, then invoke it from a worker thread — coordinator must marshal.
            Action<ConnectionRequestedEvent>? captured = null;
            var bus = Substitute.For<IEventBus>();
            bus.When(b => b.Subscribe(Arg.Any<object>(), Arg.Any<Action<ConnectionRequestedEvent>>()))
                .Do(ci => captured = ci.Arg<Action<ConnectionRequestedEvent>>());
            var connect = Substitute.For<IConnectionPipeline>();
            connect.ConnectAsync(Arg.Any<ConnectionModel>()).Returns(Task.FromResult(new PipelineResult(true)));
            var disconnect = Substitute.For<IDisconnectPipeline>();
            var dispatcher = Dispatcher.CurrentDispatcher;

            using var coord = new ConnectionCoordinator(
                bus, connect, disconnect, NullLogger<ConnectionCoordinator>.Instance, dispatcher);

            captured.Should().NotBeNull();

            // Publish from worker thread
            var model = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
            Task.Run(() => captured!(new ConnectionRequestedEvent(model))).GetAwaiter().GetResult();

            // Pump the dispatcher briefly so the InvokeAsync callback runs
            dispatcher.Invoke(() => { /* drain */ }, DispatcherPriority.ApplicationIdle);

            connect.Received().ConnectAsync(Arg.Is<ConnectionModel>(c => c == model));
        });
    }

    [Fact]
    public void CallsConnectPipeline_ConnectAsync_WithTheEventConnection()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            Action<ConnectionRequestedEvent>? captured = null;
            var bus = Substitute.For<IEventBus>();
            bus.When(b => b.Subscribe(Arg.Any<object>(), Arg.Any<Action<ConnectionRequestedEvent>>()))
                .Do(ci => captured = ci.Arg<Action<ConnectionRequestedEvent>>());
            var connect = Substitute.For<IConnectionPipeline>();
            connect.ConnectAsync(Arg.Any<ConnectionModel>()).Returns(Task.FromResult(new PipelineResult(true)));
            var disconnect = Substitute.For<IDisconnectPipeline>();
            var dispatcher = Dispatcher.CurrentDispatcher;

            using var coord = new ConnectionCoordinator(
                bus, connect, disconnect, NullLogger<ConnectionCoordinator>.Instance, dispatcher);

            var model = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
            captured!(new ConnectionRequestedEvent(model));

            // Invoke runs synchronously since we're on the dispatcher thread already
            connect.Received().ConnectAsync(model);
        });
    }

    /// <summary>
    /// Siting-order regression guard: <c>HostCreatedEvent</c> from the pipeline must
    /// (a) populate <see cref="ConnectionCoordinator.ActiveHost"/> and
    /// (b) raise <see cref="IConnectionCoordinator.HostMounted"/> — BEFORE
    /// <c>ConnectStage</c> runs. MainWindow relies on this to mount the WFH and force
    /// a layout pass so AxHost.Handle is realized in time for <c>ConnectAsync</c>
    /// (RDP-ACTIVEX-PITFALLS §1).
    /// </summary>
    [Fact]
    public void HostCreatedEvent_SetsActiveHost_AndRaisesHostMounted()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            Action<HostCreatedEvent>? hostCreatedHandler = null;
            var bus = Substitute.For<IEventBus>();
            bus.When(b => b.Subscribe(Arg.Any<object>(), Arg.Any<Action<HostCreatedEvent>>()))
                .Do(ci => hostCreatedHandler = ci.Arg<Action<HostCreatedEvent>>());
            var connect = Substitute.For<IConnectionPipeline>();
            var disconnect = Substitute.For<IDisconnectPipeline>();
            var dispatcher = Dispatcher.CurrentDispatcher;

            using var coord = new ConnectionCoordinator(
                bus, connect, disconnect, NullLogger<ConnectionCoordinator>.Instance, dispatcher);

            IProtocolHost? mountedHost = null;
            coord.HostMounted += (_, host) => mountedHost = host;

            var model = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
            var host = Substitute.For<IProtocolHost>();
            hostCreatedHandler.Should().NotBeNull();
            hostCreatedHandler!(new HostCreatedEvent(model, host));

            coord.ActiveHost.Should().BeSameAs(host);
            mountedHost.Should().BeSameAs(host);
        });
    }

    /// <summary>
    /// Regression guard: <see cref="ConnectionFailedEvent"/> for the active host must
    /// raise <see cref="IConnectionCoordinator.HostUnmounted"/> BEFORE disposing the host.
    /// Reverse order causes a cascade <c>ObjectDisposedException</c> because
    /// <c>MainWindow.OnHostUnmounted</c> accesses <c>rdp.Host</c>, whose getter throws
    /// once <c>Dispose</c> has nulled the underlying WinForms host.
    /// Before this subscription existed, the pipeline published ConnectionFailedEvent
    /// but no one logged or cleaned up — users saw a "silent hang" with no diagnostic trail.
    /// </summary>
    [Fact]
    public void ConnectionFailedEvent_ForActiveHost_RaisesHostUnmountedBeforeDispose()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            Action<HostCreatedEvent>? hostCreatedHandler = null;
            Action<ConnectionFailedEvent>? failedHandler = null;
            var bus = Substitute.For<IEventBus>();
            bus.When(b => b.Subscribe(Arg.Any<object>(), Arg.Any<Action<HostCreatedEvent>>()))
                .Do(ci => hostCreatedHandler = ci.Arg<Action<HostCreatedEvent>>());
            bus.When(b => b.Subscribe(Arg.Any<object>(), Arg.Any<Action<ConnectionFailedEvent>>()))
                .Do(ci => failedHandler = ci.Arg<Action<ConnectionFailedEvent>>());
            var connect = Substitute.For<IConnectionPipeline>();
            var disconnect = Substitute.For<IDisconnectPipeline>();
            var dispatcher = Dispatcher.CurrentDispatcher;

            using var coord = new ConnectionCoordinator(
                bus, connect, disconnect, NullLogger<ConnectionCoordinator>.Instance, dispatcher);

            // Ordering probe: record every observable action against the host in sequence.
            // "Unmounted" is recorded from the HostUnmounted event; "Disposed" is recorded
            // from the NSubstitute stub's Dispose() call. The fix requires Unmounted first.
            var callOrder = new List<string>();
            IProtocolHost? unmountedHost = null;

            var model = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
            var host = Substitute.For<IProtocolHost>();
            host.When(h => h.Dispose()).Do(_ => callOrder.Add("Disposed"));

            coord.HostUnmounted += (_, h) =>
            {
                callOrder.Add("Unmounted");
                unmountedHost = h;
            };

            // Seed active host via HostCreatedEvent, then publish matching ConnectionFailedEvent.
            hostCreatedHandler.Should().NotBeNull();
            failedHandler.Should().NotBeNull();
            hostCreatedHandler!(new HostCreatedEvent(model, host));
            coord.ActiveHost.Should().BeSameAs(host);

            failedHandler!(new ConnectionFailedEvent(model, "self-rejected (1800)", null));

            host.Received().Dispose();
            unmountedHost.Should().BeSameAs(host);
            coord.ActiveHost.Should().BeNull();
            callOrder.Should().Equal("Unmounted", "Disposed");
        });
    }

    /// <summary>
    /// Rapid-double-click guard: a second <see cref="ConnectionRequestedEvent"/> for the
    /// SAME <see cref="ConnectionModel.Id"/> while the first host is still active must be
    /// dropped silently. Without this, the replace-active-host branch tears down the
    /// in-flight <c>RdpHostControl</c> mid-<c>Connect()</c>, producing the
    /// <c>RdpConnectFailedException discReason=1</c> → <c>COMException 0x83450003</c>
    /// cascade observed in the field. Auto-reconnect (driven by
    /// <see cref="RdpReconnectCoordinator"/>) is unaffected because it bypasses
    /// <see cref="ConnectionRequestedEvent"/> entirely.
    /// </summary>
    [Fact]
    public void OnConnectionRequested_IgnoresDuplicateRequestForSameActiveModel()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            Action<ConnectionRequestedEvent>? reqHandler = null;
            Action<HostCreatedEvent>? hostCreatedHandler = null;
            var bus = Substitute.For<IEventBus>();
            bus.When(b => b.Subscribe(Arg.Any<object>(), Arg.Any<Action<ConnectionRequestedEvent>>()))
                .Do(ci => reqHandler = ci.Arg<Action<ConnectionRequestedEvent>>());
            bus.When(b => b.Subscribe(Arg.Any<object>(), Arg.Any<Action<HostCreatedEvent>>()))
                .Do(ci => hostCreatedHandler = ci.Arg<Action<HostCreatedEvent>>());
            var connect = Substitute.For<IConnectionPipeline>();
            connect.ConnectAsync(Arg.Any<ConnectionModel>()).Returns(Task.FromResult(new PipelineResult(true)));
            var disconnect = Substitute.For<IDisconnectPipeline>();
            var dispatcher = Dispatcher.CurrentDispatcher;

            using var coord = new ConnectionCoordinator(
                bus, connect, disconnect, NullLogger<ConnectionCoordinator>.Instance, dispatcher);

            var model = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
            var host = Substitute.For<IProtocolHost>();

            // Fire #1: legitimate connect → pipeline invoked → host created → _active set.
            reqHandler!(new ConnectionRequestedEvent(model));
            hostCreatedHandler!(new HostCreatedEvent(model, host));
            coord.ActiveHost.Should().BeSameAs(host);

            // Fire #2: duplicate (same model.Id) while host still active → must be dropped.
            // Use the SAME model instance so model.Id matches exactly (rapid-double-click case).
            reqHandler!(new ConnectionRequestedEvent(model));

            // Connect must have been called exactly once (the first request only); the
            // duplicate must NOT have triggered another ConnectAsync. Disconnect must NOT
            // have been called either — the in-flight host stays untouched.
            connect.Received(1).ConnectAsync(Arg.Any<ConnectionModel>());
            disconnect.DidNotReceive().DisconnectAsync(Arg.Any<DisconnectContext>());
            host.DidNotReceive().Dispose();
            coord.ActiveHost.Should().BeSameAs(host);
        });
    }

    [Fact]
    public void DisposesAndReplaces_ActiveHost_OnSecondConnectionRequestedEvent()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            Action<ConnectionRequestedEvent>? reqHandler = null;
            Action<HostCreatedEvent>? hostCreatedHandler = null;
            var bus = Substitute.For<IEventBus>();
            bus.When(b => b.Subscribe(Arg.Any<object>(), Arg.Any<Action<ConnectionRequestedEvent>>()))
                .Do(ci => reqHandler = ci.Arg<Action<ConnectionRequestedEvent>>());
            bus.When(b => b.Subscribe(Arg.Any<object>(), Arg.Any<Action<HostCreatedEvent>>()))
                .Do(ci => hostCreatedHandler = ci.Arg<Action<HostCreatedEvent>>());
            var connect = Substitute.For<IConnectionPipeline>();
            connect.ConnectAsync(Arg.Any<ConnectionModel>()).Returns(Task.FromResult(new PipelineResult(true)));
            var disconnect = Substitute.For<IDisconnectPipeline>();
            disconnect.DisconnectAsync(Arg.Any<DisconnectContext>()).Returns(Task.FromResult(new PipelineResult(true)));
            var dispatcher = Dispatcher.CurrentDispatcher;

            using var coord = new ConnectionCoordinator(
                bus, connect, disconnect, NullLogger<ConnectionCoordinator>.Instance, dispatcher);

            var first = new ConnectionModel { Hostname = "first", Protocol = Protocol.Rdp };
            var second = new ConnectionModel { Hostname = "second", Protocol = Protocol.Rdp };
            var firstHost = Substitute.For<IProtocolHost>();

            // Simulate connect flow: first request → HostCreated (populates _active in new design)
            reqHandler!(new ConnectionRequestedEvent(first));
            hostCreatedHandler!(new HostCreatedEvent(first, firstHost));

            coord.ActiveHost.Should().BeSameAs(firstHost);

            // Second request while one active → coordinator must run disconnect pipeline
            reqHandler!(new ConnectionRequestedEvent(second));

            disconnect.Received().DisconnectAsync(Arg.Is<DisconnectContext>(d => d.Host == firstHost));
            connect.Received().ConnectAsync(second);
        });
    }
}
