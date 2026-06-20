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
    private bool _overlayVisible = true;
    private bool _isActiveMode;
    private bool _useHandleWindowForPinnedExpanded;
    private bool _dragCandidate;
    private bool _isDragging;
    private bool _handleDragCandidate;
    private bool _isHandleDragging;
    private bool _finishingHandleDrag;
    private bool _suppressTaskClick;
    private bool _adjustingBounds;
    private bool _contextInteractionActive;
    private bool _settingsInteractionActive;
    private int _modalInteractionCount;
    private DrawingPoint _dragStartCursorPixels;
    private double _dragStartLeft;
    private double _dragStartTop;
    private Matrix _dragFromDevice = Matrix.Identity;
    private HandleWindow? _handleWindow;
    private TaskDetailsWindow? _taskDetailsWindow;

    public event Action<OverlayMode>? OverlayModeChanged;

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
            : _state.OverlaySettings.OverlayMode == OverlayMode.PinnedExpanded
                ? "pinned-expanded"
                : _isActiveMode
                    ? "active"
                    : _state.OverlaySettings.OverlayMode == OverlayMode.CollapsedHandle
                        ? "collapsed-handle"
                        : "passive";
    public bool IsClosed => _isClosed;
    public bool IsOverlayVisible =>
        _overlayVisible && (IsVisible || _handleWindow?.IsVisible == true);

    public void RestoreVisibleMode()
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        _overlayVisible = true;
        var shouldExpand =
            _state.OverlaySettings.OverlayMode == OverlayMode.PinnedExpanded ||
            !CanCollapse();
        SetActiveMode(shouldExpand, force: !shouldExpand);
    }

    public void SetOverlayMode(OverlayMode mode)
    {
        if (_isClosed ||
            _isShuttingDown ||
            _state.OverlaySettings.OverlayMode == mode)
        {
            return;
        }

        var previousMode = _state.OverlaySettings.OverlayMode;
        StopModeTimers();
        _state.OverlaySettings.OverlayMode = mode;

        if (mode == OverlayMode.CollapsedHandle)
        {
            _useHandleWindowForPinnedExpanded = false;
            if (_state.WindowPlacement.CollapsedLeft is null ||
                _state.WindowPlacement.CollapsedTop is null)
            {
                CaptureCollapsedAnchor();
            }

            RestoreCollapsedHandle();
            SetActiveMode(false, force: true);
        }
        else if (mode == OverlayMode.PinnedExpanded)
        {
            _useHandleWindowForPinnedExpanded =
                OverlaySurfacePolicy.UseHandleWindowForPinned(
                    previousMode,
                    mode,
                    _state.WindowPlacement.CollapsedLeft is not null &&
                    _state.WindowPlacement.CollapsedTop is not null);

            if (_useHandleWindowForPinnedExpanded &&
                _state.WindowPlacement.CollapsedLeft is double anchorLeft &&
                _state.WindowPlacement.CollapsedTop is double anchorTop)
            {
                PositionPanelFromCollapsedAnchor(anchorLeft, anchorTop);
            }
            else
            {
                HideHandleWindow();
                _state.WindowPlacement.Left = Left;
                _state.WindowPlacement.Top = Top;
            }

            SetActiveMode(true);
        }
        else
        {
            _useHandleWindowForPinnedExpanded = false;
            HideHandleWindow();
            RestoreNormalPosition();
            SetActiveMode(IsMouseOver);
        }

        _saveState();
        _log($"Overlay mode changed: previous={previousMode}; current={mode}.");
        OverlayModeChanged?.Invoke(mode);
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
        _overlayVisible = false;
        if (_state.OverlaySettings.OverlayMode != OverlayMode.PinnedExpanded)
        {
            SetActiveMode(false, force: true);
        }

        _handleWindow?.HideSafely();
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
        CloseHandleWindow();
        _allowClose = true;
        Close();
    }

    public void CaptureWindowPlacement()
    {
        if (_isClosed || WindowState != WindowState.Normal)
        {
            return;
        }

        if (_state.OverlaySettings.OverlayMode == OverlayMode.CollapsedHandle ||
            (_state.OverlaySettings.OverlayMode == OverlayMode.PinnedExpanded &&
             _useHandleWindowForPinnedExpanded))
        {
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
            if (_state.OverlaySettings.OverlayMode != OverlayMode.PinnedExpanded)
            {
                SetActiveMode(false, force: true);
            }

            _overlayVisible = false;
            _handleWindow?.HideSafely();
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

        if (_state.OverlaySettings.OverlayMode == OverlayMode.CollapsedHandle)
        {
            SetActiveMode(false, force: true);
        }
        else if (_state.OverlaySettings.OverlayMode == OverlayMode.PinnedExpanded)
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
        CloseHandleWindow();
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
            IsPointerOverOverlay() ||
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
            _handleDragCandidate ||
            _isHandleDragging ||
            _handleWindow?.IsDragging == true ||
            !IsPointerOverOverlay())
        {
            return;
        }

        SetActiveMode(true);
    }

    private void HoverSurface_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (_isClosed ||
            _isShuttingDown ||
            _isDragging ||
            _handleDragCandidate ||
            _isHandleDragging)
        {
            return;
        }

        _passiveTimer.Stop();
        StartCollapsedExpansionOrActivate();
    }

    private void HoverSurface_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isClosed ||
            _isShuttingDown ||
            _dragCandidate ||
            _isDragging ||
            _handleDragCandidate ||
            _isHandleDragging)
        {
            return;
        }

        _collapsedExpandTimer.Stop();
        ScheduleCollapse();
    }

    private void HandleWindow_OnHoverEntered()
    {
        if (_isClosed || _isShuttingDown || _handleWindow?.IsDragging == true)
        {
            return;
        }

        _passiveTimer.Stop();
        StartCollapsedExpansionOrActivate();
    }

    private void HandleWindow_OnHoverLeft()
    {
        if (_isClosed || _isShuttingDown || _handleWindow?.IsDragging == true)
        {
            return;
        }

        _collapsedExpandTimer.Stop();
        ScheduleCollapse();
    }

    private void HandleWindow_OnDragStateChanged(bool dragging)
    {
        StopModeTimers();
        if (dragging)
        {
            SetActiveMode(false, force: true);
            return;
        }

        if (IsPointerOverOverlay())
        {
            StartCollapsedExpansionOrActivate();
        }
    }

    private void HandleWindow_OnContextInteractionChanged(bool active)
    {
        _contextInteractionActive = active;
        if (active)
        {
            StopModeTimers();
        }
        else
        {
            ScheduleCollapse();
        }
    }

    private void HandleWindow_OnModeRequested(OverlayMode mode)
    {
        SetOverlayMode(mode);
    }

    private void StartCollapsedExpansionOrActivate()
    {
        if (_state.OverlaySettings.OverlayMode == OverlayMode.CollapsedHandle &&
            !_isActiveMode)
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

        var modeChanged = _isActiveMode != active;
        _isActiveMode = active;
        if (modeChanged)
        {
            RefreshTasks();
        }

        if (_state.OverlaySettings.OverlayMode == OverlayMode.CollapsedHandle)
        {
            ApplyCollapsedSurfaceState(active);
            return;
        }

        if (_state.OverlaySettings.OverlayMode == OverlayMode.PinnedExpanded &&
            _useHandleWindowForPinnedExpanded)
        {
            ApplyPinnedHandleSurfaceState(active);
            return;
        }

        HideHandleWindow();
        CollapsedActivation.Visibility = Visibility.Visible;
        OverlayPanel.Visibility = Visibility.Visible;
        OverlayPanel.Background = active ? ActiveBackground : Brushes.Transparent;
        OverlayPanel.BorderBrush = active ? ActiveBorder : Brushes.Transparent;
        ActiveChrome.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        EnsurePanelShown();
        var anchorScreen = GetCurrentScreen();
        ConfigureExpandedLayout(anchorScreen);
        UpdateLayout();
        ConstrainToWorkArea(anchorScreen, snap: false);
        UpdateHandleVisual();
    }

    private void ApplyPinnedHandleSurfaceState(bool panelVisible)
    {
        if (_handleWindow is null || !_handleWindow.IsVisible)
        {
            RestoreCollapsedHandle();
        }

        CollapsedActivation.Visibility = Visibility.Collapsed;
        _handleWindow?.SetPanelVisible(panelVisible);

        if (!panelVisible)
        {
            OverlayPanel.Visibility = Visibility.Collapsed;
            ActiveChrome.Visibility = Visibility.Collapsed;
            Hide();
            return;
        }

        OverlayPanel.Visibility = Visibility.Visible;
        OverlayPanel.Background = ActiveBackground;
        OverlayPanel.BorderBrush = ActiveBorder;
        ActiveChrome.Visibility = Visibility.Visible;
        EnsurePanelShown();
        PositionCollapsedPanel();
        ModeStatus.Text = "PINNED";
    }

    private void ApplyCollapsedSurfaceState(bool panelVisible)
    {
        if (_handleWindow is null || !_handleWindow.IsVisible)
        {
            RestoreCollapsedHandle();
        }

        CollapsedActivation.Visibility = Visibility.Collapsed;
        _handleWindow?.SetPanelVisible(panelVisible);

        if (!panelVisible)
        {
            OverlayPanel.Visibility = Visibility.Collapsed;
            ActiveChrome.Visibility = Visibility.Collapsed;
            Hide();
            return;
        }

        OverlayPanel.Visibility = Visibility.Visible;
        OverlayPanel.Background = ActiveBackground;
        OverlayPanel.BorderBrush = ActiveBorder;
        ActiveChrome.Visibility = Visibility.Visible;
        EnsurePanelShown();
        PositionCollapsedPanel();
        ModeStatus.Text = "ACTIVE";
    }

    private void HoverSurface_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isClosed ||
            _isShuttingDown ||
            e.ChangedButton != MouseButton.Left ||
            _state.OverlaySettings.OverlayMode == OverlayMode.CollapsedHandle ||
            _useHandleWindowForPinnedExpanded ||
            IsHandleEventSource(e.OriginalSource))
        {
            return;
        }

        StopModeTimers();
        _dragCandidate = true;
        _isDragging = false;
        _dragStartCursorPixels = Forms.Cursor.Position;
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _dragFromDevice =
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ??
            Matrix.Identity;
    }

    private void HoverSurface_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragCandidate ||
            _isClosed ||
            _isShuttingDown ||
            IsHandleEventSource(e.OriginalSource))
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
        if (!_dragCandidate ||
            e.ChangedButton != MouseButton.Left ||
            IsHandleEventSource(e.OriginalSource))
        {
            return;
        }

        var dragged = _isDragging;

        if (dragged)
        {
            e.Handled = true;
            ResetDragState();

            if (HoverSurface.IsMouseCaptured)
            {
                HoverSurface.ReleaseMouseCapture();
            }

            FinishDrag();
            ScheduleTaskClickSuppressionReset();
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
            FinishDrag();
            ScheduleTaskClickSuppressionReset();
        }

        ResetDragState();
    }

    private void FinishDrag()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        ConstrainToWorkArea(screen, snap: true);

        if (_state.OverlaySettings.OverlayMode != OverlayMode.CollapsedHandle)
        {
            _state.WindowPlacement.Left = Left;
            _state.WindowPlacement.Top = Top;
        }

        _saveState();
        _log($"Overlay moved: left={Left:F1}; top={Top:F1}; mode={CurrentMode}");

        if (!IsPointerOverOverlay())
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
    }

    private void ScheduleTaskClickSuppressionReset()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => _suppressTaskClick = false));
    }

    private void ActivationHandle_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isClosed || _isShuttingDown || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        StopModeTimers();
        ResetDragState();
        _handleDragCandidate = true;
        _isHandleDragging = false;
        _dragStartCursorPixels = Forms.Cursor.Position;
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _dragFromDevice =
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ??
            Matrix.Identity;
        Mouse.Capture(CollapsedActivation, CaptureMode.Element);
        e.Handled = true;
    }

    private void ActivationHandle_OnPreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_handleDragCandidate || _isClosed || _isShuttingDown)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelHandleDrag();
            return;
        }

        var cursor = Forms.Cursor.Position;
        var pixelDelta = new Vector(
            cursor.X - _dragStartCursorPixels.X,
            cursor.Y - _dragStartCursorPixels.Y);
        var logicalDelta = _dragFromDevice.Transform(pixelDelta);

        if (!_isHandleDragging &&
            !PointerDragGesture.HasExceededThreshold(
                0,
                0,
                logicalDelta.X,
                logicalDelta.Y))
        {
            return;
        }

        _isHandleDragging = true;
        Left = _dragStartLeft + logicalDelta.X;
        Top = _dragStartTop + logicalDelta.Y;
        e.Handled = true;
    }

    private void ActivationHandle_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_handleDragCandidate || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var dragged = _isHandleDragging;
        ResetHandleDragState(releaseCapture: true);

        if (dragged)
        {
            FinishDrag();
        }
        else
        {
            ToggleModeFromHandle();
        }

        e.Handled = true;
    }

    private void ActivationHandle_OnLostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        if (_finishingHandleDrag || !_handleDragCandidate)
        {
            return;
        }

        var dragged = _isHandleDragging;
        ResetHandleDragState(releaseCapture: false);

        if (dragged)
        {
            FinishDrag();
        }
    }

    private void CancelHandleDrag()
    {
        ResetHandleDragState(releaseCapture: true);
        ScheduleCollapse();
    }

    private void ResetHandleDragState(bool releaseCapture)
    {
        _handleDragCandidate = false;
        _isHandleDragging = false;

        if (!releaseCapture || !CollapsedActivation.IsMouseCaptured)
        {
            return;
        }

        _finishingHandleDrag = true;
        try
        {
            Mouse.Capture(null);
        }
        finally
        {
            _finishingHandleDrag = false;
        }
    }

    private void ToggleModeFromHandle()
    {
        SetOverlayMode(OverlayModeCycle.Next(_state.OverlaySettings.OverlayMode));
    }

    private void HandleModeContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        _contextInteractionActive = true;
        StopModeTimers();

        if (sender is not ContextMenu menu)
        {
            return;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsChecked =
                Enum.TryParse(item.Tag?.ToString(), out OverlayMode mode) &&
                mode == _state.OverlaySettings.OverlayMode;
        }
    }

    private void HandleModeContextMenu_OnClosed(object sender, RoutedEventArgs e)
    {
        _contextInteractionActive = false;
        ScheduleCollapse();
    }

    private void HandleModeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item &&
            Enum.TryParse(item.Tag?.ToString(), out OverlayMode mode))
        {
            SetOverlayMode(mode);
        }
    }

    private bool IsHandleEventSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, CollapsedActivation))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
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
            Topmost = _state.OverlaySettings.AlwaysOnTop;
            _handleWindow?.RefreshSettings();
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

        if (_state.OverlaySettings.OverlayMode == OverlayMode.CollapsedHandle)
        {
            RestoreCollapsedHandle();
        }
    }

    private void CaptureCollapsedAnchor()
    {
        if (!_overlayVisible)
        {
            _state.WindowPlacement.CollapsedLeft = Left;
            _state.WindowPlacement.CollapsedTop = Top;
            return;
        }

        var handle = EnsureHandleWindow();
        handle.ShowAt(Left, Top, panelVisible: false);
        _state.WindowPlacement.CollapsedLeft = handle.Left;
        _state.WindowPlacement.CollapsedTop = handle.Top;
    }

    private void RestoreCollapsedHandle()
    {
        if (_state.WindowPlacement.CollapsedLeft is not double left ||
            _state.WindowPlacement.CollapsedTop is not double top)
        {
            CaptureCollapsedAnchor();
            _saveState();
            left = _state.WindowPlacement.CollapsedLeft ?? Left;
            top = _state.WindowPlacement.CollapsedTop ?? Top;
        }

        var handle = EnsureHandleWindow();
        if (_overlayVisible)
        {
            handle.ShowAt(left, top, _isActiveMode);
        }
        else
        {
            handle.HideSafely();
        }
    }

    private void RestoreNormalPosition()
    {
        if (_state.WindowPlacement.Left is not double normalLeft ||
            _state.WindowPlacement.Top is not double normalTop)
        {
            return;
        }

        Left = normalLeft;
        Top = normalTop;
        ConstrainToWorkArea(GetCurrentScreen(), snap: false);
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
        ConfigureExpandedLayout(GetWorkArea(screen));
    }

    private void ConfigureExpandedLayout(OverlayBounds workArea)
    {
        var availableWidth = Math.Max(120, workArea.Width - (WorkAreaMargin * 2));
        var availableHeight = Math.Max(80, workArea.Height - (WorkAreaMargin * 2));
        var availableContentWidth = Math.Max(80, availableWidth - 30);

        ContentStack.Width = Math.Min(420, availableContentWidth);
        OverlayPanel.MaxWidth = availableWidth;
        TasksScroller.MaxHeight = Math.Max(40, availableHeight - 80);
    }

    private void KeepCurrentModeWithinWorkArea()
    {
        if (_state.OverlaySettings.OverlayMode == OverlayMode.CollapsedHandle ||
            (_state.OverlaySettings.OverlayMode == OverlayMode.PinnedExpanded &&
             _useHandleWindowForPinnedExpanded))
        {
            if (_isActiveMode && IsVisible)
            {
                PositionCollapsedPanel();
            }

            return;
        }

        var screen = GetCurrentScreen();

        if (OverlayPanel.Visibility == Visibility.Visible)
        {
            ConfigureExpandedLayout(screen);
            UpdateLayout();
        }

        ConstrainToWorkArea(screen, snap: false);
    }

    private void PositionCollapsedPanel()
    {
        if (_handleWindow is null || !_handleWindow.IsVisible)
        {
            return;
        }

        ConfigureExpandedLayout(_handleWindow.CurrentWorkArea);
        UpdateLayout();
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var placement = PanelLayoutService.PlacePanel(
            _handleWindow.HandleBounds,
            ActualWidth,
            ActualHeight,
            _handleWindow.CurrentWorkArea);
        Left = placement.Left;
        Top = placement.Top;
    }

    private void PositionPanelFromCollapsedAnchor(double anchorLeft, double anchorTop)
    {
        if (!_overlayVisible)
        {
            Left = anchorLeft;
            Top = anchorTop;
            return;
        }

        var handle = EnsureHandleWindow();
        handle.ShowAt(anchorLeft, anchorTop, panelVisible: true);
        CollapsedActivation.Visibility = Visibility.Collapsed;
        OverlayPanel.Visibility = Visibility.Visible;
        ActiveChrome.Visibility = Visibility.Visible;
        EnsurePanelShown();
        PositionCollapsedPanel();
    }

    private HandleWindow EnsureHandleWindow()
    {
        if (_handleWindow is not null)
        {
            return _handleWindow;
        }

        _handleWindow = new HandleWindow(_state, _saveState, _log);
        _handleWindow.HoverEntered += HandleWindow_OnHoverEntered;
        _handleWindow.HoverLeft += HandleWindow_OnHoverLeft;
        _handleWindow.DragStateChanged += HandleWindow_OnDragStateChanged;
        _handleWindow.ContextInteractionChanged +=
            HandleWindow_OnContextInteractionChanged;
        _handleWindow.ModeRequested += HandleWindow_OnModeRequested;
        return _handleWindow;
    }

    private void HideHandleWindow()
    {
        _handleWindow?.HideSafely();
    }

    private void CloseHandleWindow()
    {
        if (_handleWindow is null)
        {
            return;
        }

        _handleWindow.HoverEntered -= HandleWindow_OnHoverEntered;
        _handleWindow.HoverLeft -= HandleWindow_OnHoverLeft;
        _handleWindow.DragStateChanged -= HandleWindow_OnDragStateChanged;
        _handleWindow.ContextInteractionChanged -=
            HandleWindow_OnContextInteractionChanged;
        _handleWindow.ModeRequested -= HandleWindow_OnModeRequested;
        _handleWindow.CloseForExit();
        _handleWindow = null;
    }

    private void EnsurePanelShown()
    {
        if (_overlayVisible && !IsVisible)
        {
            Show();
        }
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
                OverlayMode: _state.OverlaySettings.OverlayMode,
                TaskDetailsOpen: _taskDetailsWindow is not null,
                ContextMenuOpen: _contextInteractionActive,
                SettingsOpen: _settingsInteractionActive,
                ModalDialogOpen: _modalInteractionCount > 0,
                Dragging: _dragCandidate ||
                          _isDragging ||
                          _handleDragCandidate ||
                          _isHandleDragging ||
                          _handleWindow?.IsDragging == true));
    }

    private void ScheduleCollapse()
    {
        _passiveTimer.Stop();

        if (_isClosed ||
            _isShuttingDown ||
            !IsLoaded ||
            IsPointerOverOverlay() ||
            !CanCollapse())
        {
            return;
        }

        _passiveTimer.Start();
    }

    private bool IsPointerOverOverlay()
    {
        return IsMouseOver || _handleWindow?.IsMouseOver == true;
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
        if (_state.OverlaySettings.OverlayMode == OverlayMode.PinnedExpanded)
        {
            CollapsedActivation.Background = PinnedHandleBackground;
            CollapsedActivation.BorderBrush = PinnedHandleBorder;
            HandleIndicator.Fill = PinnedHandleForeground;
            CollapsedActivation.ToolTip =
                "Pinned expanded. Click to return to collapsed handle.";
            ModeStatus.Text = "PINNED";
            return;
        }

        if (OverlayPanel.Visibility == Visibility.Collapsed)
        {
            CollapsedActivation.Background = CollapsedHandleBackground;
            CollapsedActivation.BorderBrush = CollapsedHandleBorder;
            HandleIndicator.Fill = CollapsedHandleForeground;
            CollapsedActivation.ToolTip = "Collapsed. Click to pin expanded.";
            ModeStatus.Text = "COLLAPSED";
            return;
        }

        CollapsedActivation.Background = ExpandedHandleBackground;
        CollapsedActivation.BorderBrush = ExpandedHandleBorder;
        HandleIndicator.Fill = ExpandedHandleForeground;
        CollapsedActivation.ToolTip =
            "Expanded. Click to pin; right-click to choose overlay mode.";
        ModeStatus.Text = "ACTIVE";
    }

    private void StopModeTimers()
    {
        _passiveTimer.Stop();
        _collapsedExpandTimer.Stop();
    }
}
