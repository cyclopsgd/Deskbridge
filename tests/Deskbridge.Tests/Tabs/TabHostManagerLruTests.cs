using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Services;
using Deskbridge.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Wpf.Ui;

namespace Deskbridge.Tests.Tabs;

/// <summary>
/// Unit tests for TabHostManager's bounded last-closed LRU (D-16). Uses the
/// internal test seam <c>PushLastClosedForTesting</c> via
/// <c>[InternalsVisibleTo("Deskbridge.Tests")]</c> to avoid reflection on the
/// private LinkedList. STA fixture required: TabHostManager constructs Dispatcher.CurrentDispatcher.
/// </summary>
[Collection("RDP-STA")]
public sealed class TabHostManagerLruTests
{
    private readonly StaCollectionFixture _fixture;
    public TabHostManagerLruTests(StaCollectionFixture fixture) => _fixture = fixture;

    private static TabHostManager BuildSut()
    {
        var bus = Substitute.For<IEventBus>();
        var coord = Substitute.For<IConnectionCoordinator>();
        var disc = Substitute.For<IDisconnectPipeline>();
        disc.DisconnectAsync(Arg.Any<DisconnectContext>()).Returns(Task.FromResult(new PipelineResult(true)));
        var snack = Substitute.For<ISnackbarService>();

        return new TabHostManager(
            bus, coord, disc, snack,
            NullLogger<TabHostManager>.Instance,
            Dispatcher.CurrentDispatcher);
    }

    [Fact]
    public void PopLastClosed_ReturnsNull_WhenLruIsEmpty()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var sut = BuildSut();
            sut.PopLastClosed().Should().BeNull();
            sut.PopLastClosed().Should().BeNull();  // idempotent
        });
    }

    [Fact]
    public void PushLru_DedupesByConnectionId()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var sut = BuildSut();
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();

            // Push A, B, A → dedupe keeps one A, re-fronted.
            sut.PushLastClosedForTesting(a);
            sut.PushLastClosedForTesting(b);
            sut.PushLastClosedForTesting(a);

            sut.PopLastClosed().Should().Be(a, "most-recent push wins after dedupe");
            sut.PopLastClosed().Should().Be(b);
            sut.PopLastClosed().Should().BeNull();
        });
    }

    [Fact]
    public void PushLru_CapsAtTen_EvictsOldest()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var sut = BuildSut();
            var ids = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToArray();
            foreach (var id in ids) sut.PushLastClosedForTesting(id);

            // After 11 pushes, capacity is 10 — the FIRST pushed (ids[0]) is evicted.
            // Pops come out newest-first: ids[10], ids[9], …, ids[1].
            for (var i = ids.Length - 1; i >= 1; i--)
            {
                sut.PopLastClosed().Should().Be(ids[i]);
            }
            sut.PopLastClosed().Should().BeNull();
        });
    }

    [Fact]
    public void PopLastClosed_RemovesAndReturnsMostRecent()
    {
        _ = _fixture;
        StaRunner.Run(() =>
        {
            using var sut = BuildSut();
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var c = Guid.NewGuid();

            sut.PushLastClosedForTesting(a);
            sut.PushLastClosedForTesting(b);
            sut.PushLastClosedForTesting(c);

            sut.PopLastClosed().Should().Be(c);
            sut.PopLastClosed().Should().Be(b);
            sut.PopLastClosed().Should().Be(a);
            sut.PopLastClosed().Should().BeNull();
        });
    }
}
