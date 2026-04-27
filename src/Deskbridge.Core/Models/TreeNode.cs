namespace Deskbridge.Core.Models;

/// <summary>
/// Pure tree result types for ConnectionTreeBuilder. Immutable records
/// representing the built tree structure with depth values computed
/// during construction.
/// </summary>
public abstract record TreeNode(Guid Id, string Name, int SortOrder, int Depth);

public sealed record GroupNode(
    Guid Id,
    string Name,
    int SortOrder,
    int Depth,
    Guid? ParentGroupId,
    IReadOnlyList<TreeNode> Children) : TreeNode(Id, Name, SortOrder, Depth);

public sealed record ConnectionNode(
    Guid Id,
    string Name,
    int SortOrder,
    int Depth,
    Guid? GroupId,
    string Hostname) : TreeNode(Id, Name, SortOrder, Depth);
