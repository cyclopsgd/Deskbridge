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
        throw new NotImplementedException();
    }
}
