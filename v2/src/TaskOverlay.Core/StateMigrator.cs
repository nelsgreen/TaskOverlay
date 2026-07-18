using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TaskOverlay.Core;

public static class StateMigrator
{
    public static AppState Migrate(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Projects ??= new List<ProjectItem>();
        state.Groups ??= new List<GroupItem>();
        state.Meetings ??= new List<MeetingItem>();
        state.TaskWorkSessions ??= new List<TaskWorkSession>();
        state.ContextSources ??= new List<SourceDocument>();
        state.ContextItems ??= new List<ContextItem>();
        state.MeetingRecordings ??= new List<MeetingRecording>();
        state.MeetingTranscripts ??= new List<MeetingTranscript>();
        state.MeetingScreenshots ??= new List<MeetingScreenshot>();
        state.MeetingAnalyses ??= new List<MeetingAnalysis>();
        state.WorkspaceSettings ??= new WorkspaceSettings();
        state.TelegramCapture ??= new TelegramCaptureSettings();

        state.Tasks ??= new List<TaskItem>();
        while (state.SchemaVersion != AppState.CurrentSchemaVersion)
        {
            switch (state.SchemaVersion)
            {
                case 1:
                    MigrateV1ToV2(state);
                    break;
                case 2:
                    MigrateLegacyPlannedWork(state);
                    state.SchemaVersion = 3;
                    break;
                case 3:
                    state.MeetingRecordings ??= new List<MeetingRecording>();
                    state.MeetingAnalyses ??= new List<MeetingAnalysis>();
                    state.SchemaVersion = 4;
                    break;
                case 4:
                    // Recording artifacts are repaired below. Missing format metadata
                    // intentionally maps to legacy WAV through the enum's zero value.
                    state.SchemaVersion = 5;
                    break;
                case 5:
                    MigrateMeetingSourcesV5ToV6(state);
                    state.SchemaVersion = 6;
                    break;
                case 6:
                    // Schema 7 adds only nullable user-edited transcript revision
                    // provenance (SourceTranscriptId / ParentRevisionId) and the
                    // UserEdited transcript origin. Existing records need no
                    // transformation; the bump keeps older binaries from loading
                    // states whose origin value they cannot deserialize.
                    state.SchemaVersion = 7;
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported schema version: {state.SchemaVersion}.");
            }
        }

        return state;
    }

    private static void MigrateMeetingSourcesV5ToV6(AppState state)
    {
        state.MeetingTranscripts ??= new List<MeetingTranscript>();
        state.MeetingScreenshots ??= new List<MeetingScreenshot>();
        foreach (var recording in state.MeetingRecordings.Where(recording =>
                     recording.MeetId.HasValue &&
                     !string.IsNullOrWhiteSpace(recording.TranscriptFile)))
        {
            if (state.MeetingTranscripts.Any(transcript => transcript.RecordingId == recording.Id))
            {
                continue;
            }

            var timestamp = recording.UpdatedAtUtc == default
                ? recording.CreatedAtUtc == default
                    ? DateTimeOffset.UtcNow
                    : recording.CreatedAtUtc
                : recording.UpdatedAtUtc;
            var transcript = new MeetingTranscript
            {
                Id = recording.Id,
                MeetId = recording.MeetId!.Value,
                RecordingId = recording.Id,
                Origin = MeetingTranscriptOrigin.Generated,
                Format = MeetingTranscriptFormat.NormalizedJson,
                Provider = "TaskOverlay",
                SourceLabel = "TaskOverlay",
                StorageFolderRelativePath = recording.RecordingFolderRelativePath,
                OriginalArtifactFile = string.IsNullOrWhiteSpace(recording.TranscriptRawFile)
                    ? recording.TranscriptFile
                    : recording.TranscriptRawFile,
                NormalizedArtifactFile = recording.TranscriptFile,
                MarkdownArtifactFile = recording.TranscriptMarkdownFile,
                HasTimestamps = true,
                RevisionId = Guid.NewGuid(),
                CreatedAtUtc = timestamp,
                UpdatedAtUtc = timestamp
            };
            state.MeetingTranscripts.Add(transcript);
            var meeting = state.Meetings.FirstOrDefault(item => item.Id == transcript.MeetId);
            if (meeting is not null && meeting.ActiveTranscriptId is null)
            {
                meeting.ActiveTranscriptId = transcript.Id;
            }

            foreach (var analysis in state.MeetingAnalyses.Where(analysis =>
                         analysis.RecordingId == recording.Id))
            {
                analysis.TranscriptId ??= transcript.Id;
                analysis.TranscriptRevisionId ??= transcript.RevisionId;
            }
        }
    }

