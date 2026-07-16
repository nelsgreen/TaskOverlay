using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public sealed record ProposedActionOverride(
    Guid ActionId,
    string? Title = null,
    Guid? ProjectId = null,
    TaskStatus? Status = null,
    string? WaitingFor = null,
    DateTimeOffset? DeadlineAtUtc = null,
    DateTimeOffset? ReminderAtUtc = null);

public sealed record ProposedActionApplyResult(
    IReadOnlyList<Guid> AppliedActionIds,
    IReadOnlyList<Guid> CreatedTaskIds,
    IReadOnlyList<Guid> CreatedContextItemIds,
    IReadOnlyList<Guid> FailedActionIds);

public sealed class ProposedActionService
{
    private readonly AppState _state;

    public ProposedActionService(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _state.MeetingAnalyses ??= new List<MeetingAnalysis>();
    }

    public ProposedActionApplyResult Apply(
        Guid analysisId,
        IReadOnlyCollection<Guid> selectedActionIds,
        IReadOnlyCollection<ProposedActionOverride>? overrides = null,
        DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var analysis = _state.MeetingAnalyses.FirstOrDefault(item => item.Id == analysisId);
        if (analysis is null || selectedActionIds.Count == 0)
        {
            return new ProposedActionApplyResult(
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                selectedActionIds.ToList());
        }

        var selected = selectedActionIds.ToHashSet();
        var overrideById = (overrides ?? Array.Empty<ProposedActionOverride>())
            .GroupBy(item => item.ActionId)
            .ToDictionary(group => group.Key, group => group.Last());
        var applied = new List<Guid>();
        var createdTasks = new List<Guid>();
        var createdContextItems = new List<Guid>();
        var failed = new List<Guid>();

        foreach (var action in analysis.ProposedActions.Where(item => selected.Contains(item.Id)))
        {
            if (action.ReviewState == ProposedActionReviewState.Applied)
            {
                applied.Add(action.Id);
                continue;
            }

            overrideById.TryGetValue(action.Id, out var edit);
            if (TryApplyAction(analysis, action, edit, timestamp, out var taskId, out var contextId))
            {
                action.ReviewState = ProposedActionReviewState.Applied;
                action.AppliedTaskId = taskId;
                action.AppliedContextItemId = contextId;
                applied.Add(action.Id);
                if (taskId.HasValue)
                {
                    createdTasks.Add(taskId.Value);
                }

                if (contextId.HasValue)
                {
                    createdContextItems.Add(contextId.Value);
                }
            }
            else
            {
                action.ReviewState = ProposedActionReviewState.Failed;
                failed.Add(action.Id);
            }
        }

        analysis.State = failed.Count == 0 && analysis.ProposedActions.All(action =>
                action.ReviewState is ProposedActionReviewState.Applied or
                    ProposedActionReviewState.Rejected)
            ? MeetingAnalysisState.Applied
            : applied.Count > 0
                ? MeetingAnalysisState.PartiallyApplied
                : analysis.State;
        analysis.UpdatedAtUtc = timestamp;
        return new ProposedActionApplyResult(applied, createdTasks, createdContextItems, failed);
    }

    public bool Reject(Guid analysisId, Guid actionId, DateTimeOffset? now = null)
    {
        var analysis = _state.MeetingAnalyses.FirstOrDefault(item => item.Id == analysisId);
        var action = analysis?.ProposedActions.FirstOrDefault(item => item.Id == actionId);
        if (analysis is null || action is null || action.ReviewState == ProposedActionReviewState.Applied)
        {
            return false;
        }

        action.ReviewState = ProposedActionReviewState.Rejected;
        analysis.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        if (analysis.ProposedActions.All(item =>
                item.ReviewState is ProposedActionReviewState.Applied or
                    ProposedActionReviewState.Rejected))
        {
            analysis.State = MeetingAnalysisState.Applied;
        }

        return true;
    }

    private bool TryApplyAction(
        MeetingAnalysis analysis,
        ProposedAction action,
        ProposedActionOverride? edit,
        DateTimeOffset timestamp,
        out Guid? taskId,
        out Guid? contextItemId)
    {
        taskId = null;
        contextItemId = null;
        var title = (edit?.Title ?? action.Title).Trim();
        if (title.Length == 0 || title.Length > WorkspaceCommandProcessor.MaximumTitleLength)
        {
            return false;
        }

        var projectId = ResolveProjectId(edit?.ProjectId ?? action.ProposedProjectId, analysis.MeetId);
        if (!projectId.HasValue)
        {
            return false;
        }

        if (action.Type == ProposedActionType.AddMeetingContextNote)
        {
            var context = new ContextService(_state).CreateItem(
                new ContextItemUpdate(
                    projectId.Value,
                    ContextItemType.Note,
                    ContextItemStatus.Active,
                    title,
                    action.SourceExcerpt,
                    Array.Empty<Guid>(),
                    Array.Empty<Guid>(),
                    analysis.MeetId.HasValue
                        ? new[] { analysis.MeetId.Value }
                        : Array.Empty<Guid>()),
                timestamp);
            contextItemId = context?.Id;
            return context is not null;
        }

        var created = new TreeStateService(_state).CreateTask(projectId.Value, title, timestamp);
        if (created is null)
        {
            return false;
        }

        var task = _state.Tasks.First(item => item.Id == created.Id);
        var desiredStatus = edit?.Status ?? action.ProposedStatus;
        if (action.Type == ProposedActionType.CreateWaitingTask)
        {
            desiredStatus = TaskStatus.Waiting;
        }

        TaskInteractionService.SetStatus(_state, task, desiredStatus, timestamp);
        task.WaitingFor = (edit?.WaitingFor ?? action.WaitingFor).Trim();
        task.DueAtUtc = edit?.DeadlineAtUtc ?? action.DeadlineAtUtc;
        var reminderAt = edit?.ReminderAtUtc ?? action.ReminderAtUtc;
        if (reminderAt.HasValue)
        {
            ReminderService.SetSchedule(task, reminderAt.Value, null, timestamp);
        }

        task.SourceReferences ??= new List<TaskSourceReference>();
        task.SourceReferences.Add(new TaskSourceReference
        {
            MeetId = analysis.MeetId,
            RecordingId = analysis.RecordingId,
            AnalysisId = analysis.Id,
            ProposedActionId = action.Id,
            SegmentStartSeconds = action.SourceSegmentStart,
            SegmentEndSeconds = action.SourceSegmentEnd,
            Excerpt = action.SourceExcerpt
        });
        task.UpdatedAtUtc = timestamp;
        taskId = task.Id;
        return true;
    }

    private Guid? ResolveProjectId(Guid? proposedProjectId, Guid? meetId)
    {
        if (proposedProjectId.HasValue &&
            _state.Projects.Any(project => project.Id == proposedProjectId.Value))
        {
            return proposedProjectId;
        }

        if (meetId.HasValue)
        {
            var meetingProjectId = _state.Meetings
                .FirstOrDefault(meeting => meeting.Id == meetId.Value)
                ?.ProjectId;
            if (meetingProjectId.HasValue &&
                _state.Projects.Any(project => project.Id == meetingProjectId.Value))
            {
                return meetingProjectId;
            }
        }

        return _state.Projects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.CreatedAtUtc)
            .Select(project => (Guid?)project.Id)
            .FirstOrDefault();
    }
}
