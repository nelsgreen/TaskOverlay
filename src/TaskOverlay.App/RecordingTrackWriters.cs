using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.Wave;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public enum RecordingTrackWriterState
{
    Created,
    Writing,
    Finalizing,
    Completed,
    Failed,
    Aborted
}

public sealed record PcmAudioFrame(
    byte[] Data,
    long SampleTime100Nanoseconds,
    long SampleDuration100Nanoseconds);

public sealed record RecordingTrackWriterStartRequest(
    MeetingRecordingTrackKind Kind,
    string RecordingFolder,
    string BaseFileName,
    int PreferredChannels,
    int PreferredSampleRate = 48_000,
    int PreferredBitrate = 96_000,
    Guid RecordingId = default);

public interface IRecordingTrackWriter : IAsyncDisposable
{
    RecordingTrackWriterState CurrentState { get; }
    WaveFormat InputFormat { get; }
    long BytesWritten { get; }
    TimeSpan Duration { get; }
    string? Error { get; }
    Task StartAsync(
        RecordingTrackWriterStartRequest request,
        CancellationToken cancellationToken = default);
    bool TryWrite(PcmAudioFrame frame);
    ValueTask WriteAsync(
        PcmAudioFrame frame,
        CancellationToken cancellationToken = default);
    Task<MeetingRecordingTrackArtifact> CompleteAsync(
        CancellationToken cancellationToken = default);
    Task AbortAsync(CancellationToken cancellationToken = default);
    MeetingRecordingTrackArtifact SnapshotArtifact();
}

public interface IRecordingTrackWriterFactory
{
    IRecordingTrackWriter Create(MeetingRecordingFormat format);
}

public sealed class RecordingTrackWriterFactory : IRecordingTrackWriterFactory
{
    private readonly Action<string, Exception?>? _diagnostic;

    public RecordingTrackWriterFactory(Action<string, Exception?>? diagnostic = null)
    {
        _diagnostic = diagnostic;
    }

    public IRecordingTrackWriter Create(MeetingRecordingFormat format) => format switch
    {
        MeetingRecordingFormat.AacM4a => new MediaFoundationAacTrackWriter(_diagnostic),
        MeetingRecordingFormat.Wav => new WaveTrackWriter(_diagnostic),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}

public abstract class QueuedRecordingTrackWriter : IRecordingTrackWriter
{
    private const int QueueCapacity = 256;
    private readonly object _sync = new();
    private readonly Action<string, Exception?>? _diagnostic;
    private Channel<PcmAudioFrame>? _channel;
    private Task? _worker;
    private RecordingTrackWriterState _state = RecordingTrackWriterState.Created;
    private string? _error;
    private long _inputBytes;
    private long _maximumEndTime;
    private bool _hasFrames;

    protected QueuedRecordingTrackWriter(Action<string, Exception?>? diagnostic)
    {
        _diagnostic = diagnostic;
    }

    public RecordingTrackWriterState CurrentState
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public WaveFormat InputFormat { get; protected set; } = new(48_000, 16, 1);
    public long BytesWritten { get; protected set; }
    public TimeSpan Duration => TimeSpan.FromTicks(
        Interlocked.Read(ref _maximumEndTime));
    public string? Error
    {
        get
        {
            lock (_sync)
            {
                return _error;
            }
        }
    }

    protected RecordingTrackWriterStartRequest? StartRequest { get; private set; }
    protected string FinalPath { get; set; } = string.Empty;
    protected string InProgressPath { get; set; } = string.Empty;
    protected string Container { get; set; } = string.Empty;
    protected string Codec { get; set; } = string.Empty;
    protected int Bitrate { get; set; }
    protected MeetingRecordingValidationState ValidationState { get; set; } =
        MeetingRecordingValidationState.Unknown;

    public async Task StartAsync(
        RecordingTrackWriterStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_state != RecordingTrackWriterState.Created)
            {
                throw new InvalidOperationException("Recording track writer has already started.");
            }

            StartRequest = request;
        }

        Directory.CreateDirectory(request.RecordingFolder);
        await InitializeCoreAsync(request, cancellationToken);
        _channel = Channel.CreateBounded<PcmAudioFrame>(new BoundedChannelOptions(
            QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        lock (_sync)
        {
            _state = RecordingTrackWriterState.Writing;
        }

        _worker = Task.Run(ProcessQueueAsync, CancellationToken.None);
        Report(
            $"Recording track writer started: kind={request.Kind}; " +
            $"container={Container}; codec={Codec}; sampleRate={InputFormat.SampleRate}; " +
            $"channels={InputFormat.Channels}; bitrate={Bitrate}.");
    }

    public bool TryWrite(PcmAudioFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Data.Length == 0 || frame.SampleDuration100Nanoseconds <= 0)
        {
            return true;
        }

        Channel<PcmAudioFrame>? channel;
        lock (_sync)
        {
            if (_state != RecordingTrackWriterState.Writing)
            {
                return false;
            }

            channel = _channel;
        }

        if (channel?.Writer.TryWrite(frame) == true)
        {
            return true;
        }

        Fail(new InvalidOperationException(
            "The bounded audio encoder queue is full; recording cannot continue safely."));
        return false;
    }

