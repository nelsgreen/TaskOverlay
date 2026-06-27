using System.Collections.Generic;
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
        string modeStatus)
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
    public string EmptyText => Presentation.IsWorking ? "No FOCUS tasks" : "No tasks";
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
            modeStatus)
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
            modeStatus)
    {
    }
}
