using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaskOverlay.Core;

public enum BackupOutcome
{
    Succeeded,
    SkippedDisabled,
    Failed
}

public readonly record struct BackupResult(
    BackupOutcome Outcome,
    string Message,
    string? BackupPath = null)
{
    public bool Succeeded => Outcome == BackupOutcome.Succeeded;
}

public readonly record struct BackupFileInfo(
    string Path,
    DateTimeOffset LastWriteTimeUtc);

public sealed class BackupMetadata
{
    public int FormatVersion { get; set; } = 1;
    public string App { get; set; } = "TaskOverlay";
    public string Product { get; set; } = "WPF v2";
    public string TaskSpace { get; set; } = BackupService.CurrentTaskSpace;
    public string SourceMachine { get; set; } = string.Empty;
    public string StatePath { get; set; } = string.Empty;
    public string BackupFileName { get; set; } = string.Empty;
    public DateTimeOffset BackupCreatedAtUtc { get; set; }
    public DateTimeOffset StateLastWriteTimeUtc { get; set; }
}

public enum BackupFreshnessStatus
{
    NoFolderConfigured,
    FolderUnavailable,
    NoBackupsFound,
    BackupNewer,
    LocalNewer,
    UpToDate,
    Failed
}

public sealed record BackupCandidate(
    string BackupPath,
    string? MetadataPath,
    DateTimeOffset FreshnessUtc,
    DateTimeOffset BackupCreatedAtUtc,
    string SourceMachine,
    string TaskSpace);

public sealed record BackupFolderCheckResult(
    BackupFreshnessStatus Status,
    string Message,
    DateTimeOffset? LocalStateTimestampUtc,
    string CurrentMachine,
    BackupCandidate? LatestBackup = null);

public readonly record struct RestoreResult(
    bool Succeeded,
    string Message,
    string? SafetyBackupPath = null);

public sealed class BackupService
{
    public const string CurrentTaskSpace = "Work";

    private static readonly Regex BackupTimestampPattern = new(
        @"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}\.json$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CandidateNamePattern = new(
        @"^TaskOverlay_Work_(?<machine>[A-Za-z0-9_-]+)_(?<timestamp>\d{4}-\d{2}-\d{2}_\d{2}-\d{2})\.json$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _statePath;
    private readonly Action<string, Exception?>? _diagnostic;

    public BackupService(
        string statePath,
        Action<string, Exception?>? diagnostic = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = statePath;
        _diagnostic = diagnostic;
    }

    public BackupResult CreateBackup(
        BackupConfiguration configuration,
        bool requireEnabled,
        DateTimeOffset? now = null,
        string? machineName = null)
    {
        if (requireEnabled && !configuration.Enabled)
        {
            return new BackupResult(
                BackupOutcome.SkippedDisabled,
                "Automatic backups are disabled.");
        }

        if (string.IsNullOrWhiteSpace(configuration.FolderPath))
        {
            return Failure("Backup folder is not configured.");
        }

        if (!Directory.Exists(configuration.FolderPath))
        {
            return Failure($"Backup folder is unavailable: {configuration.FolderPath}");
        }

        if (!File.Exists(_statePath))
        {
            return Failure($"State file is missing: {_statePath}");
        }

        var timestamp = now ?? DateTimeOffset.Now;
        var currentMachineName = machineName ?? Environment.MachineName;
        var fileName = BuildFileName(
            CurrentTaskSpace,
            currentMachineName,
            timestamp);
        var metadata = CreateMetadata(
            fileName,
            currentMachineName,
            timestamp.ToUniversalTime(),
            File.GetLastWriteTimeUtc(_statePath));
        return WriteBackupPair(
            configuration,
            fileName,
            metadata,
            applyRetention: true);
    }

    public static bool IsAutomaticBackupDue(
        BackupSettings settings,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.FolderPath))
        {
            return false;
        }

