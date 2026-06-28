using System;

namespace TaskOverlay.Core;

public enum UtilityWindowKind
{
    QuickAdd,
    TaskDetails,
    Settings
}

public readonly record struct ResolvedWindowSize(double Width, double Height);

public static class UtilityWindowSizePolicy
{
    private readonly record struct SizeProfile(
        double DefaultWidth,
        double DefaultHeight,
        double MinimumWidth,
        double MinimumHeight,
        double MaximumWidth,
        double MaximumHeight);

    public static ResolvedWindowSize Resolve(
        UtilityWindowKind kind,
        WindowSizeState? savedSize,
        double availableWidth = double.PositiveInfinity,
        double availableHeight = double.PositiveInfinity)
    {
        var profile = GetProfile(kind);
        var maximumWidth = ResolveAvailableMaximum(
            availableWidth,
            profile.MaximumWidth);
        var maximumHeight = ResolveAvailableMaximum(
            availableHeight,
            profile.MaximumHeight);
        var width = Clamp(
            savedSize?.Width ?? profile.DefaultWidth,
            Math.Min(profile.MinimumWidth, maximumWidth),
            maximumWidth,
            Math.Min(profile.DefaultWidth, maximumWidth));
        var height = Clamp(
            savedSize?.Height ?? profile.DefaultHeight,
            Math.Min(profile.MinimumHeight, maximumHeight),
            maximumHeight,
            Math.Min(profile.DefaultHeight, maximumHeight));
        return new ResolvedWindowSize(width, height);
    }

    public static WindowSizeState Capture(
        UtilityWindowKind kind,
        double width,
        double height)
    {
        var resolved = Resolve(
            kind,
            new WindowSizeState { Width = width, Height = height });
        return new WindowSizeState
        {
            Width = resolved.Width,
            Height = resolved.Height
        };
    }

    public static WindowSizeState? GetSavedSize(
        WindowPlacement placement,
        UtilityWindowKind kind)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return kind switch
        {
            UtilityWindowKind.QuickAdd => placement.QuickAddSize,
            UtilityWindowKind.TaskDetails => placement.TaskDetailsSize,
            _ => placement.SettingsSize
        };
    }

    public static void SetSavedSize(
        WindowPlacement placement,
        UtilityWindowKind kind,
        WindowSizeState size)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(size);
        switch (kind)
        {
            case UtilityWindowKind.QuickAdd:
                placement.QuickAddSize = size;
                break;
            case UtilityWindowKind.TaskDetails:
                placement.TaskDetailsSize = size;
                break;
            case UtilityWindowKind.Settings:
                placement.SettingsSize = size;
                break;
        }
    }

    public static bool Normalize(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var changed = false;
        changed |= Normalize(placement, UtilityWindowKind.QuickAdd);
        changed |= Normalize(placement, UtilityWindowKind.TaskDetails);
        changed |= Normalize(placement, UtilityWindowKind.Settings);
        return changed;
    }

    private static bool Normalize(
        WindowPlacement placement,
        UtilityWindowKind kind)
    {
        var current = GetSavedSize(placement, kind);
        if (current is null)
        {
            return false;
        }

        var normalized = Capture(kind, current.Width, current.Height);
        if (current.Width == normalized.Width && current.Height == normalized.Height)
        {
            return false;
        }

        SetSavedSize(placement, kind, normalized);
        return true;
    }

    private static double ResolveAvailableMaximum(
        double available,
        double configuredMaximum)
    {
        if (!double.IsFinite(available) || available <= 0)
        {
            return configuredMaximum;
        }

        return Math.Min(configuredMaximum, available);
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum,
        double fallback)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }

    private static SizeProfile GetProfile(UtilityWindowKind kind)
    {
        return kind switch
        {
            UtilityWindowKind.QuickAdd => new SizeProfile(
                620, 740, 560, 640, 1200, 1000),
            UtilityWindowKind.TaskDetails => new SizeProfile(
                620, 760, 560, 620, 1200, 1000),
            _ => new SizeProfile(
                680, 820, 600, 680, 1400, 1100)
        };
    }
}
