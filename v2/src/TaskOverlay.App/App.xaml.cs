using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TaskOverlay.Core;
using Forms = System.Windows.Forms;

namespace TaskOverlay.App;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlayWindow;
    private SettingsWindow? _settingsWindow;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private AppStateStore? _stateStore;
    private AppState? _state;
    private AppDiagnostics? _diagnostics;
    private volatile bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        InitializeDiagnostics();
        RegisterExceptionHandlers();
        _diagnostics!.Log("Application startup.");

        try
        {
            base.OnStartup(e);

            _stateStore = new AppStateStore(
                _diagnostics.StateDirectory,
                (message, exception) => _diagnostics.Log(message, exception));
            _state = _stateStore.Load();

            _overlayWindow = new OverlayWindow(
                _state,
                PersistState,
                message => _diagnostics.Log(message));
            _overlayWindow.Show();

            CreateTrayIcon();
            _diagnostics.Log("Application startup completed.");
        }
        catch (Exception ex)
        {
            LogUnhandledException("Startup exception", ex);
            BeginShutdown("Startup failed.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_isShuttingDown)
        {
            _isShuttingDown = true;
            _diagnostics?.Log("Application exit started outside the tray shutdown path.");
            StopOverlayAndPersist();
        }

        DisposeTrayIcon();
        UnregisterExceptionHandlers();
        _diagnostics?.Log("Application shutdown completed.");
        base.OnExit(e);
    }

    private void InitializeDiagnostics()
    {
        var stateDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskOverlayV2");
        _diagnostics = new AppDiagnostics(stateDirectory);
    }

    private void RegisterExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_OnUnhandledException;
        DispatcherUnhandledException += App_OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_OnUnobservedTaskException;
    }

    private void UnregisterExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_OnUnhandledException;
        DispatcherUnhandledException -= App_OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_OnUnobservedTaskException;
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.Add("Show overlay", null, (_, _) => RunTrayCommand("Show overlay", ShowOverlay));
        _trayMenu.Items.Add("Hide overlay", null, (_, _) => RunTrayCommand("Hide overlay", HideOverlay));
        _trayMenu.Items.Add("Settings", null, (_, _) => RunTrayCommand("Settings", ShowSettings));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => RunTrayCommand("Exit", () => BeginShutdown("Tray Exit command.")));

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "TaskOverlay v2 prototype",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += TrayIcon_OnDoubleClick;
    }

    private void RunTrayCommand(string command, Action action)
    {
        if (!Dispatcher.CheckAccess())
        {
            try
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    new Action(() => RunTrayCommand(command, action)));
            }
            catch (Exception ex)
            {
                _diagnostics?.Log($"Could not dispatch tray command: {command}.", ex);
            }

            return;
        }

        if (_isShuttingDown)
        {
            return;
        }

        try
        {
            _diagnostics?.Log($"Tray command: {command}.");
            action();
        }
        catch (Exception ex)
        {
            _diagnostics?.Log($"Tray command failed: {command}.", ex);
        }
    }

    private void TrayIcon_OnDoubleClick(object? sender, EventArgs e)
    {
        RunTrayCommand("Show overlay (double-click)", ShowOverlay);
    }

    private void ShowOverlay()
    {
        if (_isShuttingDown || _state is null)
        {
            return;
        }

        if (_overlayWindow is null || _overlayWindow.IsClosed)
        {
            _overlayWindow = new OverlayWindow(
                _state,
                PersistState,
                message => _diagnostics?.Log(message));
        }

        _overlayWindow.Show();
        _overlayWindow.Activate();
    }

    private void HideOverlay()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _overlayWindow?.HideSafely();
    }

    private void ShowSettings()
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void BeginShutdown(string reason)
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _diagnostics?.Log($"Application shutdown started. Reason: {reason}");

        StopOverlayAndPersist();
        DisposeTrayIcon();

        try
        {
            _settingsWindow?.Close();
            _settingsWindow = null;
            _overlayWindow?.CloseForExit();
            _overlayWindow = null;
        }
        catch (Exception ex)
        {
            _diagnostics?.Log("Window shutdown failed.", ex);
        }

        Shutdown();
    }

    private void StopOverlayAndPersist()
    {
        try
        {
            _overlayWindow?.PrepareForShutdown();
        }
        catch (Exception ex)
        {
            _diagnostics?.Log("Overlay shutdown preparation failed.", ex);
        }

        PersistState(allowDuringShutdown: true);
    }

    private void PersistState()
    {
        PersistState(allowDuringShutdown: false);
    }

    private void PersistState(bool allowDuringShutdown)
    {
        if ((!allowDuringShutdown && _isShuttingDown) ||
            _stateStore is null ||
            _state is null)
        {
            return;
        }

        try
        {
            _stateStore.Save(_state);
        }
        catch (Exception ex)
        {
            _diagnostics?.Log("State save failed.", ex);
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            try
            {
                _trayIcon.DoubleClick -= TrayIcon_OnDoubleClick;
                _trayIcon.Visible = false;
                _trayIcon.ContextMenuStrip = null;
                _trayIcon.Dispose();
            }
            catch (Exception ex)
            {
                _diagnostics?.Log("Tray icon disposal failed.", ex);
            }
            finally
            {
                _trayIcon = null;
            }
        }

        if (_trayMenu is not null)
        {
            try
            {
                _trayMenu.Dispose();
            }
            catch (Exception ex)
            {
                _diagnostics?.Log("Tray menu disposal failed.", ex);
            }
            finally
            {
                _trayMenu = null;
            }
        }
    }

    private void App_OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        LogUnhandledException("DispatcherUnhandledException", e.Exception);
        e.Handled = true;

        if (!_isShuttingDown)
        {
            try
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Send,
                    new Action(() => BeginShutdown("Unhandled dispatcher exception.")));
            }
            catch (Exception dispatchException)
            {
                _diagnostics?.Log(
                    "Could not schedule shutdown after dispatcher exception.",
                    dispatchException);
            }
        }
    }

    private void CurrentDomain_OnUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ??
                        new Exception($"Unhandled non-Exception object: {e.ExceptionObject}");
        LogUnhandledException(
            $"AppDomain.CurrentDomain.UnhandledException; terminating={e.IsTerminating}",
            exception);
    }

    private void TaskScheduler_OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        LogUnhandledException("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private void LogUnhandledException(string source, Exception exception)
    {
        _diagnostics?.LogCrash(source, exception, BuildRuntimeContext());
    }

    private string BuildRuntimeContext()
    {
        var overlayMode = _overlayWindow?.CurrentMode ?? "unavailable";
        var overlayClosed = _overlayWindow?.IsClosed.ToString() ?? "unavailable";

        return
            $"ShuttingDown={_isShuttingDown}; " +
            $"OverlayMode={overlayMode}; " +
            $"OverlayClosed={overlayClosed}; " +
            $"StatePath={_stateStore?.StatePath ?? _diagnostics?.StatePath ?? "unavailable"}";
    }
}