    private static void MigrateV1ToV2(AppState state)
    {
        var defaultProject = state.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase));

        if (defaultProject is null)
        {
            var createdAtUtc = state.CreatedAtUtc == default
                ? DateTimeOffset.UtcNow
                : state.CreatedAtUtc;
            defaultProject = ProjectItem.CreateDefault(createdAtUtc);
            state.Projects.Add(defaultProject);
        }
        else if (defaultProject.Id == Guid.Empty)
        {
            defaultProject.Id = Guid.NewGuid();
        }

        foreach (var task in state.Tasks)
        {
            task.ProjectId = defaultProject.Id;
            task.GroupId = null;
        }

        state.SchemaVersion = 2;
    }

    public static bool RepairCurrentState(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var changed = false;
        if (state.Projects is null)
        {
            state.Projects = new List<ProjectItem>();
            changed = true;
        }

        if (state.Groups is null)
        {
            state.Groups = new List<GroupItem>();
            changed = true;
        }

        if (state.Tasks is null)
        {
            state.Tasks = new List<TaskItem>();
            changed = true;
        }

        if (state.Meetings is null)
        {
            state.Meetings = new List<MeetingItem>();
            changed = true;
        }

        if (state.TaskWorkSessions is null)
        {
            state.TaskWorkSessions = new List<TaskWorkSession>();
            changed = true;
        }

        if (state.ContextSources is null)
        {
            state.ContextSources = new List<SourceDocument>();
            changed = true;
        }

        if (state.ContextItems is null)
        {
            state.ContextItems = new List<ContextItem>();
            changed = true;
        }

        if (state.MeetingRecordings is null)
        {
            state.MeetingRecordings = new List<MeetingRecording>();
            changed = true;
        }

        if (state.MeetingTranscripts is null)
        {
            state.MeetingTranscripts = new List<MeetingTranscript>();
            changed = true;
        }

        if (state.MeetingScreenshots is null)
        {
            state.MeetingScreenshots = new List<MeetingScreenshot>();
            changed = true;
        }

        if (state.MeetingAnalyses is null)
        {
            state.MeetingAnalyses = new List<MeetingAnalysis>();
            changed = true;
        }

        if (state.TelegramCapture is null)
        {
            state.TelegramCapture = new TelegramCaptureSettings();
            changed = true;
        }

        if (state.OverlaySettings is not null &&
            state.OverlaySettings.NormalizeOverlayMode())
        {
            changed = true;
        }

        if (state.OverlaySettings is not null &&
            state.OverlaySettings.NormalizeWorkingPresentation())
        {
            changed = true;
        }

        if (state.OverlaySettings is not null &&
            state.OverlaySettings.NormalizePanelPresentation())
        {
            changed = true;
        }

        if (state.WindowPlacement is not null &&
            UtilityShellGeometryPolicy.Normalize(state.WindowPlacement))
        {
            changed = true;
        }

        var defaultProject = state.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase));
        if (defaultProject is null)
        {
            var timestamp = state.CreatedAtUtc == default
                ? DateTimeOffset.UtcNow
                : state.CreatedAtUtc;
            defaultProject = ProjectItem.CreateDefault(timestamp);
            defaultProject.SortOrder = state.Projects.Count == 0
                ? 0
                : state.Projects.Max(project => project.SortOrder) + 1;
            state.Projects.Add(defaultProject);
            changed = true;
        }
        else if (defaultProject.Id == Guid.Empty)
        {
            defaultProject.Id = Guid.NewGuid();
            changed = true;
        }

        var projectIds = state.Projects.Select(project => project.Id).ToHashSet();
        foreach (var project in state.Projects)
        {
            if (!ProjectColorPalette.IsValid(project.ColorHex))
            {
                project.ColorHex = ProjectColorPalette.Resolve(project.Name, project.Id);
                changed = true;
            }
        }

        foreach (var group in state.Groups)
        {
            if (!projectIds.Contains(group.ProjectId))
            {
                group.ProjectId = defaultProject.Id;
                changed = true;
            }
        }

        var groupsById = state.Groups
            .GroupBy(group => group.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var tasksById = state.Tasks
            .GroupBy(task => task.Id)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var task in state.Tasks)
        {
            var normalizedStatus = task.StoredStatus ??
                                   (task.Completed
                                       ? TaskStatus.Done
                                       : task.InWork
                                           ? TaskStatus.InWork
                                           : TaskStatus.Todo);
            var shouldBeCompleted = normalizedStatus == TaskStatus.Done;
            var shouldBeInWork = normalizedStatus == TaskStatus.InWork;
            if (task.StoredStatus != normalizedStatus ||
                task.Completed != shouldBeCompleted ||
                task.InWork != shouldBeInWork)
            {
                task.Status = normalizedStatus;
                task.Completed = shouldBeCompleted;
                task.InWork = shouldBeInWork;
                changed = true;
            }

            if (task.WaitingFor is null)
            {
                task.WaitingFor = string.Empty;
                changed = true;
            }

            if (task.RemindEveryMinutes <= 0)
            {
                task.RemindEveryMinutes = null;
                changed = true;
            }

            if (task.Completed && task.ReminderActive)
            {
                task.ReminderActive = false;
                changed = true;
            }

            if (task.ParentTaskId == task.Id ||
                task.ParentTaskId.HasValue && !tasksById.ContainsKey(task.ParentTaskId.Value))
            {
                task.ParentTaskId = null;
                changed = true;
            }

            if (CheckpointService.Normalize(task))
            {
                changed = true;
            }
        }

        if (MigrateLegacyPlannedWork(state))
        {
            changed = true;
        }

        var workSessionIds = new HashSet<Guid>();
        foreach (var session in state.TaskWorkSessions.ToList())
        {
            if (!tasksById.ContainsKey(session.TaskId) ||
                !TaskWorkSessionService.IsValidRange(session.StartUtc, session.EndUtc))
            {
                state.TaskWorkSessions.Remove(session);
                changed = true;
                continue;
            }

            if (session.Id == Guid.Empty || !workSessionIds.Add(session.Id))
            {
                session.Id = Guid.NewGuid();
                workSessionIds.Add(session.Id);
                changed = true;
            }

            var note = session.Note?.Trim() ?? string.Empty;
            if (note.Length > TaskWorkSessionService.MaximumNoteLength)
            {
                note = note[..TaskWorkSessionService.MaximumNoteLength];
            }

            if (session.Note != note)
            {
                session.Note = note;
                changed = true;
            }

            if (session.CreatedAtUtc == default)
            {
                session.CreatedAtUtc = session.StartUtc;
                changed = true;
            }

            if (session.UpdatedAtUtc == default)
            {
                session.UpdatedAtUtc = session.CreatedAtUtc;
                changed = true;
            }
        }

        var meetingIds = new HashSet<Guid>();
        foreach (var meeting in state.Meetings.ToList())
        {
            if (meeting.StartsAtUtc == default)
            {
                state.Meetings.Remove(meeting);
                changed = true;
                continue;
            }

            if (meeting.Id == Guid.Empty || !meetingIds.Add(meeting.Id))
            {
                meeting.Id = Guid.NewGuid();
                meetingIds.Add(meeting.Id);
                changed = true;
            }

            if (!projectIds.Contains(meeting.ProjectId))
            {
                meeting.ProjectId = defaultProject.Id;
                changed = true;
            }

            var projectName = state.Projects.FirstOrDefault(project =>
                project.Id == meeting.ProjectId)?.Name;
            var title = string.IsNullOrWhiteSpace(meeting.Title)
                ? MeetingService.GenerateTitle(projectName, meeting.StartsAtUtc)
                : meeting.Title.Trim();
            var titleIsGenerated = string.IsNullOrWhiteSpace(meeting.Title) ||
                                   meeting.TitleIsGenerated;
            var notes = meeting.Notes?.Trim() ?? string.Empty;
            var location = meeting.Location?.Trim() ?? string.Empty;
            var link = meeting.Link?.Trim() ?? string.Empty;
            if (meeting.Title != title || meeting.Notes != notes ||
                meeting.Location != location || meeting.Link != link ||
                meeting.TitleIsGenerated != titleIsGenerated)
            {
                meeting.Title = title;
                meeting.TitleIsGenerated = titleIsGenerated;
                meeting.Notes = notes;
                meeting.Location = location;
                meeting.Link = link;
                changed = true;
            }

            if (meeting.DurationMinutes <= 0 ||
                meeting.DurationMinutes > MeetingService.MaximumDurationMinutes)
            {
                meeting.DurationMinutes = MeetingItem.DefaultDurationMinutes;
                changed = true;
            }

            if (meeting.LinkedTaskId is Guid linkedTaskId && !tasksById.ContainsKey(linkedTaskId))
            {
                meeting.LinkedTaskId = null;
                changed = true;
            }

            if (!Enum.IsDefined(meeting.RecordingPolicy))
            {
                meeting.RecordingPolicy = MeetingRecordingPolicy.Inherit;
                changed = true;
            }

            if (meeting.CreatedAtUtc == default)
            {
                meeting.CreatedAtUtc = meeting.StartsAtUtc;
                changed = true;
            }

            if (meeting.UpdatedAtUtc == default)
            {
                meeting.UpdatedAtUtc = meeting.CreatedAtUtc;
                changed = true;
            }
        }

        // ContextHUB repair: normalize records conservatively and drop dangling
        // links, but never delete a valid record just because one link is stale.
        var sourceIds = new HashSet<Guid>();
        foreach (var source in state.ContextSources.ToList())
        {
            if (string.IsNullOrWhiteSpace(source.Title))
            {
                state.ContextSources.Remove(source);
                changed = true;
                continue;
            }

            if (source.Id == Guid.Empty || !sourceIds.Add(source.Id))
            {
                source.Id = Guid.NewGuid();
                sourceIds.Add(source.Id);
                changed = true;
            }

            if (!projectIds.Contains(source.ProjectId))
            {
                source.ProjectId = defaultProject.Id;
                changed = true;
            }

            if (!Enum.IsDefined(source.SourceType))
            {
                source.SourceType = ContextSourceType.Other;
                changed = true;
            }

            if (source.SourceApp is { } app && !Enum.IsDefined(app))
            {
                source.SourceApp = ContextSourceApp.Other;
                changed = true;
            }

            var title = source.Title.Trim();
            var body = source.Body?.Trim() ?? string.Empty;
            var summary = source.Summary?.Trim() ?? string.Empty;
            if (source.Title != title || source.Body != body || source.Summary != summary)
            {
                source.Title = title;
                source.Body = body;
                source.Summary = summary;
                changed = true;
            }

            changed |= RepairIdList(
                source.LinkedTaskIds is null
                    ? source.LinkedTaskIds = new List<Guid>()
                    : source.LinkedTaskIds,
                tasksById.ContainsKey);
            changed |= RepairIdList(
                source.LinkedMeetingIds is null
                    ? source.LinkedMeetingIds = new List<Guid>()
                    : source.LinkedMeetingIds,
                meetingIds.Contains);

            if (source.SourceDateUtc == default)
            {
                source.SourceDateUtc = source.CreatedAtUtc == default
                    ? DateTimeOffset.UtcNow
                    : source.CreatedAtUtc;
                changed = true;
            }

            if (source.CreatedAtUtc == default)
            {
                source.CreatedAtUtc = source.SourceDateUtc;
                changed = true;
            }

            if (source.UpdatedAtUtc == default)
            {
                source.UpdatedAtUtc = source.CreatedAtUtc;
                changed = true;
            }
        }

        var contextItemIds = new HashSet<Guid>();
        foreach (var item in state.ContextItems.ToList())
        {
            if (string.IsNullOrWhiteSpace(item.Title))
            {
                state.ContextItems.Remove(item);
                changed = true;
                continue;
            }

            if (item.Id == Guid.Empty || !contextItemIds.Add(item.Id))
            {
                item.Id = Guid.NewGuid();
                contextItemIds.Add(item.Id);
                changed = true;
            }

            if (!projectIds.Contains(item.ProjectId))
            {
                item.ProjectId = defaultProject.Id;
                changed = true;
            }

            if (!Enum.IsDefined(item.ItemType))
            {
                item.ItemType = ContextItemType.Note;
                changed = true;
            }

            if (!Enum.IsDefined(item.Status))
            {
                item.Status = ContextItemStatus.Active;
                changed = true;
            }

            var itemTitle = item.Title.Trim();
            var itemBody = item.Body?.Trim() ?? string.Empty;
            if (item.Title != itemTitle || item.Body != itemBody)
            {
                item.Title = itemTitle;
                item.Body = itemBody;
                changed = true;
            }

            changed |= RepairIdList(
                item.SourceDocumentIds is null
                    ? item.SourceDocumentIds = new List<Guid>()
                    : item.SourceDocumentIds,
                sourceIds.Contains);
            changed |= RepairIdList(
                item.LinkedTaskIds is null
                    ? item.LinkedTaskIds = new List<Guid>()
                    : item.LinkedTaskIds,
                tasksById.ContainsKey);
            changed |= RepairIdList(
                item.LinkedMeetingIds is null
                    ? item.LinkedMeetingIds = new List<Guid>()
                    : item.LinkedMeetingIds,
                meetingIds.Contains);

            if (item.Status == ContextItemStatus.Active && item.ResolvedAtUtc is not null)
            {
                item.ResolvedAtUtc = null;
                changed = true;
            }

            if (item.CreatedAtUtc == default)
            {
                item.CreatedAtUtc = DateTimeOffset.UtcNow;
                changed = true;
            }

            if (item.UpdatedAtUtc == default)
            {
                item.UpdatedAtUtc = item.CreatedAtUtc;
                changed = true;
            }
        }

        var recordingIds = new HashSet<Guid>();
        foreach (var recording in state.MeetingRecordings)
        {
            if (recording.Id == Guid.Empty || !recordingIds.Add(recording.Id))
            {
                recording.Id = Guid.NewGuid();
                recordingIds.Add(recording.Id);
                changed = true;
            }

            if (recording.MeetId is Guid recordingMeetId && !meetingIds.Contains(recordingMeetId))
            {
                recording.MeetId = null;
                recording.SourceKind = MeetingRecordingSourceKind.Emergency;
                changed = true;
            }

            if (!Enum.IsDefined(recording.SourceKind))
            {
                recording.SourceKind = recording.MeetId.HasValue
                    ? MeetingRecordingSourceKind.ManualMeet
                    : MeetingRecordingSourceKind.Emergency;
                changed = true;
            }

            if (!Enum.IsDefined(recording.State))
            {
                recording.State = MeetingRecordingState.Failed;
                recording.LastError = "Recording state was invalid; original files were kept.";
                changed = true;
            }

            if (!Enum.IsDefined(recording.SystemAudioHealth))
            {
                recording.SystemAudioHealth = AudioTrackHealth.Unknown;
                changed = true;
            }

            if (!Enum.IsDefined(recording.MicrophoneHealth))
            {
                recording.MicrophoneHealth = AudioTrackHealth.Unknown;
                changed = true;
            }

            if (!Enum.IsDefined(recording.RecordingFormat))
            {
                recording.RecordingFormat = InferRecordingFormat(recording);
                changed = true;
            }

            var folder = RecordingPathPolicy.NormalizeRelativePath(
                recording.RecordingFolderRelativePath);
            if (!RecordingPathPolicy.IsSafeRelativePath(folder))
            {
                folder = $"meetings/emergency/recordings/{recording.Id:N}";
                recording.State = MeetingRecordingState.Failed;
                recording.LastError =
                    "Recording path was invalid. Locate the original files and retry.";
                changed = true;
            }

            if (recording.RecordingFolderRelativePath != folder)
            {
                recording.RecordingFolderRelativePath = folder;
                changed = true;
            }

            if (recording.TranscriptionChunkFiles is null)
            {
                recording.TranscriptionChunkFiles = new List<string>();
                changed = true;
            }

            if (recording.Tracks is null)
            {
                recording.Tracks = new List<MeetingRecordingTrackArtifact>();
                changed = true;
            }

            changed |= NormalizeRecordingFileNames(recording);
            changed |= RepairRecordingTracks(recording);
            recording.OriginalFileName = NormalizeBounded(recording.OriginalFileName, 500);
            recording.ManagedFileName = NormalizeManagedFileName(recording.ManagedFileName);
            if (recording.ImportedFileBytes < 0)
            {
                recording.ImportedFileBytes = 0;
                changed = true;
            }

            var importedDuration = recording.Tracks
                .Where(track => track.Kind == MeetingRecordingTrackKind.Mixed)
                .Select(track => track.DurationSeconds)
                .DefaultIfEmpty(0)
                .Max();
            if (recording.SourceKind != MeetingRecordingSourceKind.Imported &&
                (recording.ProcessFromSeconds.HasValue || recording.ProcessUntilSeconds.HasValue) ||
                recording.ProcessFromSeconds is < 0 ||
                recording.ProcessUntilSeconds is <= 0 ||
                recording.ProcessFromSeconds is double from &&
                recording.ProcessUntilSeconds is double until && until <= from ||
                importedDuration > 0 && recording.ProcessFromSeconds is double rangeStart &&
                rangeStart >= importedDuration ||
                importedDuration > 0 && recording.ProcessUntilSeconds is double rangeEnd &&
                rangeEnd > importedDuration + 0.01)
            {
                recording.ProcessFromSeconds = null;
                recording.ProcessUntilSeconds = null;
                changed = true;
            }
            recording.LastError = NormalizeBounded(recording.LastError, 2_000);
            if (recording.CreatedAtUtc == default)
            {
                recording.CreatedAtUtc = recording.StartedAtUtc ?? DateTimeOffset.UtcNow;
                changed = true;
            }

            if (recording.UpdatedAtUtc == default)
            {
                recording.UpdatedAtUtc = recording.CreatedAtUtc;
                changed = true;
            }
        }

        var transcriptIds = new HashSet<Guid>();
        foreach (var transcript in state.MeetingTranscripts.ToList())
        {
            var ownerRecording = transcript.RecordingId is Guid linkedRecordingId
                ? state.MeetingRecordings.FirstOrDefault(recording => recording.Id == linkedRecordingId)
                : null;
            if (ownerRecording is not null && transcript.MeetId != ownerRecording.MeetId)
            {
                transcript.MeetId = ownerRecording.MeetId;
                changed = true;
            }
            else if (transcript.MeetId is Guid transcriptMeetId &&
                     !meetingIds.Contains(transcriptMeetId))
            {
                transcript.MeetId = null;
                changed = true;
            }

            if (transcript.Id == Guid.Empty || !transcriptIds.Add(transcript.Id))
            {
                transcript.Id = Guid.NewGuid();
                transcriptIds.Add(transcript.Id);
                changed = true;
            }

            if (transcript.RecordingId is Guid transcriptRecordingId &&
                !recordingIds.Contains(transcriptRecordingId))
            {
                transcript.RecordingId = null;
                changed = true;
            }

            if (!Enum.IsDefined(transcript.Origin))
            {
                transcript.Origin = transcript.RecordingId.HasValue
                    ? MeetingTranscriptOrigin.Generated
                    : MeetingTranscriptOrigin.Imported;
                changed = true;
            }

            if (!Enum.IsDefined(transcript.Format))
            {
                transcript.Format = MeetingTranscriptFormat.NormalizedJson;
                changed = true;
            }

            var transcriptFolder = RecordingPathPolicy.NormalizeRelativePath(
                transcript.StorageFolderRelativePath);
            if (!RecordingPathPolicy.IsSafeRelativePath(transcriptFolder) &&
                transcript.RecordingId is Guid ownerRecordingId)
            {
                transcriptFolder = state.MeetingRecordings
                    .First(recording => recording.Id == ownerRecordingId)
                    .RecordingFolderRelativePath;
            }

            if (!RecordingPathPolicy.IsSafeRelativePath(transcriptFolder))
            {
                state.MeetingTranscripts.Remove(transcript);
                transcriptIds.Remove(transcript.Id);
                changed = true;
                continue;
            }

            if (transcript.StorageFolderRelativePath != transcriptFolder)
            {
                transcript.StorageFolderRelativePath = transcriptFolder;
                changed = true;
            }

            transcript.Provider = NormalizeBounded(transcript.Provider, 100);
            transcript.Model = NormalizeBounded(transcript.Model, 100);
            transcript.SourceLabel = NormalizeBounded(transcript.SourceLabel, 200);
            transcript.OriginalFileName = NormalizeBounded(transcript.OriginalFileName, 500);
            transcript.OriginalArtifactFile = NormalizeManagedFileName(transcript.OriginalArtifactFile);
            transcript.NormalizedArtifactFile = NormalizeManagedFileName(transcript.NormalizedArtifactFile);
            transcript.MarkdownArtifactFile = NormalizeManagedFileName(transcript.MarkdownArtifactFile);
            transcript.ImportWarnings ??= new List<string>();
            transcript.ImportWarnings = transcript.ImportWarnings
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Select(warning => NormalizeBounded(warning, 500))
                .Take(100)
                .ToList();
            transcript.Speakers ??= new List<TranscriptSpeaker>();
            var speakerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var speaker in transcript.Speakers.ToList())
            {
                speaker.OriginalLabel = NormalizeBounded(speaker.OriginalLabel, 200);
                speaker.DisplayName = NormalizeBounded(speaker.DisplayName, 200);
                speaker.SpeakerId = NormalizeBounded(speaker.SpeakerId, 100);
                if (speaker.SpeakerId.Length == 0 || !speakerIds.Add(speaker.SpeakerId))
                {
                    transcript.Speakers.Remove(speaker);
                    changed = true;
                }
            }

            if (transcript.RevisionId == Guid.Empty)
            {
                transcript.RevisionId = Guid.NewGuid();
                changed = true;
            }

            if (transcript.CreatedAtUtc == default)
            {
                transcript.CreatedAtUtc = transcript.ImportedAtUtc ?? DateTimeOffset.UtcNow;
                changed = true;
            }

            if (transcript.UpdatedAtUtc == default)
            {
                transcript.UpdatedAtUtc = transcript.CreatedAtUtc;
                changed = true;
            }
        }

        foreach (var meeting in state.Meetings)
        {
            if (meeting.ActiveTranscriptId is Guid activeTranscriptId &&
                !state.MeetingTranscripts.Any(transcript =>
                transcript.Id == activeTranscriptId && transcript.MeetId == meeting.Id))
            {
                meeting.ActiveTranscriptId = null;
                changed = true;
            }

            if (meeting.ActiveTranscriptId is null)
            {
                var fallback = state.MeetingTranscripts
                    .Where(transcript => transcript.MeetId == meeting.Id)
                    .OrderByDescending(transcript => transcript.CreatedAtUtc)
                    .Select(transcript => (Guid?)transcript.Id)
                    .FirstOrDefault();
                if (fallback.HasValue)
                {
                    meeting.ActiveTranscriptId = fallback;
                    changed = true;
                }
            }
        }

        var screenshotIds = new HashSet<Guid>();
        foreach (var screenshot in state.MeetingScreenshots.ToList())
        {
            var folder = RecordingPathPolicy.NormalizeRelativePath(
                screenshot.StorageFolderRelativePath);
            if (!meetingIds.Contains(screenshot.MeetId) ||
                !RecordingPathPolicy.IsSafeRelativePath(folder) ||
                NormalizeManagedFileName(screenshot.FileName).Length == 0 ||
                screenshot.Width <= 0 || screenshot.Height <= 0)
            {
                state.MeetingScreenshots.Remove(screenshot);
                changed = true;
                continue;
            }

            if (screenshot.Id == Guid.Empty || !screenshotIds.Add(screenshot.Id))
            {
                screenshot.Id = Guid.NewGuid();
                screenshotIds.Add(screenshot.Id);
                changed = true;
            }

            if (screenshot.RecordingId is Guid screenshotRecordingId &&
                (!recordingIds.Contains(screenshotRecordingId) ||
                 state.MeetingRecordings.First(recording => recording.Id == screenshotRecordingId)
                     .MeetId != screenshot.MeetId))
            {
                screenshot.RecordingId = null;
                screenshot.OffsetFromRecordingStartSeconds = null;
                changed = true;
            }

            screenshot.StorageFolderRelativePath = folder;
            screenshot.FileName = NormalizeManagedFileName(screenshot.FileName);
            screenshot.SourceLabel = NormalizeBounded(screenshot.SourceLabel, 500);
            screenshot.Bytes = Math.Max(0, screenshot.Bytes);
            if (screenshot.OffsetFromRecordingStartSeconds is < 0)
            {
                screenshot.OffsetFromRecordingStartSeconds = null;
                changed = true;
            }

            if (!Enum.IsDefined(screenshot.SourceKind))
            {
                screenshot.SourceKind = MeetingScreenshotSourceKind.Display;
                changed = true;
            }

            if (screenshot.CapturedAtUtc == default)
            {
                screenshot.CapturedAtUtc = DateTimeOffset.UtcNow;
                changed = true;
            }
        }

        var analysisIds = new HashSet<Guid>();
        foreach (var analysis in state.MeetingAnalyses.ToList())
        {
            if (analysis.TranscriptId is null && analysis.RecordingId is Guid legacyRecordingId)
            {
                var generated = state.MeetingTranscripts.FirstOrDefault(transcript =>
                    transcript.RecordingId == legacyRecordingId);
                if (generated is not null)
                {
                    analysis.TranscriptId = generated.Id;
                    analysis.TranscriptRevisionId ??= generated.RevisionId;
                    changed = true;
                }
            }

            if (analysis.RecordingId is Guid analysisRecordingId &&
                !recordingIds.Contains(analysisRecordingId))
            {
                analysis.RecordingId = null;
                changed = true;
            }

            if (analysis.TranscriptId is not Guid analysisTranscriptId ||
                !transcriptIds.Contains(analysisTranscriptId))
            {
                state.MeetingAnalyses.Remove(analysis);
                changed = true;
                continue;
            }

            if (analysis.TranscriptRevisionId is null)
            {
                analysis.TranscriptRevisionId = state.MeetingTranscripts
                    .First(transcript => transcript.Id == analysisTranscriptId)
                    .RevisionId;
                changed = true;
            }

            var analysisTranscript = state.MeetingTranscripts
                .First(transcript => transcript.Id == analysisTranscriptId);
            if (analysis.RecordingId != analysisTranscript.RecordingId)
            {
                analysis.RecordingId = analysisTranscript.RecordingId;
                changed = true;
            }

            if (analysis.MeetId != analysisTranscript.MeetId)
            {
                analysis.MeetId = analysisTranscript.MeetId;
                changed = true;
            }

            if (analysis.Id == Guid.Empty || !analysisIds.Add(analysis.Id))
            {
                analysis.Id = Guid.NewGuid();
                analysisIds.Add(analysis.Id);
                changed = true;
            }

            if (analysis.MeetId is Guid analysisMeetId && !meetingIds.Contains(analysisMeetId))
            {
                analysis.MeetId = null;
                changed = true;
            }

            analysis.Provider = NormalizeBounded(analysis.Provider, 100);
            analysis.Model = NormalizeBounded(analysis.Model, 100);
            analysis.Summary = NormalizeBounded(analysis.Summary, 100_000);
            analysis.LastError = NormalizeBounded(analysis.LastError, 2_000);
            analysis.Decisions ??= new List<string>();
            analysis.MyActionItems ??= new List<string>();
            analysis.OtherPeopleActionItems ??= new List<string>();
            analysis.WaitingFor ??= new List<string>();
            analysis.Risks ??= new List<string>();
            analysis.QuestionsToClarify ??= new List<string>();
            analysis.Deadlines ??= new List<string>();
            analysis.KeyQuotesOrSourceReferences ??= new List<MeetingSourceReference>();
            analysis.ProposedActions ??= new List<ProposedAction>();
            foreach (var action in analysis.ProposedActions)
            {
                if (action.Id == Guid.Empty)
                {
                    action.Id = Guid.NewGuid();
                    changed = true;
                }

                action.Title = NormalizeBounded(action.Title, 500);
                action.ProjectSuggestion = NormalizeBounded(action.ProjectSuggestion, 500);
                action.WaitingFor = NormalizeBounded(action.WaitingFor, 300);
                action.SourceExcerpt = NormalizeBounded(action.SourceExcerpt, 5_000);
                action.Rationale = NormalizeBounded(action.Rationale, 5_000);
                action.Confidence = Math.Clamp(action.Confidence, 0, 1);
                if (action.ProposedProjectId is Guid actionProjectId &&
                    !projectIds.Contains(actionProjectId))
                {
                    action.ProposedProjectId = null;
                    changed = true;
                }
            }

            if (analysis.CreatedAtUtc == default)
            {
                analysis.CreatedAtUtc = DateTimeOffset.UtcNow;
                changed = true;
            }

            if (analysis.UpdatedAtUtc == default)
            {
                analysis.UpdatedAtUtc = analysis.CreatedAtUtc;
                changed = true;
            }
        }

        var visitState = new Dictionary<Guid, int>();
        foreach (var task in state.Tasks)
        {
            RepairTask(task);
        }

        if (TreeManagerStatePolicy.Normalize(state))
        {
            changed = true;
        }

        if (WorkspaceStatePolicy.Normalize(state))
        {
            changed = true;
        }

        if (state.TelegramCapture.Normalize(state.Projects))
        {
            changed = true;
        }

        return changed;

        void RepairTask(TaskItem task)
        {
            if (task.SourceReferences is not null)
            {
                var originalCount = task.SourceReferences.Count;
                task.SourceReferences = task.SourceReferences
                    .Where(reference =>
                        reference is not null &&
                        (reference.RecordingId is null || recordingIds.Contains(reference.RecordingId.Value)) &&
                        analysisIds.Contains(reference.AnalysisId))
                    .GroupBy(reference => reference.ProposedActionId)
                    .Select(group => group.First())
                    .ToList();
                if (task.SourceReferences.Count != originalCount)
                {
                    changed = true;
                }
            }

            if (visitState.TryGetValue(task.Id, out var currentState))
            {
                if (currentState == 1)
                {
                    task.ParentTaskId = null;
                    changed = true;
                    RepairDirectAssignment(task);
                    visitState[task.Id] = 2;
                }

                return;
            }

            visitState[task.Id] = 1;
            if (task.ParentTaskId.HasValue &&
                tasksById.TryGetValue(task.ParentTaskId.Value, out var parentTask))
            {
                RepairTask(parentTask);
                if (task.ProjectId != parentTask.ProjectId || task.GroupId != parentTask.GroupId)
                {
                    task.ProjectId = parentTask.ProjectId;
                    task.GroupId = parentTask.GroupId;
                    changed = true;
                }
            }
            else
            {
                RepairDirectAssignment(task);
            }

            visitState[task.Id] = 2;
        }

        bool NormalizeRecordingFileNames(MeetingRecording recording)
        {
            var fileChanged = false;
            recording.SystemAudioFile = NormalizeFile(recording.SystemAudioFile, ref fileChanged);
            recording.MicrophoneFile = NormalizeFile(recording.MicrophoneFile, ref fileChanged);
            recording.MixedAudioFile = NormalizeFile(recording.MixedAudioFile, ref fileChanged);
            recording.TranscriptRawFile = NormalizeFile(recording.TranscriptRawFile, ref fileChanged);
            recording.TranscriptFile = NormalizeFile(recording.TranscriptFile, ref fileChanged);
            recording.TranscriptMarkdownFile = NormalizeFile(recording.TranscriptMarkdownFile, ref fileChanged);
            recording.AnalysisFile = NormalizeFile(recording.AnalysisFile, ref fileChanged);
            for (var index = recording.TranscriptionChunkFiles.Count - 1; index >= 0; index--)
            {
                var original = recording.TranscriptionChunkFiles[index];
                var normalized = NormalizeFile(original, ref fileChanged);
                if (normalized.Length == 0)
                {
                    recording.TranscriptionChunkFiles.RemoveAt(index);
                    fileChanged = true;
                }
                else
                {
                    recording.TranscriptionChunkFiles[index] = normalized;
                }
            }

            return fileChanged;
        }

        bool RepairRecordingTracks(MeetingRecording recording)
        {
            var trackChanged = false;
            if (recording.Tracks.Count == 0)
            {
                AddLegacyTrack(
                    MeetingRecordingTrackKind.System,
                    recording.SystemAudioFile,
                    recording.SystemAudioHealth);
                AddLegacyTrack(
                    MeetingRecordingTrackKind.Microphone,
                    recording.MicrophoneFile,
                    recording.MicrophoneHealth);
                AddLegacyTrack(
                    MeetingRecordingTrackKind.Mixed,
                    recording.MixedAudioFile,
                    recording.MixedAudioFile.Length > 0
                        ? AudioTrackHealth.Healthy
                        : AudioTrackHealth.Unknown);
            }

            var seenKinds = new HashSet<MeetingRecordingTrackKind>();
            for (var index = recording.Tracks.Count - 1; index >= 0; index--)
            {
                var track = recording.Tracks[index];
                if (track is null ||
                    !Enum.IsDefined(track.Kind) ||
                    !seenKinds.Add(track.Kind))
                {
                    recording.Tracks.RemoveAt(index);
                    trackChanged = true;
                    continue;
                }

                track.SegmentFiles ??= new List<string>();
                track.FileName = NormalizeFile(track.FileName, ref trackChanged);
                track.InProgressFileName = NormalizeFile(
                    track.InProgressFileName,
                    ref trackChanged);
                for (var segmentIndex = track.SegmentFiles.Count - 1;
                     segmentIndex >= 0;
                     segmentIndex--)
                {
                    var normalized = NormalizeFile(
                        track.SegmentFiles[segmentIndex],
                        ref trackChanged);
                    if (normalized.Length == 0)
                    {
                        track.SegmentFiles.RemoveAt(segmentIndex);
                    }
                    else
                    {
                        track.SegmentFiles[segmentIndex] = normalized;
                    }
                }

                track.Container = NormalizeBounded(track.Container, 40);
                track.Codec = NormalizeBounded(track.Codec, 80);
                track.Error = NormalizeBounded(track.Error, 2_000);
                if (!Enum.IsDefined(track.FinalizationState))
                {
                    track.FinalizationState = MeetingRecordingFinalizationState.Failed;
                    trackChanged = true;
                }

                if (!Enum.IsDefined(track.ValidationState))
                {
                    track.ValidationState = MeetingRecordingValidationState.Invalid;
                    trackChanged = true;
                }

                var sampleRate = Math.Max(0, track.SampleRate);
                var channelCount = Math.Max(0, track.ChannelCount);
                var bitrate = Math.Max(0, track.Bitrate);
                var duration = double.IsFinite(track.DurationSeconds)
                    ? Math.Max(0, track.DurationSeconds)
                    : 0;
                var bytes = Math.Max(0, track.Bytes);
                if (sampleRate != track.SampleRate ||
                    channelCount != track.ChannelCount ||
                    bitrate != track.Bitrate ||
                    duration != track.DurationSeconds ||
                    bytes != track.Bytes)
                {
                    track.SampleRate = sampleRate;
                    track.ChannelCount = channelCount;
                    track.Bitrate = bitrate;
                    track.DurationSeconds = duration;
                    track.Bytes = bytes;
                    trackChanged = true;
                }
            }

            var system = recording.Tracks.FirstOrDefault(track =>
                track.Kind == MeetingRecordingTrackKind.System);
            var microphone = recording.Tracks.FirstOrDefault(track =>
                track.Kind == MeetingRecordingTrackKind.Microphone);
            var mixed = recording.Tracks.FirstOrDefault(track =>
                track.Kind == MeetingRecordingTrackKind.Mixed);
            trackChanged |= SetCompatibilityFile(
                recording.SystemAudioFile,
                system?.FileName,
                value => recording.SystemAudioFile = value);
            trackChanged |= SetCompatibilityFile(
                recording.MicrophoneFile,
                microphone?.FileName,
                value => recording.MicrophoneFile = value);
            trackChanged |= SetCompatibilityFile(
                recording.MixedAudioFile,
                mixed?.FileName,
                value => recording.MixedAudioFile = value);
            return trackChanged;

            void AddLegacyTrack(
                MeetingRecordingTrackKind kind,
                string fileName,
                AudioTrackHealth health)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return;
                }

                var format = Path.GetExtension(fileName)
                    .Equals(".m4a", StringComparison.OrdinalIgnoreCase)
                    ? MeetingRecordingFormat.AacM4a
                    : MeetingRecordingFormat.Wav;
                recording.RecordingFormat = format;
                recording.Tracks.Add(new MeetingRecordingTrackArtifact
                {
                    Kind = kind,
                    FileName = fileName,
                    Container = format == MeetingRecordingFormat.AacM4a ? "MPEG-4" : "WAV",
                    Codec = format == MeetingRecordingFormat.AacM4a ? "AAC-LC" : "PCM",
                    HasAudioFrames = health == AudioTrackHealth.Healthy,
                    FinalizationState = MeetingRecordingFinalizationState.Finalized,
                    ValidationState = MeetingRecordingValidationState.Unknown
                });
                trackChanged = true;
            }

            static bool SetCompatibilityFile(
                string current,
                string? artifactFile,
                Action<string> set)
            {
                if (!string.IsNullOrWhiteSpace(current) ||
                    string.IsNullOrWhiteSpace(artifactFile))
                {
                    return false;
                }

                set(artifactFile);
                return true;
            }
        }

        static MeetingRecordingFormat InferRecordingFormat(MeetingRecording recording)
        {
            return new[]
                {
                    recording.SystemAudioFile,
                    recording.MicrophoneFile,
                    recording.MixedAudioFile
                }
                .Any(path => string.Equals(
                    Path.GetExtension(path),
                    ".m4a",
                    StringComparison.OrdinalIgnoreCase))
                ? MeetingRecordingFormat.AacM4a
                : MeetingRecordingFormat.Wav;
        }

        static string NormalizeFile(string? value, ref bool fileChanged)
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length > 0 &&
                (Path.IsPathRooted(normalized) || normalized.Contains("..", StringComparison.Ordinal)))
            {
                normalized = string.Empty;
            }

            if (value != normalized)
            {
                fileChanged = true;
            }

            return normalized;
        }

        static string NormalizeBounded(string? value, int maximumLength)
        {
            var normalized = value?.Trim() ?? string.Empty;
            return normalized.Length <= maximumLength
                ? normalized
                : normalized[..maximumLength];
        }

        // Removes empty, duplicate, and dangling ids in place.
        static bool RepairIdList(List<Guid> ids, Func<Guid, bool> exists)
        {
            var repaired = ids
                .Where(id => id != Guid.Empty && exists(id))
                .Distinct()
                .ToList();
            if (repaired.Count == ids.Count)
            {
                return false;
            }

            ids.Clear();
            ids.AddRange(repaired);
            return true;
        }

        void RepairDirectAssignment(TaskItem task)
        {
            if (task.GroupId.HasValue &&
                groupsById.TryGetValue(task.GroupId.Value, out var group))
            {
                if (task.ProjectId != group.ProjectId)
                {
                    task.ProjectId = group.ProjectId;
                    changed = true;
                }

                return;
            }

            if (task.GroupId.HasValue)
            {
                task.GroupId = null;
                changed = true;
            }

            if (!task.ProjectId.HasValue || !projectIds.Contains(task.ProjectId.Value))
            {
                task.ProjectId = defaultProject.Id;
                changed = true;
            }
        }
    }

    private static bool MigrateLegacyPlannedWork(AppState state)
    {
        state.TaskWorkSessions ??= new List<TaskWorkSession>();
        var changed = false;
        foreach (var task in state.Tasks)
        {
            if (task.PlannedStartAtUtc is DateTimeOffset legacyStart)
            {
                var duration = task.PlannedDurationMinutes is >=
                                   TaskWorkSessionService.MinimumDurationMinutes and <=
                                   TaskWorkSessionService.MaximumDurationMinutes
                    ? task.PlannedDurationMinutes.Value
                    : TaskWorkSessionService.LegacyDefaultDurationMinutes;
                var startUtc = legacyStart.ToUniversalTime();
                var endUtc = startUtc.AddMinutes(duration);
                if (!state.TaskWorkSessions.Any(session =>
                        session.TaskId == task.Id &&
                        session.StartUtc == startUtc &&
                        session.EndUtc == endUtc))
                {
                    var timestamp = task.UpdatedAtUtc != default
                        ? task.UpdatedAtUtc
                        : task.CreatedAtUtc != default
                            ? task.CreatedAtUtc
                            : startUtc;
                    state.TaskWorkSessions.Add(new TaskWorkSession
                    {
                        Id = Guid.NewGuid(),
                        TaskId = task.Id,
                        StartUtc = startUtc,
                        EndUtc = endUtc,
                        CreatedAtUtc = timestamp,
                        UpdatedAtUtc = timestamp
                    });
                }

                changed = true;
            }
            else if (task.PlannedDurationMinutes is not null)
            {
                changed = true;
            }

            task.PlannedStartAtUtc = null;
            task.PlannedDurationMinutes = null;
        }

        return changed;
    }

    private static string NormalizeManagedFileName(string? value)
    {
        var fileName = value?.Trim() ?? string.Empty;
        return fileName.Length == 0 ||
               Path.IsPathRooted(fileName) ||
               fileName.Contains("..", StringComparison.Ordinal) ||
               !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            ? string.Empty
            : fileName;
    }
}
