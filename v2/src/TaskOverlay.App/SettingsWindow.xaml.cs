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
            OverlayModeStatus.Text =
                $"Overlay mode: {GetOverlayModeLabel(_state.OverlaySettings.OverlayMode)}";
            WorkingFontSizeTextBox.Text = FormatNumber(
                _state.OverlaySettings.WorkingFontSize);
            WorkingWindowWidthTextBox.Text = FormatNumber(
                _state.OverlaySettings.WorkingWindowWidth);
            WorkingWindowHeightTextBox.Text = FormatNumber(
                _state.OverlaySettings.WorkingWindowHeight);

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
        ApplyWorkingPresentationSettings();
    }

    private void WorkingSettingTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyWorkingPresentationSettings();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void ApplyWorkingPresentationSettings()
    {
        if (_updatingControls)
        {
            return;
        }

        var settings = _state.OverlaySettings;
        var fontSize = ReadNumber(WorkingFontSizeTextBox, settings.WorkingFontSize);
        var windowWidth = ReadNumber(
            WorkingWindowWidthTextBox,
            settings.WorkingWindowWidth);
        var windowHeight = ReadNumber(
            WorkingWindowHeightTextBox,
            settings.WorkingWindowHeight);

        fontSize = OverlaySettings.ClampWorkingFontSize(fontSize);
        windowWidth = OverlaySettings.ClampWorkingWindowWidth(windowWidth);
        windowHeight = OverlaySettings.ClampWorkingWindowHeight(windowHeight);
        var changed = settings.WorkingFontSize != fontSize ||
                      settings.WorkingWindowWidth != windowWidth ||
                      settings.WorkingWindowHeight != windowHeight;

        settings.WorkingFontSize = fontSize;
        settings.WorkingWindowWidth = windowWidth;
        settings.WorkingWindowHeight = windowHeight;
        UpdateFromSettings();

        if (changed)
        {
            _saveState();
            _settingsChanged();
        }
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
