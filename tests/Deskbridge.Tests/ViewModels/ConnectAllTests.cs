using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Settings;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Tests.Fakes;
using Deskbridge.ViewModels;
using FluentAssertions;
using NSubstitute;
using Wpf.Ui;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// BULK-01 — Connect All command behavior contract (Phase 23 — bulk-operations-ux).
///
/// ConnectAllCommand publishes <see cref="ConnectionRequestedEvent"/> per descendant connection
/// (RDP-05: never calls the pipeline directly), switches to already-open tabs, and gates the GDI
/// confirmation on the user-configured threshold read from <see cref="IWindowStateService"/>.
///
/// Threshold boundary: projected == GdiWarningThreshold → does NOT warn; projected == threshold + 1
/// → warns. Projected count = ITabHostManager.ActiveCount + group.ConnectionCount (recursive).
///
/// The GDI confirmation is a ContentDialog that requires a live ContentDialogHost; under headless
/// xUnit there is none, so the "warns" path bails before publishing (host is null → logged + return).
/// We therefore assert the warn boundary via the OBSERVABLE outcome: below/at threshold publishes
/// for every descendant; above threshold (confirm on) publishes for NONE (the confirm gate stops it).
/// Projected-count math is asserted directly against the same inputs.
/// </summary>
public sealed class ConnectAllTests
{
    private static (
        ConnectionTreeViewModel sut,
        IEventBus bus,
        ITabHostManager tab,
        IConnectionStore store,
        IWindowStateService windowState
    ) BuildSut(BulkOperationsRecord? bulk = null)
    {
        var store = Substitute.For<IConnectionStore>();
        var query = Substitute.For<IConnectionQuery>();
        var creds = Substitute.For<ICredentialService>();
        var dialogs = Substitute.For<IContentDialogService>();
        var snackbar = Substitute.For<ISnackbarService>();
        var provider = Substitute.For<IServiceProvider>();
        var bus = Substitute.For<IEventBus>();
        var tab = Substitute.For<ITabHostManager>();
        var windowState = Substitute.For<IWindowStateService>();

        // Default settings with the requested BulkOperations (confirm/threshold).
        var settings = new AppSettings(
            WindowStateRecord.Default,
            SecuritySettingsRecord.Default,
            UpdateSettingsRecord.Default,
            BulkOperations: bulk ?? BulkOperationsRecord.Default);
        windowState.LoadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(settings));

        var sut = new ConnectionTreeViewModel(
            store, query, creds, dialogs, snackbar, provider, bus, tab,
            new AirspaceSwapper(), new FakeDebouncer(), windowState);

