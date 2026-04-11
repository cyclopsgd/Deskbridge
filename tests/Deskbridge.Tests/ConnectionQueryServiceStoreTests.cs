using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests;

public sealed class ConnectionQueryServiceStoreTests
{
    [Fact]
    public void Search_ReturnsResults_FromInjectedStore()
    {
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Production-DB", Hostname = "prod-db.local" },
            new() { Id = Guid.NewGuid(), Name = "Staging-Web", Hostname = "staging-web.local" }
        };

        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns(connections.AsReadOnly());

        var service = new ConnectionQueryService(store);

        var results = service.Search("Production");
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Production-DB");
    }

    [Fact]
    public void GetAll_DelegatesToStore()
    {
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Server1", Hostname = "s1.local" },
            new() { Id = Guid.NewGuid(), Name = "Server2", Hostname = "s2.local" }
        };

        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns(connections.AsReadOnly());

        var service = new ConnectionQueryService(store);

        var all = service.GetAll();
        all.Should().HaveCount(2);
        store.Received(1).GetAll();
    }

    [Fact]
    public void OldEnumerableConstructor_StillWorks()
    {
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Legacy", Hostname = "legacy.local" }
        };

        var service = new ConnectionQueryService(connections);

        var all = service.GetAll();
        all.Should().HaveCount(1);
        all[0].Name.Should().Be("Legacy");
    }

    [Fact]
    public void GetByGroup_DelegatesToStore()
    {
        var groupId = Guid.NewGuid();
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "InGroup", Hostname = "s1.local", GroupId = groupId },
            new() { Id = Guid.NewGuid(), Name = "OutGroup", Hostname = "s2.local", GroupId = Guid.NewGuid() }
        };

        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns(connections.AsReadOnly());

        var service = new ConnectionQueryService(store);

        var results = service.GetByGroup(groupId);
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("InGroup");
    }

    [Fact]
    public void GetRecent_DelegatesToStore()
    {
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Old", Hostname = "old.local", UpdatedAt = DateTime.UtcNow.AddDays(-10) },
            new() { Id = Guid.NewGuid(), Name = "Recent", Hostname = "recent.local", UpdatedAt = DateTime.UtcNow }
        };

        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns(connections.AsReadOnly());

        var service = new ConnectionQueryService(store);

        var recent = service.GetRecent(1);
        recent.Should().HaveCount(1);
        recent[0].Name.Should().Be("Recent");
    }
}
