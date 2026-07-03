using System;
using System.Threading.Tasks;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed class SettingsWindowActions
{
    public SettingsWindowActions(
        Action<OverlayMode> setOverlayMode,
        Action openLogs,
        Action openStateFolder,
        Action resetWindowPositions,
        Func<BackupSettings> getBackupSettings,
        Action saveBackupSettings,
        Func<string?> chooseBackupFolder,
        Func<bool> openBackupFolder,
        Func<Task<BackupResult>> backupNow,
        Func<Task<BackupFolderCheckResult>> checkBackupFolder,
        Func<Task<RestoreResult>> restoreLatestBackup)
    {
        SetOverlayMode = setOverlayMode;
        OpenLogs = openLogs;
        OpenStateFolder = openStateFolder;
        ResetWindowPositions = resetWindowPositions;
        GetBackupSettings = getBackupSettings;
        SaveBackupSettings = saveBackupSettings;
        ChooseBackupFolder = chooseBackupFolder;
        OpenBackupFolder = openBackupFolder;
        BackupNow = backupNow;
        CheckBackupFolder = checkBackupFolder;
        RestoreLatestBackup = restoreLatestBackup;
    }

    public Action<OverlayMode> SetOverlayMode { get; }
    public Action OpenLogs { get; }
    public Action OpenStateFolder { get; }
    public Action ResetWindowPositions { get; }
    public Func<BackupSettings> GetBackupSettings { get; }
    public Action SaveBackupSettings { get; }
    public Func<string?> ChooseBackupFolder { get; }
    public Func<bool> OpenBackupFolder { get; }
    public Func<Task<BackupResult>> BackupNow { get; }
    public Func<Task<BackupFolderCheckResult>> CheckBackupFolder { get; }
    public Func<Task<RestoreResult>> RestoreLatestBackup { get; }
}
