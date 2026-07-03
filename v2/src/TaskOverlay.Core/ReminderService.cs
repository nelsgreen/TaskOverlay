using System;
using System.Collections.Generic;

namespace TaskOverlay.Core;

public static class ReminderService
{
    public static bool ApplyPreset(
        TaskItem task,
        ReminderPreset preset,
        DateTimeOffset? now = null,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (preset == ReminderPreset.KeepCurrent)
        {
            return false;
        }

        if (task.Status == TaskStatus.Done && preset != ReminderPreset.None)
        {
            preset = ReminderPreset.None;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var previous = new ReminderSnapshot(
            task.RemindAtUtc,
            task.RemindEveryMinutes,
            task.LastReminderAtUtc,
            task.ReminderActive,
            task.ReminderAcknowledgedAtUtc,
            task.ReminderSnoozedUntilUtc);

        task.ReminderActive = false;
        task.LastReminderAtUtc = null;
        task.ReminderAcknowledgedAtUtc = null;
        task.ReminderSnoozedUntilUtc = null;
        switch (preset)
        {
            case ReminderPreset.None:
                task.RemindAtUtc = null;
                task.RemindEveryMinutes = null;
                break;
            case ReminderPreset.In30Minutes:
                SetOnce(task, timestamp.AddMinutes(30));
                break;
            case ReminderPreset.In1Hour:
                SetOnce(task, timestamp.AddHours(1));
                break;
            case ReminderPreset.In2Hours:
                SetOnce(task, timestamp.AddHours(2));
                break;
            case ReminderPreset.TomorrowMorning:
                SetOnce(task, GetTomorrowMorning(timestamp, timeZone ?? TimeZoneInfo.Local));
                break;
            case ReminderPreset.RepeatEvery2Hours:
                task.RemindEveryMinutes = 120;
                task.RemindAtUtc = timestamp.AddHours(2);
                break;
            case ReminderPreset.RepeatDaily:
                task.RemindEveryMinutes = 24 * 60;
                task.RemindAtUtc = timestamp.AddDays(1);
                break;
        }

        task.UpdatedAtUtc = timestamp;
        return previous != new ReminderSnapshot(
            task.RemindAtUtc,
            task.RemindEveryMinutes,
            task.LastReminderAtUtc,
            task.ReminderActive,
            task.ReminderAcknowledgedAtUtc,
            task.ReminderSnoozedUntilUtc);
    }

    public static IReadOnlyList<TaskItem> ProcessDueReminders(
        IEnumerable<TaskItem> tasks,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var activated = new List<TaskItem>();
        foreach (var task in tasks)
        {
            if (task.Status == TaskStatus.Done ||
                task.ReminderActive ||
                task.RemindAtUtc is not DateTimeOffset remindAtUtc ||
                remindAtUtc > timestamp)
            {
                continue;
            }

            task.ReminderActive = true;
            task.LastReminderAtUtc = timestamp;
            task.ReminderSnoozedUntilUtc = null;
            task.UpdatedAtUtc = timestamp;
            if (task.RemindEveryMinutes is int intervalMinutes && intervalMinutes > 0)
            {
                var next = remindAtUtc;
                do
                {
                    next = next.AddMinutes(intervalMinutes);
                }
                while (next <= timestamp);

                task.RemindAtUtc = next;
            }

            activated.Add(task);
        }

        return activated;
    }

    public static bool Snooze(
        TaskItem task,
        int minutes,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (minutes <= 0 || task.Status == TaskStatus.Done)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        task.RemindAtUtc = timestamp.AddMinutes(minutes);
        task.ReminderActive = false;
        task.ReminderAcknowledgedAtUtc = null;
        task.ReminderSnoozedUntilUtc = null;
        task.UpdatedAtUtc = timestamp;
        return true;
    }

    public static bool SetSchedule(
        TaskItem task,
        DateTimeOffset? remindAtUtc,
        int? repeatMinutes,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Status == TaskStatus.Done)
        {
            remindAtUtc = null;
            repeatMinutes = null;
        }

        if (repeatMinutes <= 0)
        {
            repeatMinutes = null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        if (repeatMinutes is int interval && remindAtUtc is null)
        {
            remindAtUtc = timestamp.AddMinutes(interval);
        }

        var changed = task.RemindAtUtc != remindAtUtc ||
                      task.RemindEveryMinutes != repeatMinutes ||
                      task.ReminderActive ||
                      task.LastReminderAtUtc is not null ||
                      task.ReminderAcknowledgedAtUtc is not null ||
                      task.ReminderSnoozedUntilUtc is not null;
        task.RemindAtUtc = remindAtUtc;
        task.RemindEveryMinutes = repeatMinutes;
        task.LastReminderAtUtc = null;
        task.ReminderActive = false;
        task.ReminderAcknowledgedAtUtc = null;
        task.ReminderSnoozedUntilUtc = null;
        task.UpdatedAtUtc = timestamp;
        return changed;
    }

    public static bool MarkStillWaiting(
        TaskItem task,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Status == TaskStatus.Done)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        task.Status = TaskStatus.Waiting;
        task.Completed = false;
        task.InWork = false;
        var interval = task.RemindEveryMinutes is > 0
            ? task.RemindEveryMinutes.Value
            : 120;
        task.RemindAtUtc = timestamp.AddMinutes(interval);
        task.ReminderActive = false;
        task.ReminderAcknowledgedAtUtc = null;
        task.ReminderSnoozedUntilUtc = null;
        task.UpdatedAtUtc = timestamp;
        return true;
    }

    public static bool IsDue(TaskItem task, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.Status != TaskStatus.Done &&
               (task.ReminderActive ||
                task.RemindAtUtc is DateTimeOffset remindAtUtc &&
                remindAtUtc <= (now ?? DateTimeOffset.UtcNow));
    }

    public static ReminderPreset DetectPreset(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.RemindEveryMinutes switch
        {
            120 => ReminderPreset.RepeatEvery2Hours,
            1440 => ReminderPreset.RepeatDaily,
            _ when task.RemindAtUtc is null => ReminderPreset.None,
            _ => ReminderPreset.KeepCurrent
        };
    }

    private static void SetOnce(TaskItem task, DateTimeOffset remindAtUtc)
    {
        task.RemindAtUtc = remindAtUtc;
        task.RemindEveryMinutes = null;
    }

    private static DateTimeOffset GetTomorrowMorning(
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localMorning = localNow.Date.AddDays(1).AddHours(9);
        var offset = timeZone.GetUtcOffset(localMorning);
        return new DateTimeOffset(localMorning, offset).ToUniversalTime();
    }

    private readonly record struct ReminderSnapshot(
        DateTimeOffset? RemindAtUtc,
        int? RemindEveryMinutes,
        DateTimeOffset? LastReminderAtUtc,
        bool ReminderActive,
        DateTimeOffset? ReminderAcknowledgedAtUtc,
        DateTimeOffset? ReminderSnoozedUntilUtc);
}
