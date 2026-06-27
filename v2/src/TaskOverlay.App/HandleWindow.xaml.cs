using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TaskOverlay.Core;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;

namespace TaskOverlay.App;

public partial class HandleWindow : Window
{
    private const double SnapThreshold = 16;

    private static readonly Brush CollapsedBackground =
        new SolidColorBrush(Color.FromArgb(204, 39, 42, 50));
    private static readonly Brush CollapsedBorder =
        new SolidColorBrush(Color.FromArgb(153, 255, 232, 120));
    private static readonly Brush CollapsedForeground =
        new SolidColorBrush(Color.FromRgb(255, 232, 120));
    private static readonly Brush ExpandedBackground =
        new SolidColorBrush(Color.FromArgb(220, 30, 58, 82));
    private static readonly Brush ExpandedBorder =
        new SolidColorBrush(Color.FromArgb(190, 96, 165, 250));
    private static readonly Brush ExpandedForeground =
        new SolidColorBrush(Color.FromRgb(147, 197, 253));
    private static readonly Brush PinnedBackground =
        new SolidColorBrush(Color.FromArgb(230, 24, 61, 52));
    private static readonly Brush PinnedBorder =
        new SolidColorBrush(Color.FromArgb(220, 52, 211, 153));
    private static readonly Brush PinnedForeground =
        new SolidColorBrush(Color.FromRgb(110, 231, 183));

    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly Action<string> _log;
    private bool _allowClose;
    private bool _dragCandidate;
    private bool _isDragging;
    private bool _releasingCapture;
    private DrawingPoint _dragStartCursorPixels;
    private double _dragStartLeft;
    private double _dragStartTop;
    private Matrix _dragFromDevice = Matrix.Identity;

    public HandleWindow(AppState state, Action saveState, Action<string> log)
    {
        _state = state;
        _saveState = saveState;
        _log = log;
        InitializeComponent();
        Topmost = state.OverlaySettings.AlwaysOnTop;
    }

    public event Action? HoverEntered;
    public event Action? HoverLeft;
    public event Action<bool>? DragStateChanged;
    public event Action<bool>? ContextInteractionChanged;
    public event Action<OverlayMode>? ModeRequested;

    public bool IsDragging => _isDragging;

    public OverlayBounds HandleBounds =>
        new(Left, Top, EffectiveWidth, EffectiveHeight);

    public OverlayBounds CurrentWorkArea => GetWorkArea(GetCurrentScreen());

    public void ShowAt(double left, double top, bool panelVisible)
    {
        Left = left;
        Top = top;
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        ConstrainToWorkArea(GetCurrentScreen(), snap: false);
        UpdateVisual(panelVisible);
    }

    public void SetPanelVisible(bool visible)
    {
        UpdateVisual(visible);
    }

    public void RefreshSettings()
    {
        Topmost = _state.OverlaySettings.AlwaysOnTop;
    }

    public void HideSafely()
    {
        CancelDrag();
        Hide();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideSafely();
            return;
        }

