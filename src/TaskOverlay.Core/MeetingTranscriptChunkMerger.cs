using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TaskOverlay.Core;

/// <summary>
/// One chunk's provider result, already expressed on the original recording
/// timeline.
/// </summary>
public sealed record MeetingTranscriptChunkInput(
    int Index,
    double SourceStartSeconds,
    double SourceEndSeconds,
    string Text,
    string? DetectedLanguage,
    IReadOnlyList<TranscriptSegment> Segments);

public sealed record MeetingTranscriptMergeResult(
    IReadOnlyList<TranscriptSegment> Segments,
    string Text,
    string? DetectedLanguage,
    int RemovedOverlapDuplicateCount,
    int MappedSpeakerCount,
    int UnresolvedSpeakerCount);

/// <summary>
/// Deterministic merge of sequential chunk transcripts.
///
/// This is intentionally pure and arithmetic - no AI request is used to merge,
/// deduplicate, or map speakers. Every decision comes from timestamps and
/// normalized text.
/// </summary>
public static class MeetingTranscriptChunkMerger
{
    /// <summary>
    /// How far apart two segments may start and still be considered the same
    /// utterance heard by two adjacent requests.
    /// </summary>
    public const double DuplicateStartToleranceSeconds = 2.5;

    /// <summary>
    /// Minimum length of a normalized text before it is trusted as speaker
    /// evidence. Very short interjections ("yes", "ok") repeat constantly and
    /// would produce false speaker mappings.
    /// </summary>
    public const int MinimumSpeakerEvidenceLength = 12;

    public static MeetingTranscriptMergeResult Merge(
        IReadOnlyList<MeetingTranscriptChunkInput> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var ordered = chunks.OrderBy(chunk => chunk.Index).ToList();
        if (ordered.Count == 0)
        {
            return new MeetingTranscriptMergeResult(
                Array.Empty<TranscriptSegment>(),
                string.Empty,
                null,
                0,
                0,
                0);
        }

        var merged = new List<TranscriptSegment>();
        var texts = new List<string>();
        var removedDuplicates = 0;
        var mappedSpeakers = 0;
        var unresolvedSpeakers = 0;

        // Canonical speaker labels are allocated once for the whole merged
        // transcript. Chunk-local provider labels ("speaker_0", "S1", ...) may
        // restart or disagree between requests, so they are never used
        // directly in the output.
        var canonicalLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        var canonicalCounter = 0;

        for (var chunkPosition = 0; chunkPosition < ordered.Count; chunkPosition++)
        {
            var chunk = ordered[chunkPosition];
            var previous = chunkPosition == 0 ? null : ordered[chunkPosition - 1];
            var overlapStart = previous is null
                ? double.NegativeInfinity
                : chunk.SourceStartSeconds;
            var overlapEnd = previous?.SourceEndSeconds ?? double.NegativeInfinity;

            var localMapping = new Dictionary<string, string>(StringComparer.Ordinal);
            if (chunkPosition == 0)
            {
                // First chunk defines the initial canonical identities.
                foreach (var label in DistinctLabels(chunk.Segments))
                {
                    localMapping[label] = AllocateCanonical(
                        canonicalLabels,
                        chunk.Index,
                        label,
                        ref canonicalCounter);
                }
            }
            else
            {
                var evidence = BuildSpeakerEvidence(
                    merged,
                    chunk,
                    overlapStart,
                    overlapEnd);
                foreach (var label in DistinctLabels(chunk.Segments))
                {
                    if (evidence.TryGetValue(label, out var votes) &&
                        TryResolveUnambiguous(votes, out var canonical))
                    {
                        localMapping[label] = canonical;
                        mappedSpeakers++;
                        continue;
                    }

                    // Not enough deterministic evidence. Preserving a distinct
                    // unresolved identity is correct; guessing by label order
                    // would silently merge two different people.
                    localMapping[label] = AllocateCanonical(
                        canonicalLabels,
                        chunk.Index,
                        label,
                        ref canonicalCounter);
                    unresolvedSpeakers++;
                }
            }

            foreach (var segment in chunk.Segments.OrderBy(item => item.StartSeconds))
            {
                var text = segment.Text?.Trim() ?? string.Empty;
                if (text.Length == 0)
                {
                    continue;
                }

                var start = Math.Max(0, segment.StartSeconds);
                var end = Math.Max(start, segment.EndSeconds);

                // Deduplicate only inside the real overlap window. Repeated
                // speech outside it is legitimate content and is preserved.
                var insideOverlap = previous is not null &&
                    start >= overlapStart - DuplicateStartToleranceSeconds &&
                    start <= overlapEnd + DuplicateStartToleranceSeconds;
                if (insideOverlap && IsDuplicate(merged, start, text))
                {
                    removedDuplicates++;
                    continue;
                }

                var originalLabel = NormalizeLabel(segment);
                string? canonicalLabel = null;
                if (originalLabel.Length > 0 &&
                    localMapping.TryGetValue(originalLabel, out var mapped))
                {
                    canonicalLabel = mapped;
                }

                merged.Add(new TranscriptSegment
                {
                    StartSeconds = start,
                    EndSeconds = end,
                    Text = text,
                    // Speaker carries the canonical label; SpeakerId is
                    // allocated afterwards by the existing
                    // TranscriptSpeakerMapping.EnsureStableSpeakers contract.
                    Speaker = canonicalLabel
                });
            }

            var chunkText = chunk.Text?.Trim() ?? string.Empty;
            if (chunkText.Length > 0)
            {
                texts.Add(chunkText);
            }
        }

        var final = merged
            .OrderBy(segment => segment.StartSeconds)
            .ThenBy(segment => segment.EndSeconds)
            .ToList();
        for (var index = 0; index < final.Count; index++)
        {
            final[index].Index = index;
            if (final[index].EndSeconds < final[index].StartSeconds)
            {
                final[index].EndSeconds = final[index].StartSeconds;
            }
        }

        var detected = ordered
            .Select(chunk => chunk.DetectedLanguage)
            .FirstOrDefault(language => !string.IsNullOrWhiteSpace(language));

        return new MeetingTranscriptMergeResult(
            final,
            BuildText(final, texts),
            detected,
            removedDuplicates,
            mappedSpeakers,
            unresolvedSpeakers);
    }

