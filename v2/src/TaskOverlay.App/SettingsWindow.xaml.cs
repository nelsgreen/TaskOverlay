using System;
using System.Windows;
using System.Windows.Controls;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class SettingsWindow : Window
{
    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly Action _settingsChanged;
    private bool _updatingControls;

    public SettingsWindow(
        AppState state,
        Action saveState,
        Action settingsChanged)
    {
        _state = state;
        _saveState = saveState;
        _settingsChanged = settingsChanged;

        InitializeComponent();
        UpdateFromSettings();
    }

    public void UpdateFromSettings()
    {
        _updatingControls = true;
        try
        {
            CollapsedModeStatus.Text =
                $"Collapsed mode: {(_state.OverlaySettings.CollapsedMode ? "enabled" : "disabled")}";
            PinnedActiveModeStatus.Text =
                $"Keep expanded: {(_state.OverlaySettings.PinnedActiveMode ? "enabled" : "disabled")}";

            foreach (var item in InWorkModeComboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(
                        comboItem.Tag?.ToString(),
                        _state.OverlaySettings.InWorkMode.ToString(),
                        StringComparison.Ordinal))
                {
                    InWorkModeComboBox.SelectedItem = comboItem;
                    break;
                }
            }
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void InWorkModeComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingControls ||
            InWorkModeComboBox.SelectedItem is not ComboBoxItem selected ||
            !Enum.TryParse(selected.Tag?.ToString(), out InWorkMode mode) ||
            _state.OverlaySettings.InWorkMode == mode)
        {
            return;
        }

        TaskInteractionService.SetInWorkMode(_state, mode);
        _saveState();
        _settingsChanged();
    }
}
