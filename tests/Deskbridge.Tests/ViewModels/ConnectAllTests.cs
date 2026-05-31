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
/// BULK-01 — Connect All command behavior contract (Phase 23 — bulk-operations-ux).
///
/// WAVE 0 SCAFFOLD: ConnectAllCommand and its GDI-threshold/projection logic do not exist yet —
/// they are implemented by plan 23-03 on ConnectionTreeViewModel. Every test below is
/// [Fact(Skip = ...)] and pins ONE row of the 23-VALIDATION.md Per-Task Verification Map
/// (BULK-01 rows). Each body documents the intended Arrange/Act/Assert in a `// TODO 23-03:`
/// block plus a trivial Assert so the file COMPILES against existing types today.
///
/// Threshold boundary (23-RESEARCH Pitfall 1, confirm against TabHostManager.cs:368):
///   projected == GdiWarningThreshold → does NOT warn;  projected == threshold + 1 → warns.
/// Projected count = ITabHostManager.ActiveCount + group.ConnectionCount (recursive).
///
/// NOTE ctor change for 23-03: plan 23-03 adds an IWindowStateService parameter to
/// ConnectionTreeViewModel (to reach the GDI threshold via settings.BulkOperations). The
/// BuildSut helper here mirrors the CURRENT verified ctor (SwitchToExistingTabTests); 23-03
/// will extend BuildSut with the IWindowStateService substitute and wire settings.BulkOperations
/// (GdiWarningThreshold) in each Arrange.
/// </summary>
public sealed class ConnectAllTests
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

        // TODO 23-03: add  var windowState = Substitute.For<IWindowStateService>();
        // and configure windowState.LoadAsync() -> settings with BulkOperations(ConfirmBeforeBulkOperations, GdiWarningThreshold).
        var sut = new ConnectionTreeViewModel(
            store, query, creds, dialogs, snackbar, provider, bus, tab,
            new AirspaceSwapper(), new FakeDebouncer());

        return (sut, bus, tab, store);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void ProjectedCount_EqualsActiveCountPlusGroupConnectionCount()
    {
        // TODO 23-03:
        //   var (sut, _, tab, _) = BuildSut();
        //   tab.ActiveCount.Returns(3);
        //   var group = new GroupTreeItemViewModel { Id = Guid.NewGuid() }; // ConnectionCount (recursive) == 4
        //   sut.ProjectedConnectionCount(group).Should().Be(3 + 4);
        Assert.True(true);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void Confirm_ShownOnly_WhenOverThreshold_And_ConfirmBeforeBulkOperationsTrue()
    {
        // TODO 23-03:
        //   settings.BulkOperations = new BulkOperationsRecord(ConfirmBeforeBulkOperations: true, GdiWarningThreshold: 5);
        //   tab.ActiveCount.Returns(5); group.ConnectionCount == 1 → projected 6 > 5 → confirm shown.
        //   sut.ConnectAllCommand.Execute(group);
        //   dialogs.Received(1).ShowSimpleDialogAsync(Arg.Any<...>());  // GDI warning dialog
        Assert.True(true);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void AtOrBelowThreshold_ConnectsImmediately_NoDialog()
    {
        // TODO 23-03:
        //   settings.BulkOperations = new BulkOperationsRecord(true, 10);
        //   tab.ActiveCount.Returns(0); group.ConnectionCount == 3 → projected 3 ≤ 10 → no dialog.
        //   sut.ConnectAllCommand.Execute(group);
        //   dialogs.DidNotReceive().ShowSimpleDialogAsync(Arg.Any<...>());
        Assert.True(true);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void ConfirmBeforeBulkOperationsFalse_ConnectsImmediately_NoDialog()
    {
        // TODO 23-03:
        //   settings.BulkOperations = new BulkOperationsRecord(ConfirmBeforeBulkOperations: false, GdiWarningThreshold: 1);
        //   projected greatly exceeds threshold but confirm is OFF → no dialog, connects immediately.
        //   sut.ConnectAllCommand.Execute(group);
        //   dialogs.DidNotReceive().ShowSimpleDialogAsync(Arg.Any<...>());
        Assert.True(true);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void ThresholdBoundary_EqualThreshold_DoesNotWarn()
    {
        // TODO 23-03: projected == GdiWarningThreshold → NO warn (confirm against TabHostManager.cs:368).
        //   settings.BulkOperations = new BulkOperationsRecord(true, 5);
        //   tab.ActiveCount.Returns(2); group.ConnectionCount == 3 → projected 5 == threshold → no dialog.
        //   sut.ConnectAllCommand.Execute(group);
        //   dialogs.DidNotReceive().ShowSimpleDialogAsync(Arg.Any<...>());
        Assert.True(true);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void ThresholdBoundary_ThresholdPlusOne_Warns()
    {
        // TODO 23-03: projected == threshold + 1 → WARN.
        //   settings.BulkOperations = new BulkOperationsRecord(true, 5);
        //   tab.ActiveCount.Returns(3); group.ConnectionCount == 3 → projected 6 == threshold+1 → dialog shown.
        //   sut.ConnectAllCommand.Execute(group);
        //   dialogs.Received(1).ShowSimpleDialogAsync(Arg.Any<...>());
        Assert.True(true);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void PublishesConnectionRequestedEvent_PerDescendant()
    {
        // TODO 23-03:
        //   var (sut, bus, tab, _) = BuildSut();
        //   tab.TryGetExistingTab(Arg.Any<Guid>(), out Arg.Any<IProtocolHost>()).Returns(false);
        //   group with 3 connection descendants → ConnectAll publishes 3 ConnectionRequestedEvents.
        //   sut.ConnectAllCommand.Execute(group);
        //   bus.Received(3).Publish(Arg.Any<ConnectionRequestedEvent>());
        Assert.True(true);
    }

    [Fact(Skip = "Wave 2: implemented by 23-03 (ConnectionTreeViewModel commands)")]
    public void SkipsAlreadyOpenTabs_CallsSwitchTo()
    {
        // TODO 23-03: one descendant already open → SwitchTo instead of a second ConnectionRequestedEvent.
        //   tab.TryGetExistingTab(openId, out Arg.Any<IProtocolHost>()).Returns(ci => { ci[1] = host; return true; });
        //   sut.ConnectAllCommand.Execute(group);
        //   tab.Received(1).SwitchTo(openId);
        //   bus.DidNotReceive().Publish(Arg.Is<ConnectionRequestedEvent>(e => e.Connection.Id == openId));
        Assert.True(true);
    }
}
