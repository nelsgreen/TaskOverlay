using System;
using System.Collections.Generic;

namespace TaskOverlay.Core;

public static class ClipboardTaskFactory
{
    public static TaskItem? Create(string? clipboardText, DateTimeOffset? now = null)
    {
        var parsed = Parse(clipboardText);
        if (parsed is null)
        {
            return null;
        }

        return new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = parsed.Value.Title,
            Description = parsed.Value.Description,
            Completed = false,
            Priority = TaskPriority.Normal,
            InWork = false,
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            CompletedAtUtc = null,
            DueAtUtc = null
        };
    }

    public static ClipboardTaskText? Parse(string? clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            return null;
        }

        var normalized = clipboardText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');

        var titleIndex = -1;
        for (var index = 0; index < lines.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
            {
                titleIndex = index;
                break;
            }
        }

        if (titleIndex < 0)
        {
            return null;
        }

        var title = lines[titleIndex].Trim();
        var descriptionLines = new List<string>();
        for (var index = titleIndex + 1; index < lines.Length; index++)
        {
            descriptionLines.Add(lines[index]);
        }

        while (descriptionLines.Count > 0 &&
               string.IsNullOrWhiteSpace(descriptionLines[0]))
        {
            descriptionLines.RemoveAt(0);
        }

        while (descriptionLines.Count > 0 &&
               string.IsNullOrWhiteSpace(descriptionLines[^1]))
        {
            descriptionLines.RemoveAt(descriptionLines.Count - 1);
        }

        var description = string.Join("\n", descriptionLines).Trim();
        return new ClipboardTaskText(title, description);
    }
}

public readonly record struct ClipboardTaskText(string Title, string Description);
