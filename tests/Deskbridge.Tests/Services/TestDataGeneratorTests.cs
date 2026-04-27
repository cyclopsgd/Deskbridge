using System.Text.RegularExpressions;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests.Services;

[Trait("Category", "TestDataGenerator")]
public sealed class TestDataGeneratorTests
{
    [Fact]
    public void Generate_WithSameSeedAndCount_ProducesIdenticalOutput()
    {
        var (connections1, groups1) = TestDataGenerator.Generate(100, 42);
        var (connections2, groups2) = TestDataGenerator.Generate(100, 42);

        connections1.Select(c => c.Id).Should().BeEquivalentTo(connections2.Select(c => c.Id));
        groups1.Select(g => g.Id).Should().BeEquivalentTo(groups2.Select(g => g.Id));
        connections1.Select(c => c.Hostname).Should().BeEquivalentTo(connections2.Select(c => c.Hostname));
    }

    [Fact]
    public void Generate_WithDifferentSeeds_ProducesDifferentOutput()
    {
        var (connections1, _) = TestDataGenerator.Generate(100, 42);
        var (connections2, _) = TestDataGenerator.Generate(100, 99);

        var ids1 = connections1.Select(c => c.Id).ToList();
        var ids2 = connections2.Select(c => c.Id).ToList();
        ids1.SequenceEqual(ids2).Should().BeFalse("different seeds should produce different Ids");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void Generate_ReturnsExactConnectionCount(int count)
    {
        var (connections, _) = TestDataGenerator.Generate(count);

        connections.Should().HaveCount(count);
    }

    [Theory]
    [InlineData(100, 8, 15)]
    [InlineData(1000, 80, 120)]
    public void Generate_GroupCountScalesWithConnectionCount(int count, int minGroups, int maxGroups)
    {
        var (_, groups) = TestDataGenerator.Generate(count);

        groups.Should().HaveCountGreaterThanOrEqualTo(minGroups);
        groups.Should().HaveCountLessThanOrEqualTo(maxGroups);
    }

    [Fact]
    public void Generate_ProducesThreeLevelNesting()
    {
        var (_, groups) = TestDataGenerator.Generate(100);

        var groupById = groups.ToDictionary(g => g.Id);

        int GetDepth(ConnectionGroup g)
        {
            int depth = 1;
            var current = g;
            while (current.ParentGroupId.HasValue)
            {
                depth++;
                current = groupById[current.ParentGroupId.Value];
            }
            return depth;
        }

        int maxDepth = groups.Max(GetDepth);
        maxDepth.Should().Be(3, "groups should have 3-level nesting: Region > Environment > Role");
    }

    [Fact]
    public void Generate_UnevenDistribution()
    {
        var (connections, _) = TestDataGenerator.Generate(200);

        var groupCounts = connections
            .Where(c => c.GroupId.HasValue)
            .GroupBy(c => c.GroupId!.Value)
            .Select(g => g.Count())
            .ToList();

        groupCounts.Should().NotBeEmpty();
        int maxCount = groupCounts.Max();
        int minCount = groupCounts.Min();
        maxCount.Should().BeGreaterThanOrEqualTo(3 * minCount,
            "distribution should be uneven: max group should have at least 3x the connections of min group");
    }

    [Fact]
    public void Generate_RealisticHostnames()
    {
        var (connections, _) = TestDataGenerator.Generate(100);
        var pattern = new Regex(@"^srv-[a-z]+-[a-z]+-[a-z]+-\d{3}$");

        foreach (var conn in connections)
        {
            conn.Hostname.Should().MatchRegex(pattern,
                $"hostname '{conn.Hostname}' should match pattern srv-<region>-<env>-<role>-NNN");
        }
    }

    [Fact]
    public void Generate_AllConnectionsHaveValidGroupId()
    {
        var (connections, groups) = TestDataGenerator.Generate(100);
        var groupIds = groups.Select(g => g.Id).ToHashSet();

        foreach (var conn in connections)
        {
            conn.GroupId.Should().NotBeNull("all connections should be assigned to a group");
            groupIds.Should().Contain(conn.GroupId!.Value,
                $"connection '{conn.Name}' has GroupId that does not reference an existing group");
        }
    }

    [Fact]
    public void Generate_AllPropertiesPopulated()
    {
        var (connections, _) = TestDataGenerator.Generate(50);

        foreach (var conn in connections)
        {
            conn.Name.Should().NotBeNullOrWhiteSpace();
            conn.Hostname.Should().NotBeNullOrWhiteSpace();
            conn.Port.Should().Be(3389);
            conn.Protocol.Should().Be(Protocol.Rdp);
            conn.CredentialMode.Should().BeOneOf(CredentialMode.Inherit, CredentialMode.Own, CredentialMode.Prompt);
            conn.Id.Should().NotBe(Guid.Empty);
        }
    }
}
