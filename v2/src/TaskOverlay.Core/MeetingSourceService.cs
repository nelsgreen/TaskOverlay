using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TaskOverlay.Core;

public sealed record TranscriptSegmentEdit(int Index, string Text);

public sealed record TranscriptSpeakerEdit(
    string SpeakerId,
    string DisplayName,
    bool IsCurrentUser);

public sealed record TranscriptSpeakerMerge(string FromSpeakerId, string IntoSpeakerId);

/// <summary>
/// One deliberate user save of transcript edits. Segment edits address parent
/// segments by index; Speakers must cover every speaker that remains after the
/// merges; merges must already be resolved to their final targets (no chains).
/// </summary>
public sealed record TranscriptRevisionRequest(
    Guid MeetId,
    Guid TranscriptId,
    Guid ParentRevisionId,
    IReadOnlyList<TranscriptSegmentEdit> SegmentEdits,
    IReadOnlyList<TranscriptSpeakerEdit> Speakers,
    IReadOnlyList<TranscriptSpeakerMerge> Merges);

public sealed record MeetingTranscriptLayout(
    string RelativeFolder,
    string AbsoluteFolder,
    string OriginalPath,
    string NormalizedPath,
    string MarkdownPath);

public sealed class MeetingSourceStorage
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public MeetingSourceStorage(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        StateDirectory = Path.GetFullPath(stateDirectory);
    }

    public string StateDirectory { get; }

    public MeetingTranscriptLayout CreateTranscriptLayout(
        Guid meetId,
        Guid transcriptId,
        string sourceExtension)
    {
        if (meetId == Guid.Empty || transcriptId == Guid.Empty)
        {
            throw new ArgumentException("MEET and transcript ids are required.");
        }

        var extension = NormalizeExtension(sourceExtension);
        var relativeFolder = $"meetings/{meetId:N}/transcripts/{transcriptId:N}";
        var folder = ResolveFolder(relativeFolder);
        Directory.CreateDirectory(folder);
        return new MeetingTranscriptLayout(
            relativeFolder,
            folder,
            Path.Combine(folder, $"original{extension}"),
            Path.Combine(folder, "transcript.json"),
            Path.Combine(folder, "transcript.md"));
    }

    public string CreateScreenshotPath(Guid meetId, Guid screenshotId)
    {
        if (meetId == Guid.Empty || screenshotId == Guid.Empty)
        {
            throw new ArgumentException("MEET and screenshot ids are required.");
        }

        var folder = ResolveFolder($"meetings/{meetId:N}/screenshots");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"{screenshotId:N}.png");
    }

    public string ResolveTranscriptFile(MeetingTranscript transcript, string fileName)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        return ResolveManagedFile(transcript.StorageFolderRelativePath, fileName);
    }

    public string ResolveScreenshotFile(MeetingScreenshot screenshot)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        return ResolveManagedFile(screenshot.StorageFolderRelativePath, screenshot.FileName);
    }

    public string ResolveFolder(string relativeFolder)
    {
        if (!RecordingPathPolicy.IsSafeRelativePath(relativeFolder))
        {
            throw new InvalidDataException("MEET source folder path is not safe.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(
            StateDirectory,
            relativeFolder.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInsideStateDirectory(fullPath);
        return fullPath;
    }

    public void CopyFileAtomic(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("The selected source file no longer exists.", source);
        }

        EnsureInsideStateDirectory(Path.GetFullPath(destinationPath));
        var directory = Path.GetDirectoryName(destinationPath) ??
                        throw new InvalidDataException("Destination has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       81920,
                       FileOptions.WriteThrough))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public void WriteTranscript(MeetingTranscriptLayout layout, NormalizedTranscript transcript)
    {
        var json = JsonSerializer.Serialize(transcript, _jsonOptions);
        MeetingRecordingStorage.WriteTextAtomic(layout.NormalizedPath, json);
        MeetingRecordingStorage.WriteTextAtomic(
            layout.MarkdownPath,
            MeetingTranscriptService.BuildMarkdown(transcript, transcript.Speakers));
    }

    public void WriteJsonAtomic<T>(string path, T value)
    {
        EnsureInsideStateDirectory(Path.GetFullPath(path));
        MeetingRecordingStorage.WriteTextAtomic(
            path,
            JsonSerializer.Serialize(value, _jsonOptions));
    }

    public void WriteTranscript(MeetingTranscript metadata, NormalizedTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var normalizedPath = ResolveTranscriptFile(metadata, metadata.NormalizedArtifactFile);
        var markdownPath = ResolveTranscriptFile(metadata, metadata.MarkdownArtifactFile);
        var json = JsonSerializer.Serialize(transcript, _jsonOptions);
        MeetingRecordingStorage.WriteTextAtomic(normalizedPath, json);
        MeetingRecordingStorage.WriteTextAtomic(
            markdownPath,
            MeetingTranscriptService.BuildMarkdown(transcript, metadata.Speakers));
    }

    public NormalizedTranscript LoadTranscript(MeetingTranscript transcript)
    {
        var path = ResolveTranscriptFile(transcript, transcript.NormalizedArtifactFile);
        var normalized = JsonSerializer.Deserialize<NormalizedTranscript>(
                             File.ReadAllText(path),
                             _jsonOptions) ??
                         throw new InvalidDataException("Normalized transcript is invalid.");
        normalized.TranscriptId = transcript.Id;
        normalized.RevisionId = transcript.RevisionId;
        normalized.Speakers ??= new List<TranscriptSpeaker>();
        normalized.Segments ??= new List<TranscriptSegment>();
        TranscriptSpeakerMapping.EnsureStableSpeakers(normalized);
        return normalized;
    }

    public void DeleteTranscriptFiles(MeetingTranscript transcript)
    {
        var folder = ResolveFolder(transcript.StorageFolderRelativePath);
        var normalizedFolder = transcript.StorageFolderRelativePath
            .Replace('\\', '/')
            .Trim('/');
        var dedicatedFolder = transcript.MeetId is Guid meetId &&
                              string.Equals(
                                  normalizedFolder,
                                  $"meetings/{meetId:N}/transcripts/{transcript.Id:N}",
                                  StringComparison.OrdinalIgnoreCase);
        if (dedicatedFolder && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
            return;
        }

        // Schema-v5 generated transcripts share their recording folder with
        // source audio. Remove only transcript artifacts in that legacy layout.
        foreach (var fileName in new[]
                 {
                     transcript.OriginalArtifactFile,
                     transcript.NormalizedArtifactFile,
                     transcript.MarkdownArtifactFile
                 }
                 .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                 .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = ResolveTranscriptFile(transcript, fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public void DeleteScreenshotFile(MeetingScreenshot screenshot)
    {
        var path = ResolveScreenshotFile(screenshot);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string ResolveManagedFile(string relativeFolder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName) ||
            fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("MEET source file path is not safe.");
        }

        var folder = ResolveFolder(relativeFolder);
        var fullPath = Path.GetFullPath(Path.Combine(folder, fileName));
        EnsureInsideStateDirectory(fullPath);
        return fullPath;
    }

    private void EnsureInsideStateDirectory(string fullPath)
    {
        var root = StateDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("MEET source path escapes the state directory.");
        }
    }

    private static string NormalizeExtension(string value)
    {
        var extension = value.StartsWith(".", StringComparison.Ordinal) ? value : $".{value}";
        if (extension.Length > 10 || extension.Any(character =>
                !char.IsLetterOrDigit(character) && character != '.'))
        {
            throw new InvalidDataException("Source file extension is invalid.");
        }

        return extension.ToLowerInvariant();
    }
}

public sealed class MeetingTranscriptService
{
    private static readonly Regex TimestampLine = new(
        @"^(?<start>[^\s]+)\s+-->\s+(?<end>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VoiceTag = new(
        @"^\s*<v\s+(?<speaker>[^>]+)>(?<text>.*?)(?:</v>)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex SpeakerPrefix = new(
        @"^\s*(?<speaker>[^:\r\n]{1,80}):\s+(?<text>.+)$",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex HtmlTag = new(
        @"</?[^>]+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AppState _state;
    private readonly MeetingSourceStorage _storage;

    public MeetingTranscriptService(AppState state, MeetingSourceStorage storage)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _state.MeetingTranscripts ??= new List<MeetingTranscript>();
    }

    public MeetingTranscript Import(
        Guid meetId,
        string sourcePath,
        string? sourceLabel = null,
        DateTimeOffset? now = null)
    {
        if (!_state.Meetings.Any(meeting => meeting.Id == meetId))
        {
            throw new InvalidDataException("MEET was not found.");
        }

        var source = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var format = extension switch
        {
            ".txt" => MeetingTranscriptFormat.PlainText,
            ".md" => MeetingTranscriptFormat.Markdown,
            ".srt" => MeetingTranscriptFormat.SubRip,
            ".vtt" => MeetingTranscriptFormat.WebVtt,
            _ => throw new InvalidDataException("Supported transcript formats are TXT, MD, SRT, and VTT.")
        };
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("The selected transcript no longer exists.", source);
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var transcript = new MeetingTranscript
        {
            MeetId = meetId,
            Origin = MeetingTranscriptOrigin.Imported,
            Format = format,
            Provider = "External",
            SourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? "External" : sourceLabel.Trim(),
            OriginalFileName = Path.GetFileName(source),
            ImportedAtUtc = timestamp,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp,
            RevisionId = Guid.NewGuid()
        };
        var layout = _storage.CreateTranscriptLayout(meetId, transcript.Id, extension);
        try
        {
            _storage.CopyFileAtomic(source, layout.OriginalPath);
            var content = File.ReadAllText(layout.OriginalPath);
            var normalized = NormalizeImported(transcript, content);
            transcript.StorageFolderRelativePath = layout.RelativeFolder;
            transcript.OriginalArtifactFile = Path.GetFileName(layout.OriginalPath);
            transcript.NormalizedArtifactFile = Path.GetFileName(layout.NormalizedPath);
            transcript.MarkdownArtifactFile = Path.GetFileName(layout.MarkdownPath);
            transcript.Speakers = normalized.Speakers.Select(CloneSpeaker).ToList();
            _storage.WriteTranscript(layout, normalized);
            _state.MeetingTranscripts.Add(transcript);
            var meeting = _state.Meetings.First(item => item.Id == meetId);
            if (meeting.ActiveTranscriptId is null)
            {
                meeting.ActiveTranscriptId = transcript.Id;
                meeting.UpdatedAtUtc = timestamp;
            }

            _state.UpdatedAtUtc = timestamp;
            return transcript;
        }
        catch
        {
            if (Directory.Exists(layout.AbsoluteFolder))
            {
                Directory.Delete(layout.AbsoluteFolder, recursive: true);
            }

            throw;
        }
    }

    /// <summary>
    /// Creates a new immutable user-edited transcript revision derived from the
    /// parent transcript's current revision, writes it as a new managed
    /// artifact, and activates it. The parent record and all of its artifacts
    /// remain untouched; a failure before completion leaves no state change.
    /// </summary>
    public MeetingTranscript SaveRevision(
        TranscriptRevisionRequest request,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var meeting = _state.Meetings.FirstOrDefault(item => item.Id == request.MeetId) ??
                      throw new InvalidDataException("MEET was not found.");
        var parent = _state.MeetingTranscripts.FirstOrDefault(item =>
                         item.Id == request.TranscriptId && item.MeetId == request.MeetId) ??
                     throw new InvalidDataException("Transcript was not found for this MEET.");
        if (parent.RevisionId != request.ParentRevisionId)
        {
            throw new InvalidDataException(
                "The transcript changed since editing started. Reopen the editor and retry.");
        }

        // Editing always starts from the active transcript, and a successful
        // save activates the new revision — so a replayed duplicate submission
        // (whose source is no longer active) is rejected instead of silently
        // creating a second identical revision.
        if (meeting.ActiveTranscriptId != parent.Id)
        {
            throw new InvalidDataException(
                "Only the active transcript can be edited. Reopen the editor and retry.");
        }

        var normalized = _storage.LoadTranscript(parent);
        if (normalized.Segments.Count == 0)
        {
            throw new InvalidDataException("The transcript has no editable segments.");
        }

        var speakerIds = normalized.Speakers
            .Select(speaker => speaker.SpeakerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mergeTargets = ResolveMerges(request.Merges, speakerIds);
        var remainingIds = speakerIds
            .Where(id => !mergeTargets.ContainsKey(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var finalSpeakers = BuildFinalSpeakers(request.Speakers, normalized.Speakers, remainingIds);

        var segmentsByIndex = new Dictionary<int, TranscriptSegment>();
        foreach (var segment in normalized.Segments)
        {
            if (!segmentsByIndex.TryAdd(segment.Index, segment))
            {
                throw new InvalidDataException("The transcript contains duplicate segment indexes.");
            }
        }

        var editedTexts = new Dictionary<int, string>();
        foreach (var edit in request.SegmentEdits)
        {
            if (edit is null || !segmentsByIndex.ContainsKey(edit.Index))
            {
                throw new InvalidDataException("A segment edit references an unknown segment.");
            }

            if (string.IsNullOrWhiteSpace(edit.Text))
            {
                throw new InvalidDataException("Segment text cannot be empty.");
            }

            if (!editedTexts.TryAdd(edit.Index, edit.Text.Trim()))
            {
                throw new InvalidDataException("A segment was edited more than once.");
            }
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var revision = new MeetingTranscript
        {
            MeetId = parent.MeetId,
            RecordingId = parent.RecordingId,
            Origin = MeetingTranscriptOrigin.UserEdited,
            Format = MeetingTranscriptFormat.NormalizedJson,
            Provider = "User",
            SourceLabel = "Edited",
            OriginalFileName = parent.OriginalFileName,
            SourceTranscriptId = parent.Id,
            ParentRevisionId = parent.RevisionId,
            HasTimestamps = normalized.HasTimestamps,
            HasSpeakerLabels = finalSpeakers.Count > 0,
            Speakers = finalSpeakers.Select(CloneSpeaker).ToList(),
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        var newSegments = normalized.Segments
            .OrderBy(segment => segment.Index)
            .Select(segment => new TranscriptSegment
            {
                Index = segment.Index,
                StartSeconds = segment.StartSeconds,
                EndSeconds = segment.EndSeconds,
                Text = editedTexts.TryGetValue(segment.Index, out var edited)
                    ? edited
                    : segment.Text,
                SpeakerId = segment.SpeakerId is string speakerId &&
                            mergeTargets.TryGetValue(speakerId, out var target)
                    ? target
                    : segment.SpeakerId,
                Speaker = segment.Speaker
            })
            .ToList();
        if (newSegments.All(segment => string.IsNullOrWhiteSpace(segment.Text)))
        {
            throw new InvalidDataException("The edited transcript has no usable content.");
        }

        if (newSegments.Any(segment => segment.SpeakerId is string id &&
                                       id.Length > 0 &&
                                       !remainingIds.Contains(id)))
        {
            throw new InvalidDataException(
                "The edited transcript would contain a dangling speaker reference.");
        }

        var newNormalized = new NormalizedTranscript
        {
            TranscriptId = revision.Id,
            RevisionId = revision.RevisionId,
            RecordingId = normalized.RecordingId,
            Provider = normalized.Provider,
            Model = normalized.Model,
            Language = normalized.Language,
            HasTimestamps = normalized.HasTimestamps,
            Segments = newSegments,
            Speakers = finalSpeakers,
            Text = string.Join(
                Environment.NewLine,
                newSegments.Select(segment => segment.Text)),
            GeneratedAtUtc = timestamp
        };

        var layout = _storage.CreateTranscriptLayout(request.MeetId, revision.Id, ".json");
        try
        {
            revision.StorageFolderRelativePath = layout.RelativeFolder;
            revision.OriginalArtifactFile = string.Empty;
            revision.NormalizedArtifactFile = Path.GetFileName(layout.NormalizedPath);
            revision.MarkdownArtifactFile = Path.GetFileName(layout.MarkdownPath);
            _storage.WriteTranscript(layout, newNormalized);
            _state.MeetingTranscripts.Add(revision);
            meeting.ActiveTranscriptId = revision.Id;
            meeting.UpdatedAtUtc = timestamp;
            _state.UpdatedAtUtc = timestamp;
            return revision;
        }
        catch
        {
            if (Directory.Exists(layout.AbsoluteFolder))
            {
                Directory.Delete(layout.AbsoluteFolder, recursive: true);
            }

            throw;
        }
    }

    private static Dictionary<string, string> ResolveMerges(
        IReadOnlyList<TranscriptSpeakerMerge> merges,
        ISet<string> speakerIds)
    {
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var merge in merges)
        {
            if (merge is null ||
                string.IsNullOrWhiteSpace(merge.FromSpeakerId) ||
                string.IsNullOrWhiteSpace(merge.IntoSpeakerId) ||
                !speakerIds.Contains(merge.FromSpeakerId) ||
                !speakerIds.Contains(merge.IntoSpeakerId))
            {
                throw new InvalidDataException("A speaker merge references an unknown speaker.");
            }

            if (string.Equals(
                    merge.FromSpeakerId,
                    merge.IntoSpeakerId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A speaker cannot be merged into itself.");
            }

            if (!targets.TryAdd(merge.FromSpeakerId, merge.IntoSpeakerId))
            {
                throw new InvalidDataException("A speaker was merged more than once.");
            }
        }

        // Merges must arrive resolved to final targets: a merge target may not
        // itself be merged away, which also rules out cycles.
        if (targets.Values.Any(target => targets.ContainsKey(target)))
        {
            throw new InvalidDataException("A speaker merge targets a merged speaker.");
        }

        return targets;
    }

    private static List<TranscriptSpeaker> BuildFinalSpeakers(
        IReadOnlyList<TranscriptSpeakerEdit> edits,
        IReadOnlyList<TranscriptSpeaker> parentSpeakers,
        ISet<string> remainingIds)
    {
        var editsById = new Dictionary<string, TranscriptSpeakerEdit>(StringComparer.OrdinalIgnoreCase);
        foreach (var edit in edits)
        {
            if (edit is null ||
                string.IsNullOrWhiteSpace(edit.SpeakerId) ||
                !remainingIds.Contains(edit.SpeakerId))
            {
                throw new InvalidDataException("A speaker mapping references an unknown speaker.");
            }

            if (string.IsNullOrWhiteSpace(edit.DisplayName))
            {
                throw new InvalidDataException("Speaker display names cannot be empty.");
            }

            if (!editsById.TryAdd(edit.SpeakerId, edit))
            {
                throw new InvalidDataException("A speaker mapping was provided more than once.");
            }
        }

        if (editsById.Count != remainingIds.Count)
        {
            throw new InvalidDataException(
                "Speaker mappings must cover every remaining transcript speaker.");
        }

        if (edits.Count(edit => edit.IsCurrentUser) > 1)
        {
            throw new InvalidDataException("Only one speaker can be marked as You.");
        }

        return parentSpeakers
            .Where(speaker => remainingIds.Contains(speaker.SpeakerId))
            .Select(speaker =>
            {
                var edit = editsById[speaker.SpeakerId];
                return new TranscriptSpeaker
                {
                    SpeakerId = speaker.SpeakerId,
                    OriginalLabel = speaker.OriginalLabel,
                    DisplayName = edit.DisplayName.Trim(),
                    IsCurrentUser = edit.IsCurrentUser
                };
            })
            .ToList();
    }

    public bool SetActive(Guid meetId, Guid transcriptId, DateTimeOffset? now = null)
    {
        var meeting = _state.Meetings.FirstOrDefault(item => item.Id == meetId);
        if (meeting is null || !_state.MeetingTranscripts.Any(transcript =>
                transcript.Id == transcriptId && transcript.MeetId == meetId))
        {
            return false;
        }

        meeting.ActiveTranscriptId = transcriptId;
        meeting.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        _state.UpdatedAtUtc = meeting.UpdatedAtUtc;
        return true;
    }

    public bool Delete(Guid transcriptId, DateTimeOffset? now = null)
    {
        var transcript = _state.MeetingTranscripts.FirstOrDefault(item => item.Id == transcriptId);
        if (transcript is null)
        {
            return false;
        }

        _storage.DeleteTranscriptFiles(transcript);
        _state.MeetingTranscripts.Remove(transcript);
        _state.MeetingAnalyses.RemoveAll(analysis => analysis.TranscriptId == transcriptId);
        var meeting = _state.Meetings.FirstOrDefault(item => item.Id == transcript.MeetId);
        if (meeting?.ActiveTranscriptId == transcriptId)
        {
            meeting.ActiveTranscriptId = _state.MeetingTranscripts
            .Where(item => item.MeetId == transcript.MeetId)
                .OrderByDescending(item => item.CreatedAtUtc)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefault();
            meeting.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        }

        return true;
    }

    public NormalizedTranscript Load(MeetingTranscript transcript) => _storage.LoadTranscript(transcript);

    public static string BuildMarkdown(
        NormalizedTranscript transcript,
        IReadOnlyCollection<TranscriptSpeaker>? mappings)
    {
        var speakers = (mappings ?? transcript.Speakers ?? new List<TranscriptSpeaker>())
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker.SpeakerId))
            .GroupBy(speaker => speaker.SpeakerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder("# Transcript\n\n");
        foreach (var segment in transcript.Segments.OrderBy(segment => segment.Index))
        {
            var timestamp = transcript.HasTimestamps
                ? $"`{TimeSpan.FromSeconds(segment.StartSeconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)}` "
                : string.Empty;
            var speaker = TranscriptSpeakerMapping.ResolveDisplayName(segment, speakers);
            var prefix = speaker.Length == 0 ? string.Empty : $"**{speaker}:** ";
            builder.Append("- ").Append(timestamp).Append(prefix).AppendLine(segment.Text);
        }

        if (transcript.Segments.Count == 0)
        {
            builder.AppendLine(transcript.Text);
        }

        return builder.ToString();
    }

    private static NormalizedTranscript NormalizeImported(
        MeetingTranscript metadata,
        string content)
    {
        var normalized = new NormalizedTranscript
        {
            TranscriptId = metadata.Id,
            RevisionId = metadata.RevisionId,
            Provider = metadata.Provider,
            Model = metadata.SourceLabel,
            GeneratedAtUtc = metadata.CreatedAtUtc
        };
        if (metadata.Format is MeetingTranscriptFormat.PlainText or MeetingTranscriptFormat.Markdown)
        {
            normalized.Text = content;
            normalized.HasTimestamps = false;
            if (!string.IsNullOrWhiteSpace(content))
            {
                normalized.Segments.Add(new TranscriptSegment { Index = 0, Text = content });
            }
        }
        else
        {
            ParseTimedTranscript(content, metadata.Format, normalized, metadata.ImportWarnings);
            if (normalized.Segments.Count == 0)
            {
                throw new InvalidDataException(
                    "The transcript did not contain any valid SRT/VTT cues. The original was not imported.");
            }

            normalized.Text = string.Join(Environment.NewLine,
                normalized.Segments.Select(segment => segment.Text));
            normalized.HasTimestamps = true;
        }

        TranscriptSpeakerMapping.EnsureStableSpeakers(normalized);
        metadata.HasTimestamps = normalized.HasTimestamps;
        metadata.HasSpeakerLabels = normalized.Speakers.Count > 0;
        return normalized;
    }

    private static void ParseTimedTranscript(
        string content,
        MeetingTranscriptFormat format,
        NormalizedTranscript target,
        ICollection<string> warnings)
    {
        var normalizedLines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var blocks = new List<List<string>>();
        var current = new List<string>();
        foreach (var line in normalizedLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current.Count > 0)
                {
                    blocks.Add(current);
                    current = new List<string>();
                }
            }
            else
            {
                current.Add(line.TrimEnd());
            }
        }

        if (current.Count > 0)
        {
            blocks.Add(current);
        }

        foreach (var block in blocks)
        {
            if (format == MeetingTranscriptFormat.WebVtt &&
                block[0].StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var timestampIndex = block.FindIndex(line => TimestampLine.IsMatch(line));
            if (timestampIndex < 0)
            {
                warnings.Add($"Skipped cue without a timestamp near: {Bound(block[0], 80)}");
                continue;
            }

            var match = TimestampLine.Match(block[timestampIndex]);
            if (!TryParseTimestamp(match.Groups["start"].Value, out var start) ||
                !TryParseTimestamp(match.Groups["end"].Value, out var end) || end < start)
            {
                warnings.Add($"Skipped cue with invalid timestamps: {Bound(block[timestampIndex], 80)}");
                continue;
            }

            var rawText = string.Join(Environment.NewLine, block.Skip(timestampIndex + 1)).Trim();
            if (rawText.Length == 0)
            {
                warnings.Add($"Skipped empty cue at {match.Groups["start"].Value}.");
                continue;
            }

            var (speaker, text) = ExtractSpeaker(rawText);
            target.Segments.Add(new TranscriptSegment
            {
                Index = target.Segments.Count,
                StartSeconds = start.TotalSeconds,
                EndSeconds = end.TotalSeconds,
                Text = text,
                Speaker = speaker
            });
        }
    }

    private static (string? Speaker, string Text) ExtractSpeaker(string rawText)
    {
        var voice = VoiceTag.Match(rawText);
        if (voice.Success)
        {
            return (
                voice.Groups["speaker"].Value.Trim(),
                HtmlTag.Replace(voice.Groups["text"].Value, string.Empty).Trim());
        }

        var plain = HtmlTag.Replace(rawText, string.Empty).Trim();
        var prefix = SpeakerPrefix.Match(plain);
        if (prefix.Success && !prefix.Groups["speaker"].Value.Contains("//", StringComparison.Ordinal))
        {
            return (prefix.Groups["speaker"].Value.Trim(), prefix.Groups["text"].Value.Trim());
        }

        return (null, plain);
    }

    private static bool TryParseTimestamp(string value, out TimeSpan timestamp)
    {
        var normalized = value.Trim().Replace(',', '.');
        var formats = new[] { @"hh\:mm\:ss\.fff", @"h\:mm\:ss\.fff", @"mm\:ss\.fff" };
        return TimeSpan.TryParseExact(
            normalized,
            formats,
            CultureInfo.InvariantCulture,
            out timestamp);
    }

    private static TranscriptSpeaker CloneSpeaker(TranscriptSpeaker source) => new()
    {
        SpeakerId = source.SpeakerId,
        OriginalLabel = source.OriginalLabel,
        DisplayName = source.DisplayName,
        IsCurrentUser = source.IsCurrentUser
    };

    private static string Bound(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}
