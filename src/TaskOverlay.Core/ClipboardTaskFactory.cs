using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public static class ClipboardTaskFactory
{
    public static IReadOnlyList<TaskItem> CreateFromLines(
        string? clipboardText,
        DateTimeOffset? now = null,
        Guid? projectId = null)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            return Array.Empty<TaskItem>();
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        return SplitLines(clipboardText)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(title => CreateTask(title, string.Empty, timestamp, projectId))
            .ToArray();
    }

    public static TaskItem? CreateSingle(
        string? clipboardText,
        DateTimeOffset? now = null,
        Guid? projectId = null)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            return null;
        }

        var title = string.Join(
            " ",
            SplitLines(clipboardText)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));

        return title.Length == 0
            ? null
            : CreateTask(
                title,
                string.Empty,
                now ?? DateTimeOffset.UtcNow,
                projectId);
    }

    public static TaskItem? CreateWithDescription(
        string? clipboardText,
        DateTimeOffset? now = null,
        Guid? projectId = null)
    {
        var parsed = ParseWithDescription(clipboardText);
        return parsed is null
            ? null
            : CreateTask(
                parsed.Value.Title,
                parsed.Value.Description,
                now ?? DateTimeOffset.UtcNow,
                projectId);
    }

    public static ClipboardTaskText? ParseWithDescription(string? clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            return null;
        }

        var lines = SplitLines(clipboardText);
        var titleIndex = Array.FindIndex(
            lines,
            line => !string.IsNullOrWhiteSpace(line));

        if (titleIndex < 0)
        {
            return null;
        }

        var title = lines[titleIndex].Trim();
        var descriptionLines = lines
            .Skip(titleIndex + 1)
            .Select(line => line.TrimEnd())
            .ToList();

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

    private static TaskItem CreateTask(
        string title,
        string description,
        DateTimeOffset createdAtUtc,
        Guid? projectId)
    {
        return new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            Completed = false,
            Priority = TaskPriority.Normal,
            InWork = false,
            Status = TaskStatus.Todo,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            CompletedAtUtc = null,
            DueAtUtc = null,
            ProjectId = projectId,
            RemindAtUtc = null,
            RemindEveryMinutes = null,
            LastReminderAtUtc = null,
            ReminderActive = false
        };
    }

    private static string[] SplitLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }
}

public readonly record struct ClipboardTaskText(string Title, string Description);