        var lastAttempt = settings.LastBackupAttemptAtUtc ??
                          settings.LastBackupAtUtc;
        return lastAttempt is null ||
               nowUtc >= lastAttempt.Value.AddMinutes(
                   Math.Clamp(settings.IntervalMinutes, 1, 24 * 60));
    }

    public BackupFolderCheckResult CheckBackupFolder(
        BackupConfiguration configuration,
        string? machineName = null)
    {
        var currentMachine = SanitizeMachineName(
            machineName ?? Environment.MachineName);
        if (string.IsNullOrWhiteSpace(configuration.FolderPath))
        {
            return new BackupFolderCheckResult(
                BackupFreshnessStatus.NoFolderConfigured,
                "Backup folder is not configured.",
                GetLocalStateTimestamp(),
                currentMachine);
        }

        if (!Directory.Exists(configuration.FolderPath))
        {
            return new BackupFolderCheckResult(
                BackupFreshnessStatus.FolderUnavailable,
                "Backup folder is missing or unavailable.",
                GetLocalStateTimestamp(),
                currentMachine);
        }

        try
        {
            var localTimestamp = GetLocalStateTimestamp();
            var latest = EnumerateBackupCandidates(configuration.FolderPath)
                .OrderByDescending(candidate => candidate.FreshnessUtc)
                .FirstOrDefault();
            if (latest is null)
            {
                return new BackupFolderCheckResult(
                    BackupFreshnessStatus.NoBackupsFound,
                    "No backups found.",
                    localTimestamp,
                    currentMachine);
            }

            if (localTimestamp is null)
            {
                return new BackupFolderCheckResult(
                    BackupFreshnessStatus.BackupNewer,
                    "Newer backup available.",
                    null,
                    currentMachine,
                    latest);
            }

            var difference = latest.FreshnessUtc - localTimestamp.Value;
            var status = difference > TimeSpan.FromMinutes(1)
                ? BackupFreshnessStatus.BackupNewer
                : difference < TimeSpan.FromMinutes(-1)
                    ? BackupFreshnessStatus.LocalNewer
                    : BackupFreshnessStatus.UpToDate;
            var message = status switch
            {
                BackupFreshnessStatus.BackupNewer => "Newer backup available.",
                BackupFreshnessStatus.LocalNewer =>
                    "Local state is newer than the latest backup.",
                _ => "Backup appears up to date."
            };
            return new BackupFolderCheckResult(
                status,
                message,
                localTimestamp,
                currentMachine,
                latest);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            Report("Backup folder check failed.", ex);
            return new BackupFolderCheckResult(
                BackupFreshnessStatus.Failed,
                "Backup folder check failed.",
                GetLocalStateTimestamp(),
                currentMachine);
        }
    }

    public RestoreResult RestoreLatestBackup(
        BackupConfiguration configuration,
        BackupCandidate candidate,
        DateTimeOffset? now = null,
        string? machineName = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (string.IsNullOrWhiteSpace(configuration.FolderPath) ||
            !Directory.Exists(configuration.FolderPath))
        {
            return new RestoreResult(false, "Backup folder is missing or unavailable.");
        }

        string configuredFolder;
        string candidateFolder;
        try
        {
            configuredFolder = Path.GetFullPath(configuration.FolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            candidateFolder = Path.GetFullPath(
                    Path.GetDirectoryName(candidate.BackupPath) ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new RestoreResult(false, "Restore candidate path is invalid.");
        }
        if (!string.Equals(
                configuredFolder,
                candidateFolder,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                candidate.TaskSpace,
                CurrentTaskSpace,
                StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreResult(false, "Restore candidate does not match this Work backup folder.");
        }

        if (!File.Exists(candidate.BackupPath))
        {
            return new RestoreResult(false, "Selected backup no longer exists.");
        }

        if (!ValidateStateBackup(candidate.BackupPath, out var validationError))
        {
            return new RestoreResult(false, validationError);
        }

        if (!File.Exists(_statePath))
        {
            return new RestoreResult(
                false,
                "Current state file is missing; safety backup cannot be created.");
        }

        var timestamp = now ?? DateTimeOffset.Now;
        var currentMachine = machineName ?? Environment.MachineName;
        var safetyFileName =
            $"TaskOverlay_{CurrentTaskSpace}_{SanitizeMachineName(currentMachine)}_" +
            $"{timestamp:yyyy-MM-dd_HH-mm}_before-restore.json";
        var safetyMetadata = CreateMetadata(
            safetyFileName,
            currentMachine,
            timestamp.ToUniversalTime(),
            File.GetLastWriteTimeUtc(_statePath));
        var safetyResult = WriteBackupPair(
            configuration,
            safetyFileName,
            safetyMetadata,
            applyRetention: false);
        if (!safetyResult.Succeeded)
        {
            return new RestoreResult(
                false,
                "Safety backup failed; local state was not replaced.");
        }

        var stateDirectory = Path.GetDirectoryName(_statePath);
        if (string.IsNullOrWhiteSpace(stateDirectory))
        {
            return new RestoreResult(false, "Local state path is invalid.");
        }

        var temporaryPath = Path.Combine(
            stateDirectory,
            $"state.restore.{Guid.NewGuid():N}.tmp");
        try
        {
            CopyFileToTemporary(candidate.BackupPath, temporaryPath);
            File.Move(temporaryPath, _statePath, overwrite: true);
            File.SetLastWriteTimeUtc(_statePath, candidate.FreshnessUtc.UtcDateTime);
            Report($"Backup restored from: {candidate.BackupPath}");
            return new RestoreResult(
                true,
                "Backup restored. Restart TaskOverlay to apply restored state.",
                safetyResult.BackupPath);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            Report("Backup restore failed.", ex);
            return new RestoreResult(false, "Backup restore failed.");
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public static string BuildFileName(
        string taskSpace,
        string machineName,
        DateTimeOffset timestamp)
    {
        var safeSpace = SanitizeFileToken(taskSpace, "Work");
        var safeMachine = SanitizeFileToken(machineName, "UnknownMachine");
        return $"TaskOverlay_{safeSpace}_{safeMachine}_{timestamp:yyyy-MM-dd_HH-mm}.json";
    }

    public static string SanitizeMachineName(string? machineName) =>
        SanitizeFileToken(machineName, "UnknownMachine");

    public static IReadOnlyList<string> SelectRetentionFiles(
        IEnumerable<BackupFileInfo> files,
        DateTimeOffset nowUtc,
        int retentionDays,
        int maximumFiles)
    {
        ArgumentNullException.ThrowIfNull(files);
        var cutoff = nowUtc.AddDays(-Math.Clamp(retentionDays, 1, 3650));
        var maximum = Math.Clamp(maximumFiles, 1, 10000);
        return files
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select((file, index) => new { file, index })
            .Where(item =>
                item.index > 0 &&
                (item.index >= maximum ||
                 item.file.LastWriteTimeUtc < cutoff))
            .Select(item => item.file.Path)
            .ToArray();
    }

    private IEnumerable<BackupCandidate> EnumerateBackupCandidates(string folderPath)
    {
        foreach (var path in Directory.EnumerateFiles(
                     folderPath,
                     $"TaskOverlay_{CurrentTaskSpace}_*.json",
                     SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            var match = CandidateNamePattern.Match(fileName);
            if (!match.Success)
            {
                continue;
            }

            var metadataPath = GetMetadataPath(path);
            var metadata = TryReadMetadata(metadataPath, fileName);
            var fileTimestamp = ParseFilenameTimestamp(
                match.Groups["timestamp"].Value);
            var lastWriteTimestamp = new DateTimeOffset(
                File.GetLastWriteTimeUtc(path));
            var backupCreatedAt = metadata?.BackupCreatedAtUtc > DateTimeOffset.MinValue
                ? metadata.BackupCreatedAtUtc
                : fileTimestamp ?? lastWriteTimestamp;
            var freshness = metadata?.StateLastWriteTimeUtc > DateTimeOffset.MinValue
                ? metadata.StateLastWriteTimeUtc
                : metadata?.BackupCreatedAtUtc > DateTimeOffset.MinValue
                    ? metadata.BackupCreatedAtUtc
                    : fileTimestamp ?? lastWriteTimestamp;
            var sourceMachine = !string.IsNullOrWhiteSpace(metadata?.SourceMachine)
                ? metadata.SourceMachine
                : match.Groups["machine"].Value;
            var taskSpace = !string.IsNullOrWhiteSpace(metadata?.TaskSpace)
                ? metadata.TaskSpace
                : CurrentTaskSpace;

            yield return new BackupCandidate(
                path,
                metadata is null ? null : metadataPath,
                freshness,
                backupCreatedAt,
                sourceMachine,
                taskSpace);
        }
    }

    private BackupMetadata? TryReadMetadata(
        string metadataPath,
        string expectedBackupFileName)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<BackupMetadata>(
                File.ReadAllText(metadataPath),
                MetadataJsonOptions);
            return metadata is not null &&
                   metadata.FormatVersion == 1 &&
                   string.Equals(
                       metadata.TaskSpace,
                       CurrentTaskSpace,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       metadata.BackupFileName,
                       expectedBackupFileName,
                       StringComparison.OrdinalIgnoreCase)
                ? metadata
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Report($"Backup metadata is invalid: {metadataPath}", ex);
            return null;
        }
    }

    private static DateTimeOffset? ParseFilenameTimestamp(string value)
    {
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM-dd_HH-mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localTimestamp))
        {
            return null;
        }

        localTimestamp = DateTime.SpecifyKind(localTimestamp, DateTimeKind.Local);
        return new DateTimeOffset(localTimestamp).ToUniversalTime();
    }

    private DateTimeOffset? GetLocalStateTimestamp()
    {
        try
        {
            return File.Exists(_statePath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(_statePath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("Local state timestamp could not be read.", ex);
            return null;
        }
    }

    private BackupMetadata CreateMetadata(
        string backupFileName,
        string machineName,
        DateTimeOffset backupCreatedAtUtc,
        DateTime stateLastWriteTimeUtc)
    {
        return new BackupMetadata
        {
            BackupCreatedAtUtc = backupCreatedAtUtc,
            SourceMachine = SanitizeMachineName(machineName),
            TaskSpace = CurrentTaskSpace,
            StatePath = _statePath,
            StateLastWriteTimeUtc = new DateTimeOffset(
                DateTime.SpecifyKind(stateLastWriteTimeUtc, DateTimeKind.Utc)),
            BackupFileName = backupFileName
        };
    }

    private BackupResult WriteBackupPair(
        BackupConfiguration configuration,
        string fileName,
        BackupMetadata metadata,
        bool applyRetention)
    {
        var finalPath = Path.Combine(configuration.FolderPath, fileName);
        var metadataPath = GetMetadataPath(finalPath);
        var temporaryPath = Path.Combine(
            configuration.FolderPath,
            $".{fileName}.{Guid.NewGuid():N}.tmp");
        var temporaryMetadataPath = $"{temporaryPath}.meta";

        try
        {
            CopyFileToTemporary(_statePath, temporaryPath);
            WriteMetadataToTemporary(metadata, temporaryMetadataPath);
            File.Move(temporaryPath, finalPath, overwrite: true);
            File.Move(temporaryMetadataPath, metadataPath, overwrite: true);
            Report($"Backup succeeded: {finalPath}");
            if (applyRetention)
            {
                TryCleanRetention(
                    configuration,
                    metadata.BackupCreatedAtUtc,
                    metadata.SourceMachine);
            }

            return new BackupResult(
                BackupOutcome.Succeeded,
                $"Backup created: {fileName}",
                finalPath);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return Failure("Backup copy or metadata write failed.", ex);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
            TryDeleteTemporaryFile(temporaryMetadataPath);
        }
    }

    private static void CopyFileToTemporary(string sourcePath, string temporaryPath)
    {
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var destination = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.WriteThrough);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
    }

    private static void WriteMetadataToTemporary(
        BackupMetadata metadata,
        string temporaryPath)
    {
        var json = JsonSerializer.Serialize(metadata, MetadataJsonOptions);
        using var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream);
        writer.Write(json);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static string GetMetadataPath(string backupPath) =>
        Path.Combine(
            Path.GetDirectoryName(backupPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(backupPath)}.meta.json");

    private static bool ValidateStateBackup(string path, out string error)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tasks", out var tasks) ||
                tasks.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("projects", out var projects) ||
                projects.ValueKind != JsonValueKind.Array)
            {
                error = "Backup is not a valid TaskOverlay state file.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            error = $"Backup validation failed: {ex.Message}";
            return false;
        }
    }

    private void TryCleanRetention(
        BackupConfiguration configuration,
        DateTimeOffset nowUtc,
        string machineName)
    {
        try
        {
            var files = Directory
                .EnumerateFiles(configuration.FolderPath, "TaskOverlay_*.json")
                .Where(path => IsBackupForTaskSpaceAndMachine(
                    Path.GetFileName(path),
                    CurrentTaskSpace,
                    machineName))
                .Select(path => new BackupFileInfo(
                    path,
                    File.GetLastWriteTimeUtc(path)))
                .ToArray();

            foreach (var path in SelectRetentionFiles(
                         files,
                         nowUtc,
                         configuration.RetentionDays,
                         configuration.MaximumFiles))
            {
                var metadataPath = GetMetadataPath(path);
                if (File.Exists(metadataPath))
                {
                    File.Delete(metadataPath);
                }

                File.Delete(path);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            Report("Backup retention cleanup failed.", ex);
        }
    }

    private static bool IsBackupForTaskSpaceAndMachine(
        string fileName,
        string taskSpace,
        string machineName)
    {
        var prefix =
            $"TaskOverlay_{SanitizeFileToken(taskSpace, "Work")}_" +
            $"{SanitizeFileToken(machineName, "UnknownMachine")}_";
        return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               BackupTimestampPattern.IsMatch(fileName[prefix.Length..]);
    }

    private static string SanitizeFileToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;
        foreach (var character in value.Trim())
        {
            var allowed = character is >= 'A' and <= 'Z' or
                >= 'a' and <= 'z' or
                >= '0' and <= '9' or
                '-' or '_';
            if (allowed)
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('_');
                previousWasSeparator = true;
            }
        }

        var result = builder.ToString().Trim('_', '-');
        return result.Length == 0 ? fallback : result;
    }

    private BackupResult Failure(string message, Exception? exception = null)
    {
        Report(message, exception);
        return new BackupResult(BackupOutcome.Failed, message);
    }

    private void TryDeleteTemporaryFile(string path)
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
            Report("Backup temporary file cleanup failed.", ex);
        }
    }

    private void Report(string message, Exception? exception = null)
    {
        try
        {
            _diagnostic?.Invoke(message, exception);
        }
        catch
        {
            // Diagnostics must never change backup behavior.
        }
    }
}
