using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class TreeManagerWindow : Window
{
    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly Action _taskPresentationChanged;
    private readonly TreeStateService _treeStateService;
    private readonly ObservableCollection<TreeCardViewModel> _cards = new();

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
        TreeCards.ItemsSource = _cards;
        Refresh();
    }

    private TreeCardViewModel? SelectedCard =>
        TreeCards.SelectedItem as TreeCardViewModel;

    public void Refresh()
    {
        var selectedNodeId = SelectedCard?.Node.Id;
        Refresh(selectedNodeId);
    }

    private void Refresh(Guid? selectedNodeId)
    {
        _cards.Clear();
        foreach (var project in _state.Projects
                     .OrderBy(item => item.SortOrder)
                     .ThenBy(item => item.CreatedAtUtc)
                     .ThenBy(item => item.Id))
        {
            var depths = new Dictionary<Guid, int>();
            foreach (var node in _treeStateService.GetProjection(
                         project.Id,
                         TreeProjection.AllInProject))
            {
                var depth = node.ParentId.HasValue &&
                            depths.TryGetValue(node.ParentId.Value, out var parentDepth)
                    ? parentDepth + 1
                    : 0;
                depths[node.Id] = depth;
                _cards.Add(new TreeCardViewModel(node, depth));
            }
        }

        TreeCards.SelectedItem = selectedNodeId.HasValue
            ? _cards.FirstOrDefault(card => card.Node.Id == selectedNodeId.Value)
            : null;
        UpdateToolbar();
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void TreeCards_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateToolbar();
    }

    private void NewProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        var title = TreeManagerDialogs.PromptForText(
            this,
            "New project",
            "Project name:");
        if (title is null)
        {
            return;
        }

        var created = _treeStateService.CreateProject(title);
        CompleteMutation(created, "Could not create the project.");
    }

    private void NewGroupButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCard is not { IsProject: true } projectCard)
        {
            return;
        }

        var title = TreeManagerDialogs.PromptForText(
            this,
            "New group",
            $"Group name under {projectCard.Title}:");
        if (title is null)
        {
            return;
        }

        var created = _treeStateService.CreateGroup(projectCard.Node.Id, title);
        CompleteMutation(created, "Could not create the group.");
    }

    private void NewTaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        var parent = ResolveNewTaskParent();
        if (parent is null)
        {
            ShowMutationFailure("No project is available for the new task.");
            return;
        }

        var title = TreeManagerDialogs.PromptForText(
            this,
            "New task",
            $"Task title under {parent.Title}:");
        if (title is null)
        {
            return;
        }

        var created = _treeStateService.CreateTask(parent.Id, title);
        CompleteMutation(created, "Could not create the task.");
    }

    private void NewSubtaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCard is not { IsTask: true } taskCard)
        {
            return;
        }

        var title = TreeManagerDialogs.PromptForText(
            this,
            "New subtask",
            $"Subtask title under {taskCard.Title}:");
        if (title is null)
        {
            return;
        }

        var created = _treeStateService.CreateTask(taskCard.Node.Id, title);
        CompleteMutation(created, "Could not create the subtask.");
    }

    private void RenameButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = SelectedCard;
        if (selected is null)
        {
            return;
        }

        var title = TreeManagerDialogs.PromptForText(
            this,
            "Rename",
            $"New name for {selected.Title}:",
            selected.Title);
        if (title is null)
        {
            return;
        }

        if (!_treeStateService.RenameNode(selected.Node.Id, title))
        {
            ShowMutationFailure(
                selected.IsProject && IsDefaultProject(selected.Node.Id)
                    ? "The Default project name is protected."
                    : "Could not rename the selected item.");
            return;
        }

        SaveAndRefresh(selected.Node.Id);
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = SelectedCard;
        if (selected is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Delete {selected.KindLabel.ToLowerInvariant()} \"{selected.Title}\"?\n\n" +
            "Child items will be safely reparented according to the tree rules.",
            "Delete tree item",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var nextSelection = selected.Node.ParentId;
        if (!_treeStateService.DeleteNode(selected.Node.Id))
        {
            ShowMutationFailure(
                selected.IsProject && IsDefaultProject(selected.Node.Id)
                    ? "The Default project cannot be deleted while it is the only fallback project."
                    : "Could not delete the selected item.");
            return;
        }

        SaveAndRefresh(nextSelection);
    }

    private void ActiveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCard is not { IsTask: true } taskCard ||
            !_treeStateService.MarkActive(taskCard.Node.Id, !taskCard.IsActive))
        {
            return;
        }

        SaveAndRefresh(taskCard.Node.Id);
    }

    private void CompletionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCard is not { IsTask: true } taskCard)
        {
            return;
        }

        var status = taskCard.IsDone ? TreeNodeStatus.Todo : TreeNodeStatus.Done;
        if (!_treeStateService.MarkStatus(taskCard.Node.Id, status))
        {
            return;
        }

        SaveAndRefresh(taskCard.Node.Id);
    }

    private void MoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = SelectedCard;
        if (selected is null || selected.IsProject)
        {
            return;
        }

        var targets = BuildMoveTargets(selected);
        if (targets.Count == 0)
        {
            ShowMutationFailure("No valid destination is available for this item.");
            return;
        }

        var targetId = TreeManagerDialogs.ChooseMoveTarget(
            this,
            selected.Title,
            targets);
        if (!targetId.HasValue)
        {
            return;
        }

        if (!_treeStateService.MoveNode(selected.Node.Id, targetId.Value))
        {
            ShowMutationFailure("The selected destination is no longer valid.");
            return;
        }

        SaveAndRefresh(selected.Node.Id);
    }

    private TreeNode? ResolveNewTaskParent()
    {
        if (SelectedCard is { } selected &&
            (selected.IsProject || selected.IsGroup))
        {
            return selected.Node;
        }

        var defaultProject = _state.Projects.FirstOrDefault(project =>
            string.Equals(
                project.Name,
                ProjectItem.DefaultName,
                StringComparison.OrdinalIgnoreCase));
        var project = defaultProject ?? _state.Projects
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedAtUtc)
            .FirstOrDefault();
        return project is null ? null : _treeStateService.GetNode(project.Id);
    }

    private IReadOnlyList<TreeMoveTarget> BuildMoveTargets(TreeCardViewModel selected)
    {
        var excludedIds = _treeStateService.GetDescendants(selected.Node.Id)
            .Select(node => node.Id)
            .ToHashSet();
        excludedIds.Add(selected.Node.Id);

        var targets = new List<TreeMoveTarget>();
        foreach (var card in _cards)
        {
            if (excludedIds.Contains(card.Node.Id) ||
                card.Node.Id == selected.Node.ParentId)
            {
                continue;
            }

            var validKind = selected.IsGroup
                ? card.IsProject
                : card.IsProject || card.IsGroup || card.IsTask;
            if (!validKind)
            {
                continue;
            }

            var depth = (int)(card.IndentWidth / 22);
            targets.Add(new TreeMoveTarget(
                card.Node.Id,
                $"{new string(' ', depth * 2)}{card.KindLabel}: {card.Title}"));
        }

        return targets;
    }

    private void CompleteMutation(TreeNode? created, string failureMessage)
    {
        if (created is null)
        {
            ShowMutationFailure(failureMessage);
            return;
        }

        SaveAndRefresh(created.Id);
    }

    private void SaveAndRefresh(Guid? selectedNodeId)
    {
        _saveState();
        Refresh(selectedNodeId);
        _taskPresentationChanged();
    }

    private void UpdateToolbar()
    {
        var selected = SelectedCard;
        var isProject = selected?.IsProject == true;
        var isGroup = selected?.IsGroup == true;
        var isTask = selected?.IsTask == true;

        NewProjectButton.IsEnabled = true;
        NewGroupButton.IsEnabled = isProject;
        NewTaskButton.IsEnabled = selected is null || isProject || isGroup;
        NewSubtaskButton.IsEnabled = isTask;
        RenameButton.IsEnabled = selected is not null;
        DeleteButton.IsEnabled = selected is not null;
        MoveButton.IsEnabled = selected is not null && !isProject;
        ActiveButton.IsEnabled = isTask && (!selected!.IsDone || selected.IsActive);
        CompletionButton.IsEnabled = isTask;

        ActiveButton.Content = selected?.IsActive == true
            ? "Clear active"
            : "Set active";
        CompletionButton.Content = selected?.IsDone == true
            ? "Reopen"
            : "Complete";
        SelectionStatus.Text = selected is null
            ? "No selection"
            : $"Selected: {selected.KindLabel} / {selected.Title}";
    }

    private bool IsDefaultProject(Guid projectId)
    {
        return _state.Projects.Any(project =>
            project.Id == projectId &&
            string.Equals(
                project.Name,
                ProjectItem.DefaultName,
                StringComparison.OrdinalIgnoreCase));
    }

    private void ShowMutationFailure(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Tree Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
