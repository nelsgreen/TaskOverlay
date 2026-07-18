using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class QuickAddView : UserControl
{
    private readonly Func<QuickTaskValues, bool> _addTask;
    private readonly Action _closeShell;

    public QuickAddView(
        AppState state,
        Func<QuickTaskValues, bool> addTask,
        Action closeShell)
    {
        _addTask = addTask;
        _closeShell = closeShell;
        InitializeComponent();

        var projects = state.Projects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.Name)
            .ToList();
        ProjectListBox.ItemsSource = projects;
        ProjectListBox.SelectedItem = TaskCaptureService.ResolvePreferredProject(state) ??
                                      projects.FirstOrDefault();
        StatusListBox.ItemsSource = TaskAttentionUiOptions.QuickAddStatuses;
        StatusListBox.SelectedIndex = 0;
        ReminderEditor.Initialize(null, null);
        UpdatePlaceholders();
        UpdateWaitingField();
    }

    public void OnActivated()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (!IsVisible)
                {
                    return;
                }

                TitleTextBox.Focus();
                Keyboard.Focus(TitleTextBox);
                TitleTextBox.CaretIndex = TitleTextBox.Text.Length;
                UpdatePlaceholders();
            }));
    }

    private void StatusListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateWaitingField();
        if (StatusListBox.SelectedItem is TaskStatusOption
            {
                Value: TaskStatus.Waiting
            })
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => WaitingForTextBox.Focus()));
        }
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
            StatusListBox.SelectedItem is not TaskStatusOption status)
        {
            ValidationText.Text = "Choose a project and status.";
            return;
        }

        if (!ReminderEditor.TryGetValue(out var reminder))
        {
            ValidationText.Text = "Choose a valid reminder date and time.";
            ReminderEditor.FocusDateTime();
            return;
        }

        var values = new QuickTaskValues(
            TitleTextBox.Text,
            project.Id,
            status.Value,
            ReminderPreset.KeepCurrent,
            WaitingForTextBox.Text,
            DescriptionTextBox.Text,
            reminder.RemindAtUtc,
            reminder.RepeatMinutes,
            true);
        if (!_addTask(values))
        {
            ValidationText.Text = "The task could not be created.";
            return;
        }

        _closeShell();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        _closeShell();
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

    private void InputTextBox_OnTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdatePlaceholders();
    }

    private void InputTextBox_OnKeyboardFocusChanged(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        UpdatePlaceholders();
    }

    private void UpdatePlaceholders()
    {
        if (TitlePlaceholder is not null)
        {
            TitlePlaceholder.Visibility =
                string.IsNullOrEmpty(TitleTextBox.Text) &&
                !TitleTextBox.IsKeyboardFocusWithin
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (DescriptionPlaceholder is not null)
        {
            DescriptionPlaceholder.Visibility =
                string.IsNullOrEmpty(DescriptionTextBox.Text) &&
                !DescriptionTextBox.IsKeyboardFocusWithin
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }
}
