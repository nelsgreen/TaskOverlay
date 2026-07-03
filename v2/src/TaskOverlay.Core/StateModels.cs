using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TaskOverlay.Core;

public sealed class AppState
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<TaskItem> Tasks { get; set; } = new();
    public List<ProjectItem> Projects { get; set; } = new();
    public List<GroupItem> Groups { get; set; } = new();
    public OverlaySettings OverlaySettings { get; set; } = new();
    public WindowPlacement WindowPlacement { get; set; } = new();
    public TreeManagerSettings TreeManagerSettings { get; set; } = new();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public static AppState CreateDefault(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var defaultProject = ProjectItem.CreateDefault(timestamp);

        return new AppState
        {
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp,
            OverlaySettings = new OverlaySettings
            {
                OverlayMode = global::TaskOverlay.Core.OverlayMode.Working
            },
            TreeManagerSettings = new TreeManagerSettings
            {
                SelectedProjectId = defaultProject.Id,
                SelectedNodeId = defaultProject.Id
            },
            Projects = { defaultProject },
            Tasks =
            {
                TaskItem.Create("Validate transparent overlay", timestamp, defaultProject.Id),
                TaskItem.Create("Test tray lifecycle", timestamp, defaultProject.Id),
                TaskItem.Create("Check DPI and hover behavior", timestamp, defaultProject.Id)
            }
        };
    }
}

public sealed class ProjectItem
{
    public const string DefaultName = "Default";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public static ProjectItem CreateDefault(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new ProjectItem
        {
            Name = DefaultName,
            ColorHex = ProjectColorPalette.Default,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
    }
}

public sealed class GroupItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public bool InWork { get; set; }

    [JsonPropertyName("status")]
    public TaskStatus? StoredStatus { get; set; }

    [JsonIgnore]
    public TaskStatus Status
    {
        get => StoredStatus ??
               (Completed
                   ? TaskStatus.Done
                   : InWork
                       ? TaskStatus.InWork
                       : TaskStatus.Todo);
        set => StoredStatus = value;
    }

