using System.Windows;

namespace TaskOverlay.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(bool collapsedModeEnabled)
    {
        InitializeComponent();
        UpdateCollapsedMode(collapsedModeEnabled);
    }

    public void UpdateCollapsedMode(bool enabled)
    {
        CollapsedModeStatus.Text =
            $"Collapsed mode: {(enabled ? "enabled" : "disabled")}";
    }
}
