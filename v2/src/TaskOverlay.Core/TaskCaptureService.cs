using System;
using System.Linq;

namespace TaskOverlay.Core;

public static class TaskCaptureService
{
    public static ProjectItem? ResolvePreferredProject(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.OverlaySettings.LastSelectedProjectId is Guid selectedId &&
            state.Projects.FirstOrDefault(project => project.Id == selectedId) is { } selected)
        {
            return selected;
        }

        return state.Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, "Personal", StringComparison.OrdinalIgnoreCase)) ??
               state.Projects.FirstOrDefault(project =>
                   string.Equals(
                       project.Name,
                       ProjectItem.DefaultName,
                       StringComparison.OrdinalIgnoreCase)) ??
               state.Projects
                   .OrderBy(project => project.SortOrder)
                   .ThenBy(project => project.CreatedAtUtc)
                   .FirstOrDefault();
    }

    public static TaskItem? CreateQuickTask(
        AppState state,
        QuickTaskValues values,
        DateTimeOffset? now = null,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var project = state.Projects.FirstOrDefault(item => item.Id == values.ProjectId);
        if (project is null || string.IsNullOrWhiteSpace(values.Title))
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var node = new TreeStateService(state).CreateTask(
            project.Id,
            values.Title,
            timestamp);
        var task = node is null
            ? null
            : state.Tasks.FirstOrDefault(item => item.Id == node.Id);
        if (task is null)
        {
            return null;
        }

        task.Description = values.Description.Trim();
        task.WaitingFor = values.WaitingFor.Trim();
        TaskInteractionService.SetStatus(state, task, values.Status, timestamp);
        ReminderService.ApplyPreset(task, values.ReminderPreset, timestamp, timeZone);
        state.OverlaySettings.LastSelectedProjectId = project.Id;
        return task;
    }
}

public readonly record struct QuickTaskValues(
    string Title,
    Guid ProjectId,
    TaskStatus Status,
    ReminderPreset ReminderPreset,
    string WaitingFor,
    string Description);
