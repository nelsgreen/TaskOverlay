using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public static class WorkspaceStatePolicy
{
    public static bool Normalize(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var changed = false;
        if (state.WorkspaceSettings is null)
        {
            state.WorkspaceSettings = new WorkspaceSettings();
            changed = true;
        }

        var settings = state.WorkspaceSettings;
        if (settings.SelectedProjectIds is null)
        {
            settings.SelectedProjectIds = new List<Guid>();
            changed = true;
        }

        if (!Enum.IsDefined(settings.ActiveTab))
        {
            settings.ActiveTab = WorkspaceTab.Tree;
            changed = true;
        }

        if (!Enum.IsDefined(settings.Filter))
        {
            settings.Filter = WorkspaceFilter.All;
            changed = true;
        }

        var projectIds = state.Projects.Select(project => project.Id).ToHashSet();
        var selectedProjectIds = settings.SelectedProjectIds
            .Where(projectIds.Contains)
            .Distinct()
            .ToList();
        if (selectedProjectIds.Count == 0 && ResolveFallbackProject(state) is { } fallbackProject)
        {
            selectedProjectIds.Add(fallbackProject.Id);
        }

        if (!settings.SelectedProjectIds.SequenceEqual(selectedProjectIds))
        {
            settings.SelectedProjectIds = selectedProjectIds;
            changed = true;
        }

        var selectedProjectIdSet = selectedProjectIds.ToHashSet();
        var hadSelectedTask = settings.SelectedTaskId.HasValue;
        var selectedTask = settings.SelectedTaskId is Guid selectedTaskId
            ? state.Tasks.FirstOrDefault(task =>
                task.Id == selectedTaskId &&
                task.ProjectId is Guid projectId &&
                selectedProjectIdSet.Contains(projectId))
            : null;
        if (hadSelectedTask && selectedTask is null)
        {
            selectedTask = state.Tasks
                .Where(task =>
                    task.ProjectId is Guid projectId &&
                    selectedProjectIdSet.Contains(projectId))
                .OrderBy(task => task.SortOrder)
                .ThenBy(task => task.CreatedAtUtc)
                .ThenBy(task => task.Id)
                .FirstOrDefault();
        }
        if (settings.SelectedTaskId != selectedTask?.Id)
        {
            settings.SelectedTaskId = selectedTask?.Id;
            changed = true;
        }

        var timelineItemId = NormalizeOptionalId(settings.SelectedTimelineItemId);
        if (timelineItemId is not null && !IsValidTimelineItemId(state, timelineItemId))
        {
            timelineItemId = null;
        }

        if (!string.Equals(
                settings.SelectedTimelineItemId,
                timelineItemId,
                StringComparison.Ordinal))
        {
            settings.SelectedTimelineItemId = timelineItemId;
            changed = true;
        }

        var workstreamId = NormalizeOptionalId(settings.SelectedWorkstreamId);
        if (!string.Equals(
                settings.SelectedWorkstreamId,
                workstreamId,
                StringComparison.Ordinal))
        {
            settings.SelectedWorkstreamId = workstreamId;
            changed = true;
        }

        return changed;
    }

    private static string? NormalizeOptionalId(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) || normalized.Length > 256
            ? null
            : normalized;
    }

    private static bool IsValidTimelineItemId(AppState state, string value)
    {
        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 ||
            !Guid.TryParse(value[(separatorIndex + 1)..], out var itemId))
        {
            return false;
        }

        if (value[..separatorIndex] == "meet")
        {
            return state.Meetings?.Any(meeting => meeting.Id == itemId) == true;
        }

        if (state.Tasks.FirstOrDefault(task => task.Id == itemId) is not { } task)
        {
            return false;
        }

        return value[..separatorIndex] switch
        {
            "remind" => task.ReminderSnoozedUntilUtc is not null ||
                        task.RemindAtUtc is not null ||
                        task.ReminderActive && task.LastReminderAtUtc is not null,
            "deadline" => task.DueAtUtc is not null,
            _ => false
        };
    }

    private static ProjectItem? ResolveFallbackProject(AppState state) =>
        state.Projects.FirstOrDefault(project =>
            string.Equals(
                project.Name,
                ProjectItem.DefaultName,
                StringComparison.OrdinalIgnoreCase)) ??
        state.Projects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.CreatedAtUtc)
            .ThenBy(project => project.Id)
            .FirstOrDefault();
}
