using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

public sealed class BackupService
{
    public const string CurrentTaskSpace = "Work";

    private static readonly Regex BackupTimestampPattern = new(
        @"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}\.json$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        var finalPath = Path.Combine(configuration.FolderPath, fileName);
        var temporaryPath = Path.Combine(
            configuration.FolderPath,
            $".{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var source = new FileStream(
                       _statePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var destination = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       81920,
                       FileOptions.WriteThrough))
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, finalPath, overwrite: true);
            Report($"Backup succeeded: {finalPath}");
            TryCleanRetention(
                configuration,
                timestamp.ToUniversalTime(),
                currentMachineName);
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
            return Failure("Backup copy failed.", ex);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
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
                item.index >= maximum ||
                item.file.LastWriteTimeUtc < cutoff)
            .Select(item => item.file.Path)
            .ToArray();
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
