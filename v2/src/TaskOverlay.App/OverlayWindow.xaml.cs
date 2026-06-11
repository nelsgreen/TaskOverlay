using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace TaskOverlay.App;

public partial class OverlayWindow : Window
{
    private static readonly System.Windows.Media.Brush ActiveBackground =
        new SolidColorBrush(Color.FromArgb(232, 24, 27, 34));
    private static readonly System.Windows.Media.Brush ActiveBorder =
        new SolidColorBrush(Color.FromArgb(96, 255, 255, 255));

    private readonly DispatcherTimer _passiveTimer;
    private bool _allowClose;

    public OverlayWindow()
    {
        InitializeComponent();

        _passiveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _passiveTimer.Tick += (_, _) =>
        {
            _passiveTimer.Stop();
            SetActiveMode(false);
        };

        Loaded += (_, _) =>
        {
            PositionOnCurrentMonitor();
            SetActiveMode(false);
        };
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
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void HoverSurface_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _passiveTimer.Stop();
        SetActiveMode(true);
    }

    private void HoverSurface_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
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