    public bool DescriptionExpanded { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? DueAtUtc { get; set; }
    public DateTimeOffset? RemindAtUtc { get; set; }
    public int? RemindEveryMinutes { get; set; }
    public DateTimeOffset? LastReminderAtUtc { get; set; }
    public DateTimeOffset? ReminderAcknowledgedAtUtc { get; set; }
    public DateTimeOffset? ReminderSnoozedUntilUtc { get; set; }
    public string WaitingFor { get; set; } = string.Empty;
    public bool ReminderActive { get; set; }
    public bool PinToPanel { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public static TaskItem Create(
        string title,
        DateTimeOffset? now = null,
        Guid? projectId = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            Status = TaskStatus.Todo,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp,
            ProjectId = projectId
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

public enum TaskStatus
{
    Todo,
    InWork,
    Waiting,
    Done
}

public enum ReminderPreset
{
    KeepCurrent,
    None,
    In30Minutes,
    In1Hour,
    In2Hours,
    TomorrowMorning,
    RepeatEvery2Hours,
    RepeatDaily
}

public enum InWorkMode
{
    MultipleTasks,
    SingleTask
}

public enum OverlayMode
{
    AutoQuestTracker,
    CollapsedHandle,
    PinnedExpanded,
    Working
}

public sealed class OverlaySettings
{
    public const double DefaultWorkingIdleFontSize = 16;
    public const double DefaultWorkingActiveFontSize = 19;
    public const double MinimumWorkingFontSize = 12;
    public const double MaximumWorkingFontSize = 24;
    public const double DefaultWorkingWindowWidth = 320;
    public const double MinimumWorkingWindowWidth = 240;
    public const double MaximumWorkingWindowWidth = 600;
    public const double DefaultWorkingWindowHeight = 240;
    public const double MinimumWorkingWindowHeight = 120;
    public const double MaximumWorkingWindowHeight = 800;

    public int ActiveToPassiveDelayMilliseconds { get; set; } = 500;
    public bool AlwaysOnTop { get; set; } = true;
    public double WorkingIdleFontSize { get; set; } = DefaultWorkingIdleFontSize;
    public double WorkingActiveFontSize { get; set; } = DefaultWorkingActiveFontSize;

    [JsonPropertyName("workingFontSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyWorkingFontSize { get; set; }

    public double WorkingWindowWidth { get; set; } = DefaultWorkingWindowWidth;
    public double WorkingWindowHeight { get; set; } = DefaultWorkingWindowHeight;

    [JsonPropertyName("overlayMode")]
    public OverlayMode? StoredOverlayMode { get; set; }

    [JsonIgnore]
    public OverlayMode OverlayMode
    {
        get => StoredOverlayMode ??
               global::TaskOverlay.Core.OverlayMode.Working;
        set => StoredOverlayMode = value;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CollapsedMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PinnedActiveMode { get; set; }

    public InWorkMode InWorkMode { get; set; } = InWorkMode.MultipleTasks;
    public OverlayPanelFilter PanelFilter { get; set; } = OverlayPanelFilter.Panel;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WaitGroupExpanded { get; set; }
    public Guid? LastSelectedProjectId { get; set; }
    public bool MvpProjectsSeeded { get; set; }

    public bool NormalizeOverlayMode()
    {
        var normalizedMode = StoredOverlayMode ??
                             (PinnedActiveMode
                                 ? global::TaskOverlay.Core.OverlayMode.PinnedExpanded
                                 : CollapsedMode
                                     ? global::TaskOverlay.Core.OverlayMode.CollapsedHandle
                                     : global::TaskOverlay.Core.OverlayMode.Working);
        if (normalizedMode == global::TaskOverlay.Core.OverlayMode.AutoQuestTracker)
        {
            normalizedMode = global::TaskOverlay.Core.OverlayMode.Working;
        }

        var changed = StoredOverlayMode != normalizedMode ||
                      CollapsedMode ||
                      PinnedActiveMode;
        OverlayMode = normalizedMode;
        CollapsedMode = false;
        PinnedActiveMode = false;
        return changed;
    }

    public bool NormalizeWorkingPresentation()
    {
        var idleFontSize = ClampWorkingIdleFontSize(
            LegacyWorkingFontSize ?? WorkingIdleFontSize);
        var activeFontSize = ClampWorkingActiveFontSize(WorkingActiveFontSize);
        var windowWidth = ClampWorkingWindowWidth(WorkingWindowWidth);
        var windowHeight = ClampWorkingWindowHeight(WorkingWindowHeight);
        var changed = LegacyWorkingFontSize is not null ||
                      WorkingIdleFontSize != idleFontSize ||
                      WorkingActiveFontSize != activeFontSize ||
                      WorkingWindowWidth != windowWidth ||
                      WorkingWindowHeight != windowHeight;

        WorkingIdleFontSize = idleFontSize;
        WorkingActiveFontSize = activeFontSize;
        LegacyWorkingFontSize = null;
        WorkingWindowWidth = windowWidth;
        WorkingWindowHeight = windowHeight;
        return changed;
    }

    public bool NormalizePanelPresentation()
    {
        if (Enum.IsDefined(PanelFilter))
        {
            return false;
        }

        PanelFilter = OverlayPanelFilter.Panel;
        return true;
    }

    public static double ClampWorkingIdleFontSize(double value)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, MinimumWorkingFontSize, MaximumWorkingFontSize)
            : DefaultWorkingIdleFontSize;
    }

    public static double ClampWorkingActiveFontSize(double value)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, MinimumWorkingFontSize, MaximumWorkingFontSize)
            : DefaultWorkingActiveFontSize;
    }

    public static double ClampWorkingWindowWidth(double value)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, MinimumWorkingWindowWidth, MaximumWorkingWindowWidth)
            : DefaultWorkingWindowWidth;
    }

    public static double ClampWorkingWindowHeight(double value)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, MinimumWorkingWindowHeight, MaximumWorkingWindowHeight)
            : DefaultWorkingWindowHeight;
    }
}

public sealed class WindowPlacement
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? CollapsedLeft { get; set; }
    public double? CollapsedTop { get; set; }
    public UtilityShellPlacementState? UtilityShellPlacement { get; set; }
}

public sealed class UtilityShellPlacementState
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}

public sealed class TreeManagerSettings
{
    public Guid? SelectedProjectId { get; set; }
    public Guid? SelectedNodeId { get; set; }
    public List<Guid> ExpandedNodeIds { get; set; } = new();
    public TreeManagerFilter Filter { get; set; } = TreeManagerFilter.All;
    public TreeManagerView ActiveView { get; set; } = TreeManagerView.Tree;
    public TreeManagerStatusFilter StatusFilter { get; set; } = TreeManagerStatusFilter.All;
    public bool ExpansionInitialized { get; set; }
}
