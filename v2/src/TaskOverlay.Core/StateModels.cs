using System;
using System.Collections.Generic;

namespace TaskOverlay.Core;

public sealed class AppState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<TaskItem> Tasks { get; set; } = new();
    public OverlaySettings OverlaySettings { get; set; } = new();
    public WindowPlacement WindowPlacement { get; set; } = new();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public static AppState CreateDefault(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;

        return new AppState
        {
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp,
            Tasks =
            {
                TaskItem.Create("Validate transparent overlay", timestamp),
                TaskItem.Create("Test tray lifecycle", timestamp),
                TaskItem.Create("Check DPI and hover behavior", timestamp)
            }
        };
    }
}

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public bool InWork { get; set; }
    public bool DescriptionExpanded { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? DueAtUtc { get; set; }

    public static TaskItem Create(string title, DateTimeOffset? now = null)
    {
        return new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow
        };
    }
}

public enum TaskPriority
{
    Low,
    Normal,
    High,
    Critical
}

public enum InWorkMode
{
    MultipleTasks,
    SingleTask
}

public sealed class OverlaySettings
{
    public int ActiveToPassiveDelayMilliseconds { get; set; } = 500;
    public bool AlwaysOnTop { get; set; } = true;
    public bool CollapsedMode { get; set; }
    public bool PinnedActiveMode { get; set; }
    public InWorkMode InWorkMode { get; set; } = InWorkMode.MultipleTasks;
}

public sealed class WindowPlacement
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? CollapsedLeft { get; set; }
    public double? CollapsedTop { get; set; }
}
