using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.MediaFoundation;
using NAudio.Wave;
using TaskOverlay.Core;

namespace TaskOverlay.App;

internal sealed record AacEncoderSessionCompletion(long Bytes, TimeSpan Duration);

internal interface IMediaFoundationAacEncoderSession : IDisposable
{
    WaveFormat InputFormat { get; }
    int Bitrate { get; }
    void WriteFrame(PcmAudioFrame frame);
    AacEncoderSessionCompletion Complete();
    void Abort(bool preserveInProgressFile);
}

internal interface IMediaFoundationAacEncoderSessionFactory
{
    IMediaFoundationAacEncoderSession Create(
        RecordingTrackWriterStartRequest request,
        string finalPath,
        string inProgressPath);
}

public sealed class MediaFoundationAacTrackWriter : IRecordingTrackWriter
{
    private const int QueueCapacity = 256;
    private const uint CoinitMultithreaded = 0;
    private const int SFalse = 1;

    private readonly object _sync = new();
    private readonly Action<string, Exception?>? _diagnostic;
    private readonly IMediaFoundationAacEncoderSessionFactory _sessionFactory;
    private Channel<EncoderCommand>? _commands;
    private TaskCompletionSource _ownerExited = NewCompletionSource();
    private Thread? _ownerThread;
    private RecordingTrackWriterStartRequest? _startRequest;
    private RecordingTrackWriterState _state = RecordingTrackWriterState.Created;
    private WaveFormat _inputFormat = new(48_000, 16, 1);
    private Task<MeetingRecordingTrackArtifact>? _completionTask;
    private Task? _abortTask;
    private string _finalPath = string.Empty;
    private string _inProgressPath = string.Empty;
    private string? _error;
    private long _durationTicks;
    private long _bytesWritten;
    private int _bitrate;
    private bool _hasFrames;
    private volatile bool _emergencyAbortRequested;

    public MediaFoundationAacTrackWriter(Action<string, Exception?>? diagnostic = null)
        : this(new MediaFoundationAacEncoderSessionFactory(diagnostic), diagnostic)
    {
    }

