using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public readonly record struct ReminderEditorValue(
    DateTimeOffset? RemindAtUtc,
    int? RepeatMinutes);

public partial class ReminderEditor : UserControl
{
    private DateTime? _localDateTime;
    private int? _repeatMinutes;
    private DateTimeOffset? _originalRemindAtUtc;
    private int? _originalRepeatMinutes;
    private ReminderPreset? _selectedPreset;
    private bool _updating;
    private bool _repeatExpanded;
    private bool _edited;

    public ReminderEditor()
    {
        InitializeComponent();
        PresetListBox.ItemsSource = TaskAttentionUiOptions.CompactReminderPresets;
        RepeatListBox.ItemsSource = TaskAttentionUiOptions.RepeatOptions;
        SetNoReminder();
    }

    public void Initialize(DateTimeOffset? remindAtUtc, int? repeatMinutes)
    {
        _originalRemindAtUtc = remindAtUtc;
        _originalRepeatMinutes = repeatMinutes;
        _localDateTime = remindAtUtc?.ToLocalTime().DateTime;
        _repeatMinutes = remindAtUtc is null ? null : repeatMinutes;
        _selectedPreset = remindAtUtc is null ? ReminderPreset.None : null;
        _repeatExpanded = _repeatMinutes is not null;
        _edited = false;
        RefreshUi();
    }

    public bool TryGetValue(out ReminderEditorValue value)
    {
        value = default;
        if (!_edited)
        {
            value = new ReminderEditorValue(
                _originalRemindAtUtc,
                _originalRepeatMinutes);
            return true;
        }

        if (_localDateTime is null)
        {
            value = new ReminderEditorValue(null, null);
            return true;
        }

        if (!TryCommitTimeText() ||
            ReminderDatePicker.SelectedDate is not DateTime selectedDate)
        {
            return false;
        }

        var local = DateTime.SpecifyKind(
            selectedDate.Date
                .AddHours(_localDateTime.Value.Hour)
                .AddMinutes(_localDateTime.Value.Minute),
            DateTimeKind.Local);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
        {
            return false;
        }

        try
        {
            value = new ReminderEditorValue(
                new DateTimeOffset(local).ToUniversalTime(),
                _repeatMinutes);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void FocusDateTime()
    {
        ReminderTimeTextBox.Focus();
        ReminderTimeTextBox.SelectAll();
    }

    private void ReminderToggleButton_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        if (ReminderToggleButton.IsChecked == true)
        {
            SetReminder(DateTime.Now.AddHours(1), ReminderPreset.In1Hour, null);
        }
        else
        {
            SetNoReminder();
        }
    }

    private void PresetListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updating ||
            PresetListBox.SelectedItem is not ReminderPresetOption option)
        {
            return;
        }

