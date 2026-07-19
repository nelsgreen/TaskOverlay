using System;
using NAudio.Wave;

namespace TaskOverlay.App;

public enum MeetingNativeAudioPlaybackState
{
    Stopped,
    Playing,
    Paused,
    Failed
}

public sealed record MeetingNativeAudioPlaybackSnapshot(
    Guid? RecordingId,
    Guid? TranscriptId,
    MeetingNativeAudioPlaybackState State,
    double PositionSeconds,
    double DurationSeconds,
    string? FailureReason = null);

public interface IMeetingNativeAudioBackend : IDisposable
{
    event EventHandler? PlaybackEnded;
    event EventHandler? PlaybackFailed;
    double PositionSeconds { get; set; }
    double DurationSeconds { get; }
    void Play();
    void Pause();
    void Stop();
}

public sealed class MeetingNativeAudioPlaybackService : IDisposable
{
    private readonly Func<string, IMeetingNativeAudioBackend> _backendFactory;
    private readonly object _sync = new();
    private IMeetingNativeAudioBackend? _backend;
    private Guid? _recordingId;
    private Guid? _transcriptId;
    private MeetingNativeAudioPlaybackState _state = MeetingNativeAudioPlaybackState.Stopped;
    private string? _failureReason;

    public MeetingNativeAudioPlaybackService(
        Func<string, IMeetingNativeAudioBackend>? backendFactory = null)
    {
        _backendFactory = backendFactory ?? (path => new NAudioMeetingAudioBackend(path));
    }

    public bool Play(
        MeetingAudioResource resource,
        Guid transcriptId,
        double positionSeconds)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_sync)
        {
            try
            {
                if (_backend is null || _recordingId != resource.RecordingId ||
                    _transcriptId != transcriptId)
                {
                    DisposeBackend();
                    _backend = _backendFactory(resource.FilePath);
                    _backend.PlaybackEnded += Backend_OnPlaybackEnded;
                    _backend.PlaybackFailed += Backend_OnPlaybackFailed;
                    _recordingId = resource.RecordingId;
                    _transcriptId = transcriptId;
                }

                _backend.PositionSeconds = ClampPosition(positionSeconds, _backend.DurationSeconds);
                _backend.Play();
                _state = MeetingNativeAudioPlaybackState.Playing;
                _failureReason = null;
                return true;
            }
            catch
            {
                FailAndDispose();
                return false;
            }
        }
    }

    public bool Pause(Guid recordingId, Guid transcriptId)
    {
        lock (_sync)
        {
            if (!Owns(recordingId, transcriptId) || _backend is null)
            {
                return false;
            }

            try
            {
                _backend.Pause();
                _state = MeetingNativeAudioPlaybackState.Paused;
                return true;
            }
            catch
            {
                FailAndDispose();
                return false;
            }
        }
    }

    public bool Seek(Guid recordingId, Guid transcriptId, double positionSeconds)
    {
        lock (_sync)
        {
            if (!Owns(recordingId, transcriptId) || _backend is null)
            {
                return false;
            }

            try
            {
                _backend.PositionSeconds = ClampPosition(positionSeconds, _backend.DurationSeconds);
                return true;
            }
            catch
            {
                FailAndDispose();
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            try
            {
                _backend?.Stop();
            }
            catch
            {
            }

            DisposeBackend();
            _recordingId = null;
            _transcriptId = null;
            _state = MeetingNativeAudioPlaybackState.Stopped;
            _failureReason = null;
        }
    }

    public MeetingNativeAudioPlaybackSnapshot Snapshot()
    {
        lock (_sync)
        {
            var position = 0d;
            var duration = 0d;
            if (_backend is not null)
            {
                try
                {
                    position = Math.Max(0, _backend.PositionSeconds);
                    duration = Math.Max(0, _backend.DurationSeconds);
                }
                catch
                {
                    FailAndDispose();
                }
            }

            return new MeetingNativeAudioPlaybackSnapshot(
                _recordingId,
                _transcriptId,
                _state,
                position,
                duration,
                _failureReason);
        }
    }

    public void Dispose() => Stop();

    private bool Owns(Guid recordingId, Guid transcriptId) =>
        _recordingId == recordingId && _transcriptId == transcriptId;

    private void Backend_OnPlaybackEnded(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            if (ReferenceEquals(sender, _backend))
            {
                _state = MeetingNativeAudioPlaybackState.Stopped;
            }
        }
    }

    private void Backend_OnPlaybackFailed(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            if (ReferenceEquals(sender, _backend))
            {
                FailAndDispose();
            }
        }
    }

    private void FailAndDispose()
    {
        DisposeBackend();
        _state = MeetingNativeAudioPlaybackState.Failed;
        _failureReason = "Native playback unavailable";
    }

    private void DisposeBackend()
    {
        if (_backend is null)
        {
            return;
        }

        _backend.PlaybackEnded -= Backend_OnPlaybackEnded;
        _backend.PlaybackFailed -= Backend_OnPlaybackFailed;
        _backend.Dispose();
        _backend = null;
    }

    private static double ClampPosition(double seconds, double duration) =>
        double.IsFinite(seconds)
            ? Math.Clamp(seconds, 0, Math.Max(0, duration))
            : 0;
}

internal sealed class NAudioMeetingAudioBackend : IMeetingNativeAudioBackend
{
    private readonly MediaFoundationReader _reader;
    private readonly WaveOutEvent _output;

    public NAudioMeetingAudioBackend(string path)
    {
        _reader = new MediaFoundationReader(path);
        _output = new WaveOutEvent();
        _output.Init(_reader);
        _output.PlaybackStopped += Output_OnPlaybackStopped;
    }

    public event EventHandler? PlaybackEnded;
    public event EventHandler? PlaybackFailed;

    public double PositionSeconds
    {
        get => _reader.CurrentTime.TotalSeconds;
        set => _reader.CurrentTime = TimeSpan.FromSeconds(
            Math.Clamp(value, 0, DurationSeconds));
    }

    public double DurationSeconds => _reader.TotalTime.TotalSeconds;

    public void Play() => _output.Play();
    public void Pause() => _output.Pause();
    public void Stop() => _output.Stop();

    public void Dispose()
    {
        _output.PlaybackStopped -= Output_OnPlaybackStopped;
        _output.Dispose();
        _reader.Dispose();
    }

    private void Output_OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            PlaybackFailed?.Invoke(this, EventArgs.Empty);
        }
        else if (_reader.Position >= _reader.Length)
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }
}
