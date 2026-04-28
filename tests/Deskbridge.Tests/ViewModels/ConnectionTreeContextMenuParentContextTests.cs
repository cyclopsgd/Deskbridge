using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Services;
using Deskbridge.Protocols.Rdp;
using Deskbridge.ViewModels;
using Wpf.Ui;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// Locks the right-click context-menu parent-context contract (quick task 260428-q3k).
/// Tests 1-3 must FAIL on the pre-fix XAML; Tests 4-5 protect already-correct behaviour
/// from regression; Test 6 locks the <c>NewGroupCommand</c> parameter shape so a future
/// refactor cannot silently drop the optional <c>Guid?</c> parameter.
///
/// <para>Pattern A: XAML text parsing (mirrors <see cref="MainWindowXamlContextMenuTests"/>).
/// Reads the XAML from disk and slices it by ContextMenu key so assertions only look at
/// the menu under test.</para>
///
/// <para>Pattern B: Reflection on the source-generated command (mirrors
/// <see cref="ConnectionTreeStateTrackingTests"/> SUT wiring).</para>
/// </summary>
public sealed class ConnectionTreeContextMenuParentContextTests
{
    private static string ReadConnectionTreeXaml()
    {
        var xamlPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../src/Deskbridge/Views/ConnectionTreeControl.xaml"));
        File.Exists(xamlPath).Should().BeTrue("ConnectionTreeControl.xaml must exist on disk");
        return File.ReadAllText(xamlPath);
    }

    /// <summary>
    /// Slices the ContextMenu identified by <paramref name="key"/>. The slice ends at
    /// the next <c>x:Key="..."</c> sentinel (or the closing of the resources section)
    /// so each menu's MenuItems are isolated from neighbours.
    /// </summary>
    private static string SliceMenuByKey(string xaml, string key, string? nextKey)
    {
        var startMarker = $"x:Key=\"{key}\"";
        var startIdx = xaml.IndexOf(startMarker, StringComparison.Ordinal);
        startIdx.Should().BePositive($"ContextMenu with x:Key=\"{key}\" must exist");

        int endIdx;
        if (nextKey is not null)
        {
            var endMarker = $"x:Key=\"{nextKey}\"";
            endIdx = xaml.IndexOf(endMarker, startIdx, StringComparison.Ordinal);
            endIdx.Should().BePositive($"sentinel x:Key=\"{nextKey}\" must follow {key}");
        }
        else
        {
            // Fallback: terminate at the close of the UserControl.Resources block.
            endIdx = xaml.IndexOf("</UserControl.Resources>", startIdx, StringComparison.Ordinal);
            endIdx.Should().BePositive("UserControl.Resources must close after the last menu");
        }

        return xaml.Substring(startIdx, endIdx - startIdx);
    }

    /// <summary>
    /// Slices a single MenuItem self-closing element starting at <c>Header="..."</c>
    /// and ending at the next <c>/&gt;</c>. Returns null if no MenuItem with that
    /// header exists in <paramref name="block"/>.
    /// </summary>
    private static string? SliceMenuItemByHeader(string block, string header)
    {
        var marker = $"Header=\"{header}\"";
        var headerIdx = block.IndexOf(marker, StringComparison.Ordinal);
        if (headerIdx < 0) return null;

        // Walk back to the opening "<MenuItem" so the slice covers the whole element.
        var openIdx = block.LastIndexOf("<MenuItem", headerIdx, StringComparison.Ordinal);
        if (openIdx < 0) return null;

        var closeIdx = block.IndexOf("/>", headerIdx, StringComparison.Ordinal);
        if (closeIdx < 0) return null;

        return block.Substring(openIdx, (closeIdx + 2) - openIdx);
    }

    // ── Regex patterns (whitespace-tolerant) ──

    private static readonly Regex CommandParameterGroupId = new(
        @"CommandParameter\s*=\s*""\{Binding\s+PlacementTarget\.DataContext\.GroupId,\s*RelativeSource=\{RelativeSource\s+AncestorType=\{x:Type\s+ContextMenu\}\}\}""",
        RegexOptions.Singleline);

    private static readonly Regex CommandParameterId = new(
        @"CommandParameter\s*=\s*""\{Binding\s+PlacementTarget\.DataContext\.Id,\s*RelativeSource=\{RelativeSource\s+AncestorType=\{x:Type\s+ContextMenu\}\}\}""",
        RegexOptions.Singleline);

    // ── Tests 1-3: failing-first XAML CommandParameter binding contract ──

    [Fact]
    public void ConnectionContextMenu_NewConnectionMenuItem_BindsCommandParameterToGroupId()
    {
        var xaml = ReadConnectionTreeXaml();
        var block = SliceMenuByKey(xaml, "ConnectionContextMenu", "GroupContextMenu");

        var item = SliceMenuItemByHeader(block, "New Connection");
        item.Should().NotBeNull("ConnectionContextMenu must contain a 'New Connection' MenuItem");

        CommandParameterGroupId.IsMatch(item!).Should().BeTrue(
            "Right-clicking a connection + New Connection must create a sibling " +
            "(same GroupId as the right-clicked connection). Bind " +
            "CommandParameter to PlacementTarget.DataContext.GroupId via the " +
            "ContextMenu RelativeSource pattern.");
    }

