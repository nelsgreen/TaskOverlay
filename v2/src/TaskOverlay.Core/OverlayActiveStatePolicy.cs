namespace TaskOverlay.Core;

public readonly record struct OverlayPresentationState(
    OverlayMode Mode,
    bool IsActive,
    bool IsWorking,
    bool ShowActiveChrome,
    bool ShowDescriptions,
    bool AllowFocusBadge,
    bool UseCompactLayout);

public static class OverlayActiveStatePolicy
{
    public static OverlayPresentationState ForModeEntry(OverlayMode mode)
    {
        return Resolve(mode, mode == OverlayMode.PinnedExpanded);
    }

    public static OverlayPresentationState Resolve(
        OverlayMode mode,
        bool activeRequested)
    {
        var working = mode is OverlayMode.Working or OverlayMode.AutoQuestTracker;
        return new OverlayPresentationState(
            mode,
            IsActive: activeRequested,
            IsWorking: working,
            ShowActiveChrome: activeRequested,
            ShowDescriptions: activeRequested,
            AllowFocusBadge: !working,
            UseCompactLayout: working);
    }

    public static bool WhileSettingsOpen(OverlayMode mode, bool pointerInside)
    {
        return mode is OverlayMode.Working or OverlayMode.AutoQuestTracker
            ? pointerInside
            : true;
    }
}
