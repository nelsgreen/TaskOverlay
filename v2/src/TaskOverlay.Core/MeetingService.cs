using System;
using System.Linq;

namespace TaskOverlay.Core;

public sealed record MeetingUpdate(
    Guid ProjectId,
    string Title,
    string? Notes,
    DateTimeOffset StartsAtUtc,
    int DurationMinutes,
    string? Location,
    string? Link,
    Guid? LinkedTaskId);

public sealed class MeetingService
{
    public const int MaximumTitleLength = 500;
    public const int MaximumNotesLength = 100_000;
    public const int MaximumLocationLength = 1_000;
    public const int MaximumLinkLength = 2_048;
    public const int MaximumDurationMinutes = 24 * 60;

    private readonly AppState _state;

    public MeetingService(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _state.Meetings ??= new();
    }

    public MeetingItem? Create(MeetingUpdate input, DateTimeOffset? now = null)
    {
        if (!TryNormalize(input, out var normalized))
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var meeting = new MeetingItem
        {
            ProjectId = normalized.ProjectId,
            Title = normalized.Title,
            Notes = normalized.Notes,
            StartsAtUtc = normalized.StartsAtUtc,
            DurationMinutes = normalized.DurationMinutes,
            Location = normalized.Location,
            Link = normalized.Link,
            LinkedTaskId = normalized.LinkedTaskId,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        _state.Meetings.Add(meeting);
        return meeting;
    }

    public bool Update(Guid meetingId, MeetingUpdate input, DateTimeOffset? now = null)
    {
        var meeting = _state.Meetings.FirstOrDefault(item => item.Id == meetingId);
        if (meeting is null || !TryNormalize(input, out var normalized))
        {
            return false;
        }

        meeting.ProjectId = normalized.ProjectId;
        meeting.Title = normalized.Title;
        meeting.Notes = normalized.Notes;
        meeting.StartsAtUtc = normalized.StartsAtUtc;
        meeting.DurationMinutes = normalized.DurationMinutes;
        meeting.Location = normalized.Location;
        meeting.Link = normalized.Link;
        meeting.LinkedTaskId = normalized.LinkedTaskId;
        meeting.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        return true;
    }

    public bool Delete(Guid meetingId, DateTimeOffset? now = null)
    {
        if (_state.Meetings.RemoveAll(item => item.Id == meetingId) == 0)
        {
            return false;
        }

        // Project memory outlives the meeting: clear navigation links only.
        new ContextService(_state).ClearMeetingLinks(meetingId, now);
        var timestamp = now ?? DateTimeOffset.UtcNow;
        foreach (var recording in (_state.MeetingRecordings ?? new())
                     .Where(recording => recording.MeetId == meetingId))
        {
            recording.MeetId = null;
            recording.SourceKind = MeetingRecordingSourceKind.Emergency;
            recording.UpdatedAtUtc = timestamp;
        }

        foreach (var analysis in (_state.MeetingAnalyses ?? new())
                     .Where(analysis => analysis.MeetId == meetingId))
        {
            analysis.MeetId = null;
            analysis.UpdatedAtUtc = timestamp;
        }

        foreach (var transcript in (_state.MeetingTranscripts ?? new())
                     .Where(transcript => transcript.MeetId == meetingId))
        {
            transcript.MeetId = null;
            transcript.UpdatedAtUtc = timestamp;
        }

        _state.MeetingScreenshots?.RemoveAll(screenshot => screenshot.MeetId == meetingId);
        return true;
    }

    public bool ClearLinkedTask(Guid taskId, DateTimeOffset? now = null)
    {
        var changed = false;
        var timestamp = now ?? DateTimeOffset.UtcNow;
        foreach (var meeting in _state.Meetings.Where(item => item.LinkedTaskId == taskId))
        {
            meeting.LinkedTaskId = null;
            meeting.UpdatedAtUtc = timestamp;
            changed = true;
        }

        return changed;
    }

    private bool TryNormalize(MeetingUpdate input, out NormalizedMeeting normalized)
    {
        normalized = default;
        var title = input.Title?.Trim();
        var notes = input.Notes?.Trim() ?? string.Empty;
        var location = input.Location?.Trim() ?? string.Empty;
        var link = input.Link?.Trim() ?? string.Empty;
        if (!_state.Projects.Any(project => project.Id == input.ProjectId) ||
            string.IsNullOrWhiteSpace(title) || title.Length > MaximumTitleLength ||
            input.StartsAtUtc == default ||
            input.DurationMinutes <= 0 || input.DurationMinutes > MaximumDurationMinutes ||
            notes.Length > MaximumNotesLength ||
            location.Length > MaximumLocationLength ||
            link.Length > MaximumLinkLength ||
            input.LinkedTaskId is Guid linkedTaskId && !_state.Tasks.Any(task => task.Id == linkedTaskId))
        {
            return false;
        }

        normalized = new NormalizedMeeting(
            input.ProjectId,
            title,
            notes,
            input.StartsAtUtc,
            input.DurationMinutes,
            location,
            link,
            input.LinkedTaskId);
        return true;
    }

    private readonly record struct NormalizedMeeting(
        Guid ProjectId,
        string Title,
        string Notes,
        DateTimeOffset StartsAtUtc,
        int DurationMinutes,
        string Location,
        string Link,
        Guid? LinkedTaskId);
}
