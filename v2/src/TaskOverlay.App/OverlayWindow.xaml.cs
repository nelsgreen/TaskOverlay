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
    private static readonly System.Windows.Media.Brush CollapsedHandleBackground =
        new SolidColorBrush(Color.FromArgb(204, 39, 42, 50));
    private static readonly System.Windows.Media.Brush CollapsedHandleBorder =
        new SolidColorBrush(Color.FromArgb(153, 255, 232, 120));
    private static readonly System.Windows.Media.Brush CollapsedHandleForeground =
        new SolidColorBrush(Color.FromRgb(255, 232, 120));
    private static readonly System.Windows.Media.Brush ExpandedHandleBackground =
        new SolidColorBrush(Color.FromArgb(220, 30, 58, 82));
    private static readonly System.Windows.Media.Brush ExpandedHandleBorder =
        new SolidColorBrush(Color.FromArgb(190, 96, 165, 250));
    private static readonly System.Windows.Media.Brush ExpandedHandleForeground =
        new SolidColorBrush(Color.FromRgb(147, 197, 253));
    private static readonly System.Windows.Media.Brush PinnedHandleBackground =
        new SolidColorBrush(Color.FromArgb(230, 24, 61, 52));
    private static readonly System.Windows.Media.Brush PinnedHandleBorder =
        new SolidColorBrush(Color.FromArgb(220, 52, 211, 153));
    private static readonly System.Windows.Media.Brush PinnedHandleForeground =
        new SolidColorBrush(Color.FromRgb(110, 231, 183));

    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly Action<string> _log;
    private readonly ObservableCollection<TaskRowViewModel> _activeTasks;
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
    private bool _contextInteractionActive;
    private bool _settingsInteractionActive;
    private int _modalInteractionCount;
    private DrawingPoint _dragStartCursorPixels;
    private double _dragStartLeft;
    private double _dragStartTop;
    private Matrix _dragFromDevice = Matrix.Identity;
    private double? _collapsedRestingLeft;
    private double? _collapsedRestingTop;
    private Forms.Screen? _collapsedRestingScreen;
    private TaskDetailsWindow? _taskDetailsWindow;

    public event Action<bool>? PinnedActiveModeChanged;

    public OverlayWindow(AppState state, Action saveState, Action<string> log)
    {
        _state = state;
        _saveState = saveState;
        _log = log;
        _activeTasks = new ObservableCollection<TaskRowViewModel>();

        InitializeComponent();
        ActiveTasks.ItemsSource = _activeTasks;
        RefreshTasks();
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
            : _state.OverlaySettings.PinnedActiveMode
                ? "pinned"
                : _isActiveMode
                    ? "active"
                    : _state.OverlaySettings.CollapsedMode
                        ? "collapsed"
                        : "passive";
    public bool IsClosed => _isClosed;
    public bool IsCollapsedModeEnabled => _state.OverlaySettings.CollapsedMode;
    public bool IsPinnedActiveModeEnabled =>
        _state.OverlaySettings.PinnedActiveMode;

    public void RestoreVisibleMode()
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        if (_state.OverlaySettings.PinnedActiveMode)
        {
            SetActiveMode(true);
        }
    }

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
            _collapsedRestingScreen = GetCurrentScreen();
            _state.WindowPlacement.CollapsedLeft = Left;
            _state.WindowPlacement.CollapsedTop = Top;
        }
        else
        {
            _collapsedRestingLeft = null;
            _collapsedRestingTop = null;
            _collapsedRestingScreen = null;

            if (_state.WindowPlacement.Left is double normalLeft &&
                _state.WindowPlacement.Top is double normalTop)
            {
                Left = normalLeft;
                Top = normalTop;
            }
        }

        SetActiveMode(IsMouseOver);
    }

    public void SetPinnedActiveMode(bool enabled)
    {
        if (_isClosed ||
            _isShuttingDown ||
            _state.OverlaySettings.PinnedActiveMode == enabled)
        {
            return;
        }

        _state.OverlaySettings.PinnedActiveMode = enabled;
        StopModeTimers();

        if (enabled)
        {
            SetActiveMode(true);
        }
        else
        {
            UpdateHandleVisual();
            ScheduleCollapse();
        }

        _saveState();
        _log($"Pinned active mode changed: enabled={enabled}.");
        PinnedActiveModeChanged?.Invoke(enabled);
    }

    public void SetSettingsInteractionActive(bool active)
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        _settingsInteractionActive = active;
        if (active)
        {
            StopModeTimers();
            SetActiveMode(true);
        }
        else
        {
            ScheduleCollapse();
        }
    }

    public void RevealTasks(IEnumerable<TaskItem> tasks)
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        RefreshTasks();

        StopModeTimers();
        SetActiveMode(true);
        ScheduleCollapse();
    }

    public void HideSafely()
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        StopModeTimers();
        if (!_state.OverlaySettings.PinnedActiveMode)
        {
            SetActiveMode(false, force: true);
        }

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
        _taskDetailsWindow?.Close();
        _taskDetailsWindow = null;
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
            _state.WindowPlacement.CollapsedLeft = collapsedLeft;
            _state.WindowPlacement.CollapsedTop = collapsedTop;
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
            if (!_state.OverlaySettings.PinnedActiveMode)
            {
                SetActiveMode(false, force: true);
            }

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

        SetActiveMode(false, force: true);
        RestoreWindowPlacement();

        if (_state.OverlaySettings.PinnedActiveMode)
        {
            SetActiveMode(true);
        }
        else if (IsMouseOver)
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

        if (_isClosed ||
            _isShuttingDown ||
            !IsLoaded ||
            IsMouseOver ||
            !CanCollapse())
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
        ScheduleCollapse();
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

    private void SetActiveMode(bool active, bool force = false)
    {
        if (_isClosed || _isShuttingDown || !Dispatcher.CheckAccess())
        {
            return;
        }

        if (!active && !force && !CanCollapse())
        {
            UpdateHandleVisual();
            return;
        }

        var anchorScreen =
            _state.OverlaySettings.CollapsedMode && _collapsedRestingScreen is not null
                ? _collapsedRestingScreen
                : GetCurrentScreen();
        var wasCollapsedResting =
            !_isActiveMode &&
            _state.OverlaySettings.CollapsedMode &&
            OverlayPanel.Visibility == Visibility.Collapsed;

        if (active && wasCollapsedResting)
        {
            _collapsedRestingLeft = Left;
            _collapsedRestingTop = Top;
            _collapsedRestingScreen = anchorScreen;
        }

        var modeChanged = _isActiveMode != active;
        _isActiveMode = active;
        if (modeChanged)
        {
            RefreshTasks();
        }

        if (!active && _state.OverlaySettings.CollapsedMode)
        {
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
            _collapsedRestingScreen = anchorScreen;
            UpdateHandleVisual();
            return;
        }

        OverlayPanel.Visibility = Visibility.Visible;
        OverlayPanel.Background = active ? ActiveBackground : Brushes.Transparent;
        OverlayPanel.BorderBrush = active ? ActiveBorder : Brushes.Transparent;
        ActiveChrome.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        ConfigureExpandedLayout(anchorScreen);
        UpdateLayout();
        ConstrainToWorkArea(anchorScreen, snap: false);
        UpdateHandleVisual();
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
            OverlayPanel.Visibility == Visibility.Collapsed;
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

        if (startedCollapsed)
        {
            _collapsedRestingLeft = Left;
            _collapsedRestingTop = Top;
            _collapsedRestingScreen = screen;
            _state.WindowPlacement.CollapsedLeft = Left;
            _state.WindowPlacement.CollapsedTop = Top;
        }
        else
        {
            _state.WindowPlacement.Left = Left;
            _state.WindowPlacement.Top = Top;
        }

        _saveState();
        _log($"Overlay moved: left={Left:F1}; top={Top:F1}; mode={CurrentMode}");

        if (startedCollapsed)
        {
            SetActiveMode(false);
        }
        else if (!IsMouseOver)
        {
            ScheduleCollapse();
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

    private void ActivationHandle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressTaskClick)
        {
            _suppressTaskClick = false;
            return;
        }

        SetPinnedActiveMode(!_state.OverlaySettings.PinnedActiveMode);
    }

    private void TaskMarker_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetTask(sender, out var task))
        {
            return;
        }

        if (!TaskInteractionService.Complete(task))
        {
            return;
        }

        _log($"Task completed: id={task.Id}; title={task.Title}");
        RefreshTasks();
        _saveState();
    }

    private void TaskBody_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetTask(sender, out var task))
        {
            return;
        }

        TaskInteractionService.ActivateFromClick(_state, task);
        _log($"Task in-work changed: id={task.Id}; inWork={task.InWork}");
        RefreshTasks();
        _saveState();
    }

    private bool TryGetTask(object sender, out TaskItem task)
    {
        task = null!;

        if (_suppressTaskClick)
        {
            _suppressTaskClick = false;
            return false;
        }

        if (_isClosed ||
            _isShuttingDown ||
            sender is not FrameworkElement
            {
                DataContext: TaskRowViewModel row
            } ||
            row.Task.Completed)
        {
            return false;
        }

        task = row.Task;
        return true;
    }

    private void TaskRow_OnContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        StopModeTimers();
        SetActiveMode(true);

        if (sender is not Border
            {
                DataContext: TaskRowViewModel row,
                ContextMenu: ContextMenu contextMenu
            })
        {
            return;
        }

        if (contextMenu.Items[1] is MenuItem descriptionItem)
        {
            descriptionItem.Header =
                row.Task.DescriptionExpanded
                    ? "Hide description"
                    : "Show description";
            descriptionItem.IsEnabled =
                !string.IsNullOrWhiteSpace(row.Task.Description);
        }

        if (contextMenu.Items[2] is MenuItem inWorkItem)
        {
            inWorkItem.Header = "Mark as in work";
            inWorkItem.IsEnabled = !row.Task.InWork;
        }
    }

    private void TaskContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        _contextInteractionActive = true;
        StopModeTimers();
    }

    private void TaskContextMenu_OnClosed(object sender, RoutedEventArgs e)
    {
        _contextInteractionActive = false;
        ScheduleCollapse();
    }

    private void EditTaskMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetMenuTask(sender, out var task))
        {
            OpenTaskDetails(task);
        }
    }

    private void ToggleDescriptionMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuTask(sender, out var task))
        {
            return;
        }

        TaskInteractionService.ToggleDescription(task);
        RefreshTasks();
        _saveState();
    }

    private void ToggleInWorkMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuTask(sender, out var task))
        {
            return;
        }

        TaskInteractionService.SetInWork(_state, task, true);
        RefreshTasks();
        _saveState();
    }

    private void CompleteTaskMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuTask(sender, out var task) ||
            !TaskInteractionService.Complete(task))
        {
            return;
        }

        RefreshTasks();
        _saveState();
    }

    private void DeleteTaskMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuTask(sender, out var task))
        {
            return;
        }

        var result = ShowModalMessage(
            () => MessageBox.Show(
                this,
                $"Delete \"{task.Title}\"?",
                "Delete task",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning));

        if (result == MessageBoxResult.Yes)
        {
            DeleteTask(task);
        }
    }

    private static bool TryGetMenuTask(object sender, out TaskItem task)
    {
        task = null!;

        if (sender is not MenuItem
            {
                DataContext: TaskRowViewModel row
            })
        {
            return false;
        }

        task = row.Task;
        return true;
    }

    private void OpenTaskDetails(TaskItem task)
    {
        if (_taskDetailsWindow is not null)
        {
            _taskDetailsWindow.Activate();
            return;
        }

        StopModeTimers();
        SetActiveMode(true);
        _taskDetailsWindow = new TaskDetailsWindow(
            task,
            SaveTaskEdits,
            DeleteTask,
            SetModalInteractionActive)
        {
            Owner = this
        };
        _taskDetailsWindow.Closed += TaskDetailsWindow_OnClosed;
        _taskDetailsWindow.Show();
        _taskDetailsWindow.Activate();
    }

    private void TaskDetailsWindow_OnClosed(object? sender, EventArgs e)
    {
        if (_taskDetailsWindow is not null)
        {
            _taskDetailsWindow.Closed -= TaskDetailsWindow_OnClosed;
            _taskDetailsWindow = null;
        }

        ScheduleCollapse();
    }

    private void SaveTaskEdits(TaskItem task, TaskEditValues values)
    {
        TaskInteractionService.Update(_state, task, values);
        RefreshTasks();
        _saveState();
        _log($"Task edited: id={task.Id}; completed={task.Completed}; inWork={task.InWork}");
    }

    private void DeleteTask(TaskItem task)
    {
        if (!TaskInteractionService.Delete(_state, task))
        {
            return;
        }

        RefreshTasks();
        _saveState();
        _log($"Task deleted: id={task.Id}; title={task.Title}");
    }

    public void RefreshTaskPresentation()
    {
        if (!_isClosed && !_isShuttingDown)
        {
            RefreshTasks();
            UpdateHandleVisual();
        }
    }

    private void RefreshTasks()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshTasks));
            return;
        }

        _activeTasks.Clear();
        foreach (var task in _state.Tasks.Where(task => !task.Completed))
        {
            _activeTasks.Add(new TaskRowViewModel(task, _isActiveMode));
        }
    }

    private void RestoreWindowPlacement()
    {
        var useCollapsedAnchor = _state.OverlaySettings.CollapsedMode;
        var savedLeft = useCollapsedAnchor
            ? _state.WindowPlacement.CollapsedLeft ?? _state.WindowPlacement.Left
            : _state.WindowPlacement.Left;
        var savedTop = useCollapsedAnchor
            ? _state.WindowPlacement.CollapsedTop ?? _state.WindowPlacement.Top
            : _state.WindowPlacement.Top;

        if (savedLeft is double left && savedTop is double top)
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
            _collapsedRestingScreen = screen;
            _state.WindowPlacement.CollapsedLeft = Left;
            _state.WindowPlacement.CollapsedTop = Top;
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

    private bool CanCollapse()
    {
        return OverlayCollapseGuard.CanCollapse(
            new OverlayInteractionState(
                PinnedActiveMode: _state.OverlaySettings.PinnedActiveMode,
                TaskDetailsOpen: _taskDetailsWindow is not null,
                ContextMenuOpen: _contextInteractionActive,
                SettingsOpen: _settingsInteractionActive,
                ModalDialogOpen: _modalInteractionCount > 0,
                Dragging: _dragCandidate || _isDragging));
    }

    private void ScheduleCollapse()
    {
        _passiveTimer.Stop();

        if (_isClosed ||
            _isShuttingDown ||
            !IsLoaded ||
            IsMouseOver ||
            !CanCollapse())
        {
            return;
        }

        _passiveTimer.Start();
    }

    private void SetModalInteractionActive(bool active)
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        if (active)
        {
            _modalInteractionCount++;
            StopModeTimers();
            SetActiveMode(true);
            return;
        }

        _modalInteractionCount = Math.Max(0, _modalInteractionCount - 1);
        ScheduleCollapse();
    }

    private T ShowModalMessage<T>(Func<T> showDialog)
    {
        SetModalInteractionActive(true);
        try
        {
            return showDialog();
        }
        finally
        {
            SetModalInteractionActive(false);
        }
    }

    private void UpdateHandleVisual()
    {
        if (_state.OverlaySettings.PinnedActiveMode)
        {
            CollapsedActivation.Background = PinnedHandleBackground;
            CollapsedActivation.BorderBrush = PinnedHandleBorder;
            CollapsedActivation.Foreground = PinnedHandleForeground;
            CollapsedActivation.ToolTip = "Pinned expanded. Click to unpin.";
            ModeStatus.Text = "PINNED";
            return;
        }

        if (OverlayPanel.Visibility == Visibility.Collapsed)
        {
            CollapsedActivation.Background = CollapsedHandleBackground;
            CollapsedActivation.BorderBrush = CollapsedHandleBorder;
            CollapsedActivation.Foreground = CollapsedHandleForeground;
            CollapsedActivation.ToolTip = "Collapsed. Click to pin expanded.";
            ModeStatus.Text = "COLLAPSED";
            return;
        }

        CollapsedActivation.Background = ExpandedHandleBackground;
        CollapsedActivation.BorderBrush = ExpandedHandleBorder;
        CollapsedActivation.Foreground = ExpandedHandleForeground;
        CollapsedActivation.ToolTip = "Expanded. Click to keep expanded.";
        ModeStatus.Text = "ACTIVE";
    }

    private void StopModeTimers()
    {
        _passiveTimer.Stop();
        _collapsedExpandTimer.Stop();
    }
}
