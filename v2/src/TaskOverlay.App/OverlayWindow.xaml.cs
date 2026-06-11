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
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;

namespace TaskOverlay.App;

public partial class OverlayWindow : Window
{
    private const double SnapThreshold = 16;
    private const double WorkAreaMargin = 16;

    private static readonly System.Windows.Media.Brush ActiveBackground =
        new SolidColorBrush(Color.FromArgb(232, 24, 27, 34));
    private static readonly System.Windows.Media.Brush ActiveBorder =
        new SolidColorBrush(Color.FromArgb(96, 255, 255, 255));

    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly Action<string> _log;
    private readonly ObservableCollection<TaskItem> _activeTasks;
    private readonly DispatcherTimer _passiveTimer;
    private readonly DispatcherTimer _collapsedExpandTimer;

    private bool _allowClose;
    private bool _isShuttingDown;
    private bool _isClosed;
    private bool _isActiveMode;
    private bool _dragCandidate;
    private bool _isDragging;
    private bool _dragStartedCollapsed;
    private bool _suppressTaskClick;
    private bool _adjustingBounds;
    private DrawingPoint _dragStartCursorPixels;
    private double _dragStartLeft;
    private double _dragStartTop;
    private Matrix _dragFromDevice = Matrix.Identity;
    private double? _collapsedRestingLeft;
    private double? _collapsedRestingTop;

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

        _collapsedExpandTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _collapsedExpandTimer.Tick += CollapsedExpandTimer_OnTick;

        Loaded += OverlayWindow_OnLoaded;
        Closed += OverlayWindow_OnClosed;
        SizeChanged += OverlayWindow_OnSizeChanged;
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
        StopModeTimers();

        if (enabled)
        {
            _collapsedRestingLeft = Left;
            _collapsedRestingTop = Top;
        }
        else
        {
            _collapsedRestingLeft = null;
            _collapsedRestingTop = null;
        }

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

