using System;

namespace TaskOverlay.Core;

public static class OverlayTaskPresentationPolicy
{
    public static double GetWorkingFontSize(OverlaySettings settings, bool activeMode)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return activeMode
            ? OverlaySettings.ClampWorkingActiveFontSize(
                settings.WorkingActiveFontSize)
            : OverlaySettings.ClampWorkingIdleFontSize(
                settings.WorkingIdleFontSize);
    }

    public static bool ShouldShowFocusBadge(TaskItem task, OverlayMode mode)
    {
        ArgumentNullException.ThrowIfNull(task);

        return task.Status == TaskStatus.InWork &&
               mode is not OverlayMode.Working and not OverlayMode.AutoQuestTracker;
    }
}
