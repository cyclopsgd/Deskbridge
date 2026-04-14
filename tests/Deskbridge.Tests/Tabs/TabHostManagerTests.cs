using System.Reflection;
using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Services;
using Deskbridge.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Deskbridge.Tests.Tabs;

/// <summary>
/// Unit tests for <see cref="TabHostManager"/> (Phase 5, D-01). STA fixture required
/// because TabHostManager marshals its handlers through the dispatcher
/// (mirrors the ConnectionCoordinator tests' pattern).
/// </summary>
[Collection("RDP-STA")]
public sealed class TabHostManagerTests
{
    private readonly StaCollectionFixture _fixture;
    public TabHostManagerTests(StaCollectionFixture fixture) => _fixture = fixture;

    private static (
        TabHostManager sut,
        IEventBus bus,
        IConnectionCoordinator coord,
        IDisconnectPipeline disc,
        ISnackbarService snack,
        Action<HostCreatedEvent> raiseHostCreated
    ) BuildSut()
    {
        Action<HostCreatedEvent>? hostCreatedHandler = null;
        var bus = Substitute.For<IEventBus>();
        bus.When(b => b.Subscribe(Arg.Any<object>(), Arg.Any<Action<HostCreatedEvent>>()))
            .Do(ci => hostCreatedHandler = ci.Arg<Action<HostCreatedEvent>>());

        var coord = Substitute.For<IConnectionCoordinator>();
        var disc = Substitute.For<IDisconnectPipeline>();
        disc.DisconnectAsync(Arg.Any<DisconnectContext>()).Returns(Task.FromResult(new PipelineResult(true)));
        var snack = Substitute.For<ISnackbarService>();

        var sut = new TabHostManager(
            bus, coord, disc, snack,
            NullLogger<TabHostManager>.Instance,
            Dispatcher.CurrentDispatcher);

        hostCreatedHandler.Should().NotBeNull("TabHostManager must subscribe to HostCreatedEvent in its ctor");
        return (sut, bus, coord, disc, snack, hostCreatedHandler!);
    }

    private static IProtocolHost MakeHost(Guid id)
    {
        var host = Substitute.For<IProtocolHost>();
        host.ConnectionId.Returns(id);
        return host;
    }

    private static ConnectionModel MakeModel(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Hostname = "h",
        Protocol = Protocol.Rdp,
    };

    [Fact]
    public void TryGetExistingTab_ReturnsTrue_WhenConnectionIsOpen()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (sut, _, coord, _, _, raise) = BuildSut();
            var model = MakeModel();
            var host = MakeHost(model.Id);

