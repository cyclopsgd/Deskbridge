using System.Collections.Generic;
using Deskbridge.Core.Models;
using Deskbridge.ViewModels;

namespace Deskbridge.Tests.ViewModels;

public class TabItemViewModelTests
{
    [Fact]
    public void Title_DefaultsToEmptyString()
    {
        var sut = new TabItemViewModel();
        sut.Title.Should().Be(string.Empty);
    }

    [Fact]
    public void Title_IsObservable()
    {
        var sut = new TabItemViewModel();
        var changed = false;
        sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TabItemViewModel.Title))
                changed = true;
        };

        sut.Title = "New Title";

        changed.Should().BeTrue();
        sut.Title.Should().Be("New Title");
    }

    [Fact]
    public void IsActive_DefaultsToFalse()
    {
        var sut = new TabItemViewModel();
        sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_IsObservable()
    {
        var sut = new TabItemViewModel();
        var changed = false;
        sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TabItemViewModel.IsActive))
                changed = true;
        };

        sut.IsActive = true;

        changed.Should().BeTrue();
        sut.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ConnectionId_CanBeInitialized()
    {
        var id = Guid.NewGuid();
        var sut = new TabItemViewModel { ConnectionId = id };

        sut.ConnectionId.Should().Be(id);
    }

    [Fact]
    public void ConnectionId_DefaultsToEmptyGuid()
    {
        var sut = new TabItemViewModel();
        sut.ConnectionId.Should().Be(Guid.Empty);
    }

    // ----------------------------------------------------------------- Plan 05-03 Task 1

    [Fact]
    public void State_DefaultsToConnecting_AndDerivedFlagsFollow()
    {
        var sut = new TabItemViewModel();

        sut.State.Should().Be(TabState.Connecting);
        sut.IsConnecting.Should().BeTrue();
        sut.IsReconnecting.Should().BeFalse();
        sut.IsError.Should().BeFalse();
    }

    [Fact]
    public void SettingState_Connected_FlipsAllDerivedFlagsFalse_AndRaisesPropertyChanged()
    {
        var sut = new TabItemViewModel();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        sut.State = TabState.Connected;

        sut.IsConnecting.Should().BeFalse();
        sut.IsReconnecting.Should().BeFalse();
        sut.IsError.Should().BeFalse();
        raised.Should().Contain(nameof(TabItemViewModel.State));
        raised.Should().Contain(nameof(TabItemViewModel.IsConnecting));
        raised.Should().Contain(nameof(TabItemViewModel.IsReconnecting));
        raised.Should().Contain(nameof(TabItemViewModel.IsError));
        raised.Should().Contain(nameof(TabItemViewModel.TooltipText));
    }

    [Fact]
    public void SettingState_Reconnecting_OnlyIsReconnectingTrue()
    {
        var sut = new TabItemViewModel { State = TabState.Reconnecting };

        sut.IsConnecting.Should().BeFalse();
        sut.IsReconnecting.Should().BeTrue();
        sut.IsError.Should().BeFalse();
    }

    [Fact]
    public void SettingState_Error_OnlyIsErrorTrue()
    {
        var sut = new TabItemViewModel { State = TabState.Error };

        sut.IsConnecting.Should().BeFalse();
        sut.IsReconnecting.Should().BeFalse();
        sut.IsError.Should().BeTrue();
    }

    [Theory]
    [InlineData(TabState.Connecting)]
    [InlineData(TabState.Connected)]
    [InlineData(TabState.Reconnecting)]
    [InlineData(TabState.Error)]
    public void DerivedFlags_AreMutuallyExclusive_ForEveryState(TabState state)
    {
        var sut = new TabItemViewModel { State = state };

        var truthy = 0;
        if (sut.IsConnecting) truthy++;
        if (sut.IsReconnecting) truthy++;
        if (sut.IsError) truthy++;

        truthy.Should().BeLessThanOrEqualTo(1, "D-12 mutual exclusion invariant: at most one indicator visible");
    }

    [Fact]
    public void TooltipText_Connecting_UsesMiddleDotAndEllipsis()
    {
        var sut = new TabItemViewModel
        {
            Hostname = "myserver",
            State = TabState.Connecting,
        };

        sut.TooltipText.Should().Be("myserver \u00B7 Connecting\u2026");
    }

    [Fact]
    public void TooltipText_Connected_WithResolution_UsesMultiplicationSign()
    {
        var sut = new TabItemViewModel
        {
            Hostname = "myserver",
            State = TabState.Connected,
            Resolution = (1920, 1080),
        };

        sut.TooltipText.Should().Be("myserver \u00B7 1920\u00D71080");
    }

    [Fact]
    public void TooltipText_Connected_WithoutResolution_FallsBackToEmDash()
    {
        var sut = new TabItemViewModel
        {
            Hostname = "myserver",
            State = TabState.Connected,
            Resolution = null,
        };

        sut.TooltipText.Should().Be("myserver \u00B7 \u2014");
    }

    [Fact]
    public void TooltipText_Connected_WithZeroResolution_FallsBackToEmDash()
    {
        var sut = new TabItemViewModel
        {
            Hostname = "myserver",
            State = TabState.Connected,
            Resolution = (0, 0),
        };

        sut.TooltipText.Should().Be("myserver \u00B7 \u2014");
    }

    [Fact]
    public void TooltipText_Reconnecting_IncludesAttemptNumber()
    {
        var sut = new TabItemViewModel
        {
            Hostname = "myserver",
            State = TabState.Reconnecting,
            ReconnectAttempt = 3,
        };

        sut.TooltipText.Should().Be("myserver \u00B7 Reconnecting attempt 3/20");
    }

    [Fact]
    public void TooltipText_Error_UsesEmDashAndClickToReconnect()
    {
        var sut = new TabItemViewModel
        {
            Hostname = "myserver",
            State = TabState.Error,
        };

        sut.TooltipText.Should().Be("myserver \u00B7 Connection failed \u2014 click tab to reconnect");
    }

    [Fact]
    public void TooltipText_NeverInterpolatesCredentials()
    {
        // T-05-01: the tooltip is rendered from Hostname + state + resolution only.
        // Asserting by contract: TabItemViewModel does not expose any credential property.
        var props = typeof(TabItemViewModel).GetProperties()
            .Select(p => p.Name)
            .ToArray();

        props.Should().NotContain(n =>
            n.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hostname_SetToNewValue_RaisesTooltipTextPropertyChanged()
    {
        var sut = new TabItemViewModel();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        sut.Hostname = "myserver";

        raised.Should().Contain(nameof(TabItemViewModel.TooltipText));
    }

    [Fact]
    public void ReconnectAttempt_RaisesTooltipTextPropertyChanged()
    {
        var sut = new TabItemViewModel();
        var raised = new List<string?>();
        sut.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        sut.ReconnectAttempt = 5;

        raised.Should().Contain(nameof(TabItemViewModel.TooltipText));
    }
}
