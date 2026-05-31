using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Tests.Fakes;
using Deskbridge.ViewModels;
using NSubstitute;
using Wpf.Ui;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// BULK-02 — Disconnect All command behavior contract (Phase 23 — bulk-operations-ux).
///
/// WAVE 0 SCAFFOLD: DisconnectAllCommand and GroupHasActiveSessions do not exist yet — they are
/// implemented by plan 23-03 on ConnectionTreeViewModel. Every test below is [Fact(Skip = ...)]
/// and pins ONE row of the 23-VALIDATION.md Per-Task Verification Map (BULK-02 rows). Each body
/// documents the intended Arrange/Act/Assert in a `// TODO 23-03:` block plus a trivial Assert
/// so the file COMPILES against existing types today.
///
/// Disconnect All calls ITabHostManager.CloseTabAsync(Guid) for every descendant that currently
/// has an active tab; none for inactive descendants. The menu item / command is enabled iff the
/// group has ≥1 active session (GroupHasActiveSessions, evaluated against tab.TryGetExistingTab /
/// active-tab tracking).
///
/// NOTE ctor change for 23-03: as with ConnectAllTests, plan 23-03 adds an IWindowStateService
/// parameter to ConnectionTreeViewModel; BuildSut here mirrors the CURRENT verified ctor and 23-03
/// will extend it.
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

        // TODO 23-03: add IWindowStateService substitute (parity with ConnectAllTests.BuildSut).
        var sut = new ConnectionTreeViewModel(
            store, query, creds, dialogs, snackbar, provider, bus, tab,
            new AirspaceSwapper(), new FakeDebouncer());

        return (sut, bus, tab, store);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void CallsCloseTabAsync_ForEveryActiveDescendant()
    {
        // TODO 23-03:
        //   var (sut, _, tab, _) = BuildSut();
        //   group with 3 connection descendants; tab.TryGetExistingTab returns true for all 3.
        //   sut.DisconnectAllCommand.Execute(group);
        //   tab.Received(1).CloseTabAsync(descendant1.Id);
        //   tab.Received(1).CloseTabAsync(descendant2.Id);
        //   tab.Received(1).CloseTabAsync(descendant3.Id);
        Assert.True(true);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void DoesNotCloseTabAsync_ForInactiveDescendants()
    {
        // TODO 23-03:
        //   group with mixed descendants; tab.TryGetExistingTab returns true for activeId only.
        //   sut.DisconnectAllCommand.Execute(group);
        //   tab.Received(1).CloseTabAsync(activeId);
        //   tab.DidNotReceive().CloseTabAsync(inactiveId);
        Assert.True(true);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void GroupHasActiveSessions_TrueWhenAtLeastOneActive_FalseWhenNone()
    {
        // TODO 23-03:
        //   var (sut, _, tab, _) = BuildSut();
        //   No descendant active → sut.GroupHasActiveSessions(group).Should().BeFalse();
        //   tab.TryGetExistingTab(oneId, out _) returns true → GroupHasActiveSessions(group).Should().BeTrue();
        Assert.True(true);
    }
}
