using Deskbridge.Core.Models;

namespace Deskbridge.Core.Services;

/// <summary>
/// Pure static tree-building logic extracted from ConnectionTreeViewModel.
/// Builds a sorted, depth-assigned tree from flat connection and group lists
/// with cycle detection. No UI or DI dependencies.
/// </summary>
public static class ConnectionTreeBuilder
{
    /// <summary>
    /// Build a tree of <see cref="TreeNode"/> records from flat model lists.
    /// Groups are nested by ParentGroupId, connections placed by GroupId.
    /// Cyclic groups are promoted to root. Items sorted by SortOrder ascending,
    /// Name case-insensitive tiebreaker.
    /// </summary>
    public static IReadOnlyList<TreeNode> Build(
        IReadOnlyList<ConnectionModel> connections,
        IReadOnlyList<ConnectionGroup> groups)
    {
        // Step 1: Build group lookup for parent-chain walking
        var groupLookup = groups.ToDictionary(g => g.Id);

        // Step 2: Cycle detection -- identify groups that participate in cycles
        var cyclicGroupIds = new HashSet<Guid>();
        foreach (var group in groups)
        {
            if (group.ParentGroupId is null)
                continue;

            var visited = new HashSet<Guid> { group.Id };
            var cursor = group.ParentGroupId;
            while (cursor is not null)
            {
                if (!visited.Add(cursor.Value))
                {
                    // Cycle detected -- mark all groups in the visited set as cyclic
                    Serilog.Log.Warning(
                        "Cycle detected in group parent chain at {GroupId}; promoting to root",
                        group.Id);
                    cyclicGroupIds.Add(group.Id);
                    break;
                }
                if (!groupLookup.TryGetValue(cursor.Value, out var next))
                    break;
                cursor = next.ParentGroupId;
            }
        }

        // Step 3: Build intermediate children structure using mutable lists
        // Key = parent group id, Value = list of child objects (ConnectionGroup or ConnectionModel)
        var groupChildren = new Dictionary<Guid, List<object>>();
        var rootItems = new List<object>();

        // Initialize children lists for all groups
        foreach (var group in groups)
        {
            groupChildren[group.Id] = new List<object>();
        }

        // Place groups: cyclic or no valid parent => root, else into parent's children
        foreach (var group in groups)
        {
            if (cyclicGroupIds.Contains(group.Id))
            {
                rootItems.Add(group);
            }
            else if (group.ParentGroupId is not null
                     && groupLookup.ContainsKey(group.ParentGroupId.Value)
                     && !cyclicGroupIds.Contains(group.ParentGroupId.Value))
            {
                groupChildren[group.ParentGroupId.Value].Add(group);
            }
            else
            {
                rootItems.Add(group);
            }
        }

        // Step 4: Place connections into their groups or root
        foreach (var conn in connections)
        {
            if (conn.GroupId is not null && groupChildren.ContainsKey(conn.GroupId.Value))
            {
                groupChildren[conn.GroupId.Value].Add(conn);
            }
            else
            {
                rootItems.Add(conn);
            }
        }

        // Step 5: Recursively sort and construct immutable records with depth
        return BuildRecords(rootItems, groupChildren, depth: 0);
    }

    private static IReadOnlyList<TreeNode> BuildRecords(
        List<object> items,
        Dictionary<Guid, List<object>> groupChildren,
        int depth)
    {
        // Sort by SortOrder ascending, Name case-insensitive tiebreaker
        var sorted = items
            .OrderBy(GetSortOrder)
            .ThenBy(GetName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<TreeNode>(sorted.Count);
        foreach (var item in sorted)
        {
            switch (item)
            {
                case ConnectionGroup g:
                    var children = groupChildren.TryGetValue(g.Id, out var childList)
                        ? BuildRecords(childList, groupChildren, depth + 1)
                        : Array.Empty<TreeNode>();
                    result.Add(new GroupNode(
                        g.Id,
                        g.Name,
                        g.SortOrder,
                        depth,
                        g.ParentGroupId,
                        children));
                    break;

                case ConnectionModel c:
                    result.Add(new ConnectionNode(
                        c.Id,
                        c.Name,
                        c.SortOrder,
                        depth,
                        c.GroupId,
                        c.Hostname));
                    break;
            }
        }

        return result;
    }

    private static int GetSortOrder(object item) => item switch
    {
        ConnectionGroup g => g.SortOrder,
        ConnectionModel c => c.SortOrder,
        _ => 0,
    };

    private static string GetName(object item) => item switch
    {
        ConnectionGroup g => g.Name,
        ConnectionModel c => c.Name,
        _ => string.Empty,
    };
}
