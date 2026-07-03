using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class TaskDetailsView : UserControl
{
    private static readonly IReadOnlyList<TaskStatusOption> DetailStatuses =
        new[]
        {
            new TaskStatusOption(TaskStatus.Todo, "TODO"),
            new TaskStatusOption(TaskStatus.InWork, "FOCUS"),
            new TaskStatusOption(TaskStatus.Waiting, "WAIT"),
            new TaskStatusOption(TaskStatus.Done, "DONE")
        };

    private readonly TaskItem _task;
    private readonly Action<TaskItem, TaskEditValues> _saveTask;
    private readonly Action<TaskItem> _deleteTask;
    private readonly Action<bool> _modalInteractionChanged;
    private readonly Action _closeShell;
    private readonly DateTimeOffset? _originalRemindAtUtc;
    private readonly int? _originalRepeatMinutes;
    private bool _initializing;

    public TaskDetailsView(
        AppState state,
        TaskItem task,
        Action<TaskItem, TaskEditValues> saveTask,
        Action<TaskItem> deleteTask,
        Action<bool> modalInteractionChanged,
        Action closeShell)
    {
        _task = task;
        _saveTask = saveTask;
        _deleteTask = deleteTask;
        _modalInteractionChanged = modalInteractionChanged;
        _closeShell = closeShell;
        _originalRemindAtUtc = task.RemindAtUtc;
        _originalRepeatMinutes = task.RemindEveryMinutes;

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
        CompactReminderEditor.Initialize(task.RemindAtUtc, task.RemindEveryMinutes);

        _initializing = false;
        UpdateWaitingField();
    }

    public Guid TaskId => _task.Id;

    public void OnActivated()
    {
        if (!IsKeyboardFocusWithin)
        {
            TitleTextBox.Focus();
        }
    }

    private void StatusListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateWaitingField();
        if (!_initializing &&
            StatusListBox.SelectedItem is TaskStatusOption
            {
                Value: TaskStatus.Waiting
            })
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => WaitingForTextBox.Focus()));
        }
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
            StatusListBox.SelectedItem is not TaskStatusOption status)
        {
            ShowWarning("Choose a project and status.");
            return;
        }

        if (!CompactReminderEditor.TryGetValue(out var reminder))
        {
            ShowWarning("Choose a valid reminder date and time.");
            CompactReminderEditor.FocusDateTime();
            return;
        }

        var replaceSchedule =
            _originalRemindAtUtc != reminder.RemindAtUtc ||
            _originalRepeatMinutes != reminder.RepeatMinutes;
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
                reminder.RemindAtUtc,
                reminder.RepeatMinutes,
                replaceSchedule));
        _closeShell();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) =>
        _closeShell();

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var result = ShowModalMessage(
            () => MessageBox.Show(
                Window.GetWindow(this),
                $"Delete \"{_task.Title}\"?",
                "Delete task",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning));
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _deleteTask(_task);
        _closeShell();
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
        UpdateWaitingPlaceholder();
    }

    private void WaitingForTextBox_OnChanged(object sender, TextChangedEventArgs e) =>
        UpdateWaitingPlaceholder();

    private void WaitingForTextBox_OnFocusChanged(
        object sender,
        KeyboardFocusChangedEventArgs e) =>
        UpdateWaitingPlaceholder();

    private void UpdateWaitingPlaceholder()
    {
        if (WaitingForPlaceholder is null)
        {
            return;
        }

        WaitingForPlaceholder.Visibility =
            string.IsNullOrEmpty(WaitingForTextBox.Text) &&
            !WaitingForTextBox.IsKeyboardFocusWithin
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ShowWarning(string message)
    {
        ShowModalMessage(
            () => MessageBox.Show(
                Window.GetWindow(this),
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
