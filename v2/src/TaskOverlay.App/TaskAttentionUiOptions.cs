using System.Collections.Generic;
using TaskOverlay.Core;

namespace TaskOverlay.App;

internal sealed record TaskStatusOption(TaskStatus Value, string Label);
internal sealed record ReminderPresetOption(ReminderPreset Value, string Label);
internal sealed record RepeatOption(int? Minutes, string Label);

internal static class TaskAttentionUiOptions
{
    public static IReadOnlyList<TaskStatusOption> EditableStatuses { get; } =
        new[]
        {
            new TaskStatusOption(TaskStatus.Todo, "Todo"),
            new TaskStatusOption(TaskStatus.InWork, "Focus"),
            new TaskStatusOption(TaskStatus.Waiting, "Waiting"),
            new TaskStatusOption(TaskStatus.Done, "Done")
        };

    public static IReadOnlyList<TaskStatusOption> QuickAddStatuses { get; } =
        new[]
        {
            new TaskStatusOption(TaskStatus.Todo, "TODO"),
            new TaskStatusOption(TaskStatus.InWork, "FOCUS"),
            new TaskStatusOption(TaskStatus.Waiting, "WAIT"),
            new TaskStatusOption(TaskStatus.Done, "DONE")
        };

    public static IReadOnlyList<ReminderPresetOption> CompactReminderPresets { get; } =
        new[]
        {
            new ReminderPresetOption(ReminderPreset.In30Minutes, "In 30 minutes"),
            new ReminderPresetOption(ReminderPreset.In1Hour, "In 1 hour"),
            new ReminderPresetOption(ReminderPreset.In2Hours, "In 2 hours"),
            new ReminderPresetOption(ReminderPreset.TomorrowMorning, "Tomorrow morning")
        };

    public static IReadOnlyList<RepeatOption> RepeatOptions { get; } =
        new[]
        {
            new RepeatOption(null, "Off"),
            new RepeatOption(60, "Every 1 hour"),
            new RepeatOption(120, "Every 2 hours"),
            new RepeatOption(1440, "Daily"),
            new RepeatOption(10080, "Weekly")
        };
}