    /// <summary>
    /// Rebases original-recording-timeline segments onto the transcript's own
    /// timeline. Existing durable transcripts are transcript-relative (C# adds
    /// <c>SourceAudioStartSeconds</c> back for playback and screenshots), so a
    /// job over a range that does not begin at zero must be rebased before the
    /// final revision is written.
    /// </summary>
    public static IReadOnlyList<TranscriptSegment> RebaseToTranscriptTimeline(
        IReadOnlyList<TranscriptSegment> segments,
        double rangeStartSeconds)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var result = new List<TranscriptSegment>(segments.Count);
        foreach (var segment in segments)
        {
            var start = Math.Max(0, segment.StartSeconds - rangeStartSeconds);
            var end = Math.Max(start, segment.EndSeconds - rangeStartSeconds);
            result.Add(new TranscriptSegment
            {
                Index = result.Count,
                StartSeconds = start,
                EndSeconds = end,
                Text = segment.Text,
                SpeakerId = segment.SpeakerId,
                Speaker = segment.Speaker
            });
        }

        return result;
    }

    /// <summary>
    /// Collects, for each provider label in the current chunk, how often it
    /// lines up with an already-canonical label on a matching overlap
    /// utterance.
    /// </summary>
    private static Dictionary<string, Dictionary<string, int>> BuildSpeakerEvidence(
        IReadOnlyList<TranscriptSegment> merged,
        MeetingTranscriptChunkInput chunk,
        double overlapStart,
        double overlapEnd)
    {
        var evidence = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        if (overlapEnd <= overlapStart)
        {
            return evidence;
        }

        var candidates = merged
            .Where(segment =>
                segment.StartSeconds >= overlapStart - DuplicateStartToleranceSeconds &&
                segment.StartSeconds <= overlapEnd + DuplicateStartToleranceSeconds &&
                !string.IsNullOrWhiteSpace(segment.Speaker))
            .ToList();
        if (candidates.Count == 0)
        {
            return evidence;
        }

        foreach (var segment in chunk.Segments)
        {
            var label = NormalizeLabel(segment);
            if (label.Length == 0)
            {
                continue;
            }

            if (segment.StartSeconds < overlapStart - DuplicateStartToleranceSeconds ||
                segment.StartSeconds > overlapEnd + DuplicateStartToleranceSeconds)
            {
                continue;
            }

            var normalized = NormalizeText(segment.Text);
            if (normalized.Length < MinimumSpeakerEvidenceLength)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (!string.Equals(
                        NormalizeText(candidate.Text),
                        normalized,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (Math.Abs(candidate.StartSeconds - segment.StartSeconds) >
                    DuplicateStartToleranceSeconds)
                {
                    continue;
                }

                if (!evidence.TryGetValue(label, out var votes))
                {
                    votes = new Dictionary<string, int>(StringComparer.Ordinal);
                    evidence[label] = votes;
                }

                votes.TryGetValue(candidate.Speaker!, out var current);
                votes[candidate.Speaker!] = current + 1;
            }
        }

        return evidence;
    }

    /// <summary>
    /// Accepts a mapping only when the overlap evidence points at exactly one
    /// canonical speaker. Two different targets with equal support is
    /// ambiguous and is deliberately left unmapped.
    /// </summary>
    private static bool TryResolveUnambiguous(
        Dictionary<string, int> votes,
        out string canonical)
    {
        canonical = string.Empty;
        if (votes.Count == 0)
        {
            return false;
        }

        var ranked = votes
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();
        if (ranked.Count > 1 && ranked[0].Value == ranked[1].Value)
        {
            return false;
        }

        canonical = ranked[0].Key;
        return true;
    }

    private static bool IsDuplicate(
        IReadOnlyList<TranscriptSegment> merged,
        double start,
        string text)
    {
        var normalized = NormalizeText(text);
        if (normalized.Length == 0)
        {
            return false;
        }

        for (var index = merged.Count - 1; index >= 0; index--)
        {
            var existing = merged[index];
            if (existing.StartSeconds < start - DuplicateStartToleranceSeconds -
                DuplicateStartToleranceSeconds)
            {
                // merged is appended in non-decreasing start order, so once we
                // are far enough behind no earlier segment can match.
                break;
            }

            if (Math.Abs(existing.StartSeconds - start) > DuplicateStartToleranceSeconds)
            {
                continue;
            }

            if (string.Equals(
                    NormalizeText(existing.Text),
                    normalized,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> DistinctLabels(
        IReadOnlyList<TranscriptSegment> segments) =>
        segments
            .Select(NormalizeLabel)
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal);

    private static string AllocateCanonical(
        Dictionary<string, string> canonicalLabels,
        int chunkIndex,
        string label,
        ref int counter)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"{chunkIndex}:{label}");
        if (canonicalLabels.TryGetValue(key, out var existing))
        {
            return existing;
        }

        counter++;
        var canonical = string.Create(CultureInfo.InvariantCulture, $"Speaker {counter}");
        canonicalLabels[key] = canonical;
        return canonical;
    }

    private static string NormalizeLabel(TranscriptSegment segment)
    {
        if (!string.IsNullOrWhiteSpace(segment.Speaker))
        {
            return segment.Speaker.Trim();
        }

        return string.IsNullOrWhiteSpace(segment.SpeakerId)
            ? string.Empty
            : segment.SpeakerId.Trim();
    }

    /// <summary>
    /// Casing, punctuation, and whitespace differ between provider requests
    /// for the same utterance, so text comparison is normalized before use.
    /// </summary>
    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSpace = false;
                continue;
            }

            if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildText(
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<string> chunkTexts)
    {
        if (segments.Count > 0)
        {
            // Prefer the deduplicated segment sequence so the plain-text body
            // never contains the overlap twice.
            return string.Join(
                Environment.NewLine,
                segments.Select(segment => segment.Text));
        }

        return string.Join(Environment.NewLine, chunkTexts);
    }
}
