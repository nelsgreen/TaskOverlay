using System;

namespace TaskOverlay.Core;

public enum TreeNodeKind
{
    Project,
    Group,
    Task
}

public enum TreeNodeStatus
{
    Todo,
    Done
}

public enum TreeProjection
{
    AllInProject,
    ActiveOnly,
    ActivePlusAncestors,
    CurrentBranchOnly
}

public sealed record TreeNode(
    Guid Id,
    Guid? ParentId,
    int SortOrder,
    TreeNodeKind Kind,
    string Title,
    TreeNodeStatus Status,
    bool Active,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
