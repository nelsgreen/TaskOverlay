using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public sealed record WorkspaceSnapshot(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Mode,
    IReadOnlyList<WorkspaceProjectSnapshot> Projects,
    IReadOnlyList<WorkspaceSectionSnapshot> Sections,
    IReadOnlyList<WorkspaceTaskSnapshot> Tasks,
    IReadOnlyList<WorkspaceTaskWorkSessionSnapshot> TaskWorkSessions,
    IReadOnlyList<WorkspaceMeetingSnapshot> Meetings,
    IReadOnlyList<WorkspaceMeetingRecordingSnapshot> MeetingRecordings,
    IReadOnlyList<WorkspaceMeetingAnalysisSnapshot> MeetingAnalyses,
    string? ActiveMeetingRecordingId,
    IReadOnlyList<WorkspaceContextSourceSnapshot> ContextSources,
    IReadOnlyList<WorkspaceContextItemSnapshot> ContextItems,
    IReadOnlyList<WorkspaceActiveNowSnapshot> ActiveNow,
    IReadOnlyList<WorkspaceTimelineItemSnapshot> TimelineItems,
    WorkspaceContextSnapshot Context);

public sealed record WorkspaceContextSnapshot(
    string ActiveTab,
    IReadOnlyList<string> SelectedProjectIds,
    string? SelectedTaskId,
    string? SelectedTimelineItemId,
    string? SelectedWorkstreamId,
    string Filter,
    bool ActiveNowCollapsed);

public sealed record WorkspaceProjectSnapshot(
    string Id,
    string Name,
    string Color,
    int SortOrder);

public sealed record WorkspaceSectionSnapshot(
    string Id,
    string ProjectId,
    string Name,
    int SortOrder,
    bool IsProjectRoot);

public sealed record WorkspaceTaskSnapshot(
    string Id,
    string ProjectId,
    string SectionId,
    string? ParentId,
    string Title,
    string Description,
    string Status,
    string WaitingFor,
    bool PinToPanel,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ReminderAtUtc,
    int? ReminderEveryMinutes,
    bool ReminderActive,
    DateTimeOffset? DeadlineAtUtc,
    IReadOnlyList<WorkspaceCheckpointSnapshot>? Checkpoints = null);

public sealed record WorkspaceTaskWorkSessionSnapshot(
    string Id,
    string TaskId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Note,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string TaskTitle,
    string TaskStatus,
    string ProjectId,
    string SectionId,
    string ProjectColor);

public sealed record WorkspaceCheckpointSnapshot(
    string Id,
    string Title,
    bool Done,
    int SortOrder,
    DateTimeOffset? CompletedAtUtc);

