using System;

namespace TaskOverlay.Core;

public sealed class LocalAppSettings
{
    public BackupSettings Backups { get; set; } = new();
    public MeetingAssistantSettings MeetingAssistant { get; set; } = new();
}

public enum MeetingTranscriptLanguage
{
    Auto,
    Russian,
    English
}

public sealed class MeetingAssistantSettings
{
    public const string DefaultTranscriptionModel = "gpt-4o-transcribe";
    public const string DefaultAnalysisModel = "gpt-5.6-terra";

    public bool AutomaticRecordingEnabled { get; set; }
    public MeetingRecordingPolicy DefaultRecordingPolicy { get; set; } =
        MeetingRecordingPolicy.Manual;
    public string MicrophoneDeviceId { get; set; } = string.Empty;
    public string SystemOutputDeviceId { get; set; } = string.Empty;
    public bool AutoTranscribeAfterStop { get; set; }
    public string TranscriptionProvider { get; set; } = "OpenAI";
    public string TranscriptionModel { get; set; } = DefaultTranscriptionModel;
    public string AnalysisProvider { get; set; } = "OpenAI";
    public string AnalysisModel { get; set; } = DefaultAnalysisModel;
    public MeetingTranscriptLanguage Language { get; set; } =
        MeetingTranscriptLanguage.Auto;
    public bool ProviderUploadDisclosureAccepted { get; set; }

    public bool Normalize()
    {
        var microphoneDeviceId = MicrophoneDeviceId?.Trim() ?? string.Empty;
        var systemOutputDeviceId = SystemOutputDeviceId?.Trim() ?? string.Empty;
        var transcriptionProvider = NormalizeProvider(TranscriptionProvider);
        var transcriptionModel = NormalizeModel(
            TranscriptionModel,
            DefaultTranscriptionModel);
        var analysisProvider = NormalizeProvider(AnalysisProvider);
        var analysisModel = NormalizeModel(AnalysisModel, DefaultAnalysisModel);
        var defaultPolicy = DefaultRecordingPolicy == MeetingRecordingPolicy.AutoRecord
            ? MeetingRecordingPolicy.AutoRecord
            : MeetingRecordingPolicy.Manual;
        var language = Enum.IsDefined(Language)
            ? Language
            : MeetingTranscriptLanguage.Auto;
        var changed = MicrophoneDeviceId != microphoneDeviceId ||
                      SystemOutputDeviceId != systemOutputDeviceId ||
                      TranscriptionProvider != transcriptionProvider ||
                      TranscriptionModel != transcriptionModel ||
                      AnalysisProvider != analysisProvider ||
                      AnalysisModel != analysisModel ||
                      DefaultRecordingPolicy != defaultPolicy ||
                      Language != language;

        MicrophoneDeviceId = microphoneDeviceId;
        SystemOutputDeviceId = systemOutputDeviceId;
        TranscriptionProvider = transcriptionProvider;
        TranscriptionModel = transcriptionModel;
        AnalysisProvider = analysisProvider;
        AnalysisModel = analysisModel;
        DefaultRecordingPolicy = defaultPolicy;
        Language = language;
        return changed;
    }

    private static string NormalizeProvider(string? value) =>
        string.Equals(value?.Trim(), "OpenAI", StringComparison.OrdinalIgnoreCase)
            ? "OpenAI"
            : "OpenAI";

    private static string NormalizeModel(string? value, string fallback)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= 100 ? normalized : fallback;
    }
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
