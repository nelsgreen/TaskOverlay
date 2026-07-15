using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed class WindowsMeetingRecorder : IMeetingRecorder
{
    private readonly object _sync = new();
    private ActiveSession? _active;
    private MeetingRecorderRuntimeStatus _status = new(
        null,
        null,
        AudioTrackHealth.Unknown,
        AudioTrackHealth.Unknown,
        null);

    public MeetingRecorderRuntimeStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices() =>
        EnumerateDevices(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceDescriptor> GetSystemOutputDevices() =>
        EnumerateDevices(DataFlow.Render);

    public Task<MeetingRecordingStartResult> StartAsync(
        MeetingRecordingStartRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            if (_active is not null)
            {
                throw new InvalidOperationException("Another meeting recording is already active.");
            }
        }

        Directory.CreateDirectory(request.RecordingFolder);
        var startedAt = DateTimeOffset.UtcNow;
        TrackCapture? system = null;
        TrackCapture? microphone = null;
        var warnings = new List<string>();
        try
        {
            system = TryCreateTrack(
                DataFlow.Render,
                request.SystemOutputDeviceId,
                Path.Combine(request.RecordingFolder, "system.wav"),
                loopback: true,
                warnings);
            microphone = TryCreateTrack(
                DataFlow.Capture,
                request.MicrophoneDeviceId,
                Path.Combine(request.RecordingFolder, "microphone.wav"),
                loopback: false,
                warnings);
            if (system is null && microphone is null)
            {
                throw new InvalidOperationException(
                    "Neither system audio nor microphone capture could be started.");
            }

            system?.Start();
            microphone?.Start();
            var active = new ActiveSession(request.RecordingId, startedAt, system, microphone);
            lock (_sync)
            {
                if (_active is not null)
                {
                    throw new InvalidOperationException(
                        "Another meeting recording became active while capture was starting.");
                }

                _active = active;
                _status = new MeetingRecorderRuntimeStatus(
                    request.RecordingId,
                    startedAt,
                    system is null ? AudioTrackHealth.Unavailable : AudioTrackHealth.Healthy,
                    microphone is null ? AudioTrackHealth.Unavailable : AudioTrackHealth.Healthy,
                    warnings.Count == 0 ? null : string.Join(" ", warnings));
            }

            return Task.FromResult(new MeetingRecordingStartResult(
                startedAt,
                system?.StartedAtUtc,
                microphone?.StartedAtUtc,
                system is null ? string.Empty : "system.wav",
                microphone is null ? string.Empty : "microphone.wav",
                system is null ? AudioTrackHealth.Unavailable : AudioTrackHealth.Healthy,
                microphone is null ? AudioTrackHealth.Unavailable : AudioTrackHealth.Healthy,
                warnings.Count == 0 ? null : string.Join(" ", warnings)));
        }
        catch
        {
            system?.Dispose();
            microphone?.Dispose();
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
        }

        var warnings = new List<string>();
        var systemHealth = await StopTrackAsync(active.System, "System audio", warnings, cancellationToken);
        var microphoneHealth = await StopTrackAsync(
            active.Microphone,
            "Microphone",
            warnings,
            cancellationToken);
        var stoppedAt = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            _active = null;
            _status = new MeetingRecorderRuntimeStatus(
                null,
                null,
                systemHealth,
                microphoneHealth,
                warnings.Count == 0 ? null : string.Join(" ", warnings));
        }

        return new MeetingRecordingStopResult(
            stoppedAt,
            systemHealth,
            microphoneHealth,
            warnings.Count == 0 ? null : string.Join(" ", warnings));
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
            catch
            {
                active.System?.Dispose();
                active.Microphone?.Dispose();
                lock (_sync)
                {
                    _active = null;
                }
            }
        }
    }

    private static TrackCapture? TryCreateTrack(
        DataFlow flow,
        string? requestedDeviceId,
        string path,
        bool loopback,
        ICollection<string> warnings)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice device;
            if (!string.IsNullOrWhiteSpace(requestedDeviceId))
            {
                device = enumerator.GetDevice(requestedDeviceId.Trim());
            }
            else
            {
                device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            }

            IWaveIn capture = loopback
                ? new WasapiLoopbackCapture(device)
                : new WasapiCapture(device);
            return new TrackCapture(capture, device, path);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            ArgumentException or
            UnauthorizedAccessException or
            IOException)
        {
            warnings.Add($"{(loopback ? "System audio" : "Microphone")} unavailable ({ex.GetType().Name}).");
            return null;
        }
    }

    private static async Task<AudioTrackHealth> StopTrackAsync(
        TrackCapture? track,
        string label,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        if (track is null)
        {
            return AudioTrackHealth.Unavailable;
        }

        try
        {
            await track.StopAsync(cancellationToken);
            if (track.BytesWritten <= 0)
            {
                warnings.Add($"{label} track contains no audio data.");
                return AudioTrackHealth.Unavailable;
            }

            return track.Failed ? AudioTrackHealth.Failed : AudioTrackHealth.Healthy;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            IOException or
            OperationCanceledException)
        {
            warnings.Add($"{label} did not finalize cleanly ({ex.GetType().Name}).");
            return AudioTrackHealth.Failed;
        }
        finally
        {
            track.Dispose();
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

    private sealed record ActiveSession(
        Guid RecordingId,
        DateTimeOffset StartedAtUtc,
        TrackCapture? System,
        TrackCapture? Microphone);

    private sealed class TrackCapture : IDisposable
    {
        private readonly IWaveIn _capture;
        private readonly MMDevice _device;
        private readonly WaveFileWriter _writer;
        private readonly TaskCompletionSource _stopped = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        public TrackCapture(IWaveIn capture, MMDevice device, string path)
        {
            _capture = capture;
            _device = device;
            _writer = new WaveFileWriter(path, capture.WaveFormat);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
        }

        public DateTimeOffset? StartedAtUtc { get; private set; }
        public long BytesWritten { get; private set; }
        public bool Failed { get; private set; }

        public void Start()
        {
            _capture.StartRecording();
            StartedAtUtc = DateTimeOffset.UtcNow;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _capture.StopRecording();
            await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            _writer.Flush();
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs args)
        {
            try
            {
                _writer.Write(args.Buffer, 0, args.BytesRecorded);
                BytesWritten += args.BytesRecorded;
            }
            catch
            {
                Failed = true;
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs args)
        {
            if (args.Exception is not null)
            {
                Failed = true;
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
            _writer.Dispose();
            _capture.Dispose();
            _device.Dispose();
        }
    }
}
