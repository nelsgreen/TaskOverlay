using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    private readonly record struct OverlayLayout(
        double PanelMaxWidth,
        double ContentWidth,
        double TasksMaxHeight,
        double EmptyFontSize);

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
    private readonly Action<Guid> _openTaskDetails;
    private readonly DispatcherTimer _passiveTimer;
    private readonly DispatcherTimer _collapsedExpandTimer;

    private bool _allowClose;
    private bool _isShuttingDown;
    private bool _isClosed;
    private bool _overlayVisible = true;
    private bool _isActiveMode;
    private bool _dragCandidate;
    private bool _isDragging;
    private bool _handleDragCandidate;
    private bool _isHandleDragging;
    private bool _finishingHandleDrag;
    private bool _suppressTaskClick;
    private bool _adjustingBounds;
    private bool _handleContextInteractionActive;
    private bool _handlePanelRevealInProgress;
    private bool _settingsInteractionActive;
    private bool _workingPresentationReady = true;
    private bool _workingBoundsPrepared;
    private int _modalInteractionCount;
    private int _handlePanelRevealGeneration;
    private int _modeTransitionGeneration;
    private readonly HashSet<ContextMenu> _openContextMenus = new();
    private DrawingPoint _dragStartCursorPixels;
    private double _dragStartLeft;
    private double _dragStartTop;
    private Matrix _dragFromDevice = Matrix.Identity;
    private HandleWindow? _handleWindow;

    public event Action<OverlayMode>? OverlayModeChanged;

    public OverlayWindow(
        AppState state,
        Action saveState,
        Action<string> log,
        Action<Guid> openTaskDetails)
    {
        _state = state;
        _saveState = saveState;
        _log = log;
        _openTaskDetails = openTaskDetails;

        InitializeComponent();
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
    private bool UsesHandleWindow =>
        OverlaySurfacePolicy.UseHandleWindowForMode(
            _state.OverlaySettings.OverlayMode,
            _state.WindowPlacement.CollapsedLeft is not null &&
            _state.WindowPlacement.CollapsedTop is not null);

    public void RestoreVisibleMode()
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        ResetStaleInputState(closeContextMenus: false);
        _overlayVisible = true;
        var shouldExpand =
            _state.OverlaySettings.OverlayMode == OverlayMode.PinnedExpanded ||
            !CanCollapse();
        SetActiveMode(shouldExpand, force: !shouldExpand);
        ScheduleWorkingPointerReconciliation();
    }

    public void SetOverlayMode(OverlayMode mode)
    {
        if (mode == OverlayMode.AutoQuestTracker)
        {
            mode = OverlayMode.Working;
        }

        if (_isClosed ||
            _isShuttingDown ||
            _state.OverlaySettings.OverlayMode == mode)
        {
            return;
        }

        var previousMode = _state.OverlaySettings.OverlayMode;
        var transitionGeneration = ++_modeTransitionGeneration;
        StopModeTimers();
        ResetStaleInputState(closeContextMenus: false);
        _state.OverlaySettings.OverlayMode = mode;
        var entryPresentation = OverlayActiveStatePolicy.ForModeEntry(mode);

        if (mode == OverlayMode.CollapsedHandle)
        {
            if (_state.WindowPlacement.CollapsedLeft is null ||
                _state.WindowPlacement.CollapsedTop is null)
            {
                CaptureCollapsedAnchor();
            }
        }

        if (!UsesHandleWindow && mode == OverlayMode.PinnedExpanded)
        {
            _state.WindowPlacement.Left = Left;
            _state.WindowPlacement.Top = Top;
        }

        ApplyModeEntryPresentation(entryPresentation);
        ScheduleWorkingPointerReconciliation(transitionGeneration);

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
            var mode = _state.OverlaySettings.OverlayMode;
            var activeState = OverlayActiveStatePolicy.WhileSettingsOpen(
                mode,
                mode is OverlayMode.Working or OverlayMode.AutoQuestTracker
                    ? IsPointerInsideWorkingSurface()
                    : IsPointerOverOverlay());
            SetActiveMode(
                activeState,
                force: mode is OverlayMode.Working or OverlayMode.AutoQuestTracker);
            ScheduleWorkingPointerReconciliation();
        }
        else
        {
            if (_state.OverlaySettings.OverlayMode == OverlayMode.Working)
            {
                SetActiveMode(IsPointerInsideWorkingSurface(), force: true);
                ScheduleWorkingPointerReconciliation();
            }
            else
            {
                ScheduleCollapse();
            }
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

    public void OpenTaskDetails(Guid taskId)
    {
        if (_isClosed ||
            _isShuttingDown ||
            _state.Tasks.FirstOrDefault(task => task.Id == taskId) is not TaskItem task)
        {
            return;
        }

        _openTaskDetails(task.Id);
    }

    public void HideSafely()
    {
        if (_isClosed || _isShuttingDown)
        {
            return;
        }

        StopModeTimers();
        _overlayVisible = false;
        CancelPendingHandlePanelReveal();
        ResetStaleInputState(closeContextMenus: true);
        StopModeTimers();
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

        if (UsesHandleWindow)
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
            CancelPendingHandlePanelReveal();
            ResetStaleInputState(closeContextMenus: true);
            StopModeTimers();
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
        var transitionGeneration = ++_modeTransitionGeneration;
        ApplyModeEntryPresentation(
            OverlayActiveStatePolicy.ForModeEntry(
                _state.OverlaySettings.OverlayMode));
        ScheduleWorkingPointerReconciliation(transitionGeneration);
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
        if (_isClosed ||
            _isShuttingDown ||
            _adjustingBounds ||
            _handlePanelRevealInProgress ||
            !IsLoaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (!_isClosed &&
                    !_isShuttingDown &&
                    !_handlePanelRevealInProgress)
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
        ScheduleWorkingPointerReconciliation();
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
        ScheduleWorkingPointerReconciliation();
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
        else
        {
            ScheduleCollapse();
        }

        ScheduleWorkingPointerReconciliation();
    }

    private void HandleWindow_OnContextInteractionChanged(bool active)
    {
        _handleContextInteractionActive = active;
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

    private void SetActiveMode(
        bool active,
        bool force = false,
        bool refreshPresentation = false)
    {
        if (_isClosed || _isShuttingDown || !Dispatcher.CheckAccess())
        {
            return;
        }

        var presentation = OverlayActiveStatePolicy.Resolve(
            _state.OverlaySettings.OverlayMode,
            active);
        if (presentation.IsWorking && active && !_workingPresentationReady)
        {
            return;
        }

        if (!presentation.IsActive && !force && !CanCollapse())
        {
            UpdateHandleVisual();
            return;
        }

        var modeChanged = _isActiveMode != presentation.IsActive;
        CommitPresentationState(
            presentation,
            modeChanged || refreshPresentation || OverlayContent.Content is null);
        PreparePresentationBounds(presentation);
        ApplyPresentationSurface(presentation);
    }

    private void CommitPresentationState(
        OverlayPresentationState presentation,
        bool refreshPresentation)
    {
        _isActiveMode = presentation.IsActive;
        if (refreshPresentation)
        {
            RefreshTasks(presentation);
        }
    }

    private void ApplyPresentationSurface(OverlayPresentationState presentation)
    {
        if (UsesHandleWindow)
        {
            ApplyHandleWindowSurfaceState(presentation);
            return;
        }

        HideHandleWindow();
        CollapsedActivation.Visibility = Visibility.Visible;
        OverlayPanel.Visibility = Visibility.Visible;
        EnsurePanelShown();
        var anchorScreen = GetCurrentScreen();
        UpdateLayout();
        ConstrainToWorkArea(anchorScreen, snap: false);
        UpdateHandleVisual();
    }

    private void ApplyModeEntryPresentation(
        OverlayPresentationState entryPresentation)
    {
        _workingPresentationReady = !entryPresentation.IsWorking;
        try
        {
            // Commit the target tree before any handle, show, or positioning operation.
            CommitPresentationState(entryPresentation, refreshPresentation: true);
            if (!UsesHandleWindow)
            {
                HideHandleWindow();
                if (entryPresentation.Mode is
                    OverlayMode.Working or OverlayMode.AutoQuestTracker)
                {
                    RestoreNormalPosition();
                }
            }

            PreparePresentationBounds(entryPresentation);
            ApplyPresentationSurface(entryPresentation);
            KeepCurrentModeWithinWorkArea();
        }
        finally
        {
            _workingPresentationReady = true;
        }
    }

    private void ApplyHandleWindowSurfaceState(OverlayPresentationState presentation)
    {
        if (_handleWindow is null || !_handleWindow.IsVisible)
        {
            RestoreCollapsedHandle();
        }

        CollapsedActivation.Visibility = Visibility.Collapsed;
        var panelVisible = presentation.Mode switch
        {
            OverlayMode.CollapsedHandle => presentation.IsActive,
            OverlayMode.PinnedExpanded => presentation.IsActive,
            _ => _handleWindow?.IsDragging != true
        };
        _handleWindow?.SetPanelVisible(panelVisible);

        if (!panelVisible)
        {
            CancelPendingHandlePanelReveal();
            OverlayPanel.Visibility = Visibility.Collapsed;
            if (OverlaySurfacePolicy.KeepHostVisibleWhenPanelHidden(presentation))
            {
                EnsurePanelShown();
                UpdateLayout();
            }
            else
            {
                Hide();
            }

            return;
        }

        OverlayPanel.Visibility = Visibility.Visible;
        try
        {
            ShowPositionedHandlePanel();
        }
        finally
        {
            CompletePreparedWorkingBounds();
        }
    }

    private void PreparePresentationBounds(
        OverlayPresentationState presentation)
    {
        if (presentation.VisualBranch != OverlayVisualBranch.Working ||
            !UsesHandleWindow)
        {
            RestoreAutomaticWindowSizing();
            return;
        }

        if (_handleWindow is null || !_handleWindow.IsVisible)
        {
            RestoreCollapsedHandle();
        }

        if (_handleWindow is null)
        {
            return;
        }

        var workArea = _handleWindow.CurrentWorkArea;
        ConfigureExpandedLayout(workArea);
        if (OverlayContent.Content is not WorkingOverlayViewState viewState)
        {
            return;
        }

        OverlayContent.ApplyTemplate();
        OverlayContent.InvalidateMeasure();
        OverlayContent.Measure(
            new Size(viewState.ContentWidth, double.PositiveInfinity));
        var bounds = OverlayPanelBoundsPolicy.PlaceWorkingPanel(
            _handleWindow.HandleBounds,
            viewState.ContentWidth,
            OverlayContent.DesiredSize.Height,
            viewState.PanelMaxWidth,
            workArea);

        _adjustingBounds = true;
        try
        {
            SizeToContent = System.Windows.SizeToContent.Manual;
            Width = bounds.Width;
            Height = bounds.Height;
            Left = bounds.Left;
            Top = bounds.Top;
            _workingBoundsPrepared = true;
            UpdateLayout();
        }
        finally
        {
            _adjustingBounds = false;
        }
    }

    private void CompletePreparedWorkingBounds()
    {
        if (!_workingBoundsPrepared)
        {
            return;
        }

        RestoreAutomaticWindowSizing();
        UpdateLayout();
    }

    private void RestoreAutomaticWindowSizing()
    {
        if (!_workingBoundsPrepared &&
            SizeToContent == System.Windows.SizeToContent.WidthAndHeight)
        {
            return;
        }

        _workingBoundsPrepared = false;
        SizeToContent = System.Windows.SizeToContent.WidthAndHeight;
        ClearValue(WidthProperty);
        ClearValue(HeightProperty);
    }

    private void HoverSurface_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isClosed ||
            _isShuttingDown ||
            e.ChangedButton != MouseButton.Left ||
            UsesHandleWindow ||
            IsHandleEventSource(e.OriginalSource) ||
            IsInteractiveEventSource(e.OriginalSource))
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

        ScheduleWorkingPointerReconciliation();
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
        TrackContextMenu(sender, isOpen: true);
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
        TrackContextMenu(sender, isOpen: false);
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

    private static bool IsInteractiveEventSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void TaskBody_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetTask(sender, out var task))
        {
            return;
        }

        OpenTaskDetails(task);
        e.Handled = true;
    }

    private void TaskRow_OnMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.Handled ||
            e.ChangedButton != MouseButton.Left ||
            IsInteractiveEventSource(e.OriginalSource) ||
            !TryGetTask(sender, out var task))
        {
            return;
        }

        OpenTaskDetails(task);
        e.Handled = true;
    }

    private void OverlayFilter_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button
            {
                DataContext: OverlayFilterOptionViewModel option
            } ||
            _state.OverlaySettings.PanelFilter == option.Filter)
        {
            return;
        }

        _state.OverlaySettings.PanelFilter = option.Filter;
        if (option.Filter is OverlayPanelFilter.Wait or OverlayPanelFilter.Remind)
        {
            _state.OverlaySettings.WaitGroupExpanded = true;
        }

        StopModeTimers();
        SetActiveMode(true, refreshPresentation: true);
        _saveState();
        _log($"Overlay panel filter changed: filter={option.Filter}.");
        e.Handled = true;
    }

    private void WaitGroupHeader_OnClick(object sender, RoutedEventArgs e)
    {
        var current = _state.OverlaySettings.WaitGroupExpanded ??
                      _state.OverlaySettings.PanelFilter == OverlayPanelFilter.Wait;
        _state.OverlaySettings.WaitGroupExpanded = !current;
        RefreshTasks();
        _saveState();
        e.Handled = true;
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

        PopulateTaskContextMenu(contextMenu, row);
    }

    private void PopulateTaskContextMenu(
        ContextMenu contextMenu,
        TaskRowViewModel row)
    {
        contextMenu.Items.Clear();
        contextMenu.DataContext = row;
        contextMenu.Items.Add(CreateTaskMenuItem(
            "Open details",
            EditTaskMenuItem_OnClick,
            row));

        var descriptionItem = CreateTaskMenuItem(
            row.Task.DescriptionExpanded ? "Hide description" : "Show description",
            ToggleDescriptionMenuItem_OnClick,
            row);
        descriptionItem.IsEnabled = !string.IsNullOrWhiteSpace(row.Task.Description);
        contextMenu.Items.Add(descriptionItem);

        var statusMenu = new MenuItem { Header = "Change status", DataContext = row };
        foreach (var option in TaskAttentionUiOptions.EditableStatuses)
        {
            statusMenu.Items.Add(CreateTaskMenuItem(
                option.Label,
                SetStatusMenuItem_OnClick,
                row,
                option.Value,
                isChecked: row.Task.Status == option.Value));
        }

        contextMenu.Items.Add(statusMenu);

        var projectMenu = new MenuItem { Header = "Change project", DataContext = row };
        foreach (var project in OrderedProjects())
        {
            projectMenu.Items.Add(CreateTaskMenuItem(
                project.Name,
                ChangeProjectMenuItem_OnClick,
                row,
                project.Id,
                isChecked: row.Task.ProjectId == project.Id));
        }

        contextMenu.Items.Add(projectMenu);

        var reminderMenu = new MenuItem { Header = "Reminder", DataContext = row };
        AddReminderPresetMenuItem(reminderMenu, row, "In 30m", ReminderPreset.In30Minutes);
        AddReminderPresetMenuItem(reminderMenu, row, "In 1h", ReminderPreset.In1Hour);
        AddReminderPresetMenuItem(reminderMenu, row, "In 2h", ReminderPreset.In2Hours);
        AddReminderPresetMenuItem(reminderMenu, row, "Tomorrow morning", ReminderPreset.TomorrowMorning);
        reminderMenu.Items.Add(new Separator());
        AddReminderPresetMenuItem(reminderMenu, row, "Repeat every 2h", ReminderPreset.RepeatEvery2Hours);
        AddReminderPresetMenuItem(reminderMenu, row, "Repeat daily", ReminderPreset.RepeatDaily);
        reminderMenu.Items.Add(new Separator());
        reminderMenu.Items.Add(CreateTaskMenuItem(
            "Clear reminder",
            ClearReminderMenuItem_OnClick,
            row));
        contextMenu.Items.Add(reminderMenu);

        if (row.IsReminderDue)
        {
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(CreateTaskMenuItem(
                "Focus",
                FocusReminderMenuItem_OnClick,
                row));
            contextMenu.Items.Add(CreateTaskMenuItem(
                "Snooze 30m",
                Snooze30MenuItem_OnClick,
                row));
            contextMenu.Items.Add(CreateTaskMenuItem(
                "Snooze 1h",
                Snooze1HourMenuItem_OnClick,
                row));
        }

        if (row.IsWaiting || row.IsReminderDue)
        {
            contextMenu.Items.Add(CreateTaskMenuItem(
                "Still waiting",
                StillWaitingMenuItem_OnClick,
                row));
        }

        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(CreateTaskMenuItem(
            "Mark done",
            CompleteTaskMenuItem_OnClick,
            row));
        contextMenu.Items.Add(CreateTaskMenuItem(
            "Delete",
            DeleteTaskMenuItem_OnClick,
            row));
    }

    private static MenuItem CreateTaskMenuItem(
        string header,
        RoutedEventHandler clickHandler,
        TaskRowViewModel row,
        object? tag = null,
        bool isChecked = false)
    {
        var item = new MenuItem
        {
            Header = header,
            DataContext = row,
            Tag = tag,
            IsCheckable = tag is TaskStatus || tag is Guid,
            IsChecked = isChecked
        };
        item.Click += clickHandler;
        return item;
    }

    private void AddReminderPresetMenuItem(
        ItemsControl parent,
        TaskRowViewModel row,
        string header,
        ReminderPreset preset)
    {
        parent.Items.Add(CreateTaskMenuItem(
            header,
            ReminderPresetMenuItem_OnClick,
            row,
            preset));
    }

    private IEnumerable<ProjectItem> OrderedProjects() =>
        _state.Projects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.Name)
            .ThenBy(project => project.Id);

    private void TaskContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        TrackContextMenu(sender, isOpen: true);
        StopModeTimers();
    }

    private void TaskContextMenu_OnClosed(object sender, RoutedEventArgs e)
    {
        TrackContextMenu(sender, isOpen: false);
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

    private void SetStatusMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuTask(sender, out var task) ||
            sender is not MenuItem { Tag: TaskStatus status } ||
            !TaskInteractionService.SetStatus(_state, task, status))
        {
            return;
        }

        RefreshTasks();
        _saveState();
        _log($"Task status changed: id={task.Id}; status={task.Status}.");
    }

    private void ProjectBadge_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button
            {
                DataContext: TaskRowViewModel row
            } button)
        {
            return;
        }

        var menu = new ContextMenu
        {
            DataContext = row,
            PlacementTarget = button,
            Placement = PlacementMode.Bottom
        };
        menu.Opened += TaskContextMenu_OnOpened;
        menu.Closed += TaskContextMenu_OnClosed;
        foreach (var project in OrderedProjects())
        {
            menu.Items.Add(CreateTaskMenuItem(
                project.Name,
                ChangeProjectMenuItem_OnClick,
                row,
                project.Id,
                isChecked: row.Task.ProjectId == project.Id));
        }

        button.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void ChangeProjectMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuTask(sender, out var task) ||
            sender is not MenuItem { Tag: Guid projectId } ||
            task.ProjectId == projectId ||
            !new ProjectService(_state).AssignTaskToProject(task.Id, projectId))
        {
            return;
        }

        RefreshTasks();
        _saveState();
        _log($"Task project changed: id={task.Id}; projectId={projectId}.");
    }

    private void DueBadge_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button
            {
                ContextMenu: ContextMenu contextMenu
            } button)
        {
            return;
        }

        contextMenu.PlacementTarget = button;
        contextMenu.Placement = PlacementMode.Bottom;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void ReminderPresetMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuTask(sender, out var task) ||
            sender is not MenuItem { Tag: ReminderPreset preset } ||
            !ReminderService.ApplyPreset(task, preset))
        {
            return;
        }

        CommitReminderChange(task, $"preset={preset}");
    }

    private void ClearReminderMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuTask(sender, out var task) ||
            !ReminderService.ApplyPreset(task, ReminderPreset.None))
        {
            return;
        }

        CommitReminderChange(task, "cleared");
    }

    private void CommitReminderChange(TaskItem task, string action)
    {
        RefreshTasks();
        _saveState();
        _log($"Task reminder changed: id={task.Id}; action={action}.");
    }

    private void Snooze30MenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        SnoozeTask(sender, 30);
    }

    private void Snooze1HourMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        SnoozeTask(sender, 60);
    }

    private void SnoozeTask(object sender, int minutes)
    {
        if (!TryGetMenuTask(sender, out var task) ||
            !ReminderAttentionService.SnoozeNotification(task, minutes))
        {
            return;
        }

        RefreshTasks();
        _saveState();
        _log($"Task due notification snoozed: id={task.Id}; minutes={minutes}.");
    }

    private void FocusReminderMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuTask(sender, out var task) ||
            !ReminderAttentionService.Focus(_state, task))
        {
            return;
        }

        RefreshTasks();
        _saveState();
        _log($"Task reminder notification focused: id={task.Id}.");
    }

    private void StillWaitingMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetMenuTask(sender, out var task) ||
            !ReminderService.MarkStillWaiting(task))
        {
            return;
        }

        RefreshTasks();
        _saveState();
        _log($"Task remains waiting: id={task.Id}; nextReminder={task.RemindAtUtc:O}.");
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
        StopModeTimers();
        SetActiveMode(true);
        _openTaskDetails(task.Id);
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
            KeepCurrentModeWithinWorkArea();
            ScheduleWorkingPointerReconciliation();
        }
    }

    private void RefreshTasks()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshTasks));
            return;
        }

        RefreshTasks(
            OverlayActiveStatePolicy.Resolve(
                _state.OverlaySettings.OverlayMode,
                _isActiveMode));
    }

    private void RefreshTasks(OverlayPresentationState presentation)
    {
        RefreshTasks(presentation, ResolvePresentationWorkArea());
    }

    private void RefreshTasks(
        OverlayPresentationState presentation,
        OverlayBounds workArea)
    {
        var now = DateTimeOffset.UtcNow;
        var orderedTasks = ReminderAttentionService
            .OrderForOverlay(_state.Tasks, now)
            .ToList();
        var selectedTasks = presentation.IsWorking
            ? OverlayTaskFilter.SelectWorking(orderedTasks, now)
            : OverlayTaskFilter.SelectForPanel(
                orderedTasks,
                _state.OverlaySettings.PanelFilter,
                now);
        var taskRows = presentation.VisualBranch == OverlayVisualBranch.Collapsed
            ? Array.Empty<TaskRowViewModel>()
            : selectedTasks
                .Select(task => new TaskRowViewModel(
                    _state,
                    task,
                    presentation,
                    now))
                .ToArray();
        var rows = taskRows.Cast<object>().ToArray();
        var projectGroups = BuildProjectGroups(
                taskRows.Where(row => !row.IsWaiting))
            .Cast<object>()
            .ToArray();
        var waitProjectGroups = BuildProjectGroups(
                taskRows.Where(row => row.IsWaiting))
            .Cast<object>()
            .ToArray();
        var filterOptions = Enum.GetValues<OverlayPanelFilter>()
            .Select(filter => new OverlayFilterOptionViewModel(
                filter,
                GetPanelFilterLabel(filter),
                OverlayTaskFilter.SelectForPanel(orderedTasks, filter, now).Count(),
                _state.OverlaySettings.PanelFilter == filter))
            .Cast<object>()
            .ToArray();
        var waitGroupExpanded = _state.OverlaySettings.WaitGroupExpanded ??
                                _state.OverlaySettings.PanelFilter is
                                    OverlayPanelFilter.Wait or
                                    OverlayPanelFilter.Remind;
        var layout = CalculateOverlayLayout(presentation, workArea);
        var panelBackground = presentation.IsActive
            ? ActiveBackground
            : Brushes.Transparent;
        var panelBorder = presentation.IsActive
            ? ActiveBorder
            : Brushes.Transparent;
        var modeStatus = presentation.Mode switch
        {
            OverlayMode.PinnedExpanded => "PINNED",
            OverlayMode.Working => "WORKING",
            _ => "ACTIVE"
        };

        // A single Content replacement swaps the complete Collapsed/Working/Expanded tree.
        OverlayContent.Content = presentation.VisualBranch switch
        {
            OverlayVisualBranch.Collapsed => new CollapsedOverlayViewState(
                presentation,
                panelBackground,
                panelBorder,
                layout.PanelMaxWidth,
                layout.ContentWidth,
                layout.TasksMaxHeight,
                layout.EmptyFontSize,
                modeStatus),
            OverlayVisualBranch.Working => new WorkingOverlayViewState(
                presentation,
                rows,
                panelBackground,
                panelBorder,
                layout.PanelMaxWidth,
                layout.ContentWidth,
                layout.TasksMaxHeight,
                layout.EmptyFontSize,
                modeStatus),
            _ => new ExpandedOverlayViewState(
                presentation,
                rows,
                panelBackground,
                panelBorder,
                layout.PanelMaxWidth,
                layout.ContentWidth,
                layout.TasksMaxHeight,
                layout.EmptyFontSize,
                modeStatus,
                projectGroups,
                waitProjectGroups,
                filterOptions,
                _state.OverlaySettings.PanelFilter,
                waitGroupExpanded)
        };
    }

    private static IReadOnlyList<OverlayProjectGroupViewModel> BuildProjectGroups(
        IEnumerable<TaskRowViewModel> rows)
    {
        return rows
            .GroupBy(row => row.ProjectId)
            .Select(group =>
            {
                var first = group.First();
                return new OverlayProjectGroupViewModel(
                    first.ProjectId,
                    first.ProjectName,
                    first.ProjectColorHex,
                    group.ToArray());
            })
            .ToArray();
    }

    private static string GetPanelFilterLabel(OverlayPanelFilter filter) =>
        filter switch
        {
            OverlayPanelFilter.Focus => "FOCUS",
            OverlayPanelFilter.Wait => "WAIT",
            OverlayPanelFilter.Remind => "REMIND",
            OverlayPanelFilter.Todo => "TODO",
            _ => "Panel"
        };

    private OverlayBounds ResolvePresentationWorkArea()
    {
        if (_handleWindow is not null)
        {
            return _handleWindow.CurrentWorkArea;
        }

        if (UsesHandleWindow &&
            _state.WindowPlacement.CollapsedLeft is double left &&
            _state.WindowPlacement.CollapsedTop is double top)
        {
            var screen = Forms.Screen.FromPoint(
                new DrawingPoint((int)Math.Round(left), (int)Math.Round(top)));
            return GetWorkArea(screen);
        }

        return GetWorkArea(GetCurrentScreen());
    }

    private void RestoreWindowPlacement()
    {
        if (UsesHandleWindow)
        {
            RestoreCollapsedHandle();
            return;
        }

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
        var presentation = OverlayActiveStatePolicy.Resolve(
            _state.OverlaySettings.OverlayMode,
            _isActiveMode);
        var layout = CalculateOverlayLayout(presentation, workArea);
        if (OverlayContent.Content is OverlayViewState current &&
            current.Presentation == presentation &&
            current.PanelMaxWidth == layout.PanelMaxWidth &&
            current.ContentWidth == layout.ContentWidth &&
            current.TasksMaxHeight == layout.TasksMaxHeight &&
            current.EmptyFontSize == layout.EmptyFontSize)
        {
            return;
        }

        RefreshTasks(presentation, workArea);
    }

    private OverlayLayout CalculateOverlayLayout(
        OverlayPresentationState presentation,
        OverlayBounds workArea)
    {
        var metrics = OverlayPanelBoundsPolicy.ResolveLayout(
            presentation,
            _state.OverlaySettings,
            workArea,
            WorkAreaMargin);
        var emptyFontSize = presentation.UseCompactLayout
            ? Math.Max(
                11,
                OverlayTaskPresentationPolicy.GetWorkingFontSize(
                    _state.OverlaySettings,
                    presentation.IsActive) - 3)
            : 13;

        return new OverlayLayout(
            metrics.PanelMaxWidth,
            metrics.ContentWidth,
            metrics.TasksMaxHeight,
            emptyFontSize);
    }

    private void KeepCurrentModeWithinWorkArea()
    {
        if (UsesHandleWindow)
        {
            if (IsVisible && OverlayPanel.Visibility == Visibility.Visible)
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

    private bool PositionCollapsedPanel(bool measureBeforeReveal = false)
    {
        if (_handleWindow is null || !_handleWindow.IsVisible)
        {
            return false;
        }

        var workArea = _handleWindow.CurrentWorkArea;
        ConfigureExpandedLayout(workArea);

        double panelWidth;
        double panelHeight;
        if (measureBeforeReveal)
        {
            HoverSurface.Measure(new Size(workArea.Width, workArea.Height));
            panelWidth = HoverSurface.DesiredSize.Width;
            panelHeight = HoverSurface.DesiredSize.Height;
        }
        else
        {
            UpdateLayout();
            panelWidth = ActualWidth;
            panelHeight = ActualHeight;
        }

        if (panelWidth <= 0 || panelHeight <= 0)
        {
            return false;
        }

        var placement = PanelLayoutService.PlacePanel(
            _handleWindow.HandleBounds,
            panelWidth,
            panelHeight,
            workArea);

        _adjustingBounds = true;
        try
        {
            Left = placement.Left;
            Top = placement.Top;
        }
        finally
        {
            _adjustingBounds = false;
        }

        return true;
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

    private void ShowPositionedHandlePanel()
    {
        if (!_overlayVisible || _handlePanelRevealInProgress)
        {
            return;
        }

        var concealedForFirstLayout = !IsVisible;
        if (!concealedForFirstLayout)
        {
            PositionCollapsedPanel();
            return;
        }

        var previousOpacity = Opacity > 0 ? Opacity : 1;
        var revealGeneration = ++_handlePanelRevealGeneration;
        _handlePanelRevealInProgress = true;
        Opacity = 0;

        try
        {
            if (!PositionCollapsedPanel(measureBeforeReveal: true))
            {
                throw new InvalidOperationException(
                    "Could not measure the handle-owned panel before reveal.");
            }

            Show();
            UpdateLayout();
        }
        catch
        {
            _handlePanelRevealInProgress = false;
            Opacity = previousOpacity;
            throw;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                if (revealGeneration != _handlePanelRevealGeneration)
                {
                    return;
                }

                if (!_isClosed &&
                    !_isShuttingDown &&
                    _overlayVisible &&
                    IsVisible)
                {
                    Opacity = previousOpacity;
                }

                Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        if (revealGeneration == _handlePanelRevealGeneration)
                        {
                            _handlePanelRevealInProgress = false;
                        }
                    }));
            }));
    }

    private void CancelPendingHandlePanelReveal()
    {
        _handlePanelRevealGeneration++;
        _handlePanelRevealInProgress = false;
        if (Opacity <= 0)
        {
            Opacity = 1;
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
                TaskDetailsOpen: false,
                ContextMenuOpen: IsContextMenuOpen(),
                SettingsOpen: _settingsInteractionActive,
                ModalDialogOpen: _modalInteractionCount > 0,
                Dragging: IsDragInteractionActive()));
    }

    private bool IsContextMenuOpen()
    {
        _openContextMenus.RemoveWhere(menu => !menu.IsOpen);
        var contextMenuOpen =
            _openContextMenus.Count > 0 ||
            HasOpenContextMenu(this) ||
            (_handleWindow is not null && HasOpenContextMenu(_handleWindow));

        if (!contextMenuOpen)
        {
            _handleContextInteractionActive = false;
        }

        return contextMenuOpen || _handleContextInteractionActive;
    }

    private static bool HasOpenContextMenu(DependencyObject root)
    {
        if (root is FrameworkElement { ContextMenu.IsOpen: true })
        {
            return true;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            if (HasOpenContextMenu(VisualTreeHelper.GetChild(root, index)))
            {
                return true;
            }
        }

        return false;
    }

    private void TrackContextMenu(object sender, bool isOpen)
    {
        if (sender is not ContextMenu contextMenu)
        {
            return;
        }

        if (isOpen)
        {
            _openContextMenus.Add(contextMenu);
        }
        else
        {
            _openContextMenus.Remove(contextMenu);
        }
    }

    private bool IsDragInteractionActive()
    {
        if (Mouse.LeftButton == MouseButtonState.Released)
        {
            if (_dragCandidate || _isDragging)
            {
                ResetDragState();
                if (HoverSurface.IsMouseCaptured)
                {
                    HoverSurface.ReleaseMouseCapture();
                }
            }

            if (_handleDragCandidate || _isHandleDragging)
            {
                ResetHandleDragState(releaseCapture: true);
            }
        }

        return _dragCandidate ||
               _isDragging ||
               _handleDragCandidate ||
               _isHandleDragging ||
               _handleWindow?.IsDragging == true;
    }

    private void ResetStaleInputState(bool closeContextMenus)
    {
        if (closeContextMenus)
        {
            foreach (var contextMenu in _openContextMenus.ToArray())
            {
                contextMenu.IsOpen = false;
            }

            _openContextMenus.Clear();
            _handleContextInteractionActive = false;
        }
        else
        {
            IsContextMenuOpen();
        }

        IsDragInteractionActive();
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

    private bool IsPointerInsideWorkingSurface()
    {
        if (!IsVisible ||
            OverlayPanel.Visibility != Visibility.Visible ||
            HoverSurface.ActualWidth <= 0 ||
            HoverSurface.ActualHeight <= 0)
        {
            return false;
        }

        var pointer = Mouse.GetPosition(HoverSurface);
        return pointer.X >= 0 &&
               pointer.Y >= 0 &&
               pointer.X < HoverSurface.ActualWidth &&
               pointer.Y < HoverSurface.ActualHeight;
    }

    private void ScheduleWorkingPointerReconciliation(
        int? requiredTransitionGeneration = null)
    {
        if (_state.OverlaySettings.OverlayMode != OverlayMode.Working)
        {
            return;
        }

        var transitionGeneration = requiredTransitionGeneration ??
                                   _modeTransitionGeneration;

        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (_isClosed ||
                    _isShuttingDown ||
                    transitionGeneration != _modeTransitionGeneration ||
                    !_workingPresentationReady ||
                    _state.OverlaySettings.OverlayMode != OverlayMode.Working)
                {
                    return;
                }

                if (IsPointerInsideWorkingSurface())
                {
                    SetActiveMode(true, force: true);
                }
                else if (CanCollapse())
                {
                    SetActiveMode(false, force: true);
                }
            }));
    }

    public void SetModalInteractionActive(bool active)
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
                "Pinned. Click for collapsed handle.";
            return;
        }

        if (OverlayPanel.Visibility == Visibility.Collapsed)
        {
            CollapsedActivation.Background = CollapsedHandleBackground;
            CollapsedActivation.BorderBrush = CollapsedHandleBorder;
            HandleIndicator.Fill = CollapsedHandleForeground;
            CollapsedActivation.ToolTip = "Collapsed handle. Click for Working.";
            return;
        }

        CollapsedActivation.Background = ExpandedHandleBackground;
        CollapsedActivation.BorderBrush = ExpandedHandleBorder;
        HandleIndicator.Fill = ExpandedHandleForeground;
        var workingMode = _state.OverlaySettings.OverlayMode is
            OverlayMode.Working or OverlayMode.AutoQuestTracker;
        CollapsedActivation.ToolTip = workingMode
            ? "Working. Click for Pinned; right-click to choose overlay mode."
            : "Expanded. Click to pin; right-click to choose overlay mode.";
    }

    private void StopModeTimers()
    {
        _passiveTimer.Stop();
        _collapsedExpandTimer.Stop();
    }
}
