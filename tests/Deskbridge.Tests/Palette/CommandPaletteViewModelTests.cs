using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using Deskbridge.ViewModels;
using Wpf.Ui.Controls;

namespace Deskbridge.Tests.Palette;

/// <summary>
/// Phase 6 Plan 06-03 Task 2 (CMD-01 / CMD-03 / D-02): verifies
/// <see cref="CommandPaletteViewModel"/> empty-state + ranked-merge behaviour and
/// (most importantly) that <see cref="IConnectionQuery.Search"/> is called EXACTLY
/// ONCE per SearchText change (CMD-03 — no duplication of the Phase 3 scorer).
/// </summary>
public sealed class CommandPaletteViewModelTests
{
    // ------------------------------------------------------------------ helpers

    private static CommandPaletteService RealPalette() =>
        new(
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);

    private static ConnectionModel Conn(string name, string host, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Hostname = host,
        };

    // ------------------------------------------------------------------ tests

    [Fact]
    public void EmptySearch_ShowsRecentsThenCommands_RecentsFirst()
    {
        var recents = new[]
        {
            Conn("r1", "r1.example"),
            Conn("r2", "r2.example"),
            Conn("r3", "r3.example"),
        };
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(5).Returns(recents);
        var palette = RealPalette();
        var bus = Substitute.For<IEventBus>();

        var vm = new CommandPaletteViewModel(query, palette, bus);

        vm.Items.Should().HaveCount(3 + 4);
        vm.Items.Take(3).Should().OnlyContain(r => !r.IsCommand);
        vm.Items.Skip(3).Should().OnlyContain(r => r.IsCommand);
    }

    [Fact]
    public void EmptySearch_WithZeroRecents_ShowsJustCommands()
    {
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(5).Returns(Array.Empty<ConnectionModel>());
        var vm = new CommandPaletteViewModel(query, RealPalette(), Substitute.For<IEventBus>());

        vm.Items.Should().HaveCount(4);
        vm.Items.Should().OnlyContain(r => r.IsCommand);
    }

    [Fact]
    public void NonEmptySearch_CallsIConnectionQuerySearch_ExactlyOnce_PerKeystroke()
    {
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(Arg.Any<int>()).Returns(Array.Empty<ConnectionModel>());
        query.Search("srv").Returns(Array.Empty<ConnectionModel>());

        var vm = new CommandPaletteViewModel(query, RealPalette(), Substitute.For<IEventBus>());

        vm.SearchText = "srv";

        query.Received(1).Search("srv");
    }

    [Fact]
    public void NonEmptySearch_DoesNotCall_GetRecent()
    {
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(Arg.Any<int>()).Returns(Array.Empty<ConnectionModel>());
        query.Search(Arg.Any<string>()).Returns(Array.Empty<ConnectionModel>());

        var vm = new CommandPaletteViewModel(query, RealPalette(), Substitute.For<IEventBus>());
        query.ClearReceivedCalls();

        vm.SearchText = "x";

        query.DidNotReceive().GetRecent(Arg.Any<int>());
        query.Received(1).Search("x");
    }

    [Fact]
    public void NonEmptySearch_Merges_CommandsBeforeConnections_OnTie()
    {
        // Two connections, one command matching "new" — command scores 100 (Title exact
        // substring), top connection also maps to 100 (100-0 index). Command wins the
        // tiebreak by IsCommand=true → 0 sort key.
        var connections = new[]
        {
            Conn("new-srv01", "hostA"),
            Conn("news-box", "hostB"),
        };
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(Arg.Any<int>()).Returns(Array.Empty<ConnectionModel>());
        query.Search("new").Returns(connections);

        var vm = new CommandPaletteViewModel(query, RealPalette(), Substitute.For<IEventBus>());

        vm.SearchText = "new";

        vm.Items[0].IsCommand.Should().BeTrue();
        vm.Items[0].Title.Should().Be("New Connection");
        vm.Items.Skip(1).Should().OnlyContain(r => !r.IsCommand);
    }

    [Fact]
    public void NonEmptySearch_ConnectionRows_PreserveSearchOrder()
    {
        var connections = new[]
        {
            Conn("alpha", "alpha.host"),
            Conn("bravo", "bravo.host"),
            Conn("charlie", "charlie.host"),
        };
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(Arg.Any<int>()).Returns(Array.Empty<ConnectionModel>());
        query.Search("a").Returns(connections);

        var vm = new CommandPaletteViewModel(query, RealPalette(), Substitute.For<IEventBus>());

        vm.SearchText = "a";

        // No command matches "a" (new/settings/disconnect all/quick - none contain "a"
        // as a substring of alias or title... actually "a" IS a substring of "Add" alias
        // and "disconnect all" alias and "New Connection" title) — so filter only
        // connection rows and check they're in the same order Search returned.
        var connectionRows = vm.Items.Where(r => !r.IsCommand).ToArray();
        connectionRows.Should().HaveCount(3);
        connectionRows[0].Title.Should().Be("alpha");
        connectionRows[1].Title.Should().Be("bravo");
        connectionRows[2].Title.Should().Be("charlie");
    }

