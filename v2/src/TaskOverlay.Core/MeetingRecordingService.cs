using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public sealed class MeetingRecordingService
{
    private readonly AppState _state;

    public MeetingRecordingService(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _state.MeetingRecordings ??= new List<MeetingRecording>();
        _state.MeetingAnalyses ??= new List<MeetingAnalysis>();
    }

    public MeetingRecording? ActiveRecording => _state.MeetingRecordings
        .FirstOrDefault(recording => recording.IsActive);

    public MeetingRecording? CreatePending(
        Guid? meetId,
        MeetingRecordingSourceKind sourceKind,
        string recordingFolderRelativePath,
        DateTimeOffset? now = null,
        Guid? recordingId = null)
    {
        if (ActiveRecording is not null ||
            (meetId.HasValue && _state.Meetings.All(meeting => meeting.Id != meetId.Value)) ||
            !RecordingPathPolicy.IsSafeRelativePath(recordingFolderRelativePath))
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var recording = new MeetingRecording
        {
            Id = recordingId is Guid id && id != Guid.Empty ? id : Guid.NewGuid(),
            MeetId = meetId,
            SourceKind = sourceKind,
            State = MeetingRecordingState.Pending,
            RecordingFolderRelativePath = RecordingPathPolicy.NormalizeRelativePath(
                recordingFolderRelativePath),
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        _state.MeetingRecordings.Add(recording);
        return recording;
    }

    public MeetingRecording? CreateAutoStartFailure(
        Guid meetId,
        Guid recordingId,
        string recordingFolderRelativePath,
        string error,
        DateTimeOffset? now = null)
    {
        if (_state.Meetings.All(meeting => meeting.Id != meetId) ||
            recordingId == Guid.Empty ||
            _state.MeetingRecordings.Any(recording => recording.Id == recordingId) ||
            !RecordingPathPolicy.IsSafeRelativePath(recordingFolderRelativePath))
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var recording = new MeetingRecording
        {
            Id = recordingId,
            MeetId = meetId,
            SourceKind = MeetingRecordingSourceKind.ScheduledMeet,
            State = MeetingRecordingState.Failed,
            RecordingFolderRelativePath = RecordingPathPolicy.NormalizeRelativePath(
                recordingFolderRelativePath),
            LastError = NormalizeError(error),
            LastAutoStartAttemptAtUtc = timestamp,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        _state.MeetingRecordings.Add(recording);
        return recording;
    }

    public bool MarkRecording(
        Guid recordingId,
        MeetingRecordingStartResult result,
        DateTimeOffset? now = null)
    {
        var recording = Find(recordingId);
        if (recording is null || recording.State != MeetingRecordingState.Pending)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        recording.StartedAtUtc = result.StartedAtUtc;
        recording.SystemTrackStartedAtUtc = result.SystemTrackStartedAtUtc;
        recording.MicrophoneTrackStartedAtUtc = result.MicrophoneTrackStartedAtUtc;
        recording.SystemAudioFile = result.SystemAudioFile;
        recording.MicrophoneFile = result.MicrophoneFile;
        recording.SystemAudioHealth = result.SystemAudioHealth;
        recording.MicrophoneHealth = result.MicrophoneHealth;
        recording.LastError = result.Warning ?? string.Empty;
        recording.State = MeetingRecordingState.Recording;
        recording.UpdatedAtUtc = timestamp;
        return true;
    }

    public bool MarkStopping(Guid recordingId, DateTimeOffset? now = null) =>
        TryTransition(
            recordingId,
            MeetingRecordingState.Stopping,
            now,
            MeetingRecordingState.Recording);

    public bool MarkRecorded(
        Guid recordingId,
        MeetingRecordingStopResult result,
        DateTimeOffset? now = null)
    {
        var recording = Find(recordingId);
        if (recording is null ||
            recording.State is not (MeetingRecordingState.Recording or
                MeetingRecordingState.Stopping))
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        recording.StoppedAtUtc = result.StoppedAtUtc;
        recording.SystemAudioHealth = result.SystemAudioHealth;
        recording.MicrophoneHealth = result.MicrophoneHealth;
        recording.LastError = result.Warning ?? string.Empty;
        recording.State = MeetingRecordingState.Recorded;
        recording.UpdatedAtUtc = timestamp;
        return true;
    }

    public bool MarkProcessing(Guid recordingId, DateTimeOffset? now = null) =>
        TryTransition(
            recordingId,
            MeetingRecordingState.Processing,
            now,
            MeetingRecordingState.Recorded,
            MeetingRecordingState.Failed);

    public bool MarkTranscribing(Guid recordingId, DateTimeOffset? now = null) =>
        TryTransition(
            recordingId,
            MeetingRecordingState.Transcribing,
            now,
            MeetingRecordingState.Processing,
            MeetingRecordingState.Recorded,
            MeetingRecordingState.Failed);

    public bool MarkTranscriptReady(
        Guid recordingId,
        string mixedAudioFile,
        IReadOnlyList<string> chunkFiles,
        string rawFile,
        string transcriptFile,
        string markdownFile,
        DateTimeOffset? now = null)
    {
        var recording = Find(recordingId);
        if (recording is null ||
            recording.State is not (MeetingRecordingState.Transcribing or
                MeetingRecordingState.Processing))
        {
            return false;
        }

        recording.MixedAudioFile = mixedAudioFile ?? string.Empty;
        recording.TranscriptionChunkFiles = chunkFiles?.ToList() ?? new List<string>();
        recording.TranscriptRawFile = rawFile ?? string.Empty;
        recording.TranscriptFile = transcriptFile ?? string.Empty;
        recording.TranscriptMarkdownFile = markdownFile ?? string.Empty;
        recording.State = MeetingRecordingState.TranscriptReady;
        recording.LastError = string.Empty;
        recording.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        return true;
    }

    public bool MarkAnalyzing(Guid recordingId, DateTimeOffset? now = null) =>
        TryTransition(
            recordingId,
            MeetingRecordingState.Analyzing,
            now,
            MeetingRecordingState.TranscriptReady,
            MeetingRecordingState.Ready,
            MeetingRecordingState.Failed);

    public bool MarkReady(
        Guid recordingId,
        string analysisFile,
        DateTimeOffset? now = null)
    {
        var recording = Find(recordingId);
        if (recording is null || recording.State != MeetingRecordingState.Analyzing)
        {
            return false;
        }

        recording.AnalysisFile = analysisFile ?? string.Empty;
        recording.State = MeetingRecordingState.Ready;
        recording.LastError = string.Empty;
        recording.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        return true;
    }

    public bool MarkFailed(Guid recordingId, string? error, DateTimeOffset? now = null)
    {
        var recording = Find(recordingId);
        if (recording is null)
        {
            return false;
        }

        recording.State = MeetingRecordingState.Failed;
        recording.LastError = NormalizeError(error);
        recording.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        return true;
    }

    public bool LinkToMeeting(Guid recordingId, Guid meetId, DateTimeOffset? now = null)
    {
        var recording = Find(recordingId);
        if (recording is null || _state.Meetings.All(meeting => meeting.Id != meetId))
        {
            return false;
        }

        recording.MeetId = meetId;
        recording.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        foreach (var analysis in _state.MeetingAnalyses.Where(item =>
                     item.RecordingId == recordingId))
        {
            analysis.MeetId = meetId;
            analysis.UpdatedAtUtc = recording.UpdatedAtUtc;
        }

        return true;
    }

    public bool SetKeepLocalOnly(Guid recordingId, bool keepLocalOnly, DateTimeOffset? now = null)
    {
        var recording = Find(recordingId);
        if (recording is null)
        {
            return false;
        }

        recording.KeepLocalOnly = keepLocalOnly;
        recording.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        return true;
    }

    public bool RemoveMetadata(Guid recordingId)
    {
        _state.MeetingAnalyses.RemoveAll(analysis => analysis.RecordingId == recordingId);
        return _state.MeetingRecordings.RemoveAll(recording => recording.Id == recordingId) > 0;
    }

    public bool RecoverInterrupted(DateTimeOffset? now = null)
    {
        var changed = false;
        var timestamp = now ?? DateTimeOffset.UtcNow;
        foreach (var recording in _state.MeetingRecordings.Where(recording =>
                     recording.State is MeetingRecordingState.Recording or
                         MeetingRecordingState.Stopping or
                         MeetingRecordingState.Processing or
                         MeetingRecordingState.Transcribing or
                         MeetingRecordingState.Analyzing))
        {
            recording.State = MeetingRecordingState.Failed;
            recording.LastError =
                "The previous operation was interrupted. Original files were kept; retry the operation.";
            recording.UpdatedAtUtc = timestamp;
            changed = true;
        }

        foreach (var analysis in _state.MeetingAnalyses.Where(analysis =>
                     analysis.State == MeetingAnalysisState.Analyzing))
        {
            analysis.State = MeetingAnalysisState.Failed;
            analysis.LastError =
                "Analysis was interrupted. The transcript was kept; retry analysis.";
            analysis.UpdatedAtUtc = timestamp;
            changed = true;
        }

        return changed;
    }

    public MeetingRecording? Find(Guid recordingId) => _state.MeetingRecordings
        .FirstOrDefault(recording => recording.Id == recordingId);

    private bool TryTransition(
        Guid recordingId,
        MeetingRecordingState next,
        DateTimeOffset? now,
        params MeetingRecordingState[] allowed)
    {
        var recording = Find(recordingId);
        if (recording is null || !allowed.Contains(recording.State))
        {
            return false;
        }

        recording.State = next;
        recording.LastError = string.Empty;
        recording.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        return true;
    }

    private static string NormalizeError(string? error)
    {
        var normalized = error?.Trim() ?? string.Empty;
        return normalized.Length <= 2_000 ? normalized : normalized[..2_000];
    }
}

public static class MeetingAutoRecordScheduler
{
    public static IReadOnlyList<MeetingItem> FindDueMeetings(
        AppState state,
        MeetingAssistantSettings settings,
        DateTimeOffset now,
        TimeSpan? gracePeriod = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.AutomaticRecordingEnabled)
        {
            return Array.Empty<MeetingItem>();
        }

        var grace = gracePeriod ?? TimeSpan.FromMinutes(5);
        var attemptedMeetIds = (state.MeetingRecordings ?? new List<MeetingRecording>())
            .Where(recording =>
                recording.SourceKind == MeetingRecordingSourceKind.ScheduledMeet &&
                recording.MeetId.HasValue &&
                recording.LastAutoStartAttemptAtUtc.HasValue)
            .Select(recording => recording.MeetId!.Value)
            .ToHashSet();

        return (state.Meetings ?? new List<MeetingItem>())
            .Where(meeting => EffectivePolicy(meeting, settings) ==
                              MeetingRecordingPolicy.AutoRecord)
            .Where(meeting => !attemptedMeetIds.Contains(meeting.Id))
            .Where(meeting => now >= meeting.StartsAtUtc &&
                              now <= meeting.StartsAtUtc + grace)
            .OrderBy(meeting => meeting.StartsAtUtc)
            .ThenBy(meeting => meeting.Id)
            .ToList();
    }

    public static MeetingRecordingPolicy EffectivePolicy(
        MeetingItem meeting,
        MeetingAssistantSettings settings) =>
        meeting.RecordingPolicy == MeetingRecordingPolicy.Inherit
            ? settings.DefaultRecordingPolicy
            : meeting.RecordingPolicy;
}

public static class RecordingPathPolicy
{
    public static bool IsSafeRelativePath(string? path)
    {
        var normalized = NormalizeRelativePath(path);
        return normalized.Length > 0 &&
               !System.IO.Path.IsPathRooted(normalized) &&
               !normalized.Split(
                       new[] { '/', '\\' },
                       StringSplitOptions.RemoveEmptyEntries)
                   .Any(part => part is "." or "..");
    }

    public static string NormalizeRelativePath(string? path) =>
        (path ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
}
