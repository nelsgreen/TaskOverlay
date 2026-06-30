using System;
using System.Windows;
using TaskOverlay.Core;

namespace TaskOverlay.App;

internal sealed class TaskRowViewModel
{
    public TaskRowViewModel(
        AppState state,
        TaskItem task,
        OverlayPresentationState presentation,
        DateTimeOffset? now = null)
    {
        Task = task;
        var project = ProjectReferenceResolver.ResolveProject(state, task);
        ProjectId = project?.Id ?? Guid.Empty;
        ProjectName = project?.Name ?? ProjectItem.DefaultName;
        ProjectColorHex = ProjectColorPalette.IsValid(project?.ColorHex)
            ? project!.ColorHex
            : ProjectColorPalette.Resolve(ProjectName, project?.Id ?? Guid.Empty);
        IsReminderDue = ReminderAttentionService.ShouldShowNotification(task, now);
        var workingMode = presentation.IsWorking;
        ShowFocusBadge = OverlayTaskPresentationPolicy.ShouldShowFocusBadge(
            task,
            presentation);
        ShowDescription = presentation.ShowDescriptions &&
                          !string.IsNullOrWhiteSpace(task.Description) &&
                          (!workingMode || task.Status == TaskStatus.InWork);
        ShowWaitingFor = !workingMode &&
                         presentation.ShowDescriptions &&
                         task.Status == TaskStatus.Waiting &&
                         !string.IsNullOrWhiteSpace(task.WaitingFor);
        var quietIdleRow = workingMode && !presentation.IsActive && !IsReminderDue;
        RowOpacity = quietIdleRow ? 0.58 : 1.0;
        TitleColorHex = quietIdleRow
            ? "#FFA1A1AA"
            : "#FFF4F4F5";
        var workingFontSize = OverlayTaskPresentationPolicy.GetWorkingFontSize(
            state.OverlaySettings,
            presentation.IsActive);
        TitleFontSize = workingMode ? workingFontSize : 14;
        DescriptionFontSize = workingMode
            ? Math.Max(10, workingFontSize - 4)
            : 12;
        DescriptionLineHeight = Math.Ceiling(DescriptionFontSize * 1.35);
        DescriptionMaxHeight = DescriptionLineHeight * (workingMode ? 2 : 3);
        RowMargin = workingMode
            ? new Thickness(0, 1, 0, 1)
            : new Thickness(0, 2, 0, 2);
        RowPadding = workingMode
            ? new Thickness(6, 3, 6, 3)
            : new Thickness(8, 5, 8, 5);
    }

    public TaskItem Task { get; }
    public string Title => Task.Title;
    public string Description => Task.Description;
    public bool InWork => Task.Status == TaskStatus.InWork;
    public bool IsWaiting => Task.Status == TaskStatus.Waiting;
    public bool IsReminderDue { get; }
    public string StatusLabel => Task.Status switch
    {
        TaskStatus.InWork => "FOCUS",
        TaskStatus.Waiting => "WAIT",
        _ => "TODO"
    };
    public string StatusBackgroundHex => Task.Status switch
    {
        TaskStatus.InWork => "#FF064E3B",
        TaskStatus.Waiting => "#FF0C4A6E",
        _ => "#FF27272A"
    };
    public string StatusForegroundHex => Task.Status switch
    {
        TaskStatus.InWork => "#FF6EE7B7",
        TaskStatus.Waiting => "#FF7DD3FC",
        _ => "#FFD4D4D8"
    };
    public string StatusDotColorHex => IsReminderDue
        ? "#FFF4B74A"
        : InWork
            ? "#FF38BDF8"
            : IsWaiting
                ? "#FFF59E0B"
                : "#FF71717A";
    public string ProjectName { get; }
    public Guid ProjectId { get; }
    public string ProjectColorHex { get; }
    public string WaitingForLabel => $"Waiting for: {Task.WaitingFor}";
    public bool ShowDescription { get; }
    public bool ShowWaitingFor { get; }
    public bool ShowFocusBadge { get; }
    public double RowOpacity { get; }
    public string TitleColorHex { get; }
    public double TitleFontSize { get; }
    public double DescriptionFontSize { get; }
    public double DescriptionLineHeight { get; }
    public double DescriptionMaxHeight { get; }
    public Thickness RowMargin { get; }
    public Thickness RowPadding { get; }
}
