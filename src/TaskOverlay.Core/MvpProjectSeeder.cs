using System;
using System.Linq;

namespace TaskOverlay.Core;

public static class MvpProjectSeeder
{
    public static bool EnsureSeedProjects(
        AppState state,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.OverlaySettings.MvpProjectsSeeded)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var projectService = new ProjectService(state);
        foreach (var definition in ProjectColorPalette.MvpProjects)
        {
            var project = state.Projects.FirstOrDefault(item =>
                string.Equals(item.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
            project ??= projectService.CreateProject(definition.Name, timestamp);
            if (project is null)
            {
                continue;
            }

            if (!string.Equals(project.ColorHex, definition.ColorHex, StringComparison.OrdinalIgnoreCase))
            {
                project.ColorHex = definition.ColorHex;
                project.UpdatedAtUtc = timestamp;
            }
        }

        state.OverlaySettings.MvpProjectsSeeded = true;
        var personal = state.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, "Personal", StringComparison.OrdinalIgnoreCase));
        state.OverlaySettings.LastSelectedProjectId ??= personal?.Id;
        return true;
    }
}
