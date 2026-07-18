using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed record MeetingAssistantOperationResult(
    bool Success,
    string? Error = null,
    Guid? RecordingId = null,
    Guid? MeetingId = null,
    Guid? AnalysisId = null,
    IReadOnlyList<Guid>? CreatedTaskIds = null,
    IReadOnlyList<Guid>? CreatedContextItemIds = null,
    bool Cancelled = false)
{
    public static MeetingAssistantOperationResult Fail(string error) =>
        new(false, error);

    public static MeetingAssistantOperationResult Cancel(string message, Guid? recordingId = null) =>
        new(false, message, RecordingId: recordingId, Cancelled: true);
}

public sealed class MeetingAssistantCoordinator : IAsyncDisposable
{
    private readonly AppState _state;
    private readonly LocalAppSettings _localSettings;
    private readonly Action _persistState;
    private readonly Action _persistLocalSettings;
    private readonly Action _stateChanged;
    private readonly Action<string, Exception?>? _diagnostic;
    private readonly MeetingRecordingStorage _storage;
    private readonly MeetingSourceStorage _sourceStorage;
    private readonly MeetingTranscriptService _transcriptService;
    private readonly IMeetingRecorder _recorder;
    private readonly IMeetingAudioProcessor _audioProcessor;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly IMeetingAnalysisProvider _analysisProvider;
    private readonly SemaphoreSlim _recordingGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, ProcessingOperation> _processing = new();
    private readonly ConcurrentDictionary<Guid, byte> _transcriptRevisionSaves = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public MeetingAssistantCoordinator(
        AppState state,
        LocalAppSettings localSettings,
        string stateDirectory,
        Action persistState,
        Action persistLocalSettings,
        Action stateChanged,
        IMeetingRecorder recorder,
        IMeetingAudioProcessor audioProcessor,
        ITranscriptionProvider transcriptionProvider,
        IMeetingAnalysisProvider analysisProvider,
        Action<string, Exception?>? diagnostic = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _localSettings = localSettings ?? throw new ArgumentNullException(nameof(localSettings));
        _localSettings.MeetingAssistant ??= new MeetingAssistantSettings();
        _persistState = persistState ?? throw new ArgumentNullException(nameof(persistState));
        _persistLocalSettings = persistLocalSettings ??
                                throw new ArgumentNullException(nameof(persistLocalSettings));
        _stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _audioProcessor = audioProcessor ?? throw new ArgumentNullException(nameof(audioProcessor));
        _transcriptionProvider = transcriptionProvider ??
                                 throw new ArgumentNullException(nameof(transcriptionProvider));
        _analysisProvider = analysisProvider ??
                            throw new ArgumentNullException(nameof(analysisProvider));
        _diagnostic = diagnostic;
        _storage = new MeetingRecordingStorage(stateDirectory);
        _sourceStorage = new MeetingSourceStorage(stateDirectory);
        _transcriptService = new MeetingTranscriptService(state, _sourceStorage);
        RepairManagedTranscriptArtifacts();
    }

    public string RecordingsRoot => _storage.RootDirectory;
    public MeetingRecorderRuntimeStatus RuntimeStatus => _recorder.Status;
    public IReadOnlyList<WorkspaceMeetingOperationSnapshot> ProcessingOperations =>
        _processing.Values
            .OrderBy(operation => operation.StartedAtUtc)
            .Select(operation => operation.ToSnapshot())
            .ToList();
    public IReadOnlyList<AudioDeviceDescriptor> MicrophoneDevices =>
        _recorder.GetMicrophoneDevices();
    public IReadOnlyList<AudioDeviceDescriptor> SystemOutputDevices =>
        _recorder.GetSystemOutputDevices();

    public Guid? GetActiveRecordingIdForMeeting(Guid meetingId)
    {
        var recordingId = RuntimeStatus.RecordingId;
        return recordingId is Guid id && _state.MeetingRecordings.Any(recording =>
                recording.Id == id && recording.MeetId == meetingId && recording.IsActive)
            ? id
            : null;
    }

    public string? LoadTranscriptText(MeetingRecording recording)
    {
        try
        {
            var path = ResolveOptional(recording, recording.TranscriptFile);
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            var transcript = JsonSerializer.Deserialize<NormalizedTranscript>(
                File.ReadAllText(path),
                _jsonOptions);
            return transcript?.Text;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException)
        {
            return null;
        }
    }

