using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.MediaFoundation;
using NAudio.Wave;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed class MediaFoundationAacTrackWriter : QueuedRecordingTrackWriter
{
    private IMFSinkWriter? _sinkWriter;
    private MediaType? _outputMediaType;
    private MediaType? _inputMediaType;
    private int _streamIndex;

    public MediaFoundationAacTrackWriter(Action<string, Exception?>? diagnostic = null)
        : base(diagnostic)
    {
    }

    protected override Task InitializeCoreAsync(
        RecordingTrackWriterStartRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaFoundationApi.Startup();
        _outputMediaType = SelectAacMediaType(request);
        InputFormat = new WaveFormat(
            _outputMediaType.SampleRate,
            16,
            _outputMediaType.ChannelCount);
        Bitrate = _outputMediaType.AverageBytesPerSecond * 8;
        Container = "MPEG-4/M4A";
        Codec = "AAC-LC";
        FinalPath = Path.Combine(request.RecordingFolder, $"{request.BaseFileName}.m4a");
        InProgressPath = Path.Combine(
            request.RecordingFolder,
            $"{request.BaseFileName}.current.m4a");
        File.Delete(InProgressPath);

        _inputMediaType = new MediaType(InputFormat);
        IMFAttributes? attributes = null;
        try
        {
            attributes = MediaFoundationApi.CreateAttributes(1);
            attributes.SetUINT32(
                MediaFoundationAttributes.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,
                1);
            MediaFoundationInterop.MFCreateSinkWriterFromURL(
                InProgressPath,
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
            $"Media Foundation AAC encoder initialized: kind={request.Kind}; " +
            $"sampleRate={InputFormat.SampleRate}; channels={InputFormat.Channels}; " +
            $"bitrate={Bitrate}; profileLevel=0x{profile:X}.");
        return Task.CompletedTask;
    }

    protected override void WriteFrameCore(PcmAudioFrame frame)
    {
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
                    // The primary encoder exception is more useful.
                }
            }

            ReleaseComObject(sample);
            ReleaseComObject(buffer);
        }
    }

    protected override Task CompleteCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        using (var reader = new MediaFoundationReader(InProgressPath))
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

            SetOutputMetrics(new FileInfo(InProgressPath).Length, reader.TotalTime);
        }

        File.Move(InProgressPath, FinalPath, overwrite: true);
        ValidationState = MeetingRecordingValidationState.Valid;
        Report(
            $"M4A track finalized and validated: file={Path.GetFileName(FinalPath)}; " +
            $"bytes={BytesWritten}; duration={Duration}.");
        return Task.CompletedTask;
    }

    protected override Task AbortCoreAsync(
        bool preserveInProgressFile,
        CancellationToken cancellationToken)
    {
        ReleaseNativeObjects();
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

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }
}
