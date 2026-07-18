using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class DueAttentionWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const int NoActivateStyle = 0x08000000;
    private const double WorkAreaMargin = 16;

    private readonly Action<Guid> _focus;
    private readonly Action<Guid> _editTask;
    private readonly Action<Guid, int> _snooze;
    private readonly Action<Guid> _markDone;
    private readonly Action<Guid> _clearReminder;
    private bool _allowClose;
    private bool _isClosed;

    public DueAttentionWindow(
        Action<Guid> focus,
        Action<Guid> editTask,
        Action<Guid, int> snooze,
        Action<Guid> markDone,
        Action<Guid> clearReminder)
    {
        _focus = focus;
        _editTask = editTask;
        _snooze = snooze;
        _markDone = markDone;
        _clearReminder = clearReminder;

        InitializeComponent();
        SourceInitialized += DueAttentionWindow_OnSourceInitialized;
        Closing += DueAttentionWindow_OnClosing;
        Closed += (_, _) => _isClosed = true;
    }

    public bool IsClosed => _isClosed;

    public void UpdateTasks(IEnumerable<TaskItem> tasks)
    {
        if (_isClosed)
        {
            return;
        }

        var items = tasks
            .Select(task => new DueAttentionItem(task.Id, task.Title))
            .ToList();
        DueItems.ItemsSource = items;
        if (items.Count == 0)
        {
            Hide();
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(PositionWithinWorkArea));
    }

    public void CloseForExit()
    {
        if (_isClosed)
        {
            return;
        }

        _allowClose = true;
        Close();
    }

    private void DueAttentionWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
        SetWindowLongPtr(
            handle,
            ExtendedStyleIndex,
            new IntPtr(styles | NoActivateStyle));
    }

    private void DueAttentionWindow_OnClosing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void PositionWithinWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left, workArea.Right - ActualWidth - WorkAreaMargin);
        Top = Math.Max(workArea.Top, workArea.Bottom - ActualHeight - WorkAreaMargin);
    }

    private void Focus_OnClick(object sender, RoutedEventArgs e) =>
        InvokeTaskAction(sender, _focus);

    private void EditTask_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid taskId })
        {
            return;
        }

        Hide();
        _editTask(taskId);
    }

    private void Snooze30_OnClick(object sender, RoutedEventArgs e) =>
        InvokeSnoozeAction(sender, 30);

    private void Snooze60_OnClick(object sender, RoutedEventArgs e) =>
        InvokeSnoozeAction(sender, 60);

    private void Done_OnClick(object sender, RoutedEventArgs e) =>
        InvokeTaskAction(sender, _markDone);

    private void Clear_OnClick(object sender, RoutedEventArgs e) =>
        InvokeTaskAction(sender, _clearReminder);

    private static void InvokeTaskAction(object sender, Action<Guid> action)
    {
        if (sender is Button { Tag: Guid taskId })
        {
            action(taskId);
        }
    }

    private void InvokeSnoozeAction(object sender, int minutes)
    {
        if (sender is Button { Tag: Guid taskId })
        {
            _snooze(taskId, minutes);
        }
    }

    private sealed record DueAttentionItem(Guid TaskId, string Title);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(
        IntPtr windowHandle,
        int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr windowHandle,
        int index,
        IntPtr newValue);
}
