using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class QuickAddWindow : Window
{
    private readonly Func<QuickTaskValues, bool> _addTask;

    public QuickAddWindow(
        AppState state,
        Func<QuickTaskValues, bool> addTask,
        WindowNavigationActions navigation)
    {
        _addTask = addTask;
        InitializeComponent();
        WindowSwitcher.Configure(navigation, AppWindowKind.QuickAdd);

        var projects = state.Projects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.Name)
            .ToList();
        ProjectListBox.ItemsSource = projects;
        ProjectListBox.SelectedItem = TaskCaptureService.ResolvePreferredProject(state) ??
                                      projects.FirstOrDefault();
        StatusListBox.ItemsSource = TaskAttentionUiOptions.QuickAddStatuses;
        StatusListBox.SelectedIndex = 0;
        ReminderListBox.ItemsSource = TaskAttentionUiOptions.QuickAddReminderPresets;
        ReminderListBox.SelectedIndex = 0;
        Loaded += (_, _) => TitleTextBox.Focus();
        Activated += (_, _) => WindowSwitcher.RefreshAvailability();
        UpdatePlaceholders();
        UpdateWaitingField();
    }

    private void StatusListBox_OnSelectionChanged(
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

        if (ProjectListBox.SelectedItem is not ProjectItem project ||
            StatusListBox.SelectedItem is not TaskStatusOption status ||
            ReminderListBox.SelectedItem is not ReminderPresetOption reminder)
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
        if (WaitingForTextBox is null || WaitingCard is null)
        {
            return;
        }

        var waiting = StatusListBox.SelectedItem is TaskStatusOption
            {
                Value: TaskStatus.Waiting
            };
        WaitingForTextBox.IsEnabled = waiting;
        WaitingCard.Visibility = waiting
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void InputTextBox_OnTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdatePlaceholders();
    }

    private void UpdatePlaceholders()
    {
        if (TitlePlaceholder is not null)
        {
            TitlePlaceholder.Visibility = string.IsNullOrEmpty(TitleTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (DescriptionPlaceholder is not null)
        {
            DescriptionPlaceholder.Visibility =
                string.IsNullOrEmpty(DescriptionTextBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }
}
