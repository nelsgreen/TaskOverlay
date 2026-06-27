namespace TaskOverlay.Core;

public static class OverlayModeCycle
{
    public static OverlayMode Next(OverlayMode current)
    {
        return current switch
        {
            OverlayMode.Working => OverlayMode.CollapsedHandle,
            OverlayMode.CollapsedHandle => OverlayMode.PinnedExpanded,
            OverlayMode.PinnedExpanded => OverlayMode.Working,
            _ => OverlayMode.Working
        };
    }
}
