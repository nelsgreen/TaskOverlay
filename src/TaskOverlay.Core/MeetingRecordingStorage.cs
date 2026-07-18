using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskOverlay.Core;

public sealed record MeetingRecordingLayout(
    string RelativeFolder,
    string AbsoluteFolder,
    string MetadataPath,
    string SystemAudioPath,
    string MicrophonePath,
    string MixedAudioPath,
    string TranscriptRawPath,
    string TranscriptPath,
    string TranscriptMarkdownPath,
    string AnalysisPath);

public sealed class MeetingRecordingStorage
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public MeetingRecordingStorage(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        StateDirectory = Path.GetFullPath(stateDirectory);
        RootDirectory = Path.Combine(StateDirectory, "meetings");
    }

    public string StateDirectory { get; }
    public string RootDirectory { get; }

    public MeetingRecordingLayout CreateLayout(Guid? meetId, Guid recordingId)
    {
        if (recordingId == Guid.Empty)
        {
            throw new ArgumentException("Recording id is required.", nameof(recordingId));
        }

        var owner = meetId.HasValue ? meetId.Value.ToString("N") : "emergency";
        var relativeFolder = $"meetings/{owner}/recordings/{recordingId:N}";
        var absoluteFolder = ResolveFolder(relativeFolder);
        Directory.CreateDirectory(absoluteFolder);
        return new MeetingRecordingLayout(
            relativeFolder,
            absoluteFolder,
            Path.Combine(absoluteFolder, "recording-meta.json"),
            Path.Combine(absoluteFolder, "system.wav"),
            Path.Combine(absoluteFolder, "microphone.wav"),
            Path.Combine(absoluteFolder, "mixed.wav"),
            Path.Combine(absoluteFolder, "transcript.raw.json"),
            Path.Combine(absoluteFolder, "transcript.json"),
            Path.Combine(absoluteFolder, "transcript.md"),
            Path.Combine(absoluteFolder, "analysis.json"));
    }

    public string ResolveFolder(string relativeFolder)
    {
        if (!RecordingPathPolicy.IsSafeRelativePath(relativeFolder))
        {
            throw new InvalidDataException("Recording folder path is not safe.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(
            StateDirectory,
            relativeFolder.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInsideStateDirectory(fullPath);
        return fullPath;
    }

    public string ResolveFile(MeetingRecording recording, string fileName)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (string.IsNullOrWhiteSpace(fileName) ||
            Path.IsPathRooted(fileName) ||
            fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Recording file path is not safe.");
        }

        var folder = ResolveFolder(recording.RecordingFolderRelativePath);
        var fullPath = Path.GetFullPath(Path.Combine(folder, fileName));
        var folderPrefix = folder.TrimEnd(Path.DirectorySeparatorChar) +
                           Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Recording file escapes its folder.");
        }

        return fullPath;
    }

    public void WriteMetadata(MeetingRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var folder = ResolveFolder(recording.RecordingFolderRelativePath);
        Directory.CreateDirectory(folder);
        WriteJsonAtomic(Path.Combine(folder, "recording-meta.json"), recording);
    }

    public void WriteJsonAtomic<T>(string path, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        WriteTextAtomic(path, json);
    }

    public static void WriteTextAtomic(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path) ??
                        throw new InvalidDataException("Output path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content ?? string.Empty);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void DeleteRecordingFiles(MeetingRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var folder = ResolveFolder(recording.RecordingFolderRelativePath);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private void EnsureInsideStateDirectory(string fullPath)
    {
        var root = StateDirectory.TrimEnd(Path.DirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Recording path escapes the state directory.");
        }
    }
}
