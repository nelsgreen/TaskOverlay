using System;
using System.Collections.Generic;

namespace TaskOverlay.Core;

/// <summary>
/// Describes artifacts abandoned by an invalidated, deleted, or orphaned job
/// so the caller can delete them. The job folder is the cleanup granularity:
/// it also captures temporary extraction files that never reached a chunk
/// record.
/// </summary>
public sealed record MeetingTranscriptionJobCleanup(
    Guid RecordingId,
    string JobFolderRelativePath);

public enum MeetingTranscriptionJobState
{
    Pending,
    Running,
    Failed,
    Completed
}

/// <summary>
/// One planned chunk plus, once it succeeds, a pointer to its persisted
/// partial result. The provider payload itself never enters state.json - only
/// bounded metadata and a managed relative file name, matching the existing
/// "payloads stay in the recording folder" rule.
/// </summary>
public sealed class MeetingTranscriptionChunkRecord
{
    public int Index { get; set; }
    public double SourceStartSeconds { get; set; }
    public double SourceEndSeconds { get; set; }

    /// <summary>Actual measured duration of the extracted file that was submitted.</summary>
    public double MeasuredDurationSeconds { get; set; }

    /// <summary>Managed partial-result file name inside the job folder.</summary>
    public string ResultFileName { get; set; } = string.Empty;

    public int SegmentCount { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public MeetingTranscriptionChunkRecord Clone() => new()
    {
        Index = Index,
        SourceStartSeconds = SourceStartSeconds,
        SourceEndSeconds = SourceEndSeconds,
        MeasuredDurationSeconds = MeasuredDurationSeconds,
        ResultFileName = ResultFileName,
        SegmentCount = SegmentCount,
        IsCompleted = IsCompleted,
        CompletedAtUtc = CompletedAtUtc
    };
}

/// <summary>
/// Durable state of a chunked transcription job. Its purpose is resume: a
/// chunk that already succeeded must survive a later chunk's failure, the
/// MEET modal closing, and application restart.
/// </summary>
public sealed class MeetingTranscriptionJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecordingId { get; set; }
    public Guid? MeetId { get; set; }

    /// <summary>
    /// Deterministic identity of every material input. Partial results are
    /// reusable only while this matches.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    public int PolicyVersion { get; set; } = MeetingTranscriptionChunkPolicy.PolicyVersion;
    public double RangeStartSeconds { get; set; }
    public double RangeEndSeconds { get; set; }

    /// <summary>Job-relative managed folder holding partial chunk results.</summary>
    public string JobFolderRelativePath { get; set; } = string.Empty;

    public List<MeetingTranscriptionChunkRecord> Chunks { get; set; } = new();

    public MeetingTranscriptionJobState State { get; set; } =
        MeetingTranscriptionJobState.Pending;

    /// <summary>
    /// Set only after the single final transcript revision is durably
    /// persisted. Its presence is what makes finalization idempotent across
    /// retry and restart.
    /// </summary>
    public Guid? FinalTranscriptId { get; set; }

    public Guid? FinalTranscriptRevisionId { get; set; }

    /// <summary>Sanitized failure description. Raw provider text never lands here.</summary>
    public string LastError { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public int TotalChunkCount => Chunks.Count;

    public int CompletedChunkCount
    {
        get
        {
            var completed = 0;
            foreach (var chunk in Chunks)
            {
                if (chunk.IsCompleted)
                {
                    completed++;
                }
            }

            return completed;
        }
    }

    public bool IsFinalized => FinalTranscriptId is not null;
}

/// <summary>
/// Partial provider result for one chunk, stored as a managed JSON artifact.
/// Segment timestamps are already converted onto the original recording
/// timeline before this is written, so a resumed job never has to re-derive
/// chunk offsets.
/// </summary>
public sealed class MeetingTranscriptionChunkPayload
{
    public int Index { get; set; }
    public double SourceStartSeconds { get; set; }
    public double SourceEndSeconds { get; set; }
    public double MeasuredDurationSeconds { get; set; }
    public string RawJson { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? DetectedLanguage { get; set; }
    public List<TranscriptSegment> Segments { get; set; } = new();
}
