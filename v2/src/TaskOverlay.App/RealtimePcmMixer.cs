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

public sealed class RealtimePcmMixer : IAsyncDisposable
{
    private const int MixSampleRate = 48_000;
    private const int RenderFrameCount = 960; // 20 ms
    private const int MessageCapacity = 512;
    private const long MaximumBufferedFrames = MixSampleRate * 30L;

    private readonly bool _systemExpected;
    private readonly bool _microphoneExpected;
    private readonly IRecordingTrackWriter _writer;
    private readonly Channel<MixMessage> _messages;
    private readonly Action<string, Exception?>? _diagnostic;
    private readonly object _sync = new();
    private Task? _worker;
    private string? _error;
    private bool _completeRequested;

    public RealtimePcmMixer(
        bool systemExpected,
        bool microphoneExpected,
        IRecordingTrackWriter writer,
        Action<string, Exception?>? diagnostic = null)
    {
        _systemExpected = systemExpected;
        _microphoneExpected = microphoneExpected;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _diagnostic = diagnostic;
        _messages = Channel.CreateBounded<MixMessage>(new BoundedChannelOptions(
            MessageCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public string? Error
    {
        get
        {
            lock (_sync)
            {
                return _error ?? _writer.Error;
            }
        }
    }

    public async Task StartAsync(
        string recordingFolder,
        CancellationToken cancellationToken = default)
    {
        await _writer.StartAsync(
            new RecordingTrackWriterStartRequest(
                MeetingRecordingTrackKind.Mixed,
                recordingFolder,
                "mixed",
                PreferredChannels: 1,
                PreferredSampleRate: MixSampleRate),
            cancellationToken);
        if (_writer.InputFormat.SampleRate != MixSampleRate ||
            _writer.InputFormat.Channels != 1 ||
            _writer.InputFormat.BitsPerSample != 16)
        {
            throw new InvalidOperationException(
                "The mixed track writer must accept 48 kHz mono PCM16 input.");
        }

        _worker = Task.Run(ProcessAsync, CancellationToken.None);
    }

    public bool TryAdd(
        MeetingRecordingTrackKind source,
        PcmAudioFrame frame,
        WaveFormat sourceFormat)
    {
        if (source is not (MeetingRecordingTrackKind.System or
            MeetingRecordingTrackKind.Microphone))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (sourceFormat.SampleRate != MixSampleRate ||
            sourceFormat.BitsPerSample != 16 ||
            sourceFormat.Encoding != WaveFormatEncoding.Pcm ||
            sourceFormat.Channels is < 1 or > 2)
        {
            Fail(new InvalidDataException(
                "The real-time mixer received audio outside its normalized PCM format."));
            return false;
        }

        return TryWrite(new AudioMessage(source, frame, sourceFormat.Channels));
    }

    public bool TryAdvance(TimeSpan elapsed, TimeSpan safetyDelay)
    {
        var safeTime = elapsed - safetyDelay;
        if (safeTime <= TimeSpan.Zero)
        {
            return true;
        }

        return TryWrite(new AdvanceMessage(safeTime.Ticks));
    }

    public async Task<MeetingRecordingTrackArtifact> CompleteAsync(
        TimeSpan finalDuration,
        CancellationToken cancellationToken = default)
    {
        var enqueueCompletion = false;
        lock (_sync)
        {
            if (!_completeRequested)
            {
                _completeRequested = true;
                enqueueCompletion = true;
            }
        }

        if (enqueueCompletion)
        {
            if (Error is null)
            {
                try
                {
                    await _messages.Writer.WriteAsync(
                        new CompleteMessage(finalDuration.Ticks),
                        cancellationToken);
                }
                catch (ChannelClosedException) when (Error is not null)
                {
                    // The worker already recorded the primary mix failure.
                }
            }

            _messages.Writer.TryComplete();
        }

        if (_worker is not null)
        {
            await _worker.WaitAsync(cancellationToken);
        }

        if (Error is not null)
        {
            await _writer.AbortAsync(CancellationToken.None);
            var failed = _writer.SnapshotArtifact();
            failed.Error = Error ?? failed.Error;
            failed.FinalizationState = MeetingRecordingFinalizationState.Failed;
            failed.ValidationState = MeetingRecordingValidationState.Invalid;
            return failed;
        }

        return await _writer.CompleteAsync(cancellationToken);
    }

    public MeetingRecordingTrackArtifact SnapshotArtifact() =>
        _writer.SnapshotArtifact();

    public async ValueTask DisposeAsync()
    {
        if (!_completeRequested)
        {
            _messages.Writer.TryComplete();
        }

        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch
            {
                // Writer abort below owns cleanup.
            }
        }

        await _writer.DisposeAsync();
    }

    private bool TryWrite(MixMessage message)
    {
        lock (_sync)
        {
            if (_completeRequested || _error is not null)
            {
                return false;
            }
        }

        if (_messages.Writer.TryWrite(message))
        {
            return true;
        }

        Fail(new InvalidOperationException(
            "The bounded real-time mix queue is full; mixed audio was stopped safely."));
        return false;
    }

    private async Task ProcessAsync()
    {
        var system = new TimelineBuffer();
        var microphone = new TimelineBuffer();
        long systemWatermark = _systemExpected ? 0 : long.MaxValue;
        long microphoneWatermark = _microphoneExpected ? 0 : long.MaxValue;
        long renderedFrame = 0;
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync())
            {
                switch (message)
                {
                    case AudioMessage audio:
                    {
                        var startFrame = TimeToFrames(
                            audio.Frame.SampleTime100Nanoseconds);
                        if (startFrame < renderedFrame)
                        {
                            throw new InvalidDataException(
                                "An audio callback arrived after its mix window was finalized.");
                        }

                        var samples = ToMonoSamples(audio.Frame.Data, audio.Channels);
                        var buffer = audio.Source == MeetingRecordingTrackKind.System
                            ? system
                            : microphone;
                        buffer.Add(startFrame, samples);
                        var endFrame = startFrame + samples.LongLength;
                        if (audio.Source == MeetingRecordingTrackKind.System)
                        {
                            systemWatermark = Math.Max(systemWatermark, endFrame);
                        }
                        else
                        {
                            microphoneWatermark = Math.Max(microphoneWatermark, endFrame);
                        }

                        break;
                    }
                    case AdvanceMessage advance:
                    {
                        var safeFrame = TimeToFrames(advance.SafeTime100Nanoseconds);
                        if (_systemExpected)
                        {
                            systemWatermark = Math.Max(systemWatermark, safeFrame);
                        }

                        if (_microphoneExpected)
                        {
                            microphoneWatermark = Math.Max(microphoneWatermark, safeFrame);
                        }

                        break;
                    }
                    case CompleteMessage complete:
                    {
                        var finalFrame = TimeToFrames(complete.FinalTime100Nanoseconds);
                        systemWatermark = _systemExpected
                            ? Math.Max(systemWatermark, finalFrame)
                            : long.MaxValue;
                        microphoneWatermark = _microphoneExpected
                            ? Math.Max(microphoneWatermark, finalFrame)
                            : long.MaxValue;
                        break;
                    }
                }

                var renderUntil = Math.Min(systemWatermark, microphoneWatermark);
                if (!_systemExpected)
                {
                    renderUntil = microphoneWatermark;
                }
                else if (!_microphoneExpected)
                {
                    renderUntil = systemWatermark;
                }

                renderedFrame = Render(
                    system,
                    microphone,
                    renderedFrame,
                    renderUntil);
                var latestBuffered = Math.Max(system.LatestEndFrame, microphone.LatestEndFrame);
                if (latestBuffered - renderedFrame > MaximumBufferedFrames)
                {
                    throw new InvalidOperationException(
                        "Audio clock drift or encoder delay exceeded the bounded mix buffer.");
                }
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private long Render(
        TimelineBuffer system,
        TimelineBuffer microphone,
        long position,
        long renderUntil)
    {
        while (position < renderUntil)
        {
            var count = (int)Math.Min(RenderFrameCount, renderUntil - position);
            var systemSamples = new short[count];
            var microphoneSamples = new short[count];
            var systemPresence = new bool[count];
            var microphonePresence = new bool[count];
            system.Fill(position, systemSamples, systemPresence);
            microphone.Fill(position, microphoneSamples, microphonePresence);

            var mixed = new byte[count * sizeof(short)];
            var hasAudio = false;
            for (var index = 0; index < count; index++)
            {
                var hasSystem = systemPresence[index];
                var hasMicrophone = microphonePresence[index];
                if (!hasSystem && !hasMicrophone)
                {
                    continue;
                }

                hasAudio = true;
                var sample = hasSystem && hasMicrophone
                    ? (systemSamples[index] + microphoneSamples[index]) / 2
                    : hasSystem
                        ? systemSamples[index]
                        : microphoneSamples[index];
                mixed[index * 2] = (byte)(sample & 0xff);
                mixed[index * 2 + 1] = (byte)((sample >> 8) & 0xff);
            }

            if (hasAudio)
            {
                var start = FramesToTime(position);
                var duration = FramesToTime(count);
                if (!_writer.TryWrite(new PcmAudioFrame(mixed, start, duration)))
                {
                    throw new InvalidOperationException(
                        _writer.Error ?? "The mixed track encoder rejected audio frames.");
                }
            }

            position += count;
            system.DiscardBefore(position);
            microphone.DiscardBefore(position);
        }

        return position;
    }

    private static short[] ToMonoSamples(byte[] data, int channels)
    {
        var frameCount = data.Length / (sizeof(short) * channels);
        var samples = new short[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var offset = frame * channels * sizeof(short);
            var left = (short)(data[offset] | data[offset + 1] << 8);
            if (channels == 1)
            {
                samples[frame] = left;
                continue;
            }

            var right = (short)(data[offset + 2] | data[offset + 3] << 8);
            samples[frame] = (short)((left + right) / 2);
        }

        return samples;
    }

    private static long TimeToFrames(long time100Nanoseconds) =>
        Math.Max(0, time100Nanoseconds) * MixSampleRate / 10_000_000L;

    private static long FramesToTime(long frames) =>
        Math.Max(0, frames) * 10_000_000L / MixSampleRate;

    private void Fail(Exception exception)
    {
        lock (_sync)
        {
            _error ??= exception.Message;
        }

        _messages.Writer.TryComplete(exception);

        try
        {
            _diagnostic?.Invoke("Real-time meeting audio mix failed.", exception);
        }
        catch
        {
            // Diagnostics never affect recording behavior.
        }
    }

    private abstract record MixMessage;
    private sealed record AudioMessage(
        MeetingRecordingTrackKind Source,
        PcmAudioFrame Frame,
        int Channels) : MixMessage;
    private sealed record AdvanceMessage(long SafeTime100Nanoseconds) : MixMessage;
    private sealed record CompleteMessage(long FinalTime100Nanoseconds) : MixMessage;

    private sealed class TimelineBuffer
    {
        private readonly List<MonoChunk> _chunks = new();

        public long LatestEndFrame => _chunks.Count == 0
            ? 0
            : _chunks.Max(chunk => chunk.EndFrame);

        public void Add(long startFrame, short[] samples)
        {
            if (samples.Length == 0)
            {
                return;
            }

            _chunks.Add(new MonoChunk(startFrame, samples));
            if (_chunks.Count > 1 &&
                _chunks[^2].StartFrame > _chunks[^1].StartFrame)
            {
                _chunks.Sort((left, right) => left.StartFrame.CompareTo(right.StartFrame));
            }
        }

        public void Fill(long startFrame, short[] destination, bool[] presence)
        {
            var endFrame = startFrame + destination.LongLength;
            foreach (var chunk in _chunks)
            {
                if (chunk.EndFrame <= startFrame)
                {
                    continue;
                }

                if (chunk.StartFrame >= endFrame)
                {
                    break;
                }

                var overlapStart = Math.Max(startFrame, chunk.StartFrame);
                var overlapEnd = Math.Min(endFrame, chunk.EndFrame);
                for (var frame = overlapStart; frame < overlapEnd; frame++)
                {
                    var destinationIndex = (int)(frame - startFrame);
                    destination[destinationIndex] = chunk.Samples[frame - chunk.StartFrame];
                    presence[destinationIndex] = true;
                }
            }
        }

        public void DiscardBefore(long frame) =>
            _chunks.RemoveAll(chunk => chunk.EndFrame <= frame);
    }

    private sealed record MonoChunk(long StartFrame, short[] Samples)
    {
        public long EndFrame => StartFrame + Samples.LongLength;
    }
}