    [Fact]
    public void ConnectionContextMenu_NewGroupMenuItem_BindsCommandParameterToGroupId()
    {
        var xaml = ReadConnectionTreeXaml();
        var block = SliceMenuByKey(xaml, "ConnectionContextMenu", "GroupContextMenu");

        var item = SliceMenuItemByHeader(block, "New Group");
        item.Should().NotBeNull("ConnectionContextMenu must contain a 'New Group' MenuItem");

        CommandParameterGroupId.IsMatch(item!).Should().BeTrue(
            "Right-clicking a connection + New Group must create a sibling group " +
            "(ParentGroupId = the connection's GroupId). Bind CommandParameter to " +
            "PlacementTarget.DataContext.GroupId via the ContextMenu RelativeSource pattern.");
    }

    [Fact]
    public void GroupContextMenu_NewGroupMenuItem_BindsCommandParameterToId()
    {
        var xaml = ReadConnectionTreeXaml();
        var block = SliceMenuByKey(xaml, "GroupContextMenu", "MultiSelectContextMenu");

        var item = SliceMenuItemByHeader(block, "New Group");
        item.Should().NotBeNull("GroupContextMenu must contain a 'New Group' MenuItem");

        CommandParameterId.IsMatch(item!).Should().BeTrue(
            "Right-clicking a group + New Group must create a child group " +
            "(ParentGroupId = the right-clicked group's Id). Bind CommandParameter " +
            "to PlacementTarget.DataContext.Id via the ContextMenu RelativeSource pattern.");
    }

    // ── Test 4: non-regression on the already-correct line-67 binding ──

    [Fact]
    public void GroupContextMenu_NewConnectionMenuItem_StillBindsCommandParameterToId_NoRegression()
    {
        var xaml = ReadConnectionTreeXaml();
        var block = SliceMenuByKey(xaml, "GroupContextMenu", "MultiSelectContextMenu");

        var item = SliceMenuItemByHeader(block, "New Connection");
        item.Should().NotBeNull("GroupContextMenu must contain a 'New Connection' MenuItem");

        CommandParameterId.IsMatch(item!).Should().BeTrue(
            "Pre-existing binding (line ~67): right-clicking a group + New Connection " +
            "must remain bound to PlacementTarget.DataContext.Id so connections land " +
            "as children of the right-clicked group.");
    }

    // ── Test 5: non-regression on EmptyAreaContextMenu (must NOT pass any parameter) ──

    [Fact]
    public void EmptyAreaContextMenu_NewConnectionAndNewGroup_HaveNoCommandParameter_NoRegression()
    {
        var xaml = ReadConnectionTreeXaml();
        // EmptyAreaContextMenu is the last menu in the resources block — pass null
        // so SliceMenuByKey terminates at </UserControl.Resources>.
        var block = SliceMenuByKey(xaml, "EmptyAreaContextMenu", nextKey: null);

        var newConnection = SliceMenuItemByHeader(block, "New Connection");
        newConnection.Should().NotBeNull("EmptyAreaContextMenu must contain 'New Connection'");
        newConnection!.Should().NotContain("CommandParameter=",
            "Empty-area New Connection must create at root — no CommandParameter " +
            "binding so NewConnectionAsync(Guid? defaultGroupId = null) receives null.");

        var newGroup = SliceMenuItemByHeader(block, "New Group");
        newGroup.Should().NotBeNull("EmptyAreaContextMenu must contain 'New Group'");
        newGroup!.Should().NotContain("CommandParameter=",
            "Empty-area New Group must create at root — no CommandParameter binding " +
            "so NewGroupAsync(Guid? parentGroupId = null) receives null.");
    }

    // ── Test 6: failing-first command parameter shape (RED until VM is updated) ──

    [Fact]
    public void NewGroupCommand_AcceptsOptionalGuidParameter()
    {
        // Constructor wiring mirrors ConnectionTreeStateTrackingTests verbatim.
        var connectionStore = Substitute.For<IConnectionStore>();
        var connectionQuery = Substitute.For<IConnectionQuery>();
        var credentialService = Substitute.For<ICredentialService>();
        var contentDialogService = Substitute.For<IContentDialogService>();
        var snackbarService = Substitute.For<ISnackbarService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var bus = new EventBus();
        var tabHostManager = Substitute.For<ITabHostManager>();

        var sut = new ConnectionTreeViewModel(
            connectionStore, connectionQuery, credentialService,
            contentDialogService, snackbarService, serviceProvider,
            bus, tabHostManager, new AirspaceSwapper());

        sut.NewGroupCommand.Should().BeAssignableTo<IRelayCommand<Guid?>>(
            "NewGroupAsync must accept Guid? parentGroupId so the source-generated " +
            "command exposes the typed parameter shape required by the XAML " +
            "CommandParameter binding (mirrors NewConnectionAsync(Guid? defaultGroupId)).");
    }
}
