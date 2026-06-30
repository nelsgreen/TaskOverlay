using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public abstract class OverlayViewState
{
    protected OverlayViewState(
        OverlayPresentationState presentation,
        IReadOnlyList<object> tasks,
        Brush panelBackground,
        Brush panelBorder,
        double panelMaxWidth,
        double contentWidth,
        double tasksMaxHeight,
        double emptyFontSize,
        string modeStatus,
        string emptyText)
    {
        Presentation = presentation;
        Tasks = tasks;
        PanelBackground = panelBackground;
        PanelBorder = panelBorder;
        PanelMaxWidth = panelMaxWidth;
        ContentWidth = contentWidth;
        TasksMaxHeight = tasksMaxHeight;
        EmptyFontSize = emptyFontSize;
        ModeStatus = modeStatus;
        EmptyText = emptyText;
    }

    public OverlayPresentationState Presentation { get; }
    public IReadOnlyList<object> Tasks { get; }
    public Brush PanelBackground { get; }
    public Brush PanelBorder { get; }
    public double PanelMaxWidth { get; }
    public double ContentWidth { get; }
    public double TasksMaxHeight { get; }
    public double EmptyFontSize { get; }
    public string ModeStatus { get; }
    public bool IsHostHitTestVisible =>
        Presentation.VisualBranch != OverlayVisualBranch.Collapsed;
    public string EmptyText { get; }
    public double EmptyOpacity => Presentation.IsWorking && !Presentation.IsActive
        ? 0.58
        : 1.0;
    public Visibility EmptyVisibility => Tasks.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility ActiveHeaderVisibility => Presentation.ShowActiveChrome
        ? Visibility.Visible
        : Visibility.Collapsed;
}

public sealed class WorkingOverlayViewState : OverlayViewState
{
    public WorkingOverlayViewState(
        OverlayPresentationState presentation,
        IReadOnlyList<object> tasks,
        Brush panelBackground,
        Brush panelBorder,
        double panelMaxWidth,
        double contentWidth,
        double tasksMaxHeight,
        double emptyFontSize,
        string modeStatus)
        : base(
            presentation,
            tasks,
            panelBackground,
            panelBorder,
            panelMaxWidth,
            contentWidth,
            tasksMaxHeight,
            emptyFontSize,
            modeStatus,
            "No FOCUS tasks")
    {
    }
}

public sealed class CollapsedOverlayViewState : OverlayViewState
{
    public CollapsedOverlayViewState(
        OverlayPresentationState presentation,
        Brush panelBackground,
        Brush panelBorder,
        double panelMaxWidth,
        double contentWidth,
        double tasksMaxHeight,
        double emptyFontSize,
        string modeStatus)
        : base(
            presentation,
            System.Array.Empty<object>(),
            panelBackground,
            panelBorder,
            panelMaxWidth,
            contentWidth,
            tasksMaxHeight,
            emptyFontSize,
            modeStatus,
            string.Empty)
    {
    }
}

public sealed class ExpandedOverlayViewState : OverlayViewState
{
    public ExpandedOverlayViewState(
        OverlayPresentationState presentation,
        IReadOnlyList<object> tasks,
        Brush panelBackground,
        Brush panelBorder,
        double panelMaxWidth,
        double contentWidth,
        double tasksMaxHeight,
        double emptyFontSize,
        string modeStatus,
        IReadOnlyList<object> projectGroups,
        IReadOnlyList<object> waitProjectGroups,
        IReadOnlyList<object> filterOptions,
        OverlayPanelFilter activeFilter,
        bool waitGroupExpanded)
        : base(
            presentation,
            tasks,
            panelBackground,
            panelBorder,
            panelMaxWidth,
            contentWidth,
            tasksMaxHeight,
            emptyFontSize,
            modeStatus,
            $"No {GetFilterLabel(activeFilter)} tasks")
    {
        ProjectGroups = projectGroups;
        WaitProjectGroups = waitProjectGroups;
        FilterOptions = filterOptions;
        ActiveFilter = activeFilter;
        WaitGroupExpanded = waitGroupExpanded;
    }

    public IReadOnlyList<object> ProjectGroups { get; }
    public IReadOnlyList<object> WaitProjectGroups { get; }
    public IReadOnlyList<object> FilterOptions { get; }
    public OverlayPanelFilter ActiveFilter { get; }
    public bool WaitGroupExpanded { get; }
    public int WaitCount => WaitProjectGroups
        .OfType<OverlayProjectGroupViewModel>()
        .Sum(group => group.Tasks.Count);
    public Visibility WaitGroupVisibility => WaitCount > 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility WaitTasksVisibility => WaitGroupExpanded
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string WaitToggleGlyph => WaitGroupExpanded ? "\uE70D" : "\uE76C";

    private static string GetFilterLabel(OverlayPanelFilter filter) => filter switch
    {
        OverlayPanelFilter.Focus => "FOCUS",
        OverlayPanelFilter.Wait => "WAIT",
        OverlayPanelFilter.Remind => "REMIND",
        OverlayPanelFilter.Todo => "TODO",
        _ => "Panel"
    };
}

internal sealed class OverlayProjectGroupViewModel
{
    public OverlayProjectGroupViewModel(
        Guid projectId,
        string projectName,
        string projectColorHex,
        IReadOnlyList<TaskRowViewModel> tasks)
    {
        ProjectId = projectId;
        ProjectName = projectName;
        ProjectColorHex = projectColorHex;
        Tasks = tasks;
    }

    public Guid ProjectId { get; }
    public string ProjectName { get; }
    public string ProjectColorHex { get; }
    public IReadOnlyList<TaskRowViewModel> Tasks { get; }
    public int Count => Tasks.Count;
}

internal sealed class OverlayFilterOptionViewModel
{
    public OverlayFilterOptionViewModel(
        OverlayPanelFilter filter,
        string label,
        int count,
        bool isActive)
    {
        Filter = filter;
        Label = label;
        Count = count;
        IsActive = isActive;
    }

    public OverlayPanelFilter Filter { get; }
    public string Label { get; }
    public int Count { get; }
    public bool IsActive { get; }
}
