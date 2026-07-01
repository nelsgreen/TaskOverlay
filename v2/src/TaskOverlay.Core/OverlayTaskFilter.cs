using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public static class OverlayTaskFilter
{
    public static IEnumerable<TaskItem> SelectForMode(
        IEnumerable<TaskItem> tasks,
        OverlayMode mode,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        return mode is OverlayMode.Working or OverlayMode.AutoQuestTracker
            ? SelectWorking(tasks, now)
            : SelectForPanel(tasks, OverlayPanelFilter.Panel, now);
    }

    public static IEnumerable<TaskItem> SelectWorking(
        IEnumerable<TaskItem> tasks,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        return tasks.Where(task =>
            task.Status == TaskStatus.InWork ||
            ReminderAttentionService.ShouldShowNotification(task, timestamp));
    }

    public static IEnumerable<TaskItem> SelectForPanel(
        IEnumerable<TaskItem> tasks,
        OverlayPanelFilter filter,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var visibleTasks = tasks
            .Where(task => task.Status != TaskStatus.Done)
            .ToList();
        if (filter == OverlayPanelFilter.Remind)
        {
            return visibleTasks
                .Select((task, index) => new
                {
                    Task = task,
                    Index = index,
                    Active = ReminderAttentionService.ShouldShowNotification(
                        task,
                        timestamp)
                })
                .Where(item =>
                    item.Active ||
                    item.Task.ReminderActive ||
                    item.Task.RemindAtUtc is not null)
                .OrderByDescending(item => item.Active)
                .ThenBy(item => item.Task.RemindAtUtc ?? DateTimeOffset.MaxValue)
                .ThenBy(item => item.Index)
                .Select(item => item.Task);
        }

        return visibleTasks.Where(task => filter switch
            {
                OverlayPanelFilter.Panel => task.PinToPanel,
                OverlayPanelFilter.Focus => task.Status == TaskStatus.InWork,
                OverlayPanelFilter.Wait => task.Status == TaskStatus.Waiting,
                OverlayPanelFilter.Todo => task.Status == TaskStatus.Todo,
                _ => task.PinToPanel
            });
    }
}
