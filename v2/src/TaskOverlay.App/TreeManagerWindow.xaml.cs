using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    private readonly ObservableCollection<TreeActiveTaskViewModel> _activeTasks = new();
    private TreeNode? _selectedNode;
    private TreeNodeStatus _draftStatus;
    private bool _suppressSelectionEvents;

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
            RefreshActiveNow();
            LoadSelectedNode();
            UpdateFilterButtons();
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
        ProjectSummary.Text = $"{_projects.Count} projects · {activeTasks.Count} active";
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
            var parent = node.ParentId.HasValue
                ? _treeStateService.GetNode(node.ParentId.Value)
                : null;
            var siblings = _treeStateService.GetChildren(node.ParentId);
            var siblingIndex = siblings.ToList().FindIndex(sibling => sibling.Id == node.Id);
            var task = node.Kind == TreeNodeKind.Task
                ? _state.Tasks.FirstOrDefault(item => item.Id == node.Id)
                : null;
            var depth = _state.TreeManagerSettings.Filter == TreeManagerFilter.ActiveOnly
                ? 0
                : Math.Max(0, ancestors.Count - 1);
            _rows.Add(new TreeNodeRowViewModel(
                node,
                parent?.Kind,
                depth,
                _treeStateService.GetChildren(node.Id).Count > 0,
                _state.TreeManagerSettings.ExpandedNodeIds.Contains(node.Id),
                task is not null && ReminderAttentionService.ShouldShowNotification(task, now),
                siblingIndex > 0,
                siblingIndex >= 0 && siblingIndex < siblings.Count - 1,
                task?.WaitingFor ?? string.Empty));
        }

        TreeRows.SelectedItem = _rows.FirstOrDefault(row =>
            row.Id == _state.TreeManagerSettings.SelectedNodeId);
        TreeEmptyState.Visibility = _rows.Count == 0
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
        _draftStatus = _selectedNode?.Status ?? TreeNodeStatus.Todo;
        UpdateStatusEditor();

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

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            RefreshTreeRows();
        }
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
        if (_selectedNode is null ||
            !_treeStateService.RenameNode(_selectedNode.Id, DetailsTitleInput.Text))
        {
            return;
        }

        if (_selectedNode.Kind == TreeNodeKind.Task)
        {
            _treeStateService.MarkStatus(_selectedNode.Id, _draftStatus);
            _treeStateService.SetWaitingFor(_selectedNode.Id, WaitingForInput.Text);
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
        if (!projectId.HasValue || !TryPrompt("New section", "Section title", out var title))
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
        var parentId = _selectedNode?.Kind == TreeNodeKind.Group
            ? _selectedNode.Id
            : _state.TreeManagerSettings.SelectedProjectId;
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
        if (!parentId.HasValue || !TryPrompt(title, prompt, out var taskTitle))
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