            raise(new HostCreatedEvent(model, host));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, host);

            sut.TryGetExistingTab(model.Id, out var found).Should().BeTrue();
            found.Should().BeSameAs(host);
            sut.ActiveCount.Should().Be(1);
            sut.ActiveId.Should().Be(model.Id);
            sut.GetHost(model.Id).Should().BeSameAs(host);
            sut.Dispose();
        });
    }

    [Fact]
    public void TryGetExistingTab_ReturnsFalse_WhenConnectionIsClosed()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (sut, _, _, _, _, _) = BuildSut();
            sut.TryGetExistingTab(Guid.NewGuid(), out var host).Should().BeFalse();
            host.Should().BeNull();
            sut.Dispose();
        });
    }

    [Fact]
    public void OnHostMounted_PublishesTabOpenedEvent()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (sut, bus, coord, _, _, raise) = BuildSut();
            var model = MakeModel();
            var host = MakeHost(model.Id);

            raise(new HostCreatedEvent(model, host));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, host);

            bus.Received(1).Publish(Arg.Is<TabOpenedEvent>(e => e.ConnectionId == model.Id));
            sut.Dispose();
        });
    }

    [Fact]
    public void OnHostUnmounted_PublishesTabClosedEvent()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (sut, bus, coord, _, _, raise) = BuildSut();
            var model = MakeModel();
            var host = MakeHost(model.Id);

            raise(new HostCreatedEvent(model, host));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, host);
            bus.ClearReceivedCalls();

            coord.HostUnmounted += Raise.Event<EventHandler<IProtocolHost>>(null, host);

            bus.Received(1).Publish(Arg.Is<TabClosedEvent>(e => e.ConnectionId == model.Id));
            sut.Dispose();
        });
    }

    [Fact]
    public void OnHostMounted_PublishesTabSwitchedEvent_WithPreviousAndActive()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (sut, bus, coord, _, _, raise) = BuildSut();
            var modelA = MakeModel();
            var modelB = MakeModel();
            var hostA = MakeHost(modelA.Id);
            var hostB = MakeHost(modelB.Id);

            raise(new HostCreatedEvent(modelA, hostA));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, hostA);
            // First mount — Previous is null, Active is A.
            bus.Received(1).Publish(Arg.Is<TabSwitchedEvent>(e => e.PreviousId == null && e.ActiveId == modelA.Id));

            raise(new HostCreatedEvent(modelB, hostB));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, hostB);
            // Second mount — Previous is A, Active is B.
            bus.Received(1).Publish(Arg.Is<TabSwitchedEvent>(e => e.PreviousId == modelA.Id && e.ActiveId == modelB.Id));

            sut.Dispose();
        });
    }

    [Fact]
    public void Crossing15Threshold_FiresSnackbarOnce()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (sut, _, coord, _, snack, raise) = BuildSut();

            // Open 14 — snackbar MUST NOT fire
            for (var i = 0; i < 14; i++)
            {
                var model = MakeModel();
                var host = MakeHost(model.Id);
                raise(new HostCreatedEvent(model, host));
                coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, host);
            }
            snack.DidNotReceive().Show(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ControlAppearance>(),
                Arg.Any<IconElement>(), Arg.Any<TimeSpan>());

            // Open the 15th — snackbar fires EXACTLY ONCE with the locked UI-SPEC strings
            var m15 = MakeModel();
            var h15 = MakeHost(m15.Id);
            raise(new HostCreatedEvent(m15, h15));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, h15);

            snack.Received(1).Show(
                "Approaching session limit",
                "15 active sessions reached — performance may degrade beyond this point.",
                ControlAppearance.Caution,
                Arg.Any<IconElement>(),
                TimeSpan.FromSeconds(6));

            sut.ActiveCount.Should().Be(15);
            sut.Dispose();
        });
    }

    [Fact]
    public void Above15_DoesNotRefireSnackbar()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (sut, _, coord, _, snack, raise) = BuildSut();

            // Open 17 hosts — snackbar fires ONLY on the 14→15 crossing.
            for (var i = 0; i < 17; i++)
            {
                var m = MakeModel();
                var h = MakeHost(m.Id);
                raise(new HostCreatedEvent(m, h));
                coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, h);
            }

            snack.Received(1).Show(
                Arg.Any<string>(), Arg.Any<string>(),
                ControlAppearance.Caution, Arg.Any<IconElement>(), Arg.Any<TimeSpan>());
            sut.ActiveCount.Should().Be(17);
            sut.Dispose();
        });
    }

    [Fact]
    public void DropBelow15_ThenCrossAgain_FiresSnackbarSecondTime()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var (sut, _, coord, _, snack, raise) = BuildSut();

            // Open 15 — snackbar fires once.
            var hosts = new List<IProtocolHost>();
            for (var i = 0; i < 15; i++)
            {
                var m = MakeModel();
                var h = MakeHost(m.Id);
                hosts.Add(h);
                raise(new HostCreatedEvent(m, h));
                coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, h);
            }
            snack.Received(1).Show(Arg.Any<string>(), Arg.Any<string>(),
                ControlAppearance.Caution, Arg.Any<IconElement>(), Arg.Any<TimeSpan>());

            // Close 2 -> drop to 13 (below threshold, re-arm)
            coord.HostUnmounted += Raise.Event<EventHandler<IProtocolHost>>(null, hosts[0]);
            coord.HostUnmounted += Raise.Event<EventHandler<IProtocolHost>>(null, hosts[1]);
            sut.ActiveCount.Should().Be(13);

            // Open to 14 — still not fired again.
            var m14 = MakeModel();
            var h14 = MakeHost(m14.Id);
            raise(new HostCreatedEvent(m14, h14));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, h14);
            snack.Received(1).Show(Arg.Any<string>(), Arg.Any<string>(),
                ControlAppearance.Caution, Arg.Any<IconElement>(), Arg.Any<TimeSpan>());

            // Open to 15 again — snackbar fires a SECOND time.
            var m15b = MakeModel();
            var h15b = MakeHost(m15b.Id);
            raise(new HostCreatedEvent(m15b, h15b));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, h15b);

            snack.Received(2).Show(Arg.Any<string>(), Arg.Any<string>(),
                ControlAppearance.Caution, Arg.Any<IconElement>(), Arg.Any<TimeSpan>());
            sut.Dispose();
        });
    }

    [Fact]
    public void CloseTabAsync_RunsDisconnectPipeline_AndPublishesTabClosedEvent()
    {
        _ = _fixture;
        StaRunner.RunAsync(async () =>
        {
            var (sut, bus, coord, disc, _, raise) = BuildSut();
            var model = MakeModel();
            var host = MakeHost(model.Id);

            raise(new HostCreatedEvent(model, host));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, host);
            bus.ClearReceivedCalls();

            // Wire disconnect pipeline to simulate the coordinator firing HostUnmounted when disconnect completes.
            disc.DisconnectAsync(Arg.Any<DisconnectContext>()).Returns(ci =>
            {
                coord.HostUnmounted += Raise.Event<EventHandler<IProtocolHost>>(null, host);
                return Task.FromResult(new PipelineResult(true));
            });

            await sut.CloseTabAsync(model.Id);

            await disc.Received(1).DisconnectAsync(Arg.Is<DisconnectContext>(
                d => d.Host == host && d.Connection == model && d.Reason == DisconnectReason.UserInitiated));
            bus.Received(1).Publish(Arg.Is<TabClosedEvent>(e => e.ConnectionId == model.Id));
            sut.ActiveCount.Should().Be(0);
            sut.Dispose();
        });
    }

    [Fact]
    public void CloseAllAsync_SnapshotsKeysBeforeIterating_HandlesReentrantUnmount()
    {
        _ = _fixture;
        StaRunner.RunAsync(async () =>
        {
            var (sut, _, coord, disc, _, raise) = BuildSut();

            // Open 3 hosts.
            var models = new List<ConnectionModel>();
            var hosts = new List<IProtocolHost>();
            for (var i = 0; i < 3; i++)
            {
                var m = MakeModel();
                var h = MakeHost(m.Id);
                models.Add(m);
                hosts.Add(h);
                raise(new HostCreatedEvent(m, h));
                coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, h);
            }

            // Re-entrant OnHostUnmounted: the disconnect pipeline fires HostUnmounted mid-await
            // for THE HOST IT WAS CALLED WITH. Without snapshot iteration, this mutates _hosts
            // during CloseAllAsync's foreach -> InvalidOperationException.
            disc.DisconnectAsync(Arg.Any<DisconnectContext>()).Returns(ci =>
            {
                var ctx = ci.Arg<DisconnectContext>();
                coord.HostUnmounted += Raise.Event<EventHandler<IProtocolHost>>(null, ctx.Host!);
                return Task.FromResult(new PipelineResult(true));
            });

            // Must not throw InvalidOperationException.
            var act = () => sut.CloseAllAsync();
            await act.Should().NotThrowAsync();

            // All three hosts saw disconnect.
            await disc.Received(3).DisconnectAsync(Arg.Any<DisconnectContext>());
            sut.ActiveCount.Should().Be(0);
            sut.Dispose();
        });
    }

    [Fact]
    public void AllHosts_ReturnType_IsReadOnlyCollectionOfIProtocolHost()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            var prop = typeof(ITabHostManager).GetProperty(
                nameof(ITabHostManager.AllHosts),
                BindingFlags.Instance | BindingFlags.Public);
            prop.Should().NotBeNull();
            prop!.PropertyType.Should().Be<IReadOnlyCollection<IProtocolHost>>();
        });
    }

    [Fact]
    public void CloseAllAsync_InvokesDisconnectSequentially()
    {
        _ = _fixture;
        StaRunner.RunAsync(async () =>
        {
            var (sut, _, coord, disc, _, raise) = BuildSut();

            var m1 = MakeModel();
            var m2 = MakeModel();
            var h1 = MakeHost(m1.Id);
            var h2 = MakeHost(m2.Id);
            raise(new HostCreatedEvent(m1, h1));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, h1);
            raise(new HostCreatedEvent(m2, h2));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, h2);

            var gate1 = new TaskCompletionSource();
            var firstStarted = new TaskCompletionSource();
            var concurrent = false;
            var inFlight = 0;

            disc.DisconnectAsync(Arg.Any<DisconnectContext>()).Returns(async ci =>
            {
                var depth = Interlocked.Increment(ref inFlight);
                if (depth > 1) concurrent = true;
                if (ci.Arg<DisconnectContext>().Host == h1)
                {
                    firstStarted.TrySetResult();
                    await gate1.Task;
                }
                coord.HostUnmounted += Raise.Event<EventHandler<IProtocolHost>>(null, ci.Arg<DisconnectContext>().Host!);
                Interlocked.Decrement(ref inFlight);
                return new PipelineResult(true);
            });

            var closeAll = sut.CloseAllAsync();
            await firstStarted.Task;
            // If sequential, inFlight is exactly 1 while gate is closed — the second host hasn't started.
            inFlight.Should().Be(1, "CloseAllAsync must disconnect hosts sequentially (D-08)");
            gate1.TrySetResult();
            await closeAll;

            concurrent.Should().BeFalse();
            sut.Dispose();
        });
    }

    [Fact]
    public void CloseTabAsync_CancelReconnect_BeforeDisconnectPipeline()
    {
        _ = _fixture;
        StaRunner.RunAsync(async () =>
        {
            var (sut, _, coord, disc, _, raise) = BuildSut();
            var model = MakeModel();
            var host = MakeHost(model.Id);
            raise(new HostCreatedEvent(model, host));
            coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, host);

            disc.DisconnectAsync(Arg.Any<DisconnectContext>()).Returns(ci =>
            {
                coord.HostUnmounted += Raise.Event<EventHandler<IProtocolHost>>(null, ci.Arg<DisconnectContext>().Host!);
                return Task.FromResult(new PipelineResult(true));
            });

            await sut.CloseTabAsync(model.Id);

            Received.InOrder(() =>
            {
                coord.CancelReconnect(model.Id);
                disc.DisconnectAsync(Arg.Any<DisconnectContext>());
            });

            sut.Dispose();
        });
    }

    [Fact]
    public void CloseOthersAsync_CancelReconnect_BeforeDisconnectPipeline_PerHost()
    {
        _ = _fixture;
        StaRunner.RunAsync(async () =>
        {
            var (sut, _, coord, disc, _, raise) = BuildSut();
            var keep = MakeModel();
            var close1 = MakeModel();
            var close2 = MakeModel();
            var keepHost = MakeHost(keep.Id);
            var close1Host = MakeHost(close1.Id);
            var close2Host = MakeHost(close2.Id);

            foreach (var (m, h) in new[] { (keep, keepHost), (close1, close1Host), (close2, close2Host) })
            {
                raise(new HostCreatedEvent(m, h));
                coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, h);
            }

            disc.DisconnectAsync(Arg.Any<DisconnectContext>()).Returns(ci =>
            {
                coord.HostUnmounted += Raise.Event<EventHandler<IProtocolHost>>(null, ci.Arg<DisconnectContext>().Host!);
                return Task.FromResult(new PipelineResult(true));
            });

            await sut.CloseOthersAsync(keep.Id);

            // CloseOthersAsync cancels explicitly per-id, then delegates to CloseTabAsync
            // which cancels again (defensive no-op) — so each non-keep id is cancelled at
            // least once. The kept id is never cancelled.
            coord.Received().CancelReconnect(close1.Id);
            coord.Received().CancelReconnect(close2.Id);
            coord.DidNotReceive().CancelReconnect(keep.Id);
            await disc.Received(2).DisconnectAsync(Arg.Any<DisconnectContext>());
            sut.ActiveCount.Should().Be(1);
            sut.GetHost(keep.Id).Should().BeSameAs(keepHost);

            sut.Dispose();
        });
    }

    [Fact]
    public void CloseActiveTab_AutoActivatesNeighbor_AndPublishesTabSwitchedEvent()
    {
        _ = _fixture;
        StaRunner.RunAsync(async () =>
        {
            var (sut, bus, coord, disc, _, raise) = BuildSut();
            var m1 = MakeModel();
            var m2 = MakeModel();
            var m3 = MakeModel();
            var h1 = MakeHost(m1.Id);
            var h2 = MakeHost(m2.Id);
            var h3 = MakeHost(m3.Id);
            foreach (var (m, h) in new[] { (m1, h1), (m2, h2), (m3, h3) })
            {
                raise(new HostCreatedEvent(m, h));
                coord.HostMounted += Raise.Event<EventHandler<IProtocolHost>>(null, h);
            }

            // Active is m3 (last mounted). Close it.
            sut.ActiveId.Should().Be(m3.Id);
            bus.ClearReceivedCalls();

            disc.DisconnectAsync(Arg.Any<DisconnectContext>()).Returns(ci =>
            {
                coord.HostUnmounted += Raise.Event<EventHandler<IProtocolHost>>(null, ci.Arg<DisconnectContext>().Host!);
                return Task.FromResult(new PipelineResult(true));
            });

            await sut.CloseTabAsync(m3.Id);

            // Last-added-of-remaining is m2 → auto-activate m2.
            bus.Received(1).Publish(Arg.Is<TabSwitchedEvent>(e => e.PreviousId == m3.Id && e.ActiveId == m2.Id));
            sut.ActiveId.Should().Be(m2.Id);

            // Close m2 → m1 activates.
            await sut.CloseTabAsync(m2.Id);
            bus.Received(1).Publish(Arg.Is<TabSwitchedEvent>(e => e.PreviousId == m2.Id && e.ActiveId == m1.Id));

            // Close m1 → no tabs; ActiveId null; TabSwitchedEvent carries Guid.Empty.
            await sut.CloseTabAsync(m1.Id);
            bus.Received(1).Publish(Arg.Is<TabSwitchedEvent>(e => e.PreviousId == m1.Id && e.ActiveId == Guid.Empty));
            sut.ActiveId.Should().BeNull();
            sut.ActiveCount.Should().Be(0);

            sut.Dispose();
        });
    }
}
