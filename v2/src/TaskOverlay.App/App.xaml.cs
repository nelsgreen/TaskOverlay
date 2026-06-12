using System;
using System.Collections.Generic;
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
    private Forms.ToolStripMenuItem? _collapsedModeMenuItem;
    private GlobalHotkeyManager? _hotkeyManager;
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
            RegisterGlobalHotkeys();
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

        DisposeGlobalHotkeys();
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
        _trayMenu.Items.Add(
            "Create tasks from clipboard lines",
            null,
            (_, _) => RunCommand(
                "Tray",
                "Create tasks from clipboard lines",
                CreateTasksFromClipboardLines));
        _trayMenu.Items.Add(
            "Create one task from clipboard",
            null,
            (_, _) => RunCommand(
                "Tray",
                "Create one task from clipboard",
                CreateOneTaskFromClipboard));
        _trayMenu.Items.Add(
            "Create one task with description",
            null,
            (_, _) => RunCommand(
                "Tray",
                "Create one task with description",
                CreateOneTaskWithDescription));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Show overlay", null, (_, _) => RunCommand("Tray", "Show overlay", ShowOverlay));
        _trayMenu.Items.Add("Hide overlay", null, (_, _) => RunCommand("Tray", "Hide overlay", HideOverlay));
        _collapsedModeMenuItem = new Forms.ToolStripMenuItem("Toggle collapsed mode")
        {
            Checked = _state?.OverlaySettings.CollapsedMode ?? false
        };
        _collapsedModeMenuItem.Click += CollapsedModeMenuItem_OnClick;
        _trayMenu.Items.Add(_collapsedModeMenuItem);
        _trayMenu.Items.Add("Settings", null, (_, _) => RunCommand("Tray", "Settings", ShowSettings));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => RunCommand("Tray", "Exit", () => BeginShutdown("Tray Exit command.")));

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "TaskOverlay v2 prototype",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += TrayIcon_OnDoubleClick;
    }

    private void RegisterGlobalHotkeys()
    {
        try
        {
            _hotkeyManager = new GlobalHotkeyManager();
            _hotkeyManager.HotkeyPressed += HotkeyManager_OnHotkeyPressed;
        }
        catch (Exception ex)
        {
            _hotkeyManager = null;
            _diagnostics?.Log(
                "Global hotkey manager initialization failed; continuing without hotkeys.",
                ex);
            return;
        }

        RegisterGlobalHotkey(
            1,
            "Ctrl+Alt+A",
            Forms.Keys.A,
            GlobalHotkeyAction.CreateTasksFromLines);
        RegisterGlobalHotkey(
            2,
            "Ctrl+Alt+S",
            Forms.Keys.S,
            GlobalHotkeyAction.CreateSingleTask);
        RegisterGlobalHotkey(
            3,
            "Ctrl+Alt+D",
            Forms.Keys.D,
            GlobalHotkeyAction.CreateTaskWithDescription);
        RegisterGlobalHotkey(
            4,
            "Ctrl+Alt+T",
            Forms.Keys.T,
            GlobalHotkeyAction.ToggleOverlay);
    }

    private void RegisterGlobalHotkey(
        int id,
        string displayName,
        Forms.Keys key,
        GlobalHotkeyAction action)
    {
        if (_hotkeyManager is null)
        {
            return;
        }

        if (_hotkeyManager.Register(id, key, action, out var error))
        {
            _diagnostics?.Log($"Global hotkey registered: {displayName}.");
        }
        else
        {
            _diagnostics?.Log(
                $"Global hotkey registration failed: {displayName}; {error}");
        }
    }

    private void HotkeyManager_OnHotkeyPressed(GlobalHotkeyAction action)
    {
        switch (action)
        {
            case GlobalHotkeyAction.CreateTasksFromLines:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+A - Create tasks from clipboard lines",
                    CreateTasksFromClipboardLines);
                break;
            case GlobalHotkeyAction.CreateSingleTask:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+S - Create one task from clipboard",
                    CreateOneTaskFromClipboard);
                break;
            case GlobalHotkeyAction.CreateTaskWithDescription:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+D - Create one task with description",
                    CreateOneTaskWithDescription);
                break;
            case GlobalHotkeyAction.ToggleOverlay:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+T - Show/hide overlay",
                    ToggleOverlay);
                break;
        }
    }

    private void RunCommand(string source, string command, Action action)
    {
        if (!Dispatcher.CheckAccess())
        {
            try
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    new Action(() => RunCommand(source, command, action)));
            }
            catch (Exception ex)
            {
                _diagnostics?.Log(
                    $"Could not dispatch {source.ToLowerInvariant()} command: {command}.",
                    ex);
            }

            return;
        }

        if (_isShuttingDown)
        {
            return;
        }

        try
        {
            _diagnostics?.Log($"{source} command: {command}.");
            action();
        }
        catch (Exception ex)
        {
            _diagnostics?.Log($"{source} command failed: {command}.", ex);
        }
    }

    private void TrayIcon_OnDoubleClick(object? sender, EventArgs e)
    {
        RunCommand("Tray", "Show overlay (double-click)", ShowOverlay);
    }

    private void CollapsedModeMenuItem_OnClick(object? sender, EventArgs e)
    {
        RunCommand("Tray", "Toggle collapsed mode", ToggleCollapsedMode);
    }

    private void CreateTasksFromClipboardLines()
    {
        CreateTasksFromClipboard(
            "clipboard lines",
            text => ClipboardTaskFactory.CreateFromLines(text));
    }

    private void CreateOneTaskFromClipboard()
    {
        CreateTasksFromClipboard(
            "single task",
            text => ToTaskList(ClipboardTaskFactory.CreateSingle(text)));
    }

    private void CreateOneTaskWithDescription()
    {
        CreateTasksFromClipboard(
            "task with description",
            text => ToTaskList(ClipboardTaskFactory.CreateWithDescription(text)));
    }

    private void CreateTasksFromClipboard(
        string mode,
        Func<string, IReadOnlyList<TaskItem>> createTasks)
    {
        if (_isShuttingDown || _state is null)
        {
            return;
        }

        if (!TryReadClipboardText(out var clipboardText))
        {
            return;
        }

        var tasks = createTasks(clipboardText);
        if (tasks.Count == 0)
        {
            _diagnostics?.Log(
                $"Clipboard creation skipped for {mode}: clipboard text was empty.");
            return;
        }

        _state.Tasks.AddRange(tasks);
        PersistState();
        ShowOverlay();
        _overlayWindow?.RevealTasks(tasks);
        _diagnostics?.Log(
            $"Clipboard creation succeeded for {mode}: created {tasks.Count} task(s).");
    }

    private bool TryReadClipboardText(out string clipboardText)
    {
        try
        {
            clipboardText = Clipboard.ContainsText(TextDataFormat.UnicodeText)
                ? Clipboard.GetText(TextDataFormat.UnicodeText)
                : string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            clipboardText = string.Empty;
            _diagnostics?.Log("Clipboard read failed; no tasks were created.", ex);
            return false;
        }
    }

    private static IReadOnlyList<TaskItem> ToTaskList(TaskItem? task)
    {
        return task is null ? Array.Empty<TaskItem>() : new[] { task };
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

    private void ToggleOverlay()
    {
        if (_overlayWindow is null ||
            _overlayWindow.IsClosed ||
            !_overlayWindow.IsVisible)
        {
            ShowOverlay();
        }
        else
        {
            HideOverlay();
        }
    }

    private void ToggleCollapsedMode()
    {
        if (_state is null)
        {
            return;
        }

        var enabled = !_state.OverlaySettings.CollapsedMode;
        _state.OverlaySettings.CollapsedMode = enabled;
        _overlayWindow?.SetCollapsedMode(enabled);

        if (_collapsedModeMenuItem is not null)
        {
            _collapsedModeMenuItem.Checked = enabled;
        }

        _settingsWindow?.UpdateFromSettings();
        PersistState();
        _diagnostics?.Log($"Collapsed mode changed: enabled={enabled}.");
    }

    private void ShowSettings()
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            if (_state is null)
            {
                return;
            }

            _settingsWindow = new SettingsWindow(
                _state,
                PersistState,
                () => _overlayWindow?.RefreshTaskPresentation());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.UpdateFromSettings();
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
        DisposeGlobalHotkeys();
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
                if (_collapsedModeMenuItem is not null)
                {
                    _collapsedModeMenuItem.Click -= CollapsedModeMenuItem_OnClick;
                    _collapsedModeMenuItem = null;
                }

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

    private void DisposeGlobalHotkeys()
    {
        if (_hotkeyManager is null)
        {
            return;
        }

        try
        {
            _hotkeyManager.HotkeyPressed -= HotkeyManager_OnHotkeyPressed;
            _hotkeyManager.Dispose();
            _diagnostics?.Log("Global hotkeys unregistered.");
        }
        catch (Exception ex)
        {
            _diagnostics?.Log("Global hotkey disposal failed.", ex);
        }
        finally
        {
            _hotkeyManager = null;
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
