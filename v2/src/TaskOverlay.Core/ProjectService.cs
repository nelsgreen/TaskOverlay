using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public sealed class ProjectService
{
    private readonly AppState _state;
    private TreeStateService? _treeStateService;

    public ProjectService(AppState state)
        : this(state, treeStateService: null)
    {
    }

    internal ProjectService(AppState state, TreeStateService? treeStateService)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Projects ??= new List<ProjectItem>();
        state.Groups ??= new List<GroupItem>();
        state.Tasks ??= new List<TaskItem>();
        _state = state;
        _treeStateService = treeStateService;
    }

    public ProjectItem? CreateProject(string? name, DateTimeOffset? now = null)
    {
        var normalizedName = NormalizeName(name);
        if (normalizedName is null ||
            IsDefaultName(normalizedName) && FindDefaultProject() is not null)
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var project = new ProjectItem
        {
            Name = normalizedName,
            SortOrder = NextSortOrder(_state.Projects.Select(item => item.SortOrder)),
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        project.ColorHex = ProjectColorPalette.Resolve(project.Name, project.Id);
        _state.Projects.Add(project);
        return project;
    }

    public bool RenameProject(Guid projectId, string? newName)
    {
        var project = FindProject(projectId);
        var normalizedName = NormalizeName(newName);
        if (project is null || normalizedName is null)
        {
            return false;
        }

        var defaultProject = FindDefaultProject();
        if (ReferenceEquals(project, defaultProject) && !IsDefaultName(normalizedName))
        {
            return false;
        }

        if (IsDefaultName(normalizedName) &&
            defaultProject is not null &&
            defaultProject.Id != project.Id)
        {
            return false;
        }

        project.Name = normalizedName;
        return true;
    }

    public bool DeleteProject(Guid projectId)
    {
        var project = FindProject(projectId);
        if (project is null)
        {
            return false;
        }

        var fallbackProject = _state.Projects.FirstOrDefault(item =>
            item.Id != project.Id && IsDefaultName(item.Name));
        if (IsDefaultName(project.Name) && fallbackProject is null)
        {
            return false;
        }

        fallbackProject ??= GetOrCreateDefaultProject();
        var deletedGroupIds = _state.Groups
            .Where(group => group.ProjectId == project.Id)
            .Select(group => group.Id)
            .ToHashSet();

        foreach (var task in _state.Tasks)
        {
            if (task.ProjectId == project.Id)
            {
                task.ProjectId = fallbackProject.Id;
                task.GroupId = null;
            }
            else if (task.GroupId.HasValue && deletedGroupIds.Contains(task.GroupId.Value))
            {
                task.GroupId = null;
            }
        }

        _state.Groups.RemoveAll(group => group.ProjectId == project.Id);
        return _state.Projects.Remove(project);
    }

    public GroupItem? CreateGroup(
        Guid projectId,
        string? name,
        DateTimeOffset? now = null)
    {
        var normalizedName = NormalizeName(name);
        if (FindProject(projectId) is null || normalizedName is null)
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var group = new GroupItem
        {
            ProjectId = projectId,
            Name = normalizedName,
            SortOrder = NextProjectChildSortOrder(projectId),
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        _state.Groups.Add(group);
        return group;
    }

    public bool RenameGroup(Guid groupId, string? newName)
    {
        var group = FindGroup(groupId);
        var normalizedName = NormalizeName(newName);
        if (group is null || normalizedName is null)
        {
            return false;
        }

        group.Name = normalizedName;
        return true;
    }

    public bool DeleteGroup(Guid groupId)
    {
        var group = FindGroup(groupId);
        if (group is null)
        {
            return false;
        }

        foreach (var task in _state.Tasks.Where(task => task.GroupId == groupId))
        {
            task.GroupId = null;
        }

        return _state.Groups.Remove(group);
    }

    /// <summary>
    /// Moves a task branch to the project root, detaching its group/task parent
    /// and cascading project/group assignment to descendants.
    /// </summary>
    public bool AssignTaskToProject(Guid taskId, Guid projectId)
    {
        return TreeService.MoveNode(taskId, projectId);
    }

    /// <summary>
    /// Moves a task branch directly under a group and cascades the group's
    /// project/group assignment to descendants.
    /// </summary>
    public bool AssignTaskToGroup(Guid taskId, Guid groupId)
    {
        return TreeService.MoveNode(taskId, groupId);
    }

    /// <summary>
    /// Moves a task branch to its resolved project root, clearing both group
    /// and task-parent relationships while preserving the project.
    /// </summary>
    public bool ClearTaskGroup(Guid taskId)
    {
        var task = FindTask(taskId);
        if (task is null)
        {
            return false;
        }

        var project = ProjectReferenceResolver.ResolveProject(_state, task);
        return project is not null &&
               TreeService.MoveNode(taskId, project.Id);
    }

    private ProjectItem GetOrCreateDefaultProject()
    {
        var existing = FindDefaultProject();
        if (existing is not null)
        {
            return existing;
        }

        var project = ProjectItem.CreateDefault();
        project.SortOrder = NextSortOrder(_state.Projects.Select(item => item.SortOrder));
        _state.Projects.Add(project);
        return project;
    }

    private ProjectItem? FindDefaultProject() => _state.Projects.FirstOrDefault(project =>
        IsDefaultName(project.Name));

    private ProjectItem? FindProject(Guid projectId) =>
        _state.Projects.FirstOrDefault(project => project.Id == projectId);

    private GroupItem? FindGroup(Guid groupId) =>
        _state.Groups.FirstOrDefault(group => group.Id == groupId);

    private TaskItem? FindTask(Guid taskId) =>
        _state.Tasks.FirstOrDefault(task => task.Id == taskId);

    private TreeStateService TreeService =>
        _treeStateService ??= new TreeStateService(_state, this);

    private int NextProjectChildSortOrder(Guid projectId)
    {
        var defaultProject = FindDefaultProject();
        var groupSortOrders = _state.Groups
            .Where(group => group.ProjectId == projectId)
            .Select(group => group.SortOrder);
        var taskSortOrders = _state.Tasks
            .Where(task =>
                task.ParentTaskId is null &&
                task.GroupId is null &&
                (task.ProjectId == projectId ||
                 (defaultProject?.Id == projectId &&
                  ProjectReferenceResolver.ResolveProject(_state, task)?.Id == projectId)))
            .Select(task => task.SortOrder);
        return NextSortOrder(groupSortOrders.Concat(taskSortOrders));
    }

    private static bool IsDefaultName(string name) => string.Equals(
        name,
        ProjectItem.DefaultName,
        StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeName(string? name)
    {
        var normalizedName = name?.Trim();
        return string.IsNullOrEmpty(normalizedName) ? null : normalizedName;
    }

    private static int NextSortOrder(IEnumerable<int> sortOrders)
    {
        var sortOrderList = sortOrders.ToList();
        return sortOrderList.Count == 0 ? 0 : sortOrderList.Max() + 1;
    }
}
