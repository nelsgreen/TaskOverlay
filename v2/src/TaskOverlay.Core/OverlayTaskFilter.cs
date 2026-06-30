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
        return tasks.Where(task =>
            task.Status != TaskStatus.Done &&
            filter switch
            {
                OverlayPanelFilter.Panel => task.PinToPanel,
                OverlayPanelFilter.Focus => task.Status == TaskStatus.InWork,
                OverlayPanelFilter.Wait => task.Status == TaskStatus.Waiting,
                OverlayPanelFilter.Remind =>
                    ReminderAttentionService.ShouldShowNotification(task, timestamp),
                OverlayPanelFilter.Todo => task.Status == TaskStatus.Todo,
                _ => task.PinToPanel
            });
    }
}
