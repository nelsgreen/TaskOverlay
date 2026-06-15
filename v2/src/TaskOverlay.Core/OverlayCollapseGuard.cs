namespace TaskOverlay.Core;

public static class OverlayCollapseGuard
{
    public static bool CanCollapse(OverlayInteractionState state)
    {
        return !state.PinnedActiveMode &&
               !state.TaskDetailsOpen &&
               !state.ContextMenuOpen &&
               !state.SettingsOpen &&
               !state.ModalDialogOpen &&
               !state.Dragging;
    }
}

public readonly record struct OverlayInteractionState(
    bool PinnedActiveMode,
    bool TaskDetailsOpen,
    bool ContextMenuOpen,
    bool SettingsOpen,
    bool ModalDialogOpen,
    bool Dragging);
