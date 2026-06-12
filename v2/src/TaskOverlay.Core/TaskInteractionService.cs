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

        if (task.Completed)
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
                    changed = true;
                }
            }
        }

        task.InWork = inWork;
        return changed;
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

        if (task.Completed)
        {
            return false;
        }

        task.Completed = true;
        task.InWork = false;
        task.CompletedAtUtc = now ?? DateTimeOffset.UtcNow;
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
            task.Completed != values.Completed;

        task.Title = title;
        task.Description = values.Description.Trim();

        if (values.Completed)
        {
            Complete(task, now);
        }
        else
        {
            task.Completed = false;
            task.CompletedAtUtc = null;
            SetInWork(state, task, values.InWork);
        }

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
    bool Completed);
