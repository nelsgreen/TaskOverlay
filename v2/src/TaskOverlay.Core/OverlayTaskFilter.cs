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

        if (mode is not OverlayMode.Working and not OverlayMode.AutoQuestTracker)
        {
            return tasks;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        return tasks.Where(task =>
            task.Status == TaskStatus.InWork ||
            ReminderAttentionService.ShouldShowNotification(task, timestamp));
    }
}
