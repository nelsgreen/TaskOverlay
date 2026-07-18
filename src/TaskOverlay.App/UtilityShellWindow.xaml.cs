using System;
using System.Linq;
using System.Windows;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class UtilityShellWindow : Window
{
    private readonly AppState _state;
    private readonly Func<QuickTaskValues, bool> _addQuickTask;
    private readonly Action<TaskItem, TaskEditValues> _saveTask;
    private readonly Action<TaskItem> _deleteTask;
    private readonly Action _saveState;
    private readonly Action _settingsChanged;
    private readonly SettingsWindowActions _settingsActions;
    private readonly Action<bool> _modalInteractionChanged;

    private QuickAddView? _quickAddView;
    private SettingsView? _settingsView;
    private TaskDetailsView? _taskDetailsView;
    private AppWindowKind _activeTab = AppWindowKind.QuickAdd;

    public event Action<AppWindowKind>? ActiveTabChanged;

    public UtilityShellWindow(
        AppState state,
        Func<QuickTaskValues, bool> addQuickTask,
        Action<TaskItem, TaskEditValues> saveTask,
        Action<TaskItem> deleteTask,
        Action saveState,
        Action settingsChanged,
        SettingsWindowActions settingsActions,
        Action<bool> modalInteractionChanged)
    {
        _state = state;
        _addQuickTask = addQuickTask;
        _saveTask = saveTask;
        _deleteTask = deleteTask;
        _saveState = saveState;
        _settingsChanged = settingsChanged;
        _settingsActions = settingsActions;
        _modalInteractionChanged = modalInteractionChanged;

        InitializeComponent();
        UtilityShellGeometryManager.Restore(this, state.WindowPlacement);
        WindowSwitcher.TabRequested += WindowSwitcher_OnTabRequested;
    }

    public AppWindowKind ActiveTab => _activeTab;

    public bool HasTaskDetailsContext =>
        _taskDetailsView is not null &&
        _state.Tasks.Any(task => task.Id == _taskDetailsView.TaskId);

    public void SetTaskDetailsContext(TaskItem task)
    {
        if (_taskDetailsView?.TaskId == task.Id)
        {
            return;
        }

        _taskDetailsView = new TaskDetailsView(
            _state,
            task,
            _saveTask,
            _deleteTask,
            _modalInteractionChanged,
            Close);
    }

    public bool ShowTab(AppWindowKind target)
    {
        if (target == AppWindowKind.TaskDetails && !HasTaskDetailsContext)
        {
            UpdateSwitcher();
            return false;
        }

        if (_activeTab == AppWindowKind.Settings && target != _activeTab)
        {
            _settingsView?.OnDeactivated();
        }

        object? content = target switch
        {
            AppWindowKind.QuickAdd =>
                _quickAddView ??= new QuickAddView(_state, _addQuickTask, Close),
            AppWindowKind.Settings =>
                _settingsView ??= new SettingsView(
                    _state,
                    _saveState,
                    _settingsChanged,
                    _settingsActions,
                    Close),
            AppWindowKind.TaskDetails => _taskDetailsView,
            _ => null
        };
        if (content is null)
        {
            return false;
        }

        _activeTab = target;
        ContentHost.Content = content;
        UpdateSwitcher();
        ActiveTabChanged?.Invoke(target);
        FocusActiveView();
        return true;
    }

    public void FocusActiveView()
    {
        switch (_activeTab)
        {
            case AppWindowKind.QuickAdd:
                _quickAddView?.OnActivated();
                break;
            case AppWindowKind.Settings:
                _settingsView?.OnActivated();
                break;
            case AppWindowKind.TaskDetails:
                _taskDetailsView?.OnActivated();
                break;
        }
    }

    public void RefreshSettings()
    {
        _settingsView?.UpdateFromSettings();
    }

    public void PrepareToHide()
    {
        if (_activeTab == AppWindowKind.Settings)
        {
            _settingsView?.OnDeactivated();
        }
    }

    public bool CaptureGeometry()
    {
        return UtilityShellGeometryManager.Capture(this, _state.WindowPlacement);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _settingsView?.OnDeactivated();
        if (CaptureGeometry())
        {
            _saveState();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        WindowSwitcher.TabRequested -= WindowSwitcher_OnTabRequested;
        base.OnClosed(e);
    }

    private void WindowSwitcher_OnTabRequested(AppWindowKind target)
    {
        ShowTab(target);
    }

    private void UpdateSwitcher()
    {
        WindowSwitcher.SetState(_activeTab, HasTaskDetailsContext);
        Title = _activeTab switch
        {
            AppWindowKind.QuickAdd => "Quick Add task",
            AppWindowKind.TaskDetails => "Task details",
            _ => "TaskOverlay Settings"
        };
    }
}
