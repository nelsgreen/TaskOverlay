using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TaskOverlay.Core;

public sealed class AppState
{
    public const int CurrentSchemaVersion = 7;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<TaskItem> Tasks { get; set; } = new();
    public List<ProjectItem> Projects { get; set; } = new();
    public List<GroupItem> Groups { get; set; } = new();
    public List<MeetingItem> Meetings { get; set; } = new();
    public List<TaskWorkSession> TaskWorkSessions { get; set; } = new();
    public List<SourceDocument> ContextSources { get; set; } = new();
    public List<ContextItem> ContextItems { get; set; } = new();
    public List<MeetingRecording> MeetingRecordings { get; set; } = new();
    public List<MeetingTranscript> MeetingTranscripts { get; set; } = new();
    public List<MeetingScreenshot> MeetingScreenshots { get; set; } = new();
    public List<MeetingAnalysis> MeetingAnalyses { get; set; } = new();
    public OverlaySettings OverlaySettings { get; set; } = new();
    public WindowPlacement WindowPlacement { get; set; } = new();
    public TreeManagerSettings TreeManagerSettings { get; set; } = new();
    public WorkspaceSettings WorkspaceSettings { get; set; } = new();
    public TelegramCaptureSettings TelegramCapture { get; set; } = new();
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
            WorkspaceSettings = new WorkspaceSettings
            {
                SelectedProjectIds = { defaultProject.Id }
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

public sealed class MeetingItem
{
    public const int DefaultDurationMinutes = 30;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool TitleIsGenerated { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset StartsAtUtc { get; set; }
    public int DurationMinutes { get; set; } = DefaultDurationMinutes;
    public string Location { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public Guid? LinkedTaskId { get; set; }
    public Guid? ActiveTranscriptId { get; set; }
    public MeetingRecordingPolicy RecordingPolicy { get; set; } =
        MeetingRecordingPolicy.Inherit;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One calendar placement for a logical task. Work sessions carry only time
/// allocation metadata; task status, reminders, deadlines, and panel
/// visibility remain owned by <see cref="TaskItem"/>.
/// </summary>
public sealed class TaskWorkSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// ContextHUB source: raw captured/imported text with provenance (meeting
/// notes, chat summary, client request, Telegram capture, ...). It is the
/// "where did this come from" layer of project memory. Links to tasks and
/// meetings are navigation pointers only; the reverse "context items derived
/// from this source" list is derived in snapshot/UI from
/// <see cref="ContextItem.SourceDocumentIds"/> and never stored here.
/// </summary>
public sealed class SourceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public ContextSourceType SourceType { get; set; } = ContextSourceType.ManualNote;
    /// <summary>Optional provenance app (ChatGPT/Claude/Codex/Telegram/...).</summary>
    public ContextSourceApp? SourceApp { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Raw pasted text. Bounded; large transcripts move to external files later.</summary>
    public string Body { get; set; } = string.Empty;
    /// <summary>Optional one-line recap for scanning.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>When the source content actually happened (meeting date etc.).</summary>
    public DateTimeOffset SourceDateUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<Guid> LinkedTaskIds { get; set; } = new();
    public List<Guid> LinkedMeetingIds { get; set; } = new();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// ContextHUB item: the durable unit of project memory (decision, blocker,
/// open question, requirement, ...). It can be derived from source documents
/// but exists independently: deleting a source clears the reference, never the
/// item. Not a task: no TODO/FOCUS/WAIT/DONE, no REMIND, no DEADLINE.
/// </summary>
public sealed class ContextItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public ContextItemType ItemType { get; set; } = ContextItemType.Note;
    public ContextItemStatus Status { get; set; } = ContextItemStatus.Active;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>Sources this item was derived from. Single direction of truth;
    /// the source side never stores a reverse list.</summary>
    public List<Guid> SourceDocumentIds { get; set; } = new();
    public List<Guid> LinkedTaskIds { get; set; } = new();
    public List<Guid> LinkedMeetingIds { get; set; } = new();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Set when the item leaves Active; cleared when it returns to Active.</summary>
    public DateTimeOffset? ResolvedAtUtc { get; set; }
}

public enum ContextSourceType
{
    MeetingSummary,
    MeetingTranscript,
    ChatSummary,
    ManualNote,
    ClientRequest,
    DocumentSummary,
    StatusUpdate,
    TelegramCapture,
    Other
}

public enum ContextSourceApp
{
    ChatGpt,
    Claude,
    Codex,
    Telegram,
    Manual,
    Other
}

public enum ContextItemType
{
    Decision,
    Requirement,
    Constraint,
    Blocker,
    OpenQuestion,
    ActionItem,
    ProjectFact,
    Risk,
    Note
}

public enum ContextItemStatus
{
    Active,
    Resolved,
    Deprecated,
    Superseded
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

    // Schema-2 compatibility only. StateMigrator converts this legacy single
    // placement to AppState.TaskWorkSessions and clears both fields.
    [JsonConverter(typeof(LegacyPlannedStartUtcConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? PlannedStartAtUtc { get; set; }

    [JsonConverter(typeof(LegacyPlannedDurationMinutesConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PlannedDurationMinutes { get; set; }

    // Lightweight execution steps ("Steps" in the UI). Null on states saved
    // before checkpoints existed — treat null and empty the same everywhere.
    // A checkpoint is deliberately NOT a task: no status/REMIND/DEADLINE/WAIT/
    // pin/priority of its own. Checkpoint state is independent of parent task
    // status; marking the parent DONE must not mutate checkpoint states.
    public List<CheckpointItem>? Checkpoints { get; set; }
    public List<TaskSourceReference>? SourceReferences { get; set; }

    public Guid? ProjectId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ParentTaskId { get; set; }
    // Connected Workspace task creation may persist an empty title briefly while
    // Details owns keyboard focus. Ordinary tasks still require a non-empty title.
    public bool IsDraft { get; set; }
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

/// <summary>
/// One lightweight execution step inside a task ("Steps" in the Workspace UI).
/// Intentionally minimal: title + done + order. Anything that needs its own
/// status/reminder/deadline/visibility should become a real child task later
/// (promotion is out of scope for the MVP).
/// </summary>
public sealed class CheckpointItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public bool Done { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
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

public sealed class WorkspaceSettings
{
    public WorkspaceTab ActiveTab { get; set; } = WorkspaceTab.Tree;
    public List<Guid> SelectedProjectIds { get; set; } = new();
    public Guid? SelectedTaskId { get; set; }
    public string? SelectedTimelineItemId { get; set; }
    public string? SelectedWorkstreamId { get; set; }
    public WorkspaceFilter Filter { get; set; } = WorkspaceFilter.All;
    public bool ActiveNowCollapsed { get; set; }
}

public enum WorkspaceTab
{
    Tree,
    Status,
    Timeline,
    Calendar,
    Workstreams,
    ContextHub
}

public enum WorkspaceFilter
{
    All,
    Active,
    ActivePath
}

public sealed class TelegramCaptureSettings
{
    public const int DefaultPollIntervalSeconds = 30;
    public const int MinimumPollIntervalSeconds = 5;
    public const int MaximumPollIntervalSeconds = 3600;

    public bool Enabled { get; set; }
    public string BotUsername { get; set; } = string.Empty;
    public long? AllowedUserId { get; set; }
    public Guid? DefaultProjectId { get; set; }
    public List<TelegramProjectAlias> ProjectAliases { get; set; } = new();
    public int PollIntervalSeconds { get; set; } = DefaultPollIntervalSeconds;
    /// <summary>Highest processed Telegram update_id; next poll uses offset = LastUpdateId + 1.</summary>
    public long LastUpdateId { get; set; }

    public bool Normalize(IReadOnlyCollection<ProjectItem> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var changed = false;
        var normalizedBotUsername = NormalizeBotUsername(BotUsername);
        if (BotUsername != normalizedBotUsername)
        {
            BotUsername = normalizedBotUsername;
            changed = true;
        }

        if (LastUpdateId < 0)
        {
            LastUpdateId = 0;
            changed = true;
        }

        var projectIds = projects.Select(project => project.Id).ToHashSet();
        if (DefaultProjectId.HasValue && !projectIds.Contains(DefaultProjectId.Value))
        {
            DefaultProjectId = null;
            changed = true;
        }

        var normalizedInterval = Math.Clamp(
            PollIntervalSeconds,
            MinimumPollIntervalSeconds,
            MaximumPollIntervalSeconds);
        if (PollIntervalSeconds != normalizedInterval)
        {
            PollIntervalSeconds = normalizedInterval;
            changed = true;
        }

        if (ProjectAliases is null)
        {
            ProjectAliases = new List<TelegramProjectAlias>();
            changed = true;
        }

        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedAliases = new List<TelegramProjectAlias>();
        foreach (var alias in ProjectAliases)
        {
            if (alias is null)
            {
                changed = true;
                continue;
            }

            var aliasChanged = alias.Normalize(projectIds);
            if (aliasChanged)
            {
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(alias.Alias) ||
                alias.ProjectId == Guid.Empty ||
                !projectIds.Contains(alias.ProjectId) ||
                !seenAliases.Add(alias.Alias))
            {
                changed = true;
                continue;
            }

            normalizedAliases.Add(alias);
        }

        if (!AliasesEqual(ProjectAliases, normalizedAliases))
        {
            ProjectAliases = normalizedAliases;
            changed = true;
        }

        return changed;
    }

    private static bool AliasesEqual(
        IReadOnlyList<TelegramProjectAlias> left,
        IReadOnlyList<TelegramProjectAlias> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(
                    left[i].Alias,
                    right[i].Alias,
                    StringComparison.Ordinal) ||
                left[i].ProjectId != right[i].ProjectId)
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeBotUsername(string? value)
    {
        var username = (value ?? string.Empty).Trim();
        return username.StartsWith("@", StringComparison.Ordinal)
            ? username[1..].Trim()
            : username;
    }
}

public sealed class TelegramProjectAlias
{
    public string Alias { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }

    public bool Normalize(IReadOnlyCollection<Guid> validProjectIds)
    {
        ArgumentNullException.ThrowIfNull(validProjectIds);

        var normalizedAlias = Alias?.Trim() ?? string.Empty;
        var changed = Alias != normalizedAlias;
        Alias = normalizedAlias;
        if (ProjectId == Guid.Empty || !validProjectIds.Contains(ProjectId))
        {
            return true;
        }

        return changed;
    }
}
