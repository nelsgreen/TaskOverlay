using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

/// <summary>
/// Owns the durable lifecycle of chunked transcription jobs inside
/// <see cref="AppState"/>: resume, invalidation, retry, and cleanup.
///
/// Removal returns <see cref="MeetingTranscriptionJobCleanup"/> descriptors
/// instead of touching the filesystem, so this stays free of I/O and fully
/// synthetic-testable; the caller deletes the described artifacts.
/// </summary>
public sealed class MeetingTranscriptionJobService
{
    private readonly AppState _state;

    public MeetingTranscriptionJobService(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _state.MeetingTranscriptionJobs ??= new List<MeetingTranscriptionJob>();
    }

    public MeetingTranscriptionJob? Find(Guid recordingId) =>
        _state.MeetingTranscriptionJobs.FirstOrDefault(job => job.RecordingId == recordingId);

    /// <summary>
    /// Returns the job to run. A persisted job whose fingerprint still matches
    /// is resumed with its completed chunks intact; anything else is replaced,
    /// and the abandoned artifacts are reported for deletion.
    /// </summary>
    public MeetingTranscriptionJob Resolve(
        Guid recordingId,
        Guid? meetId,
        string fingerprint,
        double rangeStartSeconds,
        double rangeEndSeconds,
        string jobFolderRelativePath,
        IReadOnlyList<MeetingTranscriptionChunkPlanItem> plan,
        out IReadOnlyList<MeetingTranscriptionJobCleanup> invalidated,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var existing = Find(recordingId);
        if (existing is not null && IsReusable(existing, fingerprint, plan))
        {
            existing.MeetId = meetId;
            existing.JobFolderRelativePath = jobFolderRelativePath;
            existing.UpdatedAtUtc = timestamp;
            invalidated = Array.Empty<MeetingTranscriptionJobCleanup>();
            return existing;
        }

        invalidated = existing is null
            ? Array.Empty<MeetingTranscriptionJobCleanup>()
            : new[] { Describe(existing) };
        if (existing is not null)
        {
            _state.MeetingTranscriptionJobs.Remove(existing);
        }

        var job = new MeetingTranscriptionJob
        {
            Id = Guid.NewGuid(),
            RecordingId = recordingId,
            MeetId = meetId,
            Fingerprint = fingerprint,
            PolicyVersion = MeetingTranscriptionChunkPolicy.PolicyVersion,
            RangeStartSeconds = rangeStartSeconds,
            RangeEndSeconds = rangeEndSeconds,
            JobFolderRelativePath = jobFolderRelativePath,
            State = MeetingTranscriptionJobState.Pending,
            Chunks = plan
                .Select(item => new MeetingTranscriptionChunkRecord
                {
                    Index = item.Index,
                    SourceStartSeconds = item.SourceStartSeconds,
                    SourceEndSeconds = item.SourceEndSeconds
                })
                .ToList(),
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        _state.MeetingTranscriptionJobs.Add(job);
        return job;
    }

    /// <summary>
    /// A persisted job is reusable only when every material input still
    /// matches. The plan is compared as well: identical inputs must produce
    /// identical boundaries, and a mismatch means the persisted partials
    /// describe different audio intervals.
    /// </summary>
    private static bool IsReusable(
        MeetingTranscriptionJob job,
        string fingerprint,
        IReadOnlyList<MeetingTranscriptionChunkPlanItem> plan)
    {
        if (!string.Equals(job.Fingerprint, fingerprint, StringComparison.Ordinal) ||
            job.PolicyVersion != MeetingTranscriptionChunkPolicy.PolicyVersion ||
            job.Chunks.Count != plan.Count)
        {
            return false;
        }

        for (var index = 0; index < plan.Count; index++)
        {
            var planned = plan[index];
            var stored = job.Chunks[index];
            if (stored.Index != planned.Index ||
                Math.Abs(stored.SourceStartSeconds - planned.SourceStartSeconds) > 0.001 ||
                Math.Abs(stored.SourceEndSeconds - planned.SourceEndSeconds) > 0.001)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>First chunk that still needs a provider request, or null when all are done.</summary>
    public MeetingTranscriptionChunkRecord? NextIncompleteChunk(MeetingTranscriptionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Chunks
            .OrderBy(chunk => chunk.Index)
            .FirstOrDefault(chunk => !chunk.IsCompleted);
    }

    public void MarkChunkCompleted(
        MeetingTranscriptionJob job,
        int index,
        string resultFileName,
        double measuredDurationSeconds,
        int segmentCount,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        var chunk = job.Chunks.FirstOrDefault(item => item.Index == index)
            ?? throw new InvalidOperationException(
                "The completed chunk does not belong to this transcription job.");
        chunk.ResultFileName = resultFileName ?? string.Empty;
        chunk.MeasuredDurationSeconds = measuredDurationSeconds;
        chunk.SegmentCount = segmentCount;
        chunk.IsCompleted = true;
        chunk.CompletedAtUtc = now ?? DateTimeOffset.UtcNow;
        job.State = MeetingTranscriptionJobState.Running;
        job.LastError = string.Empty;
        job.UpdatedAtUtc = chunk.CompletedAtUtc.Value;
    }

    /// <summary>
    /// Records a sanitized failure. Completed chunks are intentionally left
    /// untouched so Retry resumes at the failed chunk instead of resubmitting
    /// work that already succeeded.
    /// </summary>
    public void MarkFailed(
        MeetingTranscriptionJob job,
        string sanitizedError,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.State = MeetingTranscriptionJobState.Failed;
        job.LastError = string.IsNullOrWhiteSpace(sanitizedError)
            ? "Transcription failed."
            : sanitizedError.Trim();
        job.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
    }

    public void MarkRunning(MeetingTranscriptionJob job, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.State = MeetingTranscriptionJobState.Running;
        job.LastError = string.Empty;
        job.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Binds the single final revision to the job. Called only after the
    /// revision is durably persisted, which is what makes a later retry or
    /// restart refuse to create a second final revision.
    /// </summary>
    public void MarkFinalized(
        MeetingTranscriptionJob job,
        Guid transcriptId,
        Guid revisionId,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.FinalTranscriptId = transcriptId;
        job.FinalTranscriptRevisionId = revisionId;
        job.State = MeetingTranscriptionJobState.Completed;
        job.LastError = string.Empty;
        job.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Removes a job and reports its artifacts for deletion. Used by MEET
    /// deletion, recording deletion/replacement, and after a completed job's
    /// partial results are no longer needed.
    /// </summary>
    public IReadOnlyList<MeetingTranscriptionJobCleanup> Remove(Guid recordingId)
    {
        var job = Find(recordingId);
        if (job is null)
        {
            return Array.Empty<MeetingTranscriptionJobCleanup>();
        }

        _state.MeetingTranscriptionJobs.Remove(job);
        return new[] { Describe(job) };
    }

    /// <summary>
    /// Deleting a MEET drops its unfinished transcription work. A job that
    /// already produced its final transcript is simply removed as bookkeeping;
    /// the transcript itself is owned by the existing transcript model.
    /// </summary>
    public IReadOnlyList<MeetingTranscriptionJobCleanup> RemoveForMeeting(Guid meetId)
    {
        var jobs = _state.MeetingTranscriptionJobs
            .Where(job => job.MeetId == meetId)
            .ToList();
        var cleanup = new List<MeetingTranscriptionJobCleanup>();
        foreach (var job in jobs)
        {
            cleanup.Add(Describe(job));
            _state.MeetingTranscriptionJobs.Remove(job);
        }

        return cleanup;
    }

    /// <summary>
    /// Drops jobs whose recording no longer exists. Keeps state.json from
    /// accumulating unreachable partial-result metadata.
    /// </summary>
    public IReadOnlyList<MeetingTranscriptionJobCleanup> RemoveOrphans()
    {
        var recordingIds = _state.MeetingRecordings
            .Select(recording => recording.Id)
            .ToHashSet();
        var orphans = _state.MeetingTranscriptionJobs
            .Where(job => !recordingIds.Contains(job.RecordingId))
            .ToList();
        var cleanup = new List<MeetingTranscriptionJobCleanup>();
        foreach (var job in orphans)
        {
            cleanup.Add(Describe(job));
            _state.MeetingTranscriptionJobs.Remove(job);
        }

        return cleanup;
    }

    private static MeetingTranscriptionJobCleanup Describe(MeetingTranscriptionJob job) =>
        new(job.RecordingId, job.JobFolderRelativePath);
}
