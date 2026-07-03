using System;
using System.Windows;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class BackupRestorePromptWindow : Window
{
    private BackupRestorePromptWindow(BackupFolderCheckResult check)
    {
        InitializeComponent();
        var backup = check.LatestBackup ??
                     throw new ArgumentException(
                         "A restore prompt requires a backup candidate.",
                         nameof(check));
        if (check.Status != BackupFreshnessStatus.BackupNewer)
        {
            Title = "Restore backup";
            PromptTitleText.Text = "Restore backup";
            PromptSubtitleText.Text =
                "A Work backup is available in the shared backup folder.";
        }

        LocalNewerWarningText.Visibility =
            check.Status == BackupFreshnessStatus.LocalNewer
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (check.Status == BackupFreshnessStatus.LocalNewer)
        {
            Height = 320;
        }

        LocalStateText.Text = check.LocalStateTimestampUtc is DateTimeOffset local
            ? $"Local state: {local.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "Local state: missing";
        CurrentMachineText.Text = $"This PC: {check.CurrentMachine}";
        BackupStateText.Text =
            $"Backup: {backup.FreshnessUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
        BackupSourceText.Text = $"Source: {backup.SourceMachine}";
    }

    public static bool ShowPrompt(
        BackupFolderCheckResult check,
        Window? owner)
    {
        var window = new BackupRestorePromptWindow(check);
        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        return window.ShowDialog() == true;
    }

    private void RestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void SkipButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
