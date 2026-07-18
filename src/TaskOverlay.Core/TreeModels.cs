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
    Focus,
    Wait,
    Done
}

public enum TreeProjection
{
    AllInProject,
    ActiveOnly,
    ActivePlusAncestors,
    CurrentBranchOnly
}

public enum TreeManagerFilter
{
    All,
    ActiveOnly,
    ActivePlusAncestors
}

public enum TreeManagerView
{
    Tree,
    Status
}

public enum TreeManagerStatusFilter
{
    All,
    Panel,
    Focus,
    Wait,
    Remind,
    Todo,
    Done
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