        return (sut, bus, tab, store, windowState);
    }

    /// <summary>Build a group containing the given connection ids, and wire each id into the store.</summary>
    private static GroupTreeItemViewModel BuildGroup(IConnectionStore store, params Guid[] connectionIds)
    {
        var group = new GroupTreeItemViewModel { Id = Guid.NewGuid(), Name = "Group" };
        foreach (var id in connectionIds)
        {
            group.Children.Add(new ConnectionTreeItemViewModel { Id = id, Name = id.ToString() });
            store.GetById(id).Returns(new ConnectionModel { Id = id, Name = id.ToString(), Hostname = "h" });
        }
        return group;
    }

    [Fact]
    public async Task ProjectedCount_EqualsActiveCountPlusGroupConnectionCount()
    {
        // ActiveCount (3) + group.ConnectionCount (2) → projected 5; threshold 100 so it connects.
        var (sut, bus, tab, store, _) = BuildSut(new BulkOperationsRecord(true, 100));
        tab.ActiveCount.Returns(3);
        tab.TryGetExistingTab(Arg.Any<Guid>(), out Arg.Any<IProtocolHost>()).Returns(false);
        var group = BuildGroup(store, Guid.NewGuid(), Guid.NewGuid());

        group.ConnectionCount.Should().Be(2);

        // projected (3 + 2 = 5) ≤ threshold (100) → no confirm → publishes for both descendants.
        await sut.ConnectAllCommand.ExecuteAsync(group);
        bus.Received(2).Publish(Arg.Any<ConnectionRequestedEvent>());
    }

    [Fact]
    public async Task OverThreshold_WithConfirmOn_GatesAllConnects()
    {
        // projected 6 > threshold 5 AND confirm on → the GDI confirm gate stops the connect.
        var (sut, bus, tab, store, _) = BuildSut(new BulkOperationsRecord(true, 5));
        tab.ActiveCount.Returns(5);
        tab.TryGetExistingTab(Arg.Any<Guid>(), out Arg.Any<IProtocolHost>()).Returns(false);
        var group = BuildGroup(store, Guid.NewGuid()); // ConnectionCount 1 → projected 6

        await sut.ConnectAllCommand.ExecuteAsync(group);

        // Confirm gate not satisfiable headless → nothing published.
        bus.DidNotReceive().Publish(Arg.Any<ConnectionRequestedEvent>());
    }

    [Fact]
    public async Task AtOrBelowThreshold_ConnectsImmediately_NoConfirm()
    {
        // projected 3 ≤ threshold 10 → connects immediately, publishes per descendant.
        var (sut, bus, tab, store, _) = BuildSut(new BulkOperationsRecord(true, 10));
        tab.ActiveCount.Returns(0);
        tab.TryGetExistingTab(Arg.Any<Guid>(), out Arg.Any<IProtocolHost>()).Returns(false);
        var group = BuildGroup(store, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await sut.ConnectAllCommand.ExecuteAsync(group);
        bus.Received(3).Publish(Arg.Any<ConnectionRequestedEvent>());
    }

    [Fact]
    public async Task ConfirmBeforeBulkOperationsFalse_ConnectsImmediately_NoConfirm()
    {
        // Confirm OFF, threshold 1, projected far over → still connects immediately (no gate).
        var (sut, bus, tab, store, _) = BuildSut(new BulkOperationsRecord(false, 1));
        tab.ActiveCount.Returns(50);
        tab.TryGetExistingTab(Arg.Any<Guid>(), out Arg.Any<IProtocolHost>()).Returns(false);
        var group = BuildGroup(store, Guid.NewGuid(), Guid.NewGuid());

        await sut.ConnectAllCommand.ExecuteAsync(group);
        bus.Received(2).Publish(Arg.Any<ConnectionRequestedEvent>());
    }

    [Fact]
    public async Task ThresholdBoundary_EqualThreshold_DoesNotWarn_Connects()
    {
        // projected == threshold (5) → does NOT warn → connects for every descendant.
        var (sut, bus, tab, store, _) = BuildSut(new BulkOperationsRecord(true, 5));
        tab.ActiveCount.Returns(2);
        tab.TryGetExistingTab(Arg.Any<Guid>(), out Arg.Any<IProtocolHost>()).Returns(false);
        var group = BuildGroup(store, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()); // count 3 → projected 5

        await sut.ConnectAllCommand.ExecuteAsync(group);
        bus.Received(3).Publish(Arg.Any<ConnectionRequestedEvent>());
    }

    [Fact]
    public async Task ThresholdBoundary_ThresholdPlusOne_Warns_Gated()
    {
        // projected == threshold + 1 (6) AND confirm on → warns; headless gate stops the connect.
        var (sut, bus, tab, store, _) = BuildSut(new BulkOperationsRecord(true, 5));
        tab.ActiveCount.Returns(3);
        tab.TryGetExistingTab(Arg.Any<Guid>(), out Arg.Any<IProtocolHost>()).Returns(false);
        var group = BuildGroup(store, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()); // count 3 → projected 6

        await sut.ConnectAllCommand.ExecuteAsync(group);
        bus.DidNotReceive().Publish(Arg.Any<ConnectionRequestedEvent>());
    }

    [Fact]
    public async Task PublishesConnectionRequestedEvent_PerDescendant()
    {
        var (sut, bus, tab, store, _) = BuildSut(new BulkOperationsRecord(true, 100));
        tab.ActiveCount.Returns(0);
        tab.TryGetExistingTab(Arg.Any<Guid>(), out Arg.Any<IProtocolHost>()).Returns(false);
        var group = BuildGroup(store, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await sut.ConnectAllCommand.ExecuteAsync(group);
        bus.Received(3).Publish(Arg.Any<ConnectionRequestedEvent>());
    }

    [Fact]
    public async Task OpenGroupMembers_NotDoubleCounted_InProjection()
    {
        // W2 (audit): projection must be ActiveCount + toOpen, NOT ActiveCount + ConnectionCount.
        // Group has 3 members, 2 already open. ActiveCount 0, confirm on, threshold 2.
        // Old (buggy) projection: 0 + 3 = 3 > 2 → warn → headless gate blocks everything.
        // Correct projection: 0 + toOpen(1) = 1 ≤ 2 → no warn → opens the 1, switches the 2.
        var (sut, bus, tab, store, _) = BuildSut(new BulkOperationsRecord(true, 2));
        tab.ActiveCount.Returns(0);

        var open1 = Guid.NewGuid();
        var open2 = Guid.NewGuid();
        var closed = Guid.NewGuid();
        var group = BuildGroup(store, open1, open2, closed);
        tab.TryGetExistingTab(open1, out Arg.Any<IProtocolHost>()).Returns(true);
        tab.TryGetExistingTab(open2, out Arg.Any<IProtocolHost>()).Returns(true);
        tab.TryGetExistingTab(closed, out Arg.Any<IProtocolHost>()).Returns(false);

        await sut.ConnectAllCommand.ExecuteAsync(group);

        // The one closed connection is published; the two open are switched to (no double-count gate).
        bus.Received(1).Publish(Arg.Any<ConnectionRequestedEvent>());
        tab.Received(1).SwitchTo(open1);
        tab.Received(1).SwitchTo(open2);
    }

    [Fact]
    public async Task AllMembersAlreadyOpen_SkipsDialog_StillSwitchesTo()
    {
        // W2 (audit): toOpen == 0 → no dialog at all (even with confirm on + everything over
        // threshold), but the switch-to loop still runs. Old behavior double-counted the open
        // members into the projection, tried to show the dialog (null host headless) and returned.
        var (sut, bus, tab, store, _) = BuildSut(new BulkOperationsRecord(true, 1));
        tab.ActiveCount.Returns(50); // huge active count — would trip the old over-threshold gate

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var group = BuildGroup(store, a, b);
        tab.TryGetExistingTab(a, out Arg.Any<IProtocolHost>()).Returns(true);
        tab.TryGetExistingTab(b, out Arg.Any<IProtocolHost>()).Returns(true);

        await sut.ConnectAllCommand.ExecuteAsync(group);

        // No new sessions opened; both existing tabs are switched to (dialog skipped, not blocked).
        bus.DidNotReceive().Publish(Arg.Any<ConnectionRequestedEvent>());
        tab.Received(1).SwitchTo(a);
        tab.Received(1).SwitchTo(b);
    }

    [Fact]
    public async Task SkipsAlreadyOpenTabs_CallsSwitchTo()
    {
        var (sut, bus, tab, store, _) = BuildSut(new BulkOperationsRecord(true, 100));
        tab.ActiveCount.Returns(0);

        var openId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        var group = BuildGroup(store, openId, closedId);

        // Only openId is already open.
        tab.TryGetExistingTab(openId, out Arg.Any<IProtocolHost>()).Returns(true);
        tab.TryGetExistingTab(closedId, out Arg.Any<IProtocolHost>()).Returns(false);

        await sut.ConnectAllCommand.ExecuteAsync(group);

        tab.Received(1).SwitchTo(openId);
        // openId is switched-to, not re-published; only closedId publishes.
        bus.Received(1).Publish(Arg.Any<ConnectionRequestedEvent>());
        bus.DidNotReceive().Publish(Arg.Is<ConnectionRequestedEvent>(e => e.Connection.Id == openId));
    }
}
