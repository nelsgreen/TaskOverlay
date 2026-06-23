using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public static class ReminderAttentionService
{
    public static bool ShouldShowNotification(
        TaskItem task,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var occurrence = GetCurrentOccurrence(task);
        return ReminderService.IsDue(task, timestamp) &&
               occurrence is not null &&
               (task.ReminderAcknowledgedAtUtc is not DateTimeOffset acknowledgedAt ||
                acknowledgedAt < occurrence.Value) &&
               (task.ReminderSnoozedUntilUtc is not DateTimeOffset snoozedUntil ||
                snoozedUntil <= timestamp);
    }

    public static bool Acknowledge(
        TaskItem task,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        var occurrence = GetCurrentOccurrence(task);
        if (!ReminderService.IsDue(task, now) || occurrence is null)
        {
            return false;
        }

        if (task.ReminderAcknowledgedAtUtc is DateTimeOffset acknowledgedAt &&
            acknowledgedAt >= occurrence.Value &&
            task.ReminderSnoozedUntilUtc is null)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        task.ReminderAcknowledgedAtUtc = occurrence;
        task.ReminderSnoozedUntilUtc = null;
        if (task.RemindEveryMinutes is > 0)
        {
            task.ReminderActive = false;
        }

        task.UpdatedAtUtc = timestamp;
        return true;
    }

    public static bool SnoozeNotification(
        TaskItem task,
        int minutes,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (minutes <= 0 || !ReminderService.IsDue(task, now))
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var occurrence = GetCurrentOccurrence(task);
        if (task.ReminderAcknowledgedAtUtc is DateTimeOffset acknowledgedAt &&
            occurrence is DateTimeOffset currentOccurrence &&
            acknowledgedAt >= currentOccurrence)
        {
            task.ReminderAcknowledgedAtUtc = null;
        }

        task.ReminderSnoozedUntilUtc = timestamp.AddMinutes(minutes);
        task.UpdatedAtUtc = timestamp;
        return true;
    }

    public static IEnumerable<TaskItem> OrderForOverlay(
        IEnumerable<TaskItem> tasks,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        return tasks
            .Where(task => task.Status != TaskStatus.Done)
            .OrderByDescending(task => ReminderService.IsDue(task, timestamp))
            .ThenByDescending(task => task.Status == TaskStatus.InWork)
            .ThenBy(task => task.SortOrder)
            .ThenBy(task => task.CreatedAtUtc);
    }

    private static DateTimeOffset? GetCurrentOccurrence(TaskItem task) =>
        task.LastReminderAtUtc ?? task.RemindAtUtc;
}
