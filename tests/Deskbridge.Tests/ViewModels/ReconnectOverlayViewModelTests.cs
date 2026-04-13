using Deskbridge.ViewModels;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="ReconnectOverlayViewModel"/> — the WPF MVVM surface that
/// bridges <see cref="Deskbridge.Core.Services.RdpReconnectCoordinator"/> to the
/// reconnect overlay (Plan 04-03 D-03/D-05/D-07). Events + RelayCommands are
/// verified without WPF dispatcher (no InitializeComponent).
/// </summary>
public sealed class ReconnectOverlayViewModelTests
{
    [Fact]
    public void InitialMode_IsAuto()
    {
        var vm = new ReconnectOverlayViewModel();
        vm.Mode.Should().Be(ReconnectMode.Auto);
        vm.AttemptText.Should().Contain("Reconnecting");
    }

    [Fact]
    public void Update_SetsAttemptAndDelay_FromReconnectingEvent()
    {
        var vm = new ReconnectOverlayViewModel();

        vm.Update(3, TimeSpan.FromSeconds(8));

        vm.Attempt.Should().Be(3);
        vm.Delay.Should().Be(TimeSpan.FromSeconds(8));
        vm.AttemptText.Should().Contain("3");
    }

    [Fact]
    public void AfterCap_SwitchesTo_Manual_Mode()
    {
        var vm = new ReconnectOverlayViewModel();
        vm.Update(20, TimeSpan.FromSeconds(30));

        vm.SwitchToManual();

        vm.Mode.Should().Be(ReconnectMode.Manual);
        vm.Message.Should().Be("Connection lost");
        vm.AttemptText.Should().Be("Connection lost");
    }

    [Fact]
    public void CancelCommand_SignalsCancellation_CallbackInvoked()
    {
        var vm = new ReconnectOverlayViewModel();
        var invoked = 0;
        vm.Cancelled += (_, _) => invoked++;

        vm.CancelCommand.Execute(null);

        invoked.Should().Be(1);
    }

    [Fact]
    public void ReconnectCommand_RaisesReconnectRequested_Event()
    {
        var vm = new ReconnectOverlayViewModel();
        var invoked = 0;
        vm.ReconnectRequested += (_, _) => invoked++;

        vm.ReconnectCommand.Execute(null);

        invoked.Should().Be(1);
    }

    [Fact]
    public void CloseCommand_RaisesCloseRequested_Event()
    {
        var vm = new ReconnectOverlayViewModel();
        var invoked = 0;
        vm.CloseRequested += (_, _) => invoked++;

        vm.CloseCommand.Execute(null);

        invoked.Should().Be(1);
    }
}
