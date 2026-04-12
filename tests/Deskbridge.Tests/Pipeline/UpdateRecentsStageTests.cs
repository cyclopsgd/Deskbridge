using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Pipeline.Stages;

namespace Deskbridge.Tests.Pipeline;

public sealed class UpdateRecentsStageTests
{
    [Fact]
    public async Task SetsLastUsedAt_ToNowUtc_AndCallsStoreSave()
    {
        var connection = new ConnectionModel { Hostname = "h", LastUsedAt = null };
        var store = Substitute.For<IConnectionStore>();
        var stage = new UpdateRecentsStage(store);
        var ctx = new ConnectionContext { Connection = connection };

        var before = DateTime.UtcNow;
        var result = await stage.ExecuteAsync(ctx);
        var after = DateTime.UtcNow;

        result.Success.Should().BeTrue();
        connection.LastUsedAt.Should().NotBeNull();
        connection.LastUsedAt!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        connection.LastUsedAt.Value.Should().BeOnOrBefore(after.AddSeconds(1));
        store.Received(1).Save(connection);
    }

    [Fact]
    public void StageOrderIs400()
    {
        var stage = new UpdateRecentsStage(Substitute.For<IConnectionStore>());
        stage.Order.Should().Be(400);
        stage.Name.Should().Be("UpdateRecents");
    }
}