        base.OnClosing(e);
    }

    private double EffectiveWidth => ActualWidth > 0 ? ActualWidth : Width;
    private double EffectiveHeight => ActualHeight > 0 ? ActualHeight : Height;

    private void HandleSurface_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            HoverEntered?.Invoke();
        }
    }

    private void HandleSurface_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_dragCandidate && !_isDragging)
        {
            HoverLeft?.Invoke();
        }
    }

    private void HandleSurface_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _dragCandidate = true;
        _isDragging = false;
        _dragStartCursorPixels = Forms.Cursor.Position;
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _dragFromDevice =
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ??
            Matrix.Identity;
        Mouse.Capture(HandleSurface, CaptureMode.Element);
        e.Handled = true;
    }

    private void HandleSurface_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragCandidate)
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

        if (!_isDragging &&
            !PointerDragGesture.HasExceededThreshold(
                0,
                0,
                logicalDelta.X,
                logicalDelta.Y))
        {
            return;
        }

        if (!_isDragging)
        {
            _isDragging = true;
            DragStateChanged?.Invoke(true);
        }

        Left = _dragStartLeft + logicalDelta.X;
        Top = _dragStartTop + logicalDelta.Y;
        e.Handled = true;
    }

    private void HandleSurface_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_dragCandidate || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var dragged = _isDragging;
        ResetDragState(releaseCapture: true);

        if (dragged)
        {
            FinishDrag();
        }
        else
        {
            ModeRequested?.Invoke(OverlayModeCycle.Next(_state.OverlaySettings.OverlayMode));
        }

        e.Handled = true;
    }

    private void HandleSurface_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_releasingCapture || !_dragCandidate)
        {
            return;
        }

        var dragged = _isDragging;
        ResetDragState(releaseCapture: false);
        if (dragged)
        {
            FinishDrag();
        }
    }

    private void FinishDrag()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        ConstrainToWorkArea(screen, snap: true);
        _state.WindowPlacement.CollapsedLeft = Left;
        _state.WindowPlacement.CollapsedTop = Top;
        _saveState();
        _log($"Collapsed handle moved: left={Left:F1}; top={Top:F1}.");
        DragStateChanged?.Invoke(false);
    }

    private void CancelDrag()
    {
        var wasDragging = _isDragging;
        ResetDragState(releaseCapture: true);
        if (wasDragging)
        {
            FinishDrag();
        }
    }

    private void ResetDragState(bool releaseCapture)
    {
        _dragCandidate = false;
        _isDragging = false;

        if (!releaseCapture || !HandleSurface.IsMouseCaptured)
        {
            return;
        }

        _releasingCapture = true;
        try
        {
            Mouse.Capture(null);
        }
        finally
        {
            _releasingCapture = false;
        }
    }

    private void ModeContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        ContextInteractionChanged?.Invoke(true);
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

    private void ModeContextMenu_OnClosed(object sender, RoutedEventArgs e)
    {
        ContextInteractionChanged?.Invoke(false);
    }

    private void ModeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item &&
            Enum.TryParse(item.Tag?.ToString(), out OverlayMode mode))
        {
            ModeRequested?.Invoke(mode);
        }
    }

    private void ConstrainToWorkArea(Forms.Screen screen, bool snap)
    {
        var bounds = HandleBounds;
        var workArea = GetWorkArea(screen);
        var corrected = snap
            ? WindowPlacementGeometry.SnapToWorkArea(bounds, workArea, SnapThreshold)
            : WindowPlacementGeometry.ClampToWorkArea(bounds, workArea);
        Left = corrected.Left;
        Top = corrected.Top;
    }

    private Forms.Screen GetCurrentScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        return handle != IntPtr.Zero
            ? Forms.Screen.FromHandle(handle)
            : Forms.Screen.FromPoint(Forms.Cursor.Position);
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

    private void UpdateVisual(bool panelVisible)
    {
        if (_state.OverlaySettings.OverlayMode == OverlayMode.PinnedExpanded)
        {
            HandleSurface.Background = PinnedBackground;
            HandleSurface.BorderBrush = PinnedBorder;
            HandleIndicator.Fill = PinnedForeground;
            HandleSurface.ToolTip =
                "Pinned expanded. Click to return to Working.";
            return;
        }

        if (_state.OverlaySettings.OverlayMode is
            OverlayMode.Working or OverlayMode.AutoQuestTracker)
        {
            HandleSurface.Background = ExpandedBackground;
            HandleSurface.BorderBrush = ExpandedBorder;
            HandleIndicator.Fill = ExpandedForeground;
            HandleSurface.ToolTip =
                "Working. Click for collapsed handle; right-click to choose overlay mode.";
            return;
        }

        HandleSurface.Background = panelVisible ? ExpandedBackground : CollapsedBackground;
        HandleSurface.BorderBrush = panelVisible ? ExpandedBorder : CollapsedBorder;
        HandleIndicator.Fill = panelVisible ? ExpandedForeground : CollapsedForeground;
        HandleSurface.ToolTip = panelVisible
            ? "Expanded. Click to pin; right-click to choose overlay mode."
            : "Collapsed. Click to pin expanded.";
    }
}
