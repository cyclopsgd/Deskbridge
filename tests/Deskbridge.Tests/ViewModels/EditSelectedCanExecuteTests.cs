using Deskbridge.Core.Interfaces;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Tests.Fakes;
using Deskbridge.ViewModels;
using FluentAssertions;
using NSubstitute;
using Wpf.Ui;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// W3 (audit) — the multi-select "Edit…" command is only executable for a selection of ≥2
/// connections. A lone connection, two groups, or a group+connection mix must report
/// CanExecute==false so the menu item greys out instead of silently no-opping.
/// </summary>
public sealed class EditSelectedCanExecuteTests
{
    private static ConnectionTreeViewModel BuildSut()
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

        return new ConnectionTreeViewModel(
            store, query, creds, dialogs, snackbar, provider, bus, tab,
            new AirspaceSwapper(), new FakeDebouncer(), windowState);
    }

    private static ConnectionTreeItemViewModel Conn() =>
        new() { Id = Guid.NewGuid(), Name = "conn" };

    private static GroupTreeItemViewModel Group() =>
        new() { Id = Guid.NewGuid(), Name = "group" };

    [Fact]
    public void CanExecute_False_ForEmptySelection()
    {
        var sut = BuildSut();
        sut.EditSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CanExecute_False_ForSingleConnection()
    {
        var sut = BuildSut();
        sut.SelectedItems.Add(Conn());
        sut.EditSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CanExecute_False_ForGroupPlusConnection()
    {
        var sut = BuildSut();
        sut.SelectedItems.Add(Group());
        sut.SelectedItems.Add(Conn());
        sut.EditSelectedCommand.CanExecute(null).Should().BeFalse("only one selected item is a connection");
    }

    [Fact]
    public void CanExecute_False_ForTwoGroups()
    {
        var sut = BuildSut();
        sut.SelectedItems.Add(Group());
        sut.SelectedItems.Add(Group());
        sut.EditSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CanExecute_True_ForTwoConnections()
    {
        var sut = BuildSut();
        sut.SelectedItems.Add(Conn());
        sut.SelectedItems.Add(Conn());
        sut.EditSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CanExecuteChanged_Raised_WhenSelectionChanges()
    {
        var sut = BuildSut();
        var raised = 0;
        sut.EditSelectedCommand.CanExecuteChanged += (_, _) => raised++;

        sut.SelectedItems.Add(Conn());
        sut.SelectedItems.Add(Conn());

        raised.Should().BeGreaterThan(0, "the command must re-evaluate CanExecute as the selection changes");
        sut.EditSelectedCommand.CanExecute(null).Should().BeTrue();
    }
}
