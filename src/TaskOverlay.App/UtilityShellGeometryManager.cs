using System;
using System.Windows;
using TaskOverlay.Core;

namespace TaskOverlay.App;

internal static class UtilityShellGeometryManager
{
    public static void Restore(Window window, WindowPlacement placement)
    {
        var saved = placement.UtilityShellPlacement;
        var workArea = saved?.Left is double && saved.Top is double
            ? new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight)
            : SystemParameters.WorkArea;
        var geometry = UtilityShellGeometryPolicy.Resolve(
            saved,
            new OverlayBounds(
                workArea.Left,
                workArea.Top,
                workArea.Width,
                workArea.Height));
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = geometry.Left;
        window.Top = geometry.Top;
        window.Width = geometry.Width;
        window.Height = geometry.Height;
    }

    public static bool Capture(Window window, WindowPlacement placement)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;
        var width = bounds.Width > 0 ? bounds.Width : window.Width;
        var height = bounds.Height > 0 ? bounds.Height : window.Height;
        var captured = UtilityShellGeometryPolicy.Capture(
            bounds.Left,
            bounds.Top,
            width,
            height);
        var current = placement.UtilityShellPlacement;
        if (current is not null &&
            current.Left == captured.Left &&
            current.Top == captured.Top &&
            current.Width == captured.Width &&
            current.Height == captured.Height)
        {
            return false;
        }

        placement.UtilityShellPlacement = captured;
        return true;
    }
}
