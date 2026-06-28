using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskOverlay.Core;

namespace TaskOverlay.App;

internal sealed record SettingsHotkeyItem(string Label, string Key);

public partial class SettingsWindow : Window
{
    private static readonly IReadOnlyList<GlobalHotkeyCommand> HotkeyOrder =
        new[]
        {
            GlobalHotkeyCommand.CycleOverlayMode,
            GlobalHotkeyCommand.CreateTaskWithDescription,
            GlobalHotkeyCommand.QuickAddTask,
            GlobalHotkeyCommand.CollapseOrToggleOverlay
        };

    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly Action _settingsChanged;
    private readonly SettingsWindowActions _actions;
    private bool _updatingControls;
    private bool _workingSettingsDirty;

    public SettingsWindow(
        AppState state,
        Action saveState,
        Action settingsChanged,
        SettingsWindowActions actions,
        WindowNavigationActions navigation)
    {
        _state = state;
        _saveState = saveState;
        _settingsChanged = settingsChanged;
        _actions = actions;

        _updatingControls = true;
        InitializeComponent();
        WindowSwitcher.Configure(navigation, AppWindowKind.Settings);
        ModeListBox.ItemsSource = OverlayModeDisplay.UserModes;
        HotkeyItems.ItemsSource = BuildHotkeyItems();
        _updatingControls = false;
        Activated += (_, _) => WindowSwitcher.RefreshAvailability();
        UpdateFromSettings();
    }

    public void UpdateFromSettings()
    {
        _updatingControls = true;
        try
        {
            ModeListBox.SelectedItem = OverlayModeDisplay.UserModes
                .First(option => option.Mode == _state.OverlaySettings.OverlayMode);
            WorkingIdleFontSizeSlider.Value =
                _state.OverlaySettings.WorkingIdleFontSize;
            WorkingActiveFontSizeSlider.Value =
                _state.OverlaySettings.WorkingActiveFontSize;
            WorkingWindowWidthSlider.Value =
                _state.OverlaySettings.WorkingWindowWidth;
            WorkingWindowHeightSlider.Value =
                _state.OverlaySettings.WorkingWindowHeight;
            UpdateValueLabels();
            WindowSwitcher.RefreshAvailability();
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void ModeListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingControls ||
            ModeListBox.SelectedItem is not OverlayModeDisplayOption selected ||
            selected.Mode == _state.OverlaySettings.OverlayMode)
        {
            return;
        }

        _actions.SetOverlayMode(selected.Mode);
        UpdateFromSettings();
    }

    private void SettingsSlider_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyWorkingPresentationSettings(commit: false);
    }

    private void SettingsSlider_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        ApplyWorkingPresentationSettings(commit: true);
    }

    private void SettingsSlider_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        ApplyWorkingPresentationSettings(commit: true);
    }

    private void SettingsSlider_OnKeyUp(object sender, KeyEventArgs e)
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
            WorkingIdleFontSizeSlider.Value);
        var activeFontSize = OverlaySettings.ClampWorkingActiveFontSize(
            WorkingActiveFontSizeSlider.Value);
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

        UpdateValueLabels();
        if (commit)
        {
            CommitWorkingPresentationSettings();
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

    private void UpdateValueLabels()
    {
        WorkingIdleFontSizeValue.Text = FormatPixels(WorkingIdleFontSizeSlider.Value);
        WorkingActiveFontSizeValue.Text = FormatPixels(WorkingActiveFontSizeSlider.Value);
        WorkingWindowWidthValue.Text = FormatPixels(WorkingWindowWidthSlider.Value);
        WorkingWindowHeightValue.Text = FormatPixels(WorkingWindowHeightSlider.Value);
    }

    private void OpenLogsButton_OnClick(object sender, RoutedEventArgs e)
    {
        _actions.OpenLogs();
        DiagnosticsStatusText.Text = "Opened the logs folder.";
    }

    private void OpenStateFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        _actions.OpenStateFolder();
        DiagnosticsStatusText.Text = "Opened the state folder.";
    }

    private void ResetWindowPositionsButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        _actions.ResetWindowPositions();
        DiagnosticsStatusText.Text =
            "Saved window positions were cleared for the next placement.";
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        CommitWorkingPresentationSettings();
        base.OnClosed(e);
    }

    private static IReadOnlyList<SettingsHotkeyItem> BuildHotkeyItems()
    {
        return HotkeyOrder
            .Select(command => GlobalHotkeyBindings.All.Single(item =>
                item.Command == command))
            .Select(binding => new SettingsHotkeyItem(
                GetHotkeyLabel(binding.Command),
                binding.DisplayName.Split('+')[^1]))
            .ToArray();
    }

    private static string GetHotkeyLabel(GlobalHotkeyCommand command)
    {
        return command switch
        {
            GlobalHotkeyCommand.CycleOverlayMode => "Cycle overlay mode",
            GlobalHotkeyCommand.CreateTaskWithDescription =>
                "Create one task from clipboard with description",
            GlobalHotkeyCommand.QuickAddTask => "Quick Add task",
            _ => "Collapse / show overlay"
        };
    }

    private static string FormatPixels(double value)
    {
        return $"{value:0} px";
    }
}
