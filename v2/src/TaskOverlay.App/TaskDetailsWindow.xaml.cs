using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class TaskDetailsWindow : Window
{
    private static readonly IReadOnlyList<TaskStatusOption> DetailStatuses =
        new[]
        {
            new TaskStatusOption(TaskStatus.Todo, "TODO"),
            new TaskStatusOption(TaskStatus.InWork, "FOCUS"),
            new TaskStatusOption(TaskStatus.Waiting, "WAIT"),
            new TaskStatusOption(TaskStatus.Done, "DONE")
        };

    private static readonly IReadOnlyList<ReminderPresetOption> DetailReminderPresets =
        new[]
        {
            new ReminderPresetOption(ReminderPreset.KeepCurrent, "Custom time"),
            new ReminderPresetOption(ReminderPreset.None, "No reminder"),
            new ReminderPresetOption(ReminderPreset.In30Minutes, "In 30 minutes"),
            new ReminderPresetOption(ReminderPreset.In1Hour, "In 1 hour"),
            new ReminderPresetOption(ReminderPreset.In2Hours, "In 2 hours"),
            new ReminderPresetOption(ReminderPreset.TomorrowMorning, "Tomorrow morning"),
            new ReminderPresetOption(ReminderPreset.RepeatEvery2Hours, "Every 2 hours"),
            new ReminderPresetOption(ReminderPreset.RepeatDaily, "Daily")
        };

    private readonly TaskItem _task;
    private readonly Action<TaskItem, TaskEditValues> _saveTask;
    private readonly Action<TaskItem> _deleteTask;
    private readonly Action<bool> _modalInteractionChanged;
    private readonly DateTimeOffset? _originalRemindAtUtc;
    private readonly int? _originalRepeatMinutes;

    private DateTime _reminderLocalDateTime;
    private bool _initializing;
    private bool _updatingReminderControls;
    private bool _reminderScheduleEdited;

    public TaskDetailsWindow(
        AppState state,
        TaskItem task,
        Action<TaskItem, TaskEditValues> saveTask,
        Action<TaskItem> deleteTask,
        Action<bool> modalInteractionChanged)
    {
        _task = task;
        _saveTask = saveTask;
        _deleteTask = deleteTask;
        _modalInteractionChanged = modalInteractionChanged;
        _originalRemindAtUtc = task.RemindAtUtc;
        _originalRepeatMinutes = task.RemindEveryMinutes;
        _reminderLocalDateTime = NormalizeToMinute(
            task.RemindAtUtc?.ToLocalTime().DateTime ?? DateTime.Now.AddHours(1));

        _initializing = true;
        InitializeComponent();

        TitleTextBox.Text = task.Title;
        DescriptionTextBox.Text = task.Description;
        WaitingForTextBox.Text = task.WaitingFor;

        var projects = state.Projects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.Name)
            .ToList();
        ProjectListBox.ItemsSource = projects;
        ProjectListBox.SelectedItem = projects.FirstOrDefault(project =>
            project.Id == task.ProjectId) ?? TaskCaptureService.ResolvePreferredProject(state);

        StatusListBox.ItemsSource = DetailStatuses;
        StatusListBox.SelectedItem = DetailStatuses
            .First(option => option.Value == task.Status);

        ReminderPresetListBox.ItemsSource = DetailReminderPresets;
        ReminderPresetListBox.SelectedItem = DetailReminderPresets
            .First(option => option.Value == ReminderService.DetectPreset(task));

        var repeatOptions = TaskAttentionUiOptions.RepeatOptions.ToList();
        if (task.RemindEveryMinutes is int existingInterval &&
            repeatOptions.All(option => option.Minutes != existingInterval))
        {
            repeatOptions.Add(new RepeatOption(
                existingInterval,
                $"Every {existingInterval} minutes"));
        }

        RepeatListBox.ItemsSource = repeatOptions;
        RepeatListBox.SelectedItem = repeatOptions
            .First(option => option.Minutes == task.RemindEveryMinutes);

        ReminderToggleButton.IsChecked = task.RemindAtUtc is not null;
        ReminderDatePicker.SelectedDate = _reminderLocalDateTime.Date;
        UpdateReminderTimeText();

        _initializing = false;
        UpdateWaitingField();
        UpdateReminderVisibility();
    }

    private void StatusListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateWaitingField();
    }

    private void ReminderToggleButton_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || _updatingReminderControls)
        {
            return;
        }

        _reminderScheduleEdited = true;
        if (ReminderToggleButton.IsChecked == true)
        {
            SelectReminderPreset(ReminderPreset.KeepCurrent);
        }
        else
        {
            SelectReminderPreset(ReminderPreset.None);
        }

        UpdateReminderVisibility();
    }

    private void ReminderDatePicker_OnSelectedDateChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_initializing ||
            _updatingReminderControls ||
            ReminderDatePicker.SelectedDate is not DateTime selectedDate)
        {
            return;
        }

        _reminderLocalDateTime = selectedDate.Date
            .AddHours(_reminderLocalDateTime.Hour)
            .AddMinutes(_reminderLocalDateTime.Minute);
        _reminderScheduleEdited = true;
        EnsureReminderEnabled();
        SelectReminderPreset(ReminderPreset.KeepCurrent);
    }

    private void ReminderPresetListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_initializing ||
            _updatingReminderControls ||
            ReminderPresetListBox.SelectedItem is not ReminderPresetOption option)
        {
            return;
        }

        var now = DateTime.Now;
        switch (option.Value)
        {
            case ReminderPreset.KeepCurrent:
                return;
            case ReminderPreset.None:
                ClearPendingReminder();
                return;
            case ReminderPreset.In30Minutes:
                SetPendingReminder(now.AddMinutes(30), option.Value, null);
                return;
            case ReminderPreset.In1Hour:
                SetPendingReminder(now.AddHours(1), option.Value, null);
                return;
            case ReminderPreset.In2Hours:
                SetPendingReminder(now.AddHours(2), option.Value, null);
                return;
            case ReminderPreset.TomorrowMorning:
                SetPendingReminder(
                    DateTime.Today.AddDays(1).AddHours(9),
                    option.Value,
                    null);
                return;
            case ReminderPreset.RepeatEvery2Hours:
                SetPendingReminder(now.AddHours(2), option.Value, 120);
                return;
            case ReminderPreset.RepeatDaily:
                SetPendingReminder(now.AddDays(1), option.Value, 1440);
                return;
        }
    }

    private void RepeatListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_initializing || _updatingReminderControls)
        {
            return;
        }

        _reminderScheduleEdited = true;
        EnsureReminderEnabled();
        SelectReminderPreset(ReminderPreset.KeepCurrent);
    }

    private void HourUpButton_OnClick(object sender, RoutedEventArgs e) =>
        AdjustReminderTime(TimeSpan.FromHours(1));

    private void HourDownButton_OnClick(object sender, RoutedEventArgs e) =>
        AdjustReminderTime(TimeSpan.FromHours(-1));

    private void MinuteUpButton_OnClick(object sender, RoutedEventArgs e) =>
        AdjustReminderTime(TimeSpan.FromMinutes(5));

    private void MinuteDownButton_OnClick(object sender, RoutedEventArgs e) =>
        AdjustReminderTime(TimeSpan.FromMinutes(-5));

    private void Add10MinutesButton_OnClick(object sender, RoutedEventArgs e) =>
        SetQuickReminder(DateTime.Now.AddMinutes(10));

    private void Add30MinutesButton_OnClick(object sender, RoutedEventArgs e) =>
        SetQuickReminder(DateTime.Now.AddMinutes(30));

    private void Add1HourButton_OnClick(object sender, RoutedEventArgs e) =>
        SetQuickReminder(DateTime.Now.AddHours(1));

    private void TomorrowButton_OnClick(object sender, RoutedEventArgs e) =>
        SetQuickReminder(DateTime.Today.AddDays(1).AddHours(9));

    private void ClearReminderButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearPendingReminder();
    }

    private void SetQuickReminder(DateTime localDateTime)
    {
        var repeatMinutes = (RepeatListBox.SelectedItem as RepeatOption)?.Minutes;
        SetPendingReminder(
            localDateTime,
            ReminderPreset.KeepCurrent,
            repeatMinutes);
    }

    private void AdjustReminderTime(TimeSpan adjustment)
    {
        SetPendingReminder(
            _reminderLocalDateTime.Add(adjustment),
            ReminderPreset.KeepCurrent,
            (RepeatListBox.SelectedItem as RepeatOption)?.Minutes);
    }

    private void SetPendingReminder(
        DateTime localDateTime,
        ReminderPreset selectedPreset,
        int? repeatMinutes)
    {
        _reminderLocalDateTime = NormalizeToMinute(localDateTime);
        _reminderScheduleEdited = true;

        UpdateReminderControls(
            () =>
            {
                ReminderToggleButton.IsChecked = true;
                ReminderDatePicker.SelectedDate = _reminderLocalDateTime.Date;
                SelectRepeatOptionCore(repeatMinutes);
                SelectReminderPresetCore(selectedPreset);
                UpdateReminderTimeText();
            });
        UpdateReminderVisibility();
    }

    private void ClearPendingReminder()
    {
        _reminderScheduleEdited = true;
        UpdateReminderControls(
            () =>
            {
                ReminderToggleButton.IsChecked = false;
                SelectRepeatOptionCore(null);
                SelectReminderPresetCore(ReminderPreset.None);
            });
        UpdateReminderVisibility();
    }

    private void EnsureReminderEnabled()
    {
        if (ReminderToggleButton.IsChecked == true)
        {
            return;
        }

        UpdateReminderControls(() => ReminderToggleButton.IsChecked = true);
        UpdateReminderVisibility();
    }

    private void SelectReminderPreset(ReminderPreset preset)
    {
        UpdateReminderControls(() => SelectReminderPresetCore(preset));
    }

    private void SelectReminderPresetCore(ReminderPreset preset)
    {
        ReminderPresetListBox.SelectedItem = DetailReminderPresets
            .First(option => option.Value == preset);
    }

    private void SelectRepeatOptionCore(int? minutes)
    {
        if (RepeatListBox.ItemsSource is not IEnumerable<RepeatOption> options)
        {
            return;
        }

        RepeatListBox.SelectedItem = options.First(option => option.Minutes == minutes);
    }

    private void UpdateReminderControls(Action update)
    {
        var wasUpdating = _updatingReminderControls;
        _updatingReminderControls = true;
        try
        {
            update();
        }
        finally
        {
            _updatingReminderControls = wasUpdating;
        }
    }

    private void UpdateReminderTimeText()
    {
        ReminderHourText.Text = _reminderLocalDateTime.Hour.ToString("00");
        ReminderMinuteText.Text = _reminderLocalDateTime.Minute.ToString("00");
    }

    private void UpdateReminderVisibility()
    {
        if (ReminderControlsPanel is null || ReminderOffText is null)
        {
            return;
        }

        var enabled = ReminderToggleButton.IsChecked == true;
        ReminderControlsPanel.Visibility = enabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReminderOffText.Visibility = enabled
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            ShowWarning("Task title cannot be empty.");
            TitleTextBox.Focus();
            return;
        }

        if (ProjectListBox.SelectedItem is not ProjectItem project ||
            StatusListBox.SelectedItem is not TaskStatusOption status ||
            RepeatListBox.SelectedItem is not RepeatOption repeat)
        {
            ShowWarning("Choose a project, status, and repeat option.");
            return;
        }

        DateTimeOffset? remindAtUtc = null;
        var repeatMinutes = ReminderToggleButton.IsChecked == true
            ? repeat.Minutes
            : null;
        if (ReminderToggleButton.IsChecked == true &&
            !TryBuildReminderTime(out remindAtUtc))
        {
            ShowWarning("Choose a valid reminder date and time.");
            return;
        }

        var replaceSchedule = _reminderScheduleEdited &&
                              (_originalRemindAtUtc != remindAtUtc ||
                               _originalRepeatMinutes != repeatMinutes);

        _saveTask(
            _task,
            new TaskEditValues(
                TitleTextBox.Text,
                DescriptionTextBox.Text,
                status.Value == TaskStatus.InWork,
                status.Value == TaskStatus.Done,
                project.Id,
                status.Value,
                ReminderPreset.KeepCurrent,
                WaitingForTextBox.Text,
                remindAtUtc,
                repeatMinutes,
                replaceSchedule));
        Close();
    }

    private bool TryBuildReminderTime(out DateTimeOffset? remindAtUtc)
    {
        remindAtUtc = null;
        if (ReminderDatePicker.SelectedDate is not DateTime selectedDate)
        {
            return false;
        }

        var local = DateTime.SpecifyKind(
            selectedDate.Date
                .AddHours(_reminderLocalDateTime.Hour)
                .AddMinutes(_reminderLocalDateTime.Minute),
            DateTimeKind.Local);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
        {
            return false;
        }

        try
        {
            remindAtUtc = new DateTimeOffset(local).ToUniversalTime();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var result = ShowModalMessage(
            () => MessageBox.Show(
                this,
                $"Delete \"{_task.Title}\"?",
                "Delete task",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning));

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _deleteTask(_task);
        Close();
    }

    private void UpdateWaitingField()
    {
        if (WaitingSection is null)
        {
            return;
        }

        WaitingSection.Visibility =
            StatusListBox.SelectedItem is TaskStatusOption
            {
                Value: TaskStatus.Waiting
            }
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ShowWarning(string message)
    {
        ShowModalMessage(
            () => MessageBox.Show(
                this,
                message,
                "TaskOverlay",
                MessageBoxButton.OK,
                MessageBoxImage.Warning));
    }

    private T ShowModalMessage<T>(Func<T> showDialog)
    {
        _modalInteractionChanged(true);
        try
        {
            return showDialog();
        }
        finally
        {
            _modalInteractionChanged(false);
        }
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
