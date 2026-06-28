using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TaskOverlay.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TaskOverlay.App;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlayWindow;
    private SettingsWindow? _settingsWindow;
    private TreeManagerWindow? _treeManagerWindow;
    private QuickAddWindow? _quickAddWindow;
    private DueAttentionWindow? _dueAttentionWindow;
    private Forms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _trayApplicationIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _overlayModeMenuItem;
    private Forms.ToolStripMenuItem? _workingModeMenuItem;
    private Forms.ToolStripMenuItem? _collapsedHandleMenuItem;
    private Forms.ToolStripMenuItem? _pinnedExpandedMenuItem;
    private GlobalHotkeyManager? _hotkeyManager;
    private AppStateStore? _stateStore;
    private AppState? _state;
    private AppDiagnostics? _diagnostics;
    private WindowNavigationActions? _windowNavigation;
    private SingleInstanceGuard? _singleInstanceGuard;
    private DispatcherTimer? _reminderTimer;
    private volatile bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceGuard = SingleInstanceGuard.TryAcquire();
        if (_singleInstanceGuard is null)
        {
            Shutdown();
            return;
        }

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
            if (MvpProjectSeeder.EnsureSeedProjects(_state))
            {
                PersistState();
                _diagnostics.Log("Daily MVP projects seeded.");
            }

            _windowNavigation = new WindowNavigationActions(
                SwitchUtilityWindow,
                CanShowUtilityWindow,
                PrepareForTaskDetailsOpen);
            _overlayWindow = GetOrCreateOverlayWindow();
            _overlayWindow.Show();

            CreateTrayIcon();
            RegisterGlobalHotkeys();
            StartReminderTimer();
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
            CaptureUtilityWindowSizes();
            StopOverlayAndPersist();
        }

        DisposeGlobalHotkeys();
        StopReminderTimer();
        try
        {
            _dueAttentionWindow?.CloseForExit();
            _dueAttentionWindow = null;
        }
        catch (Exception ex)
        {
            _diagnostics?.Log("Due attention window shutdown failed.", ex);
        }

        DisposeTrayIcon();
        UnregisterExceptionHandlers();
        _diagnostics?.Log("Application shutdown completed.");
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
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
            "Quick Add task",
            null,
            (_, _) => RunCommand("Tray", "Quick Add task", ShowQuickAdd));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
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
        _overlayModeMenuItem = new Forms.ToolStripMenuItem("Overlay mode");
        _workingModeMenuItem = CreateOverlayModeMenuItem(
            "Working",
            OverlayMode.Working);
        _pinnedExpandedMenuItem = CreateOverlayModeMenuItem(
            "Pinned",
            OverlayMode.PinnedExpanded);
        _collapsedHandleMenuItem = CreateOverlayModeMenuItem(
            "Collapsed handle",
            OverlayMode.CollapsedHandle);
        _overlayModeMenuItem.DropDownItems.AddRange(
            new Forms.ToolStripItem[]
            {
                _workingModeMenuItem,
                _pinnedExpandedMenuItem,
                _collapsedHandleMenuItem
            });
        _trayMenu.Items.Add(_overlayModeMenuItem);
        UpdateOverlayModeUi(_state?.OverlaySettings.OverlayMode ??
                            OverlayMode.Working);
        _trayMenu.Items.Add(
            "Open Tree Manager",
            null,
            (_, _) => RunCommand("Tray", "Open Tree Manager", ShowTreeManager));
        _trayMenu.Items.Add("Settings", null, (_, _) => RunCommand("Tray", "Settings", ShowSettings));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => RunCommand("Tray", "Exit", () => BeginShutdown("Tray Exit command.")));

        _trayApplicationIcon = LoadApplicationIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "TaskOverlay v2 prototype",
            Icon = _trayApplicationIcon,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += TrayIcon_OnDoubleClick;
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        var resource = GetResourceStream(
            new Uri("Assets/app.ico", UriKind.Relative));
        if (resource is null)
        {
            throw new InvalidOperationException("Application icon resource was not found.");
        }

        using var stream = resource.Stream;
        using var icon = new Drawing.Icon(stream);
        return (Drawing.Icon)icon.Clone();
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

        foreach (var binding in GlobalHotkeyBindings.All)
        {
            RegisterGlobalHotkey(binding);
        }
    }

    private void RegisterGlobalHotkey(GlobalHotkeyBinding binding)
    {
        if (_hotkeyManager is null)
        {
            return;
        }

        if (_hotkeyManager.Register(
                binding.Id,
                binding.VirtualKey,
                binding.Command,
                out var error))
        {
            _diagnostics?.Log($"Global hotkey registered: {binding.DisplayName}.");
        }
        else
        {
            _diagnostics?.Log(
                $"Global hotkey registration failed: {binding.DisplayName}; {error}");
        }
    }

    private void HotkeyManager_OnHotkeyPressed(GlobalHotkeyCommand action)
    {
        switch (action)
        {
            case GlobalHotkeyCommand.CreateTaskWithDescription:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+A - Create one task with description",
                    CreateOneTaskWithDescription);
                break;
            case GlobalHotkeyCommand.CollapseOrToggleOverlay:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+T - Collapse or show/hide overlay",
                    () => ApplyOverlayModeShortcut(
                        OverlayModeShortcut.CollapseOrToggle));
                break;
            case GlobalHotkeyCommand.QuickAddTask:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+Q - Quick Add task",
                    ShowQuickAdd);
                break;
            case GlobalHotkeyCommand.CycleOverlayMode:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+1 - Cycle overlay mode",
                    () => ApplyOverlayModeShortcut(OverlayModeShortcut.Cycle));
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

    private Forms.ToolStripMenuItem CreateOverlayModeMenuItem(
        string text,
        OverlayMode mode)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            Tag = mode,
            CheckOnClick = false
        };
        item.Click += OverlayModeMenuItem_OnClick;
        return item;
    }

    private void OverlayModeMenuItem_OnClick(object? sender, EventArgs e)
    {
        if (sender is Forms.ToolStripMenuItem { Tag: OverlayMode mode })
        {
            RunCommand(
                "Tray",
                $"Overlay mode - {mode}",
                () => SetOverlayMode(mode));
        }
    }

    private void CreateTasksFromClipboardLines()
    {
        CreateTasksFromClipboard(
            "clipboard lines",
            (text, projectId) => ClipboardTaskFactory.CreateFromLines(
                text,
                projectId: projectId));
    }

    private void CreateOneTaskFromClipboard()
    {
        CreateTasksFromClipboard(
            "single task",
            (text, projectId) => ToTaskList(
                ClipboardTaskFactory.CreateSingle(text, projectId: projectId)));
    }

    private void CreateOneTaskWithDescription()
    {
        CreateTasksFromClipboard(
            "task with description",
            (text, projectId) => ToTaskList(
                ClipboardTaskFactory.CreateWithDescription(
                    text,
                    projectId: projectId)));
    }

    private void CreateTasksFromClipboard(
        string mode,
        Func<string, Guid?, IReadOnlyList<TaskItem>> createTasks)
    {
        if (_isShuttingDown || _state is null)
        {
            return;
        }

        if (!TryReadClipboardText(out var clipboardText))
        {
            return;
        }

        var project = TaskCaptureService.ResolvePreferredProject(_state);
        var tasks = createTasks(clipboardText, project?.Id);
        if (tasks.Count == 0)
        {
            _diagnostics?.Log(
                $"Clipboard creation skipped for {mode}: clipboard text was empty.");
            return;
        }

        _state.Tasks.AddRange(tasks);
        _state.OverlaySettings.LastSelectedProjectId = project?.Id;
        PersistState();
        _treeManagerWindow?.Refresh();
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

        _overlayWindow = GetOrCreateOverlayWindow();

        if (!_overlayWindow.IsLoaded)
        {
            _overlayWindow.Show();
        }

        _overlayWindow.RestoreVisibleMode();
        if (_settingsWindow?.IsVisible == true)
        {
            _overlayWindow.SetSettingsInteractionActive(true);
        }

        if (_overlayWindow.IsVisible)
        {
            _overlayWindow.Activate();
        }

        RefreshDueAttentionSurface();
    }

    private void HideOverlay()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _overlayWindow?.HideSafely();
        RefreshDueAttentionSurface();
    }

    private void ToggleOverlay()
    {
        if (_overlayWindow is null ||
            _overlayWindow.IsClosed ||
            !_overlayWindow.IsOverlayVisible)
        {
            ShowOverlay();
        }
        else
        {
            HideOverlay();
        }
    }

    private void ApplyOverlayModeShortcut(OverlayModeShortcut shortcut)
    {
        if (_state is null)
        {
            return;
        }

        var result = OverlayModeShortcutPolicy.Resolve(
            _state.OverlaySettings.OverlayMode,
            shortcut);
        SetOverlayMode(result.Mode);

        if (result.EnsureVisible)
        {
            ShowOverlay();
        }
        else if (result.ToggleVisibility)
        {
            ToggleOverlay();
        }
    }

    private void SetOverlayMode(OverlayMode mode)
    {
        if (mode == OverlayMode.AutoQuestTracker)
        {
            mode = OverlayMode.Working;
        }

        if (_state is null || _state.OverlaySettings.OverlayMode == mode)
        {
            return;
        }

        if (_overlayWindow is not null && !_overlayWindow.IsClosed)
        {
            _overlayWindow.SetOverlayMode(mode);
        }
        else
        {
            _state.OverlaySettings.OverlayMode = mode;
            PersistState();
            UpdateOverlayModeUi(mode);
        }
    }

    private void ShowQuickAdd()
    {
        SwitchUtilityWindow(AppWindowKind.QuickAdd);
    }

    private bool ShowQuickAddCore()
    {
        if (_isShuttingDown || _state is null)
        {
            return false;
        }

        if (_quickAddWindow is null)
        {
            _quickAddWindow = new QuickAddWindow(
                _state,
                AddQuickTask,
                PersistState,
                RequireWindowNavigation());
            _quickAddWindow.Closed += QuickAddWindow_OnClosed;
        }

        if (!_quickAddWindow.IsVisible)
        {
            _quickAddWindow.Show();
        }

        _quickAddWindow.Activate();
        _quickAddWindow.PrepareToShow();
        return true;
    }

    private bool AddQuickTask(QuickTaskValues values)
    {
        if (_isShuttingDown || _state is null)
        {
            return false;
        }

        var task = TaskCaptureService.CreateQuickTask(_state, values);
        if (task is null)
        {
            return false;
        }

        PersistState();
        _treeManagerWindow?.Refresh();
        ShowOverlay();
        _overlayWindow?.RevealTasks(new[] { task });
        _diagnostics?.Log(
            $"Quick Add task created: id={task.Id}; status={task.Status}; projectId={task.ProjectId}.");
        return true;
    }

    private void QuickAddWindow_OnClosed(object? sender, EventArgs e)
    {
        if (_quickAddWindow is null)
        {
            return;
        }

        _quickAddWindow.Closed -= QuickAddWindow_OnClosed;
        _quickAddWindow = null;
    }

    private void StartReminderTimer()
    {
        if (_reminderTimer is not null)
        {
            return;
        }

        _reminderTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _reminderTimer.Tick += ReminderTimer_OnTick;
        _reminderTimer.Start();
        ProcessDueReminders();
    }

    private void ReminderTimer_OnTick(object? sender, EventArgs e)
    {
        ProcessDueReminders();
    }

    private void ProcessDueReminders()
    {
        if (_isShuttingDown || _state is null)
        {
            return;
        }

        var activated = ReminderService.ProcessDueReminders(_state.Tasks);
        if (activated.Count > 0)
        {
            PersistState();
            RefreshTaskPresentations();
            _diagnostics?.Log(
                $"In-app reminders activated: count={activated.Count}; " +
                $"tasks={string.Join(",", activated.Select(task => task.Id))}.");
        }
        else
        {
            RefreshDueAttentionSurface();
        }
    }

    private void StopReminderTimer()
    {
        if (_reminderTimer is null)
        {
            return;
        }

        _reminderTimer.Stop();
        _reminderTimer.Tick -= ReminderTimer_OnTick;
        _reminderTimer = null;
    }

    private void ShowSettings()
    {
        SwitchUtilityWindow(AppWindowKind.Settings);
    }

    private bool ShowSettingsCore()
    {
        if (_isShuttingDown)
        {
            return false;
        }

        if (_settingsWindow is null)
        {
            if (_state is null)
            {
                return false;
            }

            _settingsWindow = new SettingsWindow(
                _state,
                PersistState,
                RefreshTaskPresentations,
                new SettingsWindowActions(
                    SetOverlayMode,
                    () => OpenFolder(_diagnostics?.LogsDirectory),
                    () => OpenFolder(_stateStore?.StateDirectory),
                    ResetSavedWindowPositions),
                RequireWindowNavigation());
            _settingsWindow.Closed += SettingsWindow_OnClosed;
        }

        _overlayWindow?.SetSettingsInteractionActive(true);
        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        _settingsWindow.PrepareToShow();
        _settingsWindow.Activate();
        return true;
    }

    private void ShowTreeManager()
    {
        if (_isShuttingDown || _state is null)
        {
            return;
        }

        if (_treeManagerWindow is null)
        {
            _treeManagerWindow = new TreeManagerWindow(
                _state,
                PersistState,
                () =>
                {
                    _overlayWindow?.RefreshTaskPresentation();
                    RefreshDueAttentionSurface();
                });
            _treeManagerWindow.Closed += TreeManagerWindow_OnClosed;
        }

        _treeManagerWindow.Refresh();
        _treeManagerWindow.Show();
        _treeManagerWindow.Activate();
    }

    private void TreeManagerWindow_OnClosed(object? sender, EventArgs e)
    {
        if (_treeManagerWindow is null)
        {
            return;
        }

        _treeManagerWindow.Closed -= TreeManagerWindow_OnClosed;
        _treeManagerWindow = null;
    }

    private void SettingsWindow_OnClosed(object? sender, EventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= SettingsWindow_OnClosed;
            _settingsWindow = null;
        }

        _overlayWindow?.SetSettingsInteractionActive(false);
    }

    private OverlayWindow GetOrCreateOverlayWindow()
    {
        if (_state is null)
        {
            throw new InvalidOperationException("Cannot create overlay before state is loaded.");
        }

        if (_overlayWindow is { IsClosed: false })
        {
            return _overlayWindow;
        }

        var overlay = new OverlayWindow(
            _state,
            PersistOverlayState,
            message => _diagnostics?.Log(message),
            RequireWindowNavigation());
        overlay.OverlayModeChanged += OverlayWindow_OnOverlayModeChanged;
        return overlay;
    }

    private WindowNavigationActions RequireWindowNavigation()
    {
        return _windowNavigation ?? throw new InvalidOperationException(
            "Window navigation is not initialized.");
    }

    private bool CanShowUtilityWindow(AppWindowKind target)
    {
        if (_isShuttingDown || _state is null)
        {
            return false;
        }

        return target != AppWindowKind.TaskDetails ||
               _overlayWindow?.HasTaskDetailsContext == true;
    }

    private bool SwitchUtilityWindow(AppWindowKind target)
    {
        if (!CanShowUtilityWindow(target))
        {
            return false;
        }

        HideUtilityWindowsExcept(target);
        return target switch
        {
            AppWindowKind.QuickAdd => ShowQuickAddCore(),
            AppWindowKind.Settings => ShowSettingsCore(),
            AppWindowKind.TaskDetails =>
                _overlayWindow?.ShowTaskDetailsContext() == true,
            _ => false
        };
    }

    private void PrepareForTaskDetailsOpen()
    {
        HideUtilityWindowsExcept(AppWindowKind.TaskDetails);
    }

    private void HideUtilityWindowsExcept(AppWindowKind target)
    {
        if (target != AppWindowKind.QuickAdd &&
            _quickAddWindow?.IsVisible == true)
        {
            _quickAddWindow.Hide();
        }

        if (target != AppWindowKind.Settings &&
            _settingsWindow?.IsVisible == true)
        {
            _settingsWindow.HideForNavigation();
            _overlayWindow?.SetSettingsInteractionActive(false);
        }

        if (target != AppWindowKind.TaskDetails)
        {
            _overlayWindow?.HideTaskDetailsForNavigation();
        }
    }

    private void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void ResetSavedWindowPositions()
    {
        if (_state is null)
        {
            return;
        }

        _state.WindowPlacement.Left = null;
        _state.WindowPlacement.Top = null;
        _state.WindowPlacement.CollapsedLeft = null;
        _state.WindowPlacement.CollapsedTop = null;
        PersistState();
        _diagnostics?.Log("Saved overlay window positions reset from Settings.");
    }

    private void PersistOverlayState()
    {
        PersistState();
        _treeManagerWindow?.Refresh();
        RefreshDueAttentionSurface();
    }

    private void RefreshTaskPresentations()
    {
        _overlayWindow?.RefreshTaskPresentation();
        _treeManagerWindow?.Refresh();
        RefreshDueAttentionSurface();
    }

    private void RefreshDueAttentionSurface()
    {
        if (_isShuttingDown || _state is null)
        {
            return;
        }

        var shouldShow =
            _state.OverlaySettings.OverlayMode == OverlayMode.CollapsedHandle &&
            _overlayWindow?.IsOverlayVisible == true;
        var now = DateTimeOffset.UtcNow;
        var dueTasks = shouldShow
            ? ReminderAttentionService
                .OrderForOverlay(_state.Tasks, now)
                .Where(task => ReminderAttentionService.ShouldShowNotification(task, now))
                .ToList()
            : new List<TaskItem>();
        if (dueTasks.Count == 0 && _dueAttentionWindow is null)
        {
            return;
        }

        if (_dueAttentionWindow is null || _dueAttentionWindow.IsClosed)
        {
            _dueAttentionWindow = new DueAttentionWindow(
                FocusDueNotification,
                EditDueTask,
                SnoozeDueNotification,
                CompleteDueTask,
                ClearDueReminder);
        }

        _dueAttentionWindow.UpdateTasks(dueTasks);
    }

    private void FocusDueNotification(Guid taskId)
    {
        if (!TryGetTask(taskId, out var task))
        {
            return;
        }

        if (!ReminderAttentionService.Focus(_state!, task))
        {
            return;
        }

        PersistState();
        RefreshTaskPresentations();
        _diagnostics?.Log($"Due notification focused: task={taskId}.");
    }

    private void EditDueTask(Guid taskId)
    {
        _overlayWindow?.OpenTaskDetails(taskId);
    }

    private void SnoozeDueNotification(Guid taskId, int minutes)
    {
        if (TryGetTask(taskId, out var task) &&
            ReminderAttentionService.SnoozeNotification(task, minutes))
        {
            PersistState();
            RefreshTaskPresentations();
            _diagnostics?.Log(
                $"Due notification snoozed: task={taskId}; minutes={minutes}.");
        }
    }

    private void CompleteDueTask(Guid taskId)
    {
        if (TryGetTask(taskId, out var task) &&
            TaskInteractionService.Complete(task))
        {
            PersistState();
            RefreshTaskPresentations();
            _diagnostics?.Log($"Due task completed: task={taskId}.");
        }
    }

    private void ClearDueReminder(Guid taskId)
    {
        if (TryGetTask(taskId, out var task) &&
            ReminderService.ApplyPreset(task, ReminderPreset.None))
        {
            PersistState();
            RefreshTaskPresentations();
            _diagnostics?.Log($"Due reminder cleared: task={taskId}.");
        }
    }

    private bool TryGetTask(Guid taskId, out TaskItem task)
    {
        task = _state?.Tasks.FirstOrDefault(item => item.Id == taskId)!;
        return task is not null;
    }

    private void OverlayWindow_OnOverlayModeChanged(OverlayMode mode)
    {
        UpdateOverlayModeUi(mode);
        RefreshDueAttentionSurface();
    }

    private void UpdateOverlayModeUi(OverlayMode mode)
    {
        SetModeMenuCheck(_workingModeMenuItem, mode, OverlayMode.Working);
        SetModeMenuCheck(_collapsedHandleMenuItem, mode, OverlayMode.CollapsedHandle);
        SetModeMenuCheck(_pinnedExpandedMenuItem, mode, OverlayMode.PinnedExpanded);

        _settingsWindow?.UpdateFromSettings();
    }

    private static void SetModeMenuCheck(
        Forms.ToolStripMenuItem? item,
        OverlayMode current,
        OverlayMode expected)
    {
        if (item is not null)
        {
            item.Checked = current == expected;
        }
    }

    private void BeginShutdown(string reason)
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _diagnostics?.Log($"Application shutdown started. Reason: {reason}");

        StopReminderTimer();
        CaptureUtilityWindowSizes();
        StopOverlayAndPersist();
        DisposeGlobalHotkeys();
        DisposeTrayIcon();

        try
        {
            _quickAddWindow?.Close();
            _quickAddWindow = null;
            _treeManagerWindow?.Close();
            _treeManagerWindow = null;
            _settingsWindow?.Close();
            _settingsWindow = null;
            _dueAttentionWindow?.CloseForExit();
            _dueAttentionWindow = null;
            if (_overlayWindow is not null)
            {
                _overlayWindow.OverlayModeChanged -=
                    OverlayWindow_OnOverlayModeChanged;
            }

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

    private void CaptureUtilityWindowSizes()
    {
        _quickAddWindow?.CaptureWindowSize();
        _settingsWindow?.CaptureWindowSize();
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

        if (_trayApplicationIcon is not null)
        {
            try
            {
                _trayApplicationIcon.Dispose();
            }
            catch (Exception ex)
            {
                _diagnostics?.Log("Tray application icon disposal failed.", ex);
            }
            finally
            {
                _trayApplicationIcon = null;
            }
        }

        if (_trayMenu is not null)
        {
            try
            {
                foreach (var item in new[]
                {
                    _workingModeMenuItem,
                    _collapsedHandleMenuItem,
                    _pinnedExpandedMenuItem
                })
                {
                    if (item is not null)
                    {
                        item.Click -= OverlayModeMenuItem_OnClick;
                    }
                }

                _workingModeMenuItem = null;
                _collapsedHandleMenuItem = null;
                _pinnedExpandedMenuItem = null;
                _overlayModeMenuItem = null;

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