        StopModeTimers();
        SetActiveMode(true);
        _passiveTimer.Start();
    }

    public void HideSafely()
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        StopModeTimers();
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
        StopModeTimers();
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

        if (_state.OverlaySettings.CollapsedMode &&
            _collapsedRestingLeft is double collapsedLeft &&
            _collapsedRestingTop is double collapsedTop)
        {
            _state.WindowPlacement.Left = collapsedLeft;
            _state.WindowPlacement.Top = collapsedTop;
            return;
        }

        _state.WindowPlacement.Left = Left;
        _state.WindowPlacement.Top = Top;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        StopModeTimers();

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

        SetActiveMode(false);
        RestoreWindowPlacement();

        if (IsMouseOver)
        {
            StartCollapsedExpansionOrActivate();
        }
    }

    private void OverlayWindow_OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _isShuttingDown = true;
        StopModeTimers();
        _passiveTimer.Tick -= PassiveTimer_OnTick;
        _collapsedExpandTimer.Tick -= CollapsedExpandTimer_OnTick;
        Loaded -= OverlayWindow_OnLoaded;
        Closed -= OverlayWindow_OnClosed;
        SizeChanged -= OverlayWindow_OnSizeChanged;
    }

    private void OverlayWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isClosed || _isShuttingDown || _adjustingBounds || !IsLoaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (!_isClosed && !_isShuttingDown)
                {
                    KeepCurrentModeWithinWorkArea();
                }
            }));
    }

    private void PassiveTimer_OnTick(object? sender, EventArgs e)
    {
        _passiveTimer.Stop();

        if (_isClosed || _isShuttingDown || _isDragging || !IsLoaded)
        {
            return;
        }

        SetActiveMode(false);
    }

    private void CollapsedExpandTimer_OnTick(object? sender, EventArgs e)
    {
        _collapsedExpandTimer.Stop();

        if (_isClosed ||
            _isShuttingDown ||
            _dragCandidate ||
            _isDragging ||
            !IsMouseOver)
        {
            return;
        }

        SetActiveMode(true);
    }

    private void HoverSurface_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (_isClosed || _isShuttingDown || _isDragging)
        {
            return;
        }

        _passiveTimer.Stop();
        StartCollapsedExpansionOrActivate();
    }

    private void HoverSurface_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isClosed || _isShuttingDown || _dragCandidate || _isDragging)
        {
            return;
        }

        _collapsedExpandTimer.Stop();
        _passiveTimer.Stop();
        _passiveTimer.Start();
    }

    private void StartCollapsedExpansionOrActivate()
    {
        if (_state.OverlaySettings.CollapsedMode && !_isActiveMode)
        {
            _collapsedExpandTimer.Stop();
            _collapsedExpandTimer.Start();
            return;
        }

        SetActiveMode(true);
    }

    private void SetActiveMode(bool active)
    {
        if (_isClosed || _isShuttingDown || !Dispatcher.CheckAccess())
        {
            return;
        }

        var anchorScreen = GetCurrentScreen();
        var wasCollapsedResting =
            !_isActiveMode &&
            _state.OverlaySettings.CollapsedMode &&
            CollapsedActivation.Visibility == Visibility.Visible;

        if (active && wasCollapsedResting)
        {
            _collapsedRestingLeft = Left;
            _collapsedRestingTop = Top;
        }

        _isActiveMode = active;

        if (!active && _state.OverlaySettings.CollapsedMode)
        {
            CollapsedActivation.Visibility = Visibility.Visible;
            OverlayPanel.Visibility = Visibility.Collapsed;
            UpdateLayout();

            if (_collapsedRestingLeft is double collapsedLeft &&
                _collapsedRestingTop is double collapsedTop)
            {
                Left = collapsedLeft;
                Top = collapsedTop;
            }

            ConstrainToWorkArea(anchorScreen, snap: false);
            _collapsedRestingLeft = Left;
            _collapsedRestingTop = Top;
            return;
        }

        CollapsedActivation.Visibility = Visibility.Collapsed;
        OverlayPanel.Visibility = Visibility.Visible;
        OverlayPanel.Background = active ? ActiveBackground : Brushes.Transparent;
        OverlayPanel.BorderBrush = active ? ActiveBorder : Brushes.Transparent;
        ActiveChrome.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        ConfigureExpandedLayout(anchorScreen);
        UpdateLayout();
        ConstrainToWorkArea(anchorScreen, snap: false);
    }

    private void HoverSurface_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isClosed || _isShuttingDown || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        StopModeTimers();
        _dragCandidate = true;
        _isDragging = false;
        _dragStartedCollapsed =
            _state.OverlaySettings.CollapsedMode &&
            !_isActiveMode &&
            CollapsedActivation.Visibility == Visibility.Visible;
        _dragStartCursorPixels = Forms.Cursor.Position;
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _dragFromDevice =
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ??
            Matrix.Identity;
    }

    private void HoverSurface_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragCandidate || _isClosed || _isShuttingDown)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelDrag();
            return;
        }

        var cursor = Forms.Cursor.Position;
        var pixelDelta = new Vector(
            cursor.X - _dragStartCursorPixels.X,
            cursor.Y - _dragStartCursorPixels.Y);
        var logicalDelta = _dragFromDevice.Transform(pixelDelta);

        if (!_isDragging)
        {
            if (Math.Abs(logicalDelta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(logicalDelta.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _isDragging = true;
            _suppressTaskClick = true;
            HoverSurface.CaptureMouse();
        }

        Left = _dragStartLeft + logicalDelta.X;
        Top = _dragStartTop + logicalDelta.Y;
        e.Handled = true;
    }

    private void HoverSurface_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_dragCandidate || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var dragged = _isDragging;
        var startedCollapsed = _dragStartedCollapsed;

        if (dragged)
        {
            e.Handled = true;
            ResetDragState();

            if (HoverSurface.IsMouseCaptured)
            {
                HoverSurface.ReleaseMouseCapture();
            }

            FinishDrag(startedCollapsed);
            ScheduleTaskClickSuppressionReset();
        }
        else if (startedCollapsed)
        {
            SetActiveMode(true);
        }

        if (!dragged)
        {
            ResetDragState();
        }
    }

    private void HoverSurface_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            FinishDrag(_dragStartedCollapsed);
            ScheduleTaskClickSuppressionReset();
        }

        ResetDragState();
    }

    private void FinishDrag(bool startedCollapsed)
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        ConstrainToWorkArea(screen, snap: true);

        if (_state.OverlaySettings.CollapsedMode)
        {
            _collapsedRestingLeft = Left;
            _collapsedRestingTop = Top;
        }

        CaptureWindowPlacement();
        _saveState();
        _log($"Overlay moved: left={Left:F1}; top={Top:F1}; mode={CurrentMode}");

        if (startedCollapsed)
        {
            SetActiveMode(false);
        }
        else if (!IsMouseOver)
        {
            _passiveTimer.Start();
        }
    }

    private void CancelDrag()
    {
        ResetDragState();

        if (HoverSurface.IsMouseCaptured)
        {
            HoverSurface.ReleaseMouseCapture();
        }

        ScheduleTaskClickSuppressionReset();
    }

    private void ResetDragState()
    {
        _dragCandidate = false;
        _isDragging = false;
        _dragStartedCollapsed = false;
    }

    private void ScheduleTaskClickSuppressionReset()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => _suppressTaskClick = false));
    }

    private void TaskRow_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressTaskClick)
        {
            _suppressTaskClick = false;
            return;
        }

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
            _state.WindowPlacement.Top is double top)
        {
            Left = left;
            Top = top;
        }
        else
        {
            PositionOnCurrentMonitor();
        }

        UpdateLayout();
        var screen = GetCurrentScreen();
        ConstrainToWorkArea(screen, snap: false);

        if (_state.OverlaySettings.CollapsedMode)
        {
            _collapsedRestingLeft = Left;
            _collapsedRestingTop = Top;
        }
    }

    private void PositionOnCurrentMonitor()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var workArea = GetWorkArea(screen);

        UpdateLayout();
        Left = Math.Max(workArea.Left, workArea.Right - ActualWidth - 24);
        Top = workArea.Top + 24;
    }

    private void ConfigureExpandedLayout(Forms.Screen screen)
    {
        var workArea = GetWorkArea(screen);
        var availableWidth = Math.Max(120, workArea.Width - (WorkAreaMargin * 2));
        var availableHeight = Math.Max(80, workArea.Height - (WorkAreaMargin * 2));
        var availableContentWidth = Math.Max(80, availableWidth - 30);

        ContentStack.Width = Math.Min(420, availableContentWidth);
        OverlayPanel.MaxWidth = availableWidth;
        TasksScroller.MaxHeight = Math.Max(40, availableHeight - 80);
    }

    private void KeepCurrentModeWithinWorkArea()
    {
        var screen = GetCurrentScreen();

        if (OverlayPanel.Visibility == Visibility.Visible)
        {
            ConfigureExpandedLayout(screen);
            UpdateLayout();
        }

        ConstrainToWorkArea(screen, snap: false);
    }

    private void ConstrainToWorkArea(Forms.Screen screen, bool snap)
    {
        if (_adjustingBounds || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        _adjustingBounds = true;
        try
        {
            var workArea = GetWorkArea(screen);
            var windowBounds = new OverlayBounds(Left, Top, ActualWidth, ActualHeight);
            var corrected = snap
                ? WindowPlacementGeometry.SnapToWorkArea(
                    windowBounds,
                    workArea,
                    SnapThreshold)
                : WindowPlacementGeometry.ClampToWorkArea(windowBounds, workArea);

            Left = corrected.Left;
            Top = corrected.Top;
        }
        finally
        {
            _adjustingBounds = false;
        }
    }

    private Forms.Screen GetCurrentScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            return Forms.Screen.FromHandle(handle);
        }

        return Forms.Screen.FromPoint(Forms.Cursor.Position);
    }

    private OverlayBounds GetWorkArea(Forms.Screen screen)
    {
        var fromDevice =
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ??
            Matrix.Identity;
        var topLeft = fromDevice.Transform(
            new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = fromDevice.Transform(
            new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));

        return new OverlayBounds(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
    }

    private void StopModeTimers()
    {
        _passiveTimer.Stop();
        _collapsedExpandTimer.Stop();
    }
}
