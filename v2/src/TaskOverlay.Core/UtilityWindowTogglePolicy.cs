namespace TaskOverlay.Core;

public enum UtilityWindowToggleAction
{
    ShowAndActivate,
    Hide
}

public static class UtilityWindowTogglePolicy
{
    public static UtilityWindowToggleAction Resolve(
        bool isVisible,
        bool isActive,
        bool targetIsActive = true) =>
        isVisible && isActive && targetIsActive
            ? UtilityWindowToggleAction.Hide
            : UtilityWindowToggleAction.ShowAndActivate;
}
