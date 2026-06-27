namespace TaskOverlay.Core;

public static class OverlaySurfacePolicy
{
    public static bool UseHandleWindowForMode(
        OverlayMode mode,
        bool hasCollapsedAnchor)
    {
        return hasCollapsedAnchor &&
               mode is OverlayMode.Working or
                   OverlayMode.AutoQuestTracker or
                   OverlayMode.CollapsedHandle or
                   OverlayMode.PinnedExpanded;
    }

    public static bool KeepHostVisibleWhenPanelHidden(
        OverlayPresentationState presentation)
    {
        return presentation.VisualBranch == OverlayVisualBranch.Collapsed;
    }
}