        var now = DateTime.Now;
        switch (option.Value)
        {
            case ReminderPreset.None:
                SetNoReminder();
                break;
            case ReminderPreset.In30Minutes:
                SetReminder(now.AddMinutes(30), option.Value, null);
                break;
            case ReminderPreset.In1Hour:
                SetReminder(now.AddHours(1), option.Value, null);
                break;
            case ReminderPreset.In2Hours:
                SetReminder(now.AddHours(2), option.Value, null);
                break;
            case ReminderPreset.TomorrowMorning:
                SetReminder(DateTime.Today.AddDays(1).AddHours(10), option.Value, null);
                break;
        }
    }

    private void ReminderDatePicker_OnSelectedDateChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_updating || ReminderDatePicker.SelectedDate is not DateTime date)
        {
            return;
        }

        var time = _localDateTime ?? DateTime.Now.AddHours(1);
        SetReminder(
            date.Date.AddHours(time.Hour).AddMinutes(time.Minute),
            null,
            _repeatMinutes);
    }

    private void ReminderTimeTextBox_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        if (TryCommitTimeText())
        {
            RefreshUi();
        }
    }

    private void ReminderTimeTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (TryCommitTimeText())
        {
            RefreshUi();
            Keyboard.ClearFocus();
        }
    }

    private void RepeatListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updating || RepeatListBox.SelectedItem is not RepeatOption option)
        {
            return;
        }

        var local = _localDateTime ?? DateTime.Now.AddHours(1);
        SetReminder(local, null, option.Minutes);
    }

    private void Add10MinutesButton_OnClick(object sender, RoutedEventArgs e) =>
        Add(TimeSpan.FromMinutes(10));

    private void Add30MinutesButton_OnClick(object sender, RoutedEventArgs e) =>
        Add(TimeSpan.FromMinutes(30));

    private void Add1HourButton_OnClick(object sender, RoutedEventArgs e) =>
        Add(TimeSpan.FromHours(1));

    private void Add1DayButton_OnClick(object sender, RoutedEventArgs e) =>
        Add(TimeSpan.FromDays(1));

    private void RepeatButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_repeatMinutes is null)
        {
            _repeatExpanded = true;
            SetReminder(
                _localDateTime ?? DateTime.Now.AddHours(1),
                null,
                60);
            return;
        }

        _repeatExpanded = false;
        SetReminder(
            _localDateTime ?? DateTime.Now.AddHours(1),
            null,
            null);
    }

    private void Add(TimeSpan increment)
    {
        SetReminder(
            (_localDateTime ?? DateTime.Now).Add(increment),
            null,
            _repeatMinutes);
    }

    private void SetNoReminder()
    {
        _edited = true;
        _localDateTime = null;
        _repeatMinutes = null;
        _selectedPreset = ReminderPreset.None;
        _repeatExpanded = false;
        RefreshUi();
    }

    private void SetReminder(
        DateTime localDateTime,
        ReminderPreset? preset,
        int? repeatMinutes)
    {
        _edited = true;
        _localDateTime = NormalizeToMinute(localDateTime);
        _repeatMinutes = repeatMinutes;
        _selectedPreset = preset;
        RefreshUi();
    }

    private bool TryCommitTimeText()
    {
        if (_localDateTime is null)
        {
            return true;
        }

        if (!TimeSpan.TryParseExact(
                ReminderTimeTextBox.Text.Trim(),
                new[] { @"h\:mm", @"hh\:mm" },
                CultureInfo.InvariantCulture,
                out var time) ||
            time < TimeSpan.Zero ||
            time >= TimeSpan.FromDays(1))
        {
            return false;
        }

        var date = ReminderDatePicker.SelectedDate?.Date ?? _localDateTime.Value.Date;
        _localDateTime = date.Add(time);
        _selectedPreset = null;
        _edited = true;
        return true;
    }

    private void RefreshUi()
    {
        _updating = true;
        try
        {
            var enabled = _localDateTime is not null;
            ReminderToggleButton.IsChecked = enabled;
            HeaderPanel.Margin = enabled
                ? new Thickness(0, 0, 0, 8)
                : new Thickness(0);
            SummaryBorder.Visibility = enabled
                ? Visibility.Visible
                : Visibility.Collapsed;
            PresetListBox.Visibility = enabled
                ? Visibility.Visible
                : Visibility.Collapsed;
            SchedulePanel.Visibility = enabled
                ? Visibility.Visible
                : Visibility.Collapsed;
            ReminderDatePicker.SelectedDate = _localDateTime?.Date;
            ReminderTimeTextBox.Text = _localDateTime?.ToString("HH:mm") ?? "--:--";
            SummaryText.Text = BuildSummary();
            AdvancedPanel.Visibility = enabled && _repeatExpanded
                ? Visibility.Visible
                : Visibility.Collapsed;

            PresetListBox.SelectedItem = TaskAttentionUiOptions.CompactReminderPresets
                .FirstOrDefault(option => option.Value == _selectedPreset);
            RepeatListBox.SelectedItem = TaskAttentionUiOptions.RepeatOptions
                .FirstOrDefault(option => option.Minutes == _repeatMinutes);
        }
        finally
        {
            _updating = false;
        }
    }

    private string BuildSummary()
    {
        if (_localDateTime is null)
        {
            return string.Empty;
        }

        var repeat = _repeatMinutes switch
        {
            60 => " · 1h",
            120 => " · 2h",
            1440 => " · daily",
            10080 => " · weekly",
            _ => string.Empty
        };
        return $"{_localDateTime:dd.MM.yyyy HH:mm}{repeat}";
    }

    private static DateTime NormalizeToMinute(DateTime value) =>
        new(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            value.Minute,
            0,
            DateTimeKind.Unspecified);
}