    internal MediaFoundationAacTrackWriter(
        IMediaFoundationAacEncoderSessionFactory sessionFactory,
        Action<string, Exception?>? diagnostic = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
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

    public WaveFormat InputFormat
    {
        get
        {
            lock (_sync)
            {
                return _inputFormat;
            }
        }
    }

    public long BytesWritten => Interlocked.Read(ref _bytesWritten);
    public TimeSpan Duration => TimeSpan.FromTicks(Interlocked.Read(ref _durationTicks));

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

    internal int? OwnerThreadId { get; private set; }
    internal ApartmentState? OwnerApartmentState { get; private set; }

    public async Task StartAsync(
        RecordingTrackWriterStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var initialized = NewCompletionSource();
        Channel<EncoderCommand> commands;
        lock (_sync)
        {
            if (_state != RecordingTrackWriterState.Created || _ownerThread is not null)
            {
                throw new InvalidOperationException("Recording track writer has already started.");
            }

            _startRequest = request;
            _finalPath = Path.Combine(request.RecordingFolder, $"{request.BaseFileName}.m4a");
            _inProgressPath = Path.Combine(
                request.RecordingFolder,
                $"{request.BaseFileName}.current.m4a");
            Directory.CreateDirectory(request.RecordingFolder);
            commands = Channel.CreateBounded<EncoderCommand>(new BoundedChannelOptions(
                QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            _commands = commands;
            _ownerExited = NewCompletionSource();
            _ownerThread = new Thread(() => RunOwnerThread(commands))
            {
                IsBackground = true,
                Name = $"TaskOverlay AAC {request.Kind} encoder"
            };
            _ownerThread.SetApartmentState(ApartmentState.MTA);
            _ownerThread.Start();
        }

        if (!commands.Writer.TryWrite(new InitializeCommand(request, initialized)))
        {
            var exception = new InvalidOperationException(
                "The AAC encoder owner thread did not accept initialization.");
            initialized.TrySetException(exception);
            RequestEmergencyAbort(exception);
        }

        try
        {
            await initialized.Task.WaitAsync(cancellationToken);
        }
        catch
        {
            await AbortAsync(CancellationToken.None);
            throw;
        }
    }

    public bool TryWrite(PcmAudioFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Data.Length == 0 || frame.SampleDuration100Nanoseconds <= 0)
        {
            return true;
        }

        Channel<EncoderCommand>? commands;
        lock (_sync)
        {
            if (_state != RecordingTrackWriterState.Writing)
            {
                return false;
            }

            commands = _commands;
        }

        if (commands?.Writer.TryWrite(new FrameCommand(frame)) == true)
        {
            return true;
        }

        RequestEmergencyAbort(new InvalidOperationException(
            "The bounded AAC encoder queue is full; recording cannot continue safely."));
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

        Channel<EncoderCommand>? commands;
        lock (_sync)
        {
            if (_state != RecordingTrackWriterState.Writing)
            {
                throw new InvalidOperationException(
                    _error ?? "Recording track writer is not accepting audio frames.");
            }

            commands = _commands;
        }

        try
        {
            await (commands ?? throw new InvalidOperationException(
                    "AAC encoder command queue was not initialized."))
                .Writer.WriteAsync(new FrameCommand(frame), cancellationToken);
        }
        catch (ChannelClosedException ex)
        {
            throw new InvalidOperationException(
                Error ?? "AAC encoder stopped accepting audio frames.",
                ex);
        }
    }

    public async Task<MeetingRecordingTrackArtifact> CompleteAsync(
        CancellationToken cancellationToken = default)
    {
        Task<MeetingRecordingTrackArtifact> completion;
        lock (_sync)
        {
            if (_state == RecordingTrackWriterState.Completed)
            {
                return SnapshotArtifact();
            }

            if (_state is RecordingTrackWriterState.Created or RecordingTrackWriterState.Aborted)
            {
                throw new InvalidOperationException("Recording track writer is not active.");
            }

            _completionTask ??= CompleteLifecycleAsync();
            completion = _completionTask;
        }

        return await completion.WaitAsync(cancellationToken);
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        Task abort;
        lock (_sync)
        {
            if (_state is RecordingTrackWriterState.Completed or RecordingTrackWriterState.Aborted)
            {
                return;
            }

            if (_ownerThread is null)
            {
                _state = RecordingTrackWriterState.Aborted;
                return;
            }

            _abortTask ??= AbortLifecycleAsync();
            abort = _abortTask;
        }

        await abort.WaitAsync(cancellationToken);
    }

    public MeetingRecordingTrackArtifact SnapshotArtifact()
    {
        RecordingTrackWriterState state;
        RecordingTrackWriterStartRequest? request;
        WaveFormat inputFormat;
        string? error;
        int bitrate;
        bool hasFrames;
        lock (_sync)
        {
            state = _state;
            request = _startRequest;
            inputFormat = _inputFormat;
            error = _error;
            bitrate = _bitrate;
            hasFrames = _hasFrames;
        }

        var valid = state == RecordingTrackWriterState.Completed &&
                    hasFrames &&
                    File.Exists(_finalPath);
        return new MeetingRecordingTrackArtifact
        {
            Kind = request?.Kind ?? MeetingRecordingTrackKind.Mixed,
            FileName = valid ? Path.GetFileName(_finalPath) : string.Empty,
            InProgressFileName = File.Exists(_inProgressPath)
                ? Path.GetFileName(_inProgressPath)
                : string.Empty,
            Container = "MPEG-4/M4A",
            Codec = "AAC-LC",
            SampleRate = inputFormat.SampleRate,
            ChannelCount = inputFormat.Channels,
            Bitrate = bitrate,
            DurationSeconds = Duration.TotalSeconds,
            Bytes = BytesWritten,
            HasAudioFrames = hasFrames,
            FinalizationState = state switch
            {
                RecordingTrackWriterState.Created => MeetingRecordingFinalizationState.Pending,
                RecordingTrackWriterState.Writing or RecordingTrackWriterState.Finalizing =>
                    MeetingRecordingFinalizationState.InProgress,
                RecordingTrackWriterState.Completed => MeetingRecordingFinalizationState.Finalized,
                RecordingTrackWriterState.Aborted => MeetingRecordingFinalizationState.Interrupted,
                _ => MeetingRecordingFinalizationState.Failed
            },
            ValidationState = valid
                ? MeetingRecordingValidationState.Valid
                : state is RecordingTrackWriterState.Failed or
                    RecordingTrackWriterState.Aborted or
                    RecordingTrackWriterState.Completed
                    ? MeetingRecordingValidationState.Invalid
                    : MeetingRecordingValidationState.Unknown,
            Error = error ?? (!hasFrames && state == RecordingTrackWriterState.Completed
                ? "No audio frames were captured."
                : string.Empty)
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (CurrentState is not (RecordingTrackWriterState.Completed or
            RecordingTrackWriterState.Aborted))
        {
            await AbortAsync(CancellationToken.None);
        }
    }

    private async Task<MeetingRecordingTrackArtifact> CompleteLifecycleAsync()
    {
        await Task.Yield();
        Channel<EncoderCommand>? commands;
        lock (_sync)
        {
            if (_state == RecordingTrackWriterState.Failed)
            {
                commands = null;
            }
            else
            {
                _state = RecordingTrackWriterState.Finalizing;
                commands = _commands;
            }
        }

        if (commands is not null)
        {
            var finalized = NewCompletionSource();
            try
            {
                await commands.Writer.WriteAsync(new FinalizeCommand(finalized));
                await finalized.Task;
            }
            catch (Exception ex)
            {
                if (CurrentState != RecordingTrackWriterState.Failed)
                {
                    RecordFailure(ex, "Finalize command failed before completion.");
                }
            }
        }

        await _ownerExited.Task;
        return SnapshotArtifact();
    }

    private async Task AbortLifecycleAsync()
    {
        await Task.Yield();
        Channel<EncoderCommand>? commands;
        lock (_sync)
        {
            commands = _commands;
        }

        if (commands is not null && CurrentState != RecordingTrackWriterState.Failed)
        {
            var aborted = NewCompletionSource();
            try
            {
                await commands.Writer.WriteAsync(new AbortCommand(aborted));
                await aborted.Task;
            }
            catch (Exception ex)
            {
                RequestEmergencyAbort(ex);
            }
        }

        await _ownerExited.Task;
    }

    private void RunOwnerThread(Channel<EncoderCommand> commands)
    {
        OwnerThreadId = Environment.CurrentManagedThreadId;
        OwnerApartmentState = Thread.CurrentThread.GetApartmentState();
        IMediaFoundationAacEncoderSession? session = null;
        var sessionTerminal = false;
        var coInitializeResult = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
        var shouldUninitialize = coInitializeResult >= 0;
        EncoderCommand? currentCommand = null;
        try
        {
            if (coInitializeResult < 0)
            {
                Marshal.ThrowExceptionForHR(coInitializeResult);
            }

            ReportOwnerOperation(
                "COM initialized",
                $"coInitialize=0x{coInitializeResult:X8}; alreadyInitialized={coInitializeResult == SFalse}");
            while (!sessionTerminal)
            {
                if (_emergencyAbortRequested)
                {
                    break;
                }

                try
                {
                    currentCommand = commands.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (ChannelClosedException)
                {
                    break;
                }

                try
                {
                    switch (currentCommand)
                    {
                        case InitializeCommand initialize:
                            session = _sessionFactory.Create(
                                initialize.Request,
                                _finalPath,
                                _inProgressPath);
                            lock (_sync)
                            {
                                _inputFormat = session.InputFormat;
                                _bitrate = session.Bitrate;
                                _state = RecordingTrackWriterState.Writing;
                            }

                            ReportOwnerOperation(
                                "Sink Writer initialized",
                                $"sampleRate={session.InputFormat.SampleRate}; " +
                                $"channels={session.InputFormat.Channels}; bitrate={session.Bitrate}");
                            initialize.Completion.TrySetResult();
                            break;

                        case FrameCommand frame:
                            if (session is null)
                            {
                                throw new InvalidOperationException(
                                    "AAC encoder session was not initialized.");
                            }

                            session.WriteFrame(frame.Frame);
                            lock (_sync)
                            {
                                _hasFrames = true;
                            }

                            var end = frame.Frame.SampleTime100Nanoseconds +
                                      frame.Frame.SampleDuration100Nanoseconds;
                            Interlocked.Exchange(
                                ref _durationTicks,
                                Math.Max(Interlocked.Read(ref _durationTicks), end));
                            break;

                        case FinalizeCommand finalize:
                            if (session is null)
                            {
                                throw new InvalidOperationException(
                                    "AAC encoder session was not initialized.");
                            }

                            ReportOwnerOperation("Finalization started");
                            if (!_hasFrames)
                            {
                                session.Abort(preserveInProgressFile: false);
                                lock (_sync)
                                {
                                    _state = RecordingTrackWriterState.Completed;
                                }
                            }
                            else
                            {
                                var result = session.Complete();
                                Interlocked.Exchange(ref _bytesWritten, result.Bytes);
                                Interlocked.Exchange(ref _durationTicks, result.Duration.Ticks);
                                lock (_sync)
                                {
                                    _state = RecordingTrackWriterState.Completed;
                                }
                            }

                            sessionTerminal = true;
                            ReportOwnerOperation("Finalization completed");
                            finalize.Completion.TrySetResult();
                            break;

                        case AbortCommand abort:
                            session?.Abort(preserveInProgressFile: true);
                            lock (_sync)
                            {
                                if (_state != RecordingTrackWriterState.Failed)
                                {
                                    _state = RecordingTrackWriterState.Aborted;
                                }
                            }

                            sessionTerminal = true;
                            ReportOwnerOperation("Abort completed");
                            abort.Completion.TrySetResult();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    currentCommand.Fail(ex);
                    RecordFailure(ex, $"AAC owner operation {currentCommand.Operation} failed.");
                    break;
                }
                finally
                {
                    currentCommand = null;
                }
            }
        }
        catch (Exception ex)
        {
            currentCommand?.Fail(ex);
            RecordFailure(ex, "AAC encoder owner thread failed.");
        }
        finally
        {
            try
            {
                if (session is not null && !sessionTerminal)
                {
                    session.Abort(preserveInProgressFile: true);
                }
            }
            catch (Exception ex)
            {
                RecordFailure(ex, "AAC encoder cleanup failed.");
            }

            try
            {
                session?.Dispose();
                ReportOwnerOperation("COM objects released");
            }
            catch (Exception ex)
            {
                RecordFailure(ex, "AAC COM release failed.");
            }

            commands.Writer.TryComplete();
            FailPendingCommands(commands, new InvalidOperationException(
                Error ?? "AAC encoder owner thread stopped."));
            if (shouldUninitialize)
            {
                CoUninitialize();
            }

            ReportOwnerOperation("COM uninitialized");
            _ownerExited.TrySetResult();
        }
    }

    private void RequestEmergencyAbort(Exception exception)
    {
        _emergencyAbortRequested = true;
        RecordFailure(exception, "AAC encoder stopped safely.");
        _commands?.Writer.TryComplete();
    }

    private void RecordFailure(Exception exception, string message)
    {
        lock (_sync)
        {
            _error ??= exception.Message;
            _state = RecordingTrackWriterState.Failed;
        }

        try
        {
            _diagnostic?.Invoke(
                $"{message} {OwnerDescription()}",
                exception);
        }
        catch
        {
            // Diagnostics never affect recording behavior.
        }
    }

    private void ReportOwnerOperation(string operation, string? details = null)
    {
        try
        {
            _diagnostic?.Invoke(
                $"Media Foundation AAC {operation}: {OwnerDescription()}" +
                (string.IsNullOrWhiteSpace(details) ? "." : $"; {details}."),
                null);
        }
        catch
        {
            // Diagnostics never affect recording behavior.
        }
    }

    private string OwnerDescription()
    {
        var request = _startRequest;
        return $"recordingId={FormatId(request?.RecordingId)}; " +
               $"track={request?.Kind}; threadId={OwnerThreadId}; " +
               $"apartment={OwnerApartmentState}";
    }

    private static string FormatId(Guid? id) => id is Guid value && value != Guid.Empty
        ? value.ToString("N")
        : "none";

    private static void FailPendingCommands(
        Channel<EncoderCommand> commands,
        Exception exception)
    {
        while (commands.Reader.TryRead(out var pending))
        {
            pending.Fail(exception);
        }
    }

    private static TaskCompletionSource NewCompletionSource() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private abstract record EncoderCommand(string Operation)
    {
        public virtual void Fail(Exception exception)
        {
        }
    }

    private sealed record InitializeCommand(
        RecordingTrackWriterStartRequest Request,
        TaskCompletionSource Completion) : EncoderCommand("Initialize")
    {
        public override void Fail(Exception exception) => Completion.TrySetException(exception);
    }

    private sealed record FrameCommand(PcmAudioFrame Frame) : EncoderCommand("WriteSample");

    private sealed record FinalizeCommand(
        TaskCompletionSource Completion) : EncoderCommand("Finalize")
    {
        public override void Fail(Exception exception) => Completion.TrySetException(exception);
    }

    private sealed record AbortCommand(
        TaskCompletionSource Completion) : EncoderCommand("Abort")
    {
        public override void Fail(Exception exception) => Completion.TrySetException(exception);
    }
}

internal sealed class MediaFoundationAacEncoderSessionFactory :
    IMediaFoundationAacEncoderSessionFactory
{
    private readonly Action<string, Exception?>? _diagnostic;

    public MediaFoundationAacEncoderSessionFactory(Action<string, Exception?>? diagnostic)
    {
        _diagnostic = diagnostic;
    }

    public IMediaFoundationAacEncoderSession Create(
        RecordingTrackWriterStartRequest request,
        string finalPath,
        string inProgressPath) =>
        new MediaFoundationAacEncoderSession(
            request,
            finalPath,
            inProgressPath,
            _diagnostic);
}

internal sealed class MediaFoundationAacEncoderSession :
    IMediaFoundationAacEncoderSession
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly string _finalPath;
    private readonly string _inProgressPath;
    private readonly Action<string, Exception?>? _diagnostic;
    private IMFSinkWriter? _sinkWriter;
    private MediaType? _outputMediaType;
    private MediaType? _inputMediaType;
    private int _streamIndex;

    public MediaFoundationAacEncoderSession(
        RecordingTrackWriterStartRequest request,
        string finalPath,
        string inProgressPath,
        Action<string, Exception?>? diagnostic)
    {
        _finalPath = finalPath;
        _inProgressPath = inProgressPath;
        _diagnostic = diagnostic;
        AssertOwnerThread();
        MediaFoundationApi.Startup();
        _outputMediaType = SelectAacMediaType(request);
        InputFormat = new WaveFormat(
            _outputMediaType.SampleRate,
            16,
            _outputMediaType.ChannelCount);
        Bitrate = _outputMediaType.AverageBytesPerSecond * 8;
        File.Delete(_inProgressPath);

        _inputMediaType = new MediaType(InputFormat);
        IMFAttributes? attributes = null;
        try
        {
            attributes = MediaFoundationApi.CreateAttributes(1);
            attributes.SetUINT32(
                MediaFoundationAttributes.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,
                1);
            MediaFoundationInterop.MFCreateSinkWriterFromURL(
                _inProgressPath,
                null!,
                attributes,
                out var sinkWriter);
            _sinkWriter = sinkWriter;
            _sinkWriter.AddStream(
                _outputMediaType.MediaFoundationObject,
                out _streamIndex);
            _sinkWriter.SetInputMediaType(
                _streamIndex,
                _inputMediaType.MediaFoundationObject,
                null!);
            _sinkWriter.BeginWriting();
        }
        catch
        {
            ReleaseNativeObjects();
            throw;
        }
        finally
        {
            ReleaseComObject(attributes);
        }

        var profile = _outputMediaType.TryGetUInt32(
            MediaFoundationAttributes.MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION,
            0);
        Report(
            $"Media Foundation AAC media type selected: track={request.Kind}; " +
            $"sampleRate={InputFormat.SampleRate}; channels={InputFormat.Channels}; " +
            $"bitrate={Bitrate}; profileLevel=0x{profile:X}; " +
            $"threadId={_ownerThreadId}.");
    }

    public WaveFormat InputFormat { get; }
    public int Bitrate { get; }

    public void WriteFrame(PcmAudioFrame frame)
    {
        AssertOwnerThread();
        var sinkWriter = _sinkWriter ?? throw new InvalidOperationException(
            "Media Foundation Sink Writer is not initialized.");
        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        var locked = false;
        try
        {
            buffer = MediaFoundationApi.CreateMemoryBuffer(frame.Data.Length);
            buffer.Lock(out var pointer, out _, out _);
            locked = true;
            Marshal.Copy(frame.Data, 0, pointer, frame.Data.Length);
            buffer.SetCurrentLength(frame.Data.Length);
            buffer.Unlock();
            locked = false;

            sample = MediaFoundationApi.CreateSample();
            sample.AddBuffer(buffer);
            sample.SetSampleTime(frame.SampleTime100Nanoseconds);
            sample.SetSampleDuration(frame.SampleDuration100Nanoseconds);
            sinkWriter.WriteSample(_streamIndex, sample);
        }
        finally
        {
            if (locked)
            {
                try
                {
                    buffer?.Unlock();
                }
                catch
                {
                    // Preserve the primary encoder exception.
                }
            }

            ReleaseComObject(sample);
            ReleaseComObject(buffer);
        }
    }

    public AacEncoderSessionCompletion Complete()
    {
        AssertOwnerThread();
        var sinkWriter = _sinkWriter ?? throw new InvalidOperationException(
            "Media Foundation Sink Writer is not initialized.");
        try
        {
            sinkWriter.NotifyEndOfSegment(_streamIndex);
            sinkWriter.DoFinalize();
        }
        finally
        {
            ReleaseNativeObjects();
        }

        AacEncoderSessionCompletion result;
        using (var reader = new MediaFoundationReader(_inProgressPath))
        {
            if (reader.Length <= 0 || reader.TotalTime <= TimeSpan.Zero)
            {
                throw new InvalidDataException(
                    "Finalized M4A track cannot be reopened as playable audio.");
            }

            if (reader.WaveFormat.SampleRate <= 0 || reader.WaveFormat.Channels <= 0)
            {
                throw new InvalidDataException(
                    "Finalized M4A track has invalid codec metadata.");
            }

            result = new AacEncoderSessionCompletion(
                new FileInfo(_inProgressPath).Length,
                reader.TotalTime);
        }

        File.Move(_inProgressPath, _finalPath, overwrite: true);
        Report(
            $"M4A track finalized and validated: file={Path.GetFileName(_finalPath)}; " +
            $"bytes={result.Bytes}; duration={result.Duration}; threadId={_ownerThreadId}.");
        return result;
    }

    public void Abort(bool preserveInProgressFile)
    {
        AssertOwnerThread();
        ReleaseNativeObjects();
        if (!preserveInProgressFile && File.Exists(_inProgressPath))
        {
            File.Delete(_inProgressPath);
        }

    }

    public void Dispose()
    {
        AssertOwnerThread();
        ReleaseNativeObjects();
    }

    private static MediaType SelectAacMediaType(
        RecordingTrackWriterStartRequest request)
    {
        var candidates = MediaFoundationEncoder.GetOutputMediaTypes(
            AudioSubtypes.MFAudioFormat_AAC);
        MediaType? selected = null;
        try
        {
            selected = candidates
                .Where(type =>
                    type.ChannelCount == request.PreferredChannels &&
                    type.SampleRate is 44_100 or 48_000 &&
                    IsAacLcProfile(type))
                .OrderBy(type => Math.Abs(type.SampleRate - request.PreferredSampleRate))
                .ThenBy(type => Math.Abs(
                    type.AverageBytesPerSecond * 8 - request.PreferredBitrate))
                .FirstOrDefault();
            if (selected is null)
            {
                throw new InvalidOperationException(
                    $"Windows Media Foundation has no AAC-LC encoder type for " +
                    $"{request.PreferredChannels} channel(s) at 44.1/48 kHz.");
            }

            return selected;
        }
        finally
        {
            foreach (var candidate in candidates)
            {
                if (!ReferenceEquals(candidate, selected))
                {
                    ReleaseComObject(candidate.MediaFoundationObject);
                }
            }
        }
    }

    private static bool IsAacLcProfile(MediaType type)
    {
        var profile = type.TryGetUInt32(
            MediaFoundationAttributes.MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION,
            0x29);
        return profile is 0 or 0x29 or 0x2A or 0x2B;
    }

    private void ReleaseNativeObjects()
    {
        AssertOwnerThread();
        ReleaseComObject(_sinkWriter);
        _sinkWriter = null;
        if (_inputMediaType is not null)
        {
            ReleaseComObject(_inputMediaType.MediaFoundationObject);
            _inputMediaType = null;
        }

        if (_outputMediaType is not null)
        {
            ReleaseComObject(_outputMediaType.MediaFoundationObject);
            _outputMediaType = null;
        }
    }

    private void AssertOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                $"Media Foundation AAC session crossed owner threads: " +
                $"owner={_ownerThreadId}; current={Environment.CurrentManagedThreadId}.");
        }
    }

    private void Report(string message)
    {
        try
        {
            _diagnostic?.Invoke(message, null);
        }
        catch
        {
            // Diagnostics never affect recording behavior.
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }
}
