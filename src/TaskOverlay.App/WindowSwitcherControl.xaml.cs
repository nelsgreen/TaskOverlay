using System;
using System.Windows;
using System.Windows.Controls;

namespace TaskOverlay.App;

public partial class WindowSwitcherControl : UserControl
{
    private AppWindowKind _currentWindow;

    public event Action<AppWindowKind>? TabRequested;

    public WindowSwitcherControl()
    {
        InitializeComponent();
    }

    public void SetState(AppWindowKind currentWindow, bool canShowTaskDetails)
    {
        _currentWindow = currentWindow;
        ApplyCurrentSelection();
        TaskDetailsButton.IsEnabled = canShowTaskDetails;
        TaskDetailsButton.ToolTip = TaskDetailsButton.IsEnabled
            ? "Open the current task details"
            : "Open a task first to use Task Details";
    }

    private void ApplyCurrentSelection()
    {
        QuickAddButton.IsChecked = _currentWindow == AppWindowKind.QuickAdd;
        TaskDetailsButton.IsChecked = _currentWindow == AppWindowKind.TaskDetails;
        SettingsButton.IsChecked = _currentWindow == AppWindowKind.Settings;
    }

    private void QuickAddButton_OnClick(object sender, RoutedEventArgs e) =>
        Navigate(AppWindowKind.QuickAdd);

    private void TaskDetailsButton_OnClick(object sender, RoutedEventArgs e) =>
        Navigate(AppWindowKind.TaskDetails);

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e) =>
        Navigate(AppWindowKind.Settings);

    private void Navigate(AppWindowKind target)
    {
        ApplyCurrentSelection();
        if (target != _currentWindow)
        {
            TabRequested?.Invoke(target);
        }
    }
}
