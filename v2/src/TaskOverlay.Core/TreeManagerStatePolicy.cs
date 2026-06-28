using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public static class TreeManagerStatePolicy
{
    public static bool Normalize(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var changed = false;
        if (state.TreeManagerSettings is null)
        {
            state.TreeManagerSettings = new TreeManagerSettings();
            changed = true;
        }

        var settings = state.TreeManagerSettings;
        if (settings.ExpandedNodeIds is null)
        {
            settings.ExpandedNodeIds = new List<Guid>();
            changed = true;
        }

        if (!Enum.IsDefined(settings.Filter))
        {
            settings.Filter = TreeManagerFilter.All;
            changed = true;
        }

        var projectIds = state.Projects.Select(project => project.Id).ToHashSet();
        var groupIds = state.Groups.Select(group => group.Id).ToHashSet();
        var taskIds = state.Tasks.Select(task => task.Id).ToHashSet();
        var nodeIds = projectIds.Concat(groupIds).Concat(taskIds).ToHashSet();

        var selectedProject = settings.SelectedProjectId is Guid selectedProjectId &&
                              projectIds.Contains(selectedProjectId)
            ? state.Projects.First(project => project.Id == selectedProjectId)
            : ResolveFallbackProject(state);
        if (settings.SelectedProjectId != selectedProject?.Id)
        {
            settings.SelectedProjectId = selectedProject?.Id;
            changed = true;
        }

        var selectedNodeValid = settings.SelectedNodeId is Guid selectedNodeId &&
                                nodeIds.Contains(selectedNodeId) &&
                                selectedProject is not null &&
                                new TreeStateService(state).GetProjectRoot(selectedNodeId)?.Id == selectedProject.Id;
        if (!selectedNodeValid && settings.SelectedNodeId != selectedProject?.Id)
        {
            settings.SelectedNodeId = selectedProject?.Id;
            changed = true;
        }

        var normalizedExpandedIds = settings.ExpandedNodeIds
            .Where(nodeIds.Contains)
            .Distinct()
            .ToList();
        if (!settings.ExpandedNodeIds.SequenceEqual(normalizedExpandedIds))
        {
            settings.ExpandedNodeIds = normalizedExpandedIds;
            changed = true;
        }

        return changed;
    }

    private static ProjectItem? ResolveFallbackProject(AppState state)
    {
        if (state.OverlaySettings.LastSelectedProjectId is Guid lastSelectedId)
        {
            var lastSelected = state.Projects.FirstOrDefault(project => project.Id == lastSelectedId);
            if (lastSelected is not null)
            {
                return lastSelected;
            }
        }

        return state.Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase)) ??
               state.Projects
                   .OrderBy(project => project.SortOrder)
                   .ThenBy(project => project.CreatedAtUtc)
                   .ThenBy(project => project.Id)
                   .FirstOrDefault();
    }
}
