using System;
using System.IO;
using System.Linq;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed record MeetingAudioResource(
    string FilePath,
    string ContentType,
    long Length,
    double DurationSeconds);

/// <summary>
/// Resolves the opaque Workspace audio endpoint to the mixed track linked to
/// the currently active transcript. No filesystem path crosses into React.
/// </summary>
public sealed class MeetingAudioResourceResolver
{
    public const string ResourcePathPrefix = "/__meeting-audio/";

    private readonly AppState _state;
    private readonly MeetingRecordingStorage _storage;

    public MeetingAudioResourceResolver(AppState state, string stateDirectory)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _storage = new MeetingRecordingStorage(stateDirectory);
    }

    public WorkspaceTranscriptAudioSnapshot Describe(MeetingTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (transcript.RecordingId is not Guid recordingId)
        {
            return new WorkspaceTranscriptAudioSnapshot(
                WorkspaceTranscriptAudioSnapshot.NotLinked,
                null,
                0);
        }

        return TryResolveRecording(recordingId, out var resource)
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
    {
        resource = default!;
        try
        {
            var recording = _state.MeetingRecordings.SingleOrDefault(item =>
                item.Id == recordingId);
            if (recording is null || recording.MeetId is not Guid meetingId)
            {
                return false;
            }

            var meeting = _state.Meetings.SingleOrDefault(item => item.Id == meetingId);
            var activeTranscript = meeting?.ActiveTranscriptId is Guid transcriptId
                ? _state.MeetingTranscripts.SingleOrDefault(item =>
                    item.Id == transcriptId && item.MeetId == meetingId)
                : null;
            if (activeTranscript?.RecordingId != recordingId)
            {
                return false;
            }

            var expectedRelativeFolder =
                $"meetings/{meetingId:N}/recordings/{recordingId:N}";
            if (!string.Equals(
                    recording.RecordingFolderRelativePath.TrimEnd('/'),
                    expectedRelativeFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
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
                return false;
            }

            var contentType = ContentTypeFor(mixedTrack.FileName);
            if (contentType is null)
            {
                return false;
            }

            var path = _storage.ResolveFile(recording, mixedTrack.FileName);
            var meetingsRoot = Path.GetFullPath(_storage.RootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(meetingsRoot, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) ||
                ContainsReparsePoint(meetingsRoot, path))
            {
                return false;
            }

            var length = new FileInfo(path).Length;
            if (length <= 0)
            {
                return false;
            }

            resource = new MeetingAudioResource(
                path,
                contentType,
                length,
                Math.Max(0, mixedTrack.DurationSeconds));
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException)
        {
            return false;
        }
    }

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
