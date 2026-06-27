namespace TaskOverlay.Core;

public static class OverlayCollapseGuard
{
    public static bool CanCollapse(OverlayInteractionState state)
    {
        return state.OverlayMode != OverlayMode.PinnedExpanded &&
               !state.TaskDetailsOpen &&
               !state.ContextMenuOpen &&
               (!state.SettingsOpen ||
                state.OverlayMode is OverlayMode.Working or OverlayMode.AutoQuestTracker) &&
               !state.ModalDialogOpen &&
               !state.Dragging;
    }
}

public readonly record struct OverlayInteractionState(
    OverlayMode OverlayMode,
    bool TaskDetailsOpen,
    bool ContextMenuOpen,
    bool SettingsOpen,
    bool ModalDialogOpen,
    bool Dragging);
