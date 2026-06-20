namespace TaskOverlay.Core;

public static class OverlaySurfacePolicy
{
    public static bool UseHandleWindowForMode(
        OverlayMode mode,
        bool hasCollapsedAnchor)
    {
        return hasCollapsedAnchor &&
               mode is OverlayMode.AutoQuestTracker or
                   OverlayMode.CollapsedHandle or
                   OverlayMode.PinnedExpanded;
    }
}
