using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TaskOverlay.Core;
using Forms = System.Windows.Forms;

namespace TaskOverlay.App;

public sealed record MeetingScreenshotCaptureResult(
    string TemporaryPngPath,
    int Width,
    int Height,
    MeetingScreenshotSourceKind SourceKind,
    string SourceLabel,
    DateTimeOffset CapturedAtUtc);

public interface IMeetingSourceInteraction
{
    string? PickAudioFile();
    string? PickTranscriptFile();
    MeetingScreenshotCaptureResult? CaptureScreenshot();
}

public sealed class WindowsMeetingSourceInteraction : IMeetingSourceInteraction
{
    public string? PickAudioFile() => PickFile(
        "Import audio",
        "Audio files (*.m4a;*.wav;*.mp3)|*.m4a;*.wav;*.mp3");

    public string? PickTranscriptFile() => PickFile(
        "Import transcript",
        "Transcript files (*.txt;*.md;*.srt;*.vtt)|*.txt;*.md;*.srt;*.vtt");

    public MeetingScreenshotCaptureResult? CaptureScreenshot()
    {
        var sources = EnumerateSources();
        if (sources.Count == 0)
        {
            throw new InvalidOperationException("No capturable display or window is available.");
        }

        var picker = new ScreenshotSourcePickerWindow(sources);
        if (picker.ShowDialog() != true || picker.SelectedSource is null)
        {
            return null;
        }

        var source = picker.SelectedSource;
        if (source.Bounds.Width <= 0 || source.Bounds.Height <= 0)
        {
            throw new InvalidDataException("The selected capture area is empty.");
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"taskoverlay-screenshot-{Guid.NewGuid():N}.png");
        using var bitmap = new Bitmap(
            source.Bounds.Width,
            source.Bounds.Height,
            PixelFormat.Format32bppArgb);
        var captured = false;
        if (source.SourceKind == MeetingScreenshotSourceKind.Window &&
            source.WindowHandle != IntPtr.Zero)
        {
            using var graphics = Graphics.FromImage(bitmap);
            var deviceContext = graphics.GetHdc();
            try
            {
                captured = PrintWindow(source.WindowHandle, deviceContext, 2);
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }
        if (!captured)
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                source.Bounds.Left,
                source.Bounds.Top,
                0,
                0,
                source.Bounds.Size,
                CopyPixelOperation.SourceCopy);
        }

        bitmap.Save(path, ImageFormat.Png);
        return new MeetingScreenshotCaptureResult(
            path,
            bitmap.Width,
            bitmap.Height,
            source.SourceKind,
            source.Label,
            DateTimeOffset.UtcNow);
    }

    private static string? PickFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static List<CaptureSource> EnumerateSources()
    {
        var sources = Forms.Screen.AllScreens
            .Select(screen => new CaptureSource(
                MeetingScreenshotSourceKind.Display,
                IntPtr.Zero,
                screen.Bounds,
                $"Display: {screen.DeviceName}{(screen.Primary ? " (Primary)" : string.Empty)}"))
            .ToList();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) <= 0 ||
                !GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width < 80 || bounds.Height < 60)
            {
                return true;
            }

            var title = new StringBuilder(GetWindowTextLength(handle) + 1);
            GetWindowText(handle, title, title.Capacity);
            var label = title.ToString().Trim();
            if (label.Length > 0)
            {
                sources.Add(new CaptureSource(
                    MeetingScreenshotSourceKind.Window,
                    handle,
                    bounds,
                    $"Window: {label}"));
            }

            return true;
        }, IntPtr.Zero);
        return sources;
    }

    private sealed record CaptureSource(
        MeetingScreenshotSourceKind SourceKind,
        IntPtr WindowHandle,
        Rectangle Bounds,
        string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class ScreenshotSourcePickerWindow : Window
    {
        private readonly ListBox _sources;

        public ScreenshotSourcePickerWindow(IReadOnlyCollection<CaptureSource> sources)
        {
            Title = "Capture screenshot";
            Width = 560;
            Height = 430;
            MinWidth = 420;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = System.Windows.Media.Brushes.Black;
            Foreground = System.Windows.Media.Brushes.White;
            var root = new DockPanel { Margin = new Thickness(16) };
            Content = root;
            _sources = new ListBox { ItemsSource = sources, SelectedIndex = 0 };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            var cancel = new Button { Content = "Cancel", MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(cancel);
            var capture = new Button { Content = "Capture", MinWidth = 96, IsDefault = true };
            capture.Click += (_, _) =>
            {
                if (_sources.SelectedItem is CaptureSource selected)
                {
                    SelectedSource = selected;
                    DialogResult = true;
                }
            };
            buttons.Children.Add(capture);
            var heading = new TextBlock
            {
                Text = "Select a display or window. TaskOverlay never captures silently.",
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);
            _sources.MouseDoubleClick += (_, _) => capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            root.Children.Add(_sources);
        }

        public CaptureSource? SelectedSource { get; private set; }
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, uint flags);
}
