using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;
using Deskbridge.Core.Settings;
using Deskbridge.Protocols.Rdp;
using Deskbridge.ViewModels;
using NSubstitute;
using Wpf.Ui;

namespace Deskbridge.Tests.ViewModels;

public class ConnectionTreeStateTrackingTests
{
    private readonly ConnectionTreeViewModel _sut;
    private readonly IEventBus _bus;

    public ConnectionTreeStateTrackingTests()
    {
        var connectionStore = Substitute.For<IConnectionStore>();
        var connectionQuery = Substitute.For<IConnectionQuery>();
        var credentialService = Substitute.For<ICredentialService>();
        var contentDialogService = Substitute.For<IContentDialogService>();
        var snackbarService = Substitute.For<ISnackbarService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        _bus = new EventBus();
        var tabHostManager = Substitute.For<ITabHostManager>();

        _sut = new ConnectionTreeViewModel(
            connectionStore, connectionQuery, credentialService,
            contentDialogService, snackbarService, serviceProvider,
            _bus, tabHostManager, new AirspaceSwapper());
    }

    // --- SelectedConnectionState tracking ---

    [Fact]
    public void SelectedConnectionState_IsNull_WhenNothingSelected()
    {
        _sut.SelectedConnectionState.Should().BeNull();
    }

    [Fact]
    public void SelectedConnectionState_Updates_WhenTabStateChangedEvent_MatchesSelectedConnection()
    {
        var connId = Guid.NewGuid();
        var connVm = new ConnectionTreeItemViewModel { Id = connId, Name = "Test" };
        _sut.PrimarySelectedItem = connVm;

        _bus.Publish(new TabStateChangedEvent(connId, TabState.Connected));

        _sut.SelectedConnectionState.Should().Be(TabState.Connected);
    }

    [Fact]
    public void SelectedConnectionState_Unchanged_WhenTabStateChangedEvent_DifferentConnection()
    {
        var selectedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var connVm = new ConnectionTreeItemViewModel { Id = selectedId, Name = "Selected" };
        _sut.PrimarySelectedItem = connVm;

        _bus.Publish(new TabStateChangedEvent(otherId, TabState.Connected));

        _sut.SelectedConnectionState.Should().BeNull();
    }

    [Fact]
    public void SelectedConnectionState_ClearsToNull_WhenConnectionClosedEvent()
    {
        var connId = Guid.NewGuid();
        var connVm = new ConnectionTreeItemViewModel { Id = connId, Name = "Test" };
        _sut.PrimarySelectedItem = connVm;

        // First connect
        _bus.Publish(new TabStateChangedEvent(connId, TabState.Connected));
        _sut.SelectedConnectionState.Should().Be(TabState.Connected);

        // Then close
        var model = new ConnectionModel { Id = connId, Name = "Test", Hostname = "test.local" };
        _bus.Publish(new ConnectionClosedEvent(model, DisconnectReason.UserInitiated));

        _sut.SelectedConnectionState.Should().BeNull();
    }

    [Fact]
    public void SelectedConnectionState_RestoresFromMap_WhenReselectingConnection()
    {
        var connId = Guid.NewGuid();
        var connVm = new ConnectionTreeItemViewModel { Id = connId, Name = "Test" };

        // Publish state BEFORE selecting the connection
        _bus.Publish(new TabStateChangedEvent(connId, TabState.Reconnecting));

        // Now select — should pick up the cached state
        _sut.PrimarySelectedItem = connVm;

        _sut.SelectedConnectionState.Should().Be(TabState.Reconnecting);
    }

    [Fact]
    public void SelectedConnectionState_ClearsWhenGroupSelected()
    {
        var connId = Guid.NewGuid();
        var connVm = new ConnectionTreeItemViewModel { Id = connId, Name = "Test" };
        _sut.PrimarySelectedItem = connVm;
        _bus.Publish(new TabStateChangedEvent(connId, TabState.Connected));
        _sut.SelectedConnectionState.Should().Be(TabState.Connected);

        // Switch to a group
        var groupVm = new GroupTreeItemViewModel { Id = Guid.NewGuid(), Name = "Group" };
        _sut.PrimarySelectedItem = groupVm;

        _sut.SelectedConnectionState.Should().BeNull();
    }

    [Fact]
    public void SelectedConnectionState_ClearsWhenSelectionCleared()
    {
        var connId = Guid.NewGuid();
        var connVm = new ConnectionTreeItemViewModel { Id = connId, Name = "Test" };
        _sut.PrimarySelectedItem = connVm;
        _bus.Publish(new TabStateChangedEvent(connId, TabState.Connected));

        _sut.PrimarySelectedItem = null;

        _sut.SelectedConnectionState.Should().BeNull();
    }

    // --- Card expand/collapse settings ---

    [Fact]
    public void ApplyPropertiesPanelSettings_SetsCardExpandState()
    {
        var settings = new PropertiesPanelRecord(
            IsConnectionCardExpanded: false,
            IsCredentialsCardExpanded: false);

        _sut.ApplyPropertiesPanelSettings(settings);

        _sut.IsConnectionCardExpanded.Should().BeFalse();
        _sut.IsCredentialsCardExpanded.Should().BeFalse();
    }

    [Fact]
    public void GetPropertiesPanelSettings_ReturnsCurrentState()
    {
        _sut.IsConnectionCardExpanded = false;
        _sut.IsCredentialsCardExpanded = true;

        var result = _sut.GetPropertiesPanelSettings();

        result.IsConnectionCardExpanded.Should().BeFalse();
        result.IsCredentialsCardExpanded.Should().BeTrue();
    }

    [Fact]
    public void CardExpandProperties_DefaultToTrue()
    {
        _sut.IsConnectionCardExpanded.Should().BeTrue();
        _sut.IsCredentialsCardExpanded.Should().BeTrue();
    }
}
