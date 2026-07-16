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
    IReadOnlyList<Guid>? CreatedContextItemIds = null)
{
    public static MeetingAssistantOperationResult Fail(string error) =>
        new(false, error);
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
    private readonly IMeetingRecorder _recorder;
    private readonly IMeetingAudioProcessor _audioProcessor;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly IMeetingAnalysisProvider _analysisProvider;
    private readonly SemaphoreSlim _recordingGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _processing = new();
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
    }

    public string RecordingsRoot => _storage.RootDirectory;
    public MeetingRecorderRuntimeStatus RuntimeStatus => _recorder.Status;
    public IReadOnlyList<AudioDeviceDescriptor> MicrophoneDevices =>
        _recorder.GetMicrophoneDevices();
    public IReadOnlyList<AudioDeviceDescriptor> SystemOutputDevices =>
        _recorder.GetSystemOutputDevices();

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

        if (!_processing.TryAdd(
                recordingId,
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)))
        {
            return MeetingAssistantOperationResult.Fail(
                "This recording already has a processing operation in progress.");
        }

        var operation = _processing[recordingId];
        var token = operation.Token;
        var service = new MeetingRecordingService(_state);
        var recording = service.Find(recordingId);
        if (recording is null)
        {
            CompleteProcessing(recordingId);
            return MeetingAssistantOperationResult.Fail("Recording was not found.");
        }

        try
        {
            if (!service.MarkProcessing(recordingId))
            {
                return MeetingAssistantOperationResult.Fail(
                    "Recording is not ready for transcription.");
            }

            Persist(recording);
            var folder = _storage.ResolveFolder(recording.RecordingFolderRelativePath);
            var systemPath = ResolveOptional(recording, recording.SystemAudioFile);
            var microphonePath = ResolveOptional(recording, recording.MicrophoneFile);
            var mixedPath = ResolveOptional(recording, recording.MixedAudioFile);
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
                    MixedAudioBitrate: mixedBitrate),
                token);
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
            Persist(recording);

            var responses = new List<TranscriptionProviderResponse>();
            var offset = TimeSpan.Zero;
            foreach (var chunkPath in processing.OrderedChunkPaths)
            {
                token.ThrowIfCancellationRequested();
                var response = await _transcriptionProvider.TranscribeAsync(
                    new TranscriptionProviderRequest(
                        chunkPath,
                        _localSettings.MeetingAssistant.TranscriptionModel,
                        _localSettings.MeetingAssistant.Language,
                        offset),
                    token);
                responses.Add(response);
                offset += ReadAudioDuration(chunkPath);
            }

            var normalized = ComposeTranscript(
                recordingId,
                _transcriptionProvider.Name,
                _localSettings.MeetingAssistant.TranscriptionModel,
                responses);
            var layout = LayoutFor(recording);
            MeetingRecordingStorage.WriteTextAtomic(
                layout.TranscriptRawPath,
                BuildRawResponseArray(responses));
            _storage.WriteJsonAtomic(layout.TranscriptPath, normalized);
            MeetingRecordingStorage.WriteTextAtomic(
                layout.TranscriptMarkdownPath,
                BuildTranscriptMarkdown(normalized));
            service.MarkTranscriptReady(
                recordingId,
                Path.GetFileName(processing.MixedAudioPath),
                processing.OrderedChunkPaths.Select(Path.GetFileName).Cast<string>().ToList(),
                Path.GetFileName(layout.TranscriptRawPath),
                Path.GetFileName(layout.TranscriptPath),
                Path.GetFileName(layout.TranscriptMarkdownPath));
            Persist(recording);
            return new MeetingAssistantOperationResult(true, RecordingId: recordingId);
        }
        catch (OperationCanceledException)
        {
            service.MarkFailed(recordingId, "Transcription was cancelled. Original audio was kept.");
            Persist(recording);
            return MeetingAssistantOperationResult.Fail(recording.LastError);
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
        if (!_processing.TryAdd(
                recordingId,
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)))
        {
            return MeetingAssistantOperationResult.Fail(
                "This recording already has a processing operation in progress.");
        }

        var operation = _processing[recordingId];
        var service = new MeetingRecordingService(_state);
        var recording = service.Find(recordingId);
        if (recording is null)
        {
            CompleteProcessing(recordingId);
            return MeetingAssistantOperationResult.Fail("Recording was not found.");
        }

        try
        {
            var transcriptPath = ResolveOptional(recording, recording.TranscriptFile);
            if (transcriptPath is null || !File.Exists(transcriptPath))
            {
                return MeetingAssistantOperationResult.Fail(
                    "Transcript file is missing. Retry transcription first.");
            }

            if (!service.MarkAnalyzing(recordingId))
            {
                return MeetingAssistantOperationResult.Fail(
                    "Recording is not ready for analysis.");
            }

            Persist(recording);
            var transcript = JsonSerializer.Deserialize<NormalizedTranscript>(
                                 await File.ReadAllTextAsync(transcriptPath, operation.Token),
                                 _jsonOptions) ??
                             throw new InvalidDataException("Transcript file is invalid.");
            var meeting = recording.MeetId is Guid meetId
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
                    recordingId,
                    recording.MeetId,
                    projectId,
                    meeting?.Title ?? "Emergency recording",
                    transcript,
                    _localSettings.MeetingAssistant.AnalysisModel),
                operation.Token);
            _state.MeetingAnalyses.RemoveAll(analysis =>
                analysis.RecordingId == recordingId &&
                analysis.State == MeetingAnalysisState.Failed);
            _state.MeetingAnalyses.Add(providerResult.Analysis);
            var layout = LayoutFor(recording);
            _storage.WriteJsonAtomic(
                layout.AnalysisPath,
                new
                {
                    providerRawJson = providerResult.RawJson,
                    analysis = providerResult.Analysis
                });
            service.MarkReady(recordingId, Path.GetFileName(layout.AnalysisPath));
            Persist(recording);
            return new MeetingAssistantOperationResult(
                true,
                RecordingId: recordingId,
                AnalysisId: providerResult.Analysis.Id);
        }
        catch (OperationCanceledException)
        {
            service.MarkFailed(recordingId, "Analysis was cancelled. Transcript and audio were kept.");
            Persist(recording);
            return MeetingAssistantOperationResult.Fail(recording.LastError);
        }
        catch (Exception ex) when (
            ex is IOException or
            InvalidDataException or
            InvalidOperationException or
            HttpRequestException or
            OpenAiProviderException or
            JsonException)
        {
            service.MarkFailed(recordingId, SafeMessage(ex));
            var failed = new MeetingAnalysis
            {
                RecordingId = recordingId,
                MeetId = recording.MeetId,
                State = MeetingAnalysisState.Failed,
                Provider = _analysisProvider.Name,
                Model = _localSettings.MeetingAssistant.AnalysisModel,
                LastError = SafeMessage(ex)
            };
            _state.MeetingAnalyses.Add(failed);
            Persist(recording);
            Report("MEET analysis failed.", ex);
            return MeetingAssistantOperationResult.Fail(recording.LastError);
        }
        finally
        {
            CompleteProcessing(recordingId);
        }
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
        if (!Enum.IsDefined(format) || _recorder.Status.RecordingId is not null)
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
        if (recording is null || recording.IsActive || _processing.ContainsKey(recordingId))
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

    public bool CancelProcessing(Guid recordingId)
    {
        return _processing.TryGetValue(recordingId, out var cancellation) &&
               TryCancel(cancellation);
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
            TryCancel(operation);
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
            TryCancel(operation);
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

    private static TimeSpan ReadAudioDuration(string path)
    {
        using var reader = new AudioFileReader(path);
        return reader.TotalTime;
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
        var recording = new MeetingRecordingService(_state).Find(analysis.RecordingId);
        if (recording is null)
        {
            return;
        }

        _storage.WriteJsonAtomic(LayoutFor(recording).AnalysisPath, new { analysis });
    }

    private void CompleteProcessing(Guid recordingId)
    {
        if (_processing.TryRemove(recordingId, out var cancellation))
        {
            cancellation.Dispose();
        }
    }

    private static NormalizedTranscript ComposeTranscript(
        Guid recordingId,
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
                Speaker = NullIfEmpty(segment.Speaker)
            })
            .Where(segment => segment.Text.Length > 0)
            .ToList();
        return new NormalizedTranscript
        {
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
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Transcript");
        builder.AppendLine();
        foreach (var segment in transcript.Segments)
        {
            var timestamp = TimeSpan.FromSeconds(segment.StartSeconds)
                .ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            var speaker = string.IsNullOrWhiteSpace(segment.Speaker)
                ? string.Empty
                : $" **{segment.Speaker}:**";
            builder.AppendLine($"- `{timestamp}`{speaker} {segment.Text}");
        }

        if (transcript.Segments.Count == 0)
        {
            builder.AppendLine(transcript.Text);
        }

        return builder.ToString();
    }

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
