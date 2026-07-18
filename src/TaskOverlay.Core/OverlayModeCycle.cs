namespace TaskOverlay.Core;

public static class OverlayModeCycle
{
    public static OverlayMode Next(OverlayMode current)
    {
        return current switch
        {
            OverlayMode.Working => OverlayMode.PinnedExpanded,
            OverlayMode.PinnedExpanded => OverlayMode.CollapsedHandle,
            OverlayMode.CollapsedHandle => OverlayMode.Working,
            _ => OverlayMode.Working
        };
    }
}
