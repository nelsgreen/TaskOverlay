using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TaskOverlay.Core;

public static class StateMigrator
{
    public static AppState Migrate(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Projects ??= new List<ProjectItem>();
        state.Groups ??= new List<GroupItem>();

        if (state.SchemaVersion == AppState.CurrentSchemaVersion)
        {
            return state;
        }

        if (state.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported schema version: {state.SchemaVersion}.");
        }

        state.Tasks ??= new List<TaskItem>();

        var defaultProject = state.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase));

        if (defaultProject is null)
        {
            var createdAtUtc = state.CreatedAtUtc == default
                ? DateTimeOffset.UtcNow
                : state.CreatedAtUtc;
            defaultProject = ProjectItem.CreateDefault(createdAtUtc);
            state.Projects.Add(defaultProject);
        }
        else if (defaultProject.Id == Guid.Empty)
        {
            defaultProject.Id = Guid.NewGuid();
        }

        foreach (var task in state.Tasks)
        {
            task.ProjectId = defaultProject.Id;
            task.GroupId = null;
        }

        state.SchemaVersion = AppState.CurrentSchemaVersion;
        return state;
    }

    public static bool RepairCurrentState(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var changed = false;
        if (state.Projects is null)
        {
            state.Projects = new List<ProjectItem>();
            changed = true;
        }

        if (state.Groups is null)
        {
            state.Groups = new List<GroupItem>();
            changed = true;
        }

        if (state.Tasks is null)
        {
            state.Tasks = new List<TaskItem>();
            changed = true;
        }

        if (state.OverlaySettings is not null &&
            state.OverlaySettings.NormalizeOverlayMode())
        {
            changed = true;
        }

        if (state.OverlaySettings is not null &&
            state.OverlaySettings.NormalizeWorkingPresentation())
        {
            changed = true;
        }

        if (state.WindowPlacement is not null &&
            UtilityShellGeometryPolicy.Normalize(state.WindowPlacement))
        {
            changed = true;
        }

        var defaultProject = state.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase));
        if (defaultProject is null)
        {
            var timestamp = state.CreatedAtUtc == default
                ? DateTimeOffset.UtcNow
                : state.CreatedAtUtc;
            defaultProject = ProjectItem.CreateDefault(timestamp);
            defaultProject.SortOrder = state.Projects.Count == 0
                ? 0
                : state.Projects.Max(project => project.SortOrder) + 1;
            state.Projects.Add(defaultProject);
            changed = true;
        }
        else if (defaultProject.Id == Guid.Empty)
        {
            defaultProject.Id = Guid.NewGuid();
            changed = true;
        }

        var projectIds = state.Projects.Select(project => project.Id).ToHashSet();
        foreach (var project in state.Projects)
        {
            if (!ProjectColorPalette.IsValid(project.ColorHex))
            {
                project.ColorHex = ProjectColorPalette.Resolve(project.Name, project.Id);
                changed = true;
            }
        }

        foreach (var group in state.Groups)
        {
            if (!projectIds.Contains(group.ProjectId))
            {
                group.ProjectId = defaultProject.Id;
                changed = true;
            }
        }

        var groupsById = state.Groups
            .GroupBy(group => group.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var tasksById = state.Tasks
            .GroupBy(task => task.Id)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var task in state.Tasks)
        {
            var normalizedStatus = task.StoredStatus ??
                                   (task.Completed
                                       ? TaskStatus.Done
                                       : task.InWork
                                           ? TaskStatus.InWork
                                           : TaskStatus.Todo);
            var shouldBeCompleted = normalizedStatus == TaskStatus.Done;
            var shouldBeInWork = normalizedStatus == TaskStatus.InWork;
            if (task.StoredStatus != normalizedStatus ||
                task.Completed != shouldBeCompleted ||
                task.InWork != shouldBeInWork)
            {
                task.Status = normalizedStatus;
                task.Completed = shouldBeCompleted;
                task.InWork = shouldBeInWork;
                changed = true;
            }

            if (task.WaitingFor is null)
            {
                task.WaitingFor = string.Empty;
                changed = true;
            }

            if (task.RemindEveryMinutes <= 0)
            {
                task.RemindEveryMinutes = null;
                changed = true;
            }

            if (task.Completed && task.ReminderActive)
            {
                task.ReminderActive = false;
                changed = true;
            }

            if (task.ParentTaskId == task.Id ||
                task.ParentTaskId.HasValue && !tasksById.ContainsKey(task.ParentTaskId.Value))
            {
                task.ParentTaskId = null;
                changed = true;
            }
        }

        var visitState = new Dictionary<Guid, int>();
        foreach (var task in state.Tasks)
        {
            RepairTask(task);
        }

        if (TreeManagerStatePolicy.Normalize(state))
        {
            changed = true;
        }

        return changed;

        void RepairTask(TaskItem task)
        {
            if (visitState.TryGetValue(task.Id, out var currentState))
            {
                if (currentState == 1)
                {
                    task.ParentTaskId = null;
                    changed = true;
                    RepairDirectAssignment(task);
                    visitState[task.Id] = 2;
                }

                return;
            }

            visitState[task.Id] = 1;
            if (task.ParentTaskId.HasValue &&
                tasksById.TryGetValue(task.ParentTaskId.Value, out var parentTask))
            {
                RepairTask(parentTask);
                if (task.ProjectId != parentTask.ProjectId || task.GroupId != parentTask.GroupId)
                {
                    task.ProjectId = parentTask.ProjectId;
                    task.GroupId = parentTask.GroupId;
                    changed = true;
                }
            }
            else
            {
                RepairDirectAssignment(task);
            }

            visitState[task.Id] = 2;
        }

        void RepairDirectAssignment(TaskItem task)
        {
            if (task.GroupId.HasValue &&
                groupsById.TryGetValue(task.GroupId.Value, out var group))
            {
                if (task.ProjectId != group.ProjectId)
                {
                    task.ProjectId = group.ProjectId;
                    changed = true;
                }

                return;
            }

            if (task.GroupId.HasValue)
            {
                task.GroupId = null;
                changed = true;
            }

            if (!task.ProjectId.HasValue || !projectIds.Contains(task.ProjectId.Value))
            {
                task.ProjectId = defaultProject.Id;
                changed = true;
            }
        }
    }
}
