using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class TaskDetailsWindow : Window
{
    private readonly TaskItem _task;
    private readonly Action<TaskItem, TaskEditValues> _saveTask;
    private readonly Action<TaskItem> _deleteTask;
    private readonly Action<bool> _modalInteractionChanged;
    private readonly string _originalRemindAtText;
    private readonly int? _originalRepeatMinutes;

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

        InitializeComponent();
        TitleTextBox.Text = task.Title;
        DescriptionTextBox.Text = task.Description;
        WaitingForTextBox.Text = task.WaitingFor;

        var projects = state.Projects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.Name)
            .ToList();
        ProjectComboBox.ItemsSource = projects;
        ProjectComboBox.SelectedItem = projects.FirstOrDefault(project =>
            project.Id == task.ProjectId) ?? TaskCaptureService.ResolvePreferredProject(state);

        StatusComboBox.ItemsSource = TaskAttentionUiOptions.EditableStatuses;
        StatusComboBox.SelectedItem = TaskAttentionUiOptions.EditableStatuses
            .First(option => option.Value == task.Status);

        ReminderPresetComboBox.ItemsSource = TaskAttentionUiOptions.EditorReminderPresets;
        ReminderPresetComboBox.SelectedItem = TaskAttentionUiOptions.EditorReminderPresets
            .First(option => option.Value == ReminderPreset.KeepCurrent);
        RemindAtTextBox.Text = task.RemindAtUtc?.ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        _originalRemindAtText = RemindAtTextBox.Text;
        _originalRepeatMinutes = task.RemindEveryMinutes;
        RepeatComboBox.ItemsSource = TaskAttentionUiOptions.RepeatOptions;
        RepeatComboBox.SelectedItem = TaskAttentionUiOptions.RepeatOptions
            .FirstOrDefault(option => option.Minutes == task.RemindEveryMinutes) ??
            TaskAttentionUiOptions.RepeatOptions[0];
        UpdateWaitingField();
    }

    private void StatusComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateWaitingField();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            ShowWarning("Task title cannot be empty.");
            TitleTextBox.Focus();
            return;
        }

        if (ProjectComboBox.SelectedItem is not ProjectItem project ||
            StatusComboBox.SelectedItem is not TaskStatusOption status ||
            ReminderPresetComboBox.SelectedItem is not ReminderPresetOption reminder ||
            RepeatComboBox.SelectedItem is not RepeatOption repeat)
        {
            ShowWarning("Choose a project, status, reminder, and repeat value.");
            return;
        }

        DateTimeOffset? remindAtUtc = null;
        var customScheduleSelected = reminder.Value == ReminderPreset.KeepCurrent;
        var replaceSchedule = customScheduleSelected &&
                              (!string.Equals(
                                   RemindAtTextBox.Text.Trim(),
                                   _originalRemindAtText,
                                   StringComparison.Ordinal) ||
                               repeat.Minutes != _originalRepeatMinutes);
        if (replaceSchedule && !TryParseReminderTime(out remindAtUtc))
        {
            ShowWarning("Reminder time must use yyyy-MM-dd HH:mm in local time.");
            RemindAtTextBox.Focus();
            return;
        }

        _saveTask(
            _task,
            new TaskEditValues(
                TitleTextBox.Text,
                DescriptionTextBox.Text,
                status.Value == TaskStatus.InWork,
                status.Value == TaskStatus.Done,
                project.Id,
                status.Value,
                reminder.Value,
                WaitingForTextBox.Text,
                remindAtUtc,
                repeat.Minutes,
                replaceSchedule));
        Close();
    }

    private bool TryParseReminderTime(out DateTimeOffset? remindAtUtc)
    {
        remindAtUtc = null;
        if (string.IsNullOrWhiteSpace(RemindAtTextBox.Text))
        {
            return true;
        }

        if (!DateTime.TryParseExact(
                RemindAtTextBox.Text.Trim(),
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            return false;
        }

        try
        {
            var local = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
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
        if (WaitingForTextBox is null)
        {
            return;
        }

        WaitingForTextBox.IsEnabled =
            StatusComboBox.SelectedItem is TaskStatusOption
            {
                Value: TaskStatus.Waiting
            };
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
}
