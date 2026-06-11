using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TaskOverlay.Core;
using Forms = System.Windows.Forms;

namespace TaskOverlay.App;

public partial class OverlayWindow : Window
{
    private static readonly System.Windows.Media.Brush ActiveBackground =
        new SolidColorBrush(Color.FromArgb(232, 24, 27, 34));
    private static readonly System.Windows.Media.Brush ActiveBorder =
        new SolidColorBrush(Color.FromArgb(96, 255, 255, 255));

    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly ObservableCollection<TaskItem> _activeTasks;
    private readonly DispatcherTimer _passiveTimer;
    private bool _allowClose;

    public OverlayWindow(AppState state, Action saveState)
    {
        _state = state;
        _saveState = saveState;
        _activeTasks = new ObservableCollection<TaskItem>(
            state.Tasks.Where(task => !task.Completed));

        InitializeComponent();
        ActiveTasks.ItemsSource = _activeTasks;
        Topmost = state.OverlaySettings.AlwaysOnTop;

        _passiveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(
                Math.Max(0, state.OverlaySettings.ActiveToPassiveDelayMilliseconds))
        };
        _passiveTimer.Tick += (_, _) =>
        {
            _passiveTimer.Stop();
            SetActiveMode(false);
        };

        Loaded += (_, _) =>
        {
            RestoreWindowPlacement();
            SetActiveMode(false);
        };
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    public void CaptureWindowPlacement()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        _state.WindowPlacement.Left = Left;
        _state.WindowPlacement.Top = Top;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void HoverSurface_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _passiveTimer.Stop();
        SetActiveMode(true);
    }

    private void HoverSurface_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _passiveTimer.Stop();
        _passiveTimer.Start();
    }

    private void SetActiveMode(bool active)
    {
        OverlayPanel.Background = active ? ActiveBackground : Brushes.Transparent;
        OverlayPanel.BorderBrush = active ? ActiveBorder : Brushes.Transparent;
        ActiveChrome.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TaskRow_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TaskItem task } || task.Completed)
        {
            return;
        }

        task.Completed = true;
        task.CompletedAtUtc = DateTimeOffset.UtcNow;
        _activeTasks.Remove(task);
        _saveState();
    }

    private void RestoreWindowPlacement()
    {
        if (_state.WindowPlacement.Left is double left &&
            _state.WindowPlacement.Top is double top &&
            IsPointOnVirtualScreen(left, top))
        {
            Left = left;
            Top = top;
            return;
        }

        PositionOnCurrentMonitor();
    }

    private static bool IsPointOnVirtualScreen(double left, double top)
    {
        return left >= SystemParameters.VirtualScreenLeft &&
               left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
               top >= SystemParameters.VirtualScreenTop &&
               top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
    }

    private void PositionOnCurrentMonitor()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var topLeft = fromDevice.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = fromDevice.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));

        const double margin = 24;
        Left = Math.Max(topLeft.X + margin, bottomRight.X - ActualWidth - margin);
        Top = topLeft.Y + margin;
    }
}
