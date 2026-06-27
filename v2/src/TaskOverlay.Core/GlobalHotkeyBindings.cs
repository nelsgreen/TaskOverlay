using System.Collections.Generic;

namespace TaskOverlay.Core;

public enum GlobalHotkeyCommand
{
    CreateTaskWithDescription,
    QuickAddTask,
    CollapseOrToggleOverlay,
    CycleOverlayMode
}

public sealed record GlobalHotkeyBinding(
    int Id,
    string DisplayName,
    uint VirtualKey,
    GlobalHotkeyCommand Command);

public static class GlobalHotkeyBindings
{
    public static IReadOnlyList<GlobalHotkeyBinding> All { get; } =
        new[]
        {
            new GlobalHotkeyBinding(
                1,
                "Ctrl+Alt+A",
                0x41,
                GlobalHotkeyCommand.CreateTaskWithDescription),
            new GlobalHotkeyBinding(
                2,
                "Ctrl+Alt+Q",
                0x51,
                GlobalHotkeyCommand.QuickAddTask),
            new GlobalHotkeyBinding(
                3,
                "Ctrl+Alt+T",
                0x54,
                GlobalHotkeyCommand.CollapseOrToggleOverlay),
            new GlobalHotkeyBinding(
                4,
                "Ctrl+Alt+1",
                0x31,
                GlobalHotkeyCommand.CycleOverlayMode)
        };
}
