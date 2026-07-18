using System;

namespace TaskOverlay.Core;

public static class WindowPlacementGeometry
{
    public static OverlayBounds ClampToWorkArea(
        OverlayBounds window,
        OverlayBounds workArea)
    {
        var width = Math.Min(Math.Max(0, window.Width), Math.Max(0, workArea.Width));
        var height = Math.Min(Math.Max(0, window.Height), Math.Max(0, workArea.Height));
        var maxLeft = workArea.Right - width;
        var maxTop = workArea.Bottom - height;

        return new OverlayBounds(
            Clamp(window.Left, workArea.Left, maxLeft),
            Clamp(window.Top, workArea.Top, maxTop),
            width,
            height);
    }

    public static OverlayBounds SnapToWorkArea(
        OverlayBounds window,
        OverlayBounds workArea,
        double threshold)
    {
        var clamped = ClampToWorkArea(window, workArea);
        var left = clamped.Left;
        var top = clamped.Top;
        var snapThreshold = Math.Max(0, threshold);

        if (Math.Abs(clamped.Left - workArea.Left) <= snapThreshold)
        {
            left = workArea.Left;
        }
        else if (Math.Abs(clamped.Right - workArea.Right) <= snapThreshold)
        {
            left = workArea.Right - clamped.Width;
        }

        if (Math.Abs(clamped.Top - workArea.Top) <= snapThreshold)
        {
            top = workArea.Top;
        }
        else if (Math.Abs(clamped.Bottom - workArea.Bottom) <= snapThreshold)
        {
            top = workArea.Bottom - clamped.Height;
        }

        return clamped with { Left = left, Top = top };
    }

    public static bool Intersects(OverlayBounds first, OverlayBounds second)
    {
        return first.Right > second.Left &&
               first.Left < second.Right &&
               first.Bottom > second.Top &&
               first.Top < second.Bottom;
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        if (maximum < minimum)
        {
            return minimum;
        }

        return Math.Min(Math.Max(value, minimum), maximum);
    }
}

public readonly record struct OverlayBounds(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}
