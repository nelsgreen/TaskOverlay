using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TaskOverlay.Core;

namespace TaskOverlay.App;

internal sealed record SettingsHotkeyItem(string Label, string Key);
internal sealed record TelegramProjectOption(Guid Id, string Name);
internal sealed record MeetingRecordingPolicyOption(
    MeetingRecordingPolicy Value,
    string Label);
internal sealed record MeetingRecordingFormatOption(
    MeetingRecordingFormat Value,
    string Label);
internal sealed record MeetingLanguageOption(
    MeetingTranscriptLanguage Value,
    string Label);

public partial class SettingsView : UserControl
{
    private static readonly IReadOnlyList<GlobalHotkeyCommand> HotkeyOrder =
        new[]
        {
            GlobalHotkeyCommand.CycleOverlayMode,
            GlobalHotkeyCommand.CreateTaskWithDescription,
            GlobalHotkeyCommand.QuickAddTask,
            GlobalHotkeyCommand.CollapseOrToggleOverlay,
            GlobalHotkeyCommand.OpenSettings,
            GlobalHotkeyCommand.OpenTreeManager,
            GlobalHotkeyCommand.ToggleWorkspace
        };

    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly Action _settingsChanged;
    private readonly SettingsWindowActions _actions;
    private readonly Action _closeShell;
    private bool _updatingControls;
    private bool _workingSettingsDirty;
    private BackupFolderCheckResult? _backupFolderCheck;
    private string _checkedBackupFolder = string.Empty;
    private DispatcherTimer? _telegramStatusTimer;

    public SettingsView(
        AppState state,
        Action saveState,
        Action settingsChanged,
        SettingsWindowActions actions,
        Action closeShell)
    {
        _state = state;
        _saveState = saveState;
        _settingsChanged = settingsChanged;
        _actions = actions;
        _closeShell = closeShell;

        _updatingControls = true;
        InitializeComponent();
        ModeListBox.ItemsSource = OverlayModeDisplay.UserModes;
        HotkeyItems.ItemsSource = BuildHotkeyItems();
        MeetingRecordingPolicyComboBox.ItemsSource =
            new[]
            {
                new MeetingRecordingPolicyOption(
                    MeetingRecordingPolicy.Manual,
                    "Manual"),
                new MeetingRecordingPolicyOption(
                    MeetingRecordingPolicy.AutoRecord,
                    "Auto-record")
            };
        MeetingRecordingFormatComboBox.ItemsSource =
            new[]
            {
                new MeetingRecordingFormatOption(
                    MeetingRecordingFormat.AacM4a,
                    "Compact - AAC/M4A (recommended)"),
                new MeetingRecordingFormatOption(
                    MeetingRecordingFormat.Wav,
                    "Lossless - WAV (large files)")
            };
        MeetingLanguageComboBox.ItemsSource =
            new[]
            {
                new MeetingLanguageOption(MeetingTranscriptLanguage.Auto, "Auto"),
                new MeetingLanguageOption(MeetingTranscriptLanguage.Russian, "Russian"),
                new MeetingLanguageOption(MeetingTranscriptLanguage.English, "English")
            };
        _updatingControls = false;
        UpdateFromSettings();
    }

    public void OnActivated()
    {
        UpdateFromSettings();
        if (!IsKeyboardFocusWithin)
        {
            ModeListBox.Focus();
        }

        StartTelegramStatusTimer();
    }

    public void OnDeactivated()
    {
        CommitWorkingPresentationSettings();
        StopTelegramStatusTimer();
    }

    private void StartTelegramStatusTimer()
    {
        if (_telegramStatusTimer is not null)
        {
            return;
        }

        _telegramStatusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _telegramStatusTimer.Tick += (_, _) => RefreshTelegramRuntimeStatus();
        _telegramStatusTimer.Start();
    }

    private void StopTelegramStatusTimer()
    {
        if (_telegramStatusTimer is null)
        {
            return;
        }

        _telegramStatusTimer.Stop();
        _telegramStatusTimer = null;
    }

