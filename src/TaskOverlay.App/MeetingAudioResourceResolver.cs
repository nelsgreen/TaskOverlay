using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed record MeetingAudioResource(
    string FilePath,
    string ContentType,
    long Length,
    double DurationSeconds);

public enum MeetingAudioResolutionReason
{
    Available,
    InactiveTranscript,
    NoDeterministicLink,
    AmbiguousLink,
    RecordingMissing,
    DifferentMeeting,
    UnsafeManagedFolder,
    MixedTrackUnavailable,
    UnsupportedFormat,
    ManagedFileUnavailable
}

public sealed record MeetingAudioResolution(
    Guid? RecordingId,
    MeetingAudioResolutionReason Reason);

/// <summary>
/// Resolves the opaque Workspace audio endpoint to the mixed track linked to
/// the currently active transcript. No filesystem path crosses into React.
/// </summary>
public sealed class MeetingAudioResourceResolver
{
    public const string ResourcePathPrefix = "/__meeting-audio/";

    private readonly AppState _state;
    private readonly MeetingRecordingStorage _storage;
    private readonly Action<string, Exception?>? _diagnostic;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MeetingAudioResourceResolver(
        AppState state,
        string stateDirectory,
        Action<string, Exception?>? diagnostic = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _storage = new MeetingRecordingStorage(stateDirectory);
        _diagnostic = diagnostic;
    }

    public WorkspaceTranscriptAudioSnapshot Describe(MeetingTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var link = ResolveTranscriptLink(transcript);
        if (link.RecordingId is not Guid recordingId)
        {
            Report(transcript, link.Reason);
            return new WorkspaceTranscriptAudioSnapshot(
                link.Reason is MeetingAudioResolutionReason.InactiveTranscript or
                    MeetingAudioResolutionReason.NoDeterministicLink
                    ? WorkspaceTranscriptAudioSnapshot.NotLinked
                    : WorkspaceTranscriptAudioSnapshot.Unavailable,
                null,
                0);
        }

        var resolution = ResolveRecording(recordingId, out var resource);
        if (resolution != MeetingAudioResolutionReason.Available)
        {
            Report(transcript, resolution);
        }

        return resolution == MeetingAudioResolutionReason.Available
            ? new WorkspaceTranscriptAudioSnapshot(
                WorkspaceTranscriptAudioSnapshot.Available,
                $"https://taskoverlay.workspace{ResourcePathPrefix}{recordingId:N}",
                resource.DurationSeconds)
            : new WorkspaceTranscriptAudioSnapshot(
                WorkspaceTranscriptAudioSnapshot.Unavailable,
                null,
                0);
    }

