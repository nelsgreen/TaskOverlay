using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public static class ProjectReferenceResolver
{
    public static ProjectItem? ResolveProject(AppState state, TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(task);

        var projects = state.Projects ?? new List<ProjectItem>();
        var referencedProject = task.ProjectId.HasValue
            ? projects.FirstOrDefault(project => project.Id == task.ProjectId.Value)
            : null;

        return referencedProject ?? projects.FirstOrDefault(project =>
            string.Equals(project.Name, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase));
    }
}
