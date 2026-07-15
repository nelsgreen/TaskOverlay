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
    private const int TargetSampleRate = 16_000;

    public Task<MeetingAudioProcessingResult> ProcessAsync(
        MeetingAudioProcessingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Process(request, cancellationToken), cancellationToken);
    }

    private static MeetingAudioProcessingResult Process(
        MeetingAudioProcessingRequest request,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(request.RecordingFolder);
        var available = new[]
            {
                (Path: request.SystemAudioPath, Started: request.SystemTrackStartedAtUtc),
                (Path: request.MicrophonePath, Started: request.MicrophoneTrackStartedAtUtc)
            }
            .Where(track => !string.IsNullOrWhiteSpace(track.Path) && File.Exists(track.Path))
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
                var reader = new AudioFileReader(track.Path!);
                readers.Add(reader);
                ISampleProvider provider = ToMono(reader);
                if (provider.WaveFormat.SampleRate != TargetSampleRate)
                {
                    provider = new WdlResamplingSampleProvider(provider, TargetSampleRate);
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

            var chunks = SplitIntoChunks(
                mixedPath,
                request.MaximumChunkBytes,
                cancellationToken);
            using var mixedReader = new WaveFileReader(mixedPath);
            return new MeetingAudioProcessingResult(
                mixedPath,
                chunks,
                mixedReader.TotalTime);
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }
    }

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

    private static IReadOnlyList<string> SplitIntoChunks(
        string mixedPath,
        long maximumChunkBytes,
        CancellationToken cancellationToken)
    {
        if (maximumChunkBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumChunkBytes));
        }

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
