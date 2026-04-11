using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests;

public sealed class ConnectionQueryTests
{
    private static readonly Guid TestGroupId = Guid.NewGuid();

    private static ConnectionQueryService CreateService()
    {
        var connections = new List<ConnectionModel>
        {
            new()
            {
                Name = "prod-server-01",
                Hostname = "10.0.0.1",
                Tags = ["production"],
                GroupId = TestGroupId,
                UpdatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                Name = "dev-server-01",
                Hostname = "192.168.1.100",
                Tags = ["development"],
                UpdatedAt = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                Name = "web-server",
                Hostname = "10.0.0.5",
                Tags = ["web", "prod"],
                GroupId = TestGroupId,
                UpdatedAt = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                Name = "staging-db",
                Hostname = "10.0.1.50",
                Tags = ["staging", "database"],
                UpdatedAt = new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        return new ConnectionQueryService(connections);
    }

    [Theory]
    [InlineData("prod", "prod-server-01")]
    [InlineData("dev", "dev-server-01")]
    [InlineData("staging", "staging-db")]
    public void Search_FindsSubstringInName(string query, string expectedName)
    {
        var service = CreateService();
        var results = service.Search(query);
        results.Should().Contain(c => c.Name == expectedName);
    }

    [Theory]
    [InlineData("192.168", "dev-server-01")]
    [InlineData("10.0.0", "prod-server-01")]
    public void Search_FindsSubstringInHostname(string query, string expectedName)
    {
        var service = CreateService();
        var results = service.Search(query);
        results.Should().Contain(c => c.Name == expectedName);
    }

    [Theory]
    [InlineData("web", "web-server")]
    [InlineData("database", "staging-db")]
    public void Search_FindsSubstringInTags(string query, string expectedName)
    {
        var service = CreateService();
        var results = service.Search(query);
        results.Should().Contain(c => c.Name == expectedName);
    }

    [Fact]
    public void Search_RanksNameMatchesHigherThanHostname()
    {
        // "prod" matches prod-server-01 by name (score 100) and web-server by tag "prod" (score 60)
        // prod-server-01 should appear first
        var service = CreateService();
        var results = service.Search("prod");

        results.Should().HaveCountGreaterThan(1);
        results[0].Name.Should().Be("prod-server-01");
    }

    [Fact]
    public void Search_FindsSubsequenceMatch()
    {
        // "psrv" should match "prod-server-01" via subsequence: p-r-o-d--s-e-r-v-e-r
        // p matches 'p', s matches 's', r matches 'r', v matches 'v'
        var service = CreateService();
        var results = service.Search("psrv");

        results.Should().Contain(c => c.Name == "prod-server-01");
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsAll()
    {
        var service = CreateService();

        service.Search("").Should().HaveCount(4);
        service.Search("  ").Should().HaveCount(4);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var service = CreateService();
        var results = service.Search("zzz-nonexistent-xyz");

        results.Should().BeEmpty();
    }

    [Fact]
    public void GetByGroup_FiltersCorrectly()
    {
        var service = CreateService();
        var results = service.GetByGroup(TestGroupId);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(c => c.GroupId == TestGroupId);
    }

    [Fact]
    public void GetByTag_FiltersCorrectly()
    {
        var service = CreateService();
        var results = service.GetByTag("web");

        results.Should().ContainSingle();
        results[0].Name.Should().Be("web-server");
    }

    [Fact]
    public void GetRecent_ReturnsOrderedByUpdateTime()
    {
        var service = CreateService();
        var results = service.GetRecent(3);

        results.Should().HaveCount(3);
        // Most recent first: dev-server-01 (Apr 10), staging-db (Apr 8), web-server (Apr 5)
        results[0].Name.Should().Be("dev-server-01");
        results[1].Name.Should().Be("staging-db");
        results[2].Name.Should().Be("web-server");
    }
}
