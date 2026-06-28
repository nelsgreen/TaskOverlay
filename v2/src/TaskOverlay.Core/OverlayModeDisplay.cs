using System.Collections.Generic;

namespace TaskOverlay.Core;

public sealed record OverlayModeDisplayOption(OverlayMode Mode, string Label);

public static class OverlayModeDisplay
{
    public static IReadOnlyList<OverlayModeDisplayOption> UserModes { get; } =
        new[]
        {
            new OverlayModeDisplayOption(OverlayMode.Working, "Working"),
            new OverlayModeDisplayOption(OverlayMode.PinnedExpanded, "Pinned"),
            new OverlayModeDisplayOption(OverlayMode.CollapsedHandle, "Collapsed handle")
        };

    public static string GetLabel(OverlayMode mode)
    {
        return mode switch
        {
            OverlayMode.PinnedExpanded => "Pinned",
            OverlayMode.CollapsedHandle => "Collapsed handle",
            _ => "Working"
        };
    }
}
