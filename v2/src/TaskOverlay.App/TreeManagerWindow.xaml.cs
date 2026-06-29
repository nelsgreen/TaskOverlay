using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class TreeManagerWindow : Window
{
    private static readonly Brush ActiveFilterBackground = CreateBrush("#FF3A3F45");
    private static readonly Brush InactiveFilterBackground = Brushes.Transparent;
    private static readonly Brush ActiveFilterForeground = CreateBrush("#FFF2F4F5");
    private static readonly Brush InactiveFilterForeground = CreateBrush("#FF9AA5B1");
    private static readonly Brush TodoBrush = CreateBrush("#FF9AA4B2");
    private static readonly Brush FocusBrush = CreateBrush("#FF45D58A");
    private static readonly Brush WaitBrush = CreateBrush("#FF51B5DB");
    private static readonly Brush RemindBrush = CreateBrush("#FFF1B94E");
    private static readonly Brush DoneBrush = CreateBrush("#FF737B87");

    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly Action _taskPresentationChanged;
    private readonly TreeStateService _treeStateService;
    private readonly ObservableCollection<TreeProjectViewModel> _projects = new();
    private readonly ObservableCollection<TreeNodeRowViewModel> _rows = new();
    private readonly ObservableCollection<TreeStatusRowViewModel> _statusRows = new();
    private readonly ObservableCollection<TreeActiveTaskViewModel> _activeTasks = new();
    private TreeNode? _selectedNode;
    private TreeNodeStatus _draftStatus;
    private bool _suppressSelectionEvents;
    private bool _suppressLocationEvents;

    public TreeManagerWindow(
        AppState state,
        Action saveState,
        Action taskPresentationChanged)
    {
        _state = state;
        _saveState = saveState;
        _taskPresentationChanged = taskPresentationChanged;
        _treeStateService = new TreeStateService(state);

        InitializeComponent();
        ProjectList.ItemsSource = _projects;
        TreeRows.ItemsSource = _rows;
        StatusRows.ItemsSource = _statusRows;
        ActiveNowItems.ItemsSource = _activeTasks;
        Refresh();
    }

    public void Refresh()
    {
        var stateChanged = TreeManagerStatePolicy.Normalize(_state);
        stateChanged |= InitializeExpansionState();

        _suppressSelectionEvents = true;
        try
        {
            RefreshProjects();
            RefreshTreeRows();
            RefreshStatusRows();
            RefreshActiveNow();
            LoadSelectedNode();
            UpdateFilterButtons();
            UpdateStatusFilterButtons();
            UpdateActiveView();
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        if (stateChanged)
        {
            _saveState();
        }
    }

    private bool InitializeExpansionState()
    {
        var settings = _state.TreeManagerSettings;
        if (settings.ExpansionInitialized)
        {
            return false;
        }

        settings.ExpandedNodeIds = _state.Projects
            .SelectMany(project => _treeStateService.GetProjection(
                project.Id,
                TreeProjection.AllInProject))
            .Where(node => _treeStateService.GetChildren(node.Id).Count > 0)
            .Select(node => node.Id)
            .Distinct()
            .ToList();
        settings.ExpansionInitialized = true;
        return true;
    }

    private void RefreshProjects()
    {
        var now = DateTimeOffset.UtcNow;
        var activeTasks = GetOverlayFeedingTasks(now).ToList();
        var activeCounts = activeTasks
            .Select(task => new
            {
                Task = task,
                Project = ProjectReferenceResolver.ResolveProject(_state, task)
            })
            .Where(item => item.Project is not null)
            .GroupBy(item => item.Project!.Id)
            .ToDictionary(group => group.Key, group => group.Count());

        _projects.Clear();
        foreach (var project in OrderedProjects())
        {
            _projects.Add(new TreeProjectViewModel(
                project,
                activeCounts.GetValueOrDefault(project.Id)));
        }

        var selectedProject = _projects.FirstOrDefault(project =>
            project.Id == _state.TreeManagerSettings.SelectedProjectId) ??
                              _projects.FirstOrDefault();
        ProjectList.SelectedItem = selectedProject;
        SelectedProjectLabel.Text = selectedProject?.Name ?? "No project";
        TreePathLabel.Text = selectedProject is null
            ? "No project selected"
            : $"{selectedProject.Name}  /  task tree";
        var panelCount = _state.Tasks.Count(task => task.PinToPanel);
        ProjectSummary.Text = $"{_projects.Count} projects · {activeTasks.Count} active · {panelCount} panel";
    }

    private void RefreshTreeRows()
    {
        var selectedProjectId = _state.TreeManagerSettings.SelectedProjectId;
        _rows.Clear();
        if (!selectedProjectId.HasValue)
        {
            TreeEmptyState.Visibility = Visibility.Visible;
            return;
        }

        var projection = _state.TreeManagerSettings.Filter switch
        {
            TreeManagerFilter.ActiveOnly => TreeProjection.ActiveOnly,
            TreeManagerFilter.ActivePlusAncestors => TreeProjection.ActivePlusAncestors,
            _ => TreeProjection.AllInProject
        };
        var projectedNodes = _treeStateService
            .GetProjection(selectedProjectId.Value, projection)
            .ToList();
        var includedIds = ApplySearch(projectedNodes).Select(node => node.Id).ToHashSet();
        var rows = projectedNodes
            .Where(node => node.Kind != TreeNodeKind.Project && includedIds.Contains(node.Id))
            .Where(ShouldShowExpandedNode)
            .ToList();
        var now = DateTimeOffset.UtcNow;

        foreach (var node in rows)
        {
            var ancestors = _treeStateService.GetAncestors(node.Id);
            var siblings = _treeStateService.GetChildren(node.ParentId);
            var siblingIndex = siblings.ToList().FindIndex(sibling => sibling.Id == node.Id);
            var task = node.Kind == TreeNodeKind.Task
                ? _state.Tasks.FirstOrDefault(item => item.Id == node.Id)
                : null;
            var childCount = _treeStateService.GetChildren(node.Id).Count;
            var depth = _state.TreeManagerSettings.Filter == TreeManagerFilter.ActiveOnly
                ? 0
                : Math.Max(0, ancestors.Count - 1);
            _rows.Add(new TreeNodeRowViewModel(
                node,
                depth,
                childCount,
                _state.TreeManagerSettings.ExpandedNodeIds.Contains(node.Id),
                task is not null && ReminderAttentionService.ShouldShowNotification(task, now),
                siblingIndex > 0,
                siblingIndex >= 0 && siblingIndex < siblings.Count - 1,
                task?.WaitingFor ?? string.Empty,
                task?.PinToPanel ?? false));
        }

        TreeRows.SelectedItem = _rows.FirstOrDefault(row =>
            row.Id == _state.TreeManagerSettings.SelectedNodeId);
        TreeEmptyState.Visibility = _rows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshStatusRows()
    {
        var now = DateTimeOffset.UtcNow;
        var filtered = TreeManagerTaskFilter.Select(
                _state.Tasks,
                _state.TreeManagerSettings.StatusFilter,
                now)
            .OrderByDescending(task => ReminderAttentionService.ShouldShowNotification(task, now))
            .ThenByDescending(task => task.PinToPanel)
            .ThenBy(task => task.Status == TaskStatus.Done)
            .ThenByDescending(task => task.Status == TaskStatus.InWork)
            .ThenBy(task => task.SortOrder)
            .ThenBy(task => task.CreatedAtUtc)
            .ToList();
        var query = SearchBox.Text.Trim();

        _statusRows.Clear();
        foreach (var task in filtered)
        {
            var contextPath = string.Join(
                " / ",
                _treeStateService.GetAncestors(task.Id).Select(node => node.Title));
            if (query.Length > 0 &&
                !task.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !contextPath.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !task.WaitingFor.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isReminder = ReminderAttentionService.ShouldShowNotification(task, now);
            var reminderLabel = isReminder
                ? "Reminder requires attention"
                : task.RemindAtUtc is DateTimeOffset remindAt
                    ? $"Reminder {remindAt.ToLocalTime():g}"
                    : string.Empty;
            _statusRows.Add(new TreeStatusRowViewModel(
                task,
                contextPath,
                isReminder,
                reminderLabel));
        }

        StatusRows.SelectedItem = _statusRows.FirstOrDefault(row =>
            row.Id == _state.TreeManagerSettings.SelectedNodeId);
        StatusEmptyState.Visibility = _statusRows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private IReadOnlyList<TreeNode> ApplySearch(IReadOnlyList<TreeNode> nodes)
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            return nodes;
        }

        var includedIds = new HashSet<Guid>();
        foreach (var match in nodes.Where(node =>
                     node.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            includedIds.Add(match.Id);
            includedIds.UnionWith(_treeStateService.GetAncestors(match.Id).Select(node => node.Id));
        }

        return nodes.Where(node => includedIds.Contains(node.Id)).ToList();
    }

    private bool ShouldShowExpandedNode(TreeNode node)
    {
        if (_state.TreeManagerSettings.Filter != TreeManagerFilter.All ||
            SearchBox.Text.Trim().Length > 0)
        {
            return true;
        }

        return _treeStateService.GetAncestors(node.Id)
            .Where(ancestor => ancestor.Kind != TreeNodeKind.Project)
            .All(ancestor => _state.TreeManagerSettings.ExpandedNodeIds.Contains(ancestor.Id));
    }

    private void RefreshActiveNow()
    {
        var now = DateTimeOffset.UtcNow;
        _activeTasks.Clear();
        foreach (var task in GetOverlayFeedingTasks(now))
        {
            var isReminder = ReminderAttentionService.ShouldShowNotification(task, now);
            var projectName = ProjectReferenceResolver.ResolveProject(_state, task)?.Name ??
                              ProjectItem.DefaultName;
            _activeTasks.Add(new TreeActiveTaskViewModel(
                task,
                projectName,
                isReminder ? "REMIND" : "FOCUS",
                isReminder ? RemindBrush : FocusBrush));
        }

        ActiveNowCount.Text = $"{_activeTasks.Count} items";
    }

    private IEnumerable<TaskItem> GetOverlayFeedingTasks(DateTimeOffset now)
    {
        return OverlayTaskFilter.SelectForMode(
            ReminderAttentionService.OrderForOverlay(_state.Tasks, now),
            OverlayMode.Working,
            now);
    }

    private void LoadSelectedNode()
    {
        var selectedId = _state.TreeManagerSettings.SelectedNodeId ??
                         _state.TreeManagerSettings.SelectedProjectId;
        _selectedNode = selectedId.HasValue
            ? _treeStateService.GetNode(selectedId.Value)
            : null;

        var hasSelection = _selectedNode is not null;
        DetailsTitleInput.IsEnabled = hasSelection;
        DetailsTitleInput.Text = _selectedNode?.Title ?? string.Empty;
        DetailsHeading.Text = $"Edit {ResolveTypeLabel(_selectedNode)}";
        DetailsKindLabel.Text = ResolveKindLabel(_selectedNode);
        DetailsTypeValue.Text = ResolveTypeLabel(_selectedNode);
        TaskStatusPanel.Visibility = _selectedNode?.Kind == TreeNodeKind.Task
            ? Visibility.Visible
            : Visibility.Collapsed;
        AddSectionButton.Visibility = _selectedNode?.Kind == TreeNodeKind.Project
            ? Visibility.Visible
            : Visibility.Collapsed;
        AddTaskButton.Visibility = _selectedNode?.Kind is TreeNodeKind.Project or TreeNodeKind.Group
            ? Visibility.Visible
            : Visibility.Collapsed;
        AddSubtaskButton.Visibility = _selectedNode?.Kind == TreeNodeKind.Task
            ? Visibility.Visible
            : Visibility.Collapsed;

        var selectedTask = _selectedNode?.Kind == TreeNodeKind.Task
            ? _state.Tasks.FirstOrDefault(task => task.Id == _selectedNode.Id)
            : null;
        WaitingForInput.Text = selectedTask?.WaitingFor ?? string.Empty;
        DetailsPinPanel.Visibility = selectedTask is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        DetailsPinToggle.IsChecked = selectedTask?.PinToPanel ?? false;
        DetailsPinToggle.ToolTip = selectedTask?.PinToPanel == true
            ? "Remove from panel"
            : "Pin to panel";
        LocationPanel.Visibility = selectedTask is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        LoadLocationEditor(selectedTask);
        DetailsNotesLabel.Text = selectedTask is null
            ? "CONTEXT / NOTES"
            : "NOTES / DESCRIPTION";
        DetailsNotesInput.IsEnabled = selectedTask is not null;
        DetailsNotesInput.Text = selectedTask?.Description ?? string.Empty;
        DetailsNotesHint.Text = selectedTask is null
            ? "Project and section notes are planned and are not stored in this version."
            : "Saved with this task and available to Task Details.";
        _draftStatus = _selectedNode?.Status ?? TreeNodeStatus.Todo;
        UpdateStatusEditor();
        UpdateCreationContext();

        DeleteButton.IsEnabled = hasSelection && !IsProtectedDefaultProject(_selectedNode);
    }

    private void UpdateStatusEditor()
    {
        WaitingForPanel.Visibility = _selectedNode?.Kind == TreeNodeKind.Task &&
                                     _draftStatus == TreeNodeStatus.Wait
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetStatusButtonState(TodoStatusButton, TreeNodeStatus.Todo, TodoBrush);
        SetStatusButtonState(FocusStatusButton, TreeNodeStatus.Focus, FocusBrush);
        SetStatusButtonState(WaitStatusButton, TreeNodeStatus.Wait, WaitBrush);
        SetStatusButtonState(DoneStatusButton, TreeNodeStatus.Done, DoneBrush);
    }

    private void LoadLocationEditor(TaskItem? task)
    {
        _suppressLocationEvents = true;
        try
        {
            if (task is null)
            {
                ProjectLocationCombo.ItemsSource = null;
                SectionLocationCombo.ItemsSource = null;
                ParentLocationCombo.ItemsSource = null;
                return;
            }

            var project = ProjectReferenceResolver.ResolveProject(_state, task);
            var projectOptions = OrderedProjects()
                .Select(item => new TreeLocationOption(item.Id, item.Name))
                .ToList();
            ProjectLocationCombo.ItemsSource = projectOptions;
            ProjectLocationCombo.SelectedItem = projectOptions.FirstOrDefault(option =>
                option.Id == project?.Id);
            RefreshSectionLocationOptions(project?.Id, task.GroupId);
            RefreshParentLocationOptions(project?.Id, task.GroupId, task.ParentTaskId, task.Id);
        }
        finally
        {
            _suppressLocationEvents = false;
        }
    }

    private void RefreshSectionLocationOptions(Guid? projectId, Guid? selectedSectionId)
    {
        var options = new List<TreeLocationOption>
        {
            new(null, "Project root")
        };
        if (projectId.HasValue)
        {
            options.AddRange(_state.Groups
                .Where(group => group.ProjectId == projectId.Value)
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => group.CreatedAtUtc)
                .Select(group => new TreeLocationOption(group.Id, group.Name)));
        }

        SectionLocationCombo.ItemsSource = options;
        SectionLocationCombo.SelectedItem = options.FirstOrDefault(option =>
            option.Id == selectedSectionId) ?? options[0];
    }

    private void RefreshParentLocationOptions(
        Guid? projectId,
        Guid? sectionId,
        Guid? selectedParentId,
        Guid currentTaskId)
    {
        var excludedIds = _treeStateService.GetDescendants(currentTaskId)
            .Select(node => node.Id)
            .Append(currentTaskId)
            .ToHashSet();
        var options = new List<TreeLocationOption>
        {
            new(null, "No task parent")
        };
        if (projectId.HasValue)
        {
            options.AddRange(_state.Tasks
                .Where(task => task.ProjectId == projectId.Value &&
                               task.GroupId == sectionId &&
                               !excludedIds.Contains(task.Id))
                .OrderBy(task => task.SortOrder)
                .ThenBy(task => task.CreatedAtUtc)
                .Select(task => new TreeLocationOption(task.Id, task.Title)));
        }

        ParentLocationCombo.ItemsSource = options;
        ParentLocationCombo.SelectedItem = options.FirstOrDefault(option =>
            option.Id == selectedParentId) ?? options[0];
    }

    private void ProjectLocationCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLocationEvents || _selectedNode?.Kind != TreeNodeKind.Task)
        {
            return;
        }

        var projectId = (ProjectLocationCombo.SelectedItem as TreeLocationOption)?.Id;
        _suppressLocationEvents = true;
        try
        {
            RefreshSectionLocationOptions(projectId, selectedSectionId: null);
            RefreshParentLocationOptions(
                projectId,
                sectionId: null,
                selectedParentId: null,
                _selectedNode.Id);
        }
        finally
        {
            _suppressLocationEvents = false;
        }
    }

    private void SectionLocationCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLocationEvents || _selectedNode?.Kind != TreeNodeKind.Task)
        {
            return;
        }

        var projectId = (ProjectLocationCombo.SelectedItem as TreeLocationOption)?.Id;
        var sectionId = (SectionLocationCombo.SelectedItem as TreeLocationOption)?.Id;
        _suppressLocationEvents = true;
        try
        {
            RefreshParentLocationOptions(
                projectId,
                sectionId,
                selectedParentId: null,
                _selectedNode.Id);
        }
        finally
        {
            _suppressLocationEvents = false;
        }
    }

    private Guid? ResolveLocationParentId()
    {
        return (ParentLocationCombo.SelectedItem as TreeLocationOption)?.Id ??
               (SectionLocationCombo.SelectedItem as TreeLocationOption)?.Id ??
               (ProjectLocationCombo.SelectedItem as TreeLocationOption)?.Id;
    }

    private void SetStatusButtonState(Button button, TreeNodeStatus status, Brush accent)
    {
        var selected = _draftStatus == status;
        button.BorderBrush = selected ? accent : (Brush)FindResource("TreeBorder");
        button.Foreground = selected ? accent : (Brush)FindResource("TreeMuted");
        button.Background = selected
            ? new SolidColorBrush(Color.FromArgb(38, ((SolidColorBrush)accent).Color.R,
                ((SolidColorBrush)accent).Color.G, ((SolidColorBrush)accent).Color.B))
            : (Brush)FindResource("TreeElevated");
    }

    private void UpdateFilterButtons()
    {
        SetFilterButtonState(AllFilterButton, TreeManagerFilter.All);
        SetFilterButtonState(ActiveOnlyFilterButton, TreeManagerFilter.ActiveOnly);
        SetFilterButtonState(ActiveAncestorsFilterButton, TreeManagerFilter.ActivePlusAncestors);
    }

    private void UpdateStatusFilterButtons()
    {
        SetStatusFilterButtonState(StatusAllFilterButton, TreeManagerStatusFilter.All);
        SetStatusFilterButtonState(StatusPanelFilterButton, TreeManagerStatusFilter.Panel);
        SetStatusFilterButtonState(StatusFocusFilterButton, TreeManagerStatusFilter.Focus);
        SetStatusFilterButtonState(StatusWaitFilterButton, TreeManagerStatusFilter.Wait);
        SetStatusFilterButtonState(StatusRemindFilterButton, TreeManagerStatusFilter.Remind);
        SetStatusFilterButtonState(StatusTodoFilterButton, TreeManagerStatusFilter.Todo);
        SetStatusFilterButtonState(StatusDoneFilterButton, TreeManagerStatusFilter.Done);
    }

    private void SetStatusFilterButtonState(Button button, TreeManagerStatusFilter filter)
    {
        var selected = _state.TreeManagerSettings.StatusFilter == filter;
        button.Background = selected ? ActiveFilterBackground : InactiveFilterBackground;
        button.Foreground = selected ? ActiveFilterForeground : InactiveFilterForeground;
        button.BorderBrush = selected
            ? (Brush)FindResource("TreeBorder")
            : Brushes.Transparent;
    }

    private void UpdateActiveView()
    {
        var treeActive = _state.TreeManagerSettings.ActiveView == TreeManagerView.Tree;
        TreeFilterBar.Visibility = treeActive ? Visibility.Visible : Visibility.Collapsed;
        TreeContent.Visibility = treeActive ? Visibility.Visible : Visibility.Collapsed;
        StatusFilterBar.Visibility = treeActive ? Visibility.Collapsed : Visibility.Visible;
        StatusContent.Visibility = treeActive ? Visibility.Collapsed : Visibility.Visible;
        SetTabButtonState(TreeTabButton, treeActive);
        SetTabButtonState(StatusTabButton, !treeActive);
    }

    private void SetTabButtonState(Button button, bool selected)
    {
        button.Foreground = selected ? ActiveFilterForeground : InactiveFilterForeground;
        button.BorderBrush = selected
            ? (Brush)FindResource("TreeFocus")
            : Brushes.Transparent;
    }

    private void SetFilterButtonState(Button button, TreeManagerFilter filter)
    {
        var selected = _state.TreeManagerSettings.Filter == filter;
        button.Background = selected ? ActiveFilterBackground : InactiveFilterBackground;
        button.Foreground = selected ? ActiveFilterForeground : InactiveFilterForeground;
        button.BorderBrush = selected
            ? (Brush)FindResource("TreeBorder")
            : Brushes.Transparent;
    }

    private void ProjectList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents || ProjectList.SelectedItem is not TreeProjectViewModel project)
        {
            return;
        }

        _state.TreeManagerSettings.SelectedProjectId = project.Id;
        _state.TreeManagerSettings.SelectedNodeId = project.Id;
        _state.OverlaySettings.LastSelectedProjectId = project.Id;
        PersistUiState();
        Refresh();
    }

    private void TreeRows_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents || TreeRows.SelectedItem is not TreeNodeRowViewModel row)
        {
            return;
        }

        _state.TreeManagerSettings.SelectedNodeId = row.Id;
        PersistUiState();
        LoadSelectedNode();
    }

    private void StatusRows_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents || StatusRows.SelectedItem is not TreeStatusRowViewModel row)
        {
            return;
        }

        _state.TreeManagerSettings.SelectedNodeId = row.Id;
        var project = _treeStateService.GetProjectRoot(row.Id);
        if (project is not null)
        {
            _state.TreeManagerSettings.SelectedProjectId = project.Id;
            _suppressSelectionEvents = true;
            try
            {
                ProjectList.SelectedItem = _projects.FirstOrDefault(item => item.Id == project.Id);
                SelectedProjectLabel.Text = project.Title;
                TreePathLabel.Text = $"{project.Title}  /  task tree";
            }
            finally
            {
                _suppressSelectionEvents = false;
            }
        }

        PersistUiState();
        LoadSelectedNode();
    }

    private void ViewTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string viewName } ||
            !Enum.TryParse<TreeManagerView>(viewName, out var view))
        {
            return;
        }

        _state.TreeManagerSettings.ActiveView = view;
        PersistUiState();
        Refresh();
    }

    private void FilterButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filterName } ||
            !Enum.TryParse<TreeManagerFilter>(filterName, out var filter))
        {
            return;
        }

        _state.TreeManagerSettings.Filter = filter;
        PersistUiState();
        Refresh();
    }

    private void StatusFilterButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filterName } ||
            !Enum.TryParse<TreeManagerStatusFilter>(filterName, out var filter))
        {
            return;
        }

        _state.TreeManagerSettings.StatusFilter = filter;
        PersistUiState();
        RefreshStatusRows();
        UpdateStatusFilterButtons();
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            RefreshTreeRows();
            RefreshStatusRows();
        }
    }

    private void PinButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Guid? taskId = (sender as FrameworkElement)?.DataContext switch
        {
            TreeNodeRowViewModel { IsTask: true } treeRow => treeRow.Id,
            TreeStatusRowViewModel statusRow => statusRow.Id,
            _ => null
        };

        e.Handled = true;
        if (taskId.HasValue)
        {
            Dispatcher.BeginInvoke(new Action(() => TogglePinToPanel(taskId.Value)));
        }
    }

    private void DetailsPinToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedNode?.Kind == TreeNodeKind.Task)
        {
            SetPinToPanel(_selectedNode.Id, DetailsPinToggle.IsChecked == true);
        }

        e.Handled = true;
    }

    private void TogglePinToPanel(Guid taskId)
    {
        var task = _state.Tasks.FirstOrDefault(item => item.Id == taskId);
        if (task is not null)
        {
            SetPinToPanel(taskId, !task.PinToPanel);
        }
    }

    private void SetPinToPanel(Guid taskId, bool pinToPanel)
    {
        if (!_treeStateService.SetPinToPanel(taskId, pinToPanel))
        {
            return;
        }

        _saveState();
        Refresh();
    }

    private void ExpandButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TreeNodeRowViewModel row })
        {
            return;
        }

        var expandedIds = _state.TreeManagerSettings.ExpandedNodeIds;
        if (!expandedIds.Remove(row.Id))
        {
            expandedIds.Add(row.Id);
        }

        PersistUiState();
        RefreshTreeRows();
        e.Handled = true;
    }

    private void MoveUpButton_OnClick(object sender, RoutedEventArgs e) =>
        MoveNode(sender, offset: -1, e);

    private void MoveDownButton_OnClick(object sender, RoutedEventArgs e) =>
        MoveNode(sender, offset: 1, e);

    private void MoveNode(object sender, int offset, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TreeNodeRowViewModel row })
        {
            return;
        }

        var siblings = _treeStateService.GetChildren(row.Node.ParentId);
        var currentIndex = siblings.ToList().FindIndex(node => node.Id == row.Id);
        if (currentIndex >= 0 && _treeStateService.ReorderNode(row.Id, currentIndex + offset))
        {
            SaveAndRefresh(row.Id);
        }

        e.Handled = true;
    }

    private void StatusButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string statusName } &&
            Enum.TryParse<TreeNodeStatus>(statusName, out var status))
        {
            _draftStatus = status;
            UpdateStatusEditor();
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is null || string.IsNullOrWhiteSpace(DetailsTitleInput.Text))
        {
            return;
        }

        if (_selectedNode.Kind == TreeNodeKind.Task)
        {
            var locationParentId = ResolveLocationParentId();
            if (!locationParentId.HasValue ||
                _selectedNode.ParentId != locationParentId &&
                !_treeStateService.MoveNode(_selectedNode.Id, locationParentId.Value))
            {
                return;
            }
        }

        if (!_treeStateService.RenameNode(_selectedNode.Id, DetailsTitleInput.Text))
        {
            return;
        }

        if (_selectedNode.Kind == TreeNodeKind.Task)
        {
            _treeStateService.MarkStatus(_selectedNode.Id, _draftStatus);
            _treeStateService.SetWaitingFor(_selectedNode.Id, WaitingForInput.Text);
            _treeStateService.SetDescription(_selectedNode.Id, DetailsNotesInput.Text);
        }

        SaveAndRefresh(_selectedNode.Id);
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        LoadSelectedNode();
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is null || IsProtectedDefaultProject(_selectedNode))
        {
            return;
        }

        var label = ResolveTypeLabel(_selectedNode).ToLowerInvariant();
        var result = MessageBox.Show(
            this,
            $"Delete this {label}? Child tasks will be preserved and safely reparented where possible.",
            "Confirm delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var parentId = _selectedNode.ParentId ?? _state.TreeManagerSettings.SelectedProjectId;
        if (!_treeStateService.DeleteNode(_selectedNode.Id))
        {
            return;
        }

        _state.TreeManagerSettings.ExpandedNodeIds.Remove(_selectedNode.Id);
        _state.TreeManagerSettings.SelectedNodeId = parentId;
        SaveAndRefresh(parentId);
    }

    private void NewProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryPrompt("New project", "Project title", out var title))
        {
            return;
        }

        var project = _treeStateService.CreateProject(title);
        if (project is null)
        {
            return;
        }

        _state.TreeManagerSettings.SelectedProjectId = project.Id;
        _state.TreeManagerSettings.SelectedNodeId = project.Id;
        _state.OverlaySettings.LastSelectedProjectId = project.Id;
        SaveAndRefresh(project.Id);
    }

    private void NewSectionButton_OnClick(object sender, RoutedEventArgs e) =>
        CreateSection();

    private void AddSectionButton_OnClick(object sender, RoutedEventArgs e) =>
        CreateSection();

    private void CreateSection()
    {
        var projectId = _state.TreeManagerSettings.SelectedProjectId;
        var project = projectId.HasValue ? _treeStateService.GetNode(projectId.Value) : null;
        if (!projectId.HasValue ||
            project is null ||
            !TryPrompt("New section", $"Section title in {project.Title}", out var title))
        {
            return;
        }

        var section = _treeStateService.CreateGroup(projectId.Value, title);
        if (section is not null)
        {
            _state.TreeManagerSettings.ExpandedNodeIds.Add(projectId.Value);
            SaveAndRefresh(section.Id);
        }
    }

    private void NewTaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        var parentId = ResolveNewTaskParent();
        CreateTask(parentId, "New task", "Task title");
    }

    private void AddTaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        var parentId = _selectedNode?.Kind is TreeNodeKind.Project or TreeNodeKind.Group
            ? _selectedNode.Id
            : _state.TreeManagerSettings.SelectedProjectId;
        CreateTask(parentId, "New task", "Task title");
    }

    private void AddSubtaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        Guid? parentId = _selectedNode?.Kind == TreeNodeKind.Task
            ? _selectedNode.Id
            : null;
        CreateTask(parentId, "New subtask", "Subtask title");
    }

    private void CreateTask(Guid? parentId, string title, string prompt)
    {
        var parent = parentId.HasValue ? _treeStateService.GetNode(parentId.Value) : null;
        if (!parentId.HasValue ||
            parent is null ||
            !TryPrompt(title, $"{prompt} in {parent.Title}", out var taskTitle))
        {
            return;
        }

        var task = _treeStateService.CreateTask(parentId.Value, taskTitle);
        if (task is null)
        {
            return;
        }

        if (!_state.TreeManagerSettings.ExpandedNodeIds.Contains(parentId.Value))
        {
            _state.TreeManagerSettings.ExpandedNodeIds.Add(parentId.Value);
        }

        SaveAndRefresh(task.Id);
    }

    private Guid? ResolveNewTaskParent()
    {
        if (_selectedNode?.Kind is TreeNodeKind.Project or TreeNodeKind.Group)
        {
            return _selectedNode.Id;
        }

        if (_selectedNode?.Kind == TreeNodeKind.Task)
        {
            var structuralParent = _treeStateService.GetAncestors(_selectedNode.Id)
                .LastOrDefault(node => node.Kind == TreeNodeKind.Group) ??
                                   _treeStateService.GetProjectRoot(_selectedNode.Id);
            return structuralParent?.Id;
        }

        return _state.TreeManagerSettings.SelectedProjectId;
    }

    private void UpdateCreationContext()
    {
        var selectedProjectId = _state.TreeManagerSettings.SelectedProjectId;
        var project = selectedProjectId.HasValue
            ? _treeStateService.GetNode(selectedProjectId.Value)
            : null;
        var taskParentId = ResolveNewTaskParent();
        var taskParent = taskParentId.HasValue
            ? _treeStateService.GetNode(taskParentId.Value)
            : null;

        NewSectionButton.IsEnabled = project is not null;
        NewSectionButton.ToolTip = project is null
            ? "Select a project first"
            : $"Create a section in project {project.Title}";
        NewTaskButton.IsEnabled = taskParent is not null;
        NewTaskButton.ToolTip = taskParent is null
            ? "Select a project or section first"
            : $"Create a task in {ResolveTypeLabel(taskParent)} {taskParent.Title}";

        CreateTargetHint.Text = _selectedNode?.Kind switch
        {
            TreeNodeKind.Project => $"New sections and tasks will be created in Project \"{_selectedNode.Title}\".",
            TreeNodeKind.Group => $"New tasks will be created in Section \"{_selectedNode.Title}\".",
            TreeNodeKind.Task => $"New subtasks will be created under Task \"{_selectedNode.Title}\".",
            _ => "Select a project, section, or task to choose a creation target."
        };
    }

    private bool TryPrompt(string title, string prompt, out string value) =>
        TreeNodePromptWindow.TryShow(this, title, prompt, string.Empty, out value);

    private void SaveAndRefresh(Guid? selectedNodeId)
    {
        if (selectedNodeId.HasValue)
        {
            _state.TreeManagerSettings.SelectedNodeId = selectedNodeId;
            var project = _treeStateService.GetProjectRoot(selectedNodeId.Value);
            if (project is not null)
            {
                _state.TreeManagerSettings.SelectedProjectId = project.Id;
            }
        }

        _saveState();
        _taskPresentationChanged();
        Refresh();
    }

    private void PersistUiState()
    {
        _saveState();
    }

    private IEnumerable<ProjectItem> OrderedProjects() => _state.Projects
        .OrderBy(project => project.SortOrder)
        .ThenBy(project => project.CreatedAtUtc)
        .ThenBy(project => project.Id);

    private bool IsProtectedDefaultProject(TreeNode? node)
    {
        return node?.Kind == TreeNodeKind.Project &&
               _state.Projects.Any(project =>
                   project.Id == node.Id &&
                   string.Equals(
                       project.Name,
                       ProjectItem.DefaultName,
                       StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveTypeLabel(TreeNode? node)
    {
        if (node is null)
        {
            return "None";
        }

        if (node.Kind == TreeNodeKind.Project)
        {
            return "Project";
        }

        if (node.Kind == TreeNodeKind.Group)
        {
            return "Section";
        }

        var parent = node.ParentId.HasValue
            ? _treeStateService.GetNode(node.ParentId.Value)
            : null;
        return parent?.Kind == TreeNodeKind.Task ? "Subtask" : "Task";
    }

    private string ResolveKindLabel(TreeNode? node) => ResolveTypeLabel(node).ToUpperInvariant();

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