    public async ValueTask WriteAsync(
        PcmAudioFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Data.Length == 0 || frame.SampleDuration100Nanoseconds <= 0)
        {
            return;
        }

        Channel<PcmAudioFrame>? channel;
        lock (_sync)
        {
            if (_state != RecordingTrackWriterState.Writing)
            {
                throw new InvalidOperationException(
                    Error ?? "Recording track writer is not accepting audio frames.");
            }

            channel = _channel;
        }

        try
        {
            await (channel ?? throw new InvalidOperationException(
                    "Recording track queue was not initialized."))
                .Writer.WriteAsync(frame, cancellationToken);
        }
        catch (ChannelClosedException ex)
        {
            throw new InvalidOperationException(
                Error ?? "Recording track writer stopped accepting audio frames.",
                ex);
        }
    }

    public async Task<MeetingRecordingTrackArtifact> CompleteAsync(
        CancellationToken cancellationToken = default)
    {
        Task? worker;
        lock (_sync)
        {
            if (_state == RecordingTrackWriterState.Completed)
            {
                return SnapshotArtifact();
            }

            if (_state is RecordingTrackWriterState.Aborted or
                RecordingTrackWriterState.Created)
            {
                throw new InvalidOperationException("Recording track writer is not active.");
            }

            if (_state == RecordingTrackWriterState.Writing)
            {
                _state = RecordingTrackWriterState.Finalizing;
            }

            _channel?.Writer.TryComplete();
            worker = _worker;
        }

        if (worker is not null)
        {
            await worker.WaitAsync(cancellationToken);
        }

        if (Error is not null)
        {
            await AbortCoreAsync(preserveInProgressFile: true, cancellationToken);
            lock (_sync)
            {
                _state = RecordingTrackWriterState.Failed;
            }

            return SnapshotArtifact();
        }

        if (!_hasFrames)
        {
            await AbortCoreAsync(preserveInProgressFile: false, cancellationToken);
            ValidationState = MeetingRecordingValidationState.Invalid;
            lock (_sync)
            {
                _state = RecordingTrackWriterState.Completed;
            }

            return SnapshotArtifact();
        }

        try
        {
            await CompleteCoreAsync(cancellationToken);
            lock (_sync)
            {
                _state = RecordingTrackWriterState.Completed;
            }
        }
        catch (Exception ex) when (ex is IOException or
                                   UnauthorizedAccessException or
                                   InvalidDataException or
                                   InvalidOperationException or
                                   System.Runtime.InteropServices.COMException)
        {
            Fail(ex);
            await AbortCoreAsync(preserveInProgressFile: true, CancellationToken.None);
        }

        return SnapshotArtifact();
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        Task? worker;
        lock (_sync)
        {
            if (_state is RecordingTrackWriterState.Aborted or
                RecordingTrackWriterState.Completed)
            {
                return;
            }

            _channel?.Writer.TryComplete();
            worker = _worker;
        }

        if (worker is not null)
        {
            try
            {
                await worker.WaitAsync(cancellationToken);
            }
            catch
            {
                // Abort continues so native handles cannot remain active.
            }
        }

        await AbortCoreAsync(preserveInProgressFile: true, CancellationToken.None);
        lock (_sync)
        {
            _state = RecordingTrackWriterState.Aborted;
        }
    }

    public MeetingRecordingTrackArtifact SnapshotArtifact()
    {
        var request = StartRequest;
        var state = CurrentState;
        var finalFile = state == RecordingTrackWriterState.Completed &&
                        ValidationState == MeetingRecordingValidationState.Valid
            ? Path.GetFileName(FinalPath)
            : string.Empty;
        var inProgressFile = File.Exists(InProgressPath)
            ? Path.GetFileName(InProgressPath)
            : string.Empty;
        return new MeetingRecordingTrackArtifact
        {
            Kind = request?.Kind ?? MeetingRecordingTrackKind.Mixed,
            FileName = finalFile,
            InProgressFileName = inProgressFile,
            Container = Container,
            Codec = Codec,
            SampleRate = InputFormat.SampleRate,
            ChannelCount = InputFormat.Channels,
            Bitrate = Bitrate,
            DurationSeconds = Duration.TotalSeconds,
            Bytes = BytesWritten,
            HasAudioFrames = _hasFrames,
            FinalizationState = state switch
            {
                RecordingTrackWriterState.Created => MeetingRecordingFinalizationState.Pending,
                RecordingTrackWriterState.Writing or RecordingTrackWriterState.Finalizing =>
                    MeetingRecordingFinalizationState.InProgress,
                RecordingTrackWriterState.Completed => MeetingRecordingFinalizationState.Finalized,
                RecordingTrackWriterState.Aborted => MeetingRecordingFinalizationState.Interrupted,
                _ => MeetingRecordingFinalizationState.Failed
            },
            ValidationState = ValidationState,
            Error = Error ?? (!_hasFrames ? "No audio frames were captured." : string.Empty)
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (CurrentState is not (RecordingTrackWriterState.Completed or
            RecordingTrackWriterState.Aborted))
        {
            await AbortAsync();
        }
    }

    protected abstract Task InitializeCoreAsync(
        RecordingTrackWriterStartRequest request,
        CancellationToken cancellationToken);

    protected abstract void WriteFrameCore(PcmAudioFrame frame);

    protected abstract Task CompleteCoreAsync(CancellationToken cancellationToken);

    protected abstract Task AbortCoreAsync(
        bool preserveInProgressFile,
        CancellationToken cancellationToken);

    protected void SetOutputMetrics(long bytesWritten, TimeSpan duration)
    {
        BytesWritten = Math.Max(0, bytesWritten);
        Interlocked.Exchange(ref _maximumEndTime, Math.Max(0, duration.Ticks));
    }

    protected void Report(string message, Exception? exception = null)
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

    private async Task ProcessQueueAsync()
    {
        try
        {
            var channel = _channel ?? throw new InvalidOperationException(
                "Recording track queue was not initialized.");
            await foreach (var frame in channel.Reader.ReadAllAsync())
            {
                if (Error is not null)
                {
                    continue;
                }

                WriteFrameCore(frame);
                _hasFrames = true;
                Interlocked.Add(ref _inputBytes, frame.Data.LongLength);
                var end = frame.SampleTime100Nanoseconds +
                          frame.SampleDuration100Nanoseconds;
                Interlocked.Exchange(
                    ref _maximumEndTime,
                    Math.Max(Interlocked.Read(ref _maximumEndTime), end));
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Fail(Exception exception)
    {
        lock (_sync)
        {
            _error ??= exception.Message;
            _state = RecordingTrackWriterState.Failed;
            _channel?.Writer.TryComplete(exception);
        }

        Report("Recording track writer failed.", exception);
    }
}

public sealed class WaveTrackWriter : QueuedRecordingTrackWriter
{
    private WaveFileWriter? _writer;
    private long _nextSampleTime;

    public WaveTrackWriter(Action<string, Exception?>? diagnostic = null)
        : base(diagnostic)
    {
    }

    protected override Task InitializeCoreAsync(
        RecordingTrackWriterStartRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InputFormat = new WaveFormat(
            request.PreferredSampleRate,
            16,
            request.PreferredChannels);
        Container = "WAV";
        Codec = "PCM 16-bit";
        Bitrate = InputFormat.AverageBytesPerSecond * 8;
        FinalPath = Path.Combine(request.RecordingFolder, $"{request.BaseFileName}.wav");
        InProgressPath = Path.Combine(
            request.RecordingFolder,
            $"{request.BaseFileName}.current.wav");
        File.Delete(InProgressPath);
        _writer = new WaveFileWriter(InProgressPath, InputFormat);
        return Task.CompletedTask;
    }

    protected override void WriteFrameCore(PcmAudioFrame frame)
    {
        var writer = _writer ?? throw new InvalidOperationException(
            "WAV writer is not initialized.");
        if (frame.SampleTime100Nanoseconds < _nextSampleTime)
        {
            throw new InvalidDataException(
                "Audio frames arrived out of order; WAV output was not finalized.");
        }

        var gapDuration = frame.SampleTime100Nanoseconds - _nextSampleTime;
        if (gapDuration > 0)
        {
            var gapBytes = gapDuration * InputFormat.AverageBytesPerSecond / 10_000_000L;
            gapBytes -= gapBytes % InputFormat.BlockAlign;
            WriteSilence(writer, gapBytes);
        }

        writer.Write(frame.Data, 0, frame.Data.Length);
        _nextSampleTime = frame.SampleTime100Nanoseconds +
                          frame.SampleDuration100Nanoseconds;
    }

    protected override Task CompleteCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
        using (var reader = new WaveFileReader(InProgressPath))
        {
            if (reader.Length <= 0 || reader.TotalTime <= TimeSpan.Zero)
            {
                throw new InvalidDataException("Finalized WAV track contains no playable audio.");
            }

            SetOutputMetrics(new FileInfo(InProgressPath).Length, reader.TotalTime);
        }

        File.Move(InProgressPath, FinalPath, overwrite: true);
        ValidationState = MeetingRecordingValidationState.Valid;
        Report($"WAV track finalized: file={Path.GetFileName(FinalPath)}; bytes={BytesWritten}.");
        return Task.CompletedTask;
    }

    protected override Task AbortCoreAsync(
        bool preserveInProgressFile,
        CancellationToken cancellationToken)
    {
        _writer?.Dispose();
        _writer = null;
        if (File.Exists(InProgressPath))
        {
            SetOutputMetrics(new FileInfo(InProgressPath).Length, Duration);
        }

        if (!preserveInProgressFile && File.Exists(InProgressPath))
        {
            File.Delete(InProgressPath);
        }

        ValidationState = MeetingRecordingValidationState.Invalid;
        return Task.CompletedTask;
    }

    private static void WriteSilence(WaveFileWriter writer, long byteCount)
    {
        var silence = new byte[16 * 1024];
        while (byteCount > 0)
        {
            var count = (int)Math.Min(silence.Length, byteCount);
            writer.Write(silence, 0, count);
            byteCount -= count;
        }
    }
}
