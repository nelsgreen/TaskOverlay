using System;
using System.Linq;

namespace TaskOverlay.Core;

public static class TaskInteractionService
{
    public static bool SetInWorkMode(AppState state, InWorkMode mode)
    {
        ArgumentNullException.ThrowIfNull(state);

        var changed = state.OverlaySettings.InWorkMode != mode;
        state.OverlaySettings.InWorkMode = mode;

        if (mode != InWorkMode.SingleTask)
        {
            return changed;
        }

        var focusedTaskFound = false;
        foreach (var task in state.Tasks)
        {
            if (!task.InWork)
            {
                continue;
            }

            if (!focusedTaskFound)
            {
                focusedTaskFound = true;
                continue;
            }

            task.InWork = false;
            if (task.Status == TaskStatus.InWork)
            {
                task.Status = TaskStatus.Todo;
            }
            changed = true;
        }

        return changed;
    }

    public static bool ToggleInWork(AppState state, TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(task);

        return SetInWork(state, task, !task.InWork);
    }

    public static bool ActivateFromClick(AppState state, TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(task);

        return state.OverlaySettings.InWorkMode == InWorkMode.SingleTask
            ? SetInWork(state, task, true)
            : ToggleInWork(state, task);
    }

    public static bool SetInWork(AppState state, TaskItem task, bool inWork)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(task);

        if (task.Status == TaskStatus.Done)
        {
            return false;
        }

        var changed = task.InWork != inWork;

        if (inWork && state.OverlaySettings.InWorkMode == InWorkMode.SingleTask)
        {
            foreach (var other in state.Tasks.Where(item => item.Id != task.Id))
            {
                if (other.InWork)
                {
                    other.InWork = false;
                    if (other.Status == TaskStatus.InWork)
                    {
                        other.Status = TaskStatus.Todo;
                    }
                    changed = true;
                }
            }
        }

        task.InWork = inWork;
        task.Status = inWork
            ? TaskStatus.InWork
            : task.Status == TaskStatus.InWork
                ? TaskStatus.Todo
                : task.Status;
        task.Completed = false;
        task.CompletedAtUtc = null;
        return changed;
    }

    public static bool SetStatus(
        AppState state,
        TaskItem task,
        TaskStatus status,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(task);

        var previous = task.Status;
        if (status == TaskStatus.Done)
        {
            var completed = Complete(task, now);
            return completed || previous != TaskStatus.Done;
        }

        if (status == TaskStatus.InWork)
        {
            var changed = SetInWork(state, task, true);
            task.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
            return changed || previous != TaskStatus.InWork;
        }

        task.Status = status;
        task.Completed = false;
        task.CompletedAtUtc = null;
        task.InWork = false;
        task.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        return previous != status;
    }

    public static bool ToggleDescription(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.DescriptionExpanded = !task.DescriptionExpanded;
        return task.DescriptionExpanded;
    }

    public static bool Complete(TaskItem task, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var reminderResolved = ReminderService.ApplyPreset(
            task,
            ReminderPreset.None,
            timestamp);

        // A completed task has no pending deadline: clear the due date alongside
        // the reminder so Timeline and Details stop surfacing it.
        var deadlineCleared = task.DueAtUtc is not null;
        task.DueAtUtc = null;

        if (task.Completed && task.Status == TaskStatus.Done)
        {
            if (deadlineCleared)
            {
                task.UpdatedAtUtc = timestamp;
            }

            return reminderResolved || deadlineCleared;
        }

        task.Completed = true;
        task.InWork = false;
        task.Status = TaskStatus.Done;
        task.CompletedAtUtc = timestamp;
        task.UpdatedAtUtc = timestamp;
        return true;
    }

    public static bool Update(
        AppState state,
        TaskItem task,
        TaskEditValues values,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(task);

        var title = values.Title.Trim();
        if (title.Length == 0)
        {
            throw new ArgumentException("Task title cannot be empty.", nameof(values));
        }

        var changed =
            task.Title != title ||
            task.Description != values.Description.Trim() ||
            task.InWork != values.InWork ||
            task.Completed != values.Completed ||
            values.ProjectId.HasValue && task.ProjectId != values.ProjectId ||
            values.Status.HasValue && task.Status != values.Status ||
            task.WaitingFor != values.WaitingFor.Trim() ||
            values.ReminderPreset != ReminderPreset.KeepCurrent ||
            values.ReplaceReminderSchedule;

        task.Title = title;
        task.Description = values.Description.Trim();

        if (values.ProjectId is Guid projectId && task.ProjectId != projectId)
        {
            changed |= new ProjectService(state).AssignTaskToProject(task.Id, projectId);
        }

        var status = values.Status ??
                     (values.Completed
                         ? TaskStatus.Done
                         : values.InWork
                             ? TaskStatus.InWork
                             : TaskStatus.Todo);
        SetStatus(state, task, status, now);
        task.WaitingFor = values.WaitingFor.Trim();
        if (values.ReminderPreset != ReminderPreset.KeepCurrent)
        {
            ReminderService.ApplyPreset(task, values.ReminderPreset, now);
        }
        else if (values.ReplaceReminderSchedule)
        {
            ReminderService.SetSchedule(
                task,
                values.RemindAtUtc,
                values.RemindEveryMinutes,
                now);
        }

        task.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;

        return changed;
    }

    public static bool Delete(AppState state, TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(task);

        return state.Tasks.Remove(task);
    }
}

public readonly record struct TaskEditValues(
    string Title,
    string Description,
    bool InWork,
    bool Completed,
    Guid? ProjectId = null,
    TaskStatus? Status = null,
    ReminderPreset ReminderPreset = ReminderPreset.KeepCurrent,
    string WaitingFor = "",
    DateTimeOffset? RemindAtUtc = null,
    int? RemindEveryMinutes = null,
    bool ReplaceReminderSchedule = false);
