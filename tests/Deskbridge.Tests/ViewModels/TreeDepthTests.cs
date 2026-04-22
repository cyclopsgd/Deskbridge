using System.Collections.ObjectModel;
using Deskbridge.ViewModels;

namespace Deskbridge.Tests.ViewModels;

[Trait("Category", "Stability")]
public sealed class TreeDepthTests
{
    [Fact]
    public void RootLevelItems_HaveDepthZero()
    {
        var items = new ObservableCollection<TreeItemViewModel>
        {
            new ConnectionTreeItemViewModel { Id = Guid.NewGuid(), Name = "Root1" },
            new ConnectionTreeItemViewModel { Id = Guid.NewGuid(), Name = "Root2" },
        };

        ConnectionTreeViewModel.AssignDepths(items, 0);

        items[0].Depth.Should().Be(0);
        items[1].Depth.Should().Be(0);
    }

    [Fact]
    public void ItemsInTopLevelGroup_HaveDepthOne()
    {
        var group = new GroupTreeItemViewModel
        {
            Id = Guid.NewGuid(),
            Name = "Group1",
            Children = new ObservableCollection<TreeItemViewModel>
            {
                new ConnectionTreeItemViewModel { Id = Guid.NewGuid(), Name = "Child1" },
                new ConnectionTreeItemViewModel { Id = Guid.NewGuid(), Name = "Child2" },
            }
        };
        var items = new ObservableCollection<TreeItemViewModel> { group };

        ConnectionTreeViewModel.AssignDepths(items, 0);

        group.Depth.Should().Be(0);
        group.Children[0].Depth.Should().Be(1);
        group.Children[1].Depth.Should().Be(1);
    }

    [Fact]
    public void ItemsNestedThreeLevelsDeep_HaveDepthThree()
    {
        var deepConn = new ConnectionTreeItemViewModel { Id = Guid.NewGuid(), Name = "Deep" };
        var level2Group = new GroupTreeItemViewModel
        {
            Id = Guid.NewGuid(),
            Name = "Level2",
            Children = new ObservableCollection<TreeItemViewModel>
            {
                new GroupTreeItemViewModel
                {
                    Id = Guid.NewGuid(),
                    Name = "Level3",
                    Children = new ObservableCollection<TreeItemViewModel> { deepConn }
                }
            }
        };
        var level1Group = new GroupTreeItemViewModel
        {
            Id = Guid.NewGuid(),
            Name = "Level1",
            Children = new ObservableCollection<TreeItemViewModel> { level2Group }
        };
        var items = new ObservableCollection<TreeItemViewModel> { level1Group };

        ConnectionTreeViewModel.AssignDepths(items, 0);

        level1Group.Depth.Should().Be(0);
        level2Group.Depth.Should().Be(1);
        ((GroupTreeItemViewModel)level2Group.Children[0]).Depth.Should().Be(2);
        deepConn.Depth.Should().Be(3);
    }

    [Fact]
    public void AfterReassignment_DepthValuesAreRecomputed()
    {
        var conn = new ConnectionTreeItemViewModel { Id = Guid.NewGuid(), Name = "Movable" };
        var group = new GroupTreeItemViewModel
        {
            Id = Guid.NewGuid(),
            Name = "Group1",
            Children = new ObservableCollection<TreeItemViewModel> { conn }
        };
        var items = new ObservableCollection<TreeItemViewModel> { group };

        // First assignment: conn at depth 1
        ConnectionTreeViewModel.AssignDepths(items, 0);
        conn.Depth.Should().Be(1);

        // Move conn to root level and reassign
        group.Children.Clear();
        items.Add(conn);
        ConnectionTreeViewModel.AssignDepths(items, 0);
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

        var connRoot = new ConnectionTreeItemViewModel { Id = Guid.NewGuid(), Name = "Root" };
        var connA1 = new ConnectionTreeItemViewModel { Id = Guid.NewGuid(), Name = "A1" };
        var connB1 = new ConnectionTreeItemViewModel { Id = Guid.NewGuid(), Name = "B1" };
        var connC1 = new ConnectionTreeItemViewModel { Id = Guid.NewGuid(), Name = "C1" };

        var groupB = new GroupTreeItemViewModel
        {
            Id = Guid.NewGuid(),
            Name = "GroupB",
            Children = new ObservableCollection<TreeItemViewModel> { connB1 }
        };
        var groupA = new GroupTreeItemViewModel
        {
            Id = Guid.NewGuid(),
            Name = "GroupA",
            Children = new ObservableCollection<TreeItemViewModel> { connA1, groupB }
        };
        var groupC = new GroupTreeItemViewModel
        {
            Id = Guid.NewGuid(),
            Name = "GroupC",
            Children = new ObservableCollection<TreeItemViewModel> { connC1 }
        };

        var items = new ObservableCollection<TreeItemViewModel> { connRoot, groupA, groupC };

        ConnectionTreeViewModel.AssignDepths(items, 0);

        connRoot.Depth.Should().Be(0);
        groupA.Depth.Should().Be(0);
        connA1.Depth.Should().Be(1);
        groupB.Depth.Should().Be(1);
        connB1.Depth.Should().Be(2);
        groupC.Depth.Should().Be(0);
        connC1.Depth.Should().Be(1);
    }
}
