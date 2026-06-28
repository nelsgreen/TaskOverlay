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
    private readonly Func<AppWindowKind, bool> _switchWindow;
    private readonly Func<AppWindowKind, bool> _canShowWindow;
    private readonly Action _prepareForTaskDetailsOpen;

    public WindowNavigationActions(
        Func<AppWindowKind, bool> switchWindow,
        Func<AppWindowKind, bool> canShowWindow,
        Action prepareForTaskDetailsOpen)
    {
        _switchWindow = switchWindow;
        _canShowWindow = canShowWindow;
        _prepareForTaskDetailsOpen = prepareForTaskDetailsOpen;
    }

    public bool CanShowTaskDetails => _canShowWindow(AppWindowKind.TaskDetails);

    public bool Show(AppWindowKind window)
    {
        return _canShowWindow(window) && _switchWindow(window);
    }

    public void PrepareForTaskDetailsOpen()
    {
        _prepareForTaskDetailsOpen();
    }
}
