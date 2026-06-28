using System;

namespace TaskOverlay.App;

public enum AppWindowKind
{
    QuickAdd,
    TaskDetails,
    Settings
}

public sealed class WindowNavigationActions
{
    private readonly Action _showQuickAdd;
    private readonly Action _showSettings;
    private readonly Func<bool> _showTaskDetails;
    private readonly Func<bool> _canShowTaskDetails;

    public WindowNavigationActions(
        Action showQuickAdd,
        Action showSettings,
        Func<bool> showTaskDetails,
        Func<bool> canShowTaskDetails)
    {
        _showQuickAdd = showQuickAdd;
        _showSettings = showSettings;
        _showTaskDetails = showTaskDetails;
        _canShowTaskDetails = canShowTaskDetails;
    }

    public bool CanShowTaskDetails => _canShowTaskDetails();

    public void Show(AppWindowKind window)
    {
        switch (window)
        {
            case AppWindowKind.QuickAdd:
                _showQuickAdd();
                break;
            case AppWindowKind.TaskDetails:
                _showTaskDetails();
                break;
            case AppWindowKind.Settings:
                _showSettings();
                break;
        }
    }
}
