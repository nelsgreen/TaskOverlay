using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public static class TreeManagerTaskFilter
{
    public static IEnumerable<TaskItem> Select(
        IEnumerable<TaskItem> tasks,
        TreeManagerStatusFilter filter,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        return tasks.Where(task => filter switch
        {
            TreeManagerStatusFilter.Panel => task.PinToPanel,
            TreeManagerStatusFilter.Focus => task.Status == TaskStatus.InWork,
            TreeManagerStatusFilter.Wait => task.Status == TaskStatus.Waiting,
            TreeManagerStatusFilter.Remind =>
                ReminderAttentionService.ShouldShowNotification(task, timestamp),
            TreeManagerStatusFilter.Todo => task.Status == TaskStatus.Todo,
            TreeManagerStatusFilter.Done => task.Status == TaskStatus.Done,
            _ => true
        });
    }
}
