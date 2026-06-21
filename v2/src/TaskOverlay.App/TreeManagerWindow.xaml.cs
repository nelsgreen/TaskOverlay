using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
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

    public void Refresh()
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
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void ActivateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetTaskCard(sender, out var card) ||
            !_treeStateService.MarkActive(card.Node.Id, active: true))
        {
            return;
        }

        SaveAndRefresh();
    }

    private void CompletionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetTaskCard(sender, out var card))
        {
            return;
        }

        var status = card.IsDone ? TreeNodeStatus.Todo : TreeNodeStatus.Done;
        if (!_treeStateService.MarkStatus(card.Node.Id, status))
        {
            return;
        }

        SaveAndRefresh();
    }

    private void SaveAndRefresh()
    {
        _saveState();
        _taskPresentationChanged();
        Refresh();
    }

    private static bool TryGetTaskCard(object sender, out TreeCardViewModel card)
    {
        card = null!;
        if (sender is not FrameworkElement
            {
                DataContext: TreeCardViewModel candidate
            } ||
            !candidate.IsTask)
        {
            return false;
        }

        card = candidate;
        return true;
    }
}
