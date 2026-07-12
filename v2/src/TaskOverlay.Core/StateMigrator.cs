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
        state.Meetings ??= new List<MeetingItem>();
        state.ContextSources ??= new List<SourceDocument>();
        state.ContextItems ??= new List<ContextItem>();
        state.WorkspaceSettings ??= new WorkspaceSettings();

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

        if (state.Meetings is null)
        {
            state.Meetings = new List<MeetingItem>();
            changed = true;
        }

        if (state.ContextSources is null)
        {
            state.ContextSources = new List<SourceDocument>();
            changed = true;
        }

        if (state.ContextItems is null)
        {
            state.ContextItems = new List<ContextItem>();
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

        if (state.OverlaySettings is not null &&
            state.OverlaySettings.NormalizePanelPresentation())
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

            if (CheckpointService.Normalize(task))
            {
                changed = true;
            }
        }

        var meetingIds = new HashSet<Guid>();
        foreach (var meeting in state.Meetings.ToList())
        {
            if (string.IsNullOrWhiteSpace(meeting.Title) || meeting.StartsAtUtc == default)
            {
                state.Meetings.Remove(meeting);
                changed = true;
                continue;
            }

            if (meeting.Id == Guid.Empty || !meetingIds.Add(meeting.Id))
            {
                meeting.Id = Guid.NewGuid();
                meetingIds.Add(meeting.Id);
                changed = true;
            }

            if (!projectIds.Contains(meeting.ProjectId))
            {
                meeting.ProjectId = defaultProject.Id;
                changed = true;
            }

            var title = meeting.Title.Trim();
            var notes = meeting.Notes?.Trim() ?? string.Empty;
            var location = meeting.Location?.Trim() ?? string.Empty;
            var link = meeting.Link?.Trim() ?? string.Empty;
            if (meeting.Title != title || meeting.Notes != notes ||
                meeting.Location != location || meeting.Link != link)
            {
                meeting.Title = title;
                meeting.Notes = notes;
                meeting.Location = location;
                meeting.Link = link;
                changed = true;
            }

            if (meeting.DurationMinutes <= 0 ||
                meeting.DurationMinutes > MeetingService.MaximumDurationMinutes)
            {
                meeting.DurationMinutes = MeetingItem.DefaultDurationMinutes;
                changed = true;
            }

            if (meeting.LinkedTaskId is Guid linkedTaskId && !tasksById.ContainsKey(linkedTaskId))
            {
                meeting.LinkedTaskId = null;
                changed = true;
            }

            if (meeting.CreatedAtUtc == default)
            {
                meeting.CreatedAtUtc = meeting.StartsAtUtc;
                changed = true;
            }

            if (meeting.UpdatedAtUtc == default)
            {
                meeting.UpdatedAtUtc = meeting.CreatedAtUtc;
                changed = true;
            }
        }

        // ContextHUB repair: normalize records conservatively and drop dangling
        // links, but never delete a valid record just because one link is stale.
        var sourceIds = new HashSet<Guid>();
        foreach (var source in state.ContextSources.ToList())
        {
            if (string.IsNullOrWhiteSpace(source.Title))
            {
                state.ContextSources.Remove(source);
                changed = true;
                continue;
            }

            if (source.Id == Guid.Empty || !sourceIds.Add(source.Id))
            {
                source.Id = Guid.NewGuid();
                sourceIds.Add(source.Id);
                changed = true;
            }

            if (!projectIds.Contains(source.ProjectId))
            {
                source.ProjectId = defaultProject.Id;
                changed = true;
            }

            if (!Enum.IsDefined(source.SourceType))
            {
                source.SourceType = ContextSourceType.Other;
                changed = true;
            }

            if (source.SourceApp is { } app && !Enum.IsDefined(app))
            {
                source.SourceApp = ContextSourceApp.Other;
                changed = true;
            }

            var title = source.Title.Trim();
            var body = source.Body?.Trim() ?? string.Empty;
            var summary = source.Summary?.Trim() ?? string.Empty;
            if (source.Title != title || source.Body != body || source.Summary != summary)
            {
                source.Title = title;
                source.Body = body;
                source.Summary = summary;
                changed = true;
            }

            changed |= RepairIdList(
                source.LinkedTaskIds is null
                    ? source.LinkedTaskIds = new List<Guid>()
                    : source.LinkedTaskIds,
                tasksById.ContainsKey);
            changed |= RepairIdList(
                source.LinkedMeetingIds is null
                    ? source.LinkedMeetingIds = new List<Guid>()
                    : source.LinkedMeetingIds,
                meetingIds.Contains);

            if (source.SourceDateUtc == default)
            {
                source.SourceDateUtc = source.CreatedAtUtc == default
                    ? DateTimeOffset.UtcNow
                    : source.CreatedAtUtc;
                changed = true;
            }

            if (source.CreatedAtUtc == default)
            {
                source.CreatedAtUtc = source.SourceDateUtc;
                changed = true;
            }

            if (source.UpdatedAtUtc == default)
            {
                source.UpdatedAtUtc = source.CreatedAtUtc;
                changed = true;
            }
        }

        var contextItemIds = new HashSet<Guid>();
        foreach (var item in state.ContextItems.ToList())
        {
            if (string.IsNullOrWhiteSpace(item.Title))
            {
                state.ContextItems.Remove(item);
                changed = true;
                continue;
            }

            if (item.Id == Guid.Empty || !contextItemIds.Add(item.Id))
            {
                item.Id = Guid.NewGuid();
                contextItemIds.Add(item.Id);
                changed = true;
            }

            if (!projectIds.Contains(item.ProjectId))
            {
                item.ProjectId = defaultProject.Id;
                changed = true;
            }

            if (!Enum.IsDefined(item.ItemType))
            {
                item.ItemType = ContextItemType.Note;
                changed = true;
            }

            if (!Enum.IsDefined(item.Status))
            {
                item.Status = ContextItemStatus.Active;
                changed = true;
            }

            var itemTitle = item.Title.Trim();
            var itemBody = item.Body?.Trim() ?? string.Empty;
            if (item.Title != itemTitle || item.Body != itemBody)
            {
                item.Title = itemTitle;
                item.Body = itemBody;
                changed = true;
            }

            changed |= RepairIdList(
                item.SourceDocumentIds is null
                    ? item.SourceDocumentIds = new List<Guid>()
                    : item.SourceDocumentIds,
                sourceIds.Contains);
            changed |= RepairIdList(
                item.LinkedTaskIds is null
                    ? item.LinkedTaskIds = new List<Guid>()
                    : item.LinkedTaskIds,
                tasksById.ContainsKey);
            changed |= RepairIdList(
                item.LinkedMeetingIds is null
                    ? item.LinkedMeetingIds = new List<Guid>()
                    : item.LinkedMeetingIds,
                meetingIds.Contains);

            if (item.Status == ContextItemStatus.Active && item.ResolvedAtUtc is not null)
            {
                item.ResolvedAtUtc = null;
                changed = true;
            }

            if (item.CreatedAtUtc == default)
            {
                item.CreatedAtUtc = DateTimeOffset.UtcNow;
                changed = true;
            }

            if (item.UpdatedAtUtc == default)
            {
                item.UpdatedAtUtc = item.CreatedAtUtc;
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

        if (WorkspaceStatePolicy.Normalize(state))
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

        // Removes empty, duplicate, and dangling ids in place.
        static bool RepairIdList(List<Guid> ids, Func<Guid, bool> exists)
        {
            var repaired = ids
                .Where(id => id != Guid.Empty && exists(id))
                .Distinct()
                .ToList();
            if (repaired.Count == ids.Count)
            {
                return false;
            }

            ids.Clear();
            ids.AddRange(repaired);
            return true;
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
