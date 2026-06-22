using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class QuickAddWindow : Window
{
    private readonly Func<QuickTaskValues, bool> _addTask;

    public QuickAddWindow(AppState state, Func<QuickTaskValues, bool> addTask)
    {
        _addTask = addTask;
        InitializeComponent();

        var projects = state.Projects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.Name)
            .ToList();
        ProjectComboBox.ItemsSource = projects;
        ProjectComboBox.SelectedItem = TaskCaptureService.ResolvePreferredProject(state) ??
                                       projects.FirstOrDefault();
        StatusComboBox.ItemsSource = TaskAttentionUiOptions.QuickAddStatuses;
        StatusComboBox.SelectedIndex = 0;
        ReminderComboBox.ItemsSource = TaskAttentionUiOptions.ReminderPresets;
        ReminderComboBox.SelectedIndex = 0;
        Loaded += (_, _) => TitleTextBox.Focus();
        UpdateWaitingField();
    }

    private void StatusComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateWaitingField();
    }

    private void AddButton_OnClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            ValidationText.Text = "A title is required.";
            TitleTextBox.Focus();
            return;
        }

        if (ProjectComboBox.SelectedItem is not ProjectItem project ||
            StatusComboBox.SelectedItem is not TaskStatusOption status ||
            ReminderComboBox.SelectedItem is not ReminderPresetOption reminder)
        {
            ValidationText.Text = "Choose a project, status, and reminder.";
            return;
        }

        var values = new QuickTaskValues(
            TitleTextBox.Text,
            project.Id,
            status.Value,
            reminder.Value,
            WaitingForTextBox.Text,
            DescriptionTextBox.Text);
        if (!_addTask(values))
        {
            ValidationText.Text = "The task could not be created.";
            return;
        }

        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
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
}
