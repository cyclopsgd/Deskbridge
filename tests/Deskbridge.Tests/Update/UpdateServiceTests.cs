using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Services;
using NSubstitute;

namespace Deskbridge.Tests.Update;

/// <summary>
/// Phase 7 Plan 07-01 (UPD-01 / UPD-02 / UPD-03): verifies <see cref="UpdateService"/>
/// guard logic, event publication, and error handling. Since Velopack's
/// <c>UpdateManager</c> is sealed and not mockable, tests exercise the service
/// through its public <see cref="IUpdateService"/> contract, verifying the
/// dev-mode guard and exception resilience. The <c>TestableUpdateService</c>
/// subclass exposes a seam to simulate the underlying update manager behavior.
/// </summary>
public sealed class UpdateServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Test 1: When <see cref="IUpdateService.IsInstalled"/> returns false (dev mode),
    /// <see cref="IUpdateService.CheckForUpdatesAsync"/> returns false immediately
    /// without attempting any network calls.
    /// </summary>
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsFalse_WhenNotInstalled()
    {
        var bus = Substitute.For<IEventBus>();
        var sut = new TestableUpdateService(bus, isInstalled: false);

        var result = await sut.CheckForUpdatesAsync(Ct);

        result.Should().BeFalse("dev mode guard should short-circuit to false");
        sut.PendingVersion.Should().BeNull("no version should be stored when not installed");
        bus.DidNotReceiveWithAnyArgs().Publish<UpdateAvailableEvent>(default!);
    }

    /// <summary>
    /// Test 2: When an update is found, <see cref="IUpdateService.CheckForUpdatesAsync"/>
    /// publishes <see cref="UpdateAvailableEvent"/> with the correct version and returns true.
    /// </summary>
    [Fact]
    public async Task CheckForUpdatesAsync_PublishesEvent_WhenUpdateAvailable()
    {
        var bus = Substitute.For<IEventBus>();
        var sut = new TestableUpdateService(bus, isInstalled: true, simulatedVersion: "2.0.0");

        var result = await sut.CheckForUpdatesAsync(Ct);

        result.Should().BeTrue();
        sut.PendingVersion.Should().Be("2.0.0");
        bus.Received(1).Publish(Arg.Is<UpdateAvailableEvent>(e => e.Version == "2.0.0"));
    }

    /// <summary>
    /// Test 3: When the check throws an exception, <see cref="IUpdateService.CheckForUpdatesAsync"/>
    /// returns false and does not propagate the exception (non-fatal per T-07-02).
    /// </summary>
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsFalse_OnException()
    {
        var bus = Substitute.For<IEventBus>();
        var sut = new TestableUpdateService(bus, isInstalled: true, throwOnCheck: true);

        var result = await sut.CheckForUpdatesAsync(Ct);

        result.Should().BeFalse("exceptions should be caught, not propagated");
        sut.PendingVersion.Should().BeNull();
        bus.DidNotReceiveWithAnyArgs().Publish<UpdateAvailableEvent>(default!);
    }

    /// <summary>
    /// Test 4: <see cref="IUpdateService.DownloadUpdatesAsync"/> reports progress
    /// via <see cref="IProgress{T}"/>. Verifies the progress callback is invoked.
    /// </summary>
    [Fact]
    public async Task DownloadUpdatesAsync_ReportsProgress()
    {
        var bus = Substitute.For<IEventBus>();
        var sut = new TestableUpdateService(bus, isInstalled: true, simulatedVersion: "2.0.0");

        // Must check first to populate pending update
        await sut.CheckForUpdatesAsync(Ct);

        var progressValues = new List<int>();
        var progress = new SynchronousProgress<int>(v => progressValues.Add(v));

        await sut.DownloadUpdatesAsync(progress, Ct);

        progressValues.Should().NotBeEmpty("progress should have been reported");
        progressValues.Should().ContainInOrder(0, 50, 100);
    }

    /// <summary>
    /// Test 5: <see cref="IUpdateService.IsInstalled"/> returns false when the
    /// update manager is not installed (dev mode).
    /// </summary>
    [Fact]
    public void IsInstalled_ReturnsFalse_WhenNotInstalled()
    {
        var bus = Substitute.For<IEventBus>();
        var sut = new TestableUpdateService(bus, isInstalled: false);

        sut.IsInstalled.Should().BeFalse();
    }

    /// <summary>
    /// Test 6: <see cref="IUpdateService.CheckForUpdatesAsync"/> returns false
    /// when no update is available (current version is latest).
    /// </summary>
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsFalse_WhenNoUpdateAvailable()
    {
        var bus = Substitute.For<IEventBus>();
        var sut = new TestableUpdateService(bus, isInstalled: true, simulatedVersion: null);

        var result = await sut.CheckForUpdatesAsync(Ct);

        result.Should().BeFalse();
        sut.PendingVersion.Should().BeNull();
        bus.DidNotReceiveWithAnyArgs().Publish<UpdateAvailableEvent>(default!);
    }

    /// <summary>
    /// Test 7: <see cref="IUpdateService.DownloadUpdatesAsync"/> is a no-op when
    /// no pending update exists (check was not called or returned null).
    /// </summary>
    [Fact]
    public async Task DownloadUpdatesAsync_IsNoOp_WhenNoPendingUpdate()
    {
        var bus = Substitute.For<IEventBus>();
        var sut = new TestableUpdateService(bus, isInstalled: true, simulatedVersion: null);

        // No check called — should be a no-op
        var act = () => sut.DownloadUpdatesAsync(ct: Ct);
        await act.Should().NotThrowAsync();
    }
}

/// <summary>
/// Testable subclass of <see cref="UpdateService"/> that overrides the Velopack
/// calls with configurable behavior. This avoids needing a real Velopack installation.
/// </summary>
internal sealed class TestableUpdateService : UpdateService
{
    private readonly bool _isInstalled;
    private readonly string? _simulatedVersion;
    private readonly bool _throwOnCheck;

    public TestableUpdateService(
        IEventBus bus,
        bool isInstalled,
        string? simulatedVersion = null,
        bool throwOnCheck = false)
        : base(bus)
    {
        _isInstalled = isInstalled;
        _simulatedVersion = simulatedVersion;
        _throwOnCheck = throwOnCheck;
    }

    public override bool IsInstalled => _isInstalled;

    protected override Task<string?> CheckForUpdatesInternalAsync(CancellationToken ct)
    {
        if (_throwOnCheck)
            throw new InvalidOperationException("Simulated update check failure");

        return Task.FromResult(_simulatedVersion);
    }

    protected override Task DownloadUpdatesInternalAsync(Action<int>? progressCallback, CancellationToken ct)
    {
        // Simulate progress reports
        progressCallback?.Invoke(0);
        progressCallback?.Invoke(50);
        progressCallback?.Invoke(100);
        return Task.CompletedTask;
    }

    protected override void ApplyUpdatesInternalAndRestart()
    {
        // No-op in tests — would call Environment.Exit() in production
    }
}

internal sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
