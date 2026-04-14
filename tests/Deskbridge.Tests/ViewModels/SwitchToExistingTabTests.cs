using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.ViewModels;
using Wpf.Ui;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// Publisher-side D-02 chokepoint coverage. ConnectionTreeViewModel.Connect must check
/// ITabHostManager.TryGetExistingTab BEFORE publishing ConnectionRequestedEvent; if a
/// tab is already open it switches instead of opening a second one.
/// </summary>
public sealed class SwitchToExistingTabTests
{
    private static (
        ConnectionTreeViewModel sut,
        IEventBus bus,
        ITabHostManager tab,
        IConnectionStore store
    ) BuildSut(ConnectionModel? model = null)
    {
        var store = Substitute.For<IConnectionStore>();
        var query = Substitute.For<IConnectionQuery>();
        var creds = Substitute.For<ICredentialService>();
        var dialogs = Substitute.For<IContentDialogService>();
        var snackbar = Substitute.For<ISnackbarService>();
        var provider = Substitute.For<IServiceProvider>();
        var bus = Substitute.For<IEventBus>();
        var tab = Substitute.For<ITabHostManager>();

        model ??= new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        store.GetById(model.Id).Returns(model);

        var sut = new ConnectionTreeViewModel(
            store, query, creds, dialogs, snackbar, provider, bus, tab);

        return (sut, bus, tab, store);
    }

    [Fact]
    public void ConnectCommand_PublishesConnectionRequestedEvent_WhenNoExistingTab()
    {
        var model = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var (sut, bus, tab, _) = BuildSut(model);
        tab.TryGetExistingTab(model.Id, out Arg.Any<IProtocolHost>()).Returns(false);

        var item = new ConnectionTreeItemViewModel { Id = model.Id };
        sut.ConnectCommand.Execute(item);

        bus.Received(1).Publish(Arg.Is<ConnectionRequestedEvent>(e => e.Connection == model));
        tab.DidNotReceive().SwitchTo(Arg.Any<Guid>());
    }

    [Fact]
    public void ConnectCommand_SwitchesExistingTab_WhenAlreadyOpen()
    {
        var model = new ConnectionModel { Hostname = "h", Protocol = Protocol.Rdp };
        var (sut, bus, tab, _) = BuildSut(model);
        var host = Substitute.For<IProtocolHost>();
        tab.TryGetExistingTab(model.Id, out Arg.Any<IProtocolHost>()).Returns(ci =>
        {
            ci[1] = host;
            return true;
        });

        var item = new ConnectionTreeItemViewModel { Id = model.Id };
        sut.ConnectCommand.Execute(item);

        tab.Received(1).SwitchTo(model.Id);
        bus.DidNotReceive().Publish(Arg.Any<ConnectionRequestedEvent>());
    }
}
