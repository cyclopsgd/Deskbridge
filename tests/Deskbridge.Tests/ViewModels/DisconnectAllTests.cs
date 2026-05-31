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
/// BULK-02 — Disconnect All command behavior contract (Phase 23 — bulk-operations-ux).
///
/// DisconnectAllCommand calls <see cref="ITabHostManager.CloseTabAsync"/> for every descendant that
/// currently has an active tab; none for inactive descendants. No confirmation, no persistence, no
/// tree refresh. The menu item is enabled iff the group has ≥1 active session
/// (<see cref="ConnectionTreeViewModel.GroupHasActiveSessions"/>).
/// </summary>
public sealed class DisconnectAllTests
{
    private static (
        ConnectionTreeViewModel sut,
        IEventBus bus,
        ITabHostManager tab,
        IConnectionStore store
    ) BuildSut()
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
        windowState.LoadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new AppSettings()));

        var sut = new ConnectionTreeViewModel(
            store, query, creds, dialogs, snackbar, provider, bus, tab,
            new AirspaceSwapper(), new FakeDebouncer(), windowState);

        return (sut, bus, tab, store);
    }

    private static GroupTreeItemViewModel BuildGroup(params Guid[] connectionIds)
    {
        var group = new GroupTreeItemViewModel { Id = Guid.NewGuid(), Name = "Group" };
        foreach (var id in connectionIds)
            group.Children.Add(new ConnectionTreeItemViewModel { Id = id, Name = id.ToString() });
        return group;
    }

    [Fact]
    public async Task CallsCloseTabAsync_ForEveryActiveDescendant()
    {
        var (sut, _, tab, _) = BuildSut();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var group = BuildGroup(a, b, c);

        tab.TryGetExistingTab(Arg.Any<Guid>(), out Arg.Any<IProtocolHost>()).Returns(true);

        await sut.DisconnectAllCommand.ExecuteAsync(group);

        await tab.Received(1).CloseTabAsync(a);
        await tab.Received(1).CloseTabAsync(b);
        await tab.Received(1).CloseTabAsync(c);
    }

    [Fact]
    public async Task DoesNotCloseTabAsync_ForInactiveDescendants()
    {
        var (sut, _, tab, _) = BuildSut();
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var group = BuildGroup(activeId, inactiveId);

        tab.TryGetExistingTab(activeId, out Arg.Any<IProtocolHost>()).Returns(true);
        tab.TryGetExistingTab(inactiveId, out Arg.Any<IProtocolHost>()).Returns(false);

        await sut.DisconnectAllCommand.ExecuteAsync(group);

        await tab.Received(1).CloseTabAsync(activeId);
        await tab.DidNotReceive().CloseTabAsync(inactiveId);
    }

    [Fact]
    public void GroupHasActiveSessions_TrueWhenAtLeastOneActive_FalseWhenNone()
    {
        var (sut, _, tab, _) = BuildSut();
        var oneId = Guid.NewGuid();
        var group = BuildGroup(oneId, Guid.NewGuid());

        // No descendant active → false.
        tab.TryGetExistingTab(Arg.Any<Guid>(), out Arg.Any<IProtocolHost>()).Returns(false);
        sut.GroupHasActiveSessions(group).Should().BeFalse();

        // One descendant active → true.
        tab.TryGetExistingTab(oneId, out Arg.Any<IProtocolHost>()).Returns(true);
        sut.GroupHasActiveSessions(group).Should().BeTrue();
    }
}
