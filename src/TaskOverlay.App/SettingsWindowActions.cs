using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed class SettingsWindowActions
{
    public SettingsWindowActions(
        Action<OverlayMode> setOverlayMode,
        Action saveWorkingHoursSettings,
        Action saveWorkspaceAppearanceSettings,
        Action openLogs,
        Action openStateFolder,
        Action resetWindowPositions,
        Func<BackupSettings> getBackupSettings,
        Action saveBackupSettings,
        Func<string?> chooseBackupFolder,
        Func<bool> openBackupFolder,
        Func<Task<BackupResult>> backupNow,
        Func<Task<BackupFolderCheckResult>> checkBackupFolder,
        Func<Task<RestoreResult>> restoreLatestBackup,
        Func<TelegramCaptureSettings> getTelegramSettings,
        Action saveTelegramSettings,
        Func<bool> hasTelegramToken,
        Func<string, bool> saveTelegramToken,
        Func<bool> clearTelegramToken,
        Func<Task<TelegramConnectionTestResult>> testTelegramConnection,
        Func<TelegramCaptureDiagnostics> getTelegramDiagnostics,
        Func<Task<TelegramPollNowResult>> pollTelegramNow,
        Action openContextHub,
        Func<MeetingAssistantSettings> getMeetingAssistantSettings,
        Action saveMeetingAssistantSettings,
        Func<IReadOnlyList<AudioDeviceDescriptor>> getMicrophoneDevices,
        Func<IReadOnlyList<AudioDeviceDescriptor>> getSystemOutputDevices,
        Func<bool> isMeetingRecordingActive,
        Func<bool> openMeetingRecordingsFolder,
        Func<bool> hasOpenAiApiKey,
        Func<string, bool> saveOpenAiApiKey,
        Func<bool> clearOpenAiApiKey)
    {
        SetOverlayMode = setOverlayMode;
        SaveWorkingHoursSettings = saveWorkingHoursSettings;
        SaveWorkspaceAppearanceSettings = saveWorkspaceAppearanceSettings;
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
        GetTelegramSettings = getTelegramSettings;
        SaveTelegramSettings = saveTelegramSettings;
        HasTelegramToken = hasTelegramToken;
        SaveTelegramToken = saveTelegramToken;
        ClearTelegramToken = clearTelegramToken;
        TestTelegramConnection = testTelegramConnection;
        GetTelegramDiagnostics = getTelegramDiagnostics;
        PollTelegramNow = pollTelegramNow;
        OpenContextHub = openContextHub;
        GetMeetingAssistantSettings = getMeetingAssistantSettings;
        SaveMeetingAssistantSettings = saveMeetingAssistantSettings;
        GetMicrophoneDevices = getMicrophoneDevices;
        GetSystemOutputDevices = getSystemOutputDevices;
        IsMeetingRecordingActive = isMeetingRecordingActive;
        OpenMeetingRecordingsFolder = openMeetingRecordingsFolder;
        HasOpenAiApiKey = hasOpenAiApiKey;
        SaveOpenAiApiKey = saveOpenAiApiKey;
        ClearOpenAiApiKey = clearOpenAiApiKey;
    }

    public Action<OverlayMode> SetOverlayMode { get; }
    public Action SaveWorkingHoursSettings { get; }
    public Action SaveWorkspaceAppearanceSettings { get; }
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
    public Func<TelegramCaptureSettings> GetTelegramSettings { get; }
    public Action SaveTelegramSettings { get; }
    public Func<bool> HasTelegramToken { get; }
    public Func<string, bool> SaveTelegramToken { get; }
    public Func<bool> ClearTelegramToken { get; }
    public Func<Task<TelegramConnectionTestResult>> TestTelegramConnection { get; }
    public Func<TelegramCaptureDiagnostics> GetTelegramDiagnostics { get; }
    public Func<Task<TelegramPollNowResult>> PollTelegramNow { get; }
    public Action OpenContextHub { get; }
    public Func<MeetingAssistantSettings> GetMeetingAssistantSettings { get; }
    public Action SaveMeetingAssistantSettings { get; }
    public Func<IReadOnlyList<AudioDeviceDescriptor>> GetMicrophoneDevices { get; }
    public Func<IReadOnlyList<AudioDeviceDescriptor>> GetSystemOutputDevices { get; }
    public Func<bool> IsMeetingRecordingActive { get; }
    public Func<bool> OpenMeetingRecordingsFolder { get; }
    public Func<bool> HasOpenAiApiKey { get; }
    public Func<string, bool> SaveOpenAiApiKey { get; }
    public Func<bool> ClearOpenAiApiKey { get; }
}
