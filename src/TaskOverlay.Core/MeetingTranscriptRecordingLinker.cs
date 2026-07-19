using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TaskOverlay.Core;

public enum MeetingTranscriptRecordingLinkReason
{
    Linked,
    NoDeterministicLink,
    AmbiguousLink,
    DifferentMeeting,
    RecordingMissing
}

public sealed record MeetingTranscriptRecordingLink(
    Guid? RecordingId,
    MeetingTranscriptRecordingLinkReason Reason);

/// <summary>
/// Resolves and repairs transcript-to-recording identity from durable,
/// same-MEET metadata. It never selects a recording by time or list order.
/// </summary>
public sealed class MeetingTranscriptRecordingLinker
{
    private readonly AppState _state;
    private readonly MeetingSourceStorage _sourceStorage;
    private readonly MeetingRecordingStorage _recordingStorage;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MeetingTranscriptRecordingLinker(AppState state, string stateDirectory)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _sourceStorage = new MeetingSourceStorage(stateDirectory);
        _recordingStorage = new MeetingRecordingStorage(stateDirectory);
    }

    public MeetingTranscriptRecordingLink Resolve(MeetingTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (transcript.MeetId is not Guid meetingId)
        {
            return new MeetingTranscriptRecordingLink(
                null,
                MeetingTranscriptRecordingLinkReason.NoDeterministicLink);
        }

        if (transcript.RecordingId is Guid explicitId)
        {
            return ValidateCandidate(explicitId, meetingId);
        }

        var lineage = BuildLineage(transcript, meetingId);
        foreach (var ancestor in lineage.Skip(1))
        {
            if (ancestor.RecordingId is Guid inheritedId)
            {
                return ValidateCandidate(inheritedId, meetingId);
            }
        }

        var transcriptIds = lineage.Select(item => item.Id).ToHashSet();
        var revisionIds = lineage.Select(item => item.RevisionId).ToHashSet();
        var candidates = new HashSet<Guid>();

        foreach (var item in lineage)
        {
            if (TryLoadSourceTranscript(item, out var normalized) &&
                normalized.RecordingId != Guid.Empty &&
                IsRecordingInMeeting(normalized.RecordingId, meetingId))
            {
                candidates.Add(normalized.RecordingId);
            }
        }

        foreach (var analysis in _state.MeetingAnalyses.Where(item =>
                     (item.MeetId is null || item.MeetId == meetingId) &&
                     item.RecordingId.HasValue &&
                     ((item.TranscriptId.HasValue && transcriptIds.Contains(item.TranscriptId.Value)) ||
                      (item.TranscriptRevisionId.HasValue && revisionIds.Contains(item.TranscriptRevisionId.Value)))))
        {
            if (IsRecordingInMeeting(analysis.RecordingId!.Value, meetingId))
            {
                candidates.Add(analysis.RecordingId.Value);
            }
        }

        foreach (var recording in _state.MeetingRecordings.Where(item => item.MeetId == meetingId))
        {
            if (lineage.Any(item =>
                    SameManagedFolder(item.StorageFolderRelativePath, recording.RecordingFolderRelativePath) &&
                    !string.IsNullOrWhiteSpace(item.NormalizedArtifactFile) &&
                    string.Equals(item.NormalizedArtifactFile, recording.TranscriptFile,
                        StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(recording.Id);
                continue;
            }

            if (TryLoadRecordingTranscript(recording, out var persisted) &&
                ((persisted.TranscriptId != Guid.Empty && transcriptIds.Contains(persisted.TranscriptId)) ||
                 (persisted.RevisionId != Guid.Empty && revisionIds.Contains(persisted.RevisionId))))
            {
                candidates.Add(recording.Id);
            }
        }

        return candidates.Count switch
        {
            1 => new MeetingTranscriptRecordingLink(
                candidates.Single(),
                MeetingTranscriptRecordingLinkReason.Linked),
            > 1 => new MeetingTranscriptRecordingLink(
                null,
                MeetingTranscriptRecordingLinkReason.AmbiguousLink),
            _ => new MeetingTranscriptRecordingLink(
                null,
                MeetingTranscriptRecordingLinkReason.NoDeterministicLink)
        };
    }

    public int RepairMissingLinks()
    {
        var repaired = 0;
        foreach (var transcript in _state.MeetingTranscripts.Where(item => !item.RecordingId.HasValue))
        {
            var link = Resolve(transcript);
            if (link.RecordingId is not Guid recordingId)
            {
                continue;
            }

            transcript.RecordingId = recordingId;
            transcript.UpdatedAtUtc = DateTimeOffset.UtcNow;
            repaired++;
        }

        return repaired;
    }

    /// <summary>Backfills a legacy processing interval only when one generated lineage owns it.</summary>
    public int RepairMissingAudioRanges()
    {
        var repaired = 0;
        foreach (var recording in _state.MeetingRecordings)
        {
            if (recording.ProcessFromSeconds is null && recording.ProcessUntilSeconds is null)
            {
                continue;
            }

            var duration = recording.Tracks.FirstOrDefault(track =>
                track.Kind == MeetingRecordingTrackKind.Mixed)?.DurationSeconds ?? 0;
            var range = MeetingTranscriptAudioRange.Resolve(
                duration, recording.ProcessFromSeconds, recording.ProcessUntilSeconds);
            if (range.DurationSeconds <= 0 || duration <= 0)
            {
                continue;
            }

            var generatedRoots = _state.MeetingTranscripts.Where(transcript =>
                    transcript.Origin == MeetingTranscriptOrigin.Generated &&
                    transcript.RecordingId == recording.Id &&
                    transcript.SourceAudioStartSeconds is null &&
                    transcript.SourceAudioEndSeconds is null)
                .ToList();
            if (generatedRoots.Count != 1)
            {
                continue;
            }

            var root = generatedRoots[0];
            foreach (var transcript in _state.MeetingTranscripts.Where(item =>
                         item.Id == root.Id || IsDescendedFrom(item, root.Id)))
            {
                if (transcript.SourceAudioStartSeconds.HasValue || transcript.SourceAudioEndSeconds.HasValue)
                {
                    continue;
                }

                transcript.SourceAudioStartSeconds = range.SourceStartSeconds;
                transcript.SourceAudioEndSeconds = range.SourceEndSeconds;
                transcript.UpdatedAtUtc = DateTimeOffset.UtcNow;
                repaired++;
            }
        }

        return repaired;
    }

    private bool IsDescendedFrom(MeetingTranscript transcript, Guid rootId)
    {
        var visited = new HashSet<Guid>();
        var current = transcript;
        while (current.SourceTranscriptId is Guid sourceId && visited.Add(sourceId))
        {
            if (sourceId == rootId) return true;
            current = _state.MeetingTranscripts.SingleOrDefault(item => item.Id == sourceId) ?? current;
            if (current.Id == transcript.Id) break;
        }
        return false;
    }

    private List<MeetingTranscript> BuildLineage(MeetingTranscript transcript, Guid meetingId)
    {
        var lineage = new List<MeetingTranscript> { transcript };
        var visited = new HashSet<Guid> { transcript.Id };
        var current = transcript;
        while (current.SourceTranscriptId is Guid sourceId && visited.Add(sourceId))
        {
            var source = _state.MeetingTranscripts.SingleOrDefault(item =>
                item.Id == sourceId && item.MeetId == meetingId);
            if (source is null)
            {
                break;
            }

            lineage.Add(source);
            current = source;
        }

        return lineage;
    }

    private MeetingTranscriptRecordingLink ValidateCandidate(Guid recordingId, Guid meetingId)
    {
        var recording = _state.MeetingRecordings.SingleOrDefault(item => item.Id == recordingId);
        if (recording is null)
        {
            return new MeetingTranscriptRecordingLink(
                recordingId,
                MeetingTranscriptRecordingLinkReason.RecordingMissing);
        }

        return recording.MeetId == meetingId
            ? new MeetingTranscriptRecordingLink(
                recordingId,
                MeetingTranscriptRecordingLinkReason.Linked)
            : new MeetingTranscriptRecordingLink(
                null,
                MeetingTranscriptRecordingLinkReason.DifferentMeeting);
    }

    private bool IsRecordingInMeeting(Guid recordingId, Guid meetingId) =>
        _state.MeetingRecordings.Any(item => item.Id == recordingId && item.MeetId == meetingId);

    private bool TryLoadSourceTranscript(
        MeetingTranscript transcript,
        out NormalizedTranscript normalized)
    {
        normalized = default!;
        if (string.IsNullOrWhiteSpace(transcript.NormalizedArtifactFile))
        {
            return false;
        }

        try
        {
            var path = _sourceStorage.ResolveTranscriptFile(
                transcript,
                transcript.NormalizedArtifactFile);
            return TryReadManagedTranscript(path, out normalized);
        }
        catch (Exception ex) when (IsSafeReadFailure(ex))
        {
            return false;
        }
    }

    private bool TryLoadRecordingTranscript(
        MeetingRecording recording,
        out NormalizedTranscript normalized)
    {
        normalized = default!;
        if (recording.MeetId is not Guid meetingId ||
            string.IsNullOrWhiteSpace(recording.TranscriptFile) ||
            !string.Equals(
                recording.RecordingFolderRelativePath.TrimEnd('/'),
                $"meetings/{meetingId:N}/recordings/{recording.Id:N}",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var path = _recordingStorage.ResolveFile(recording, recording.TranscriptFile);
            return TryReadManagedTranscript(path, out normalized);
        }
        catch (Exception ex) when (IsSafeReadFailure(ex))
        {
            return false;
        }
    }

    private bool TryReadManagedTranscript(string path, out NormalizedTranscript normalized)
    {
        normalized = default!;
        var stateRoot = Path.GetFullPath(_sourceStorage.StateDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(stateRoot, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) ||
            ContainsReparsePoint(stateRoot, path))
        {
            return false;
        }

        normalized = JsonSerializer.Deserialize<NormalizedTranscript>(
                         File.ReadAllText(path),
                         _jsonOptions)!;
        return normalized is not null;
    }

    private static bool SameManagedFolder(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(
            left.Replace('\\', '/').TrimEnd('/'),
            right.Replace('\\', '/').TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    private static bool ContainsReparsePoint(string rootWithSeparator, string filePath)
    {
        var current = rootWithSeparator.TrimEnd(Path.DirectorySeparatorChar);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        var relative = Path.GetRelativePath(current, filePath);
        foreach (var part in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSafeReadFailure(Exception exception) =>
        exception is InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            JsonException;
}
