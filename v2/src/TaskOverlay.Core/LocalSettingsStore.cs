using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskOverlay.Core;

public sealed class LocalSettingsStore
{
    private const string SettingsFileName = "localSettings.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly Action<string, Exception?>? _diagnostic;

    public LocalSettingsStore(
        string stateDirectory,
        Action<string, Exception?>? diagnostic = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        StateDirectory = stateDirectory;
        _diagnostic = diagnostic;
    }

    public string StateDirectory { get; }
    public string SettingsPath => Path.Combine(StateDirectory, SettingsFileName);

    public LocalAppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new LocalAppSettings();
            }

            var settings = JsonSerializer.Deserialize<LocalAppSettings>(
                               File.ReadAllText(SettingsPath),
                               _jsonOptions) ??
                           new LocalAppSettings();
            settings.Backups ??= new BackupSettings();
            settings.MeetingAssistant ??= new MeetingAssistantSettings();
            if (settings.Backups.Normalize() |
                settings.MeetingAssistant.Normalize())
            {
                Save(settings);
            }

            return settings;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            Report("Local settings load failed; using defaults.", ex);
            return new LocalAppSettings();
        }
    }

    public void Save(LocalAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Backups ??= new BackupSettings();
        settings.MeetingAssistant ??= new MeetingAssistantSettings();
        settings.Backups.Normalize();
        settings.MeetingAssistant.Normalize();
        Directory.CreateDirectory(StateDirectory);

        var temporaryPath = Path.Combine(
            StateDirectory,
            $"{SettingsFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
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
            // Diagnostics must never change settings behavior.
        }
    }
}