    [Fact]
    public void SelectedItem_AutoSetsToFirst_AfterEveryRefresh()
    {
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(Arg.Any<int>()).Returns(Array.Empty<ConnectionModel>());
        query.Search(Arg.Any<string>()).Returns(Array.Empty<ConnectionModel>());

        var vm = new CommandPaletteViewModel(query, RealPalette(), Substitute.For<IEventBus>());

        // Initial empty state — first is a command row.
        vm.SelectedItem.Should().NotBeNull();
        vm.SelectedItem!.IsCommand.Should().BeTrue();

        // Search changes — SelectedItem tracks Items[0].
        vm.SearchText = "new";
        vm.SelectedItem.Should().BeSameAs(vm.Items.FirstOrDefault());
    }

    [Fact]
    public async Task ExecuteSelectedAsync_ConnectionRow_PublishesConnectionRequestedEvent()
    {
        var conn = Conn("srv01", "srv01.example");
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(Arg.Any<int>()).Returns(new[] { conn });
        var bus = Substitute.For<IEventBus>();

        var vm = new CommandPaletteViewModel(query, RealPalette(), bus);

        // Select the connection row (first in Items — empty-state recents come first).
        var connRow = vm.Items.First(r => !r.IsCommand);
        vm.SelectedItem = connRow;

        await vm.ExecuteSelectedAsync();

        bus.Received(1).Publish(Arg.Is<ConnectionRequestedEvent>(e => e.Connection.Id == conn.Id));
    }

    [Fact]
    public async Task ExecuteSelectedAsync_CommandRow_InvokesCommandDelegate()
    {
        var invoked = false;
        var palette = new CommandPaletteService(
            newConnection: () => { invoked = true; return Task.CompletedTask; },
            openSettings: () => Task.CompletedTask,
            disconnectAll: () => Task.CompletedTask,
            quickConnect: () => Task.CompletedTask);
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(Arg.Any<int>()).Returns(Array.Empty<ConnectionModel>());

        var vm = new CommandPaletteViewModel(query, palette, Substitute.For<IEventBus>());

        var newConnRow = vm.Items.Single(r => r.IsCommand && r.CommandId == "new-connection");
        vm.SelectedItem = newConnRow;

        await vm.ExecuteSelectedAsync();

        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteSelectedAsync_NullSelection_IsNoOp_DoesNotThrow()
    {
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(Arg.Any<int>()).Returns(Array.Empty<ConnectionModel>());

        var vm = new CommandPaletteViewModel(query, RealPalette(), Substitute.For<IEventBus>());
        vm.SelectedItem = null;

        var act = () => vm.ExecuteSelectedAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void ConnectionRow_Subtitle_HiddenWhen_TitleEqualsHostname()
    {
        // UI-SPEC §Row anatomy: Subtitle omitted when they match (prevents "srv01 / srv01").
        var conn = new ConnectionModel { Id = Guid.NewGuid(), Name = "srv01", Hostname = "srv01" };
        var bus = Substitute.For<IEventBus>();

        var row = new CommandPaletteRowViewModel(conn, 0, bus);

        row.Title.Should().Be("srv01");
        row.Subtitle.Should().BeNull();
    }

    [Fact]
    public void ConnectionRow_Subtitle_VisibleWhen_TitleDiffersFromHostname()
    {
        var conn = new ConnectionModel { Id = Guid.NewGuid(), Name = "Production", Hostname = "prod.example.com" };
        var bus = Substitute.For<IEventBus>();

        var row = new CommandPaletteRowViewModel(conn, 0, bus);

        row.Title.Should().Be("Production");
        row.Subtitle.Should().Be("prod.example.com");
    }

    [Fact]
    public void ConnectionRow_Uses_Desktop24_Icon()
    {
        var conn = Conn("srv", "hostA");
        var row = new CommandPaletteRowViewModel(conn, 0, Substitute.For<IEventBus>());

        row.Icon.Should().Be(SymbolRegular.Desktop24);
        row.Shortcut.Should().BeNull();
        row.IsCommand.Should().BeFalse();
    }

    [Fact]
    public void CommandRow_CopiesIconAndShortcut_FromCommandEntry()
    {
        var palette = RealPalette();
        var cmd = palette.Commands.Single(c => c.Id == "new-connection");

        var row = new CommandPaletteRowViewModel(cmd, 100);

        row.Title.Should().Be("New Connection");
        row.Icon.Should().Be(SymbolRegular.Add24);
        row.Shortcut.Should().Be("Ctrl+N");
        row.IsCommand.Should().BeTrue();
        row.CommandId.Should().Be("new-connection");
    }

    [Fact]
    public void SubsequentSearchChange_CallsSearchAgain()
    {
        // Defense-in-depth: CMD-03 guarantee holds across multiple keystrokes, not just the first.
        var query = Substitute.For<IConnectionQuery>();
        query.GetRecent(Arg.Any<int>()).Returns(Array.Empty<ConnectionModel>());
        query.Search(Arg.Any<string>()).Returns(Array.Empty<ConnectionModel>());

        var vm = new CommandPaletteViewModel(query, RealPalette(), Substitute.For<IEventBus>());

        vm.SearchText = "s";
        vm.SearchText = "sr";
        vm.SearchText = "srv";

        query.Received(1).Search("s");
        query.Received(1).Search("sr");
        query.Received(1).Search("srv");
    }
}
