using System;

namespace TaskOverlay.Core;

public readonly record struct OverlayPanelLayoutMetrics(
    double PanelMaxWidth,
    double ContentWidth,
    double TasksMaxHeight,
    double PanelWidth);

public static class OverlayPanelBoundsPolicy
{
    public const double HorizontalChrome = 30;
    public const double VerticalChrome = 30;

    public static OverlayPanelLayoutMetrics ResolveLayout(
        OverlayPresentationState presentation,
        OverlaySettings settings,
        OverlayBounds workArea,
        double workAreaMargin)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var availableWidth = Math.Max(
            120,
            workArea.Width - (workAreaMargin * 2));
        var availableHeight = Math.Max(
            80,
            workArea.Height - (workAreaMargin * 2));
        var availableContentWidth = Math.Max(
            80,
            availableWidth - HorizontalChrome);
        var desiredPanelWidth = presentation.UseCompactLayout
            ? OverlaySettings.ClampWorkingWindowWidth(settings.WorkingWindowWidth)
            : 420 + HorizontalChrome;
        var contentWidth = Math.Min(
            Math.Max(80, desiredPanelWidth - HorizontalChrome),
            availableContentWidth);
        var maximumTaskHeight = Math.Max(40, availableHeight - 80);
        var tasksMaxHeight = presentation.UseCompactLayout
            ? Math.Min(
                Math.Max(
                    40,
                    OverlaySettings.ClampWorkingWindowHeight(
                        settings.WorkingWindowHeight) - 60),
                maximumTaskHeight)
            : maximumTaskHeight;

        return new OverlayPanelLayoutMetrics(
            availableWidth,
            contentWidth,
            tasksMaxHeight,
            Math.Min(contentWidth + HorizontalChrome, availableWidth));
    }

    public static OverlayBounds PlaceWorkingPanel(
        OverlayBounds handle,
        double contentWidth,
        double contentHeight,
        double panelMaxWidth,
        OverlayBounds workArea)
    {
        var panelWidth = Math.Min(
            Math.Max(0, contentWidth + HorizontalChrome),
            Math.Max(0, panelMaxWidth));
        var panelHeight = Math.Min(
            Math.Max(0, contentHeight + VerticalChrome),
            Math.Max(0, workArea.Height));

        return PanelLayoutService.PlacePanel(
            handle,
            panelWidth,
            panelHeight,
            workArea);
    }
}
