using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Services;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Tests.Fakes;
using Deskbridge.ViewModels;
using FluentAssertions;
using NSubstitute;
using Wpf.Ui;
using Xunit;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// Phase 21 (PERF-02): trailing-fire and instant-clear debounce contract.
/// Uses <see cref="FakeDebouncer"/> to keep timing deterministic — D-03 mandates
/// the production path uses <c>DispatcherTimer</c>, but xUnit.v3 cannot pump it
/// (per 21-PATTERNS.md), so the VM is refactored to take an <see cref="IDebouncer"/>
/// and these tests assert the contract through the abstraction.
/// </summary>
public class ConnectionTreeSearchDebounceTests
{
    private readonly ConnectionTreeViewModel _sut;
    private readonly FakeDebouncer _debouncer;

    public ConnectionTreeSearchDebounceTests()
    {
        var connectionStore = Substitute.For<IConnectionStore>();
        var connectionQuery = Substitute.For<IConnectionQuery>();
        var credentialService = Substitute.For<ICredentialService>();
        var contentDialogService = Substitute.For<IContentDialogService>();
        var snackbarService = Substitute.For<ISnackbarService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var bus = new EventBus();
        var tabHostManager = Substitute.For<ITabHostManager>();
        _debouncer = new FakeDebouncer();

        _sut = new ConnectionTreeViewModel(
            connectionStore, connectionQuery, credentialService,
            contentDialogService, snackbarService, serviceProvider,
            bus, tabHostManager, new AirspaceSwapper(), _debouncer);
    }

    [Fact]
    public void SearchText_SingleKeystroke_SchedulesExactlyOneFilterCall()
    {
        _sut.SearchText = "abc";

        _debouncer.ScheduleCallCount.Should().Be(1);
        _debouncer.HasPending.Should().BeTrue();
    }

    [Fact]
    public void SearchText_FiveRapidKeystrokes_SchedulesFiveTimes_FilterRunsOnce()
    {
        _sut.SearchText = "a";
        _sut.SearchText = "ab";
        _sut.SearchText = "abc";
        _sut.SearchText = "abcd";
        _sut.SearchText = "abcde";

        _debouncer.ScheduleCallCount.Should().Be(5,
            "each keystroke calls Schedule on the debouncer; the debouncer is the one " +
            "that coalesces — only the final captured action is preserved.");
        _debouncer.HasPending.Should().BeTrue();

        // Manually fire the trailing action — only the most-recent snapshot runs.
        _debouncer.Fire();

        _debouncer.HasPending.Should().BeFalse(
            "after Fire the captured action was invoked exactly once and pending state cleared.");
    }

    [Fact]
    public void SearchText_ClearedToEmpty_BypassesDebounceAndCancels()
    {
        // Arrange: prime a pending debounce
        _sut.SearchText = "abc";
        _debouncer.HasPending.Should().BeTrue();

        // Act: clear search
        _sut.SearchText = string.Empty;

        // Assert: Cancel was invoked at least once (D-02), pending action discarded
        _debouncer.CancelCallCount.Should().BeGreaterThanOrEqualTo(1);
        _debouncer.HasPending.Should().BeFalse();
    }

    [Fact]
    public void SearchText_ClearedToWhitespace_BypassesDebounceAndCancels()
    {
        // Arrange: prime a pending debounce
        _sut.SearchText = "abc";
        _debouncer.HasPending.Should().BeTrue();

        // Act: whitespace-only counts as cleared per IsNullOrWhiteSpace check (D-02)
        _sut.SearchText = "   ";

        // Assert
        _debouncer.CancelCallCount.Should().BeGreaterThanOrEqualTo(1);
        _debouncer.HasPending.Should().BeFalse();
    }
}
