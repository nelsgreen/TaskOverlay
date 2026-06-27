using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class SettingsWindow : Window
{
    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly Action _settingsChanged;
    private bool _updatingControls;
    private bool _workingSettingsDirty;

    public SettingsWindow(
        AppState state,
        Action saveState,
        Action settingsChanged)
    {
        _state = state;
        _saveState = saveState;
        _settingsChanged = settingsChanged;

        _updatingControls = true;
        InitializeComponent();
        _updatingControls = false;
        UpdateFromSettings();
    }

    public void UpdateFromSettings()
    {
        _updatingControls = true;
        try
        {
            OverlayModeStatus.Text =
                $"Overlay mode: {GetOverlayModeLabel(_state.OverlaySettings.OverlayMode)}";
            WorkingIdleFontSizeTextBox.Text = FormatNumber(
                _state.OverlaySettings.WorkingIdleFontSize);
            WorkingActiveFontSizeTextBox.Text = FormatNumber(
                _state.OverlaySettings.WorkingActiveFontSize);
            WorkingWindowWidthSlider.Value =
                _state.OverlaySettings.WorkingWindowWidth;
            WorkingWindowHeightSlider.Value =
                _state.OverlaySettings.WorkingWindowHeight;
            UpdateWorkingSizeLabels();

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

    private void WorkingSettingTextBox_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        ApplyWorkingPresentationSettings(commit: true);
    }

    private void WorkingSettingTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyWorkingPresentationSettings(commit: true);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void WorkingSizeSlider_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyWorkingPresentationSettings(commit: false);
    }

    private void WorkingSizeSlider_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        ApplyWorkingPresentationSettings(commit: true);
    }

    private void WorkingSizeSlider_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        ApplyWorkingPresentationSettings(commit: true);
    }

    private void WorkingSizeSlider_OnKeyUp(object sender, KeyEventArgs e)
    {
        ApplyWorkingPresentationSettings(commit: true);
    }

    private void ApplyWorkingPresentationSettings(bool commit)
    {
        if (_updatingControls)
        {
            return;
        }

        var settings = _state.OverlaySettings;
        var idleFontSize = OverlaySettings.ClampWorkingIdleFontSize(
            ReadNumber(
                WorkingIdleFontSizeTextBox,
                settings.WorkingIdleFontSize));
        var activeFontSize = OverlaySettings.ClampWorkingActiveFontSize(
            ReadNumber(
                WorkingActiveFontSizeTextBox,
                settings.WorkingActiveFontSize));
        var windowWidth = OverlaySettings.ClampWorkingWindowWidth(
            WorkingWindowWidthSlider.Value);
        var windowHeight = OverlaySettings.ClampWorkingWindowHeight(
            WorkingWindowHeightSlider.Value);
        var changed = settings.WorkingIdleFontSize != idleFontSize ||
                      settings.WorkingActiveFontSize != activeFontSize ||
                      settings.WorkingWindowWidth != windowWidth ||
                      settings.WorkingWindowHeight != windowHeight;

        settings.WorkingIdleFontSize = idleFontSize;
        settings.WorkingActiveFontSize = activeFontSize;
        settings.WorkingWindowWidth = windowWidth;
        settings.WorkingWindowHeight = windowHeight;

        if (changed)
        {
            _workingSettingsDirty = true;
            _settingsChanged();
        }

        UpdateWorkingSizeLabels();
        if (commit)
        {
            CommitWorkingPresentationSettings();
            UpdateFromSettings();
        }
    }

    private void CommitWorkingPresentationSettings()
    {
        if (!_workingSettingsDirty)
        {
            return;
        }

        _saveState();
        _workingSettingsDirty = false;
    }

    private void UpdateWorkingSizeLabels()
    {
        WorkingWindowWidthValue.Text = FormatNumber(WorkingWindowWidthSlider.Value);
        WorkingWindowHeightValue.Text = FormatNumber(WorkingWindowHeightSlider.Value);
    }

    protected override void OnClosed(EventArgs e)
    {
        CommitWorkingPresentationSettings();
        base.OnClosed(e);
    }

    private static double ReadNumber(TextBox textBox, double fallback)
    {
        return double.TryParse(
                   textBox.Text,
                   NumberStyles.Float,
                   CultureInfo.CurrentCulture,
                   out var value) &&
               double.IsFinite(value)
            ? value
            : fallback;
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private static string GetOverlayModeLabel(OverlayMode mode)
    {
        return mode switch
        {
            OverlayMode.PinnedExpanded => "Pinned",
            OverlayMode.CollapsedHandle => "Collapsed handle",
            _ => "Working"
        };
    }
}
