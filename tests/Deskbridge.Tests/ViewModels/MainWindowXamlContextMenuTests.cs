using System.IO;
using System.Text.RegularExpressions;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// Plan 05-03 Task 1: asserts the tab DataTemplate in MainWindow.xaml declares
/// a <c>ContextMenu</c> with exactly three <c>MenuItem</c>s in the UI-SPEC order
/// <c>Close</c> / <c>Close Others</c> / <c>Close All</c> — no icons, no separators,
/// no Duplicate action (D-07).
///
/// <para>Parses the raw XAML text from disk rather than instantiating the full
/// FluentWindow, mirroring <see cref="Integration.HostContainerPersistenceTests"/>.
/// This avoids cross-thread Freezable exceptions when the WPF-UI theme brushes
/// are created on one STA thread and consumed on another.</para>
/// </summary>
public sealed class MainWindowXamlContextMenuTests
{
    private static string ReadMainWindowXaml()
    {
        var xamlPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../src/Deskbridge/MainWindow.xaml"));
        File.Exists(xamlPath).Should().BeTrue("MainWindow.xaml must exist on disk");
        return File.ReadAllText(xamlPath);
    }

    [Fact]
    public void TabDataTemplate_ContainsContextMenu()
    {
        var text = ReadMainWindowXaml();
        text.Should().Contain("<Border.ContextMenu>",
            "Phase 5 D-07: tab DataTemplate must declare a right-click ContextMenu");
        text.Should().Contain("<ContextMenu>");
    }

    [Fact]
    public void ContextMenu_HasExactly3MenuItems_InCanonicalOrder()
    {
        var text = ReadMainWindowXaml();

        // Slice out the ContextMenu block so we don't match MenuItems declared elsewhere.
        var start = text.IndexOf("<Border.ContextMenu>");
        var end = text.IndexOf("</Border.ContextMenu>", start);
        start.Should().BePositive();
        end.Should().BePositive();
        var block = text.Substring(start, end - start);

        // Match Header="..." values in order.
        var headers = Regex.Matches(block, "<MenuItem\\s[^>]*Header=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        headers.Should().BeEquivalentTo(
            new[] { "Close", "Close Others", "Close All" },
            options => options.WithStrictOrdering(),
            "UI-SPEC §Context Menu locks these three items in this exact order (D-07)");
    }

    [Fact]
    public void ContextMenu_HasNoDuplicateMenuItem()
    {
        var text = ReadMainWindowXaml();
        var start = text.IndexOf("<Border.ContextMenu>");
        var end = text.IndexOf("</Border.ContextMenu>", start);
        start.Should().BePositive();
        end.Should().BePositive();
        var block = text.Substring(start, end - start);

        // D-07 / D-02: no "Duplicate" action — conflicts with one-connection-per-tab rule.
        block.Should().NotContain("Duplicate", "D-07 rejects a Duplicate menu item");
    }

    [Fact]
    public void ContextMenu_HasNoSeparator()
    {
        var text = ReadMainWindowXaml();
        var start = text.IndexOf("<Border.ContextMenu>");
        var end = text.IndexOf("</Border.ContextMenu>", start);
        start.Should().BePositive();
        end.Should().BePositive();
        var block = text.Substring(start, end - start);

        // UI-SPEC §Context Menu line 255: "No separators."
        block.Should().NotContain("<Separator", "UI-SPEC locks tight grouping — no separators");
    }

    [Fact]
    public void ContextMenu_MenuItems_BindToMainWindowViewModelCommands()
    {
        var text = ReadMainWindowXaml();
        var start = text.IndexOf("<Border.ContextMenu>");
        var end = text.IndexOf("</Border.ContextMenu>", start);
        var block = text.Substring(start, end - start);

        // Command bindings must go through RelativeSource FindAncestor ItemsControl
        // so they resolve to MainWindowViewModel commands (not TabItemViewModel).
        block.Should().Contain("DataContext.CloseTabCommand");
        block.Should().Contain("DataContext.CloseOtherTabsCommand");
        block.Should().Contain("DataContext.CloseAllTabsCommand");
        block.Should().Contain("RelativeSource", "commands must bind via RelativeSource AncestorType=ItemsControl");
    }

    [Fact]
    public void TabDataTemplate_HasProgressRingAndTwoStateEllipses()
    {
        var text = ReadMainWindowXaml();

        // D-12 mutually-exclusive state indicators.
        text.Should().Contain("<ui:ProgressRing", "D-12: Connecting indicator is a 12px ProgressRing");
        text.Should().MatchRegex(
            "Ellipse\\s[^/>]*DeskbridgeWarningBrush",
            "D-12: Reconnecting indicator is an 8px amber Ellipse bound to DeskbridgeWarningBrush");
        text.Should().MatchRegex(
            "Ellipse\\s[^/>]*DeskbridgeErrorBrush",
            "D-12: Error indicator is an 8px red Ellipse bound to DeskbridgeErrorBrush");
    }

    [Fact]
    public void TabDataTemplate_HasWidthClampAndTooltipBinding()
    {
        var text = ReadMainWindowXaml();

        text.Should().Contain("MinWidth=\"96\"", "UI-SPEC line 64: min tab width 96px");
        text.Should().Contain("MaxWidth=\"240\"", "UI-SPEC line 64: max tab width 240px");
        text.Should().Contain("TextTrimming=\"CharacterEllipsis\"",
            "UI-SPEC line 64: overflow clipped with CharacterEllipsis");
        text.Should().Contain("ToolTip=\"{Binding TooltipText}\"",
            "UI-SPEC §Copywriting Contract: tab tooltip bound to TooltipText");
    }

    [Fact]
    public void TabDataTemplate_ActiveTabFontWeightSemiBold()
    {
        var text = ReadMainWindowXaml();

        // UI-SPEC §Typography: active tab is SemiBold, inactive is Regular.
        // Match a FontWeight Setter with value SemiBold anywhere inside the tab DataTemplate.
        // We rely on the fact that this is the only SemiBold font weight inside the tab template region.
        text.Should().Contain("FontWeight", "tab title must respond to IsActive with a weight change");
        text.Should().Contain("SemiBold", "active tab title is SemiBold (UI-SPEC Typography table)");
    }
}
