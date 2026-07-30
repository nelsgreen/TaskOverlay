using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed class MeetingAudioProcessor : IMeetingAudioProcessor
{
    private const int LegacyTargetSampleRate = 16_000;
    private const int EncodingBufferSamples = 4_800;

    public async Task<MeetingAudioProcessingResult> ProcessAsync(
        MeetingAudioProcessingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.MaximumChunkBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MaximumChunkBytes));
        }

        Directory.CreateDirectory(request.RecordingFolder);
        DeleteStaleTranscriptionChunks(request.RecordingFolder);
        if (!string.IsNullOrWhiteSpace(request.ExistingMixedAudioPath))
        {
            return await ProcessFinalizedMixedAsync(request, cancellationToken);
        }

        if (request.RecordingFormat == MeetingRecordingFormat.AacM4a)
        {
            throw new InvalidDataException(
                "Compact recordings require a finalized mixed M4A track for transcription.");
        }

        return await Task.Run(
            () => ProcessLegacySourceTracks(request, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Extracts one bounded interval into its own M4A artifact and measures
    /// the result.
    ///
    /// The measurement is the point of this method. Planned timestamps are not
    /// trusted: AAC encoders emit whole frames and pad the tail, and seeking
    /// lands on a frame boundary, so the real duration drifts off the plan.
    /// If a generated file still exceeds the safe limit it is re-extracted
    /// shorter, and if that still fails it is rejected here - the provider is
    /// never asked to enforce the limit.
    /// </summary>
    public async Task<MeetingAudioChunkExtractionResult> ExtractChunkAsync(
        MeetingAudioChunkExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.MaximumDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MaximumDurationSeconds));
        }

        if (request.EndSeconds <= request.StartSeconds || request.StartSeconds < 0)
        {
            throw new InvalidDataException("The transcription chunk interval is invalid.");
        }

        var sourcePath = Path.GetFullPath(request.SourceAudioPath);
        EnsureFinalizedAudioPath(sourcePath);
        Directory.CreateDirectory(request.DestinationFolder);

        var requestedDuration = Math.Min(
            request.EndSeconds - request.StartSeconds,
            request.MaximumDurationSeconds);
        var tolerance = MeetingTranscriptionChunkPolicy.MeasurementToleranceSeconds;

        string? lastPath = null;
        var lastMeasured = 0d;
        var lastBytes = 0L;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = await EncodeIntervalAsync(
                sourcePath,
                request.DestinationFolder,
                request.DestinationBaseName,
                TimeSpan.FromSeconds(request.StartSeconds),
                TimeSpan.FromSeconds(requestedDuration),
                request.Bitrate,
                cancellationToken);
            lastPath = path;
            lastBytes = new FileInfo(path).Length;
            lastMeasured = ReadDuration(path).TotalSeconds;

            var withinDuration =
                lastMeasured <= request.MaximumDurationSeconds + tolerance;
            var withinBytes = request.MaximumBytes <= 0 || lastBytes <= request.MaximumBytes;
            if (withinDuration && withinBytes)
            {
                return new MeetingAudioChunkExtractionResult(path, lastMeasured, lastBytes);
            }

            // Correct rather than reject on the first overshoot: shorten by the
            // measured excess plus a margin, and by the byte overshoot ratio.
            var corrected = requestedDuration;
            if (!withinDuration)
            {
                corrected = Math.Min(
                    corrected,
                    requestedDuration - (lastMeasured - request.MaximumDurationSeconds) - 1.0);
            }

            if (!withinBytes)
            {
                corrected = Math.Min(
                    corrected,
                    requestedDuration * request.MaximumBytes / Math.Max(1, lastBytes) * 0.98);
            }

            TryDeleteFile(path);
            lastPath = null;
            if (corrected <= 0 || corrected >= requestedDuration)
            {
                break;
            }

            requestedDuration = corrected;
        }

        if (lastPath is not null)
        {
            TryDeleteFile(lastPath);
        }

        throw new InvalidDataException(
            "A transcription part could not be extracted within the safe audio limit.");
    }

    /// <summary>
    /// Encodes exactly one interval of the source into a fresh M4A file. The
    /// source is opened read-only and never modified.
    /// </summary>
    private static async Task<string> EncodeIntervalAsync(
        string sourcePath,
        string destinationFolder,
        string destinationBaseName,
        TimeSpan start,
        TimeSpan duration,
        int bitrate,
        CancellationToken cancellationToken)
    {
        using var reader = new AudioFileReader(sourcePath);
        if (start >= reader.TotalTime)
        {
            throw new InvalidDataException(
                "The transcription chunk starts past the end of the source audio.");
        }

        reader.CurrentTime = start;
        ISampleProvider source = new OffsetSampleProvider(ToMono(reader))
        {
            Take = duration
        };

        await using var writer = new MediaFoundationAacTrackWriter();
        await writer.StartAsync(
            new RecordingTrackWriterStartRequest(
                MeetingRecordingTrackKind.Mixed,
                destinationFolder,
                destinationBaseName,
                PreferredChannels: 1,
                PreferredBitrate: Math.Max(1, bitrate)),
            cancellationToken);

        if (source.WaveFormat.SampleRate != writer.InputFormat.SampleRate)
        {
            source = new WdlResamplingSampleProvider(source, writer.InputFormat.SampleRate);
        }

        // Frame budget is derived from the requested duration, so the encoder
        // stops on time even when the source has more audio available.
        var maximumFrames = (long)Math.Round(
            duration.TotalSeconds * writer.InputFormat.SampleRate);
        var buffer = new float[EncodingBufferSamples];
        long framesWritten = 0;
        while (framesWritten < maximumFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, maximumFrames - framesWritten);
            var read = source.Read(buffer, 0, requested);
            if (read <= 0)
            {
                break;
            }

            var pcm = ConvertToPcm16(buffer, read);
            await writer.WriteAsync(
                new PcmAudioFrame(
                    pcm,
                    FramesToMediaTime(framesWritten, writer.InputFormat.SampleRate),
                    FramesToMediaTime(read, writer.InputFormat.SampleRate)),
                cancellationToken);
            framesWritten += read;
        }

        var artifact = await writer.CompleteAsync(cancellationToken);
        if (framesWritten == 0)
        {
            throw new InvalidDataException(
                "The transcription chunk interval contains no decodable audio frames.");
        }

        if (artifact.ValidationState != MeetingRecordingValidationState.Valid ||
            string.IsNullOrWhiteSpace(artifact.FileName))
        {
            throw new InvalidDataException(
                artifact.Error.Length == 0
                    ? "A transcription part could not be finalized."
                    : artifact.Error);
        }

        return Path.Combine(destinationFolder, artifact.FileName);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An abandoned extraction artifact must never fail the job; the
            // job-folder sweep removes it later.
        }
    }

    private static async Task<MeetingAudioProcessingResult> ProcessFinalizedMixedAsync(
        MeetingAudioProcessingRequest request,
        CancellationToken cancellationToken)
    {
        var mixedPath = Path.GetFullPath(request.ExistingMixedAudioPath!);
        EnsureFinalizedAudioPath(mixedPath);
        var duration = ReadDuration(mixedPath);
        var extension = Path.GetExtension(mixedPath);
        var rangeStart = TimeSpan.FromSeconds(request.ProcessFromSeconds ?? 0);
        var rangeEnd = TimeSpan.FromSeconds(request.ProcessUntilSeconds ?? duration.TotalSeconds);
        if (rangeStart < TimeSpan.Zero || rangeStart >= duration ||
            rangeEnd <= rangeStart || rangeEnd > duration.Add(TimeSpan.FromMilliseconds(10)))
        {
            throw new InvalidDataException("The selected audio processing range is invalid.");
        }

        var hasRange = request.ProcessFromSeconds.HasValue || request.ProcessUntilSeconds.HasValue;
        IReadOnlyList<string> chunks;
        if (hasRange || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            chunks = await EncodeAudioIntoM4aChunksAsync(
                mixedPath,
                request.RecordingFolder,
                request.MaximumChunkBytes,
                request.MixedAudioBitrate,
                rangeStart,
                rangeEnd,
                cancellationToken);
        }
        else if (new FileInfo(mixedPath).Length <= request.MaximumChunkBytes)
        {
            chunks = new[] { mixedPath };
        }
        else if (extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase))
        {
            chunks = await EncodeAudioIntoM4aChunksAsync(
                mixedPath,
                request.RecordingFolder,
                request.MaximumChunkBytes,
                request.MixedAudioBitrate,
                TimeSpan.Zero,
                duration,
                cancellationToken);
        }
        else if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            chunks = SplitWaveIntoChunks(
                mixedPath,
                request.MaximumChunkBytes,
                cancellationToken);
        }
        else
        {
            throw new InvalidDataException(
                "The finalized mixed recording has an unsupported audio container.");
        }

        return new MeetingAudioProcessingResult(mixedPath, chunks, rangeEnd - rangeStart);
    }

    private static MeetingAudioProcessingResult ProcessLegacySourceTracks(
        MeetingAudioProcessingRequest request,
        CancellationToken cancellationToken)
    {
        var available = new[]
            {
                (Path: request.SystemAudioPath, Started: request.SystemTrackStartedAtUtc),
                (Path: request.MicrophonePath, Started: request.MicrophoneTrackStartedAtUtc)
            }
            .Where(track => !string.IsNullOrWhiteSpace(track.Path))
            .Select(track =>
            {
                var path = Path.GetFullPath(track.Path!);
                EnsureFinalizedAudioPath(path);
                return (Path: path, track.Started);
            })
            .ToList();
        if (available.Count == 0)
        {
            throw new InvalidDataException("No recorded audio track is available for processing.");
        }

        var readers = new List<AudioFileReader>();
        try
        {
            var earliestStart = available
                .Where(track => track.Started.HasValue)
                .Select(track => track.Started!.Value)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Min();
            var providers = new List<ISampleProvider>();
            foreach (var track in available)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reader = new AudioFileReader(track.Path);
                readers.Add(reader);
                ISampleProvider provider = ToMono(reader);
                if (provider.WaveFormat.SampleRate != LegacyTargetSampleRate)
                {
                    provider = new WdlResamplingSampleProvider(
                        provider,
                        LegacyTargetSampleRate);
                }

                if (earliestStart != DateTimeOffset.MinValue &&
                    track.Started is DateTimeOffset started &&
                    started > earliestStart)
                {
                    provider = new OffsetSampleProvider(provider)
                    {
                        DelayBy = started - earliestStart
                    };
                }

                providers.Add(provider);
            }

            ISampleProvider mixedProvider = providers.Count == 1
                ? providers[0]
                : new MixingSampleProvider(providers)
                {
                    ReadFully = false
                };
            var mixedPath = Path.Combine(request.RecordingFolder, "mixed.wav");
            WaveFileWriter.CreateWaveFile16(
                mixedPath,
                new CancellationSampleProvider(mixedProvider, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();

            var chunks = SplitWaveIntoChunks(
                mixedPath,
                request.MaximumChunkBytes,
                cancellationToken);
            return new MeetingAudioProcessingResult(
                mixedPath,
                chunks,
                ReadDuration(mixedPath));
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }
    }

    private static async Task<IReadOnlyList<string>> EncodeAudioIntoM4aChunksAsync(
        string mixedPath,
        string recordingFolder,
        long maximumChunkBytes,
        int bitrate,
        TimeSpan rangeStart,
        TimeSpan rangeEnd,
        CancellationToken cancellationToken)
    {
        if (maximumChunkBytes < 256 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumChunkBytes),
                "M4A transcription chunks require at least 256 KiB.");
        }

        using var reader = new AudioFileReader(mixedPath);
        reader.CurrentTime = rangeStart;
        ISampleProvider source = new OffsetSampleProvider(ToMono(reader))
        {
            Take = rangeEnd - rangeStart
        };
        var chunks = new List<string>();
        var chunkIndex = 0;
        var endOfInput = false;
        while (!endOfInput)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var writer = new MediaFoundationAacTrackWriter();
            var baseName = $"transcription-{chunkIndex:D3}";
            await writer.StartAsync(
                new RecordingTrackWriterStartRequest(
                    MeetingRecordingTrackKind.Mixed,
                    recordingFolder,
                    baseName,
                    PreferredChannels: 1,
                    PreferredBitrate: Math.Max(1, bitrate)),
                cancellationToken);

            ISampleProvider normalized = source;
            if (normalized.WaveFormat.SampleRate != writer.InputFormat.SampleRate)
            {
                normalized = new WdlResamplingSampleProvider(
                    normalized,
                    writer.InputFormat.SampleRate);
                source = normalized;
            }

            var actualBitrate = Math.Max(1, writer.SnapshotArtifact().Bitrate);
            var payloadBudget = maximumChunkBytes * 85 / 100;
            var maximumFrames = Math.Max(
                writer.InputFormat.SampleRate,
                payloadBudget * 8 * writer.InputFormat.SampleRate / actualBitrate);
            var buffer = new float[EncodingBufferSamples];
            long framesWritten = 0;
            while (framesWritten < maximumFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requested = (int)Math.Min(buffer.Length, maximumFrames - framesWritten);
                var read = normalized.Read(buffer, 0, requested);
                if (read <= 0)
                {
                    endOfInput = true;
                    break;
                }

                var pcm = ConvertToPcm16(buffer, read);
                var start = FramesToMediaTime(framesWritten, writer.InputFormat.SampleRate);
                var duration = FramesToMediaTime(read, writer.InputFormat.SampleRate);
                await writer.WriteAsync(
                    new PcmAudioFrame(pcm, start, duration),
                    cancellationToken);
                framesWritten += read;
            }

            var artifact = await writer.CompleteAsync(cancellationToken);
            if (framesWritten == 0 && endOfInput)
            {
                break;
            }

            if (artifact.ValidationState != MeetingRecordingValidationState.Valid ||
                string.IsNullOrWhiteSpace(artifact.FileName))
            {
                throw new InvalidDataException(
                    artifact.Error.Length == 0
                        ? "A transcription M4A chunk could not be finalized."
                        : artifact.Error);
            }

            var chunkPath = Path.Combine(recordingFolder, artifact.FileName);
            if (new FileInfo(chunkPath).Length > maximumChunkBytes)
            {
                throw new InvalidDataException(
                    "An encoded transcription M4A chunk exceeds the provider size limit.");
            }

            chunks.Add(chunkPath);
            chunkIndex++;
        }

        if (chunks.Count == 0)
        {
            throw new InvalidDataException(
                "The mixed M4A recording contains no decodable audio frames.");
        }

        return chunks;
    }

    private static byte[] ConvertToPcm16(float[] samples, int count)
    {
        var result = new byte[count * sizeof(short)];
        for (var index = 0; index < count; index++)
        {
            var value = (short)Math.Round(
                Math.Clamp(samples[index], -1f, 1f) * short.MaxValue);
            result[index * 2] = (byte)(value & 0xff);
            result[index * 2 + 1] = (byte)((value >> 8) & 0xff);
        }

        return result;
    }

    private static long FramesToMediaTime(long frames, int sampleRate) =>
        frames * 10_000_000L / sampleRate;

    private static ISampleProvider ToMono(ISampleProvider provider)
    {
        return provider.WaveFormat.Channels switch
        {
            1 => provider,
            2 => new StereoToMonoSampleProvider(provider)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            },
            _ => throw new NotSupportedException(
                $"Audio with {provider.WaveFormat.Channels} channels is not supported for transcription mixing.")
        };
    }

    private static IReadOnlyList<string> SplitWaveIntoChunks(
        string mixedPath,
        long maximumChunkBytes,
        CancellationToken cancellationToken)
    {
        using var reader = new WaveFileReader(mixedPath);
        if (reader.Length <= maximumChunkBytes)
        {
            return new[] { mixedPath };
        }

        var blockAlign = reader.WaveFormat.BlockAlign;
        var chunkDataBytes = Math.Max(
            blockAlign,
            (maximumChunkBytes - 128) / blockAlign * blockAlign);
        var buffer = new byte[Math.Min(64 * 1024, (int)Math.Min(chunkDataBytes, int.MaxValue))];
        var chunks = new List<string>();
        var chunkIndex = 0;
        while (reader.Position < reader.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkPath = Path.Combine(
                Path.GetDirectoryName(mixedPath)!,
                $"transcription-{chunkIndex:D3}.wav");
            using var writer = new WaveFileWriter(chunkPath, reader.WaveFormat);
            long written = 0;
            while (written < chunkDataBytes && reader.Position < reader.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requested = (int)Math.Min(buffer.Length, chunkDataBytes - written);
                requested -= requested % blockAlign;
                if (requested <= 0)
                {
                    break;
                }

                var read = reader.Read(buffer, 0, requested);
                if (read <= 0)
                {
                    break;
                }

                writer.Write(buffer, 0, read);
                written += read;
            }

            chunks.Add(chunkPath);
            chunkIndex++;
        }

        return chunks;
    }

    private static void DeleteStaleTranscriptionChunks(string recordingFolder)
    {
        foreach (var path in Directory.EnumerateFiles(
                     recordingFolder,
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
    }

    private static void EnsureFinalizedAudioPath(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!File.Exists(path) ||
            fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(".current.", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Only a finalized recording artifact can be used for transcription.");
        }
    }

    private static TimeSpan ReadDuration(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            if (reader.TotalTime <= TimeSpan.Zero)
            {
                throw new InvalidDataException(
                    "The finalized recording has no playable duration.");
            }

            return reader.TotalTime;
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException(
                "The finalized recording cannot be opened as playable audio.",
                ex);
        }
    }

    private sealed class CancellationSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _inner;
        private readonly CancellationToken _cancellationToken;

        public CancellationSampleProvider(
            ISampleProvider inner,
            CancellationToken cancellationToken)
        {
            _inner = inner;
            _cancellationToken = cancellationToken;
        }

        public WaveFormat WaveFormat => _inner.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return _inner.Read(buffer, offset, count);
        }
    }
}
