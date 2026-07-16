using System;
using System.Collections.Generic;

namespace TaskOverlay.Core;

public enum MeetingRecordingPolicy
{
    Inherit,
    Manual,
    AutoRecord
}

public enum MeetingRecordingSourceKind
{
    ScheduledMeet,
    ManualMeet,
    Emergency
}

public enum MeetingRecordingState
{
    Pending,
    Recording,
    Stopping,
    Recorded,
    Processing,
    Transcribing,
    TranscriptReady,
    Analyzing,
    Ready,
    Failed
}

public enum AudioTrackHealth
{
    Unknown,
    Healthy,
    Unavailable,
    Failed
}

public enum MeetingRecordingFormat
{
    // Zero remains WAV so recordings written before schema v5 deserialize safely.
    Wav,
    AacM4a
}

public enum MeetingRecordingTrackKind
{
    System,
    Microphone,
    Mixed
}

public enum MeetingRecordingFinalizationState
{
    Pending,
    InProgress,
    Finalized,
    Interrupted,
    Failed
}

public enum MeetingRecordingValidationState
{
    Unknown,
    Valid,
    Invalid
}

public sealed class MeetingRecordingTrackArtifact
{
    public MeetingRecordingTrackKind Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string InProgressFileName { get; set; } = string.Empty;
    public List<string> SegmentFiles { get; set; } = new();
    public string Container { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public int SampleRate { get; set; }
    public int ChannelCount { get; set; }
    public int Bitrate { get; set; }
    public double DurationSeconds { get; set; }
    public long Bytes { get; set; }
    public bool HasAudioFrames { get; set; }
    public MeetingRecordingFinalizationState FinalizationState { get; set; } =
        MeetingRecordingFinalizationState.Pending;
    public MeetingRecordingValidationState ValidationState { get; set; } =
        MeetingRecordingValidationState.Unknown;
    public string Error { get; set; } = string.Empty;
}

public sealed class MeetingRecording
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MeetId { get; set; }
    public MeetingRecordingSourceKind SourceKind { get; set; }
    public MeetingRecordingState State { get; set; } = MeetingRecordingState.Pending;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? StoppedAtUtc { get; set; }
    public DateTimeOffset? SystemTrackStartedAtUtc { get; set; }
    public DateTimeOffset? MicrophoneTrackStartedAtUtc { get; set; }
    public MeetingRecordingFormat RecordingFormat { get; set; } =
        MeetingRecordingFormat.Wav;
    public List<MeetingRecordingTrackArtifact> Tracks { get; set; } = new();
    public string RecordingFolderRelativePath { get; set; } = string.Empty;
    public string SystemAudioFile { get; set; } = string.Empty;
    public string MicrophoneFile { get; set; } = string.Empty;
    public string MixedAudioFile { get; set; } = string.Empty;
    public List<string> TranscriptionChunkFiles { get; set; } = new();
    public string TranscriptRawFile { get; set; } = string.Empty;
    public string TranscriptFile { get; set; } = string.Empty;
    public string TranscriptMarkdownFile { get; set; } = string.Empty;
    public string AnalysisFile { get; set; } = string.Empty;
    public AudioTrackHealth SystemAudioHealth { get; set; } = AudioTrackHealth.Unknown;
    public AudioTrackHealth MicrophoneHealth { get; set; } = AudioTrackHealth.Unknown;
    public string LastError { get; set; } = string.Empty;
    public bool KeepLocalOnly { get; set; }
    public DateTimeOffset? LastAutoStartAttemptAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive => State is MeetingRecordingState.Recording or
        MeetingRecordingState.Stopping;
}

public sealed class NormalizedTranscript
{
    public Guid RecordingId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public List<TranscriptSegment> Segments { get; set; } = new();
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TranscriptSegment
{
    public int Index { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Speaker { get; set; }
}

public enum MeetingAnalysisState
{
    Pending,
    Analyzing,
    ReadyForReview,
    PartiallyApplied,
    Applied,
    Failed
}

public sealed class MeetingAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecordingId { get; set; }
    public Guid? MeetId { get; set; }
    public MeetingAnalysisState State { get; set; } = MeetingAnalysisState.Pending;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Decisions { get; set; } = new();
    public List<string> MyActionItems { get; set; } = new();
    public List<string> OtherPeopleActionItems { get; set; } = new();
    public List<string> WaitingFor { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public List<string> QuestionsToClarify { get; set; } = new();
    public List<string> Deadlines { get; set; } = new();
    public List<MeetingSourceReference> KeyQuotesOrSourceReferences { get; set; } = new();
    public List<ProposedAction> ProposedActions { get; set; } = new();
    public string LastError { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MeetingSourceReference
{
    public double? StartSeconds { get; set; }
    public double? EndSeconds { get; set; }
    public string Excerpt { get; set; } = string.Empty;
}

public enum ProposedActionType
{
    CreateTask,
    CreateWaitingTask,
    CreateFollowUpTask,
    AddMeetingContextNote
}

public enum ProposedActionReviewState
{
    Pending,
    Rejected,
    Applied,
    Failed
}

public sealed class ProposedAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ProposedActionType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? ProposedProjectId { get; set; }
    public string ProjectSuggestion { get; set; } = string.Empty;
    public TaskStatus ProposedStatus { get; set; } = TaskStatus.Todo;
    public string WaitingFor { get; set; } = string.Empty;
    public DateTimeOffset? DeadlineAtUtc { get; set; }
    public DateTimeOffset? ReminderAtUtc { get; set; }
    public double? SourceSegmentStart { get; set; }
    public double? SourceSegmentEnd { get; set; }
    public string SourceExcerpt { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public ProposedActionReviewState ReviewState { get; set; } =
        ProposedActionReviewState.Pending;
    public Guid? AppliedTaskId { get; set; }
    public Guid? AppliedContextItemId { get; set; }
}

public sealed class TaskSourceReference
{
    public Guid? MeetId { get; set; }
    public Guid RecordingId { get; set; }
    public Guid AnalysisId { get; set; }
    public Guid ProposedActionId { get; set; }
    public double? SegmentStartSeconds { get; set; }
    public double? SegmentEndSeconds { get; set; }
    public string Excerpt { get; set; } = string.Empty;
}
