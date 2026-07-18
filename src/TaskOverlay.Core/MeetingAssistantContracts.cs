using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TaskOverlay.Core;

public sealed record AudioDeviceDescriptor(string Id, string DisplayName, bool IsDefault);

public sealed record MeetingRecordingStartRequest(
    Guid RecordingId,
    string RecordingFolder,
    string? MicrophoneDeviceId,
    string? SystemOutputDeviceId,
    MeetingRecordingFormat RecordingFormat = MeetingRecordingFormat.AacM4a);

public sealed record MeetingRecordingStartResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? SystemTrackStartedAtUtc,
    DateTimeOffset? MicrophoneTrackStartedAtUtc,
    string SystemAudioFile,
    string MicrophoneFile,
    AudioTrackHealth SystemAudioHealth,
    AudioTrackHealth MicrophoneHealth,
    string? Warning,
    MeetingRecordingFormat RecordingFormat = MeetingRecordingFormat.AacM4a,
    IReadOnlyList<MeetingRecordingTrackArtifact>? Tracks = null);

public sealed record MeetingRecordingStopResult(
    DateTimeOffset StoppedAtUtc,
    AudioTrackHealth SystemAudioHealth,
    AudioTrackHealth MicrophoneHealth,
    string? Warning,
    MeetingRecordingFormat RecordingFormat = MeetingRecordingFormat.AacM4a,
    IReadOnlyList<MeetingRecordingTrackArtifact>? Tracks = null,
    bool HasUsableAudio = true);

public sealed record MeetingRecorderRuntimeStatus(
    Guid? RecordingId,
    DateTimeOffset? StartedAtUtc,
    AudioTrackHealth SystemAudioHealth,
    AudioTrackHealth MicrophoneHealth,
    string? Error,
    MeetingRecordingFormat? RecordingFormat = null);

public interface IMeetingRecorder : IAsyncDisposable
{
    MeetingRecorderRuntimeStatus Status { get; }
    IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices();
    IReadOnlyList<AudioDeviceDescriptor> GetSystemOutputDevices();
    Task<MeetingRecordingStartResult> StartAsync(
        MeetingRecordingStartRequest request,
        CancellationToken cancellationToken = default);
    Task<MeetingRecordingStopResult> StopAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default);
}

public sealed record MeetingAudioProcessingRequest(
    Guid RecordingId,
    string RecordingFolder,
    string? SystemAudioPath,
    DateTimeOffset? SystemTrackStartedAtUtc,
    string? MicrophonePath,
    DateTimeOffset? MicrophoneTrackStartedAtUtc,
    long MaximumChunkBytes = 20L * 1024L * 1024L,
    string? ExistingMixedAudioPath = null,
    MeetingRecordingFormat RecordingFormat = MeetingRecordingFormat.Wav,
    int MixedAudioBitrate = 96_000,
    double? ProcessFromSeconds = null,
    double? ProcessUntilSeconds = null);

public sealed record MeetingAudioProcessingResult(
    string MixedAudioPath,
    IReadOnlyList<string> OrderedChunkPaths,
    TimeSpan Duration);

public interface IMeetingAudioProcessor
{
    Task<MeetingAudioProcessingResult> ProcessAsync(
        MeetingAudioProcessingRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record TranscriptionProviderRequest(
    string AudioPath,
    string Model,
    MeetingTranscriptLanguage Language,
    TimeSpan ChunkOffset,
    Guid? RecordingId = null,
    Guid? MeetingId = null);

public sealed record TranscriptionProviderResponse(
    string RawJson,
    string Text,
    IReadOnlyList<TranscriptSegment> Segments,
    string? DetectedLanguage);

public interface ITranscriptionProvider
{
    string Name { get; }
    Task<TranscriptionProviderResponse> TranscribeAsync(
        TranscriptionProviderRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record MeetingAnalysisProviderRequest(
    Guid? RecordingId,
    Guid TranscriptId,
    Guid TranscriptRevisionId,
    Guid? MeetId,
    Guid ProjectId,
    string MeetingTitle,
    NormalizedTranscript Transcript,
    string Model);

public sealed record MeetingAnalysisProviderResponse(
    string RawJson,
    MeetingAnalysis Analysis);

public interface IMeetingAnalysisProvider
{
    string Name { get; }
    Task<MeetingAnalysisProviderResponse> AnalyzeAsync(
        MeetingAnalysisProviderRequest request,
        CancellationToken cancellationToken = default);
}