    public void UpdateFromSettings()
    {
        _updatingControls = true;
        try
        {
            ModeListBox.SelectedItem = OverlayModeDisplay.UserModes
                .First(option => option.Mode == _state.OverlaySettings.OverlayMode);
            WorkingIdleFontSizeSlider.Value =
                _state.OverlaySettings.WorkingIdleFontSize;
            WorkingActiveFontSizeSlider.Value =
                _state.OverlaySettings.WorkingActiveFontSize;
            WorkingWindowWidthSlider.Value =
                _state.OverlaySettings.WorkingWindowWidth;
            WorkingWindowHeightSlider.Value =
                _state.OverlaySettings.WorkingWindowHeight;
            UpdateValueLabels();
            UpdateBackupControls();
            UpdateTelegramControls();
            UpdateMeetingAssistantControls();
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void ModeListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingControls ||
            ModeListBox.SelectedItem is not OverlayModeDisplayOption selected ||
            selected.Mode == _state.OverlaySettings.OverlayMode)
        {
            return;
        }

        _actions.SetOverlayMode(selected.Mode);
        UpdateFromSettings();
    }

    private void SettingsSlider_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyWorkingPresentationSettings(commit: false);
    }

    private void SettingsSlider_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        ApplyWorkingPresentationSettings(commit: true);
    }

    private void SettingsSlider_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        ApplyWorkingPresentationSettings(commit: true);
    }

    private void SettingsSlider_OnKeyUp(object sender, KeyEventArgs e)
    {
        ApplyWorkingPresentationSettings(commit: true);
    }

    private void ApplyWorkingPresentationSettings(bool commit)
    {
        if (_updatingControls)
        {
            return;
        }

        var settings = _state.OverlaySettings;
        var idleFontSize = OverlaySettings.ClampWorkingIdleFontSize(
            WorkingIdleFontSizeSlider.Value);
        var activeFontSize = OverlaySettings.ClampWorkingActiveFontSize(
            WorkingActiveFontSizeSlider.Value);
        var windowWidth = OverlaySettings.ClampWorkingWindowWidth(
            WorkingWindowWidthSlider.Value);
        var windowHeight = OverlaySettings.ClampWorkingWindowHeight(
            WorkingWindowHeightSlider.Value);
        var changed = settings.WorkingIdleFontSize != idleFontSize ||
                      settings.WorkingActiveFontSize != activeFontSize ||
                      settings.WorkingWindowWidth != windowWidth ||
                      settings.WorkingWindowHeight != windowHeight;

        settings.WorkingIdleFontSize = idleFontSize;
        settings.WorkingActiveFontSize = activeFontSize;
        settings.WorkingWindowWidth = windowWidth;
        settings.WorkingWindowHeight = windowHeight;

        if (changed)
        {
            _workingSettingsDirty = true;
            _settingsChanged();
        }

        UpdateValueLabels();
        if (commit)
        {
            CommitWorkingPresentationSettings();
        }
    }

    private void CommitWorkingPresentationSettings()
    {
        if (!_workingSettingsDirty)
        {
            return;
        }

        _saveState();
        _workingSettingsDirty = false;
    }

    private void UpdateValueLabels()
    {
        WorkingIdleFontSizeValue.Text = FormatPixels(WorkingIdleFontSizeSlider.Value);
        WorkingActiveFontSizeValue.Text = FormatPixels(WorkingActiveFontSizeSlider.Value);
        WorkingWindowWidthValue.Text = FormatPixels(WorkingWindowWidthSlider.Value);
        WorkingWindowHeightValue.Text = FormatPixels(WorkingWindowHeightSlider.Value);
    }

    private void OpenLogsButton_OnClick(object sender, RoutedEventArgs e)
    {
        _actions.OpenLogs();
        DiagnosticsStatusText.Text = "Opened the logs folder.";
    }

    private void OpenStateFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        _actions.OpenStateFolder();
        DiagnosticsStatusText.Text = "Opened the state folder.";
    }

    private void BackupsEnabledCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        _actions.GetBackupSettings().Enabled =
            BackupsEnabledCheckBox.IsChecked == true;
        _actions.SaveBackupSettings();
        UpdateBackupControls();
    }

    private void ChooseBackupFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedPath = _actions.ChooseBackupFolder();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        var settings = _actions.GetBackupSettings();
        settings.FolderPath = selectedPath;
        settings.LastError = string.Empty;
        _backupFolderCheck = null;
        _checkedBackupFolder = string.Empty;
        _actions.SaveBackupSettings();
        UpdateBackupControls();
    }

    private void OpenBackupFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        BackupStatusText.Text = _actions.OpenBackupFolder()
            ? "Opened the backup folder."
            : "Backup folder is missing or unavailable.";
    }

    private async void BackupNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        BackupNowButton.IsEnabled = false;
        BackupStatusText.Text = "Creating backup...";
        try
        {
            var result = await _actions.BackupNow();
            UpdateBackupControls();
            if (!result.Succeeded)
            {
                BackupStatusText.Text = result.Message;
            }
            else
            {
                await CheckBackupFolderCoreAsync();
            }
        }
        finally
        {
            BackupNowButton.IsEnabled =
                !string.IsNullOrWhiteSpace(
                    _actions.GetBackupSettings().FolderPath);
        }
    }

    private async void CheckBackupFolderButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        await CheckBackupFolderCoreAsync();
    }

    private async System.Threading.Tasks.Task CheckBackupFolderCoreAsync()
    {
        CheckBackupFolderButton.IsEnabled = false;
        BackupFreshnessText.Text = "Checking backup folder...";
        try
        {
            _backupFolderCheck = await _actions.CheckBackupFolder();
            _checkedBackupFolder = _actions.GetBackupSettings().FolderPath;
            UpdateBackupFreshnessDisplay();
        }
        finally
        {
            CheckBackupFolderButton.IsEnabled = true;
        }
    }

    private async void RestoreLatestBackupButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_backupFolderCheck?.LatestBackup is null ||
            !BackupRestorePromptWindow.ShowPrompt(
                _backupFolderCheck,
                Window.GetWindow(this)))
        {
            return;
        }

        RestoreLatestBackupButton.IsEnabled = false;
        BackupFreshnessText.Text = "Restoring latest backup...";
        var result = await _actions.RestoreLatestBackup();
        if (!result.Succeeded)
        {
            BackupFreshnessText.Text = result.Message;
            RestoreLatestBackupButton.IsEnabled = true;
            return;
        }

        MessageBox.Show(
            Window.GetWindow(this),
            result.Message,
            "TaskOverlay backup restore",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Application.Current.Shutdown();
    }

    private void UpdateBackupControls()
    {
        if (BackupsEnabledCheckBox is null)
        {
            return;
        }

        var settings = _actions.GetBackupSettings();
        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            BackupsEnabledCheckBox.IsChecked = settings.Enabled;
            BackupFolderTextBox.Text = settings.FolderPath;
            var hasFolder = !string.IsNullOrWhiteSpace(settings.FolderPath);
            OpenBackupFolderButton.IsEnabled = hasFolder;
            BackupNowButton.IsEnabled = hasFolder;
            CheckBackupFolderButton.IsEnabled = true;
            BackupPolicyText.Text =
                $"Automatic backups run every {settings.IntervalMinutes} minutes. " +
                $"Keeps {settings.RetentionDays} days and at most " +
                $"{settings.MaximumFiles} Work backups.";

            BackupStatusText.Text = !string.IsNullOrWhiteSpace(settings.LastError)
                ? $"Last error: {settings.LastError}"
                : settings.LastBackupAtUtc is DateTimeOffset lastBackup
                    ? $"Last backup: {lastBackup.ToLocalTime():yyyy-MM-dd HH:mm}"
                    : hasFolder
                        ? "No backup has been created yet."
                        : "Choose a local folder to enable backups.";
            if (!string.Equals(
                    _checkedBackupFolder,
                    settings.FolderPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _backupFolderCheck = null;
            }

            UpdateBackupFreshnessDisplay();
        }
        finally
        {
            _updatingControls = wasUpdating;
        }
    }

    private void UpdateBackupFreshnessDisplay()
    {
        var check = _backupFolderCheck;
        if (check is null)
        {
            BackupLocalStateText.Text = "Local state: not checked";
            BackupLatestStateText.Text = "Latest backup: not checked";
            BackupFreshnessText.Text = "Click Check backup folder to compare.";
            BackupRestoreWarningText.Visibility = Visibility.Collapsed;
            RestoreLatestBackupButton.IsEnabled = false;
            return;
        }

        BackupLocalStateText.Text = check.LocalStateTimestampUtc is DateTimeOffset local
            ? $"Local state: {local.ToLocalTime():yyyy-MM-dd HH:mm}, {check.CurrentMachine}"
            : $"Local state: missing, {check.CurrentMachine}";
        BackupLatestStateText.Text = check.LatestBackup is BackupCandidate backup
            ? $"Latest backup: {backup.FreshnessUtc.ToLocalTime():yyyy-MM-dd HH:mm}, " +
              $"{backup.SourceMachine}, {backup.TaskSpace}"
            : "Latest backup: none";
        BackupFreshnessText.Text = $"Status: {check.Message}";
        BackupRestoreWarningText.Visibility =
            check.Status == BackupFreshnessStatus.LocalNewer
                ? Visibility.Visible
                : Visibility.Collapsed;
        RestoreLatestBackupButton.IsEnabled =
            check.LatestBackup is not null;
    }

    private void TelegramSettings_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        CommitTelegramSettings();
    }

    private void TelegramSettings_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        CommitTelegramSettings();
    }

    private void TelegramDefaultProjectComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        CommitTelegramSettings();
    }

    private void UpdateTelegramControls()
    {
        if (TelegramEnabledCheckBox is null)
        {
            return;
        }

        var settings = _actions.GetTelegramSettings();
        var projects = _state.Projects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .Select(project => new TelegramProjectOption(project.Id, project.Name))
            .ToArray();

        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            TelegramDefaultProjectComboBox.ItemsSource = projects;
            TelegramEnabledCheckBox.IsChecked = settings.Enabled;
            TelegramBotUsernameTextBox.Text = settings.BotUsername;
            TelegramAllowedUserIdTextBox.Text = settings.AllowedUserId?.ToString() ?? string.Empty;
            TelegramDefaultProjectComboBox.SelectedValue = settings.DefaultProjectId;
            TelegramAliasesTextBox.Text = FormatTelegramAliases(settings.ProjectAliases);
            TelegramTokenPasswordBox.Clear();
            TelegramTokenPasswordBox.Password = string.Empty;
            TelegramStatusText.Text = _actions.HasTelegramToken()
                ? "Token saved. Enter a new token only when replacing it."
                : "No bot token saved.";
        }
        finally
        {
            _updatingControls = wasUpdating;
        }

        RefreshTelegramRuntimeStatus();
    }

    private void RefreshTelegramRuntimeStatus()
    {
        if (TelegramRuntimeStatusText is null)
        {
            return;
        }

        var diagnostics = _actions.GetTelegramDiagnostics();
        TelegramRuntimeStatusText.Text = $"Status: {DescribeStatus(diagnostics.Kind)}";

        var detailLines = new List<string>
        {
            $"Last poll: {FormatTimestamp(diagnostics.LastPollStartedUtc)}",
            $"Last success: {FormatTimestamp(diagnostics.LastSuccessfulPollUtc)}",
            $"Last captured message: {FormatTimestamp(diagnostics.LastCapturedMessageUtc)}",
            $"Last processed update id: {(diagnostics.LastProcessedUpdateId > 0 ? diagnostics.LastProcessedUpdateId.ToString() : "none yet")}"
        };
        TelegramRuntimeDetailText.Text = string.Join("   |   ", detailLines);

        if (diagnostics.ConsecutiveErrorCount > 0 && !string.IsNullOrWhiteSpace(diagnostics.LastErrorSummary))
        {
            TelegramRuntimeErrorText.Text =
                $"Last error ({diagnostics.ConsecutiveErrorCount} in a row): {diagnostics.LastErrorSummary}";
            TelegramRuntimeErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            TelegramRuntimeErrorText.Text = string.Empty;
            TelegramRuntimeErrorText.Visibility = Visibility.Collapsed;
        }
    }

    private static string DescribeStatus(TelegramCaptureStatusKind kind) => kind switch
    {
        TelegramCaptureStatusKind.NotConfigured => "Not configured (missing token or allowed user id)",
        TelegramCaptureStatusKind.Disabled => "Disabled",
        TelegramCaptureStatusKind.Running => "Running",
        TelegramCaptureStatusKind.WaitingForMessages => "Running - waiting for messages",
        TelegramCaptureStatusKind.TokenError => "Token error - check the bot token",
        TelegramCaptureStatusKind.NetworkError => "Network error - retrying",
        TelegramCaptureStatusKind.Error => "Error - see details below",
        _ => "Unknown"
    };

    private static string FormatTimestamp(DateTimeOffset? value) =>
        value is DateTimeOffset timestamp
            ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "never (this session)";

    private async void PollTelegramNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        PollTelegramNowButton.IsEnabled = false;
        TelegramRuntimeStatusText.Text = "Status: checking now...";
        try
        {
            var result = await _actions.PollTelegramNow();
            RefreshTelegramRuntimeStatus();
            TelegramStatusText.Text = result.Message;
        }
        finally
        {
            PollTelegramNowButton.IsEnabled = true;
        }
    }

    private void OpenTelegramContextHubButton_OnClick(object sender, RoutedEventArgs e)
    {
        _actions.OpenContextHub();
    }

    private void CommitTelegramSettings()
    {
        var settings = _actions.GetTelegramSettings();
        settings.Enabled = TelegramEnabledCheckBox.IsChecked == true;
        settings.BotUsername = TelegramBotUsernameTextBox.Text;
        settings.AllowedUserId = ParseAllowedTelegramUserId(
            TelegramAllowedUserIdTextBox.Text);
        settings.DefaultProjectId =
            TelegramDefaultProjectComboBox.SelectedValue is Guid projectId
                ? projectId
                : null;
        settings.ProjectAliases = ParseTelegramAliases(
            TelegramAliasesTextBox.Text,
            out var aliasWarning);
        settings.Normalize(_state.Projects);
        _actions.SaveTelegramSettings();
        TelegramStatusText.Text = string.IsNullOrWhiteSpace(aliasWarning)
            ? "Telegram Capture settings saved."
            : aliasWarning;
    }

    private void UpdateTelegramTokenButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var token = TelegramTokenPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(token))
        {
            TelegramStatusText.Text = "Enter a bot token before updating token storage.";
            return;
        }

        if (_actions.SaveTelegramToken(token))
        {
            TelegramTokenPasswordBox.Clear();
            TelegramStatusText.Text = "Bot token saved with Windows user protection.";
        }
        else
        {
            TelegramStatusText.Text = "Bot token could not be saved.";
        }
    }

    private void ClearTelegramTokenButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        TelegramTokenPasswordBox.Clear();
        TelegramStatusText.Text = _actions.ClearTelegramToken()
            ? "Bot token cleared."
            : "Bot token could not be cleared.";
    }

    private async void TestTelegramConnectionButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        TestTelegramConnectionButton.IsEnabled = false;
        TelegramStatusText.Text = "Testing Telegram bot connection...";
        try
        {
            var result = await _actions.TestTelegramConnection();
            TelegramStatusText.Text = result.Message;
        }
        finally
        {
            TestTelegramConnectionButton.IsEnabled = true;
        }
    }

    private void ResetWindowPositionsButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        _actions.ResetWindowPositions();
        DiagnosticsStatusText.Text =
            "Saved window positions were cleared for the next placement.";
    }

    private void MeetingAssistantSettings_OnChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (!_updatingControls)
        {
            CommitMeetingAssistantSettings();
        }
    }

    private void MeetingAssistantSettings_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_updatingControls)
        {
            CommitMeetingAssistantSettings();
        }
    }

    private void MeetingAssistantSettings_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (!_updatingControls)
        {
            CommitMeetingAssistantSettings();
        }
    }

    private void OpenMeetingRecordingsFolderButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        MeetingAssistantStatusText.Text = _actions.OpenMeetingRecordingsFolder()
            ? "Opened the local recordings folder."
            : "The recordings folder could not be opened.";
    }

    private void UpdateOpenAiApiKeyButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var key = OpenAiApiKeyPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            MeetingAssistantStatusText.Text =
                "Enter an API key before choosing Update key.";
            return;
        }

        MeetingAssistantStatusText.Text = _actions.SaveOpenAiApiKey(key)
            ? "OpenAI API key saved with Windows user protection."
            : "OpenAI API key could not be saved.";
        OpenAiApiKeyPasswordBox.Clear();
        UpdateMeetingAssistantKeyStatus();
    }

    private void ClearOpenAiApiKeyButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        MeetingAssistantStatusText.Text = _actions.ClearOpenAiApiKey()
            ? "OpenAI API key removed."
            : "OpenAI API key could not be removed.";
        OpenAiApiKeyPasswordBox.Clear();
        UpdateMeetingAssistantKeyStatus();
    }

    private void UpdateMeetingAssistantControls()
    {
        if (AutomaticMeetingRecordingCheckBox is null)
        {
            return;
        }

        var settings = _actions.GetMeetingAssistantSettings();
        var microphones = BuildAudioDeviceOptions(
            _actions.GetMicrophoneDevices(),
            "Default microphone");
        var outputs = BuildAudioDeviceOptions(
            _actions.GetSystemOutputDevices(),
            "Default system output");
        var wasUpdating = _updatingControls;
        _updatingControls = true;
        try
        {
            AutomaticMeetingRecordingCheckBox.IsChecked =
                settings.AutomaticRecordingEnabled;
            AutoTranscribeMeetingCheckBox.IsChecked =
                settings.AutoTranscribeAfterStop;
            MeetingRecordingPolicyComboBox.SelectedValue =
                settings.DefaultRecordingPolicy;
            MeetingRecordingFormatComboBox.SelectedValue = settings.RecordingFormat;
            MeetingRecordingFormatComboBox.IsEnabled =
                !_actions.IsMeetingRecordingActive();
            MeetingMicrophoneComboBox.ItemsSource = microphones;
            MeetingMicrophoneComboBox.SelectedValue =
                microphones.Any(item => item.Id == settings.MicrophoneDeviceId)
                    ? settings.MicrophoneDeviceId
                    : string.Empty;
            MeetingSystemOutputComboBox.ItemsSource = outputs;
            MeetingSystemOutputComboBox.SelectedValue =
                outputs.Any(item => item.Id == settings.SystemOutputDeviceId)
                    ? settings.SystemOutputDeviceId
                    : string.Empty;
            MeetingTranscriptionModelTextBox.Text = settings.TranscriptionModel;
            MeetingAnalysisModelTextBox.Text = settings.AnalysisModel;
            MeetingLanguageComboBox.SelectedValue = settings.Language;
            OpenAiApiKeyPasswordBox.Clear();
            UpdateMeetingAssistantKeyStatus();
        }
        finally
        {
            _updatingControls = wasUpdating;
        }
    }

    private void CommitMeetingAssistantSettings()
    {
        var settings = _actions.GetMeetingAssistantSettings();
        var recordingFormatChangeBlocked = false;
        settings.AutomaticRecordingEnabled =
            AutomaticMeetingRecordingCheckBox.IsChecked == true;
        settings.AutoTranscribeAfterStop =
            AutoTranscribeMeetingCheckBox.IsChecked == true;
        if (MeetingRecordingPolicyComboBox.SelectedValue is
            MeetingRecordingPolicy policy)
        {
            settings.DefaultRecordingPolicy = policy;
        }

        if (MeetingRecordingFormatComboBox.SelectedValue is
            MeetingRecordingFormat recordingFormat)
        {
            if (_actions.IsMeetingRecordingActive() &&
                recordingFormat != settings.RecordingFormat)
            {
                var wasUpdating = _updatingControls;
                _updatingControls = true;
                MeetingRecordingFormatComboBox.SelectedValue = settings.RecordingFormat;
                _updatingControls = wasUpdating;
                recordingFormatChangeBlocked = true;
            }
            else
            {
                settings.RecordingFormat = recordingFormat;
            }
        }

        settings.MicrophoneDeviceId =
            MeetingMicrophoneComboBox.SelectedValue as string ?? string.Empty;
        settings.SystemOutputDeviceId =
            MeetingSystemOutputComboBox.SelectedValue as string ?? string.Empty;
        settings.TranscriptionModel = MeetingTranscriptionModelTextBox.Text;
        settings.AnalysisModel = MeetingAnalysisModelTextBox.Text;
        if (MeetingLanguageComboBox.SelectedValue is
            MeetingTranscriptLanguage language)
        {
            settings.Language = language;
        }

        settings.Normalize();
        _actions.SaveMeetingAssistantSettings();
        MeetingAssistantStatusText.Text = recordingFormatChangeBlocked
            ? "Recording format cannot change while a recording is active. Other settings were saved."
            : "Meeting Assistant settings saved.";
    }

    private void UpdateMeetingAssistantKeyStatus()
    {
        OpenAiApiKeyStatusText.Text = _actions.HasOpenAiApiKey()
            ? "API key saved. Enter a new key only when replacing it."
            : "No OpenAI API key saved.";
    }

    private static IReadOnlyList<AudioDeviceDescriptor> BuildAudioDeviceOptions(
        IReadOnlyList<AudioDeviceDescriptor> devices,
        string defaultLabel)
    {
        var options = new List<AudioDeviceDescriptor>
        {
            new(string.Empty, defaultLabel, true)
        };
        options.AddRange(devices.Where(device =>
            !string.IsNullOrWhiteSpace(device.Id)));
        return options;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        _closeShell();
    }

    private static IReadOnlyList<SettingsHotkeyItem> BuildHotkeyItems()
    {
        return HotkeyOrder
            .Select(command => GlobalHotkeyBindings.All.Single(item =>
                item.Command == command))
            .Select(binding => new SettingsHotkeyItem(
                GetHotkeyLabel(binding.Command),
                binding.DisplayName.Split('+')[^1]))
            .ToArray();
    }

    private static string GetHotkeyLabel(GlobalHotkeyCommand command)
    {
        return command switch
        {
            GlobalHotkeyCommand.CycleOverlayMode => "Cycle overlay mode",
            GlobalHotkeyCommand.CreateTaskWithDescription =>
                "Create one task from clipboard with description",
            GlobalHotkeyCommand.QuickAddTask => "Toggle Quick Add",
            GlobalHotkeyCommand.OpenSettings => "Toggle Settings",
            GlobalHotkeyCommand.OpenTreeManager => "Toggle Tree Manager",
            GlobalHotkeyCommand.ToggleWorkspace => "Toggle Workspace",
            _ => "Collapse / show overlay"
        };
    }

    private static string FormatPixels(double value)
    {
        return $"{value:0} px";
    }

    private static long? ParseAllowedTelegramUserId(string value)
    {
        return long.TryParse(value.Trim(), out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    private List<TelegramProjectAlias> ParseTelegramAliases(
        string value,
        out string warning)
    {
        var aliases = new List<TelegramProjectAlias>();
        var warnings = new List<string>();
        var projectsByName = _state.Projects.ToDictionary(
            project => project.Name,
            StringComparer.OrdinalIgnoreCase);
        var projectsById = _state.Projects.ToDictionary(project => project.Id);
        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in value.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('=', 2);
            if (parts.Length != 2)
            {
                warnings.Add($"Ignored alias line without '=': {line}");
                continue;
            }

            var alias = parts[0].Trim();
            var projectValue = parts[1].Trim();
            if (alias.Length == 0 || !seenAliases.Add(alias))
            {
                warnings.Add($"Ignored duplicate or empty alias: {line}");
                continue;
            }

            ProjectItem? project = null;
            if (Guid.TryParse(projectValue, out var projectId))
            {
                projectsById.TryGetValue(projectId, out project);
            }

            project ??= projectsByName.TryGetValue(projectValue, out var namedProject)
                ? namedProject
                : null;
            if (project is null)
            {
                warnings.Add($"Ignored alias with unknown project: {line}");
                continue;
            }

            aliases.Add(new TelegramProjectAlias
            {
                Alias = alias,
                ProjectId = project.Id
            });
        }

        warning = warnings.Count == 0
            ? string.Empty
            : string.Join(" ", warnings);
        return aliases;
    }

    private string FormatTelegramAliases(
        IEnumerable<TelegramProjectAlias> aliases)
    {
        var projects = _state.Projects.ToDictionary(project => project.Id);
        return string.Join(
            Environment.NewLine,
            aliases.Select(alias =>
            {
                var projectLabel = projects.TryGetValue(alias.ProjectId, out var project)
                    ? project.Name
                    : alias.ProjectId.ToString();
                return $"{alias.Alias} = {projectLabel}";
            }));
    }
}
