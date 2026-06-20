using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TaskOverlay.Core;

public static class StateMigrator
{
    public static AppState Migrate(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Projects ??= new List<ProjectItem>();
        state.Groups ??= new List<GroupItem>();

        if (state.SchemaVersion == AppState.CurrentSchemaVersion)
        {
            return state;
        }

        if (state.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported schema version: {state.SchemaVersion}.");
        }

        state.Tasks ??= new List<TaskItem>();

        var defaultProject = state.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase));

        if (defaultProject is null)
        {
            var createdAtUtc = state.CreatedAtUtc == default
                ? DateTimeOffset.UtcNow
                : state.CreatedAtUtc;
            defaultProject = ProjectItem.CreateDefault(createdAtUtc);
            state.Projects.Add(defaultProject);
        }
        else if (defaultProject.Id == Guid.Empty)
        {
            defaultProject.Id = Guid.NewGuid();
        }

        foreach (var task in state.Tasks)
        {
            task.ProjectId = defaultProject.Id;
            task.GroupId = null;
        }

        state.SchemaVersion = AppState.CurrentSchemaVersion;
        return state;
    }
}
