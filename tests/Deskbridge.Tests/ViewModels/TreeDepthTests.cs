using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests.ViewModels;

[Trait("Category", "Stability")]
public sealed class TreeDepthTests
{
    [Fact]
    public void RootLevelItems_HaveDepthZero()
    {
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Root1", Hostname = "h1" },
            new() { Id = Guid.NewGuid(), Name = "Root2", Hostname = "h2" },
        };

        var result = ConnectionTreeBuilder.Build(connections, []);

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

        var group = result[0].Should().BeOfType<GroupNode>().Subject;
        group.Depth.Should().Be(0);
        group.Children[0].Depth.Should().Be(1);
        group.Children[1].Depth.Should().Be(1);
    }

    [Fact]
    public void ItemsNestedThreeLevelsDeep_HaveDepthThree()
    {
        var level1Id = Guid.NewGuid();
        var level2Id = Guid.NewGuid();
        var level3Id = Guid.NewGuid();

        var groups = new List<ConnectionGroup>
        {
            new() { Id = level1Id, Name = "Level1" },
            new() { Id = level2Id, Name = "Level2", ParentGroupId = level1Id },
            new() { Id = level3Id, Name = "Level3", ParentGroupId = level2Id },
        };
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Deep", Hostname = "deep", GroupId = level3Id },
        };

        var result = ConnectionTreeBuilder.Build(connections, groups);

        var level1 = result[0].Should().BeOfType<GroupNode>().Subject;
        level1.Depth.Should().Be(0);

        var level2 = level1.Children[0].Should().BeOfType<GroupNode>().Subject;
        level2.Depth.Should().Be(1);

        var level3 = level2.Children[0].Should().BeOfType<GroupNode>().Subject;
        level3.Depth.Should().Be(2);

        var deep = level3.Children[0].Should().BeOfType<ConnectionNode>().Subject;
        deep.Depth.Should().Be(3);
    }

    [Fact]
    public void AfterReassignment_DepthValuesAreRecomputed()
    {
        // Build with connection inside a group (depth 1)
        var groupId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        var groups = new List<ConnectionGroup>
        {
            new() { Id = groupId, Name = "Group1" },
        };
        var connections = new List<ConnectionModel>
        {
            new() { Id = connId, Name = "Movable", Hostname = "h1", GroupId = groupId },
        };

        var result1 = ConnectionTreeBuilder.Build(connections, groups);
        var group1 = result1[0].Should().BeOfType<GroupNode>().Subject;
        group1.Children[0].Depth.Should().Be(1);

        // Rebuild with same connection at root level (no GroupId)
        var connectionsAtRoot = new List<ConnectionModel>
        {
            new() { Id = connId, Name = "Movable", Hostname = "h1", GroupId = null },
        };

        var result2 = ConnectionTreeBuilder.Build(connectionsAtRoot, groups);
        // Group is empty, connection is at root
        var conn = result2.OfType<ConnectionNode>().First(c => c.Id == connId);
        conn.Depth.Should().Be(0);
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

        var groups = new List<ConnectionGroup>
        {
            new() { Id = groupAId, Name = "GroupA", SortOrder = 1 },
            new() { Id = groupBId, Name = "GroupB", ParentGroupId = groupAId, SortOrder = 1 },
            new() { Id = groupCId, Name = "GroupC", SortOrder = 2 },
        };
        var connections = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Root", Hostname = "root", SortOrder = 0 },
            new() { Id = Guid.NewGuid(), Name = "A1", Hostname = "a1", GroupId = groupAId, SortOrder = 0 },
            new() { Id = Guid.NewGuid(), Name = "B1", Hostname = "b1", GroupId = groupBId },
            new() { Id = Guid.NewGuid(), Name = "C1", Hostname = "c1", GroupId = groupCId },
        };

        var result = ConnectionTreeBuilder.Build(connections, groups);

        // Root level: Root conn (sort=0), GroupA (sort=1), GroupC (sort=2)
        result[0].Should().BeOfType<ConnectionNode>().Which.Depth.Should().Be(0);

        var groupA = result[1].Should().BeOfType<GroupNode>().Subject;
        groupA.Depth.Should().Be(0);

        var a1 = groupA.Children[0].Should().BeOfType<ConnectionNode>().Subject;
        a1.Depth.Should().Be(1);

        var groupB = groupA.Children[1].Should().BeOfType<GroupNode>().Subject;
        groupB.Depth.Should().Be(1);

        var b1 = groupB.Children[0].Should().BeOfType<ConnectionNode>().Subject;
        b1.Depth.Should().Be(2);

        var groupC = result[2].Should().BeOfType<GroupNode>().Subject;
        groupC.Depth.Should().Be(0);

        var c1 = groupC.Children[0].Should().BeOfType<ConnectionNode>().Subject;
        c1.Depth.Should().Be(1);
    }
}
