using System;

namespace TaskOverlay.Core;

public static class PanelLayoutService
{
    public static OverlayBounds PlacePanel(
        OverlayBounds handle,
        double panelWidth,
        double panelHeight,
        OverlayBounds workArea)
    {
        var width = Math.Min(Math.Max(0, panelWidth), Math.Max(0, workArea.Width));
        var height = Math.Min(Math.Max(0, panelHeight), Math.Max(0, workArea.Height));
        var left = handle.Left;
        var top = handle.Bottom;

        if (left + width > workArea.Right)
        {
            left = handle.Right - width;
        }

        if (top + height > workArea.Bottom)
        {
            top = handle.Top - height;
        }

        return WindowPlacementGeometry.ClampToWorkArea(
            new OverlayBounds(left, top, width, height),
            workArea);
    }
}
