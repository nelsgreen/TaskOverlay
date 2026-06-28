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
        ApplyCurrentSelection();
        RefreshAvailability();
    }

    private void ApplyCurrentSelection()
    {
        QuickAddButton.IsChecked = _currentWindow == AppWindowKind.QuickAdd;
        TaskDetailsButton.IsChecked = _currentWindow == AppWindowKind.TaskDetails;
        SettingsButton.IsChecked = _currentWindow == AppWindowKind.Settings;
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
        ApplyCurrentSelection();
        if (target != _currentWindow && _navigation?.Show(target) != true)
        {
            ApplyCurrentSelection();
        }
    }
}
