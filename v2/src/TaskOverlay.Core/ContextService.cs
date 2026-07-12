using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public sealed record SourceDocumentUpdate(
    Guid ProjectId,
    ContextSourceType SourceType,
    ContextSourceApp? SourceApp,
    string Title,
    string? Body,
    string? Summary,
    DateTimeOffset SourceDateUtc,
    IReadOnlyList<Guid> LinkedTaskIds,
    IReadOnlyList<Guid> LinkedMeetingIds);

public sealed record ContextItemUpdate(
    Guid ProjectId,
    ContextItemType ItemType,
    ContextItemStatus Status,
    string Title,
    string? Body,
    IReadOnlyList<Guid> SourceDocumentIds,
    IReadOnlyList<Guid> LinkedTaskIds,
    IReadOnlyList<Guid> LinkedMeetingIds);

/// <summary>
/// Domain mutations for ContextHUB (project memory): source documents and
/// context items. Follows the MeetingService pattern: all-or-nothing
/// normalization, navigation-pointer links, and explicit cascade cleanup so
/// deleting a task/meeting/source repairs links without destroying memory.
/// </summary>
public sealed class ContextService
{
    public const int MaximumTitleLength = 500;
    public const int MaximumBodyLength = 100_000;
    public const int MaximumSummaryLength = 2_000;

    private readonly AppState _state;

    public ContextService(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _state.ContextSources ??= new();
        _state.ContextItems ??= new();
    }

