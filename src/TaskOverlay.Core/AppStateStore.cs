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

            var json = File.ReadAllBytes(StatePath);
            ThrowIfFutureSchema(json, StatePath);
            var state = JsonSerializer.Deserialize<AppState>(json, _jsonOptions);
            if (state is null)
            {
                throw new InvalidDataException("State file is empty.");
            }

            var sourceSchemaVersion = state.SchemaVersion;
            StateMigrator.Migrate(state);
            var stateRepaired = StateMigrator.RepairCurrentState(state);
            stateRepaired |= new MeetingRecordingService(state).RecoverInterrupted();
            Validate(state);
            if (sourceSchemaVersion != state.SchemaVersion || stateRepaired)
            {
                Report(
                    $"Normalized state after load: schema {sourceSchemaVersion} to {state.SchemaVersion}; " +
                    $"repaired={stateRepaired}.");
                TrySave(state, "Normalized state save failed.");
            }

            Report(
                $"State load succeeded with {state.Tasks.Count} tasks and " +
                $"{state.Meetings.Count} meetings and " +
                $"{state.TaskWorkSessions.Count} task work sessions.");
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

            Report(
                $"State save succeeded with {state.Tasks.Count} tasks and " +
                $"{state.Meetings.Count} meetings and " +
                $"{state.TaskWorkSessions.Count} task work sessions.");
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

    internal static void ThrowIfFutureSchema(ReadOnlyMemory<byte> json, string statePath)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out var schemaVersion))
        {
            return;
        }

        if (schemaVersion.ValueKind != JsonValueKind.Number ||
            !schemaVersion.TryGetInt32(out var storedSchemaVersion))
        {
            throw new InvalidDataException("State schemaVersion must be an integer.");
        }

        if (storedSchemaVersion > AppState.CurrentSchemaVersion)
        {
            throw new UnsupportedFutureStateVersionException(
                storedSchemaVersion,
                AppState.CurrentSchemaVersion,
                statePath);
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
            state.Meetings is null ||
            state.TaskWorkSessions is null ||
            state.ContextSources is null ||
            state.ContextItems is null ||
            state.MeetingRecordings is null ||
            state.MeetingTranscripts is null ||
            state.MeetingScreenshots is null ||
            state.MeetingAnalyses is null ||
            state.OverlaySettings is null ||
            state.WindowPlacement is null ||
            state.TreeManagerSettings is null ||
            state.TreeManagerSettings.ExpandedNodeIds is null ||
            state.WorkspaceSettings is null ||
            state.WorkspaceSettings.SelectedProjectIds is null ||
            state.TelegramCapture is null ||
            state.TelegramCapture.ProjectAliases is null)
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
        var meetingIds = state.Meetings.Select(meeting => meeting.Id).ToHashSet();
        var taskWorkSessionIds = state.TaskWorkSessions
            .Select(session => session.Id)
            .ToHashSet();
        var recordingIds = state.MeetingRecordings.Select(recording => recording.Id).ToHashSet();
        var transcriptIds = state.MeetingTranscripts.Select(transcript => transcript.Id).ToHashSet();
        var screenshotIds = state.MeetingScreenshots.Select(screenshot => screenshot.Id).ToHashSet();
        var analysisIds = state.MeetingAnalyses.Select(analysis => analysis.Id).ToHashSet();
        if (projectIds.Count != state.Projects.Count ||
            groupIds.Count != state.Groups.Count ||
            taskIds.Count != state.Tasks.Count ||
            meetingIds.Count != state.Meetings.Count ||
            taskWorkSessionIds.Count != state.TaskWorkSessions.Count ||
            recordingIds.Count != state.MeetingRecordings.Count ||
            transcriptIds.Count != state.MeetingTranscripts.Count ||
            screenshotIds.Count != state.MeetingScreenshots.Count ||
            analysisIds.Count != state.MeetingAnalyses.Count)
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
                (string.IsNullOrWhiteSpace(task.Title) && !task.IsDraft) ||
                !task.ProjectId.HasValue ||
                !projectIds.Contains(task.ProjectId.Value) ||
                (task.GroupId.HasValue && !groupIds.Contains(task.GroupId.Value)) ||
                (task.ParentTaskId.HasValue &&
                 (!taskIds.Contains(task.ParentTaskId.Value) || task.ParentTaskId == task.Id)))
            {
                throw new InvalidDataException("State file contains an invalid task.");
            }

            if (task.SourceReferences is not null && task.SourceReferences.Any(reference =>
                    reference is null ||
                    reference.RecordingId is Guid recordingId && !recordingIds.Contains(recordingId) ||
                    !analysisIds.Contains(reference.AnalysisId)))
            {
                throw new InvalidDataException("State file contains an invalid task source reference.");
            }
        }

        foreach (var meeting in state.Meetings)
        {
            if (meeting.Id == Guid.Empty ||
                !projectIds.Contains(meeting.ProjectId) ||
                string.IsNullOrWhiteSpace(meeting.Title) ||
                meeting.StartsAtUtc == default ||
                meeting.DurationMinutes <= 0 ||
                meeting.DurationMinutes > MeetingService.MaximumDurationMinutes ||
                meeting.LinkedTaskId is Guid linkedTaskId && !taskIds.Contains(linkedTaskId) ||
                meeting.ActiveTranscriptId is Guid activeTranscriptId &&
                !state.MeetingTranscripts.Any(transcript =>
                    transcript.Id == activeTranscriptId && transcript.MeetId == meeting.Id))
            {
                throw new InvalidDataException("State file contains an invalid meeting.");
            }
        }

        foreach (var recording in state.MeetingRecordings)
        {
            if (recording.Id == Guid.Empty ||
                recording.MeetId is Guid meetId && !meetingIds.Contains(meetId) ||
                !RecordingPathPolicy.IsSafeRelativePath(recording.RecordingFolderRelativePath) ||
                recording.TranscriptionChunkFiles is null ||
                recording.Tracks is null ||
                !Enum.IsDefined(recording.SourceKind) ||
                !Enum.IsDefined(recording.State) ||
                !Enum.IsDefined(recording.RecordingFormat) ||
                !Enum.IsDefined(recording.SystemAudioHealth) ||
                !Enum.IsDefined(recording.MicrophoneHealth) ||
                recording.Tracks.Any(track =>
                    track is null ||
                    !Enum.IsDefined(track.Kind) ||
                    !Enum.IsDefined(track.FinalizationState) ||
                    !Enum.IsDefined(track.ValidationState) ||
                    track.SegmentFiles is null) ||
                recording.ProcessFromSeconds is < 0 ||
                recording.ProcessUntilSeconds is <= 0 ||
                recording.ProcessFromSeconds is double from &&
                recording.ProcessUntilSeconds is double until && until <= from)
            {
                throw new InvalidDataException("State file contains an invalid meeting recording.");
            }
        }

        foreach (var transcript in state.MeetingTranscripts)
        {
            if (transcript.Id == Guid.Empty ||
                transcript.MeetId is Guid transcriptMeetId && !meetingIds.Contains(transcriptMeetId) ||
                transcript.RecordingId is Guid recordingId && !recordingIds.Contains(recordingId) ||
                !RecordingPathPolicy.IsSafeRelativePath(transcript.StorageFolderRelativePath) ||
                string.IsNullOrWhiteSpace(transcript.NormalizedArtifactFile) ||
                transcript.RevisionId == Guid.Empty ||
                transcript.SourceTranscriptId is Guid sourceTranscriptId &&
                (sourceTranscriptId == Guid.Empty || sourceTranscriptId == transcript.Id) ||
                transcript.ParentRevisionId == Guid.Empty ||
                transcript.Origin == MeetingTranscriptOrigin.UserEdited &&
                (transcript.SourceTranscriptId is null || transcript.ParentRevisionId is null) ||
                transcript.Speakers is null ||
                transcript.ImportWarnings is null ||
                transcript.Speakers.Any(speaker =>
                    speaker is null || string.IsNullOrWhiteSpace(speaker.SpeakerId)) ||
                transcript.Speakers.Select(speaker => speaker.SpeakerId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != transcript.Speakers.Count)
            {
                throw new InvalidDataException("State file contains an invalid meeting transcript.");
            }
        }

        foreach (var screenshot in state.MeetingScreenshots)
        {
            if (screenshot.Id == Guid.Empty ||
                !meetingIds.Contains(screenshot.MeetId) ||
                screenshot.RecordingId is Guid recordingId && !recordingIds.Contains(recordingId) ||
                !RecordingPathPolicy.IsSafeRelativePath(screenshot.StorageFolderRelativePath) ||
                string.IsNullOrWhiteSpace(screenshot.FileName) ||
                screenshot.Width <= 0 || screenshot.Height <= 0 || screenshot.Bytes < 0 ||
                screenshot.OffsetFromRecordingStartSeconds is < 0 ||
                !Enum.IsDefined(screenshot.SourceKind))
            {
                throw new InvalidDataException("State file contains an invalid meeting screenshot.");
            }
        }

        foreach (var analysis in state.MeetingAnalyses)
        {
            if (analysis.Id == Guid.Empty ||
                analysis.RecordingId is Guid recordingId && !recordingIds.Contains(recordingId) ||
                analysis.TranscriptId is not Guid transcriptId ||
                !transcriptIds.Contains(transcriptId) ||
                analysis.TranscriptRevisionId is null ||
                analysis.MeetId is Guid meetId && !meetingIds.Contains(meetId) ||
                analysis.ProposedActions is null ||
                analysis.Decisions is null ||
                analysis.MyActionItems is null ||
                analysis.OtherPeopleActionItems is null ||
                analysis.WaitingFor is null ||
                analysis.Risks is null ||
                analysis.QuestionsToClarify is null ||
                analysis.Deadlines is null ||
                analysis.KeyQuotesOrSourceReferences is null)
            {
                throw new InvalidDataException("State file contains an invalid meeting analysis.");
            }
        }

        foreach (var session in state.TaskWorkSessions)
        {
            if (session.Id == Guid.Empty ||
                !taskIds.Contains(session.TaskId) ||
                !TaskWorkSessionService.IsValidRange(session.StartUtc, session.EndUtc) ||
                session.Note is null ||
                session.Note.Length > TaskWorkSessionService.MaximumNoteLength)
            {
                throw new InvalidDataException(
                    "State file contains an invalid task work session.");
            }
        }

        var sourceIds = state.ContextSources.Select(source => source.Id).ToHashSet();
        if (sourceIds.Count != state.ContextSources.Count ||
            state.ContextItems.Select(item => item.Id).Distinct().Count() != state.ContextItems.Count)
        {
            throw new InvalidDataException("State file contains duplicate context IDs.");
        }

        foreach (var source in state.ContextSources)
        {
            if (source.Id == Guid.Empty ||
                !projectIds.Contains(source.ProjectId) ||
                string.IsNullOrWhiteSpace(source.Title) ||
                source.LinkedTaskIds is null ||
                source.LinkedMeetingIds is null ||
                source.LinkedTaskIds.Any(id => !taskIds.Contains(id)) ||
                source.LinkedMeetingIds.Any(id => !meetingIds.Contains(id)))
            {
                throw new InvalidDataException("State file contains an invalid context source.");
            }
        }

        foreach (var item in state.ContextItems)
        {
            if (item.Id == Guid.Empty ||
                !projectIds.Contains(item.ProjectId) ||
                string.IsNullOrWhiteSpace(item.Title) ||
                item.SourceDocumentIds is null ||
                item.LinkedTaskIds is null ||
                item.LinkedMeetingIds is null ||
                item.SourceDocumentIds.Any(id => !sourceIds.Contains(id)) ||
                item.LinkedTaskIds.Any(id => !taskIds.Contains(id)) ||
                item.LinkedMeetingIds.Any(id => !meetingIds.Contains(id)))
            {
                throw new InvalidDataException("State file contains an invalid context item.");
            }
        }

        if (state.TelegramCapture.DefaultProjectId is Guid defaultTelegramProjectId &&
            !projectIds.Contains(defaultTelegramProjectId))
        {
            throw new InvalidDataException("State file contains an invalid Telegram default project.");
        }

        foreach (var alias in state.TelegramCapture.ProjectAliases)
        {
            if (alias is null ||
                string.IsNullOrWhiteSpace(alias.Alias) ||
                alias.ProjectId == Guid.Empty ||
                !projectIds.Contains(alias.ProjectId))
            {
                throw new InvalidDataException("State file contains an invalid Telegram project alias.");
            }
        }
    }
}
