using System;
using System.Windows;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class TaskDetailsWindow : Window
{
    private readonly TaskItem _task;
    private readonly Action<TaskItem, TaskEditValues> _saveTask;
    private readonly Action<TaskItem> _deleteTask;
    private readonly Action<bool> _modalInteractionChanged;

    public TaskDetailsWindow(
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
        InWorkCheckBox.IsChecked = task.InWork;
        CompletedCheckBox.IsChecked = task.Completed;
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            ShowModalMessage(
                () => MessageBox.Show(
                    this,
                    "Task title cannot be empty.",
                    "TaskOverlay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
            TitleTextBox.Focus();
            return;
        }

        _saveTask(
            _task,
            new TaskEditValues(
                TitleTextBox.Text,
                DescriptionTextBox.Text,
                InWorkCheckBox.IsChecked == true,
                CompletedCheckBox.IsChecked == true));
        Close();
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
