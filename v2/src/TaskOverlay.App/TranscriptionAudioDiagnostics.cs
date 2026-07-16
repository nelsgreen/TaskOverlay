using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace TaskOverlay.App;

internal sealed record TranscriptionAudioFingerprint(
    string FullPath,
    string FileName,
    long Bytes,
    TimeSpan Duration,
    string Sha256);

internal static class TranscriptionAudioDiagnostics
{
    public static async Task<TranscriptionAudioFingerprint> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        await using var stream = OpenRead(fullPath);
        var bytes = stream.Length;
        var sha256 = await ComputeSha256Async(stream, cancellationToken);
        using var reader = new AudioFileReader(fullPath);
        return new TranscriptionAudioFingerprint(
            fullPath,
            Path.GetFileName(fullPath),
            bytes,
            reader.TotalTime,
            sha256);
    }

    public static FileStream OpenRead(string path) => new(
        Path.GetFullPath(path),
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        64 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    public static async Task<string> ComputeSha256Async(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new InvalidOperationException("The audio stream is not readable.");
        }

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
