using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed class WindowsMeetingRecorder : IMeetingRecorder
{
    private static readonly TimeSpan MixSafetyDelay = TimeSpan.FromSeconds(2);
    private readonly object _sync = new();
    private readonly IRecordingTrackWriterFactory _writerFactory;
    private readonly Action<string, Exception?>? _diagnostic;
    private ActiveSession? _active;
    private MeetingRecorderRuntimeStatus _lastStatus = IdleStatus();

    public WindowsMeetingRecorder(
        Action<string, Exception?>? diagnostic = null,
        IRecordingTrackWriterFactory? writerFactory = null)
    {
        _diagnostic = diagnostic;
        _writerFactory = writerFactory ?? new RecordingTrackWriterFactory(diagnostic);
    }

    public MeetingRecorderRuntimeStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _active?.CreateStatus() ?? _lastStatus;
            }
        }
    }

    public IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices() =>
        EnumerateDevices(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceDescriptor> GetSystemOutputDevices() =>
        EnumerateDevices(DataFlow.Render);

    public async Task<MeetingRecordingStartResult> StartAsync(
        MeetingRecordingStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(request.RecordingFormat))
        {
            throw new ArgumentOutOfRangeException(nameof(request.RecordingFormat));
        }

        lock (_sync)
        {
            if (_active is not null)
            {
                throw new InvalidOperationException(
                    "Another meeting recording is already active.");
            }
        }

        Directory.CreateDirectory(request.RecordingFolder);
        var warnings = new List<string>();
        TrackCapture? system = null;
        TrackCapture? microphone = null;
        RealtimePcmMixer? mixer = null;
        var clock = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            system = await TryCreateTrackAsync(
                DataFlow.Render,
                request.SystemOutputDeviceId,
                loopback: true,
                MeetingRecordingTrackKind.System,
                preferredChannels: 2,
                request,
                clock,
                warnings,
                cancellationToken);
            microphone = await TryCreateTrackAsync(
                DataFlow.Capture,
                request.MicrophoneDeviceId,
                loopback: false,
                MeetingRecordingTrackKind.Microphone,
                preferredChannels: 1,
                request,
                clock,
                warnings,
                cancellationToken);
            if (system is null && microphone is null)
            {
                throw new InvalidOperationException(
                    "Neither system audio nor microphone capture could be initialized.");
            }

            var mixedWriter = _writerFactory.Create(request.RecordingFormat);
            mixer = new RealtimePcmMixer(
                system is not null,
                microphone is not null,
                mixedWriter,
                _diagnostic);
            await mixer.StartAsync(request.RecordingFolder, cancellationToken);
            system?.AttachMixer(mixer);
            microphone?.AttachMixer(mixer);

            system?.Start();
            microphone?.Start();
            var active = new ActiveSession(
                request.RecordingId,
                request.RecordingFormat,
                startedAt,
                clock,
                system,
                microphone,
                mixer,
                _diagnostic);
            active.StartHeartbeat(MixSafetyDelay);
            lock (_sync)
            {
                if (_active is not null)
                {
                    throw new InvalidOperationException(
                        "Another meeting recording became active while capture was starting.");
                }

                _active = active;
                _lastStatus = active.CreateStatus();
            }

            var artifacts = active.SnapshotArtifacts();
            Report(
                $"Meeting recording capture started: recordingId={request.RecordingId:N}; " +
                $"format={request.RecordingFormat}; " +
                $"systemFormat={DescribeFormat(system?.WaveFormat)}; " +
                $"microphoneFormat={DescribeFormat(microphone?.WaveFormat)}; " +
                $"mixedFormat={DescribeFormat(mixedWriter.InputFormat)}.");
            return new MeetingRecordingStartResult(
                startedAt,
                system?.StartedAtUtc,
                microphone?.StartedAtUtc,
                string.Empty,
                string.Empty,
                system is null ? AudioTrackHealth.Unavailable : AudioTrackHealth.Healthy,
                microphone is null ? AudioTrackHealth.Unavailable : AudioTrackHealth.Healthy,
                warnings.Count == 0 ? null : string.Join(" ", warnings),
                request.RecordingFormat,
                artifacts);
        }
        catch
        {
            await CleanupFailedStartAsync(system, microphone, mixer);
            throw;
        }
    }

    public async Task<MeetingRecordingStopResult> StopAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        ActiveSession active;
        lock (_sync)
        {
            active = _active ??
                     throw new InvalidOperationException("No meeting recording is active.");
            if (active.RecordingId != recordingId)
            {
                throw new InvalidOperationException("A different meeting recording is active.");
            }

            active.Finalizing = true;
        }

        var warnings = new List<string>();
        IReadOnlyList<MeetingRecordingTrackArtifact> artifacts;
        AudioTrackHealth systemHealth;
        AudioTrackHealth microphoneHealth;
        var stoppedAt = DateTimeOffset.UtcNow;
        try
        {
            await Task.WhenAll(
                active.System?.StopCaptureAsync(cancellationToken) ?? Task.CompletedTask,
                active.Microphone?.StopCaptureAsync(cancellationToken) ?? Task.CompletedTask);
            stoppedAt = DateTimeOffset.UtcNow;
            await active.StopHeartbeatAsync();

            var systemTask = CompleteTrackAsync(
                active.System,
                "System audio",
                warnings,
                cancellationToken);
            var microphoneTask = CompleteTrackAsync(
                active.Microphone,
                "Microphone",
                warnings,
                cancellationToken);
            var mixedTask = active.Mixer.CompleteAsync(
                active.Clock.Elapsed,
                cancellationToken);
            await Task.WhenAll(
                (Task)systemTask,
                (Task)microphoneTask,
                mixedTask);
            var systemResult = await systemTask;
            var microphoneResult = await microphoneTask;
            var mixedResult = await mixedTask;
            systemHealth = ResolveHealth(active.System, systemResult);
            microphoneHealth = ResolveHealth(active.Microphone, microphoneResult);
            if (mixedResult.ValidationState != MeetingRecordingValidationState.Valid)
            {
                warnings.Add(
                    $"Mixed track is unavailable ({NormalizeTrackError(mixedResult)}). " +
                    "Source tracks were kept.");
            }

            artifacts = new[] { systemResult, microphoneResult, mixedResult }
                .Where(artifact => artifact is not null)
                .Cast<MeetingRecordingTrackArtifact>()
                .ToList();
            var hasUsableAudio = artifacts.Any(artifact =>
                artifact.HasAudioFrames &&
                artifact.ValidationState == MeetingRecordingValidationState.Valid);
            if (!hasUsableAudio)
            {
                warnings.Add("No microphone or system audio frames were captured.");
            }

            Report(
                $"Meeting recording files finalized: recordingId={recordingId:N}; " +
                $"format={active.Format}; system={systemHealth}; " +
                $"microphone={microphoneHealth}; mixed={mixedResult.ValidationState}; " +
                $"bytes={artifacts.Sum(artifact => artifact.Bytes)}.");
            return new MeetingRecordingStopResult(
                stoppedAt,
                systemHealth,
                microphoneHealth,
                warnings.Count == 0 ? null : string.Join(" ", warnings),
                active.Format,
                artifacts,
                hasUsableAudio);
        }
        finally
        {
            await active.DisposeAsync();
            lock (_sync)
            {
                _active = null;
                _lastStatus = new MeetingRecorderRuntimeStatus(
                    null,
                    null,
                    active.System?.Failed == true
                        ? AudioTrackHealth.Failed
                        : AudioTrackHealth.Unknown,
                    active.Microphone?.Failed == true
                        ? AudioTrackHealth.Failed
                        : AudioTrackHealth.Unknown,
                    warnings.Count == 0 ? active.Error : string.Join(" ", warnings),
                    active.Format);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        ActiveSession? active;
        lock (_sync)
        {
            active = _active;
        }

        if (active is not null)
        {
            try
            {
                await StopAsync(active.RecordingId);
            }
            catch (Exception ex)
            {
                Report("Active meeting recording could not finalize during disposal.", ex);
                await active.AbortAsync();
                lock (_sync)
                {
                    _active = null;
                    _lastStatus = IdleStatus();
                }
            }
        }
    }

    private async Task<TrackCapture?> TryCreateTrackAsync(
        DataFlow flow,
        string? requestedDeviceId,
        bool loopback,
        MeetingRecordingTrackKind kind,
        int preferredChannels,
        MeetingRecordingStartRequest request,
        Stopwatch clock,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        MMDevice device;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            device = string.IsNullOrWhiteSpace(requestedDeviceId)
                ? enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia)
                : enumerator.GetDevice(requestedDeviceId.Trim());
        }
        catch (Exception ex) when (IsDeviceUnavailableException(ex))
        {
            warnings.Add(
                $"{(loopback ? "System audio" : "Microphone")} unavailable " +
                $"({ex.GetType().Name}).");
            return null;
        }

        var writer = _writerFactory.Create(request.RecordingFormat);
        try
        {
            await writer.StartAsync(
                new RecordingTrackWriterStartRequest(
                    kind,
                    request.RecordingFolder,
                    kind == MeetingRecordingTrackKind.System ? "system" : "microphone",
                    preferredChannels),
                cancellationToken);
        }
        catch
        {
            device.Dispose();
            await writer.DisposeAsync();
            throw;
        }

        IWaveIn? capture = null;
        try
        {
            capture = loopback
                ? new WasapiLoopbackCapture(device)
                : new WasapiCapture(device);
            capture.WaveFormat = writer.InputFormat;
            return new TrackCapture(
                capture,
                device,
                writer,
                kind,
                clock,
                _diagnostic);
        }
        catch (Exception ex) when (IsDeviceUnavailableException(ex))
        {
            await writer.AbortAsync(CancellationToken.None);
            await writer.DisposeAsync();
            capture?.Dispose();
            device.Dispose();
            warnings.Add(
                $"{(loopback ? "System audio" : "Microphone")} unavailable " +
                $"({ex.GetType().Name}).");
            return null;
        }
    }

    private static bool IsDeviceUnavailableException(Exception exception) =>
        exception is InvalidOperationException or
            ArgumentException or
            UnauthorizedAccessException or
            IOException or
            COMException;

    private static async Task<MeetingRecordingTrackArtifact?> CompleteTrackAsync(
        TrackCapture? track,
        string label,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        if (track is null)
        {
            return null;
        }

        var artifact = await track.Writer.CompleteAsync(cancellationToken);
        if (track.BytesCaptured <= 0)
        {
            warnings.Add($"{label} track received no audio frames.");
        }
        else if (artifact.ValidationState != MeetingRecordingValidationState.Valid)
        {
            warnings.Add($"{label} did not finalize ({NormalizeTrackError(artifact)}).");
        }

        return artifact;
    }

    private static AudioTrackHealth ResolveHealth(
        TrackCapture? track,
        MeetingRecordingTrackArtifact? artifact)
    {
        if (track is null || track.BytesCaptured <= 0)
        {
            return AudioTrackHealth.Unavailable;
        }

        return track.Failed ||
               artifact?.ValidationState != MeetingRecordingValidationState.Valid
            ? AudioTrackHealth.Failed
            : AudioTrackHealth.Healthy;
    }

    private static string NormalizeTrackError(MeetingRecordingTrackArtifact artifact) =>
        string.IsNullOrWhiteSpace(artifact.Error)
            ? artifact.ValidationState.ToString()
            : artifact.Error;

    private async Task CleanupFailedStartAsync(
        TrackCapture? system,
        TrackCapture? microphone,
        RealtimePcmMixer? mixer)
    {
        foreach (var track in new[] { system, microphone }.Where(track => track is not null))
        {
            try
            {
                await track!.AbortAsync();
            }
            catch
            {
                // Continue cleanup for remaining native capture handles.
            }
        }

        if (mixer is not null)
        {
            await mixer.DisposeAsync();
        }
    }

    private static IReadOnlyList<AudioDeviceDescriptor> EnumerateDevices(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = enumerator
                .GetDefaultAudioEndpoint(flow, Role.Multimedia)
                .ID;
            return enumerator
                .EnumerateAudioEndPoints(flow, DeviceState.Active)
                .Select(device => new AudioDeviceDescriptor(
                    device.ID,
                    device.FriendlyName,
                    string.Equals(device.ID, defaultId, StringComparison.Ordinal)))
                .OrderByDescending(device => device.IsDefault)
                .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<AudioDeviceDescriptor>();
        }
    }

    private static string DescribeFormat(WaveFormat? format) => format is null
        ? "unavailable"
        : $"{format.SampleRate}Hz/{format.BitsPerSample}bit/{format.Channels}ch";

    private static MeetingRecorderRuntimeStatus IdleStatus() => new(
        null,
        null,
        AudioTrackHealth.Unknown,
        AudioTrackHealth.Unknown,
        null,
        null);

    private void Report(string message, Exception? exception = null)
    {
        try
        {
            _diagnostic?.Invoke(message, exception);
        }
        catch
        {
            // Diagnostics never affect recording behavior.
        }
    }

    private sealed class ActiveSession : IAsyncDisposable
    {
        private readonly CancellationTokenSource _heartbeatCancellation = new();
        private readonly Action<string, Exception?>? _diagnostic;
        private Task? _heartbeat;

        public ActiveSession(
            Guid recordingId,
            MeetingRecordingFormat format,
            DateTimeOffset startedAtUtc,
            Stopwatch clock,
            TrackCapture? system,
            TrackCapture? microphone,
            RealtimePcmMixer mixer,
            Action<string, Exception?>? diagnostic)
        {
            RecordingId = recordingId;
            Format = format;
            StartedAtUtc = startedAtUtc;
            Clock = clock;
            System = system;
            Microphone = microphone;
            Mixer = mixer;
            _diagnostic = diagnostic;
        }

        public Guid RecordingId { get; }
        public MeetingRecordingFormat Format { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public Stopwatch Clock { get; }
        public TrackCapture? System { get; }
        public TrackCapture? Microphone { get; }
        public RealtimePcmMixer Mixer { get; }
        public bool Finalizing { get; set; }
        public string? Error => System?.Error ?? Microphone?.Error ?? Mixer.Error;

        public void StartHeartbeat(TimeSpan safetyDelay)
        {
            _heartbeat = Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
                    while (await timer.WaitForNextTickAsync(_heartbeatCancellation.Token))
                    {
                        if (!Mixer.TryAdvance(Clock.Elapsed, safetyDelay))
                        {
                            return;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal Stop cancels the heartbeat before final mix drain.
                }
                catch (Exception ex)
                {
                    try
                    {
                        _diagnostic?.Invoke("Meeting mix heartbeat failed.", ex);
                    }
                    catch
                    {
                    }
                }
            });
        }

        public async Task StopHeartbeatAsync()
        {
            _heartbeatCancellation.Cancel();
            if (_heartbeat is not null)
            {
                await _heartbeat;
            }
        }

        public IReadOnlyList<MeetingRecordingTrackArtifact> SnapshotArtifacts()
        {
            var artifacts = new List<MeetingRecordingTrackArtifact>();
            if (System is not null)
            {
                artifacts.Add(System.Writer.SnapshotArtifact());
            }

            if (Microphone is not null)
            {
                artifacts.Add(Microphone.Writer.SnapshotArtifact());
            }

            artifacts.Add(Mixer.SnapshotArtifact());
            return artifacts;
        }

        public MeetingRecorderRuntimeStatus CreateStatus() => new(
            RecordingId,
            StartedAtUtc,
            System is null
                ? AudioTrackHealth.Unavailable
                : System.Failed
                    ? AudioTrackHealth.Failed
                    : AudioTrackHealth.Healthy,
            Microphone is null
                ? AudioTrackHealth.Unavailable
                : Microphone.Failed
                    ? AudioTrackHealth.Failed
                    : AudioTrackHealth.Healthy,
            Error,
            Format);

        public async Task AbortAsync()
        {
            _heartbeatCancellation.Cancel();
            await Task.WhenAll(
                System?.AbortAsync() ?? Task.CompletedTask,
                Microphone?.AbortAsync() ?? Task.CompletedTask);
            await Mixer.DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _heartbeatCancellation.Dispose();
            System?.Dispose();
            Microphone?.Dispose();
            if (System is not null)
            {
                await System.Writer.DisposeAsync();
            }

            if (Microphone is not null)
            {
                await Microphone.Writer.DisposeAsync();
            }

            await Mixer.DisposeAsync();
        }
    }

    private sealed class TrackCapture : IDisposable
    {
        private readonly IWaveIn _capture;
        private readonly MMDevice _device;
        private readonly MeetingRecordingTrackKind _kind;
        private readonly Stopwatch _clock;
        private readonly Action<string, Exception?>? _diagnostic;
        private readonly TaskCompletionSource _stopped = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TrackTimestampClock _timestampClock;
        private RealtimePcmMixer? _mixer;
        private bool _disposed;
        private bool _reportedFirstFrame;

        public TrackCapture(
            IWaveIn capture,
            MMDevice device,
            IRecordingTrackWriter writer,
            MeetingRecordingTrackKind kind,
            Stopwatch clock,
            Action<string, Exception?>? diagnostic)
        {
            _capture = capture;
            _device = device;
            Writer = writer;
            _kind = kind;
            _clock = clock;
            _diagnostic = diagnostic;
            _timestampClock = new TrackTimestampClock(writer.InputFormat.SampleRate);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
        }

        public IRecordingTrackWriter Writer { get; }
        public WaveFormat WaveFormat => Writer.InputFormat;
        public DateTimeOffset? StartedAtUtc { get; private set; }
        public long BytesCaptured { get; private set; }
        public bool Failed { get; private set; }
        public string? Error { get; private set; }

        public void AttachMixer(RealtimePcmMixer mixer) => _mixer = mixer;

        public void Start()
        {
            _capture.StartRecording();
            StartedAtUtc = DateTimeOffset.UtcNow;
        }

        public async Task StopCaptureAsync(CancellationToken cancellationToken)
        {
            _capture.StopRecording();
            await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        public async Task AbortAsync()
        {
            try
            {
                _capture.StopRecording();
                await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            await Writer.AbortAsync();
            Dispose();
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs args)
        {
            if (args.BytesRecorded <= 0 || Failed)
            {
                return;
            }

            try
            {
                var alignedBytes = args.BytesRecorded -
                                   args.BytesRecorded % WaveFormat.BlockAlign;
                if (alignedBytes <= 0)
                {
                    return;
                }

                var data = new byte[alignedBytes];
                Buffer.BlockCopy(args.Buffer, 0, data, 0, alignedBytes);
                var frameCount = alignedBytes / WaveFormat.BlockAlign;
                var timing = _timestampClock.Next(frameCount, _clock.Elapsed);
                var frame = new PcmAudioFrame(data, timing.Start, timing.Duration);
                if (!Writer.TryWrite(frame))
                {
                    throw new InvalidOperationException(
                        Writer.Error ?? "Audio encoder rejected captured frames.");
                }

                _mixer?.TryAdd(_kind, frame, WaveFormat);
                BytesCaptured += alignedBytes;
                if (!_reportedFirstFrame)
                {
                    _reportedFirstFrame = true;
                    try
                    {
                        _diagnostic?.Invoke(
                            $"First {_kind} audio frame received: " +
                            $"format={WaveFormat.SampleRate}Hz/" +
                            $"{WaveFormat.BitsPerSample}bit/{WaveFormat.Channels}ch.",
                            null);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Failed = true;
                Error = ex.Message;
                try
                {
                    _diagnostic?.Invoke(
                        $"{_kind} capture callback could not queue audio.",
                        ex);
                }
                catch
                {
                }
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs args)
        {
            if (args.Exception is not null)
            {
                Failed = true;
                Error = args.Exception.Message;
            }

            _stopped.TrySetResult();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _device.Dispose();
        }
    }

    private sealed class TrackTimestampClock
    {
        private const long GapToleranceTicks = TimeSpan.TicksPerMillisecond * 100;
        private readonly int _sampleRate;
        private long _nextTime;
        private bool _hasFrame;

        public TrackTimestampClock(int sampleRate)
        {
            _sampleRate = sampleRate;
        }

        public (long Start, long Duration) Next(int frameCount, TimeSpan elapsed)
        {
            var duration = frameCount * TimeSpan.TicksPerSecond / _sampleRate;
            var observedStart = Math.Max(0, elapsed.Ticks - duration);
            var start = !_hasFrame
                ? observedStart
                : observedStart > _nextTime + GapToleranceTicks
                    ? observedStart
                    : _nextTime;
            _hasFrame = true;
            _nextTime = start + duration;
            return (start, duration);
        }
    }
}
