using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    private UtilityShellWindow? _utilityShellWindow;
    private TreeManagerWindow? _treeManagerWindow;
    private WorkspaceWindow? _workspaceWindow;
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
    private LocalSettingsStore? _localSettingsStore;
    private LocalAppSettings? _localSettings;
    private BackupService? _backupService;
    private AppState? _state;
    private AppDiagnostics? _diagnostics;
    private SingleInstanceGuard? _singleInstanceGuard;
    private DispatcherTimer? _reminderTimer;
    private DispatcherTimer? _backupTimer;
    private readonly SemaphoreSlim _backupGate = new(1, 1);
    private BackupFolderCheckResult? _latestBackupCheck;
    private bool _stateWritesSuppressed;
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
            _localSettingsStore = new LocalSettingsStore(
                _diagnostics.StateDirectory,
                (message, exception) => _diagnostics.Log(message, exception));
            _localSettings = _localSettingsStore.Load();
            _backupService = new BackupService(
                _stateStore.StatePath,
                (message, exception) => _diagnostics.Log(message, exception));
            _state = _stateStore.Load();
            if (MvpProjectSeeder.EnsureSeedProjects(_state))
            {
                PersistState();
                _diagnostics.Log("Daily MVP projects seeded.");
            }

            RunStartupBackupFreshnessCheck();

            _overlayWindow = GetOrCreateOverlayWindow();
            _overlayWindow.Show();

            CreateTrayIcon();
            RegisterGlobalHotkeys();
            StartReminderTimer();
            StartBackupTimer();
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
            CaptureUtilityShellGeometry();
            StopOverlayAndPersist();
        }

        DisposeGlobalHotkeys();
        StopReminderTimer();
        StopBackupTimer();
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
            "Open Workspace",
            null,
            (_, _) => RunCommand("Tray", "Open Workspace", ShowWorkspace));
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
                    "Ctrl+Alt+Q - Toggle Quick Add task",
                    () => ToggleUtilityShell(AppWindowKind.QuickAdd));
                break;
            case GlobalHotkeyCommand.CycleOverlayMode:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+1 - Cycle overlay mode",
                    () => ApplyOverlayModeShortcut(OverlayModeShortcut.Cycle));
                break;
            case GlobalHotkeyCommand.OpenSettings:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+S - Toggle Settings",
                    () => ToggleUtilityShell(AppWindowKind.Settings));
                break;
            case GlobalHotkeyCommand.OpenTreeManager:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+D - Toggle Tree Manager",
                    ToggleTreeManager);
                break;
            case GlobalHotkeyCommand.ToggleWorkspace:
                RunCommand(
                    "Hotkey",
                    "Ctrl+Alt+W - Toggle Workspace",
                    ToggleWorkspace);
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
        if (_utilityShellWindow?.IsVisible == true &&
            _utilityShellWindow.ActiveTab != AppWindowKind.QuickAdd)
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
        ShowUtilityShell(AppWindowKind.QuickAdd);
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

    private void StartBackupTimer()
    {
        if (_backupTimer is not null)
        {
            return;
        }

        _backupTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _backupTimer.Tick += BackupTimer_OnTick;
        _backupTimer.Start();
        _ = RunAutomaticBackupAsync();
    }

    private async void BackupTimer_OnTick(object? sender, EventArgs e)
    {
        await RunAutomaticBackupAsync();
    }

    private async Task RunAutomaticBackupAsync()
    {
        if (_isShuttingDown ||
            _localSettings is null ||
            !BackupService.IsAutomaticBackupDue(
                _localSettings.Backups,
                DateTimeOffset.UtcNow))
        {
            return;
        }

        await RunBackupAsync(manual: false);
    }

    private void RunStartupBackupFreshnessCheck()
    {
        if (_localSettings is null ||
            _backupService is null ||
            !_localSettings.Backups.Enabled ||
            string.IsNullOrWhiteSpace(_localSettings.Backups.FolderPath))
        {
            return;
        }

        _latestBackupCheck = _backupService.CheckBackupFolder(
            _localSettings.Backups.Snapshot());
        if (_latestBackupCheck.Status != BackupFreshnessStatus.BackupNewer ||
            _latestBackupCheck.LatestBackup is not BackupCandidate candidate)
        {
            return;
        }

        if (!BackupRestorePromptWindow.ShowPrompt(_latestBackupCheck, owner: null))
        {
            _diagnostics?.Log("Startup backup restore skipped by user.");
            return;
        }

        var restore = _backupService.RestoreLatestBackup(
            _localSettings.Backups.Snapshot(),
            candidate);
        if (!restore.Succeeded)
        {
            MessageBox.Show(
                restore.Message,
                "TaskOverlay backup restore",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _diagnostics?.Log("Startup backup restored before UI initialization.");
        _state = _stateStore?.Load();
        _latestBackupCheck = _backupService.CheckBackupFolder(
            _localSettings.Backups.Snapshot());
    }

    private async Task<BackupFolderCheckResult> CheckBackupFolderAsync()
    {
        if (_localSettings is null || _backupService is null)
        {
            return new BackupFolderCheckResult(
                BackupFreshnessStatus.Failed,
                "Backup service is not available.",
                null,
                Environment.MachineName);
        }

        _latestBackupCheck = await Task.Run(() =>
            _backupService.CheckBackupFolder(_localSettings.Backups.Snapshot()));
        return _latestBackupCheck;
    }

    private async Task<RestoreResult> RestoreLatestBackupAsync()
    {
        if (_localSettings is null ||
            _backupService is null ||
            _latestBackupCheck?.LatestBackup is not BackupCandidate candidate)
        {
            return new RestoreResult(false, "No restore candidate is available.");
        }

        var result = await Task.Run(() => _backupService.RestoreLatestBackup(
            _localSettings.Backups.Snapshot(),
            candidate));
        if (result.Succeeded)
        {
            _stateWritesSuppressed = true;
            StopBackupTimer();
            StopReminderTimer();
            _diagnostics?.Log(
                "Manual backup restore succeeded; state writes are suppressed until restart.");
        }

        return result;
    }

    private async Task<BackupResult> BackupNowAsync()
    {
        _diagnostics?.Log("Backup now requested.");
        return await RunBackupAsync(manual: true);
    }

    private async Task<BackupResult> RunBackupAsync(bool manual)
    {
        if (_isShuttingDown ||
            _localSettings is null ||
            _backupService is null)
        {
            return new BackupResult(
                BackupOutcome.Failed,
                "Backup service is not available.");
        }

        if (!await _backupGate.WaitAsync(0))
        {
            return new BackupResult(
                BackupOutcome.Failed,
                "A backup is already in progress.");
        }

        try
        {
            var configuration = _localSettings.Backups.Snapshot();
            var result = await Task.Run(() => _backupService.CreateBackup(
                configuration,
                requireEnabled: !manual,
                DateTimeOffset.Now));
            _localSettings.Backups.LastBackupAttemptAtUtc = DateTimeOffset.UtcNow;

            if (result.Succeeded)
            {
                _localSettings.Backups.LastBackupAtUtc = DateTimeOffset.UtcNow;
                _localSettings.Backups.LastError = string.Empty;
            }
            else if (result.Outcome == BackupOutcome.Failed)
            {
                _localSettings.Backups.LastError = result.Message;
            }

            SaveLocalSettings();
            _utilityShellWindow?.RefreshSettings();
            return result;
        }
        catch (Exception ex)
        {
            const string message = "Backup operation failed unexpectedly.";
            _diagnostics?.Log(message, ex);
            _localSettings.Backups.LastBackupAttemptAtUtc = DateTimeOffset.UtcNow;
            _localSettings.Backups.LastError = message;
            SaveLocalSettings();
            _utilityShellWindow?.RefreshSettings();
            return new BackupResult(BackupOutcome.Failed, message);
        }
        finally
        {
            _backupGate.Release();
        }
    }

    private void StopBackupTimer()
    {
        if (_backupTimer is null)
        {
            return;
        }

        _backupTimer.Stop();
        _backupTimer.Tick -= BackupTimer_OnTick;
        _backupTimer = null;
    }

    private void ShowSettings()
    {
        ShowUtilityShell(AppWindowKind.Settings);
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
        ShowAndActivateWindow(_treeManagerWindow);
    }

    private void ShowWorkspace()
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (_workspaceWindow is null && _state is not null)
        {
            _workspaceWindow = new WorkspaceWindow(
                _state,
                PersistState,
                RefreshTaskPresentations);
            _workspaceWindow.Closed += WorkspaceWindow_OnClosed;
        }

        if (_workspaceWindow is null)
        {
            return;
        }

        ShowAndActivateWindow(_workspaceWindow);
    }

    private void ToggleTreeManager()
    {
        if (_treeManagerWindow is not null &&
            UtilityWindowTogglePolicy.Resolve(
                _treeManagerWindow.IsVisible,
                _treeManagerWindow.IsActive) == UtilityWindowToggleAction.Hide)
        {
            _treeManagerWindow.Hide();
            return;
        }

        ShowTreeManager();
    }

    private void ToggleWorkspace()
    {
        if (_workspaceWindow is not null &&
            UtilityWindowTogglePolicy.Resolve(
                _workspaceWindow.IsVisible,
                _workspaceWindow.IsActive) == UtilityWindowToggleAction.Hide)
        {
            _workspaceWindow.Hide();
            return;
        }

        ShowWorkspace();
    }

    private void WorkspaceWindow_OnClosed(object? sender, EventArgs e)
    {
        if (_workspaceWindow is null)
        {
            return;
        }

        _workspaceWindow.Closed -= WorkspaceWindow_OnClosed;
        _workspaceWindow = null;
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
            OpenTaskDetailsInShell);
        overlay.OverlayModeChanged += OverlayWindow_OnOverlayModeChanged;
        return overlay;
    }

    private UtilityShellWindow GetOrCreateUtilityShell()
    {
        if (_state is null)
        {
            throw new InvalidOperationException(
                "Cannot create the utility shell before state is loaded.");
        }

        if (_utilityShellWindow is not null)
        {
            return _utilityShellWindow;
        }

        _utilityShellWindow = new UtilityShellWindow(
            _state,
            AddQuickTask,
            SaveTaskEdits,
            DeleteTask,
            PersistState,
            RefreshTaskPresentations,
            new SettingsWindowActions(
                SetOverlayMode,
                () => OpenFolder(_diagnostics?.LogsDirectory),
                () => OpenFolder(_stateStore?.StateDirectory),
                ResetSavedWindowPositions,
                GetBackupSettings,
                SaveLocalSettings,
                ChooseBackupFolder,
                OpenConfiguredBackupFolder,
                BackupNowAsync,
                CheckBackupFolderAsync,
                RestoreLatestBackupAsync),
            active => _overlayWindow?.SetModalInteractionActive(active));
        _utilityShellWindow.ActiveTabChanged += UtilityShellWindow_OnActiveTabChanged;
        _utilityShellWindow.Closed += UtilityShellWindow_OnClosed;
        return _utilityShellWindow;
    }

    private bool ShowUtilityShell(AppWindowKind target)
    {
        if (_isShuttingDown || _state is null)
        {
            return false;
        }

        var shell = GetOrCreateUtilityShell();
        if (!shell.ShowTab(target))
        {
            return false;
        }

        ShowAndActivateWindow(shell);
        shell.FocusActiveView();
        return true;
    }

    private void ToggleUtilityShell(AppWindowKind target)
    {
        if (_isShuttingDown || _state is null)
        {
            return;
        }

        var shell = GetOrCreateUtilityShell();
        if (UtilityWindowTogglePolicy.Resolve(
                shell.IsVisible,
                shell.IsActive,
                shell.ActiveTab == target) == UtilityWindowToggleAction.Hide)
        {
            shell.PrepareToHide();
            shell.Hide();
            _overlayWindow?.SetSettingsInteractionActive(false);
            return;
        }

        ShowUtilityShell(target);
    }

    private static void ShowAndActivateWindow(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private void OpenTaskDetailsInShell(Guid taskId)
    {
        if (_state?.Tasks.FirstOrDefault(task => task.Id == taskId) is not TaskItem task)
        {
            return;
        }

        var shell = GetOrCreateUtilityShell();
        shell.SetTaskDetailsContext(task);
        ShowUtilityShell(AppWindowKind.TaskDetails);
    }

    private void UtilityShellWindow_OnClosed(object? sender, EventArgs e)
    {
        if (_utilityShellWindow is not null)
        {
            _utilityShellWindow.ActiveTabChanged -=
                UtilityShellWindow_OnActiveTabChanged;
            _utilityShellWindow.Closed -= UtilityShellWindow_OnClosed;
            _utilityShellWindow = null;
        }

        _overlayWindow?.SetSettingsInteractionActive(false);
    }

    private void UtilityShellWindow_OnActiveTabChanged(AppWindowKind tab)
    {
        _overlayWindow?.SetSettingsInteractionActive(
            tab != AppWindowKind.QuickAdd);
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

    private BackupSettings GetBackupSettings()
    {
        return _localSettings?.Backups ??
               throw new InvalidOperationException(
                   "Local settings are not available before startup completes.");
    }

    private void SaveLocalSettings()
    {
        if (_localSettingsStore is null || _localSettings is null)
        {
            return;
        }

        try
        {
            _localSettingsStore.Save(_localSettings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _diagnostics?.Log("Local settings save failed.", ex);
        }
    }

    private string? ChooseBackupFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose a local folder for TaskOverlay backups",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };
        var currentPath = _localSettings?.Backups.FolderPath;
        if (!string.IsNullOrWhiteSpace(currentPath) &&
            System.IO.Directory.Exists(currentPath))
        {
            dialog.SelectedPath = currentPath;
        }

        return dialog.ShowDialog() == Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private bool OpenConfiguredBackupFolder()
    {
        var path = _localSettings?.Backups.FolderPath;
        if (string.IsNullOrWhiteSpace(path) ||
            !System.IO.Directory.Exists(path))
        {
            _diagnostics?.Log("Backup folder is missing or unavailable.");
            return false;
        }

        try
        {
            OpenFolder(path);
            return true;
        }
        catch (Exception ex)
        {
            _diagnostics?.Log("Backup folder could not be opened.", ex);
            return false;
        }
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
        if (_state.WindowPlacement.UtilityShellPlacement is not null)
        {
            _state.WindowPlacement.UtilityShellPlacement.Left = null;
            _state.WindowPlacement.UtilityShellPlacement.Top = null;
        }
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

    private void SaveTaskEdits(TaskItem task, TaskEditValues values)
    {
        if (_state is null)
        {
            return;
        }

        TaskInteractionService.Update(_state, task, values);
        PersistState();
        RefreshTaskPresentations();
        _diagnostics?.Log(
            $"Task edited: id={task.Id}; status={task.Status}; " +
            $"projectId={task.ProjectId}; remindAt={task.RemindAtUtc:O}");
    }

    private void DeleteTask(TaskItem task)
    {
        if (_state is null || !TaskInteractionService.Delete(_state, task))
        {
            return;
        }

        PersistState();
        RefreshTaskPresentations();
        _diagnostics?.Log($"Task deleted: id={task.Id}; title={task.Title}");
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

        _utilityShellWindow?.RefreshSettings();
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
        StopBackupTimer();
        CaptureUtilityShellGeometry();
        StopOverlayAndPersist();
        DisposeGlobalHotkeys();
        DisposeTrayIcon();

        try
        {
            _utilityShellWindow?.Close();
            _utilityShellWindow = null;
            _treeManagerWindow?.Close();
            _treeManagerWindow = null;
            _workspaceWindow?.Close();
            _workspaceWindow = null;
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

    private void CaptureUtilityShellGeometry()
    {
        _utilityShellWindow?.CaptureGeometry();
    }

    private void PersistState()
    {
        PersistState(allowDuringShutdown: false);
    }

    private void PersistState(bool allowDuringShutdown)
    {
        if (_stateWritesSuppressed ||
            (!allowDuringShutdown && _isShuttingDown) ||
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
