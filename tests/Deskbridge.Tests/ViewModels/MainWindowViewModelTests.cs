using Deskbridge.Models;
using Deskbridge.ViewModels;

namespace Deskbridge.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private readonly MainWindowViewModel _sut = new();

    // --- Panel toggle state machine (D-04) ---

    [Fact]
    public void ActivePanelMode_DefaultsToNone()
    {
        _sut.ActivePanelMode.Should().Be(PanelMode.None);
    }

    [Fact]
    public void IsPanelVisible_DefaultsToFalse()
    {
        _sut.IsPanelVisible.Should().BeFalse();
    }

    [Fact]
    public void TogglePanel_FromNone_OpensPanelWithThatMode()
    {
        _sut.TogglePanelCommand.Execute(PanelMode.Connections);

        _sut.ActivePanelMode.Should().Be(PanelMode.Connections);
        _sut.IsPanelVisible.Should().BeTrue();
    }

    [Fact]
    public void TogglePanel_SameMode_ClosesPanel()
    {
        _sut.TogglePanelCommand.Execute(PanelMode.Connections);
        _sut.TogglePanelCommand.Execute(PanelMode.Connections);

        _sut.ActivePanelMode.Should().Be(PanelMode.None);
        _sut.IsPanelVisible.Should().BeFalse();
    }

    [Fact]
    public void TogglePanel_DifferentMode_SwitchesPanel()
    {
        _sut.TogglePanelCommand.Execute(PanelMode.Connections);
        _sut.TogglePanelCommand.Execute(PanelMode.Search);

        _sut.ActivePanelMode.Should().Be(PanelMode.Search);
        _sut.IsPanelVisible.Should().BeTrue();
    }

    [Fact]
    public void IsConnectionsActive_TrueOnlyWhenModeIsConnections()
    {
        _sut.IsConnectionsActive.Should().BeFalse();

        _sut.TogglePanelCommand.Execute(PanelMode.Connections);
        _sut.IsConnectionsActive.Should().BeTrue();

        _sut.TogglePanelCommand.Execute(PanelMode.Search);
        _sut.IsConnectionsActive.Should().BeFalse();
    }

    [Fact]
    public void IsSearchActive_TrueOnlyWhenModeIsSearch()
    {
        _sut.IsSearchActive.Should().BeFalse();

        _sut.TogglePanelCommand.Execute(PanelMode.Search);
        _sut.IsSearchActive.Should().BeTrue();

        _sut.TogglePanelCommand.Execute(PanelMode.Settings);
        _sut.IsSearchActive.Should().BeFalse();
    }

    [Fact]
    public void IsSettingsActive_TrueOnlyWhenModeIsSettings()
    {
        _sut.IsSettingsActive.Should().BeFalse();

        _sut.TogglePanelCommand.Execute(PanelMode.Settings);
        _sut.IsSettingsActive.Should().BeTrue();

        _sut.TogglePanelCommand.Execute(PanelMode.Connections);
        _sut.IsSettingsActive.Should().BeFalse();
    }

    [Fact]
    public void TogglePanel_RaisesPropertyChangedForComputedProperties()
    {
        var changedProperties = new List<string>();
        _sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                changedProperties.Add(args.PropertyName);
        };

        _sut.TogglePanelCommand.Execute(PanelMode.Connections);

        changedProperties.Should().Contain(nameof(MainWindowViewModel.ActivePanelMode));
        changedProperties.Should().Contain(nameof(MainWindowViewModel.IsPanelVisible));
        changedProperties.Should().Contain(nameof(MainWindowViewModel.IsConnectionsActive));
        changedProperties.Should().Contain(nameof(MainWindowViewModel.IsSearchActive));
        changedProperties.Should().Contain(nameof(MainWindowViewModel.IsSettingsActive));
    }

    // --- Tab management ---

    [Fact]
    public void Tabs_DefaultsToEmptyCollection()
    {
        _sut.Tabs.Should().BeEmpty();
    }

    [Fact]
    public void ActiveTab_DefaultsToNull()
    {
        _sut.ActiveTab.Should().BeNull();
    }

    [Fact]
    public void HasNoTabs_TrueWhenCollectionIsEmpty()
    {
        _sut.HasNoTabs.Should().BeTrue();
    }

    [Fact]
    public void CloseTab_RemovesTabFromCollection()
    {
        var tab = new TabItemViewModel { Title = "Test" };
        _sut.Tabs.Add(tab);

        _sut.CloseTabCommand.Execute(tab);

        _sut.Tabs.Should().BeEmpty();
    }

    [Fact]
    public void CloseTab_SetsActiveTabToNull_WhenClosedTabWasActiveAndNoOtherTabs()
    {
        var tab = new TabItemViewModel { Title = "Test" };
        _sut.Tabs.Add(tab);
        _sut.ActiveTab = tab;

        _sut.CloseTabCommand.Execute(tab);

        _sut.ActiveTab.Should().BeNull();
    }

    [Fact]
    public void CloseTab_SetsActiveTabToLastRemaining_WhenClosedTabWasActive()
    {
        var tab1 = new TabItemViewModel { Title = "Tab 1" };
        var tab2 = new TabItemViewModel { Title = "Tab 2" };
        _sut.Tabs.Add(tab1);
        _sut.Tabs.Add(tab2);
        _sut.ActiveTab = tab2;

        _sut.CloseTabCommand.Execute(tab2);

        _sut.ActiveTab.Should().Be(tab1);
        tab1.IsActive.Should().BeTrue();
    }

    [Fact]
    public void CloseTab_UpdatesHasNoTabs()
    {
        var tab = new TabItemViewModel { Title = "Test" };
        _sut.Tabs.Add(tab);
        _sut.HasNoTabs.Should().BeFalse();

        _sut.CloseTabCommand.Execute(tab);

        _sut.HasNoTabs.Should().BeTrue();
    }

    [Fact]
    public void SwitchTab_SetsActiveTabAndMarksIsActive()
    {
        var tab1 = new TabItemViewModel { Title = "Tab 1" };
        var tab2 = new TabItemViewModel { Title = "Tab 2" };
        _sut.Tabs.Add(tab1);
        _sut.Tabs.Add(tab2);

        _sut.SwitchTabCommand.Execute(tab1);

        _sut.ActiveTab.Should().Be(tab1);
        tab1.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SwitchTab_ClearsIsActiveOnPreviousTab()
    {
        var tab1 = new TabItemViewModel { Title = "Tab 1", IsActive = true };
        var tab2 = new TabItemViewModel { Title = "Tab 2" };
        _sut.Tabs.Add(tab1);
        _sut.Tabs.Add(tab2);
        _sut.ActiveTab = tab1;

        _sut.SwitchTabCommand.Execute(tab2);

        tab1.IsActive.Should().BeFalse();
        tab2.IsActive.Should().BeTrue();
        _sut.ActiveTab.Should().Be(tab2);
    }

    // --- Status bar ---

    [Fact]
    public void StatusText_DefaultsToReady()
    {
        _sut.StatusText.Should().Be("Ready");
    }

    [Fact]
    public void StatusSecondary_DefaultsToNoActiveConnection()
    {
        _sut.StatusSecondary.Should().Be("No active connection");
    }
}
