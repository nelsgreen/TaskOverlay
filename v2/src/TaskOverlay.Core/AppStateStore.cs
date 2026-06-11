using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskOverlay.Core;

public sealed class AppStateStore
{
    private const string StateFileName = "state.json";
    private const string BackupFileName = "state.backup.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public AppStateStore(string? stateDirectory = null)
    {
        StateDirectory = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskOverlayV2");
    }

    public string StateDirectory { get; }
    public string StatePath => Path.Combine(StateDirectory, StateFileName);
    public string BackupPath => Path.Combine(StateDirectory, BackupFileName);

    public AppState Load()
    {
        Directory.CreateDirectory(StateDirectory);

        if (!File.Exists(StatePath))
        {
            var defaultState = AppState.CreateDefault();
            TrySave(defaultState);
            return defaultState;
        }

        try
        {
            var json = File.ReadAllText(StatePath);
            var state = JsonSerializer.Deserialize<AppState>(json, _jsonOptions);
            Validate(state);
            return state!;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            TryBackupCorruptedState();
            var defaultState = AppState.CreateDefault();
            TrySave(defaultState);
            return defaultState;
        }
    }

    public void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Validate(state);

        Directory.CreateDirectory(StateDirectory);
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var temporaryPath = Path.Combine(
            StateDirectory,
            $"{StateFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(state, _jsonOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(StatePath))
            {
                ReplaceExistingState(temporaryPath);
            }
            else
            {
                File.Move(temporaryPath, StatePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void ReplaceExistingState(string temporaryPath)
    {
        try
        {
            File.Replace(temporaryPath, StatePath, BackupPath, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(StatePath, BackupPath, overwrite: true);
            File.Move(temporaryPath, StatePath, overwrite: true);
        }
        catch (IOException)
        {
            File.Copy(StatePath, BackupPath, overwrite: true);
            File.Move(temporaryPath, StatePath, overwrite: true);
        }
    }

    private void TrySave(AppState state)
    {
        try
        {
            Save(state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The in-memory defaults still let the overlay start safely.
        }
    }

    private void TryBackupCorruptedState()
    {
        if (!File.Exists(StatePath))
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var corruptedBackupPath = Path.Combine(
            StateDirectory,
            $"state.corrupt.{timestamp}.json");

        try
        {
            File.Copy(StatePath, corruptedBackupPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Recovery must not fail just because the backup could not be created.
        }
    }

    private static void Validate(AppState? state)
    {
        if (state is null)
        {
            throw new InvalidDataException("State file is empty.");
        }

        if (state.SchemaVersion != AppState.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported state schema version {state.SchemaVersion}.");
        }

        if (state.Tasks is null ||
            state.OverlaySettings is null ||
            state.WindowPlacement is null)
        {
            throw new InvalidDataException("State file is missing required sections.");
        }

        foreach (var task in state.Tasks)
        {
            if (task.Id == Guid.Empty || string.IsNullOrWhiteSpace(task.Title))
            {
                throw new InvalidDataException("State file contains an invalid task.");
            }
        }
    }
}
