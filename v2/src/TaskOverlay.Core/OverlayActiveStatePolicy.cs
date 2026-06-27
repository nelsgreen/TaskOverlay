namespace TaskOverlay.Core;

public static class OverlayActiveStatePolicy
{
    public static bool AfterModeSwitch(OverlayMode mode)
    {
        return mode == OverlayMode.PinnedExpanded;
    }

    public static bool WhileSettingsOpen(OverlayMode mode, bool pointerInside)
    {
        return mode is OverlayMode.Working or OverlayMode.AutoQuestTracker
            ? pointerInside
            : true;
    }
}
