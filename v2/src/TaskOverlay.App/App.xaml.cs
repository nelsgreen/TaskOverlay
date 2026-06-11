using System;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TaskOverlay.App;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlayWindow;
    private SettingsWindow? _settingsWindow;
    private Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _overlayWindow = new OverlayWindow();
        _overlayWindow.Show();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show overlay", null, (_, _) => ShowOverlay());
        menu.Items.Add("Hide overlay", null, (_, _) => _overlayWindow?.Hide());
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "TaskOverlay v2 prototype",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowOverlay();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.OnExit(e);
    }

    private void ShowOverlay()
    {
        _overlayWindow ??= new OverlayWindow();
        _overlayWindow.Show();
        _overlayWindow.Activate();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ExitApplication()
    {
        _settingsWindow?.Close();
        _overlayWindow?.CloseForExit();
        Shutdown();
    }
}