    public MeetingTranscriptSnapshotContent? LoadTranscriptContent(MeetingTranscript transcript)
    {
        try
        {
            // User-edited revisions have no original artifact of their own.
            var originalAvailable = transcript.OriginalArtifactFile.Length > 0 &&
                                    File.Exists(_sourceStorage.ResolveTranscriptFile(
                                        transcript,
                                        transcript.OriginalArtifactFile));
            var normalizedAvailable = File.Exists(_sourceStorage.ResolveTranscriptFile(
                transcript,
                transcript.NormalizedArtifactFile));
            var markdownAvailable = transcript.MarkdownArtifactFile.Length > 0 &&
                                    File.Exists(_sourceStorage.ResolveTranscriptFile(
                                        transcript,
                                        transcript.MarkdownArtifactFile));
            var normalized = normalizedAvailable ? _transcriptService.Load(transcript) : null;
            return new MeetingTranscriptSnapshotContent(
                normalized,
                originalAvailable,
                normalizedAvailable,
                markdownAvailable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or JsonException)
        {
            Report($"MEET transcript artifact unavailable: transcriptId={transcript.Id:N}.", ex);
            return new MeetingTranscriptSnapshotContent(null, false, false, false);
        }
    }

    public string? LoadScreenshotThumbnailDataUrl(MeetingScreenshot screenshot)
    {
        try
        {
            var path = _sourceStorage.ResolveScreenshotFile(screenshot);
            if (!File.Exists(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var source = System.Drawing.Image.FromFile(path);
            const int maximumWidth = 360;
            const int maximumHeight = 220;
            var scale = Math.Min(
                1,
                Math.Min(
                    maximumWidth / (double)source.Width,
                    maximumHeight / (double)source.Height));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            using var thumbnail = new System.Drawing.Bitmap(
                width,
                height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(thumbnail))
            {
                graphics.CompositingQuality =
                    System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode =
                    System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, width, height);
            }

            using var output = new MemoryStream();
            thumbnail.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            return $"data:image/png;base64,{Convert.ToBase64String(output.ToArray())}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or ArgumentException or OutOfMemoryException)
        {
            Report($"MEET screenshot unavailable: screenshotId={screenshot.Id:N}.", ex);
            return null;
        }
    }

    public async Task<MeetingAssistantOperationResult> StartMeetingAsync(
        Guid meetingId,
        bool automatic,
        CancellationToken cancellationToken = default)
    {
        Report(
            $"{(automatic ? "Automatic" : "Manual")} MEET recording start command received: " +
            $"meetId={meetingId:N}.");
        var meeting = _state.Meetings.FirstOrDefault(item => item.Id == meetingId);
        if (meeting is null)
        {
            Report($"MEET recording start rejected: meetId={meetingId:N}; reason=not-found.");
            return MeetingAssistantOperationResult.Fail("MEET was not found.");
        }

        return await StartAsync(
            meetingId,
            automatic
                ? MeetingRecordingSourceKind.ScheduledMeet
                : MeetingRecordingSourceKind.ManualMeet,
            automatic,
            cancellationToken);
    }

    public Task<MeetingAssistantOperationResult> StartEmergencyAsync(
        CancellationToken cancellationToken = default) =>
        StartAsync(
            meetId: null,
            MeetingRecordingSourceKind.Emergency,
            automatic: false,
            cancellationToken);

    public async Task<MeetingAssistantOperationResult> StopAsync(
        Guid recordingId,
        bool allowAutoTranscription = true,
        CancellationToken cancellationToken = default)
    {
        Report($"MEET recording stop command received: recordingId={recordingId:N}.");
        await _recordingGate.WaitAsync(cancellationToken);
        try
        {
            var service = new MeetingRecordingService(_state);
            var recording = service.Find(recordingId);
            var runtimeRecordingId = _recorder.Status.RecordingId;
            Report(
                $"MEET recording stop runtime check: requested={recordingId:N}; " +
                $"runtimePresent={runtimeRecordingId.HasValue}; " +
                $"runtimeRecordingId={FormatOptionalId(runtimeRecordingId)}.");

            if (recording is null)
            {
                return MeetingAssistantOperationResult.Fail("Recording was not found.");
            }

            if (runtimeRecordingId is null)
            {
                if (recording.IsActive)
                {
                    service.MarkFailed(
                        recordingId,
                        "No live recorder session was found. The interrupted recording can be retried.");
                    Persist(recording);
                    Report(
                        $"Stale MEET recording state reconciled during Stop: " +
                        $"recordingId={recordingId:N}.");
                }

                return MeetingAssistantOperationResult.Fail(
                    "No live meeting recording is active. Workspace state was refreshed.");
            }

            if (runtimeRecordingId.Value != recordingId)
            {
                return MeetingAssistantOperationResult.Fail(
                    "A different meeting recording is active.");
            }

            if (!recording.IsActive)
            {
                return MeetingAssistantOperationResult.Fail(
                    "Live recorder state does not match the saved recording state. Stop the active recording from its owning MEET.");
            }

            if (recording.State != MeetingRecordingState.Stopping &&
                !service.MarkStopping(recordingId))
            {
                return MeetingAssistantOperationResult.Fail("Recording could not enter stopping state.");
            }

            Persist(recording);
            try
            {
                var result = await _recorder.StopAsync(recordingId, cancellationToken);
                if (!result.HasUsableAudio)
                {
                    const string userMessage =
                        "Recording could not be finalized. " +
                        "The incomplete files were preserved for diagnostics.";
                    Report(
                        $"MEET recording produced no usable finalized audio: " +
                        $"recordingId={recordingId:N}; warning={result.Warning ?? "none"}.");
                    foreach (var track in result.Tracks ?? Array.Empty<MeetingRecordingTrackArtifact>())
                    {
                        if (!string.IsNullOrWhiteSpace(track.Error))
                        {
                            Report(
                                $"MEET recording track finalization detail: " +
                                $"recordingId={recordingId:N}; track={track.Kind}; " +
                                $"state={track.FinalizationState}; validation={track.ValidationState}; " +
                                $"error={track.Error}.");
                        }
                    }

                    service.MarkFailed(
                        recordingId,
                        userMessage);
                    recording.RecordingFormat = result.RecordingFormat;
                    recording.Tracks = result.Tracks?.Select(CloneTrack).ToList() ?? new();
                    recording.StoppedAtUtc = result.StoppedAtUtc;
                    recording.SystemAudioHealth = result.SystemAudioHealth;
                    recording.MicrophoneHealth = result.MicrophoneHealth;
                    Persist(recording);
                    return MeetingAssistantOperationResult.Fail(userMessage);
                }

                if (!service.MarkRecorded(recordingId, result))
                {
                    service.MarkFailed(
                        recordingId,
                        "Audio capture stopped, but recording metadata could not be finalized.");
                    Persist(recording);
                    return MeetingAssistantOperationResult.Fail(recording.LastError);
                }

                Report(
                    $"MEET recording finalized: recordingId={recordingId:N}; " +
                    $"systemLoopback={result.SystemAudioHealth}; " +
                    $"microphone={result.MicrophoneHealth}.");
                Persist(recording);
            }
            catch (Exception ex)
            {
                if (_recorder.Status.RecordingId == recordingId)
                {
                    recording.State = MeetingRecordingState.Stopping;
                    recording.LastError = SafeMessage(ex);
                    recording.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
                else
                {
                    service.MarkFailed(recordingId, SafeMessage(ex));
                }

                Persist(recording);
                Report("MEET recording stop failed.", ex);
                return MeetingAssistantOperationResult.Fail(recording.LastError);
            }

            if (allowAutoTranscription &&
                _localSettings.MeetingAssistant.AutoTranscribeAfterStop &&
                !recording.KeepLocalOnly)
            {
                _ = RunAutoTranscriptionAsync(recordingId);
            }

            return new MeetingAssistantOperationResult(true, RecordingId: recordingId);
        }
        finally
        {
            _recordingGate.Release();
        }
    }

    public async Task TickAutoRecordAsync(
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var due = MeetingAutoRecordScheduler.FindDueMeetings(
            _state,
            _localSettings.MeetingAssistant,
            timestamp);
        foreach (var meeting in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_recorder.Status.RecordingId is not null)
            {
                var recordingId = Guid.NewGuid();
                var layout = _storage.CreateLayout(meeting.Id, recordingId);
                var conflict = new MeetingRecordingService(_state).CreateAutoStartFailure(
                    meeting.Id,
                    recordingId,
                    layout.RelativeFolder,
                    "Automatic recording could not start because another recording is active.",
                    timestamp);
                if (conflict is not null)
                {
                    _storage.WriteMetadata(conflict);
                    PersistStateAndNotify();
                }

                continue;
            }

            await StartMeetingAsync(meeting.Id, automatic: true, cancellationToken);
        }
    }

    public async Task<MeetingAssistantOperationResult> TranscribeAsync(
        Guid recordingId,
        bool acceptUploadDisclosure,
        CancellationToken cancellationToken = default)
    {
        if (!_localSettings.MeetingAssistant.ProviderUploadDisclosureAccepted)
        {
            if (!acceptUploadDisclosure)
            {
                return MeetingAssistantOperationResult.Fail(
                    "Confirm that the mixed audio will be uploaded to the configured transcription provider.");
            }

            _localSettings.MeetingAssistant.ProviderUploadDisclosureAccepted = true;
            _persistLocalSettings();
        }

        var service = new MeetingRecordingService(_state);
        var recording = service.Find(recordingId);
        if (recording is null)
        {
            return MeetingAssistantOperationResult.Fail("Recording was not found.");
        }

        var processingOperation = ProcessingOperation.ForTranscription(recording, cancellationToken);
        if (!_processing.TryAdd(recordingId, processingOperation))
        {
            return MeetingAssistantOperationResult.Fail(
                "This recording already has a processing operation in progress.");
        }

        _stateChanged();
        var token = processingOperation.Cancellation.Token;
        try
        {
            Report(
                $"MEET transcription started: recordingId={recordingId:N}; " +
                $"meetId={FormatOptionalId(recording.MeetId)}; " +
                $"model={_localSettings.MeetingAssistant.TranscriptionModel}; " +
                $"language={_localSettings.MeetingAssistant.Language}.");
            if (!service.MarkProcessing(recordingId))
            {
                return MeetingAssistantOperationResult.Fail(
                    "Recording is not ready for transcription.");
            }

            var folder = _storage.ResolveFolder(recording.RecordingFolderRelativePath);
            var systemPath = ResolveOptional(recording, recording.SystemAudioFile);
            var microphonePath = ResolveOptional(recording, recording.MicrophoneFile);
            var mixedPath = ResolveMixedPathForTranscription(recording);
            await ReportTranscriptionInputsAsync(
                recording,
                folder,
                systemPath,
                microphonePath,
                mixedPath,
                token);
            ClearPreviousTranscriptionChunks(recording, folder);
            Persist(recording);
            var mixedBitrate = recording.Tracks.FirstOrDefault(track =>
                track.Kind == MeetingRecordingTrackKind.Mixed)?.Bitrate ?? 96_000;
            var processing = await _audioProcessor.ProcessAsync(
                new MeetingAudioProcessingRequest(
                    recording.Id,
                    folder,
                    systemPath,
                    recording.SystemTrackStartedAtUtc,
                    microphonePath,
                    recording.MicrophoneTrackStartedAtUtc,
                    ExistingMixedAudioPath: mixedPath,
                    RecordingFormat: recording.RecordingFormat,
                    MixedAudioBitrate: mixedBitrate,
                    ProcessFromSeconds: recording.ProcessFromSeconds,
                    ProcessUntilSeconds: recording.ProcessUntilSeconds),
                token);
            var sourceFingerprint = await TranscriptionAudioDiagnostics.InspectAsync(
                processing.MixedAudioPath,
                token);
            if (recording.RecordingFormat == MeetingRecordingFormat.AacM4a &&
                !PathsEqual(sourceFingerprint.FullPath, mixedPath))
            {
                throw new InvalidDataException(
                    "Compact transcription did not retain the finalized mixed track as its source.");
            }

            Report(
                $"MEET transcription source selected: recordingId={recordingId:N}; " +
                $"meetId={FormatOptionalId(recording.MeetId)}; " +
                $"path={sourceFingerprint.FullPath}; fileName={sourceFingerprint.FileName}; " +
                $"bytes={sourceFingerprint.Bytes}; " +
                $"durationSeconds={sourceFingerprint.Duration.TotalSeconds:F3}; " +
                $"sha256={sourceFingerprint.Sha256}; " +
                $"chunkCount={processing.OrderedChunkPaths.Count}.");
            if (!service.MarkTranscribing(recordingId))
            {
                throw new InvalidOperationException(
                    "Recording could not enter transcription state.");
            }

            recording.MixedAudioFile = Path.GetFileName(processing.MixedAudioPath);
            recording.TranscriptionChunkFiles = processing.OrderedChunkPaths
                .Select(Path.GetFileName)
                .Where(path => path is not null)
                .Cast<string>()
                .ToList();
            UpdateProcessingStage(recordingId, "Transcribing");
            Persist(recording);

            var chunkFingerprints = new List<TranscriptionAudioFingerprint>();
            for (var index = 0; index < processing.OrderedChunkPaths.Count; index++)
            {
                var chunkPath = processing.OrderedChunkPaths[index];
                EnsureFileInsideFolder(chunkPath, folder);
                var fingerprint = await TranscriptionAudioDiagnostics.InspectAsync(
                    chunkPath,
                    token);
                chunkFingerprints.Add(fingerprint);
                Report(
                    $"MEET transcription chunk prepared: recordingId={recordingId:N}; " +
                    $"meetId={FormatOptionalId(recording.MeetId)}; index={index}; " +
                    $"path={fingerprint.FullPath}; fileName={fingerprint.FileName}; " +
                    $"bytes={fingerprint.Bytes}; " +
                    $"durationSeconds={fingerprint.Duration.TotalSeconds:F3}; " +
                    $"sha256={fingerprint.Sha256}.");
            }

            var responses = new List<TranscriptionProviderResponse>();
            var offset = TimeSpan.Zero;
            foreach (var chunk in chunkFingerprints)
            {
                token.ThrowIfCancellationRequested();
                var response = await _transcriptionProvider.TranscribeAsync(
                    new TranscriptionProviderRequest(
                        chunk.FullPath,
                        _localSettings.MeetingAssistant.TranscriptionModel,
                        _localSettings.MeetingAssistant.Language,
                        offset,
                        recordingId,
                        recording.MeetId),
                    token);
                responses.Add(response);
                offset += chunk.Duration;
            }
            token.ThrowIfCancellationRequested();

            var transcriptId = Guid.NewGuid();
            var transcriptRevisionId = Guid.NewGuid();
            var normalized = ComposeTranscript(
                recordingId,
                transcriptId,
                transcriptRevisionId,
                _transcriptionProvider.Name,
                _localSettings.MeetingAssistant.TranscriptionModel,
                responses);
            TranscriptSpeakerMapping.EnsureStableSpeakers(normalized);
            var layout = LayoutFor(recording);
            var rawResponse = BuildRawResponseArray(responses);
            MeetingRecordingStorage.WriteTextAtomic(
                layout.TranscriptRawPath,
                rawResponse);
            _storage.WriteJsonAtomic(layout.TranscriptPath, normalized);
            MeetingRecordingStorage.WriteTextAtomic(
                layout.TranscriptMarkdownPath,
                BuildTranscriptMarkdown(normalized));
            if (!service.MarkTranscriptReady(
                    recordingId,
                    Path.GetFileName(processing.MixedAudioPath),
                    processing.OrderedChunkPaths.Select(Path.GetFileName).Cast<string>().ToList(),
                    Path.GetFileName(layout.TranscriptRawPath),
                    Path.GetFileName(layout.TranscriptPath),
                    Path.GetFileName(layout.TranscriptMarkdownPath)))
            {
                throw new InvalidOperationException(
                    "Transcript could not be associated with its recording.");
            }

            if (recording.MeetId is Guid transcriptMeetId)
            {
                var sourceLayout = _sourceStorage.CreateTranscriptLayout(
                    transcriptMeetId,
                    transcriptId,
                    ".json");
                MeetingRecordingStorage.WriteTextAtomic(sourceLayout.OriginalPath, rawResponse);
                _sourceStorage.WriteTranscript(sourceLayout, normalized);
                var transcript = new MeetingTranscript
                {
                    Id = transcriptId,
                    MeetId = transcriptMeetId,
                    RecordingId = recordingId,
                    Origin = MeetingTranscriptOrigin.Generated,
                    Format = MeetingTranscriptFormat.NormalizedJson,
                    Provider = _transcriptionProvider.Name,
                    Model = _localSettings.MeetingAssistant.TranscriptionModel,
                    SourceLabel = "TaskOverlay",
                    StorageFolderRelativePath = sourceLayout.RelativeFolder,
                    OriginalArtifactFile = Path.GetFileName(sourceLayout.OriginalPath),
                    NormalizedArtifactFile = Path.GetFileName(sourceLayout.NormalizedPath),
                    MarkdownArtifactFile = Path.GetFileName(sourceLayout.MarkdownPath),
                    HasTimestamps = normalized.HasTimestamps,
                    HasSpeakerLabels = normalized.Speakers.Count > 0,
                    RevisionId = transcriptRevisionId,
                    Speakers = normalized.Speakers.Select(CloneSpeaker).ToList(),
                    CreatedAtUtc = normalized.GeneratedAtUtc,
                    UpdatedAtUtc = normalized.GeneratedAtUtc
                };
                _state.MeetingTranscripts.Add(transcript);
                var owner = _state.Meetings.First(item => item.Id == transcriptMeetId);
                owner.ActiveTranscriptId = transcript.Id;
                owner.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            Persist(recording);
            Report(
                $"MEET transcription completed: recordingId={recordingId:N}; " +
                $"meetId={FormatOptionalId(recording.MeetId)}; " +
                $"sourceFile={sourceFingerprint.FileName}; " +
                $"sourceSha256={sourceFingerprint.Sha256}; " +
                $"chunks={chunkFingerprints.Count}; " +
                $"transcriptPath={layout.TranscriptPath}.");
            return new MeetingAssistantOperationResult(true, RecordingId: recordingId);
        }
        catch (OperationCanceledException)
        {
            service.MarkReadyAfterCancellation(recordingId);
            Persist(recording);
            return MeetingAssistantOperationResult.Cancel(
                "Transcription cancelled. Original audio was kept.",
                recordingId);
        }
        catch (Exception ex) when (
            ex is IOException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            HttpRequestException or
            OpenAiProviderException)
        {
            service.MarkFailed(recordingId, SafeMessage(ex));
            Persist(recording);
            Report("MEET transcription failed.", ex);
            return MeetingAssistantOperationResult.Fail(recording.LastError);
        }
        finally
        {
            CompleteProcessing(recordingId);
        }
    }

    public async Task<MeetingAssistantOperationResult> AnalyzeAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        var recording = new MeetingRecordingService(_state).Find(recordingId);
        if (recording is null)
        {
            return MeetingAssistantOperationResult.Fail("Recording was not found.");
        }

        var transcript = ResolveTranscriptForRecording(recording);
        return transcript is null
            ? MeetingAssistantOperationResult.Fail(
                "Transcript file is missing. Retry transcription first.")
            : await AnalyzeTranscriptAsync(transcript.Id, cancellationToken);
    }

    public async Task<MeetingAssistantOperationResult> AnalyzeTranscriptAsync(
        Guid transcriptId,
        CancellationToken cancellationToken = default)
    {
        var transcriptMetadata = _state.MeetingTranscripts.FirstOrDefault(item =>
            item.Id == transcriptId);
        if (transcriptMetadata is null)
        {
            return MeetingAssistantOperationResult.Fail("Transcript was not found.");
        }

        var processingOperation = ProcessingOperation.ForAnalysis(
            transcriptMetadata,
            cancellationToken);
        if (!_processing.TryAdd(transcriptId, processingOperation))
        {
            return MeetingAssistantOperationResult.Fail(
                "This transcript already has an analysis operation in progress.");
        }

        _stateChanged();
        var recording = transcriptMetadata.RecordingId is Guid recordingId
            ? new MeetingRecordingService(_state).Find(recordingId)
            : null;
        var recordingService = new MeetingRecordingService(_state);
        try
        {
            if (recording is not null)
            {
                if (!recordingService.MarkAnalyzing(recording.Id))
                {
                    return MeetingAssistantOperationResult.Fail(
                        "Recording is not ready for analysis.");
                }

                Persist(recording);
            }

            var transcript = _transcriptService.Load(transcriptMetadata);
            transcript.Text = TranscriptSpeakerMapping.BuildAnalysisText(
                transcript,
                transcriptMetadata.Speakers);
            transcript.Speakers = transcriptMetadata.Speakers.Select(CloneSpeaker).ToList();
            transcript.TranscriptId = transcriptMetadata.Id;
            transcript.RevisionId = transcriptMetadata.RevisionId;
            var meeting = transcriptMetadata.MeetId is Guid meetId
                ? _state.Meetings.FirstOrDefault(item => item.Id == meetId)
                : null;
            var projectId = meeting?.ProjectId ?? _state.Projects
                .OrderBy(project => project.SortOrder)
                .ThenBy(project => project.CreatedAtUtc)
                .Select(project => project.Id)
                .FirstOrDefault();
            if (projectId == Guid.Empty)
            {
                throw new InvalidDataException("No project is available for meeting analysis.");
            }

            var providerResult = await _analysisProvider.AnalyzeAsync(
                new MeetingAnalysisProviderRequest(
                    recording?.Id,
                    transcriptMetadata.Id,
                    transcriptMetadata.RevisionId,
                    transcriptMetadata.MeetId,
                    projectId,
                    meeting?.Title ?? "Emergency recording",
                    transcript,
                    _localSettings.MeetingAssistant.AnalysisModel),
                processingOperation.Cancellation.Token);
            processingOperation.Cancellation.Token.ThrowIfCancellationRequested();
            _state.MeetingAnalyses.RemoveAll(analysis =>
                analysis.TranscriptId == transcriptId &&
                analysis.State == MeetingAnalysisState.Failed);
            _state.MeetingAnalyses.Add(providerResult.Analysis);
            var transcriptFolder = _sourceStorage.ResolveFolder(
                transcriptMetadata.StorageFolderRelativePath);
            var analysisPath = Path.Combine(
                transcriptFolder,
                $"analysis-{providerResult.Analysis.Id:N}.json");
            MeetingRecordingStorage.WriteTextAtomic(
                analysisPath,
                JsonSerializer.Serialize(
                new
                {
                    providerRawJson = providerResult.RawJson,
                    analysis = providerResult.Analysis
                },
                _jsonOptions));
            if (recording is not null)
            {
                recordingService.MarkReady(recording.Id, Path.GetFileName(analysisPath));
                Persist(recording);
            }
            else
            {
                PersistStateAndNotify();
            }

            return new MeetingAssistantOperationResult(
                true,
                RecordingId: recording?.Id,
                AnalysisId: providerResult.Analysis.Id);
        }
        catch (OperationCanceledException)
        {
            if (recording is not null)
            {
                recordingService.MarkReadyAfterCancellation(recording.Id);
                Persist(recording);
            }

            return MeetingAssistantOperationResult.Cancel(
                "Analysis cancelled. Previous analysis was kept.",
                recording?.Id);
        }
        catch (Exception ex) when (
            ex is IOException or
            InvalidDataException or
            InvalidOperationException or
            HttpRequestException or
            OpenAiProviderException or
            JsonException)
        {
            if (recording is not null)
            {
                recordingService.MarkFailed(recording.Id, SafeMessage(ex));
            }

            var failed = new MeetingAnalysis
            {
                RecordingId = recording?.Id,
                TranscriptId = transcriptMetadata.Id,
                TranscriptRevisionId = transcriptMetadata.RevisionId,
                MeetId = transcriptMetadata.MeetId,
                State = MeetingAnalysisState.Failed,
                Provider = _analysisProvider.Name,
                Model = _localSettings.MeetingAssistant.AnalysisModel,
                LastError = SafeMessage(ex)
            };
            _state.MeetingAnalyses.Add(failed);
            if (recording is not null)
            {
                Persist(recording);
            }
            else
            {
                PersistStateAndNotify();
            }

            Report("MEET analysis failed.", ex);
            return MeetingAssistantOperationResult.Fail(SafeMessage(ex));
        }
        finally
        {
            CompleteProcessing(transcriptId);
        }
    }

    public MeetingAssistantOperationResult ImportAudio(
        Guid meetingId,
        string sourcePath,
        DateTimeOffset? now = null)
    {
        if (!_state.Meetings.Any(meeting => meeting.Id == meetingId))
        {
            return MeetingAssistantOperationResult.Fail("MEET was not found.");
        }

        var source = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var format = extension switch
        {
            ".m4a" => MeetingRecordingFormat.AacM4a,
            ".wav" => MeetingRecordingFormat.Wav,
            ".mp3" => MeetingRecordingFormat.Mp3,
            _ => (MeetingRecordingFormat?)null
        };
        if (!format.HasValue)
        {
            return MeetingAssistantOperationResult.Fail(
                "Supported audio formats are M4A, WAV, and MP3.");
        }

        if (!File.Exists(source))
        {
            return MeetingAssistantOperationResult.Fail("The selected audio file no longer exists.");
        }

        var recordingId = Guid.NewGuid();
        var layout = _storage.CreateLayout(meetingId, recordingId);
        var managedName = $"imported-original{extension}";
        var managedPath = Path.Combine(layout.AbsoluteFolder, managedName);
        try
        {
            _sourceStorage.CopyFileAtomic(source, managedPath);
            using var reader = new AudioFileReader(managedPath);
            if (reader.TotalTime <= TimeSpan.Zero)
            {
                throw new InvalidDataException("The imported audio has no playable duration.");
            }

            var timestamp = now ?? DateTimeOffset.UtcNow;
            var bytes = new FileInfo(managedPath).Length;
            var recording = new MeetingRecording
            {
                Id = recordingId,
                MeetId = meetingId,
                SourceKind = MeetingRecordingSourceKind.Imported,
                State = MeetingRecordingState.Recorded,
                RecordingFormat = format.Value,
                RecordingFolderRelativePath = layout.RelativeFolder,
                MixedAudioFile = managedName,
                OriginalFileName = Path.GetFileName(source),
                ManagedFileName = managedName,
                ImportedAtUtc = timestamp,
                ImportedFileBytes = bytes,
                SystemAudioHealth = AudioTrackHealth.Unavailable,
                MicrophoneHealth = AudioTrackHealth.Unavailable,
                Tracks =
                {
                    new MeetingRecordingTrackArtifact
                    {
                        Kind = MeetingRecordingTrackKind.Mixed,
                        FileName = managedName,
                        Container = extension.TrimStart('.').ToUpperInvariant(),
                        Codec = format.Value switch
                        {
                            MeetingRecordingFormat.AacM4a => "AAC",
                            MeetingRecordingFormat.Mp3 => "MP3",
                            _ => "PCM"
                        },
                        SampleRate = reader.WaveFormat.SampleRate,
                        ChannelCount = reader.WaveFormat.Channels,
                        DurationSeconds = reader.TotalTime.TotalSeconds,
                        Bytes = bytes,
                        HasAudioFrames = true,
                        FinalizationState = MeetingRecordingFinalizationState.Finalized,
                        ValidationState = MeetingRecordingValidationState.Valid
                    }
                },
                CreatedAtUtc = timestamp,
                UpdatedAtUtc = timestamp
            };
            _state.MeetingRecordings.Add(recording);
            Persist(recording);
            return new MeetingAssistantOperationResult(true, RecordingId: recording.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or InvalidOperationException or
                                   NotSupportedException or
                                   System.Runtime.InteropServices.COMException)
        {
            if (Directory.Exists(layout.AbsoluteFolder))
            {
                Directory.Delete(layout.AbsoluteFolder, recursive: true);
            }

            return MeetingAssistantOperationResult.Fail(SafeMessage(ex));
        }
    }

    public MeetingAssistantOperationResult ImportTranscript(
        Guid meetingId,
        string sourcePath,
        string? sourceLabel = null)
    {
        try
        {
            var transcript = _transcriptService.Import(meetingId, sourcePath, sourceLabel);
            PersistStateAndNotify();
            return new MeetingAssistantOperationResult(
                true,
                MeetingId: meetingId,
                AnalysisId: transcript.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or NotSupportedException)
        {
            return MeetingAssistantOperationResult.Fail(SafeMessage(ex));
        }
    }

    public bool SetImportedAudioRange(Guid recordingId, double? fromSeconds, double? untilSeconds)
    {
        var recording = new MeetingRecordingService(_state).Find(recordingId);
        var duration = recording?.Tracks.FirstOrDefault(track =>
            track.Kind == MeetingRecordingTrackKind.Mixed)?.DurationSeconds ?? 0;
        if (recording is null || recording.SourceKind != MeetingRecordingSourceKind.Imported ||
            fromSeconds is < 0 || untilSeconds is <= 0 ||
            fromSeconds is double from && untilSeconds is double until && until <= from ||
            fromSeconds is double start && start >= duration ||
            untilSeconds is double end && end > duration + 0.01)
        {
            return false;
        }

        recording.ProcessFromSeconds = fromSeconds;
        recording.ProcessUntilSeconds = untilSeconds;
        recording.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Persist(recording);
        return true;
    }

    public MeetingAssistantOperationResult SaveTranscriptRevision(
        TranscriptRevisionRequest request)
    {
        // One deliberate save at a time per source transcript: a duplicate
        // submission while the first is writing is rejected instead of creating
        // two identical revisions.
        if (!_transcriptRevisionSaves.TryAdd(request.TranscriptId, 0))
        {
            return MeetingAssistantOperationResult.Fail(
                "A transcript revision save is already running for this transcript.");
        }

        try
        {
            var meeting = _state.Meetings.FirstOrDefault(item => item.Id == request.MeetId);
            var previousActiveTranscriptId = meeting?.ActiveTranscriptId;
            var previousMeetingUpdatedAtUtc = meeting?.UpdatedAtUtc ?? default;
            var previousStateUpdatedAtUtc = _state.UpdatedAtUtc;
            var revision = _transcriptService.SaveRevision(request);

            // Commit boundary: the revision counts as saved only once
            // state.json persists. A persistence failure rolls the in-memory
            // mutation, active selection, timestamps, and the new managed
            // folder back so the previous durable state stays authoritative.
            try
            {
                _persistState();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       InvalidDataException or NotSupportedException or
                                       JsonException)
            {
                try
                {
                    _transcriptService.DiscardUnpersistedRevision(
                        revision,
                        previousActiveTranscriptId,
                        previousMeetingUpdatedAtUtc,
                        previousStateUpdatedAtUtc);
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or
                                                  UnauthorizedAccessException or
                                                  InvalidDataException)
                {
                    Report(
                        $"Transcript revision rollback cleanup incomplete: transcriptId={revision.Id:N}.",
                        cleanupEx);
                }

                return MeetingAssistantOperationResult.Fail(SafeMessage(ex));
            }

            // Past the commit boundary the revision is durable: a UI/snapshot
            // notification failure must neither fail the command nor trigger
            // any rollback.
            try
            {
                _stateChanged();
            }
            catch (Exception ex)
            {
                Report(
                    $"Transcript revision saved but state-change notification failed: transcriptId={revision.Id:N}.",
                    ex);
            }

            return new MeetingAssistantOperationResult(
                true,
                MeetingId: request.MeetId,
                AnalysisId: revision.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or NotSupportedException or
                                   JsonException)
        {
            return MeetingAssistantOperationResult.Fail(SafeMessage(ex));
        }
        finally
        {
            _transcriptRevisionSaves.TryRemove(request.TranscriptId, out _);
        }
    }

    public bool SetActiveTranscript(Guid meetingId, Guid transcriptId)
    {
        if (!_transcriptService.SetActive(meetingId, transcriptId))
        {
            return false;
        }

        PersistStateAndNotify();
        return true;
    }

    public bool DeleteTranscript(Guid transcriptId)
    {
        if (!_transcriptService.Delete(transcriptId))
        {
            return false;
        }

        PersistStateAndNotify();
        return true;
    }

    public bool OpenTranscriptArtifact(Guid transcriptId, string artifact)
    {
        var transcript = _state.MeetingTranscripts.FirstOrDefault(item => item.Id == transcriptId);
        if (transcript is null)
        {
            return false;
        }

        var fileName = artifact switch
        {
            "original" => transcript.OriginalArtifactFile,
            "normalized" => transcript.NormalizedArtifactFile,
            "markdown" => transcript.MarkdownArtifactFile,
            _ => string.Empty
        };
        if (fileName.Length == 0)
        {
            return false;
        }

        var path = _sourceStorage.ResolveTranscriptFile(transcript, fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return true;
    }

    public MeetingScreenshot RegisterScreenshot(
        Guid meetingId,
        Guid? recordingId,
        string capturedPngPath,
        int width,
        int height,
        MeetingScreenshotSourceKind sourceKind,
        string sourceLabel,
        DateTimeOffset capturedAtUtc)
    {
        if (!_state.Meetings.Any(meeting => meeting.Id == meetingId) ||
            recordingId is Guid ownerRecordingId && !_state.MeetingRecordings.Any(recording =>
                recording.Id == ownerRecordingId && recording.MeetId == meetingId))
        {
            throw new InvalidDataException("MEET or active recording is invalid.");
        }

        var screenshot = new MeetingScreenshot
        {
            MeetId = meetingId,
            RecordingId = recordingId,
            CapturedAtUtc = capturedAtUtc,
            Width = width,
            Height = height,
            SourceKind = sourceKind,
            SourceLabel = sourceLabel.Trim()
        };
        var destination = _sourceStorage.CreateScreenshotPath(meetingId, screenshot.Id);
        _sourceStorage.CopyFileAtomic(capturedPngPath, destination);
        screenshot.StorageFolderRelativePath =
            $"meetings/{meetingId:N}/screenshots";
        screenshot.FileName = Path.GetFileName(destination);
        screenshot.Bytes = new FileInfo(destination).Length;
        var recording = recordingId is Guid id
            ? _state.MeetingRecordings.First(item => item.Id == id)
            : null;
        if (recording?.StartedAtUtc is DateTimeOffset started)
        {
            screenshot.OffsetFromRecordingStartSeconds = Math.Max(
                0,
                (capturedAtUtc - started).TotalSeconds);
        }

        _state.MeetingScreenshots.Add(screenshot);
        PersistStateAndNotify();
        return screenshot;
    }

    public bool OpenScreenshot(Guid screenshotId)
    {
        var screenshot = _state.MeetingScreenshots.FirstOrDefault(item => item.Id == screenshotId);
        if (screenshot is null)
        {
            return false;
        }

        var path = _sourceStorage.ResolveScreenshotFile(screenshot);
        if (!File.Exists(path))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return true;
    }

    public bool DeleteScreenshot(Guid screenshotId)
    {
        var screenshot = _state.MeetingScreenshots.FirstOrDefault(item => item.Id == screenshotId);
        if (screenshot is null)
        {
            return false;
        }

        _sourceStorage.DeleteScreenshotFile(screenshot);
        _state.MeetingScreenshots.Remove(screenshot);
        PersistStateAndNotify();
        return true;
    }

    public MeetingAssistantOperationResult ApplyProposedActions(
        Guid analysisId,
        IReadOnlyCollection<Guid> actionIds,
        IReadOnlyCollection<ProposedActionOverride>? overrides = null)
    {
        var result = new ProposedActionService(_state).Apply(
            analysisId,
            actionIds,
            overrides);
        var analysis = _state.MeetingAnalyses.FirstOrDefault(item => item.Id == analysisId);
        if (analysis is null)
        {
            return MeetingAssistantOperationResult.Fail("Meeting analysis was not found.");
        }

        RewriteAnalysisFile(analysis);
        PersistStateAndNotify();
        return new MeetingAssistantOperationResult(
            result.FailedActionIds.Count == 0,
            result.FailedActionIds.Count == 0
                ? null
                : "Some proposed actions could not be applied.",
            RecordingId: analysis.RecordingId,
            AnalysisId: analysis.Id,
            CreatedTaskIds: result.CreatedTaskIds,
            CreatedContextItemIds: result.CreatedContextItemIds);
    }

    public bool RejectProposedAction(Guid analysisId, Guid actionId)
    {
        var service = new ProposedActionService(_state);
        if (!service.Reject(analysisId, actionId))
        {
            return false;
        }

        var analysis = _state.MeetingAnalyses.First(item => item.Id == analysisId);
        RewriteAnalysisFile(analysis);
        PersistStateAndNotify();
        return true;
    }

    public bool SetMeetingPolicy(Guid meetingId, MeetingRecordingPolicy policy)
    {
        var meeting = _state.Meetings.FirstOrDefault(item => item.Id == meetingId);
        if (meeting is null || !Enum.IsDefined(policy))
        {
            return false;
        }

        meeting.RecordingPolicy = policy;
        meeting.UpdatedAtUtc = DateTimeOffset.UtcNow;
        PersistStateAndNotify();
        return true;
    }

    public bool SetRecordingFormat(MeetingRecordingFormat format)
    {
        if (format is not (MeetingRecordingFormat.AacM4a or MeetingRecordingFormat.Wav) ||
            _recorder.Status.RecordingId is not null)
        {
            return false;
        }

        _localSettings.MeetingAssistant.RecordingFormat = format;
        _persistLocalSettings();
        Report($"MEET recording format changed: format={format}.");
        return true;
    }

    public bool SetKeepLocalOnly(Guid recordingId, bool keepLocalOnly)
    {
        var service = new MeetingRecordingService(_state);
        var recording = service.Find(recordingId);
        if (recording is null || !service.SetKeepLocalOnly(recordingId, keepLocalOnly))
        {
            return false;
        }

        Persist(recording);
        return true;
    }

    public bool LinkToMeeting(Guid recordingId, Guid meetingId)
    {
        var service = new MeetingRecordingService(_state);
        var recording = service.Find(recordingId);
        if (recording is null || !service.LinkToMeeting(recordingId, meetingId))
        {
            return false;
        }

        Persist(recording);
        return true;
    }

    public MeetingAssistantOperationResult CreateMeetingFromRecording(
        Guid recordingId,
        Guid projectId,
        string title)
    {
        var recording = new MeetingRecordingService(_state).Find(recordingId);
        if (recording is null || recording.IsActive)
        {
            return MeetingAssistantOperationResult.Fail("Recording is not ready to classify.");
        }

        var duration = recording.StartedAtUtc.HasValue && recording.StoppedAtUtc.HasValue
            ? Math.Clamp(
                (int)Math.Ceiling((recording.StoppedAtUtc.Value - recording.StartedAtUtc.Value).TotalMinutes),
                1,
                MeetingService.MaximumDurationMinutes)
            : MeetingItem.DefaultDurationMinutes;
        var meeting = new MeetingService(_state).Create(new MeetingUpdate(
            projectId,
            title,
            "Created from an emergency recording.",
            recording.StartedAtUtc ?? DateTimeOffset.UtcNow,
            duration,
            string.Empty,
            string.Empty,
            null));
        if (meeting is null || !new MeetingRecordingService(_state).LinkToMeeting(recordingId, meeting.Id))
        {
            if (meeting is not null)
            {
                new MeetingService(_state).Delete(meeting.Id);
            }

            return MeetingAssistantOperationResult.Fail("MEET could not be created from recording.");
        }

        Persist(recording);
        return new MeetingAssistantOperationResult(
            true,
            RecordingId: recordingId,
            MeetingId: meeting.Id);
    }

    public bool DeleteRecording(Guid recordingId)
    {
        var service = new MeetingRecordingService(_state);
        var recording = service.Find(recordingId);
        if (recording is null || recording.IsActive || _processing.Values.Any(operation =>
                operation.RecordingId == recordingId))
        {
            return false;
        }

        _storage.DeleteRecordingFiles(recording);
        if (!service.RemoveMetadata(recordingId))
        {
            return false;
        }

        PersistStateAndNotify();
        return true;
    }

    public bool CancelProcessing(Guid targetId)
    {
        var match = _processing.FirstOrDefault(pair =>
            pair.Key == targetId ||
            pair.Value.RecordingId == targetId ||
            pair.Value.TranscriptId == targetId);
        if (match.Value is null || !TryCancel(match.Value.Cancellation))
        {
            return false;
        }

        match.Value.Stage = "Cancelling";
        match.Value.CancellationRequested = true;
        _stateChanged();
        return true;
    }

    public bool OpenRecordingFolder(Guid recordingId)
    {
        var recording = new MeetingRecordingService(_state).Find(recordingId);
        if (recording is null)
        {
            return false;
        }

        return OpenFolder(_storage.ResolveFolder(recording.RecordingFolderRelativePath));
    }

    public bool OpenRecordingsRoot()
    {
        Directory.CreateDirectory(_storage.RootDirectory);
        return OpenFolder(_storage.RootDirectory);
    }

    public bool OpenMeetingLink(Guid meetingId)
    {
        var link = _state.Meetings.FirstOrDefault(meeting => meeting.Id == meetingId)?.Link;
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return true;
    }

    public async Task StopForShutdownAsync(CancellationToken cancellationToken = default)
    {
        foreach (var operation in _processing.Values)
        {
            TryCancel(operation.Cancellation);
        }

        if (_recorder.Status.RecordingId is Guid activeRecordingId)
        {
            await StopAsync(
                activeRecordingId,
                allowAutoTranscription: false,
                cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var operation in _processing.Values)
        {
            TryCancel(operation.Cancellation);
        }

        await _recorder.DisposeAsync();
        _recordingGate.Dispose();
    }

    private async Task<MeetingAssistantOperationResult> StartAsync(
        Guid? meetId,
        MeetingRecordingSourceKind sourceKind,
        bool automatic,
        CancellationToken cancellationToken)
    {
        await _recordingGate.WaitAsync(cancellationToken);
        try
        {
            var service = new MeetingRecordingService(_state);
            if (_recorder.Status.RecordingId is Guid runtimeRecordingId)
            {
                Report(
                    $"MEET recording start rejected: runtime recording " +
                    $"{runtimeRecordingId:N} is already active.");
                return MeetingAssistantOperationResult.Fail(
                    "Another meeting recording is already active.");
            }

            if (service.ActiveRecording is { } staleRecording)
            {
                service.MarkFailed(
                    staleRecording.Id,
                    "No live recorder session was found. The interrupted recording can be retried.");
                Persist(staleRecording);
                Report(
                    $"Stale MEET recording state reconciled before Start: " +
                    $"recordingId={staleRecording.Id:N}.");
            }

            var recordingId = Guid.NewGuid();
            var layout = _storage.CreateLayout(meetId, recordingId);
            var recording = service.CreatePending(
                meetId,
                sourceKind,
                layout.RelativeFolder,
                recordingId: recordingId);
            if (recording is null)
            {
                return MeetingAssistantOperationResult.Fail("Recording could not be initialized.");
            }

            if (automatic)
            {
                recording.LastAutoStartAttemptAtUtc = DateTimeOffset.UtcNow;
            }

            recording.RecordingFormat = _localSettings.MeetingAssistant.RecordingFormat;

            Persist(recording);
            try
            {
                var settings = _localSettings.MeetingAssistant;
                Report(
                    $"MEET recorder initialization started: recordingId={recordingId:N}; " +
                    $"meetId={FormatOptionalId(meetId)}; " +
                    $"format={settings.RecordingFormat}.");
                var result = await _recorder.StartAsync(
                    new MeetingRecordingStartRequest(
                        recordingId,
                        layout.AbsoluteFolder,
                        NullIfEmpty(settings.MicrophoneDeviceId),
                        NullIfEmpty(settings.SystemOutputDeviceId),
                        settings.RecordingFormat),
                    cancellationToken);
                Report(
                    $"MEET recorder device initialization completed: " +
                    $"recordingId={recordingId:N}; " +
                    $"systemLoopback={result.SystemAudioHealth}; " +
                    $"microphone={result.MicrophoneHealth}.");
                if (!service.MarkRecording(recordingId, result))
                {
                    await TryStopRuntimeAfterFailedStartAsync(recordingId);
                    service.MarkFailed(
                        recordingId,
                        "Recorder started, but recording metadata could not enter the Recording state.");
                    Persist(recording);
                    return MeetingAssistantOperationResult.Fail(recording.LastError);
                }

                Persist(recording);
                Report(
                    $"MEET recording transitioned to Recording: " +
                    $"recordingId={recordingId:N}; meetId={FormatOptionalId(meetId)}.");
                return new MeetingAssistantOperationResult(
                    true,
                    RecordingId: recordingId);
            }
            catch (Exception ex)
            {
                await TryStopRuntimeAfterFailedStartAsync(recordingId);
                service.MarkFailed(recordingId, SafeMessage(ex));
                Persist(recording);
                Report("MEET recording start failed.", ex);
                return MeetingAssistantOperationResult.Fail(recording.LastError);
            }
        }
        finally
        {
            _recordingGate.Release();
        }
    }

    private async Task TryStopRuntimeAfterFailedStartAsync(Guid recordingId)
    {
        if (_recorder.Status.RecordingId != recordingId)
        {
            return;
        }

        try
        {
            await _recorder.StopAsync(recordingId, CancellationToken.None);
            Report(
                $"MEET recorder runtime cleaned up after failed Start: " +
                $"recordingId={recordingId:N}.");
        }
        catch (Exception cleanupException)
        {
            Report(
                $"MEET recorder cleanup failed after Start: recordingId={recordingId:N}.",
                cleanupException);
        }
    }

    private async Task RunAutoTranscriptionAsync(Guid recordingId)
    {
        try
        {
            await TranscribeAsync(recordingId, acceptUploadDisclosure: false);
        }
        catch (Exception ex)
        {
            Report("Automatic MEET transcription failed.", ex);
        }
    }

    private void Persist(MeetingRecording recording)
    {
        try
        {
            _storage.WriteMetadata(recording);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("Recording metadata file could not be updated.", ex);
        }

        PersistStateAndNotify();
    }

    private void PersistStateAndNotify()
    {
        _persistState();
        _stateChanged();
    }

    private string? ResolveMixedPathForTranscription(MeetingRecording recording)
    {
        var compatibilityPath = ResolveOptional(recording, recording.MixedAudioFile);
        if (recording.RecordingFormat != MeetingRecordingFormat.AacM4a)
        {
            return compatibilityPath;
        }

        var validMixedTracks = recording.Tracks
            .Where(track =>
                track.Kind == MeetingRecordingTrackKind.Mixed &&
                track.FinalizationState == MeetingRecordingFinalizationState.Finalized &&
                track.ValidationState == MeetingRecordingValidationState.Valid &&
                !string.IsNullOrWhiteSpace(track.FileName))
            .ToList();
        if (validMixedTracks.Count != 1 ||
            string.IsNullOrWhiteSpace(recording.MixedAudioFile) ||
            !string.Equals(
                recording.MixedAudioFile,
                validMixedTracks[0].FileName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Compact transcription requires metadata for exactly one finalized mixed M4A track.");
        }

        return _storage.ResolveFile(recording, validMixedTracks[0].FileName);
    }

    private static void ClearPreviousTranscriptionChunks(
        MeetingRecording recording,
        string folder)
    {
        foreach (var path in Directory.EnumerateFiles(
                     folder,
                     "transcription-*.*",
                     SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(path);
            if (extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
            }
        }

        recording.TranscriptionChunkFiles.Clear();
    }

    private async Task ReportTranscriptionInputsAsync(
        MeetingRecording recording,
        string folder,
        string? systemPath,
        string? microphonePath,
        string? mixedPath,
        CancellationToken cancellationToken)
    {
        Report(
            $"MEET transcription metadata resolved: recordingId={recording.Id:N}; " +
            $"meetId={FormatOptionalId(recording.MeetId)}; folder={folder}; " +
            $"format={recording.RecordingFormat}; " +
            $"systemFile={EmptyAsNone(recording.SystemAudioFile)}; " +
            $"microphoneFile={EmptyAsNone(recording.MicrophoneFile)}; " +
            $"mixedFile={EmptyAsNone(recording.MixedAudioFile)}.");
        foreach (var track in recording.Tracks)
        {
            Report(
                $"MEET transcription track metadata: recordingId={recording.Id:N}; " +
                $"kind={track.Kind}; fileName={EmptyAsNone(track.FileName)}; " +
                $"bytes={track.Bytes}; durationSeconds={track.DurationSeconds:F3}; " +
                $"finalization={track.FinalizationState}; validation={track.ValidationState}.");
        }

        await ReportResolvedAudioAsync(recording, "system", systemPath, cancellationToken);
        await ReportResolvedAudioAsync(recording, "microphone", microphonePath, cancellationToken);
        await ReportResolvedAudioAsync(recording, "mixed", mixedPath, cancellationToken);
    }

    private async Task ReportResolvedAudioAsync(
        MeetingRecording recording,
        string label,
        string? path,
        CancellationToken cancellationToken)
    {
        if (path is null || !File.Exists(path))
        {
            Report(
                $"MEET transcription resolved audio: recordingId={recording.Id:N}; " +
                $"kind={label}; path={path ?? "none"}; exists=False.");
            return;
        }

        try
        {
            var fingerprint = await TranscriptionAudioDiagnostics.InspectAsync(
                path,
                cancellationToken);
            Report(
                $"MEET transcription resolved audio: recordingId={recording.Id:N}; " +
                $"kind={label}; path={fingerprint.FullPath}; exists=True; " +
                $"bytes={fingerprint.Bytes}; durationSeconds={fingerprint.Duration.TotalSeconds:F3}; " +
                $"sha256={fingerprint.Sha256}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            NotSupportedException)
        {
            Report(
                $"MEET transcription audio diagnostics failed: " +
                $"recordingId={recording.Id:N}; kind={label}; path={path}.",
                ex);
        }
    }

    private static void EnsureFileInsideFolder(string path, string folder)
    {
        var fullPath = Path.GetFullPath(path);
        var fullFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) +
                         Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A transcription chunk is outside its recording folder.");
        }
    }

    private static bool PathsEqual(string left, string? right) =>
        right is not null && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static string EmptyAsNone(string value) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value;

    private MeetingRecordingLayout LayoutFor(MeetingRecording recording)
    {
        var folder = _storage.ResolveFolder(recording.RecordingFolderRelativePath);
        return new MeetingRecordingLayout(
            recording.RecordingFolderRelativePath,
            folder,
            Path.Combine(folder, "recording-meta.json"),
            Path.Combine(folder, "system.wav"),
            Path.Combine(folder, "microphone.wav"),
            Path.Combine(folder, "mixed.wav"),
            Path.Combine(folder, "transcript.raw.json"),
            Path.Combine(folder, "transcript.json"),
            Path.Combine(folder, "transcript.md"),
            Path.Combine(folder, "analysis.json"));
    }

    private string? ResolveOptional(MeetingRecording recording, string fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? null : _storage.ResolveFile(recording, fileName);

    private MeetingTranscript? ResolveTranscriptForRecording(MeetingRecording recording)
    {
        if (recording.MeetId is Guid meetId)
        {
            var activeId = _state.Meetings.FirstOrDefault(meeting => meeting.Id == meetId)
                ?.ActiveTranscriptId;
            var active = activeId is Guid activeTranscriptId
                ? _state.MeetingTranscripts.FirstOrDefault(transcript =>
                    transcript.Id == activeTranscriptId && transcript.MeetId == meetId)
                : null;
            if (active is not null)
            {
                return active;
            }
        }

        var existing = _state.MeetingTranscripts
            .Where(transcript => transcript.RecordingId == recording.Id)
            .OrderByDescending(transcript => transcript.CreatedAtUtc)
            .FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        var transcriptPath = ResolveOptional(recording, recording.TranscriptFile);
        if (transcriptPath is null || !File.Exists(transcriptPath))
        {
            return null;
        }

        var normalized = JsonSerializer.Deserialize<NormalizedTranscript>(
            File.ReadAllText(transcriptPath),
            _jsonOptions);
        if (normalized is null)
        {
            return null;
        }

        var id = _state.MeetingTranscripts.Any(transcript => transcript.Id == recording.Id)
            ? Guid.NewGuid()
            : recording.Id;
        var revisionId = normalized.RevisionId == Guid.Empty ? Guid.NewGuid() : normalized.RevisionId;
        normalized.TranscriptId = id;
        normalized.RevisionId = revisionId;
        normalized.HasTimestamps = normalized.HasTimestamps || normalized.Segments.Count > 0;
        TranscriptSpeakerMapping.EnsureStableSpeakers(normalized);
        _storage.WriteJsonAtomic(transcriptPath, normalized);
        var transcript = new MeetingTranscript
        {
            Id = id,
            MeetId = recording.MeetId,
            RecordingId = recording.Id,
            Origin = MeetingTranscriptOrigin.Generated,
            Format = MeetingTranscriptFormat.NormalizedJson,
            Provider = normalized.Provider,
            Model = normalized.Model,
            SourceLabel = "TaskOverlay",
            StorageFolderRelativePath = recording.RecordingFolderRelativePath,
            OriginalArtifactFile = recording.TranscriptRawFile.Length > 0
                ? recording.TranscriptRawFile
                : recording.TranscriptFile,
            NormalizedArtifactFile = recording.TranscriptFile,
            MarkdownArtifactFile = recording.TranscriptMarkdownFile,
            HasTimestamps = normalized.HasTimestamps,
            HasSpeakerLabels = normalized.Speakers.Count > 0,
            RevisionId = revisionId,
            Speakers = normalized.Speakers.Select(CloneSpeaker).ToList(),
            CreatedAtUtc = normalized.GeneratedAtUtc,
            UpdatedAtUtc = normalized.GeneratedAtUtc
        };
        _state.MeetingTranscripts.Add(transcript);
        if (recording.MeetId is Guid ownerId)
        {
            var meeting = _state.Meetings.FirstOrDefault(item => item.Id == ownerId);
            if (meeting is not null)
            {
                meeting.ActiveTranscriptId ??= transcript.Id;
            }
        }

        PersistStateAndNotify();
        return transcript;
    }

    private void RepairManagedTranscriptArtifacts()
    {
        var stateChanged = false;
        foreach (var metadata in _state.MeetingTranscripts)
        {
            try
            {
                var path = _sourceStorage.ResolveTranscriptFile(
                    metadata,
                    metadata.NormalizedArtifactFile);
                if (!File.Exists(path))
                {
                    continue;
                }

                var normalized = JsonSerializer.Deserialize<NormalizedTranscript>(
                    File.ReadAllText(path),
                    _jsonOptions);
                if (normalized is null)
                {
                    continue;
                }

                var artifactChanged = normalized.TranscriptId != metadata.Id ||
                                      normalized.RevisionId != metadata.RevisionId ||
                                      normalized.Segments.Any(segment =>
                                          !string.IsNullOrWhiteSpace(segment.Speaker) &&
                                          string.IsNullOrWhiteSpace(segment.SpeakerId));
                normalized.TranscriptId = metadata.Id;
                normalized.RevisionId = metadata.RevisionId;
                normalized.HasTimestamps = normalized.HasTimestamps ||
                                           normalized.Segments.Any(segment =>
                                               segment.StartSeconds > 0 || segment.EndSeconds > 0);
                artifactChanged |= TranscriptSpeakerMapping.EnsureStableSpeakers(normalized);
                foreach (var speaker in normalized.Speakers)
                {
                    if (metadata.Speakers.Any(existing => string.Equals(
                            existing.SpeakerId,
                            speaker.SpeakerId,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    metadata.Speakers.Add(CloneSpeaker(speaker));
                    stateChanged = true;
                }

                if (metadata.HasTimestamps != normalized.HasTimestamps ||
                    metadata.HasSpeakerLabels != metadata.Speakers.Count > 0)
                {
                    metadata.HasTimestamps = normalized.HasTimestamps;
                    metadata.HasSpeakerLabels = metadata.Speakers.Count > 0;
                    stateChanged = true;
                }

                if (artifactChanged && metadata.MarkdownArtifactFile.Length > 0)
                {
                    _sourceStorage.WriteTranscript(metadata, normalized);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       InvalidDataException or JsonException)
            {
                Report($"MEET transcript repair skipped: transcriptId={metadata.Id:N}.", ex);
            }
        }

        if (stateChanged)
        {
            _persistState();
        }
    }

    private static MeetingRecordingTrackArtifact CloneTrack(
        MeetingRecordingTrackArtifact track) => new()
    {
        Kind = track.Kind,
        FileName = track.FileName,
        InProgressFileName = track.InProgressFileName,
        SegmentFiles = track.SegmentFiles?.ToList() ?? new List<string>(),
        Container = track.Container,
        Codec = track.Codec,
        SampleRate = track.SampleRate,
        ChannelCount = track.ChannelCount,
        Bitrate = track.Bitrate,
        DurationSeconds = track.DurationSeconds,
        Bytes = track.Bytes,
        HasAudioFrames = track.HasAudioFrames,
        FinalizationState = track.FinalizationState,
        ValidationState = track.ValidationState,
        Error = track.Error
    };

    private void RewriteAnalysisFile(MeetingAnalysis analysis)
    {
        var recording = analysis.RecordingId is Guid recordingId
            ? new MeetingRecordingService(_state).Find(recordingId)
            : null;
        if (recording is null)
        {
            var transcript = analysis.TranscriptId is Guid transcriptId
                ? _state.MeetingTranscripts.FirstOrDefault(item => item.Id == transcriptId)
                : null;
            if (transcript is null)
            {
                return;
            }

            var folder = _sourceStorage.ResolveFolder(transcript.StorageFolderRelativePath);
            _sourceStorage.WriteJsonAtomic(
                Path.Combine(folder, $"analysis-{analysis.Id:N}.json"),
                new { analysis });
            return;
        }

        _storage.WriteJsonAtomic(LayoutFor(recording).AnalysisPath, new { analysis });
    }

    private void UpdateProcessingStage(Guid operationId, string stage)
    {
        if (_processing.TryGetValue(operationId, out var operation))
        {
            operation.Stage = stage;
            _stateChanged();
        }
    }

    private void CompleteProcessing(Guid recordingId)
    {
        if (_processing.TryRemove(recordingId, out var operation))
        {
            operation.Cancellation.Dispose();
            _stateChanged();
        }
    }

    private sealed class ProcessingOperation
    {
        private ProcessingOperation(
            Guid id,
            string kind,
            string stage,
            Guid? meetingId,
            Guid? recordingId,
            Guid? transcriptId,
            CancellationToken cancellationToken)
        {
            Id = id;
            Kind = kind;
            Stage = stage;
            MeetingId = meetingId;
            RecordingId = recordingId;
            TranscriptId = transcriptId;
            StartedAtUtc = DateTimeOffset.UtcNow;
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public Guid Id { get; }
        public string Kind { get; }
        public string Stage { get; set; }
        public Guid? MeetingId { get; }
        public Guid? RecordingId { get; }
        public Guid? TranscriptId { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public bool CancellationRequested { get; set; }
        public CancellationTokenSource Cancellation { get; }

        public static ProcessingOperation ForTranscription(
            MeetingRecording recording,
            CancellationToken cancellationToken) => new(
                recording.Id,
                "Transcription",
                "PreparingAudio",
                recording.MeetId,
                recording.Id,
                null,
                cancellationToken);

        public static ProcessingOperation ForAnalysis(
            MeetingTranscript transcript,
            CancellationToken cancellationToken) => new(
                transcript.Id,
                "Analysis",
                "Analyzing",
                transcript.MeetId,
                transcript.RecordingId,
                transcript.Id,
                cancellationToken);

        public WorkspaceMeetingOperationSnapshot ToSnapshot() => new(
            Id.ToString("N"),
            Kind,
            Stage,
            MeetingId?.ToString("N"),
            RecordingId?.ToString("N"),
            TranscriptId?.ToString("N"),
            StartedAtUtc,
            CancellationRequested);
    }

    private static NormalizedTranscript ComposeTranscript(
        Guid recordingId,
        Guid transcriptId,
        Guid transcriptRevisionId,
        string provider,
        string model,
        IReadOnlyList<TranscriptionProviderResponse> responses)
    {
        var segments = responses
            .SelectMany(response => response.Segments)
            .OrderBy(segment => segment.StartSeconds)
            .ThenBy(segment => segment.EndSeconds)
            .Select((segment, index) => new TranscriptSegment
            {
                Index = index,
                StartSeconds = Math.Max(0, segment.StartSeconds),
                EndSeconds = Math.Max(segment.StartSeconds, segment.EndSeconds),
                Text = segment.Text.Trim(),
                SpeakerId = segment.SpeakerId,
                Speaker = NullIfEmpty(segment.Speaker)
            })
            .Where(segment => segment.Text.Length > 0)
            .ToList();
        return new NormalizedTranscript
        {
            TranscriptId = transcriptId,
            RevisionId = transcriptRevisionId,
            RecordingId = recordingId,
            Provider = provider,
            Model = model,
            Language = responses.Select(response => response.DetectedLanguage)
                .FirstOrDefault(language => !string.IsNullOrWhiteSpace(language)) ?? string.Empty,
            Text = string.Join(
                Environment.NewLine,
                responses.Select(response => response.Text.Trim())
                    .Where(text => text.Length > 0)),
            Segments = segments,
            HasTimestamps = segments.Count > 0,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string BuildRawResponseArray(
        IReadOnlyList<TranscriptionProviderResponse> responses)
    {
        var builder = new StringBuilder("[\n");
        for (var index = 0; index < responses.Count; index++)
        {
            using var document = JsonDocument.Parse(responses[index].RawJson);
            builder.Append(JsonSerializer.Serialize(document.RootElement));
            if (index < responses.Count - 1)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildTranscriptMarkdown(NormalizedTranscript transcript)
        => MeetingTranscriptService.BuildMarkdown(transcript, transcript.Speakers);

    private static TranscriptSpeaker CloneSpeaker(TranscriptSpeaker speaker) => new()
    {
        SpeakerId = speaker.SpeakerId,
        OriginalLabel = speaker.OriginalLabel,
        DisplayName = speaker.DisplayName,
        IsCurrentUser = speaker.IsCurrentUser
    };

    private static bool OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return true;
    }

    private static bool TryCancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static string SafeMessage(Exception exception) =>
        ProviderErrorRedactor.Redact(exception.Message);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatOptionalId(Guid? value) =>
        value is Guid id ? id.ToString("N") : "none";

    private void Report(string message, Exception? exception = null)
    {
        try
        {
            _diagnostic?.Invoke(message, exception);
        }
        catch
        {
            // Diagnostics must not change recording behavior.
        }
    }
}
