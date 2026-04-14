using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using Deskbridge.Models;
using Deskbridge.ViewModels;
using NSubstitute;
using Wpf.Ui;

namespace Deskbridge.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private readonly MainWindowViewModel _sut;
    private readonly ITabHostManager _tabHostManager;
    private readonly IEventBus _bus;
    private readonly IConnectionStore _connectionStore;

    public MainWindowViewModelTests()
    {
        _connectionStore = Substitute.For<IConnectionStore>();
        var connectionQuery = Substitute.For<IConnectionQuery>();
        var credentialService = Substitute.For<ICredentialService>();
        var contentDialogService = Substitute.For<IContentDialogService>();
        var snackbarService = Substitute.For<ISnackbarService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        _bus = new EventBus();   // real bus so Publish/Subscribe deliver end-to-end
        _tabHostManager = Substitute.For<ITabHostManager>();

        var treeVm = new ConnectionTreeViewModel(
            _connectionStore, connectionQuery, credentialService,
            contentDialogService, snackbarService, serviceProvider, _bus, _tabHostManager);

        _sut = new MainWindowViewModel(treeVm, _tabHostManager, _bus, _connectionStore);
    }

    // --- Panel toggle state machine (D-04) ---

    [Fact]
    public void ActivePanelMode_DefaultsToConnections()
    {
        _sut.ActivePanelMode.Should().Be(PanelMode.Connections);
    }

    [Fact]
    public void IsPanelVisible_DefaultsToTrue()
    {
        _sut.IsPanelVisible.Should().BeTrue();
    }

    [Fact]
    public void TogglePanel_FromNone_OpensPanelWithThatMode()
    {
        _sut.TogglePanelCommand.Execute(PanelMode.Connections);
        _sut.ActivePanelMode.Should().Be(PanelMode.None);

        _sut.TogglePanelCommand.Execute(PanelMode.Connections);

        _sut.ActivePanelMode.Should().Be(PanelMode.Connections);
        _sut.IsPanelVisible.Should().BeTrue();
    }

    [Fact]
    public void TogglePanel_SameMode_ClosesPanel()
    {
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
        _sut.IsConnectionsActive.Should().BeTrue();

        _sut.TogglePanelCommand.Execute(PanelMode.Search);
        _sut.IsConnectionsActive.Should().BeFalse();

        _sut.TogglePanelCommand.Execute(PanelMode.Connections);
        _sut.IsConnectionsActive.Should().BeTrue();
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

    // --- Tab management (Phase 5 delegations) ---

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
    public void CloseTab_DelegatesToTabHostManager_CloseTabAsync()
    {
        _tabHostManager.CloseTabAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        var tab = new TabItemViewModel { Title = "Test", ConnectionId = Guid.NewGuid() };

        _sut.CloseTabCommand.Execute(tab);

        _tabHostManager.Received(1).CloseTabAsync(tab.ConnectionId);
    }

    [Fact]
    public void CloseTab_NullTab_DoesNotCallService()
    {
        _sut.CloseTabCommand.Execute(null);

        _tabHostManager.DidNotReceive().CloseTabAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void SwitchTab_DelegatesToTabHostManager_SwitchTo()
    {
        var tab = new TabItemViewModel { Title = "Test", ConnectionId = Guid.NewGuid() };

        _sut.SwitchTabCommand.Execute(tab);

        _tabHostManager.Received(1).SwitchTo(tab.ConnectionId);
    }

    [Fact]
    public void CloseOtherTabs_DelegatesToTabHostManager_CloseOthersAsync()
    {
        _tabHostManager.CloseOthersAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        var tab = new TabItemViewModel { Title = "Keep", ConnectionId = Guid.NewGuid() };

        _sut.CloseOtherTabsCommand.Execute(tab);

        _tabHostManager.Received(1).CloseOthersAsync(tab.ConnectionId);
    }

    [Fact]
    public void CloseAllTabs_DelegatesToTabHostManager_CloseAllAsync()
    {
        _tabHostManager.CloseAllAsync().Returns(Task.CompletedTask);

        _sut.CloseAllTabsCommand.Execute(null);

        _tabHostManager.Received(1).CloseAllAsync();
    }

    [Fact]
    public void ReopenLastClosed_EmptyLru_IsSilentNoOp()
    {
        _tabHostManager.PopLastClosed().Returns((Guid?)null);

        // Use an NSubstitute-mocked bus here — the real bus won't help us prove a negative.
        var bus = Substitute.For<IEventBus>();
        var vm = new MainWindowViewModel(_sut.ConnectionTree, _tabHostManager, bus, _connectionStore);

        vm.ReopenLastClosedCommand.Execute(null);

        bus.DidNotReceive().Publish(Arg.Any<ConnectionRequestedEvent>());
    }

    [Fact]
    public void ReopenLastClosed_ConnectionDeleted_IsSilentNoOp()
    {
        var id = Guid.NewGuid();
        _tabHostManager.PopLastClosed().Returns((Guid?)id);
        _connectionStore.GetById(id).Returns((ConnectionModel?)null);

        var bus = Substitute.For<IEventBus>();
        var vm = new MainWindowViewModel(_sut.ConnectionTree, _tabHostManager, bus, _connectionStore);

        vm.ReopenLastClosedCommand.Execute(null);

        bus.DidNotReceive().Publish(Arg.Any<ConnectionRequestedEvent>());
    }

    [Fact]
    public void ReopenLastClosed_ValidConnection_PublishesConnectionRequestedEvent()
    {
        var id = Guid.NewGuid();
        var model = new ConnectionModel { Id = id, Name = "x", Hostname = "h" };
        _tabHostManager.PopLastClosed().Returns((Guid?)id);
        _connectionStore.GetById(id).Returns(model);

        var bus = Substitute.For<IEventBus>();
        var vm = new MainWindowViewModel(_sut.ConnectionTree, _tabHostManager, bus, _connectionStore);

        vm.ReopenLastClosedCommand.Execute(null);

        bus.Received(1).Publish(Arg.Is<ConnectionRequestedEvent>(e => e.Connection.Id == id));
    }

    // --- TabOpenedEvent / TabClosedEvent subscribers ---

    [Fact]
    public void OnTabOpened_AddsTabToCollection_WithConnectionName()
    {
        var id = Guid.NewGuid();
        var model = new ConnectionModel { Id = id, Name = "MyServer", Hostname = "srv01" };
        _connectionStore.GetById(id).Returns(model);

        _bus.Publish(new TabOpenedEvent(id, model));

        _sut.Tabs.Should().ContainSingle(t => t.ConnectionId == id && t.Title == "MyServer");
    }

    [Fact]
    public void OnTabClosed_RemovesTabFromCollection()
    {
        var id = Guid.NewGuid();
        var model = new ConnectionModel { Id = id, Name = "x", Hostname = "h" };
        _connectionStore.GetById(id).Returns(model);
        _bus.Publish(new TabOpenedEvent(id, model));

        _bus.Publish(new TabClosedEvent(id));

        _sut.Tabs.Should().BeEmpty();
    }

    // --- TabSwitchedEvent / status bar ---

    [Fact]
    public void OnTabSwitched_UpdatesStatusText_WithHostnameAndConnectedState()
    {
        var id = Guid.NewGuid();
        var model = new ConnectionModel
        {
            Id = id,
            Name = "S",
            Hostname = "myserver",
            DisplaySettings = new DisplaySettings { Width = 1920, Height = 1080 },
        };
        _connectionStore.GetById(id).Returns(model);
        _bus.Publish(new TabOpenedEvent(id, model));

        // Tab is created with State=Connecting; explicitly flip to Connected first.
        _bus.Publish(new TabStateChangedEvent(id, TabState.Connected));
        _bus.Publish(new TabSwitchedEvent(null, id));

        _sut.StatusText.Should().Be("myserver \u00B7 Connected");
        _sut.StatusSecondary.Should().Be("1920 \u00D7 1080");
    }

    [Fact]
    public void OnTabSwitched_ActiveIdEmpty_ResetsStatusToReady()
    {
        _bus.Publish(new TabSwitchedEvent(null, Guid.Empty));

        _sut.StatusText.Should().Be("Ready");
        _sut.StatusSecondary.Should().Be(string.Empty);
    }

    [Fact]
    public void OnTabSwitched_Connecting_RendersEllipsisAndEmDash()
    {
        var id = Guid.NewGuid();
        var model = new ConnectionModel { Id = id, Name = "X", Hostname = "h" };
        _connectionStore.GetById(id).Returns(model);
        _bus.Publish(new TabOpenedEvent(id, model));

        _bus.Publish(new TabSwitchedEvent(null, id));

        _sut.StatusText.Should().Be("h \u00B7 Connecting\u2026");    // U+2026 ellipsis
        _sut.StatusSecondary.Should().Be("\u2014");                    // U+2014 em-dash
    }

    // --- Status bar defaults ---

    [Fact]
    public void StatusText_DefaultsToReady()
    {
        _sut.StatusText.Should().Be("Ready");
    }

    [Fact]
    public void StatusSecondary_DefaultsToEmpty()
    {
        _sut.StatusSecondary.Should().BeEmpty();
    }
}
