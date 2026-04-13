using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests.Rdp;

/// <summary>
/// Tests for <see cref="RdpReconnectCoordinator"/> — the DispatcherTimer-free reconnect
/// loop that drives Plan 04-03 auto-retry (D-03 backoff schedule, D-05 20-attempt cap).
/// The coordinator lives in Core (protocol-agnostic per D-10) and injects a
/// <c>Func&lt;TimeSpan, CancellationToken, Task&gt;</c> delay surface so tests run
/// instantly without real Task.Delay.
/// </summary>
public sealed class RdpReconnectCoordinatorTests
{
    private static ConnectionModel MakeModel() =>
        new() { Hostname = "test.host", Protocol = Protocol.Rdp };

    private static RdpReconnectCoordinator MakeCoordinator(List<TimeSpan> capturedDelays) =>
        new(delay: (ts, _) =>
        {
            capturedDelays.Add(ts);
            return Task.CompletedTask;
        });

    [Fact]
    public async Task BackoffSchedule_First4AttemptsAre_2_4_8_16()
    {
        var captured = new List<TimeSpan>();
        var coord = MakeCoordinator(captured);
        var callCount = 0;

        // Always-fail reconnect so the loop runs through the full schedule.
        Func<ConnectionModel, Task<bool>> reconnect = _ =>
        {
            callCount++;
            return Task.FromResult(false);
        };

        await coord.RunAsync(MakeModel(), reconnect,
            notifyAttempt: (_, _) => Task.CompletedTask, CancellationToken.None);

        callCount.Should().Be(RdpReconnectCoordinator.MaxAttempts);
        captured.Take(4).Should().Equal(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(16));
    }

    [Fact]
    public async Task BackoffSchedule_Attempts5Through20_CapAt30Seconds()
    {
        var captured = new List<TimeSpan>();
        var coord = MakeCoordinator(captured);

        await coord.RunAsync(MakeModel(),
            reconnect: _ => Task.FromResult(false),
            notifyAttempt: (_, _) => Task.CompletedTask,
            CancellationToken.None);

        captured.Should().HaveCount(RdpReconnectCoordinator.MaxAttempts);
        for (int i = 4; i < RdpReconnectCoordinator.MaxAttempts; i++)
        {
            captured[i].Should().Be(TimeSpan.FromSeconds(30),
                $"attempt {i + 1} should cap at 30s");
        }
    }

    [Fact]
    public async Task Stops_AfterMaxAttempts_Is20()
    {
        var captured = new List<TimeSpan>();
        var coord = MakeCoordinator(captured);
        var callCount = 0;

        var success = await coord.RunAsync(MakeModel(),
            reconnect: _ => { callCount++; return Task.FromResult(false); },
            notifyAttempt: (_, _) => Task.CompletedTask,
            CancellationToken.None);

        success.Should().BeFalse();
        callCount.Should().Be(20);
        captured.Should().HaveCount(20);
    }

    [Fact]
    public async Task Stops_OnFirstSuccessfulReconnect()
    {
        var captured = new List<TimeSpan>();
        var coord = MakeCoordinator(captured);
        var callCount = 0;

        var success = await coord.RunAsync(MakeModel(),
            reconnect: _ =>
            {
                callCount++;
                return Task.FromResult(callCount == 3);
            },
            notifyAttempt: (_, _) => Task.CompletedTask,
            CancellationToken.None);

        success.Should().BeTrue();
        callCount.Should().Be(3);
        captured.Should().HaveCount(3, "only three delays should have elapsed before success");
    }

    [Fact]
    public async Task Cancels_Cleanly_OnCancellationToken()
    {
        // Cancelling injected delay -> OperationCanceledException -> RunAsync returns false.
        using var cts = new CancellationTokenSource();
        var callCount = 0;

        var coord = new RdpReconnectCoordinator(delay: (_, ct) =>
        {
            // Cancel during the first delay.
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

        var success = await coord.RunAsync(MakeModel(),
            reconnect: _ => { callCount++; return Task.FromResult(false); },
            notifyAttempt: (_, _) => Task.CompletedTask,
            cts.Token);

        success.Should().BeFalse();
        callCount.Should().Be(0, "reconnect must not be called after cancellation during delay");
    }

    [Fact]
    public async Task NotifyAttempt_IsCalled_BeforeEachDelay_WithCorrectNumberAndDelay()
    {
        var captured = new List<TimeSpan>();
        var coord = MakeCoordinator(captured);
        var notifications = new List<(int Attempt, TimeSpan Delay)>();

        await coord.RunAsync(MakeModel(),
            reconnect: _ => Task.FromResult(false),
            notifyAttempt: (attempt, delay) =>
            {
                notifications.Add((attempt, delay));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        notifications.Should().HaveCount(20);
        notifications[0].Should().Be((1, TimeSpan.FromSeconds(2)));
        notifications[1].Should().Be((2, TimeSpan.FromSeconds(4)));
        notifications[2].Should().Be((3, TimeSpan.FromSeconds(8)));
        notifications[3].Should().Be((4, TimeSpan.FromSeconds(16)));
        notifications[4].Should().Be((5, TimeSpan.FromSeconds(30)));
        notifications[19].Should().Be((20, TimeSpan.FromSeconds(30)));
    }

    /// <summary>
    /// Convenience test ensuring callers can defer the auto-retry decision to the
    /// classifier before invoking <see cref="RdpReconnectCoordinator.RunAsync"/>.
    /// This guards the composition: a coordinator in isolation will happily retry
    /// auth/licensing, so the caller (ConnectionCoordinator) MUST gate on
    /// <see cref="DisconnectReasonClassifier.ShouldAutoRetry"/> first.
    /// </summary>
    [Fact]
    public void SkipsAutoRetry_WhenCategory_IsAuthentication()
    {
        // 2055 = Authentication per RDP-ACTIVEX-PITFALLS §7
        var category = DisconnectReasonClassifier.Classify(2055);
        DisconnectReasonClassifier.ShouldAutoRetry(category).Should().BeFalse();
    }
}
