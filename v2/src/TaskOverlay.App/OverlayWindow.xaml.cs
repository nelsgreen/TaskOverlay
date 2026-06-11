using System;
using System.Collections.Generic;
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
    private readonly Action<string> _log;
    private readonly ObservableCollection<TaskItem> _activeTasks;
    private readonly DispatcherTimer _passiveTimer;
    private bool _allowClose;
    private bool _isShuttingDown;
    private bool _isClosed;
    private bool _isActiveMode;

    public OverlayWindow(AppState state, Action saveState, Action<string> log)
    {
        _state = state;
        _saveState = saveState;
        _log = log;
        _activeTasks = new ObservableCollection<TaskItem>(
            state.Tasks.Where(task => !task.Completed));

        InitializeComponent();
        ActiveTasks.ItemsSource = _activeTasks;
        Topmost = state.OverlaySettings.AlwaysOnTop;

        _passiveTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(
                Math.Max(0, state.OverlaySettings.ActiveToPassiveDelayMilliseconds))
        };
        _passiveTimer.Tick += PassiveTimer_OnTick;

        Loaded += OverlayWindow_OnLoaded;
        Closed += OverlayWindow_OnClosed;
    }

    public string CurrentMode =>
        _isClosed
            ? "closed"
            : _isActiveMode
                ? "active"
                : _state.OverlaySettings.CollapsedMode
                    ? "collapsed"
                    : "passive";
    public bool IsClosed => _isClosed;
    public bool IsCollapsedModeEnabled => _state.OverlaySettings.CollapsedMode;

    public void SetCollapsedMode(bool enabled)
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        _state.OverlaySettings.CollapsedMode = enabled;
        _passiveTimer.Stop();
        SetActiveMode(IsMouseOver);
    }

    public void RevealTasks(IEnumerable<TaskItem> tasks)
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        foreach (var task in tasks)
        {
            if (_activeTasks.All(item => item.Id != task.Id))
            {
                _activeTasks.Add(task);
            }
        }

        _passiveTimer.Stop();
        SetActiveMode(true);
        _passiveTimer.Start();
    }

    public void HideSafely()
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        _passiveTimer.Stop();
        SetActiveMode(false);
        Hide();
    }

    public void PrepareForShutdown()
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _passiveTimer.Stop();
        CaptureWindowPlacement();
    }

    public void CloseForExit()
    {
        if (_isClosed)
        {
            return;
        }

        PrepareForShutdown();
        _allowClose = true;
        Close();
    }

    public void CaptureWindowPlacement()
    {
        if (_isClosed || WindowState != WindowState.Normal)
        {
            return;
        }

        _state.WindowPlacement.Left = Left;
        _state.WindowPlacement.Top = Top;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _passiveTimer.Stop();

        if (!_allowClose && !_isShuttingDown)
        {
            e.Cancel = true;
            SetActiveMode(false);
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void OverlayWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        RestoreWindowPlacement();
        SetActiveMode(IsMouseOver);
    }

    private void OverlayWindow_OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _isShuttingDown = true;
        _passiveTimer.Stop();
        _passiveTimer.Tick -= PassiveTimer_OnTick;
        Loaded -= OverlayWindow_OnLoaded;
        Closed -= OverlayWindow_OnClosed;
    }

    private void PassiveTimer_OnTick(object? sender, EventArgs e)
    {
        _passiveTimer.Stop();

        if (_isClosed || _isShuttingDown || !IsLoaded)
        {
            return;
        }

        SetActiveMode(false);
    }

    private void HoverSurface_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        _passiveTimer.Stop();
        SetActiveMode(true);
    }

    private void HoverSurface_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        _passiveTimer.Stop();
        _passiveTimer.Start();
    }

    private void SetActiveMode(bool active)
    {
        if (_isClosed || _isShuttingDown || !Dispatcher.CheckAccess())
        {
            return;
        }

        _isActiveMode = active;

        if (!active && _state.OverlaySettings.CollapsedMode)
        {
            CollapsedActivation.Visibility = Visibility.Visible;
            OverlayPanel.Visibility = Visibility.Collapsed;
            return;
        }

        CollapsedActivation.Visibility = Visibility.Collapsed;
        OverlayPanel.Visibility = Visibility.Visible;
        OverlayPanel.Background = active ? ActiveBackground : Brushes.Transparent;
        OverlayPanel.BorderBrush = active ? ActiveBorder : Brushes.Transparent;
        ActiveChrome.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TaskRow_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isClosed ||
            _isShuttingDown ||
            sender is not Button { DataContext: TaskItem task } ||
            task.Completed)
        {
            return;
        }

        task.Completed = true;
        task.CompletedAtUtc = DateTimeOffset.UtcNow;
        _activeTasks.Remove(task);
        _log($"Task completed: id={task.Id}; title={task.Title}");
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
