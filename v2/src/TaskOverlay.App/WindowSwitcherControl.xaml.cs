using System.Windows;
using System.Windows.Controls;

namespace TaskOverlay.App;

public partial class WindowSwitcherControl : UserControl
{
    private WindowNavigationActions? _navigation;
    private AppWindowKind _currentWindow;

    public WindowSwitcherControl()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshAvailability();
    }

    public void Configure(
        WindowNavigationActions navigation,
        AppWindowKind currentWindow)
    {
        _navigation = navigation;
        _currentWindow = currentWindow;
        QuickAddButton.IsChecked = currentWindow == AppWindowKind.QuickAdd;
        TaskDetailsButton.IsChecked = currentWindow == AppWindowKind.TaskDetails;
        SettingsButton.IsChecked = currentWindow == AppWindowKind.Settings;
        RefreshAvailability();
    }

    public void RefreshAvailability()
    {
        TaskDetailsButton.IsEnabled =
            _currentWindow == AppWindowKind.TaskDetails ||
            _navigation?.CanShowTaskDetails == true;
        TaskDetailsButton.ToolTip = TaskDetailsButton.IsEnabled
            ? "Open the current task details"
            : "Open a task first to use Task Details";
    }

    private void QuickAddButton_OnClick(object sender, RoutedEventArgs e) =>
        Navigate(AppWindowKind.QuickAdd);

    private void TaskDetailsButton_OnClick(object sender, RoutedEventArgs e) =>
        Navigate(AppWindowKind.TaskDetails);

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e) =>
        Navigate(AppWindowKind.Settings);

    private void Navigate(AppWindowKind target)
    {
        if (target != _currentWindow)
        {
            _navigation?.Show(target);
        }
    }
}
