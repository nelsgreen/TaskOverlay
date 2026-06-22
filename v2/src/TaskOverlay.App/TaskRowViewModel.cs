using System;
using TaskOverlay.Core;

namespace TaskOverlay.App;

internal sealed class TaskRowViewModel
{
    public TaskRowViewModel(
        AppState state,
        TaskItem task,
        bool activeMode,
        DateTimeOffset? now = null)
    {
        Task = task;
        var project = ProjectReferenceResolver.ResolveProject(state, task);
        ProjectName = project?.Name ?? ProjectItem.DefaultName;
        ProjectColorHex = ProjectColorPalette.IsValid(project?.ColorHex)
            ? project!.ColorHex
            : ProjectColorPalette.Resolve(ProjectName, project?.Id ?? Guid.Empty);
        IsReminderDue = ReminderService.IsDue(task, now);
        ShowDescription =
            activeMode &&
            !string.IsNullOrWhiteSpace(task.Description) &&
            (task.DescriptionExpanded || task.InWork || task.Status == TaskStatus.Waiting);
        ShowWaitingFor =
            activeMode &&
            task.Status == TaskStatus.Waiting &&
            !string.IsNullOrWhiteSpace(task.WaitingFor);
    }

    public TaskItem Task { get; }
    public string Title => Task.Title;
    public string Description => Task.Description;
    public bool InWork => Task.Status == TaskStatus.InWork;
    public bool IsWaiting => Task.Status == TaskStatus.Waiting;
    public bool IsReminderDue { get; }
    public string StatusDotColorHex => IsReminderDue
        ? "#FFEF4444"
        : InWork
            ? "#FF38BDF8"
            : IsWaiting
                ? "#FFF59E0B"
                : "#FF71717A";
    public string ProjectName { get; }
    public string ProjectColorHex { get; }
    public string WaitingForLabel => $"Waiting for: {Task.WaitingFor}";
    public bool ShowDescription { get; }
    public bool ShowWaitingFor { get; }
}