    public SourceDocument? CreateSource(SourceDocumentUpdate input, DateTimeOffset? now = null)
    {
        if (!TryNormalizeSource(input, out var normalized))
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var source = new SourceDocument
        {
            ProjectId = normalized.ProjectId,
            SourceType = normalized.SourceType,
            SourceApp = normalized.SourceApp,
            Title = normalized.Title,
            Body = normalized.Body,
            Summary = normalized.Summary,
            SourceDateUtc = normalized.SourceDateUtc,
            LinkedTaskIds = normalized.LinkedTaskIds.ToList(),
            LinkedMeetingIds = normalized.LinkedMeetingIds.ToList(),
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        _state.ContextSources.Add(source);
        return source;
    }

    public bool UpdateSource(Guid sourceId, SourceDocumentUpdate input, DateTimeOffset? now = null)
    {
        var source = _state.ContextSources.FirstOrDefault(item => item.Id == sourceId);
        if (source is null || !TryNormalizeSource(input, out var normalized))
        {
            return false;
        }

        source.ProjectId = normalized.ProjectId;
        source.SourceType = normalized.SourceType;
        source.SourceApp = normalized.SourceApp;
        source.Title = normalized.Title;
        source.Body = normalized.Body;
        source.Summary = normalized.Summary;
        source.SourceDateUtc = normalized.SourceDateUtc;
        source.LinkedTaskIds = normalized.LinkedTaskIds.ToList();
        source.LinkedMeetingIds = normalized.LinkedMeetingIds.ToList();
        source.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Deletes the source and clears its id from every context item's
    /// SourceDocumentIds. Context items themselves are never deleted here.
    /// </summary>
    public bool DeleteSource(Guid sourceId, DateTimeOffset? now = null)
    {
        if (_state.ContextSources.RemoveAll(item => item.Id == sourceId) == 0)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        foreach (var item in _state.ContextItems)
        {
            if (item.SourceDocumentIds.Remove(sourceId))
            {
                item.UpdatedAtUtc = timestamp;
            }
        }

        return true;
    }

    public ContextItem? CreateItem(ContextItemUpdate input, DateTimeOffset? now = null)
    {
        if (!TryNormalizeItem(input, out var normalized))
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var item = new ContextItem
        {
            ProjectId = normalized.ProjectId,
            ItemType = normalized.ItemType,
            Status = normalized.Status,
            Title = normalized.Title,
            Body = normalized.Body,
            SourceDocumentIds = normalized.SourceDocumentIds.ToList(),
            LinkedTaskIds = normalized.LinkedTaskIds.ToList(),
            LinkedMeetingIds = normalized.LinkedMeetingIds.ToList(),
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp,
            ResolvedAtUtc = normalized.Status == ContextItemStatus.Active ? null : timestamp
        };
        _state.ContextItems.Add(item);
        return item;
    }

    public bool UpdateItem(Guid itemId, ContextItemUpdate input, DateTimeOffset? now = null)
    {
        var item = _state.ContextItems.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null || !TryNormalizeItem(input, out var normalized))
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        if (item.Status != normalized.Status)
        {
            item.ResolvedAtUtc = normalized.Status == ContextItemStatus.Active
                ? null
                : timestamp;
        }

        item.ProjectId = normalized.ProjectId;
        item.ItemType = normalized.ItemType;
        item.Status = normalized.Status;
        item.Title = normalized.Title;
        item.Body = normalized.Body;
        item.SourceDocumentIds = normalized.SourceDocumentIds.ToList();
        item.LinkedTaskIds = normalized.LinkedTaskIds.ToList();
        item.LinkedMeetingIds = normalized.LinkedMeetingIds.ToList();
        item.UpdatedAtUtc = timestamp;
        return true;
    }

    public bool DeleteItem(Guid itemId) =>
        _state.ContextItems.RemoveAll(item => item.Id == itemId) > 0;

    /// <summary>
    /// Links a context item to a task in the same project. Task Details only ever
    /// offers same-project candidates, but this is enforced here too so a
    /// cross-project link can never be created even if attempted directly.
    /// </summary>
    public bool LinkItemToTask(Guid itemId, Guid taskId, DateTimeOffset? now = null)
    {
        var item = FindItem(itemId);
        var task = _state.Tasks.FirstOrDefault(candidate => candidate.Id == taskId);
        return MutateLinks(
            item,
            candidate => candidate.LinkedTaskIds,
            taskId,
            add: true,
            targetExists: item is not null && task is not null && task.ProjectId == item.ProjectId,
            now);
    }

    public bool UnlinkItemFromTask(Guid itemId, Guid taskId, DateTimeOffset? now = null) =>
        MutateLinks(FindItem(itemId), item => item.LinkedTaskIds, taskId, add: false, targetExists: true, now);

    /// <summary>
    /// Links a context item to a MEET in the same project. MEET Details only ever
    /// offers same-project candidates, but this is enforced here too so a
    /// cross-project link can never be created even if attempted directly.
    /// </summary>
    public bool LinkItemToMeeting(Guid itemId, Guid meetingId, DateTimeOffset? now = null)
    {
        var item = FindItem(itemId);
        var meeting = _state.Meetings.FirstOrDefault(candidate => candidate.Id == meetingId);
        return MutateLinks(
            item,
            candidate => candidate.LinkedMeetingIds,
            meetingId,
            add: true,
            targetExists: item is not null && meeting is not null && meeting.ProjectId == item.ProjectId,
            now);
    }

    public bool UnlinkItemFromMeeting(Guid itemId, Guid meetingId, DateTimeOffset? now = null) =>
        MutateLinks(FindItem(itemId), item => item.LinkedMeetingIds, meetingId, add: false, targetExists: true, now);

    /// <summary>
    /// Links a source document to a task in the same project. Task Details only ever
    /// offers same-project candidates, but this is enforced here too so a
    /// cross-project link can never be created even if attempted directly.
    /// </summary>
    public bool LinkSourceToTask(Guid sourceId, Guid taskId, DateTimeOffset? now = null)
    {
        var source = FindSource(sourceId);
        var task = _state.Tasks.FirstOrDefault(candidate => candidate.Id == taskId);
        return MutateLinks(
            source,
            candidate => candidate.LinkedTaskIds,
            taskId,
            add: true,
            targetExists: source is not null && task is not null && task.ProjectId == source.ProjectId,
            now);
    }

    public bool UnlinkSourceFromTask(Guid sourceId, Guid taskId, DateTimeOffset? now = null) =>
        MutateLinks(FindSource(sourceId), source => source.LinkedTaskIds, taskId, add: false, targetExists: true, now);

    /// <summary>
    /// Links a source document to a MEET in the same project. MEET Details only ever
    /// offers same-project candidates, but this is enforced here too so a
    /// cross-project link can never be created even if attempted directly.
    /// </summary>
    public bool LinkSourceToMeeting(Guid sourceId, Guid meetingId, DateTimeOffset? now = null)
    {
        var source = FindSource(sourceId);
        var meeting = _state.Meetings.FirstOrDefault(candidate => candidate.Id == meetingId);
        return MutateLinks(
            source,
            candidate => candidate.LinkedMeetingIds,
            meetingId,
            add: true,
            targetExists: source is not null && meeting is not null && meeting.ProjectId == source.ProjectId,
            now);
    }

    public bool UnlinkSourceFromMeeting(Guid sourceId, Guid meetingId, DateTimeOffset? now = null) =>
        MutateLinks(FindSource(sourceId), source => source.LinkedMeetingIds, meetingId, add: false, targetExists: true, now);

    /// <summary>Removes a deleted task's id from every context record.</summary>
    public bool ClearTaskLinks(Guid taskId, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var item in _state.ContextItems)
        {
            if (item.LinkedTaskIds.Remove(taskId))
            {
                item.UpdatedAtUtc = timestamp;
                changed = true;
            }
        }

        foreach (var source in _state.ContextSources)
        {
            if (source.LinkedTaskIds.Remove(taskId))
            {
                source.UpdatedAtUtc = timestamp;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Removes a deleted meeting's id from every context record.</summary>
    public bool ClearMeetingLinks(Guid meetingId, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var item in _state.ContextItems)
        {
            if (item.LinkedMeetingIds.Remove(meetingId))
            {
                item.UpdatedAtUtc = timestamp;
                changed = true;
            }
        }

        foreach (var source in _state.ContextSources)
        {
            if (source.LinkedMeetingIds.Remove(meetingId))
            {
                source.UpdatedAtUtc = timestamp;
                changed = true;
            }
        }

        return changed;
    }

    private ContextItem? FindItem(Guid itemId) =>
        _state.ContextItems.FirstOrDefault(item => item.Id == itemId);

    private SourceDocument? FindSource(Guid sourceId) =>
        _state.ContextSources.FirstOrDefault(source => source.Id == sourceId);

    /// <summary>
    /// Link mutations are idempotent: linking an already-linked id or unlinking
    /// an absent id succeeds without a duplicate/no-op write. Unknown owner or
    /// unknown link target fails the command.
    /// </summary>
    private static bool MutateLinks<TOwner>(
        TOwner? owner,
        Func<TOwner, List<Guid>> links,
        Guid targetId,
        bool add,
        bool targetExists,
        DateTimeOffset? now)
        where TOwner : class
    {
        if (owner is null || targetId == Guid.Empty || (add && !targetExists))
        {
            return false;
        }

        var list = links(owner);
        bool changed;
        if (add)
        {
            changed = !list.Contains(targetId);
            if (changed)
            {
                list.Add(targetId);
            }
        }
        else
        {
            changed = list.Remove(targetId);
        }

        if (changed)
        {
            var timestamp = now ?? DateTimeOffset.UtcNow;
            switch (owner)
            {
                case ContextItem item:
                    item.UpdatedAtUtc = timestamp;
                    break;
                case SourceDocument source:
                    source.UpdatedAtUtc = timestamp;
                    break;
            }
        }

        return true;
    }

    private bool TryNormalizeSource(SourceDocumentUpdate input, out NormalizedSource normalized)
    {
        normalized = default!;
        var title = input.Title?.Trim();
        var body = input.Body?.Trim() ?? string.Empty;
        var summary = input.Summary?.Trim() ?? string.Empty;
        var taskIds = NormalizeIds(input.LinkedTaskIds);
        var meetingIds = NormalizeIds(input.LinkedMeetingIds);
        if (!_state.Projects.Any(project => project.Id == input.ProjectId) ||
            string.IsNullOrWhiteSpace(title) || title.Length > MaximumTitleLength ||
            body.Length > MaximumBodyLength ||
            summary.Length > MaximumSummaryLength ||
            !Enum.IsDefined(input.SourceType) ||
            input.SourceApp is { } app && !Enum.IsDefined(app) ||
            input.SourceDateUtc == default ||
            taskIds.Any(id => !_state.Tasks.Any(task => task.Id == id)) ||
            meetingIds.Any(id => _state.Meetings?.Any(meeting => meeting.Id == id) != true))
        {
            return false;
        }

        normalized = new NormalizedSource(
            input.ProjectId,
            input.SourceType,
            input.SourceApp,
            title,
            body,
            summary,
            input.SourceDateUtc,
            taskIds,
            meetingIds);
        return true;
    }

    private bool TryNormalizeItem(ContextItemUpdate input, out NormalizedItem normalized)
    {
        normalized = default!;
        var title = input.Title?.Trim();
        var body = input.Body?.Trim() ?? string.Empty;
        var sourceIds = NormalizeIds(input.SourceDocumentIds);
        var taskIds = NormalizeIds(input.LinkedTaskIds);
        var meetingIds = NormalizeIds(input.LinkedMeetingIds);
        if (!_state.Projects.Any(project => project.Id == input.ProjectId) ||
            string.IsNullOrWhiteSpace(title) || title.Length > MaximumTitleLength ||
            body.Length > MaximumBodyLength ||
            !Enum.IsDefined(input.ItemType) ||
            !Enum.IsDefined(input.Status) ||
            sourceIds.Any(id => !_state.ContextSources.Any(source => source.Id == id)) ||
            taskIds.Any(id => !_state.Tasks.Any(task => task.Id == id)) ||
            meetingIds.Any(id => _state.Meetings?.Any(meeting => meeting.Id == id) != true))
        {
            return false;
        }

        normalized = new NormalizedItem(
            input.ProjectId,
            input.ItemType,
            input.Status,
            title,
            body,
            sourceIds,
            taskIds,
            meetingIds);
        return true;
    }

    private static List<Guid> NormalizeIds(IReadOnlyList<Guid>? ids) =>
        ids is null
            ? new List<Guid>()
            : ids.Where(id => id != Guid.Empty).Distinct().ToList();

    private sealed record NormalizedSource(
        Guid ProjectId,
        ContextSourceType SourceType,
        ContextSourceApp? SourceApp,
        string Title,
        string Body,
        string Summary,
        DateTimeOffset SourceDateUtc,
        List<Guid> LinkedTaskIds,
        List<Guid> LinkedMeetingIds);

    private sealed record NormalizedItem(
        Guid ProjectId,
        ContextItemType ItemType,
        ContextItemStatus Status,
        string Title,
        string Body,
        List<Guid> SourceDocumentIds,
        List<Guid> LinkedTaskIds,
        List<Guid> LinkedMeetingIds);
}