    public bool TryResolveRequest(string requestUri, out MeetingAudioResource resource)
    {
        resource = default!;
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "taskoverlay.workspace", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.AbsolutePath.StartsWith(ResourcePathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var idText = uri.AbsolutePath[ResourcePathPrefix.Length..];
        return idText.Length == 32 &&
               !idText.Contains('/') &&
               Guid.TryParseExact(idText, "N", out var recordingId) &&
               TryResolveRecording(recordingId, out resource);
    }

    public bool TryResolveRecording(Guid recordingId, out MeetingAudioResource resource)
        => ResolveRecording(recordingId, out resource) ==
           MeetingAudioResolutionReason.Available;

    public MeetingAudioResolution ResolveTranscriptLink(MeetingTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (transcript.MeetId is not Guid meetingId ||
            _state.Meetings.SingleOrDefault(item => item.Id == meetingId)
                ?.ActiveTranscriptId != transcript.Id)
        {
            return new MeetingAudioResolution(
                null,
                MeetingAudioResolutionReason.InactiveTranscript);
        }

        if (transcript.RecordingId is Guid explicitRecordingId)
        {
            if (_state.MeetingRecordings.SingleOrDefault(item =>
                    item.Id == explicitRecordingId) is { MeetId: Guid ownerMeetingId } &&
                ownerMeetingId != meetingId)
            {
                return new MeetingAudioResolution(
                    null,
                    MeetingAudioResolutionReason.DifferentMeeting);
            }

            return new MeetingAudioResolution(
                explicitRecordingId,
                MeetingAudioResolutionReason.Available);
        }

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
            if (source.RecordingId is Guid inheritedRecordingId)
            {
                if (_state.MeetingRecordings.SingleOrDefault(item =>
                        item.Id == inheritedRecordingId) is { MeetId: Guid ownerMeetingId } &&
                    ownerMeetingId != meetingId)
                {
                    return new MeetingAudioResolution(
                        null,
                        MeetingAudioResolutionReason.DifferentMeeting);
                }

                return new MeetingAudioResolution(
                    inheritedRecordingId,
                    MeetingAudioResolutionReason.Available);
            }

            current = source;
        }

        var transcriptIds = lineage.Select(item => item.Id).ToHashSet();
        var revisionIds = lineage.Select(item => item.RevisionId).ToHashSet();
        var candidates = new HashSet<Guid>();

        foreach (var analysis in _state.MeetingAnalyses.Where(item =>
                     (item.MeetId is null || item.MeetId == meetingId) &&
                     item.RecordingId.HasValue &&
                     _state.MeetingRecordings.Any(recording =>
                         recording.Id == item.RecordingId.Value &&
                         recording.MeetId == meetingId) &&
                     ((item.TranscriptId.HasValue && transcriptIds.Contains(item.TranscriptId.Value)) ||
                      (item.TranscriptRevisionId.HasValue && revisionIds.Contains(item.TranscriptRevisionId.Value)))))
        {
            candidates.Add(analysis.RecordingId!.Value);
        }

        foreach (var recording in _state.MeetingRecordings.Where(item =>
                     item.MeetId == meetingId))
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
            1 => new MeetingAudioResolution(
                candidates.Single(),
                MeetingAudioResolutionReason.Available),
            > 1 => new MeetingAudioResolution(
                null,
                MeetingAudioResolutionReason.AmbiguousLink),
            _ => new MeetingAudioResolution(
                null,
                MeetingAudioResolutionReason.NoDeterministicLink)
        };
    }

    private MeetingAudioResolutionReason ResolveRecording(
        Guid recordingId,
        out MeetingAudioResource resource)
    {
        resource = default!;
        try
        {
            var recording = _state.MeetingRecordings.SingleOrDefault(item =>
                item.Id == recordingId);
            if (recording is null || recording.MeetId is not Guid meetingId)
            {
                return MeetingAudioResolutionReason.RecordingMissing;
            }

            var meeting = _state.Meetings.SingleOrDefault(item => item.Id == meetingId);
            var activeTranscript = meeting?.ActiveTranscriptId is Guid transcriptId
                ? _state.MeetingTranscripts.SingleOrDefault(item =>
                    item.Id == transcriptId && item.MeetId == meetingId)
                : null;
            if (activeTranscript is null)
            {
                return MeetingAudioResolutionReason.InactiveTranscript;
            }

            var link = ResolveTranscriptLink(activeTranscript);
            if (link.RecordingId != recordingId)
            {
                return link.Reason == MeetingAudioResolutionReason.Available
                    ? MeetingAudioResolutionReason.DifferentMeeting
                    : link.Reason;
            }

            var expectedRelativeFolder =
                $"meetings/{meetingId:N}/recordings/{recordingId:N}";
            if (!string.Equals(
                    recording.RecordingFolderRelativePath.TrimEnd('/'),
                    expectedRelativeFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                return MeetingAudioResolutionReason.UnsafeManagedFolder;
            }

            var mixedTrack = recording.Tracks
                .Where(track =>
                    track.Kind == MeetingRecordingTrackKind.Mixed &&
                    track.FinalizationState == MeetingRecordingFinalizationState.Finalized &&
                    (track.ValidationState is MeetingRecordingValidationState.Valid or
                        MeetingRecordingValidationState.Unknown) &&
                    !string.IsNullOrWhiteSpace(track.FileName))
                .FirstOrDefault(track => string.Equals(
                    track.FileName,
                    recording.MixedAudioFile,
                    StringComparison.OrdinalIgnoreCase));
            if (mixedTrack is null)
            {
                return MeetingAudioResolutionReason.MixedTrackUnavailable;
            }

            var contentType = ContentTypeFor(mixedTrack.FileName);
            if (contentType is null)
            {
                return MeetingAudioResolutionReason.UnsupportedFormat;
            }

            var path = _storage.ResolveFile(recording, mixedTrack.FileName);
            var meetingsRoot = Path.GetFullPath(_storage.RootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(meetingsRoot, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) ||
                ContainsReparsePoint(meetingsRoot, path))
            {
                return MeetingAudioResolutionReason.ManagedFileUnavailable;
            }

            var length = new FileInfo(path).Length;
            if (length <= 0)
            {
                return MeetingAudioResolutionReason.ManagedFileUnavailable;
            }

            resource = new MeetingAudioResource(
                path,
                contentType,
                length,
                Math.Max(0, mixedTrack.DurationSeconds));
            return MeetingAudioResolutionReason.Available;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException)
        {
            return MeetingAudioResolutionReason.ManagedFileUnavailable;
        }
    }

    private bool TryLoadRecordingTranscript(
        MeetingRecording recording,
        out NormalizedTranscript transcript)
    {
        transcript = default!;
        if (string.IsNullOrWhiteSpace(recording.TranscriptFile))
        {
            return false;
        }

        try
        {
            if (recording.MeetId is not Guid meetingId ||
                !string.Equals(
                    recording.RecordingFolderRelativePath.TrimEnd('/'),
                    $"meetings/{meetingId:N}/recordings/{recording.Id:N}",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var path = _storage.ResolveFile(recording, recording.TranscriptFile);
            var meetingsRoot = Path.GetFullPath(_storage.RootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(meetingsRoot, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) ||
                ContainsReparsePoint(meetingsRoot, path))
            {
                return false;
            }

            transcript = JsonSerializer.Deserialize<NormalizedTranscript>(
                             File.ReadAllText(path),
                             _jsonOptions)!;
            return transcript is not null;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            return false;
        }
    }

    private static bool SameManagedFolder(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(
            left.Replace('\\', '/').TrimEnd('/'),
            right.Replace('\\', '/').TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    private void Report(
        MeetingTranscript transcript,
        MeetingAudioResolutionReason reason) =>
        _diagnostic?.Invoke(
            $"Meeting transcript audio rejected: reason={reason}; " +
            $"meetId={transcript.MeetId?.ToString("N") ?? "none"}; " +
            $"transcriptId={transcript.Id:N}.",
            null);

    private static string? ContentTypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            _ => null
        };

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
}

public readonly record struct MeetingAudioByteRange(long Start, long Length)
{
    public long End => Start + Length - 1;

    public static bool TryParse(string? value, long resourceLength, out MeetingAudioByteRange range)
    {
        range = default;
        if (resourceLength <= 0 || string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(','))
        {
            return false;
        }

        var parts = value[6..].Split('-', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        if (parts[0].Length == 0)
        {
            if (!long.TryParse(parts[1], out var suffixLength) || suffixLength <= 0)
            {
                return false;
            }

            var length = Math.Min(suffixLength, resourceLength);
            range = new MeetingAudioByteRange(resourceLength - length, length);
            return true;
        }

        if (!long.TryParse(parts[0], out var start) || start < 0 || start >= resourceLength)
        {
            return false;
        }

        var end = resourceLength - 1;
        if (parts[1].Length > 0 &&
            (!long.TryParse(parts[1], out end) || end < start))
        {
            return false;
        }

        end = Math.Min(end, resourceLength - 1);
        range = new MeetingAudioByteRange(start, end - start + 1);
        return true;
    }
}

internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _length;
    private long _position;

    public BoundedReadStream(Stream inner, long start, long length)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (!inner.CanRead || !inner.CanSeek || start < 0 || length < 0)
        {
            throw new ArgumentException("A readable seekable stream and valid range are required.");
        }

        _start = start;
        _length = length;
        _inner.Position = start;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _length - _position;
        if (remaining <= 0)
        {
            return 0;
        }

        var read = _inner.Read(buffer, offset, (int)Math.Min(count, remaining));
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (target < 0 || target > _length)
        {
            throw new IOException("Seek is outside the response range.");
        }

        _inner.Position = _start + target;
        _position = target;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
