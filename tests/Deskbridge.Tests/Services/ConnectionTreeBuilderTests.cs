using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests.Services;

[Trait("Category", "TreeBuilder")]
public sealed class ConnectionTreeBuilderTests
{
    [Fact]
    public void RootLevelItems_HaveDepthZero()
    {
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Root1", Hostname = "host1" },
            new() { Id = Guid.NewGuid(), Name = "Root2", Hostname = "host2" },
        };

        var result = ConnectionTreeBuilder.Build(connections, []);

        result.Should().HaveCount(2);
        result[0].Depth.Should().Be(0);
        result[1].Depth.Should().Be(0);
    }

    [Fact]
    public void ItemsInTopLevelGroup_HaveDepthOne()
    {
        var groupId = Guid.NewGuid();
        var groups = new List<ConnectionGroup>
        {
            new() { Id = groupId, Name = "Group1" },
        };
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Child1", Hostname = "h1", GroupId = groupId },
            new() { Id = Guid.NewGuid(), Name = "Child2", Hostname = "h2", GroupId = groupId },
        };

        var result = ConnectionTreeBuilder.Build(connections, groups);

        result.Should().ContainSingle();
        var group = result[0].Should().BeOfType<GroupNode>().Subject;
        group.Depth.Should().Be(0);
        group.Children.Should().HaveCount(2);
        group.Children[0].Depth.Should().Be(1);
        group.Children[1].Depth.Should().Be(1);
    }

    [Fact]
    public void ThreeLevelNesting_CorrectDepths()
    {
        // Region > Env > Role > Connection => depths 0, 1, 2, 3
        var regionId = Guid.NewGuid();
        var envId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var groups = new List<ConnectionGroup>
        {
            new() { Id = regionId, Name = "Region" },
            new() { Id = envId, Name = "Environment", ParentGroupId = regionId },
            new() { Id = roleId, Name = "Role", ParentGroupId = envId },
        };
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Server1", Hostname = "srv1", GroupId = roleId },
        };

        var result = ConnectionTreeBuilder.Build(connections, groups);

        result.Should().ContainSingle();
        var region = result[0].Should().BeOfType<GroupNode>().Subject;
        region.Depth.Should().Be(0);

        var env = region.Children[0].Should().BeOfType<GroupNode>().Subject;
        env.Depth.Should().Be(1);

        var role = env.Children[0].Should().BeOfType<GroupNode>().Subject;
        role.Depth.Should().Be(2);

        var conn = role.Children[0].Should().BeOfType<ConnectionNode>().Subject;
        conn.Depth.Should().Be(3);
    }

    [Fact]
    public void CyclicGroupChain_PromotedToRoot()
    {
        // A.Parent = B, B.Parent = A => both promoted to root
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        var groups = new List<ConnectionGroup>
        {
            new() { Id = idA, Name = "GroupA", ParentGroupId = idB },
            new() { Id = idB, Name = "GroupB", ParentGroupId = idA },
        };

        var result = ConnectionTreeBuilder.Build([], groups);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(n => n.Should().BeOfType<GroupNode>());
        result.Should().AllSatisfy(n => n.Depth.Should().Be(0));
    }

    [Fact]
    public void SelfReferencingGroup_PromotedToRoot()
    {
        var groupId = Guid.NewGuid();
        var groups = new List<ConnectionGroup>
        {
            new() { Id = groupId, Name = "SelfRef", ParentGroupId = groupId },
        };

        var result = ConnectionTreeBuilder.Build([], groups);

        result.Should().ContainSingle();
        var group = result[0].Should().BeOfType<GroupNode>().Subject;
        group.Depth.Should().Be(0);
        group.Id.Should().Be(groupId);
    }

    [Fact]
    public void SortOrder_GroupsAndConnectionsSortedBySortOrderThenName()
    {
        var groupId = Guid.NewGuid();
        var groups = new List<ConnectionGroup>
        {
            new() { Id = groupId, Name = "ZGroup", SortOrder = 1 },
        };
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Bravo", Hostname = "b", SortOrder = 2 },
            new() { Id = Guid.NewGuid(), Name = "Alpha", Hostname = "a", SortOrder = 2 },
            new() { Id = Guid.NewGuid(), Name = "Charlie", Hostname = "c", SortOrder = 0 },
        };

        var result = ConnectionTreeBuilder.Build(connections, groups);

        // Charlie (sort=0), ZGroup (sort=1), Alpha (sort=2), Bravo (sort=2, name tiebreaker)
        result.Should().HaveCount(4);
        result[0].Name.Should().Be("Charlie");
        result[1].Name.Should().Be("ZGroup");
        result[2].Name.Should().Be("Alpha");
        result[3].Name.Should().Be("Bravo");
    }

    [Fact]
    public void ConnectionsPlacedInCorrectGroups()
    {
        var group1Id = Guid.NewGuid();
        var group2Id = Guid.NewGuid();

        var groups = new List<ConnectionGroup>
        {
            new() { Id = group1Id, Name = "Group1" },
            new() { Id = group2Id, Name = "Group2" },
        };
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Conn1", Hostname = "h1", GroupId = group1Id },
            new() { Id = Guid.NewGuid(), Name = "Conn2", Hostname = "h2", GroupId = group2Id },
        };

        var result = ConnectionTreeBuilder.Build(connections, groups);

        result.Should().HaveCount(2);

        var g1 = result.OfType<GroupNode>().First(g => g.Id == group1Id);
        g1.Children.Should().ContainSingle().Which.Name.Should().Be("Conn1");

        var g2 = result.OfType<GroupNode>().First(g => g.Id == group2Id);
        g2.Children.Should().ContainSingle().Which.Name.Should().Be("Conn2");
    }

    [Fact]
    public void OrphanedConnections_PlacedAtRoot()
    {
        var missingGroupId = Guid.NewGuid();
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Orphan", Hostname = "orphan", GroupId = missingGroupId },
        };

        var result = ConnectionTreeBuilder.Build(connections, []);

        result.Should().ContainSingle();
        var conn = result[0].Should().BeOfType<ConnectionNode>().Subject;
        conn.Name.Should().Be("Orphan");
        conn.Depth.Should().Be(0);
        conn.GroupId.Should().Be(missingGroupId); // preserves original GroupId
    }

    [Fact]
    public void MixedTree_AllItemsHaveCorrectDepth()
    {
        // Build mixed tree:
        // Root connection (depth 0)
        // Group A (depth 0)
        //   Connection A1 (depth 1)
        //   Group B (depth 1)
        //     Connection B1 (depth 2)
        // Group C (depth 0)
        //   Connection C1 (depth 1)

        var groupAId = Guid.NewGuid();
        var groupBId = Guid.NewGuid();
        var groupCId = Guid.NewGuid();
        var rootConnId = Guid.NewGuid();
        var connA1Id = Guid.NewGuid();
        var connB1Id = Guid.NewGuid();
        var connC1Id = Guid.NewGuid();

        var groups = new List<ConnectionGroup>
        {
            new() { Id = groupAId, Name = "GroupA", SortOrder = 1 },
            new() { Id = groupBId, Name = "GroupB", ParentGroupId = groupAId, SortOrder = 1 },
            new() { Id = groupCId, Name = "GroupC", SortOrder = 2 },
        };
        var connections = new List<ConnectionModel>
        {
            new() { Id = rootConnId, Name = "Root", Hostname = "root", SortOrder = 0 },
            new() { Id = connA1Id, Name = "A1", Hostname = "a1", GroupId = groupAId, SortOrder = 0 },
            new() { Id = connB1Id, Name = "B1", Hostname = "b1", GroupId = groupBId },
            new() { Id = connC1Id, Name = "C1", Hostname = "c1", GroupId = groupCId },
        };

        var result = ConnectionTreeBuilder.Build(connections, groups);

        // Root level: Root conn (sort=0), GroupA (sort=1), GroupC (sort=2)
        result.Should().HaveCount(3);

        var rootConn = result[0].Should().BeOfType<ConnectionNode>().Subject;
        rootConn.Depth.Should().Be(0);

        var groupA = result[1].Should().BeOfType<GroupNode>().Subject;
        groupA.Depth.Should().Be(0);
        groupA.Children.Should().HaveCount(2);

        var a1 = groupA.Children[0].Should().BeOfType<ConnectionNode>().Subject;
        a1.Depth.Should().Be(1);

        var groupB = groupA.Children[1].Should().BeOfType<GroupNode>().Subject;
        groupB.Depth.Should().Be(1);
        groupB.Children.Should().ContainSingle();

        var b1 = groupB.Children[0].Should().BeOfType<ConnectionNode>().Subject;
        b1.Depth.Should().Be(2);

        var groupC = result[2].Should().BeOfType<GroupNode>().Subject;
        groupC.Depth.Should().Be(0);
        groupC.Children.Should().ContainSingle();

        var c1 = groupC.Children[0].Should().BeOfType<ConnectionNode>().Subject;
        c1.Depth.Should().Be(1);
    }
}
