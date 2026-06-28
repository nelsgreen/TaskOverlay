using System;

namespace TaskOverlay.Core;

public readonly record struct ResolvedUtilityShellGeometry(
    double Left,
    double Top,
    double Width,
    double Height);

public static class UtilityShellGeometryPolicy
{
    public const double DefaultWidth = 680;
    public const double DefaultHeight = 820;
    public const double MinimumWidth = 600;
    public const double MinimumHeight = 680;
    public const double MaximumWidth = 1400;
    public const double MaximumHeight = 1100;
    public const double WorkAreaMargin = 16;

    public static ResolvedUtilityShellGeometry Resolve(
        UtilityShellPlacementState? saved,
        OverlayBounds workArea)
    {
        var availableWidth = Math.Max(320, workArea.Width - (2 * WorkAreaMargin));
        var availableHeight = Math.Max(240, workArea.Height - (2 * WorkAreaMargin));
        var maximumWidth = Math.Min(MaximumWidth, availableWidth);
        var maximumHeight = Math.Min(MaximumHeight, availableHeight);
        var minimumWidth = Math.Min(MinimumWidth, maximumWidth);
        var minimumHeight = Math.Min(MinimumHeight, maximumHeight);
        var width = Clamp(
            saved?.Width,
            minimumWidth,
            maximumWidth,
            Math.Min(DefaultWidth, maximumWidth));
        var height = Clamp(
            saved?.Height,
            minimumHeight,
            maximumHeight,
            Math.Min(DefaultHeight, maximumHeight));
        var defaultLeft = workArea.Left + ((workArea.Width - width) / 2);
        var defaultTop = workArea.Top + ((workArea.Height - height) / 2);
        var left = Clamp(
            saved?.Left,
            workArea.Left + WorkAreaMargin,
            workArea.Right - WorkAreaMargin - width,
            defaultLeft);
        var top = Clamp(
            saved?.Top,
            workArea.Top + WorkAreaMargin,
            workArea.Bottom - WorkAreaMargin - height,
            defaultTop);

        return new ResolvedUtilityShellGeometry(left, top, width, height);
    }

    public static UtilityShellPlacementState Capture(
        double left,
        double top,
        double width,
        double height)
    {
        return new UtilityShellPlacementState
        {
            Left = double.IsFinite(left) ? left : null,
            Top = double.IsFinite(top) ? top : null,
            Width = Clamp(width, MinimumWidth, MaximumWidth, DefaultWidth),
            Height = Clamp(height, MinimumHeight, MaximumHeight, DefaultHeight)
        };
    }

    public static bool Normalize(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var current = placement.UtilityShellPlacement;
        if (current is null)
        {
            return false;
        }

        var normalized = Capture(
            current.Left ?? double.NaN,
            current.Top ?? double.NaN,
            current.Width ?? double.NaN,
            current.Height ?? double.NaN);
        if (current.Left == normalized.Left &&
            current.Top == normalized.Top &&
            current.Width == normalized.Width &&
            current.Height == normalized.Height)
        {
            return false;
        }

        placement.UtilityShellPlacement = normalized;
        return true;
    }

    private static double Clamp(
        double? value,
        double minimum,
        double maximum,
        double fallback)
    {
        if (maximum < minimum)
        {
            return fallback;
        }

        return value is double finite && double.IsFinite(finite)
            ? Math.Clamp(finite, minimum, maximum)
            : fallback;
    }
}
