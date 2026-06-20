using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public sealed class ProjectService
{
    private readonly AppState _state;

    public ProjectService(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Projects ??= new List<ProjectItem>();
        state.Groups ??= new List<GroupItem>();
        state.Tasks ??= new List<TaskItem>();
        _state = state;
    }

    public ProjectItem? CreateProject(string? name, DateTimeOffset? now = null)
    {
        var normalizedName = NormalizeName(name);
        if (normalizedName is null ||
            IsDefaultName(normalizedName) && FindDefaultProject() is not null)
        {
            return null;
        }

        var project = new ProjectItem
        {
            Name = normalizedName,
            SortOrder = NextSortOrder(_state.Projects.Select(item => item.SortOrder)),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow
        };
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

        var group = new GroupItem
        {
            ProjectId = projectId,
            Name = normalizedName,
            SortOrder = NextSortOrder(_state.Groups
                .Where(item => item.ProjectId == projectId)
                .Select(item => item.SortOrder)),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow
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

    public bool AssignTaskToProject(Guid taskId, Guid projectId)
    {
        var task = FindTask(taskId);
        var project = FindProject(projectId);
        if (task is null || project is null)
        {
            return false;
        }

        if (task.GroupId.HasValue)
        {
            var currentGroup = FindGroup(task.GroupId.Value);
            if (currentGroup is null || currentGroup.ProjectId != project.Id)
            {
                task.GroupId = null;
            }
        }

        task.ProjectId = project.Id;
        return true;
    }

    public bool AssignTaskToGroup(Guid taskId, Guid groupId)
    {
        var task = FindTask(taskId);
        var group = FindGroup(groupId);
        if (task is null || group is null || FindProject(group.ProjectId) is null)
        {
            return false;
        }

        task.ProjectId = group.ProjectId;
        task.GroupId = group.Id;
        return true;
    }

    public bool ClearTaskGroup(Guid taskId)
    {
        var task = FindTask(taskId);
        if (task is null)
        {
            return false;
        }

        task.GroupId = null;
        return true;
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
