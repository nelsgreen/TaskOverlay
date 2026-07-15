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
    string? SystemOutputDeviceId);

public sealed record MeetingRecordingStartResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? SystemTrackStartedAtUtc,
    DateTimeOffset? MicrophoneTrackStartedAtUtc,
    string SystemAudioFile,
    string MicrophoneFile,
    AudioTrackHealth SystemAudioHealth,
    AudioTrackHealth MicrophoneHealth,
    string? Warning);

public sealed record MeetingRecordingStopResult(
    DateTimeOffset StoppedAtUtc,
    AudioTrackHealth SystemAudioHealth,
    AudioTrackHealth MicrophoneHealth,
    string? Warning);

public sealed record MeetingRecorderRuntimeStatus(
    Guid? RecordingId,
    DateTimeOffset? StartedAtUtc,
    AudioTrackHealth SystemAudioHealth,
    AudioTrackHealth MicrophoneHealth,
    string? Error);

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
    long MaximumChunkBytes = 20L * 1024L * 1024L);

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
    TimeSpan ChunkOffset);

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
    Guid RecordingId,
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
