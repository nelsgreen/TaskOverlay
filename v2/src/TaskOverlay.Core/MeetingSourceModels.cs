using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TaskOverlay.Core;

public enum MeetingTranscriptOrigin
{
    Generated,
    Imported
}

public enum MeetingTranscriptFormat
{
    NormalizedJson,
    PlainText,
    Markdown,
    SubRip,
    WebVtt
}

public enum MeetingScreenshotSourceKind
{
    Window,
    Display
}

/// <summary>
/// A durable transcript version. OriginalArtifactFile always points at an
/// unchanged provider/import artifact; display speaker mappings live in state
/// and are applied when normalized text is rendered or analyzed.
/// </summary>
public sealed class MeetingTranscript
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MeetId { get; set; }
    public Guid? RecordingId { get; set; }
    public MeetingTranscriptOrigin Origin { get; set; }
    public MeetingTranscriptFormat Format { get; set; } = MeetingTranscriptFormat.NormalizedJson;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public DateTimeOffset? ImportedAtUtc { get; set; }
    public string StorageFolderRelativePath { get; set; } = string.Empty;
    public string OriginalArtifactFile { get; set; } = string.Empty;
    public string NormalizedArtifactFile { get; set; } = string.Empty;
    public string MarkdownArtifactFile { get; set; } = string.Empty;
    public bool HasTimestamps { get; set; }
    public bool HasSpeakerLabels { get; set; }
    public Guid RevisionId { get; set; } = Guid.NewGuid();
    public List<TranscriptSpeaker> Speakers { get; set; } = new();
    public List<string> ImportWarnings { get; set; } = new();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TranscriptSpeaker
{
    public string SpeakerId { get; set; } = string.Empty;
    public string OriginalLabel { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsCurrentUser { get; set; }
}

public sealed class MeetingScreenshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MeetId { get; set; }
    public Guid? RecordingId { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public double? OffsetFromRecordingStartSeconds { get; set; }
    public string StorageFolderRelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public MeetingScreenshotSourceKind SourceKind { get; set; }
    public string SourceLabel { get; set; } = string.Empty;
    public long Bytes { get; set; }
}

public static class TranscriptSpeakerMapping
{
    public static bool EnsureStableSpeakers(NormalizedTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        transcript.Segments ??= new List<TranscriptSegment>();
        transcript.Speakers ??= new List<TranscriptSpeaker>();
        var changed = false;
        var usedIds = transcript.Speakers
            .Select(speaker => speaker.SpeakerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var speaker in transcript.Speakers)
        {
            if (string.IsNullOrWhiteSpace(speaker.SpeakerId))
            {
                speaker.SpeakerId = CreateSpeakerId(speaker.OriginalLabel, usedIds);
                usedIds.Add(speaker.SpeakerId);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(speaker.DisplayName))
            {
                speaker.DisplayName = speaker.OriginalLabel.Trim();
                changed = true;
            }
        }

        var speakersByOriginal = transcript.Speakers
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker.OriginalLabel))
            .GroupBy(speaker => speaker.OriginalLabel.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var segment in transcript.Segments)
        {
            var original = segment.Speaker?.Trim() ?? string.Empty;
            if (original.Length == 0)
            {
                continue;
            }

            if (!speakersByOriginal.TryGetValue(original, out var speaker))
            {
                var speakerId = CreateSpeakerId(original, usedIds);
                speaker = new TranscriptSpeaker
                {
                    SpeakerId = speakerId,
                    OriginalLabel = original,
                    DisplayName = original
                };
                transcript.Speakers.Add(speaker);
                speakersByOriginal.Add(original, speaker);
                usedIds.Add(speakerId);
                changed = true;
            }

            if (!string.Equals(segment.SpeakerId, speaker.SpeakerId, StringComparison.Ordinal))
            {
                segment.SpeakerId = speaker.SpeakerId;
                changed = true;
            }
        }

        return changed;
    }

    public static string BuildAnalysisText(
        NormalizedTranscript transcript,
        IReadOnlyCollection<TranscriptSpeaker>? mappings = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var speakers = (mappings ?? transcript.Speakers ?? new List<TranscriptSpeaker>())
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker.SpeakerId))
            .GroupBy(speaker => speaker.SpeakerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (transcript.Segments is null || transcript.Segments.Count == 0)
        {
            return transcript.Text ?? string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var segment in transcript.Segments.OrderBy(segment => segment.Index))
        {
            var label = ResolveDisplayName(segment, speakers);
            if (!string.IsNullOrWhiteSpace(label))
            {
                builder.Append(label).Append(": ");
            }

            builder.AppendLine(segment.Text?.Trim() ?? string.Empty);
        }

        return builder.ToString().TrimEnd();
    }

    public static string ResolveDisplayName(
        TranscriptSegment segment,
        IReadOnlyDictionary<string, TranscriptSpeaker> speakers)
    {
        if (!string.IsNullOrWhiteSpace(segment.SpeakerId) &&
            speakers.TryGetValue(segment.SpeakerId, out var speaker))
        {
            return string.IsNullOrWhiteSpace(speaker.DisplayName)
                ? speaker.OriginalLabel
                : speaker.DisplayName;
        }

        return segment.Speaker?.Trim() ?? string.Empty;
    }

    private static string CreateSpeakerId(string originalLabel, ISet<string> usedIds)
    {
        var normalized = new string((originalLabel ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        if (normalized.Length > 24)
        {
            normalized = normalized[..24];
        }

        if (normalized.Length == 0)
        {
            var hash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(originalLabel ?? string.Empty)))[..8]
                .ToLowerInvariant();
            normalized = $"unknown-{hash}";
        }

        var root = $"speaker-{normalized}";
        var candidate = root;
        var suffix = 2;
        while (usedIds.Contains(candidate))
        {
            candidate = $"{root}-{suffix.ToString(CultureInfo.InvariantCulture)}";
            suffix++;
        }

        return candidate;
    }
}
