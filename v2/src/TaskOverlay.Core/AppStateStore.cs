using System;
using System.IO;
using System.Linq;
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
    private readonly Action<string, Exception?>? _diagnostic;

    public AppStateStore(
        string? stateDirectory = null,
        Action<string, Exception?>? diagnostic = null)
    {
        StateDirectory = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskOverlayV2");
        _diagnostic = diagnostic;
    }

    public string StateDirectory { get; }
    public string StatePath => Path.Combine(StateDirectory, StateFileName);
    public string BackupPath => Path.Combine(StateDirectory, BackupFileName);

    public AppState Load()
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);

            if (!File.Exists(StatePath))
            {
                Report("State file is missing; creating seed state.");
                var defaultState = AppState.CreateDefault();
                TrySave(defaultState);
                return defaultState;
            }

            var json = File.ReadAllText(StatePath);
            var state = JsonSerializer.Deserialize<AppState>(json, _jsonOptions);
            if (state is null)
            {
                throw new InvalidDataException("State file is empty.");
            }

            var sourceSchemaVersion = state.SchemaVersion;
            StateMigrator.Migrate(state);
            var stateRepaired = StateMigrator.RepairCurrentState(state);
            Validate(state);
            if (sourceSchemaVersion != state.SchemaVersion || stateRepaired)
            {
                Report(
                    $"Normalized state after load: schema {sourceSchemaVersion} to {state.SchemaVersion}; " +
                    $"repaired={stateRepaired}.");
                TrySave(state, "Normalized state save failed.");
            }

            Report($"State load succeeded with {state.Tasks.Count} tasks.");
            return state;
        }
        catch (Exception ex) when (
            ex is JsonException or
            IOException or
            InvalidDataException or
            UnauthorizedAccessException)
        {
            Report("State load failed; recovering seed state.", ex);
            TryBackupCorruptedState();
            var defaultState = AppState.CreateDefault();
            TrySave(defaultState);
            return defaultState;
        }
    }

    public void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var stateRepaired = StateMigrator.RepairCurrentState(state);
        if (stateRepaired)
        {
            Report("State normalized before save.");
        }

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

            Report($"State save succeeded with {state.Tasks.Count} tasks.");
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

    private void TrySave(
        AppState state,
        string failureMessage = "Seed/recovery state save failed.")
    {
        try
        {
            Save(state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(failureMessage, ex);
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
            Report("Corrupted state backup failed.", ex);
            // Recovery must not fail just because the backup could not be created.
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
            // Diagnostics must never change storage behavior.
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
            state.Projects is null ||
            state.Groups is null ||
            state.OverlaySettings is null ||
            state.WindowPlacement is null ||
            state.TreeManagerSettings is null ||
            state.TreeManagerSettings.ExpandedNodeIds is null)
        {
            throw new InvalidDataException("State file is missing required sections.");
        }

        state.OverlaySettings.NormalizeOverlayMode();

        var defaultProject = state.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase));
        if (defaultProject is null)
        {
            throw new InvalidDataException("State file is missing the Default project.");
        }

        var projectIds = state.Projects.Select(project => project.Id).ToHashSet();
        var groupIds = state.Groups.Select(group => group.Id).ToHashSet();
        var taskIds = state.Tasks.Select(task => task.Id).ToHashSet();
        if (projectIds.Count != state.Projects.Count ||
            groupIds.Count != state.Groups.Count ||
            taskIds.Count != state.Tasks.Count)
        {
            throw new InvalidDataException("State file contains duplicate node IDs.");
        }

        foreach (var project in state.Projects)
        {
            if (project.Id == Guid.Empty || string.IsNullOrWhiteSpace(project.Name))
            {
                throw new InvalidDataException("State file contains an invalid project.");
            }
        }

        foreach (var group in state.Groups)
        {
            if (group.Id == Guid.Empty ||
                group.ProjectId == Guid.Empty ||
                !projectIds.Contains(group.ProjectId) ||
                string.IsNullOrWhiteSpace(group.Name))
            {
                throw new InvalidDataException("State file contains an invalid group.");
            }
        }

        foreach (var task in state.Tasks)
        {
            if (task.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(task.Title) ||
                !task.ProjectId.HasValue ||
                !projectIds.Contains(task.ProjectId.Value) ||
                (task.GroupId.HasValue && !groupIds.Contains(task.GroupId.Value)) ||
                (task.ParentTaskId.HasValue &&
                 (!taskIds.Contains(task.ParentTaskId.Value) || task.ParentTaskId == task.Id)))
            {
                throw new InvalidDataException("State file contains an invalid task.");
            }
        }
    }
}
