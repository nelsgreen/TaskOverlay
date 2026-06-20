namespace TaskOverlay.Core;

public static class OverlaySurfacePolicy
{
    public static bool UseHandleWindowForPinned(
        OverlayMode previousMode,
        OverlayMode currentMode,
        bool hasCollapsedAnchor)
    {
        return previousMode == OverlayMode.CollapsedHandle &&
               currentMode == OverlayMode.PinnedExpanded &&
               hasCollapsedAnchor;
    }
}
