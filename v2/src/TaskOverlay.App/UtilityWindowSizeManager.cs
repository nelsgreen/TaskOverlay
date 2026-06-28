using System;
using System.Windows;
using TaskOverlay.Core;

namespace TaskOverlay.App;

internal static class UtilityWindowSizeManager
{
    private const double WorkAreaMargin = 32;

    public static void Restore(
        Window window,
        WindowPlacement placement,
        UtilityWindowKind kind)
    {
        var workArea = SystemParameters.WorkArea;
        var size = UtilityWindowSizePolicy.Resolve(
            kind,
            UtilityWindowSizePolicy.GetSavedSize(placement, kind),
            Math.Max(320, workArea.Width - WorkAreaMargin),
            Math.Max(240, workArea.Height - WorkAreaMargin));
        window.Width = size.Width;
        window.Height = size.Height;
    }

    public static bool Capture(
        Window window,
        WindowPlacement placement,
        UtilityWindowKind kind)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(0, 0, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;
        var width = bounds.Width > 0 ? bounds.Width : window.Width;
        var height = bounds.Height > 0 ? bounds.Height : window.Height;
        var captured = UtilityWindowSizePolicy.Capture(kind, width, height);
        var current = UtilityWindowSizePolicy.GetSavedSize(placement, kind);
        if (current is not null &&
            current.Width == captured.Width &&
            current.Height == captured.Height)
        {
            return false;
        }

        UtilityWindowSizePolicy.SetSavedSize(placement, kind, captured);
        return true;
    }
}
