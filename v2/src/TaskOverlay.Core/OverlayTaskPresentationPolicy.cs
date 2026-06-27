using System;

namespace TaskOverlay.Core;

public static class OverlayTaskPresentationPolicy
{
    public static bool ShouldShowFocusBadge(TaskItem task, OverlayMode mode)
    {
        ArgumentNullException.ThrowIfNull(task);

        return task.Status == TaskStatus.InWork &&
               mode is not OverlayMode.Working and not OverlayMode.AutoQuestTracker;
    }
}
