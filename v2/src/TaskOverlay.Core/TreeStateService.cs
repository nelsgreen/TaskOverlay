using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public sealed class TreeStateService
{
    private readonly AppState _state;
    private readonly ProjectService _projectService;

    public TreeStateService(AppState state)
        : this(state, projectService: null)
    {
    }

    internal TreeStateService(AppState state, ProjectService? projectService)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Projects ??= new List<ProjectItem>();
        state.Groups ??= new List<GroupItem>();
        state.Tasks ??= new List<TaskItem>();
        _state = state;
        _projectService = projectService ?? new ProjectService(state, this);
    }

    public TreeNode? CreateProject(string? title, DateTimeOffset? now = null)
    {
        var normalizedTitle = NormalizeTitle(title);
        if (normalizedTitle is null)
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var project = _projectService.CreateProject(normalizedTitle, timestamp);
        if (project is null)
        {
            return null;
        }

        project.UpdatedAtUtc = timestamp;
        return ToNode(new NodeEntry(TreeNodeKind.Project, project));
    }

    public TreeNode? CreateGroup(
        Guid projectId,
        string? title,
        DateTimeOffset? now = null)
    {
        var normalizedTitle = NormalizeTitle(title);
        if (normalizedTitle is null || FindProject(projectId) is null)
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var group = _projectService.CreateGroup(projectId, normalizedTitle, timestamp);
        if (group is null)
        {
            return null;
        }

        group.UpdatedAtUtc = timestamp;
        return ToNode(new NodeEntry(TreeNodeKind.Group, group));
    }

    public TreeNode? CreateTask(
        Guid parentId,
        string? title,
        DateTimeOffset? now = null)
    {
        var normalizedTitle = NormalizeTitle(title);
        var parent = FindNode(parentId);
        if (normalizedTitle is null || parent is null)
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var task = TaskItem.Create(normalizedTitle, timestamp);
        if (!AssignTaskParent(task, parent.Value))
        {
            return null;
        }

        task.SortOrder = NextSortOrder(parentId);
        task.UpdatedAtUtc = timestamp;
        _state.Tasks.Add(task);
        return ToNode(new NodeEntry(TreeNodeKind.Task, task));
    }

    public bool RenameNode(Guid nodeId, string? title, DateTimeOffset? now = null)
    {
        var normalizedTitle = NormalizeTitle(title);
        var node = FindNode(nodeId);
        if (normalizedTitle is null || node is null)
        {
            return false;
        }

        var renamed = node.Value.Kind switch
        {
            TreeNodeKind.Project => _projectService.RenameProject(nodeId, normalizedTitle),
            TreeNodeKind.Group => _projectService.RenameGroup(nodeId, normalizedTitle),
            TreeNodeKind.Task => RenameTask((TaskItem)node.Value.Value, normalizedTitle),
            _ => false
        };
        if (renamed)
        {
            Touch(node.Value, now);
        }

        return renamed;
    }

    public bool DeleteNode(Guid nodeId, DateTimeOffset? now = null)
    {
        var node = FindNode(nodeId);
        if (node is null)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var oldParentId = ResolveParentId(node.Value);
        switch (node.Value.Kind)
        {
            case TreeNodeKind.Project:
            {
                var before = _state.Tasks.ToDictionary(
                    task => task.Id,
                    task => (task.ProjectId, task.GroupId));
                if (!_projectService.DeleteProject(nodeId))
                {
                    return false;
                }

                TouchChangedTaskAssignments(before, timestamp);
                break;
            }
            case TreeNodeKind.Group:
            {
                var before = _state.Tasks.ToDictionary(
                    task => task.Id,
                    task => (task.ProjectId, task.GroupId));
                if (!_projectService.DeleteGroup(nodeId))
                {
                    return false;
                }

                TouchChangedTaskAssignments(before, timestamp);
                break;
            }
            case TreeNodeKind.Task:
            {
                var task = (TaskItem)node.Value.Value;
                var replacementParent = oldParentId.HasValue
                    ? FindNode(oldParentId.Value)
                    : null;
                var children = _state.Tasks
                    .Where(item => item.ParentTaskId == task.Id)
                    .ToList();
                var assignmentPlans = new List<(TaskItem Task, TaskParentAssignment Assignment)>();
                foreach (var child in children)
                {
                    if (replacementParent is null ||
                        !TryPlanTaskParent(replacementParent.Value, out var assignment))
                    {
                        return false;
                    }

                    assignmentPlans.Add((child, assignment));
                }

                foreach (var plan in assignmentPlans)
                {
                    ApplyTaskParent(plan.Task, plan.Assignment);
                    plan.Task.UpdatedAtUtc = timestamp;
                }

                if (!_state.Tasks.Remove(task))
                {
                    return false;
                }

                break;
            }
            default:
                return false;
        }

        NormalizeSiblingOrder(oldParentId, timestamp);
        return true;
    }

    public bool MoveNode(
        Guid nodeId,
        Guid newParentId,
        DateTimeOffset? now = null)
    {
        var node = FindNode(nodeId);
        var newParent = FindNode(newParentId);
        if (node is null ||
            newParent is null ||
            nodeId == newParentId ||
            node.Value.Kind == TreeNodeKind.Project ||
            GetDescendants(nodeId).Any(descendant => descendant.Id == newParentId))
        {
            return false;
        }

        var oldParentId = ResolveParentId(node.Value);
        if (oldParentId == newParentId)
        {
            return true;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        switch (node.Value.Kind)
        {
            case TreeNodeKind.Group when newParent.Value.Kind == TreeNodeKind.Project:
            {
                var group = (GroupItem)node.Value.Value;
                group.ProjectId = ((ProjectItem)newParent.Value.Value).Id;
                foreach (var task in _state.Tasks.Where(task => task.GroupId == group.Id))
                {
                    task.ProjectId = group.ProjectId;
                    task.UpdatedAtUtc = timestamp;
                }

                break;
            }
            case TreeNodeKind.Task:
            {
                var movingTask = (TaskItem)node.Value.Value;
                if (!AssignTaskParent(movingTask, newParent.Value))
                {
                    return false;
                }

                foreach (var descendant in GetDescendants(nodeId).Where(item =>
                             item.Kind == TreeNodeKind.Task))
                {
                    var descendantTask = FindTask(descendant.Id)!;
                    descendantTask.ProjectId = movingTask.ProjectId;
                    descendantTask.GroupId = movingTask.GroupId;
                    descendantTask.UpdatedAtUtc = timestamp;
                }

                break;
            }
            default:
                return false;
        }

        SetSortOrder(node.Value, NextSortOrder(newParentId, nodeId));
        Touch(node.Value, timestamp);
        NormalizeSiblingOrder(oldParentId, timestamp);
        NormalizeSiblingOrder(newParentId, timestamp);
        return true;
    }

    public bool ReorderNode(Guid nodeId, int newIndex, DateTimeOffset? now = null)
    {
        var node = FindNode(nodeId);
        if (node is null)
        {
            return false;
        }

        var parentId = ResolveParentId(node.Value);
        var siblings = GetChildren(parentId)
            .Select(item => FindNode(item.Id))
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToList();
        var currentIndex = siblings.FindIndex(item => GetId(item) == nodeId);
        if (currentIndex < 0)
        {
            return false;
        }

        var targetIndex = Math.Clamp(newIndex, 0, siblings.Count - 1);
        var movingNode = siblings[currentIndex];
        siblings.RemoveAt(currentIndex);
        siblings.Insert(targetIndex, movingNode);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        for (var index = 0; index < siblings.Count; index++)
        {
            if (GetSortOrder(siblings[index]) != index)
            {
                SetSortOrder(siblings[index], index);
                Touch(siblings[index], timestamp);
            }
        }

        return true;
    }

    public bool MarkActive(Guid nodeId, bool active, DateTimeOffset? now = null)
    {
        var node = FindNode(nodeId);
        if (node is null)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        switch (node.Value.Kind)
        {
            case TreeNodeKind.Task:
            {
                var task = (TaskItem)node.Value.Value;
                if (active && task.Completed)
                {
                    return false;
                }

                var before = _state.Tasks.ToDictionary(item => item.Id, item => item.InWork);
                TaskInteractionService.SetInWork(_state, task, active);
                foreach (var changedTask in _state.Tasks.Where(item => before[item.Id] != item.InWork))
                {
                    changedTask.UpdatedAtUtc = timestamp;
                }

                return true;
            }
            default:
                return false;
        }
    }

    public bool MarkStatus(
        Guid nodeId,
        TreeNodeStatus status,
        DateTimeOffset? now = null)
    {
        var node = FindNode(nodeId);
        if (node is null || node.Value.Kind != TreeNodeKind.Task)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var task = (TaskItem)node.Value.Value;
        if (status == TreeNodeStatus.Done)
        {
            TaskInteractionService.Complete(task, timestamp);
        }
        else
        {
            TaskInteractionService.SetStatus(
                _state,
                task,
                TaskStatus.Todo,
                timestamp);
        }

        task.UpdatedAtUtc = timestamp;
        return true;
    }

    public TreeNode? GetNode(Guid nodeId)
    {
        var node = FindNode(nodeId);
        return node.HasValue ? ToNode(node.Value) : null;
    }

    public IReadOnlyList<TreeNode> GetChildren(Guid? parentId)
    {
        return GetAllNodeEntries()
            .Where(node => ResolveParentId(node) == parentId)
            .OrderBy(GetSortOrder)
            .ThenBy(GetCreatedAtUtc)
            .ThenBy(GetId)
            .Select(ToNode)
            .ToList();
    }

    public IReadOnlyList<TreeNode> GetAncestors(Guid nodeId)
    {
        var ancestors = new List<TreeNode>();
        var visited = new HashSet<Guid> { nodeId };
        var current = FindNode(nodeId);
        while (current.HasValue)
        {
            var parentId = ResolveParentId(current.Value);
            if (!parentId.HasValue || !visited.Add(parentId.Value))
            {
                break;
            }

            var parent = FindNode(parentId.Value);
            if (!parent.HasValue)
            {
                break;
            }

            ancestors.Add(ToNode(parent.Value));
            current = parent;
        }

        ancestors.Reverse();
        return ancestors;
    }

    public IReadOnlyList<TreeNode> GetDescendants(Guid nodeId)
    {
        var descendants = new List<TreeNode>();
        var visited = new HashSet<Guid> { nodeId };
        AppendDescendants(nodeId, descendants, visited);
        return descendants;
    }

    public IReadOnlyList<TreeNode> GetCurrentBranch(Guid nodeId)
    {
        var projectRoot = GetProjectRoot(nodeId);
        if (projectRoot is null)
        {
            return Array.Empty<TreeNode>();
        }

        var includedIds = GetAncestors(nodeId).Select(node => node.Id).ToHashSet();
        includedIds.Add(nodeId);
        includedIds.UnionWith(GetDescendants(nodeId).Select(node => node.Id));
        return FlattenProject(projectRoot.Id)
            .Where(node => includedIds.Contains(node.Id))
            .ToList();
    }

    public TreeNode? GetProjectRoot(Guid nodeId)
    {
        var node = FindNode(nodeId);
        if (node is null)
        {
            return null;
        }

        if (node.Value.Kind == TreeNodeKind.Project)
        {
            return ToNode(node.Value);
        }

        return GetAncestors(nodeId).FirstOrDefault(item => item.Kind == TreeNodeKind.Project);
    }

    public IReadOnlyList<TreeNode> GetProjection(
        Guid projectId,
        TreeProjection projection,
        Guid? selectedNodeId = null)
    {
        if (FindProject(projectId) is null)
        {
            return Array.Empty<TreeNode>();
        }

        var allNodes = FlattenProject(projectId);
        return projection switch
        {
            TreeProjection.AllInProject => allNodes,
            TreeProjection.ActiveOnly => allNodes
                .Where(node => node.Kind == TreeNodeKind.Task && node.Active)
                .ToList(),
            TreeProjection.ActivePlusAncestors => ActivePlusAncestors(allNodes),
            TreeProjection.CurrentBranchOnly when selectedNodeId.HasValue =>
                CurrentBranchProjection(projectId, selectedNodeId.Value, allNodes),
            _ => Array.Empty<TreeNode>()
        };
    }

    private IReadOnlyList<TreeNode> ActivePlusAncestors(IReadOnlyList<TreeNode> allNodes)
    {
        var includedIds = new HashSet<Guid>();
        foreach (var activeTask in allNodes.Where(node =>
                     node.Kind == TreeNodeKind.Task && node.Active))
        {
            includedIds.Add(activeTask.Id);
            includedIds.UnionWith(GetAncestors(activeTask.Id).Select(node => node.Id));
        }

        return allNodes.Where(node => includedIds.Contains(node.Id)).ToList();
    }

    private IReadOnlyList<TreeNode> CurrentBranchProjection(
        Guid projectId,
        Guid selectedNodeId,
        IReadOnlyList<TreeNode> allNodes)
    {
        if (GetProjectRoot(selectedNodeId)?.Id != projectId)
        {
            return Array.Empty<TreeNode>();
        }

        var includedIds = GetAncestors(selectedNodeId).Select(node => node.Id).ToHashSet();
        includedIds.Add(selectedNodeId);
        includedIds.UnionWith(GetDescendants(selectedNodeId).Select(node => node.Id));
        return allNodes.Where(node => includedIds.Contains(node.Id)).ToList();
    }

    private IReadOnlyList<TreeNode> FlattenProject(Guid projectId)
    {
        var project = FindNode(projectId);
        if (project is null || project.Value.Kind != TreeNodeKind.Project)
        {
            return Array.Empty<TreeNode>();
        }

        var nodes = new List<TreeNode> { ToNode(project.Value) };
        var visited = new HashSet<Guid> { projectId };
        AppendDescendants(projectId, nodes, visited);
        return nodes;
    }

    private void AppendDescendants(
        Guid parentId,
        ICollection<TreeNode> nodes,
        ISet<Guid> visited)
    {
        foreach (var child in GetChildren(parentId))
        {
            if (!visited.Add(child.Id))
            {
                continue;
            }

            nodes.Add(child);
            AppendDescendants(child.Id, nodes, visited);
        }
    }

    private bool AssignTaskParent(TaskItem task, NodeEntry parent)
    {
        if (!TryPlanTaskParent(parent, out var assignment))
        {
            return false;
        }

        ApplyTaskParent(task, assignment);
        return true;
    }

    private bool TryPlanTaskParent(
        NodeEntry parent,
        out TaskParentAssignment assignment)
    {
        assignment = default;
        switch (parent.Kind)
        {
            case TreeNodeKind.Project:
                assignment = new TaskParentAssignment(
                    ((ProjectItem)parent.Value).Id,
                    GroupId: null,
                    ParentTaskId: null);
                return true;
            case TreeNodeKind.Group:
            {
                var group = (GroupItem)parent.Value;
                if (FindProject(group.ProjectId) is null)
                {
                    return false;
                }

                assignment = new TaskParentAssignment(
                    group.ProjectId,
                    group.Id,
                    ParentTaskId: null);
                return true;
            }
            case TreeNodeKind.Task:
            {
                var parentTask = (TaskItem)parent.Value;
                var projectRoot = GetProjectRoot(parentTask.Id);
                if (projectRoot is null)
                {
                    return false;
                }

                var groupId = parentTask.GroupId.HasValue &&
                              FindGroup(parentTask.GroupId.Value) is not null
                    ? parentTask.GroupId
                    : null;
                assignment = new TaskParentAssignment(
                    projectRoot.Id,
                    groupId,
                    parentTask.Id);
                return true;
            }
            default:
                return false;
        }
    }

    private static void ApplyTaskParent(
        TaskItem task,
        TaskParentAssignment assignment)
    {
        task.ProjectId = assignment.ProjectId;
        task.GroupId = assignment.GroupId;
        task.ParentTaskId = assignment.ParentTaskId;
    }

    private Guid? ResolveParentId(NodeEntry node)
    {
        return node.Kind switch
        {
            TreeNodeKind.Project => null,
            TreeNodeKind.Group => ResolveGroupParent((GroupItem)node.Value),
            TreeNodeKind.Task => ResolveTaskParent((TaskItem)node.Value),
            _ => null
        };
    }

    private Guid? ResolveGroupParent(GroupItem group)
    {
        return FindProject(group.ProjectId)?.Id ?? FindDefaultProject()?.Id;
    }

    private Guid? ResolveTaskParent(TaskItem task)
    {
        if (task.ParentTaskId.HasValue &&
            task.ParentTaskId.Value != task.Id &&
            FindTask(task.ParentTaskId.Value) is not null)
        {
            return task.ParentTaskId;
        }

        if (task.GroupId.HasValue && FindGroup(task.GroupId.Value) is GroupItem group)
        {
            return FindProject(group.ProjectId) is not null ? group.Id : FindDefaultProject()?.Id;
        }

        return ProjectReferenceResolver.ResolveProject(_state, task)?.Id;
    }

    private TreeNode ToNode(NodeEntry node)
    {
        return node.Kind switch
        {
            TreeNodeKind.Project => ToNode((ProjectItem)node.Value),
            TreeNodeKind.Group => ToNode((GroupItem)node.Value),
            TreeNodeKind.Task => ToNode((TaskItem)node.Value),
            _ => throw new InvalidOperationException("Unknown tree node kind.")
        };
    }

    private TreeNode ToNode(ProjectItem project) => new(
        project.Id,
        ParentId: null,
        project.SortOrder,
        TreeNodeKind.Project,
        project.Name,
        TreeNodeStatus.Todo,
        Active: false,
        project.CreatedAtUtc,
        project.UpdatedAtUtc);

    private TreeNode ToNode(GroupItem group) => new(
        group.Id,
        ResolveGroupParent(group),
        group.SortOrder,
        TreeNodeKind.Group,
        group.Name,
        TreeNodeStatus.Todo,
        Active: false,
        group.CreatedAtUtc,
        group.UpdatedAtUtc);

    private TreeNode ToNode(TaskItem task) => new(
        task.Id,
        ResolveTaskParent(task),
        task.SortOrder,
        TreeNodeKind.Task,
        task.Title,
        task.Status == TaskStatus.Done ? TreeNodeStatus.Done : TreeNodeStatus.Todo,
        task.InWork,
        task.CreatedAtUtc,
        task.UpdatedAtUtc);

    private IEnumerable<NodeEntry> GetAllNodeEntries()
    {
        foreach (var project in _state.Projects)
        {
            yield return new NodeEntry(TreeNodeKind.Project, project);
        }

        foreach (var group in _state.Groups)
        {
            yield return new NodeEntry(TreeNodeKind.Group, group);
        }

        foreach (var task in _state.Tasks)
        {
            yield return new NodeEntry(TreeNodeKind.Task, task);
        }
    }

    private NodeEntry? FindNode(Guid nodeId)
    {
        if (FindProject(nodeId) is ProjectItem project)
        {
            return new NodeEntry(TreeNodeKind.Project, project);
        }

        if (FindGroup(nodeId) is GroupItem group)
        {
            return new NodeEntry(TreeNodeKind.Group, group);
        }

        return FindTask(nodeId) is TaskItem task
            ? new NodeEntry(TreeNodeKind.Task, task)
            : null;
    }

    private ProjectItem? FindDefaultProject() => _state.Projects.FirstOrDefault(project =>
        string.Equals(project.Name, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase));

    private ProjectItem? FindProject(Guid projectId) =>
        _state.Projects.FirstOrDefault(project => project.Id == projectId);

    private GroupItem? FindGroup(Guid groupId) =>
        _state.Groups.FirstOrDefault(group => group.Id == groupId);

    private TaskItem? FindTask(Guid taskId) =>
        _state.Tasks.FirstOrDefault(task => task.Id == taskId);

    private int NextSortOrder(Guid? parentId, Guid? excludedNodeId = null)
    {
        var sortOrders = GetChildren(parentId)
            .Where(node => node.Id != excludedNodeId)
            .Select(node => node.SortOrder)
            .ToList();
        return sortOrders.Count == 0 ? 0 : sortOrders.Max() + 1;
    }

    private void NormalizeSiblingOrder(Guid? parentId, DateTimeOffset timestamp)
    {
        var siblings = GetChildren(parentId);
        for (var index = 0; index < siblings.Count; index++)
        {
            var sibling = FindNode(siblings[index].Id);
            if (sibling.HasValue && GetSortOrder(sibling.Value) != index)
            {
                SetSortOrder(sibling.Value, index);
                Touch(sibling.Value, timestamp);
            }
        }
    }

    private void TouchChangedTaskAssignments(
        IReadOnlyDictionary<Guid, (Guid? ProjectId, Guid? GroupId)> before,
        DateTimeOffset timestamp)
    {
        foreach (var task in _state.Tasks)
        {
            if (before.TryGetValue(task.Id, out var previous) &&
                (previous.ProjectId != task.ProjectId || previous.GroupId != task.GroupId))
            {
                task.UpdatedAtUtc = timestamp;
            }
        }
    }

    private static bool RenameTask(TaskItem task, string title)
    {
        task.Title = title;
        return true;
    }

    private static string? NormalizeTitle(string? title)
    {
        var normalizedTitle = title?.Trim();
        return string.IsNullOrEmpty(normalizedTitle) ? null : normalizedTitle;
    }

    private static Guid GetId(NodeEntry node) => node.Kind switch
    {
        TreeNodeKind.Project => ((ProjectItem)node.Value).Id,
        TreeNodeKind.Group => ((GroupItem)node.Value).Id,
        TreeNodeKind.Task => ((TaskItem)node.Value).Id,
        _ => Guid.Empty
    };

    private static int GetSortOrder(NodeEntry node) => node.Kind switch
    {
        TreeNodeKind.Project => ((ProjectItem)node.Value).SortOrder,
        TreeNodeKind.Group => ((GroupItem)node.Value).SortOrder,
        TreeNodeKind.Task => ((TaskItem)node.Value).SortOrder,
        _ => 0
    };

    private static DateTimeOffset GetCreatedAtUtc(NodeEntry node) => node.Kind switch
    {
        TreeNodeKind.Project => ((ProjectItem)node.Value).CreatedAtUtc,
        TreeNodeKind.Group => ((GroupItem)node.Value).CreatedAtUtc,
        TreeNodeKind.Task => ((TaskItem)node.Value).CreatedAtUtc,
        _ => DateTimeOffset.MinValue
    };

    private static void SetSortOrder(NodeEntry node, int sortOrder)
    {
        switch (node.Kind)
        {
            case TreeNodeKind.Project:
                ((ProjectItem)node.Value).SortOrder = sortOrder;
                break;
            case TreeNodeKind.Group:
                ((GroupItem)node.Value).SortOrder = sortOrder;
                break;
            case TreeNodeKind.Task:
                ((TaskItem)node.Value).SortOrder = sortOrder;
                break;
        }
    }

    private static void Touch(NodeEntry node, DateTimeOffset? now)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        switch (node.Kind)
        {
            case TreeNodeKind.Project:
                ((ProjectItem)node.Value).UpdatedAtUtc = timestamp;
                break;
            case TreeNodeKind.Group:
                ((GroupItem)node.Value).UpdatedAtUtc = timestamp;
                break;
            case TreeNodeKind.Task:
                ((TaskItem)node.Value).UpdatedAtUtc = timestamp;
                break;
        }
    }

    private readonly record struct NodeEntry(TreeNodeKind Kind, object Value);

    private readonly record struct TaskParentAssignment(
        Guid ProjectId,
        Guid? GroupId,
        Guid? ParentTaskId);
}
