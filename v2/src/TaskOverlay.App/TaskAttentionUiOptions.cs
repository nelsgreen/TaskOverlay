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
            new TaskStatusOption(TaskStatus.InWork, "In work"),
            new TaskStatusOption(TaskStatus.Waiting, "Waiting"),
            new TaskStatusOption(TaskStatus.Done, "Done")
        };

    public static IReadOnlyList<TaskStatusOption> QuickAddStatuses { get; } =
        new[]
        {
            new TaskStatusOption(TaskStatus.Todo, "Todo"),
            new TaskStatusOption(TaskStatus.Waiting, "Waiting"),
            new TaskStatusOption(TaskStatus.InWork, "In work")
        };

    public static IReadOnlyList<ReminderPresetOption> ReminderPresets { get; } =
        new[]
        {
            new ReminderPresetOption(ReminderPreset.None, "No reminder"),
            new ReminderPresetOption(ReminderPreset.In30Minutes, "In 30 minutes"),
            new ReminderPresetOption(ReminderPreset.In1Hour, "In 1 hour"),
            new ReminderPresetOption(ReminderPreset.In2Hours, "In 2 hours"),
            new ReminderPresetOption(ReminderPreset.TomorrowMorning, "Tomorrow morning"),
            new ReminderPresetOption(ReminderPreset.RepeatEvery2Hours, "Repeat every 2 hours"),
            new ReminderPresetOption(ReminderPreset.RepeatDaily, "Repeat daily")
        };

    public static IReadOnlyList<ReminderPresetOption> EditorReminderPresets { get; } =
        new[]
        {
            new ReminderPresetOption(ReminderPreset.KeepCurrent, "Use reminder time below"),
            new ReminderPresetOption(ReminderPreset.None, "No reminder"),
            new ReminderPresetOption(ReminderPreset.In30Minutes, "In 30 minutes"),
            new ReminderPresetOption(ReminderPreset.In1Hour, "In 1 hour"),
            new ReminderPresetOption(ReminderPreset.In2Hours, "In 2 hours"),
            new ReminderPresetOption(ReminderPreset.TomorrowMorning, "Tomorrow morning"),
            new ReminderPresetOption(ReminderPreset.RepeatEvery2Hours, "Repeat every 2 hours"),
            new ReminderPresetOption(ReminderPreset.RepeatDaily, "Repeat daily")
        };

    public static IReadOnlyList<RepeatOption> RepeatOptions { get; } =
        new[]
        {
            new RepeatOption(null, "No repeat"),
            new RepeatOption(120, "Every 2 hours"),
            new RepeatOption(1440, "Daily")
        };
}
