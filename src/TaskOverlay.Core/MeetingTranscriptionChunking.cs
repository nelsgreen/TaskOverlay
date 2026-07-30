using System;
using System.Collections.Generic;
using System.Globalization;

namespace TaskOverlay.Core;

/// <summary>
/// Duration policy for chunked transcription.
///
/// The historical path bounded transcription fragments only by *bytes*
/// (<c>MaximumChunkBytes</c>). At the default compact-recording bitrate a
/// 20 MiB budget maps to 20 MiB * 85% * 8 / 96000 = 1485.4827 seconds of
/// audio, which is above the provider's per-request duration limit. Duration
/// is therefore an independent, primary bound here: bytes alone can never
/// keep a fragment inside a provider's duration limit.
/// </summary>
public static class MeetingTranscriptionChunkPolicy
{
    /// <summary>
    /// Bump whenever the boundary math below changes. Persisted partial
    /// results produced under an older policy are not reusable.
    /// </summary>
    public const int PolicyVersion = 1;

    /// <summary>
    /// Hard safe ceiling for the measured duration of any submitted file.
    /// Deliberately below the provider's advertised 1400 s limit so container,
    /// codec, rounding, and seek behaviour cannot push a real file over it.
    /// </summary>
    public const double MaxChunkSeconds = 1200.0;

    /// <summary>Target overlap between consecutive chunks.</summary>
    public const double OverlapSeconds = 10.0;

    /// <summary>
    /// Tolerance applied when validating a *measured* file duration. Encoders
    /// pad the final AAC frame, so an exact comparison would reject valid
    /// output; the margin stays far below the provider limit.
    /// </summary>
    public const double MeasurementToleranceSeconds = 0.75;

    /// <summary>Shortest range that is still worth splitting.</summary>
    public const double MinimumSplittableSeconds = 1.0;
}

/// <summary>
/// User-facing failure text for chunked transcription. Raw provider errors
/// (HTTP bodies, model ids, request ids) never reach persisted state or the
/// Workspace snapshot; they stay in the diagnostics/log path.
/// </summary>
public static class MeetingTranscriptionFailureMessage
{
    public static string ForPart(int partNumber, int totalParts)
    {
        if (partNumber <= 0 || totalParts <= 0)
        {
            return "Could not transcribe the recording.";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "Could not transcribe part {0} of {1}.",
            partNumber,
            totalParts);
    }
}

/// <summary>One planned chunk, in seconds on the source-file timeline.</summary>
public sealed record MeetingTranscriptionChunkPlanItem(
    int Index,
    double SourceStartSeconds,
    double SourceEndSeconds)
{
    public double PlannedDurationSeconds => SourceEndSeconds - SourceStartSeconds;
}

public static class MeetingTranscriptionChunkPlanner
{
    /// <summary>
    /// Splits <paramref name="rangeStartSeconds"/>..<paramref name="rangeEndSeconds"/>
    /// into sequential overlapping chunks that each stay within the safe
    /// duration limit.
    ///
    /// The selected range is the outer boundary of the whole job: chunk 0
    /// starts exactly at the range start and the last chunk ends exactly at
    /// the range end. Chunk length is distributed evenly rather than greedily
    /// so a long recording never ends with a degenerate few-second tail.
    /// </summary>
    public static IReadOnlyList<MeetingTranscriptionChunkPlanItem> Plan(
        double rangeStartSeconds,
        double rangeEndSeconds,
        double maxChunkSeconds = MeetingTranscriptionChunkPolicy.MaxChunkSeconds,
        double overlapSeconds = MeetingTranscriptionChunkPolicy.OverlapSeconds)
    {
        if (double.IsNaN(rangeStartSeconds) || double.IsNaN(rangeEndSeconds) ||
            double.IsInfinity(rangeStartSeconds) || double.IsInfinity(rangeEndSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rangeStartSeconds),
                "The transcription range must be finite.");
        }

        if (rangeStartSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rangeStartSeconds),
                "The transcription range cannot start before zero.");
        }

        var total = rangeEndSeconds - rangeStartSeconds;
        if (total <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rangeEndSeconds),
                "The transcription range must end after it starts.");
        }

        if (maxChunkSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChunkSeconds));
        }

        if (overlapSeconds < 0 || overlapSeconds >= maxChunkSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overlapSeconds),
                "Overlap must be non-negative and shorter than one chunk.");
        }

        if (total <= maxChunkSeconds ||
            total < MeetingTranscriptionChunkPolicy.MinimumSplittableSeconds)
        {
            return new[]
            {
                new MeetingTranscriptionChunkPlanItem(0, rangeStartSeconds, rangeEndSeconds)
            };
        }

        // count = smallest n such that n chunks with (n-1) overlaps cover the
        // range without any chunk exceeding maxChunkSeconds.
        var stride = maxChunkSeconds - overlapSeconds;
        var count = (int)Math.Ceiling((total - overlapSeconds) / stride);
        count = Math.Max(2, count);

        // Even distribution: every chunk gets the same length, so the last
        // chunk is never a degenerate remainder.
        var chunkLength = (total + ((count - 1) * overlapSeconds)) / count;
        var effectiveStride = chunkLength - overlapSeconds;

        var items = new List<MeetingTranscriptionChunkPlanItem>(count);
        for (var index = 0; index < count; index++)
        {
            var start = rangeStartSeconds + (index * effectiveStride);
            var end = start + chunkLength;
            if (index == count - 1)
            {
                // Absorb accumulated floating-point drift at the outer edge so
                // the job ends exactly on the selected range boundary.
                end = rangeEndSeconds;
            }

            if (index == 0)
            {
                start = rangeStartSeconds;
            }

            items.Add(new MeetingTranscriptionChunkPlanItem(index, start, end));
        }

        return items;
    }

    /// <summary>
    /// Stable textual form of a plan, used inside the job fingerprint so a
    /// policy or boundary change invalidates persisted partial results.
    /// </summary>
    public static string Describe(IReadOnlyList<MeetingTranscriptionChunkPlanItem> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var parts = new List<string>(plan.Count);
        foreach (var item in plan)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{item.Index}:{item.SourceStartSeconds:F3}-{item.SourceEndSeconds:F3}"));
        }

        return string.Join("|", parts);
    }
}
