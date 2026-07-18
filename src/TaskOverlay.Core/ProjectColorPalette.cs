using System;
using System.Collections.Generic;

namespace TaskOverlay.Core;

public static class ProjectColorPalette
{
    public const string KazChess = "#22C55E";
    public const string Plhiv = "#F59E0B";
    public const string TaskOverlay = "#8B5CF6";
    public const string Personal = "#6B7280";
    public const string Default = "#64748B";

    private static readonly string[] AdditionalColors =
    {
        "#0EA5E9",
        "#EC4899",
        "#14B8A6",
        "#EF4444",
        "#A855F7"
    };

    public static IReadOnlyList<MvpProjectDefinition> MvpProjects { get; } =
        new[]
        {
            new MvpProjectDefinition("KazChess", KazChess),
            new MvpProjectDefinition("PLHIV", Plhiv),
            new MvpProjectDefinition("TaskOverlay", TaskOverlay),
            new MvpProjectDefinition("Personal", Personal)
        };

    public static string Resolve(string? projectName, Guid projectId)
    {
        if (string.Equals(projectName, "KazChess", StringComparison.OrdinalIgnoreCase))
        {
            return KazChess;
        }

        if (string.Equals(projectName, "PLHIV", StringComparison.OrdinalIgnoreCase))
        {
            return Plhiv;
        }

        if (string.Equals(projectName, "TaskOverlay", StringComparison.OrdinalIgnoreCase))
        {
            return TaskOverlay;
        }

        if (string.Equals(projectName, "Personal", StringComparison.OrdinalIgnoreCase))
        {
            return Personal;
        }

        if (string.Equals(projectName, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            return Default;
        }

        var index = (int)((uint)projectId.GetHashCode() % AdditionalColors.Length);
        return AdditionalColors[index];
    }

    public static bool IsValid(string? colorHex)
    {
        if (colorHex is null || colorHex.Length != 7 || colorHex[0] != '#')
        {
            return false;
        }

        for (var index = 1; index < colorHex.Length; index++)
        {
            if (!Uri.IsHexDigit(colorHex[index]))
            {
                return false;
            }
        }

        return true;
    }
}

public readonly record struct MvpProjectDefinition(string Name, string ColorHex);