public sealed record WorkspaceMeetingSnapshot(
    string Id,
    string ProjectId,
    string Title,
    string Notes,
    DateTimeOffset StartsAtUtc,
    int DurationMinutes,
    string Location,
    string Link,
    string? LinkedTaskId,
    string RecordingPolicy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceMeetingRecordingSnapshot(
    string Id,
    string? MeetingId,
    string SourceKind,
    string State,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    string SystemAudioHealth,
    string MicrophoneHealth,
    bool KeepLocalOnly,
    bool PlannedEndPassed,
    bool HasSystemAudio,
    bool HasMicrophoneAudio,
    bool HasMixedAudio,
    bool HasTranscript,
    bool HasAnalysis,
    string TranscriptText,
    string LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceMeetingAnalysisSnapshot(
    string Id,
    string RecordingId,
    string? MeetingId,
    string State,
    string Provider,
    string Model,
    string Summary,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<string> MyActionItems,
    IReadOnlyList<string> OtherPeopleActionItems,
    IReadOnlyList<string> WaitingFor,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> QuestionsToClarify,
    IReadOnlyList<string> Deadlines,
    IReadOnlyList<WorkspaceMeetingSourceReferenceSnapshot> KeyQuotesOrSourceReferences,
    IReadOnlyList<WorkspaceProposedActionSnapshot> ProposedActions,
    string LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceMeetingSourceReferenceSnapshot(
    double? StartSeconds,
    double? EndSeconds,
    string Excerpt);

public sealed record WorkspaceProposedActionSnapshot(
    string Id,
    string Type,
    string Title,
    string? ProposedProjectId,
    string ProjectSuggestion,
    string ProposedStatus,
    string WaitingFor,
    DateTimeOffset? DeadlineAtUtc,
    DateTimeOffset? ReminderAtUtc,
    double? SourceSegmentStart,
    double? SourceSegmentEnd,
    string SourceExcerpt,
    double Confidence,
    string Rationale,
    string ReviewState,
    string? AppliedTaskId,
    string? AppliedContextItemId);

public sealed record WorkspaceContextSourceSnapshot(
    string Id,
    string ProjectId,
    string SourceType,
    string? SourceApp,
    string Title,
    string Body,
    string Summary,
    DateTimeOffset SourceDateUtc,
    IReadOnlyList<string> LinkedTaskIds,
    IReadOnlyList<string> LinkedMeetingIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceContextItemSnapshot(
    string Id,
    string ProjectId,
    string ItemType,
    string Status,
    string Title,
    string Body,
    IReadOnlyList<string> SourceDocumentIds,
    IReadOnlyList<string> LinkedTaskIds,
    IReadOnlyList<string> LinkedMeetingIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

public sealed record WorkspaceActiveNowSnapshot(
    string TaskId,
    string Kind);

public sealed record WorkspaceTimelineItemSnapshot(
    string Id,
    string Kind,
    string Title,
    string ProjectId,
    string ProjectPath,
    string? LinkedTaskId,
    string? LinkedMeetingId,
    DateTimeOffset OccursAtUtc,
    string? Meta);

public static class WorkspaceSnapshotFactory
{
    public const int CurrentSchemaVersion = 3;
    public const string ReadOnlyMode = "readonly";
    public const string ConnectedMode = "connected";

    public static WorkspaceSnapshot Create(
        AppState state,
        DateTimeOffset? now = null,
        string mode = ReadOnlyMode,
        Func<MeetingRecording, string?>? transcriptLoader = null,
        Guid? activeMeetingRecordingId = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var sourceProjects = state.Projects ?? new List<ProjectItem>();
        var sourceGroups = state.Groups ?? new List<GroupItem>();
        var sourceTasks = state.Tasks ?? new List<TaskItem>();
        var sourceTaskWorkSessions = state.TaskWorkSessions ?? new List<TaskWorkSession>();
        var sourceMeetings = state.Meetings ?? new List<MeetingItem>();
        var projects = sourceProjects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.CreatedAtUtc)
            .Select(ToProjectSnapshot)
            .ToList();

        var fallbackProject = sourceProjects.FirstOrDefault(project =>
                                  string.Equals(
                                      project.Name,
                                      ProjectItem.DefaultName,
                                      StringComparison.OrdinalIgnoreCase)) ??
                              sourceProjects
                                  .OrderBy(project => project.SortOrder)
                                  .ThenBy(project => project.CreatedAtUtc)
                                  .FirstOrDefault();
        if (fallbackProject is null)
        {
            fallbackProject = new ProjectItem
            {
                Id = Guid.Empty,
                Name = ProjectItem.DefaultName,
                ColorHex = ProjectColorPalette.Default,
                CreatedAtUtc = timestamp,
                UpdatedAtUtc = timestamp
            };
            projects.Add(ToProjectSnapshot(fallbackProject));
        }

        var projectById = sourceProjects.ToDictionary(project => project.Id);
        projectById[fallbackProject.Id] = fallbackProject;
        var groupById = sourceGroups
            .GroupBy(group => group.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var taskIds = sourceTasks.Select(task => task.Id).ToHashSet();

        var sections = sourceGroups
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.CreatedAtUtc)
            .Select(group =>
            {
                var project = ResolveProject(projectById, fallbackProject, group.ProjectId);
                return new WorkspaceSectionSnapshot(
                    GroupSectionId(group.Id),
                    FormatId(project.Id),
                    group.Name,
                    group.SortOrder,
                    IsProjectRoot: false);
            })
            .ToList();

        foreach (var project in projects)
        {
            sections.Add(new WorkspaceSectionSnapshot(
                RootSectionId(project.Id),
                project.Id,
                "Project root",
                int.MinValue,
                IsProjectRoot: true));
        }

        var taskContexts = sourceTasks
            .Select(task => CreateTaskContext(
                task,
                projectById,
                fallbackProject,
                groupById,
                taskIds,
                timestamp))
            .OrderBy(context => context.Snapshot.ProjectId)
            .ThenBy(context => context.Snapshot.SectionId)
            .ThenBy(context => context.Snapshot.SortOrder)
            .ThenBy(context => context.Snapshot.CreatedAtUtc)
            .ToList();
        var tasks = taskContexts.Select(context => context.Snapshot).ToList();
        var taskContextById = taskContexts.ToDictionary(context => context.Source.Id);
        var taskWorkSessions = sourceTaskWorkSessions
            .Where(session =>
                taskContextById.ContainsKey(session.TaskId) &&
                TaskWorkSessionService.IsValidRange(session.StartUtc, session.EndUtc))
            .OrderBy(session => session.StartUtc)
            .ThenBy(session => session.EndUtc)
            .ThenBy(session => session.Id)
            .Select(session =>
            {
                var taskContext = taskContextById[session.TaskId];
                var project = ResolveProject(
                    projectById,
                    fallbackProject,
                    taskContext.Source.ProjectId);
                return new WorkspaceTaskWorkSessionSnapshot(
                    FormatId(session.Id),
                    FormatId(session.TaskId),
                    session.StartUtc,
                    session.EndUtc,
                    session.Note,
                    session.CreatedAtUtc,
                    session.UpdatedAtUtc,
                    taskContext.Snapshot.Title,
                    taskContext.Snapshot.Status,
                    taskContext.Snapshot.ProjectId,
                    taskContext.Snapshot.SectionId,
                    project.ColorHex);
            })
            .ToList();
        var meetings = sourceMeetings
            .Where(meeting => projectById.ContainsKey(meeting.ProjectId))
            .OrderBy(meeting => meeting.StartsAtUtc)
            .ThenBy(meeting => meeting.CreatedAtUtc)
            .ThenBy(meeting => meeting.Id)
            .Select(meeting => new WorkspaceMeetingSnapshot(
                FormatId(meeting.Id),
                FormatId(meeting.ProjectId),
                meeting.Title,
                meeting.Notes,
                meeting.StartsAtUtc,
                meeting.DurationMinutes,
                meeting.Location,
                meeting.Link,
                meeting.LinkedTaskId is Guid linkedTaskId && taskIds.Contains(linkedTaskId)
                    ? FormatId(linkedTaskId)
                    : null,
                meeting.RecordingPolicy.ToString(),
                meeting.CreatedAtUtc,
                meeting.UpdatedAtUtc))
            .ToList();

        var meetingById = sourceMeetings.ToDictionary(meeting => meeting.Id);
        var sourceRecordings = state.MeetingRecordings ?? new List<MeetingRecording>();
        var runtimeActiveRecordingId = activeMeetingRecordingId is Guid runtimeId &&
                                       sourceRecordings.Any(recording => recording.Id == runtimeId)
            ? runtimeId
            : (Guid?)null;
        var recordings = sourceRecordings
            .OrderByDescending(recording => recording.StartedAtUtc ?? recording.CreatedAtUtc)
            .ThenBy(recording => recording.Id)
            .Select(recording =>
            {
                var plannedEndPassed = recording.Id == runtimeActiveRecordingId &&
                                       recording.MeetId is Guid recordingMeetId &&
                                       meetingById.TryGetValue(recordingMeetId, out var ownerMeeting) &&
                                       timestamp >= ownerMeeting.StartsAtUtc.AddMinutes(
                                           ownerMeeting.DurationMinutes);
                return new WorkspaceMeetingRecordingSnapshot(
                    FormatId(recording.Id),
                    recording.MeetId is Guid meetId ? FormatId(meetId) : null,
                    recording.SourceKind.ToString(),
                    recording.State.ToString(),
                    recording.StartedAtUtc,
                    recording.StoppedAtUtc,
                    recording.SystemAudioHealth.ToString(),
                    recording.MicrophoneHealth.ToString(),
                    recording.KeepLocalOnly,
                    plannedEndPassed,
                    recording.SystemAudioFile.Length > 0,
                    recording.MicrophoneFile.Length > 0,
                    recording.MixedAudioFile.Length > 0,
                    recording.TranscriptFile.Length > 0,
                    recording.AnalysisFile.Length > 0,
                    transcriptLoader?.Invoke(recording) ?? string.Empty,
                    recording.LastError,
                    recording.CreatedAtUtc,
                    recording.UpdatedAtUtc);
            })
            .ToList();
        var recordingIdSet = sourceRecordings.Select(recording => recording.Id).ToHashSet();
        var analyses = (state.MeetingAnalyses ?? new List<MeetingAnalysis>())
            .Where(analysis => recordingIdSet.Contains(analysis.RecordingId))
            .OrderByDescending(analysis => analysis.UpdatedAtUtc)
            .ThenBy(analysis => analysis.Id)
            .Select(analysis => new WorkspaceMeetingAnalysisSnapshot(
                FormatId(analysis.Id),
                FormatId(analysis.RecordingId),
                analysis.MeetId is Guid meetId ? FormatId(meetId) : null,
                analysis.State.ToString(),
                analysis.Provider,
                analysis.Model,
                analysis.Summary,
                analysis.Decisions,
                analysis.MyActionItems,
                analysis.OtherPeopleActionItems,
                analysis.WaitingFor,
                analysis.Risks,
                analysis.QuestionsToClarify,
                analysis.Deadlines,
                analysis.KeyQuotesOrSourceReferences.Select(reference =>
                    new WorkspaceMeetingSourceReferenceSnapshot(
                        reference.StartSeconds,
                        reference.EndSeconds,
                        reference.Excerpt)).ToList(),
                analysis.ProposedActions.Select(action =>
                    new WorkspaceProposedActionSnapshot(
                        FormatId(action.Id),
                        action.Type.ToString(),
                        action.Title,
                        action.ProposedProjectId is Guid projectId &&
                        projectById.ContainsKey(projectId)
                            ? FormatId(projectId)
                            : null,
                        action.ProjectSuggestion,
                        ToStatus(action.ProposedStatus),
                        action.WaitingFor,
                        action.DeadlineAtUtc,
                        action.ReminderAtUtc,
                        action.SourceSegmentStart,
                        action.SourceSegmentEnd,
                        action.SourceExcerpt,
                        action.Confidence,
                        action.Rationale,
                        action.ReviewState.ToString(),
                        action.AppliedTaskId is Guid taskId && taskIds.Contains(taskId)
                            ? FormatId(taskId)
                            : null,
                        action.AppliedContextItemId is Guid contextId
                            ? FormatId(contextId)
                            : null)).ToList(),
                analysis.LastError,
                analysis.CreatedAtUtc,
                analysis.UpdatedAtUtc))
            .ToList();

        // ContextHUB: links to deleted tasks/meetings/sources are filtered out of
        // the snapshot defensively; repair removes them from state on load/save.
        var meetingIdSet = sourceMeetings.Select(meeting => meeting.Id).ToHashSet();
        var sourceContextSources = state.ContextSources ?? new List<SourceDocument>();
        var sourceContextItems = state.ContextItems ?? new List<ContextItem>();
        var contextSourceIdSet = sourceContextSources.Select(source => source.Id).ToHashSet();
        var contextSources = sourceContextSources
            .Where(source => projectById.ContainsKey(source.ProjectId))
            .OrderByDescending(source => source.SourceDateUtc)
            .ThenByDescending(source => source.CreatedAtUtc)
            .ThenBy(source => source.Id)
            .Select(source => new WorkspaceContextSourceSnapshot(
                FormatId(source.Id),
                FormatId(source.ProjectId),
                ToContextSourceType(source.SourceType),
                source.SourceApp is { } app ? ToContextSourceApp(app) : null,
                source.Title,
                source.Body,
                source.Summary,
                source.SourceDateUtc,
                FormatLinkedIds(source.LinkedTaskIds, taskIds),
                FormatLinkedIds(source.LinkedMeetingIds, meetingIdSet),
                source.CreatedAtUtc,
                source.UpdatedAtUtc))
            .ToList();
        var contextItems = sourceContextItems
            .Where(item => projectById.ContainsKey(item.ProjectId))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => new WorkspaceContextItemSnapshot(
                FormatId(item.Id),
                FormatId(item.ProjectId),
                ToContextItemType(item.ItemType),
                ToContextItemStatus(item.Status),
                item.Title,
                item.Body,
                FormatLinkedIds(item.SourceDocumentIds, contextSourceIdSet),
                FormatLinkedIds(item.LinkedTaskIds, taskIds),
                FormatLinkedIds(item.LinkedMeetingIds, meetingIdSet),
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                item.ResolvedAtUtc))
            .ToList();

        var activeNow = taskContexts
            .Where(context =>
                context.Source.Status == TaskStatus.InWork ||
                context.Snapshot.ReminderActive)
            .Select(context => new WorkspaceActiveNowSnapshot(
                context.Snapshot.Id,
                context.Snapshot.ReminderActive ? "REMIND" : "FOCUS"))
            .ToList();

        var timelineItems = taskContexts
            .SelectMany(CreateTimelineItems)
            .Concat(meetings.Select(meeting => new WorkspaceTimelineItemSnapshot(
                $"meet:{meeting.Id}",
                "MEET",
                meeting.Title,
                meeting.ProjectId,
                projectById.TryGetValue(Guid.Parse(meeting.ProjectId), out var meetingProject)
                    ? meetingProject.Name
                    : ProjectItem.DefaultName,
                meeting.LinkedTaskId,
                meeting.Id,
                meeting.StartsAtUtc,
                FormatMeetingMeta(meeting))))
            .OrderBy(item => item.OccursAtUtc)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        var settings = state.WorkspaceSettings ?? new WorkspaceSettings();
        var context = new WorkspaceContextSnapshot(
            ToWorkspaceTab(settings.ActiveTab),
            settings.SelectedProjectIds?
                .Select(FormatId)
                .ToList() ?? new List<string>(),
            settings.SelectedTaskId is Guid selectedTaskId
                ? FormatId(selectedTaskId)
                : null,
            settings.SelectedTimelineItemId,
            settings.SelectedWorkstreamId,
            ToWorkspaceFilter(settings.Filter),
            settings.ActiveNowCollapsed);

        return new WorkspaceSnapshot(
            CurrentSchemaVersion,
            timestamp,
            mode == ConnectedMode ? ConnectedMode : ReadOnlyMode,
            projects,
            sections,
            tasks,
            taskWorkSessions,
            meetings,
            recordings,
            analyses,
            runtimeActiveRecordingId is Guid recordingId
                ? FormatId(recordingId)
                : null,
            contextSources,
            contextItems,
            activeNow,
            timelineItems,
            context);
    }

    private static WorkspaceProjectSnapshot ToProjectSnapshot(ProjectItem project) =>
        new(
            FormatId(project.Id),
            string.IsNullOrWhiteSpace(project.Name)
                ? ProjectItem.DefaultName
                : project.Name,
            string.IsNullOrWhiteSpace(project.ColorHex)
                ? ProjectColorPalette.Default
                : project.ColorHex,
            project.SortOrder);

    private static TaskContext CreateTaskContext(
        TaskItem task,
        IReadOnlyDictionary<Guid, ProjectItem> projectById,
        ProjectItem fallbackProject,
        IReadOnlyDictionary<Guid, GroupItem> groupById,
        IReadOnlySet<Guid> taskIds,
        DateTimeOffset timestamp)
    {
        var project = ResolveProject(projectById, fallbackProject, task.ProjectId);
        var group = task.GroupId is Guid groupId &&
                    groupById.TryGetValue(groupId, out var referencedGroup) &&
                    referencedGroup.ProjectId == project.Id
            ? referencedGroup
            : null;
        var reminderAt = task.ReminderSnoozedUntilUtc ??
                         task.RemindAtUtc ??
                         (task.ReminderActive ? task.LastReminderAtUtc : null);
        var reminderActive = reminderAt is not null &&
                             ReminderAttentionService.ShouldShowNotification(task, timestamp);
        var projectId = FormatId(project.Id);
        var snapshot = new WorkspaceTaskSnapshot(
            FormatId(task.Id),
            projectId,
            group is null ? RootSectionId(projectId) : GroupSectionId(group.Id),
            task.ParentTaskId is Guid parentId &&
            parentId != task.Id &&
            taskIds.Contains(parentId)
                ? FormatId(parentId)
                : null,
            task.Title,
            task.Description,
            ToStatus(task.Status),
            task.WaitingFor,
            task.PinToPanel,
            task.SortOrder,
            task.CreatedAtUtc,
            task.UpdatedAtUtc,
            reminderAt,
            task.RemindEveryMinutes,
            reminderActive,
            task.DueAtUtc,
            ToCheckpointSnapshots(task.Checkpoints));
        var projectPath = group is null
            ? project.Name
            : $"{project.Name} / {group.Name}";
        return new TaskContext(task, snapshot, projectPath);
    }

    private static IEnumerable<WorkspaceTimelineItemSnapshot> CreateTimelineItems(
        TaskContext context)
    {
        if (context.Snapshot.ReminderAtUtc is DateTimeOffset reminderAt)
        {
            yield return new WorkspaceTimelineItemSnapshot(
                $"remind:{context.Snapshot.Id}",
                "REMIND",
                context.Snapshot.Title,
                context.Snapshot.ProjectId,
                context.ProjectPath,
                context.Snapshot.Id,
                null,
                reminderAt,
                context.Snapshot.ReminderEveryMinutes is > 0
                    ? $"Repeats every {context.Snapshot.ReminderEveryMinutes}m"
                    : "Task reminder");
        }

        if (context.Snapshot.DeadlineAtUtc is DateTimeOffset deadlineAt)
        {
            yield return new WorkspaceTimelineItemSnapshot(
                $"deadline:{context.Snapshot.Id}",
                "DEADLINE",
                context.Snapshot.Title,
                context.Snapshot.ProjectId,
                context.ProjectPath,
                context.Snapshot.Id,
                null,
                deadlineAt,
                "Task deadline");
        }
    }

    private static string FormatMeetingMeta(WorkspaceMeetingSnapshot meeting)
    {
        var duration = meeting.DurationMinutes % 60 == 0
            ? $"{meeting.DurationMinutes / 60}h"
            : $"{meeting.DurationMinutes}m";
        return string.IsNullOrWhiteSpace(meeting.Location)
            ? duration
            : $"{duration} · {meeting.Location}";
    }

    private static IReadOnlyList<WorkspaceCheckpointSnapshot> ToCheckpointSnapshots(
        List<CheckpointItem>? checkpoints) =>
        checkpoints is null
            ? Array.Empty<WorkspaceCheckpointSnapshot>()
            : checkpoints
                .Where(checkpoint => checkpoint is not null)
                .OrderBy(checkpoint => checkpoint.SortOrder)
                .ThenBy(checkpoint => checkpoint.CreatedAtUtc)
                .Select(checkpoint => new WorkspaceCheckpointSnapshot(
                    FormatId(checkpoint.Id),
                    checkpoint.Title,
                    checkpoint.Done,
                    checkpoint.SortOrder,
                    checkpoint.CompletedAtUtc))
                .ToList();

    private static ProjectItem ResolveProject(
        IReadOnlyDictionary<Guid, ProjectItem> projectById,
        ProjectItem fallbackProject,
        Guid? projectId) =>
        projectId is Guid id && projectById.TryGetValue(id, out var project)
            ? project
            : fallbackProject;

    private static string ToStatus(TaskStatus status) => status switch
    {
        TaskStatus.InWork => "FOCUS",
        TaskStatus.Waiting => "WAIT",
        TaskStatus.Done => "DONE",
        _ => "TODO"
    };

    private static string ToWorkspaceTab(WorkspaceTab tab) => tab switch
    {
        WorkspaceTab.Status => "status",
        WorkspaceTab.Timeline => "timeline",
        WorkspaceTab.Calendar => "calendar",
        WorkspaceTab.Workstreams => "workstreams",
        WorkspaceTab.ContextHub => "contexthub",
        _ => "tree"
    };

    private static IReadOnlyList<string> FormatLinkedIds(
        List<Guid>? ids,
        IReadOnlySet<Guid> existing) =>
        ids is null
            ? Array.Empty<string>()
            : ids.Where(existing.Contains).Select(FormatId).ToList();

    private static string ToContextSourceType(ContextSourceType type) => type switch
    {
        ContextSourceType.MeetingSummary => "meetingSummary",
        ContextSourceType.MeetingTranscript => "meetingTranscript",
        ContextSourceType.ChatSummary => "chatSummary",
        ContextSourceType.ManualNote => "manualNote",
        ContextSourceType.ClientRequest => "clientRequest",
        ContextSourceType.DocumentSummary => "documentSummary",
        ContextSourceType.StatusUpdate => "statusUpdate",
        ContextSourceType.TelegramCapture => "telegramCapture",
        _ => "other"
    };

    private static string ToContextSourceApp(ContextSourceApp app) => app switch
    {
        ContextSourceApp.ChatGpt => "chatgpt",
        ContextSourceApp.Claude => "claude",
        ContextSourceApp.Codex => "codex",
        ContextSourceApp.Telegram => "telegram",
        ContextSourceApp.Manual => "manual",
        _ => "other"
    };

    private static string ToContextItemType(ContextItemType type) => type switch
    {
        ContextItemType.Decision => "decision",
        ContextItemType.Requirement => "requirement",
        ContextItemType.Constraint => "constraint",
        ContextItemType.Blocker => "blocker",
        ContextItemType.OpenQuestion => "openQuestion",
        ContextItemType.ActionItem => "actionItem",
        ContextItemType.ProjectFact => "projectFact",
        ContextItemType.Risk => "risk",
        _ => "note"
    };

    private static string ToContextItemStatus(ContextItemStatus status) => status switch
    {
        ContextItemStatus.Resolved => "resolved",
        ContextItemStatus.Deprecated => "deprecated",
        ContextItemStatus.Superseded => "superseded",
        _ => "active"
    };

    private static string ToWorkspaceFilter(WorkspaceFilter filter) => filter switch
    {
        WorkspaceFilter.Active => "active",
        WorkspaceFilter.ActivePath => "active-path",
        _ => "all"
    };

    private static string RootSectionId(string projectId) =>
        $"project:{projectId}:root";

    private static string GroupSectionId(Guid groupId) =>
        $"group:{FormatId(groupId)}";

    private static string FormatId(Guid id) => id.ToString("N");

    private sealed record TaskContext(
        TaskItem Source,
        WorkspaceTaskSnapshot Snapshot,
        string ProjectPath);
}
