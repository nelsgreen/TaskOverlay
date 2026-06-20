namespace TaskOverlay.Core;

public static class OverlayModeCycle
{
    public static OverlayMode Next(OverlayMode current)
    {
        return current switch
        {
            OverlayMode.AutoQuestTracker => OverlayMode.CollapsedHandle,
            OverlayMode.CollapsedHandle => OverlayMode.PinnedExpanded,
            _ => OverlayMode.AutoQuestTracker
        };
    }
}
