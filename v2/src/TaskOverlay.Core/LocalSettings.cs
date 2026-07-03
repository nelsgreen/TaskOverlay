using System;

namespace TaskOverlay.Core;

public sealed class LocalAppSettings
{
    public BackupSettings Backups { get; set; } = new();
}

public sealed class BackupSettings
{
    public const int DefaultIntervalMinutes = 30;
    public const int DefaultRetentionDays = 14;
    public const int DefaultMaximumFiles = 100;

    public bool Enabled { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public int IntervalMinutes { get; set; } = DefaultIntervalMinutes;
    public int RetentionDays { get; set; } = DefaultRetentionDays;
    public int MaximumFiles { get; set; } = DefaultMaximumFiles;
    public DateTimeOffset? LastBackupAtUtc { get; set; }
    public DateTimeOffset? LastBackupAttemptAtUtc { get; set; }
    public string LastError { get; set; } = string.Empty;

    public bool Normalize()
    {
        var normalizedFolder = FolderPath?.Trim() ?? string.Empty;
        var normalizedError = LastError?.Trim() ?? string.Empty;
        var normalizedInterval = Math.Clamp(IntervalMinutes, 1, 24 * 60);
        var normalizedRetentionDays = Math.Clamp(RetentionDays, 1, 3650);
        var normalizedMaximumFiles = Math.Clamp(MaximumFiles, 1, 10000);
        var changed = FolderPath != normalizedFolder ||
                      LastError != normalizedError ||
                      IntervalMinutes != normalizedInterval ||
                      RetentionDays != normalizedRetentionDays ||
                      MaximumFiles != normalizedMaximumFiles;

        FolderPath = normalizedFolder;
        LastError = normalizedError;
        IntervalMinutes = normalizedInterval;
        RetentionDays = normalizedRetentionDays;
        MaximumFiles = normalizedMaximumFiles;
        return changed;
    }

    public BackupConfiguration Snapshot() =>
        new(
            Enabled,
            FolderPath,
            IntervalMinutes,
            RetentionDays,
            MaximumFiles);
}

public readonly record struct BackupConfiguration(
    bool Enabled,
    string FolderPath,
    int IntervalMinutes,
    int RetentionDays,
    int MaximumFiles);
