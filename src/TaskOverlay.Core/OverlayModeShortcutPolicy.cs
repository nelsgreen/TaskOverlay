namespace TaskOverlay.Core;

public enum OverlayModeShortcut
{
    Cycle,
    CollapseOrToggle
}

public readonly record struct OverlayModeShortcutResult(
    OverlayMode Mode,
    bool ToggleVisibility,
    bool EnsureVisible);

public static class OverlayModeShortcutPolicy
{
    public static OverlayModeShortcutResult Resolve(
        OverlayMode current,
        OverlayModeShortcut shortcut)
    {
        return shortcut switch
        {
            OverlayModeShortcut.Cycle => new OverlayModeShortcutResult(
                OverlayModeCycle.Next(current),
                ToggleVisibility: false,
                EnsureVisible: false),
            OverlayModeShortcut.CollapseOrToggle
                when current == OverlayMode.CollapsedHandle =>
                new OverlayModeShortcutResult(
                    OverlayMode.CollapsedHandle,
                    ToggleVisibility: true,
                    EnsureVisible: false),
            OverlayModeShortcut.CollapseOrToggle =>
                new OverlayModeShortcutResult(
                    OverlayMode.CollapsedHandle,
                    ToggleVisibility: false,
                    EnsureVisible: true),
            _ => Select(OverlayMode.Working)
        };
    }

    private static OverlayModeShortcutResult Select(OverlayMode mode)
    {
        return new OverlayModeShortcutResult(
            mode,
            ToggleVisibility: false,
            EnsureVisible: false);
    }
}
