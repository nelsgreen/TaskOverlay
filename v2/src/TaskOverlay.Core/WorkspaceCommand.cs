using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace TaskOverlay.Core;

public sealed record WorkspaceCommandResult(
    int SchemaVersion,
    string MessageType,
    string CommandId,
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    string? CreatedTaskId = null,
    string? CreatedSectionId = null,
    string? CreatedMeetingId = null,
    string? CreatedTaskWorkSessionId = null,
    string? CreatedContextSourceId = null,
    string? CreatedContextItemId = null)
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentMessageType = "commandResult";

    public static WorkspaceCommandResult Succeeded(
        string commandId,
        string? createdTaskId = null,
        string? createdSectionId = null,
        string? createdMeetingId = null,
        string? createdTaskWorkSessionId = null,
        string? createdContextSourceId = null,
        string? createdContextItemId = null) =>
        new(
            CurrentSchemaVersion,
            CurrentMessageType,
            commandId,
            Success: true,
            ErrorCode: null,
            ErrorMessage: null,
            createdTaskId,
            createdSectionId,
            createdMeetingId,
            createdTaskWorkSessionId,
            createdContextSourceId,
            createdContextItemId);

    public static WorkspaceCommandResult Failed(
        string commandId,
        string errorCode,
        string errorMessage) =>
        new(
            CurrentSchemaVersion,
            CurrentMessageType,
            commandId,
            Success: false,
            errorCode,
            errorMessage);
}

public static class WorkspaceCommandProcessor
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumCommandIdLength = 128;
    public const int MaximumTitleLength = 500;
    public const int MaximumNotesLength = 100_000;
    public const int MaximumWaitingForLength = 300;

    public static WorkspaceCommandResult Execute(
        AppState state,
        string json,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail(string.Empty, "invalidEnvelope", "Command envelope is required.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail(string.Empty, "invalidEnvelope", "Command envelope must be an object.");
            }

            var commandId = ReadString(root, "commandId") ?? string.Empty;
            if (commandId.Length == 0 || commandId.Length > MaximumCommandIdLength)
            {
                return Fail(commandId, "invalidCommandId", "A valid commandId is required.");
            }

            if (!root.TryGetProperty("schemaVersion", out var schemaVersion) ||
                schemaVersion.ValueKind != JsonValueKind.Number ||
                !schemaVersion.TryGetInt32(out var version) ||
                version != CurrentSchemaVersion)
            {
                return Fail(commandId, "unsupportedSchemaVersion", "Unsupported command schemaVersion.");
            }

            var type = ReadString(root, "type");
            if (string.IsNullOrWhiteSpace(type))
            {
                return Fail(commandId, "invalidCommandType", "Command type is required.");
            }

            if (!root.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object)
            {
                return Fail(commandId, "invalidPayload", "Command payload must be an object.");
            }

            if (type == "updateWorkspaceContext")
            {
                return UpdateWorkspaceContext(state, payload, commandId);
            }

            if (type == "createTask")
            {
                return CreateTask(state, payload, commandId, now ?? DateTimeOffset.UtcNow);
            }

            if (type == "createSection")
            {
                return CreateSection(state, payload, commandId, now ?? DateTimeOffset.UtcNow);
            }

            if (type == "createMeeting")
            {
                return CreateMeeting(state, payload, commandId, now ?? DateTimeOffset.UtcNow);
            }

            if (type == "updateMeeting")
            {
                return UpdateMeeting(state, payload, commandId, now ?? DateTimeOffset.UtcNow);
            }

            if (type == "deleteMeeting")
            {
                return DeleteMeeting(state, payload, commandId);
            }

            if (type == "createTaskWorkSession")
            {
                return CreateTaskWorkSession(
                    state,
                    payload,
                    commandId,
                    now ?? DateTimeOffset.UtcNow);
            }

            if (type == "updateTaskWorkSession")
            {
                return UpdateTaskWorkSession(
                    state,
                    payload,
                    commandId,
                    now ?? DateTimeOffset.UtcNow);
            }

            if (type == "deleteTaskWorkSession")
            {
                return DeleteTaskWorkSession(state, payload, commandId);
            }

            if (type == "renameSection")
            {
                return RenameSection(state, payload, commandId, now ?? DateTimeOffset.UtcNow);
            }

            if (type == "deleteSection")
            {
                return DeleteSection(state, payload, commandId, now ?? DateTimeOffset.UtcNow);
            }

            if (type is "createContextSource" or "updateContextSource" or "deleteContextSource" or
                "createContextItem" or "updateContextItem" or "deleteContextItem" or
                "linkContextItemToTask" or "unlinkContextItemFromTask" or
                "linkContextItemToMeeting" or "unlinkContextItemFromMeeting" or
                "linkSourceToTask" or "unlinkSourceFromTask" or
                "linkSourceToMeeting" or "unlinkSourceFromMeeting")
            {
                return ExecuteContextCommand(state, type, payload, commandId, now ?? DateTimeOffset.UtcNow);
            }

            var taskIdText = ReadString(payload, "taskId");
            if (!Guid.TryParse(taskIdText, out var taskId))
            {
                return Fail(commandId, "invalidTaskId", "A valid taskId is required.");
            }

            var task = state.Tasks.FirstOrDefault(candidate => candidate.Id == taskId);
            if (task is null)
            {
                return Fail(commandId, "taskNotFound", "The requested task does not exist.");
            }

            var timestamp = now ?? DateTimeOffset.UtcNow;
            var treeService = new TreeStateService(state);
            return type switch
            {
                "updateTaskStatus" => UpdateStatus(
                    treeService,
                    taskId,
                    payload,
                    commandId,
                    timestamp),
                "updateTaskPinToPanel" => UpdatePin(
                    treeService,
                    taskId,
                    payload,
                    commandId,
                    timestamp),
                "updateTaskNotes" => UpdateNotes(
                    treeService,
                    taskId,
                    payload,
                    commandId,
                    timestamp),
                "updateTaskTitle" => UpdateTitle(
                    treeService,
                    taskId,
                    payload,
                    commandId,
                    timestamp),
                "updateTaskWaitingFor" => UpdateWaitingFor(
                    treeService,
                    taskId,
                    payload,
                    commandId,
                    timestamp),
                "updateTaskReminder" => UpdateReminder(
                    task,
                    payload,
                    commandId,
                    timestamp),
                "updateTaskDeadline" => UpdateDeadline(
                    treeService,
                    taskId,
                    payload,
                    commandId,
                    timestamp),
                "addTaskCheckpoints" => AddCheckpoints(
                    task,
                    payload,
                    commandId,
                    timestamp),
                "updateTaskCheckpointTitle" => UpdateCheckpointTitle(
                    task,
                    payload,
                    commandId,
                    timestamp),
                "toggleTaskCheckpoint" => ToggleCheckpoint(
                    task,
                    payload,
                    commandId,
                    timestamp),
                "deleteTaskCheckpoint" => DeleteCheckpoint(
                    task,
                    payload,
                    commandId,
                    timestamp),
                "reorderTaskCheckpoint" => ReorderCheckpoint(
                    task,
                    payload,
                    commandId,
                    timestamp),
                "moveTask" => MoveTask(
                    treeService,
                    taskId,
                    payload,
                    commandId,
                    timestamp),
                "deleteTask" => DeleteTask(
                    treeService,
                    taskId,
                    commandId,
                    timestamp),
                _ => Fail(commandId, "unknownCommandType", "Unknown Workspace command type.")
            };
        }
        catch (JsonException)
        {
            return Fail(string.Empty, "invalidJson", "Command JSON is invalid.");
        }
        catch (Exception)
        {
            return Fail(string.Empty, "commandFailed", "Workspace command could not be applied.");
        }
    }

    private static WorkspaceCommandResult UpdateStatus(
        TreeStateService treeService,
        Guid taskId,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        var statusText = ReadString(payload, "status");
        var status = statusText switch
        {
            "TODO" => TreeNodeStatus.Todo,
            "FOCUS" => TreeNodeStatus.Focus,
            "WAIT" => TreeNodeStatus.Wait,
            "DONE" => TreeNodeStatus.Done,
            _ => (TreeNodeStatus?)null
        };
        if (status is null)
        {
            return Fail(commandId, "invalidStatus", "Status must be TODO, FOCUS, WAIT, or DONE.");
        }

        return treeService.MarkStatus(taskId, status.Value, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Task status could not be updated.");
    }

    private static WorkspaceCommandResult UpdatePin(
        TreeStateService treeService,
        Guid taskId,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!payload.TryGetProperty("pinToPanel", out var pin) ||
            pin.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return Fail(commandId, "invalidPayload", "pinToPanel must be a boolean.");
        }

        return treeService.SetPinToPanel(taskId, pin.GetBoolean(), timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Task panel pin could not be updated.");
    }

    private static WorkspaceCommandResult UpdateNotes(
        TreeStateService treeService,
        Guid taskId,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        var notes = ReadString(payload, "notes");
        if (notes is null || notes.Length > MaximumNotesLength)
        {
            return Fail(commandId, "invalidPayload", "Task notes are invalid or too long.");
        }

        return treeService.SetDescription(taskId, notes, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Task notes could not be updated.");
    }

    private static WorkspaceCommandResult UpdateTitle(
        TreeStateService treeService,
        Guid taskId,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        var title = ReadString(payload, "title");
        if (string.IsNullOrWhiteSpace(title) || title.Length > MaximumTitleLength)
        {
            return Fail(commandId, "invalidPayload", "Task title is required and must not be too long.");
        }

        return treeService.RenameNode(taskId, title, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Task title could not be updated.");
    }

    private static WorkspaceCommandResult UpdateWaitingFor(
        TreeStateService treeService,
        Guid taskId,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        var waitingFor = ReadString(payload, "waitingFor");
        if (waitingFor is null || waitingFor.Length > MaximumWaitingForLength)
        {
            return Fail(commandId, "invalidPayload", "Task waitingFor is invalid or too long.");
        }

        return treeService.SetWaitingFor(taskId, waitingFor, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Task waitingFor could not be updated.");
    }

    private static WorkspaceCommandResult UpdateReminder(
        TaskItem task,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!TryReadOptionalIsoTimestamp(payload, "remindAtUtc", out var remindAtUtc))
        {
            return Fail(commandId, "invalidPayload", "remindAtUtc must be an ISO-8601 timestamp or null.");
        }

        int? repeatMinutes = null;
        if (payload.TryGetProperty("remindEveryMinutes", out var repeatElement) &&
            repeatElement.ValueKind != JsonValueKind.Null)
        {
            if (repeatElement.ValueKind != JsonValueKind.Number ||
                !repeatElement.TryGetInt32(out var repeatValue) ||
                repeatValue <= 0)
            {
                return Fail(commandId, "invalidPayload", "remindEveryMinutes must be a positive integer or null.");
            }

            repeatMinutes = repeatValue;
        }

        ReminderService.SetSchedule(task, remindAtUtc, repeatMinutes, timestamp);
        return WorkspaceCommandResult.Succeeded(commandId);
    }

    private static WorkspaceCommandResult UpdateDeadline(
        TreeStateService treeService,
        Guid taskId,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!TryReadOptionalIsoTimestamp(payload, "deadlineAtUtc", out var deadlineAtUtc))
        {
            return Fail(commandId, "invalidPayload", "deadlineAtUtc must be an ISO-8601 timestamp or null.");
        }

        return treeService.SetDeadline(taskId, deadlineAtUtc, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Task deadline could not be updated.");
    }

    private static WorkspaceCommandResult AddCheckpoints(
        TaskItem task,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!payload.TryGetProperty("titles", out var titlesElement) ||
            titlesElement.ValueKind != JsonValueKind.Array)
        {
            return Fail(commandId, "invalidPayload", "titles must be an array of step titles.");
        }

        var titles = new List<string>();
        foreach (var element in titlesElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                return Fail(commandId, "invalidPayload", "Each step title must be a string.");
            }

            titles.Add(element.GetString() ?? string.Empty);
        }

        // Blank lines from a multiline paste are skipped by the service; the
        // command only fails when nothing at all could be added.
        var created = CheckpointService.Add(task, titles, timestamp);
        return created.Count > 0
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "invalidPayload", "At least one non-empty step title is required.");
    }

    private static WorkspaceCommandResult UpdateCheckpointTitle(
        TaskItem task,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!TryReadCheckpointId(payload, out var checkpointId))
        {
            return Fail(commandId, "invalidCheckpointId", "A valid checkpointId is required.");
        }

        var title = ReadString(payload, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return Fail(commandId, "invalidPayload", "Step title must not be empty.");
        }

        return CheckpointService.Rename(task, checkpointId, title, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "checkpointNotFound", "The requested step does not exist.");
    }

    private static WorkspaceCommandResult ToggleCheckpoint(
        TaskItem task,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!TryReadCheckpointId(payload, out var checkpointId))
        {
            return Fail(commandId, "invalidCheckpointId", "A valid checkpointId is required.");
        }

        if (!payload.TryGetProperty("done", out var done) ||
            done.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return Fail(commandId, "invalidPayload", "done must be a boolean.");
        }

        return CheckpointService.Toggle(task, checkpointId, done.GetBoolean(), timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "checkpointNotFound", "The requested step does not exist.");
    }

    private static WorkspaceCommandResult DeleteCheckpoint(
        TaskItem task,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!TryReadCheckpointId(payload, out var checkpointId))
        {
            return Fail(commandId, "invalidCheckpointId", "A valid checkpointId is required.");
        }

        return CheckpointService.Delete(task, checkpointId, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "checkpointNotFound", "The requested step does not exist.");
    }

    private static WorkspaceCommandResult ReorderCheckpoint(
        TaskItem task,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!TryReadCheckpointId(payload, out var checkpointId))
        {
            return Fail(commandId, "invalidCheckpointId", "A valid checkpointId is required.");
        }

        if (!payload.TryGetProperty("targetIndex", out var indexElement) ||
            indexElement.ValueKind != JsonValueKind.Number ||
            !indexElement.TryGetInt32(out var targetIndex) ||
            targetIndex < 0)
        {
            return Fail(commandId, "invalidPayload", "targetIndex must be a non-negative integer.");
        }

        return CheckpointService.Move(task, checkpointId, targetIndex, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "checkpointNotFound", "The requested step does not exist.");
    }

    private static bool TryReadCheckpointId(JsonElement payload, out Guid checkpointId) =>
        Guid.TryParse(ReadString(payload, "checkpointId"), out checkpointId) &&
        checkpointId != Guid.Empty;

    private static WorkspaceCommandResult CreateTask(
        AppState state,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        var title = ReadString(payload, "title");
        var isDraft = payload.TryGetProperty("draft", out var draftElement) &&
                      draftElement.ValueKind == JsonValueKind.True;
        if ((!isDraft && string.IsNullOrWhiteSpace(title)) ||
            (title?.Length ?? 0) > MaximumTitleLength)
        {
            return Fail(commandId, "invalidPayload", "Task title is required and must not be too long.");
        }

        if (isDraft && !string.IsNullOrWhiteSpace(title))
        {
            return Fail(commandId, "invalidPayload", "A draft task must start with an empty title.");
        }

        // A subtask is a task created under another task: parentTaskId wins over
        // the project/section fields and makes the new task inherit the parent's
        // project and section (TreeStateService.CreateTask handles this).
        Guid parentId;
        var parentTaskIdText = ReadString(payload, "parentTaskId");
        if (!string.IsNullOrEmpty(parentTaskIdText))
        {
            if (!Guid.TryParse(parentTaskIdText, out var parentTaskId))
            {
                return Fail(commandId, "invalidPayload", "parentTaskId must be a valid task id.");
            }

            if (!state.Tasks.Any(candidate => candidate.Id == parentTaskId))
            {
                return Fail(commandId, "mutationRejected", "The parent task does not exist.");
            }

            parentId = parentTaskId;
        }
        else if (!TryResolveSectionParent(payload, out parentId))
        {
            return Fail(commandId, "invalidPayload", "A valid projectId or sectionId is required.");
        }

        if (!TryReadOptionalInitialWorkSession(
                payload,
                out var workSessionStartUtc,
                out var workSessionEndUtc))
        {
            return Fail(
                commandId,
                "invalidPayload",
                "workSessionStartUtc/workSessionEndUtc must be valid ISO-8601 timestamps with end after start.");
        }

        var treeService = new TreeStateService(state);
        var created = isDraft
            ? treeService.CreateDraftTask(parentId, timestamp)
            : treeService.CreateTask(parentId, title, timestamp);
        if (created is null)
        {
            return Fail(commandId, "mutationRejected", "Task could not be created in the given location.");
        }

        TaskWorkSession? workSession = null;
        if (workSessionStartUtc is not null && workSessionEndUtc is not null)
        {
            workSession = new TaskWorkSessionService(state).Create(
                created.Id,
                workSessionStartUtc.Value,
                workSessionEndUtc.Value,
                now: timestamp);
            if (workSession is null)
            {
                state.Tasks.RemoveAll(task => task.Id == created.Id);
                return Fail(
                    commandId,
                    "mutationRejected",
                    "Task work session could not be created.");
            }
        }

        return WorkspaceCommandResult.Succeeded(
            commandId,
            created.Id.ToString("N"),
            createdTaskWorkSessionId: workSession?.Id.ToString("N"));
    }

    private static bool TryReadOptionalInitialWorkSession(
        JsonElement payload,
        out DateTimeOffset? startUtc,
        out DateTimeOffset? endUtc)
    {
        startUtc = null;
        endUtc = null;
        var hasStart = payload.TryGetProperty("workSessionStartUtc", out var startElement);
        var hasEnd = payload.TryGetProperty("workSessionEndUtc", out var endElement);
        if (!hasStart && !hasEnd)
        {
            return true;
        }

        if (!hasStart ||
            startElement.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                startElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedStart) ||
            !hasEnd ||
            endElement.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                endElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedEnd) ||
            !TaskWorkSessionService.IsValidRange(parsedStart, parsedEnd))
        {
            return false;
        }

        startUtc = parsedStart;
        endUtc = parsedEnd;
        return true;
    }

    private static WorkspaceCommandResult CreateTaskWorkSession(
        AppState state,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!Guid.TryParse(ReadString(payload, "taskId"), out var taskId) ||
            taskId == Guid.Empty)
        {
            return Fail(commandId, "invalidTaskId", "A valid taskId is required.");
        }

        if (!state.Tasks.Any(task => task.Id == taskId))
        {
            return Fail(commandId, "taskNotFound", "The requested task does not exist.");
        }

        if (!TryReadRequiredWorkSessionRange(payload, out var startUtc, out var endUtc))
        {
            return Fail(
                commandId,
                "invalidPayload",
                "startUtc/endUtc must be valid ISO-8601 timestamps with end after start.");
        }

        if (!TryReadOptionalWorkSessionNote(payload, out var note))
        {
            return Fail(commandId, "invalidPayload", "note is invalid or too long.");
        }

        var session = new TaskWorkSessionService(state).Create(
            taskId,
            startUtc,
            endUtc,
            note,
            timestamp);
        return session is not null
            ? WorkspaceCommandResult.Succeeded(
                commandId,
                createdTaskWorkSessionId: session.Id.ToString("N"))
            : Fail(commandId, "mutationRejected", "Task work session could not be created.");
    }

    private static WorkspaceCommandResult UpdateTaskWorkSession(
        AppState state,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!Guid.TryParse(ReadString(payload, "sessionId"), out var sessionId) ||
            sessionId == Guid.Empty)
        {
            return Fail(commandId, "invalidSessionId", "A valid sessionId is required.");
        }

        var session = state.TaskWorkSessions.FirstOrDefault(item => item.Id == sessionId);
        if (session is null)
        {
            return Fail(commandId, "sessionNotFound", "The requested task work session does not exist.");
        }

        var startUtc = session.StartUtc;
        var endUtc = session.EndUtc;
        var note = session.Note;
        var hasPatch = false;
        if (payload.TryGetProperty("startUtc", out var startElement))
        {
            hasPatch = true;
            if (!TryParseIsoTimestamp(startElement, out startUtc))
            {
                return Fail(commandId, "invalidPayload", "startUtc must be an ISO-8601 timestamp.");
            }
        }

        if (payload.TryGetProperty("endUtc", out var endElement))
        {
            hasPatch = true;
            if (!TryParseIsoTimestamp(endElement, out endUtc))
            {
                return Fail(commandId, "invalidPayload", "endUtc must be an ISO-8601 timestamp.");
            }
        }

        if (payload.TryGetProperty("note", out var noteElement))
        {
            hasPatch = true;
            if (noteElement.ValueKind == JsonValueKind.Null)
            {
                note = string.Empty;
            }
            else if (noteElement.ValueKind != JsonValueKind.String ||
                     (noteElement.GetString()?.Length ?? 0) > TaskWorkSessionService.MaximumNoteLength)
            {
                return Fail(commandId, "invalidPayload", "note is invalid or too long.");
            }
            else
            {
                note = noteElement.GetString() ?? string.Empty;
            }
        }

        if (!hasPatch || !TaskWorkSessionService.IsValidRange(startUtc, endUtc))
        {
            return Fail(commandId, "invalidPayload", "A valid session patch is required.");
        }

        return new TaskWorkSessionService(state).Update(
                sessionId,
                startUtc,
                endUtc,
                note,
                timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Task work session could not be updated.");
    }

    private static WorkspaceCommandResult DeleteTaskWorkSession(
        AppState state,
        JsonElement payload,
        string commandId)
    {
        if (!Guid.TryParse(ReadString(payload, "sessionId"), out var sessionId) ||
            sessionId == Guid.Empty)
        {
            return Fail(commandId, "invalidSessionId", "A valid sessionId is required.");
        }

        return new TaskWorkSessionService(state).Delete(sessionId)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "sessionNotFound", "The requested task work session does not exist.");
    }

    private static bool TryReadRequiredWorkSessionRange(
        JsonElement payload,
        out DateTimeOffset startUtc,
        out DateTimeOffset endUtc)
    {
        startUtc = default;
        endUtc = default;
        return payload.TryGetProperty("startUtc", out var startElement) &&
               TryParseIsoTimestamp(startElement, out startUtc) &&
               payload.TryGetProperty("endUtc", out var endElement) &&
               TryParseIsoTimestamp(endElement, out endUtc) &&
               TaskWorkSessionService.IsValidRange(startUtc, endUtc);
    }

    private static bool TryReadOptionalWorkSessionNote(
        JsonElement payload,
        out string? note)
    {
        note = null;
        if (!payload.TryGetProperty("note", out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        note = element.GetString();
        return (note?.Length ?? 0) <= TaskWorkSessionService.MaximumNoteLength;
    }

    private static bool TryParseIsoTimestamp(
        JsonElement element,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        return element.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(
            element.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out timestamp);
    }

    private static WorkspaceCommandResult MoveTask(
        TreeStateService treeService,
        Guid taskId,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        // Reuse the createTask location resolver: the Details Location control
        // sends a snapshot section id (group:{id} or project:{id}:root), which
        // resolves to the group or project the task should move under.
        if (!TryResolveSectionParent(payload, out var newParentId))
        {
            return Fail(commandId, "invalidPayload", "A valid sectionId or projectId is required.");
        }

        return treeService.MoveNode(taskId, newParentId, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Task could not be moved to the given location.");
    }

    private static WorkspaceCommandResult DeleteTask(
        TreeStateService treeService,
        Guid taskId,
        string commandId,
        DateTimeOffset timestamp)
    {
        // TreeStateService.DeleteNode reparents any subtasks up to the deleted
        // task's parent (project/section/task); it does not cascade-delete them.
        return treeService.DeleteNode(taskId, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Task could not be deleted.");
    }

    private static WorkspaceCommandResult CreateSection(
        AppState state,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        var title = ReadString(payload, "title");
        if (string.IsNullOrWhiteSpace(title) || title.Length > MaximumTitleLength)
        {
            return Fail(commandId, "invalidPayload", "Section title is required and must not be too long.");
        }

        var projectIdText = ReadString(payload, "projectId");
        if (!Guid.TryParse(projectIdText, out var projectId))
        {
            return Fail(commandId, "invalidPayload", "A valid projectId is required.");
        }

        var treeService = new TreeStateService(state);
        var created = treeService.CreateGroup(projectId, title, timestamp);
        return created is not null
            ? WorkspaceCommandResult.Succeeded(
                commandId,
                createdSectionId: $"group:{created.Id.ToString("N")}")
            : Fail(commandId, "mutationRejected", "Section could not be created in the given project.");
    }

    private static WorkspaceCommandResult RenameSection(
        AppState state,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        var title = ReadString(payload, "title");
        if (string.IsNullOrWhiteSpace(title) || title.Length > MaximumTitleLength)
        {
            return Fail(commandId, "invalidPayload", "Section title is required and must not be too long.");
        }

        if (!TryResolveGroupSection(payload, out var groupId))
        {
            return Fail(commandId, "invalidPayload", "A valid editable sectionId is required.");
        }

        var treeService = new TreeStateService(state);
        return treeService.RenameNode(groupId, title, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Section could not be renamed.");
    }

    private static WorkspaceCommandResult DeleteSection(
        AppState state,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!TryResolveGroupSection(payload, out var groupId))
        {
            return Fail(commandId, "invalidPayload", "A valid editable sectionId is required.");
        }

        var treeService = new TreeStateService(state);
        return treeService.DeleteNode(groupId, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Section could not be deleted.");
    }

    private static bool TryResolveGroupSection(JsonElement payload, out Guid groupId)
    {
        groupId = Guid.Empty;
        var sectionId = ReadString(payload, "sectionId");
        const string groupPrefix = "group:";
        return sectionId is not null &&
               sectionId.StartsWith(groupPrefix, StringComparison.Ordinal) &&
               Guid.TryParse(sectionId[groupPrefix.Length..], out groupId) &&
               groupId != Guid.Empty;
    }

    private static bool TryResolveSectionParent(JsonElement payload, out Guid parentId)
    {
        parentId = Guid.Empty;
        var sectionId = ReadString(payload, "sectionId");
        if (!string.IsNullOrEmpty(sectionId))
        {
            const string groupPrefix = "group:";
            const string projectRootPrefix = "project:";
            const string projectRootSuffix = ":root";
            if (sectionId.StartsWith(groupPrefix, StringComparison.Ordinal))
            {
                return Guid.TryParse(sectionId[groupPrefix.Length..], out parentId);
            }

            if (sectionId.StartsWith(projectRootPrefix, StringComparison.Ordinal) &&
                sectionId.EndsWith(projectRootSuffix, StringComparison.Ordinal))
            {
                var inner = sectionId[projectRootPrefix.Length..^projectRootSuffix.Length];
                return Guid.TryParse(inner, out parentId);
            }

            return false;
        }

        var projectId = ReadString(payload, "projectId");
        return Guid.TryParse(projectId, out parentId);
    }

    private static bool TryReadOptionalIsoTimestamp(
        JsonElement payload,
        string propertyName,
        out DateTimeOffset? value)
    {
        value = null;
        if (!payload.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static WorkspaceCommandResult UpdateWorkspaceContext(
        AppState state,
        JsonElement payload,
        string commandId)
    {
        var activeTab = ReadString(payload, "activeTab") switch
        {
            "tree" => WorkspaceTab.Tree,
            "status" => WorkspaceTab.Status,
            "timeline" => WorkspaceTab.Timeline,
            "calendar" => WorkspaceTab.Calendar,
            "workstreams" => WorkspaceTab.Workstreams,
            "contexthub" => WorkspaceTab.ContextHub,
            _ => (WorkspaceTab?)null
        };
        var filter = ReadString(payload, "filter") switch
        {
            "all" => WorkspaceFilter.All,
            "active" => WorkspaceFilter.Active,
            "active-path" => WorkspaceFilter.ActivePath,
            _ => (WorkspaceFilter?)null
        };
        if (activeTab is null || filter is null ||
            !TryReadGuidArray(payload, "selectedProjectIds", out var selectedProjectIds) ||
            !TryReadOptionalGuid(payload, "selectedTaskId", out var selectedTaskId) ||
            !TryReadOptionalString(payload, "selectedTimelineItemId", out var selectedTimelineItemId) ||
            !TryReadOptionalString(payload, "selectedWorkstreamId", out var selectedWorkstreamId))
        {
            return Fail(commandId, "invalidPayload", "Workspace context payload is invalid.");
        }

        var activeNowCollapsed = state.WorkspaceSettings?.ActiveNowCollapsed ?? false;
        if (payload.TryGetProperty("activeNowCollapsed", out var collapsedElement))
        {
            if (collapsedElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                return Fail(commandId, "invalidPayload", "Workspace context payload is invalid.");
            }

            activeNowCollapsed = collapsedElement.GetBoolean();
        }

        state.WorkspaceSettings = new WorkspaceSettings
        {
            ActiveTab = activeTab.Value,
            SelectedProjectIds = selectedProjectIds,
            SelectedTaskId = selectedTaskId,
            SelectedTimelineItemId = selectedTimelineItemId,
            SelectedWorkstreamId = selectedWorkstreamId,
            Filter = filter.Value,
            ActiveNowCollapsed = activeNowCollapsed
        };
        WorkspaceStatePolicy.Normalize(state);
        return WorkspaceCommandResult.Succeeded(commandId);
    }

    private static WorkspaceCommandResult CreateMeeting(
        AppState state,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!TryReadMeetingInput(payload, existing: null, out var input))
        {
            return Fail(commandId, "invalidPayload", "Meeting payload is invalid.");
        }

        var created = new MeetingService(state).Create(input, timestamp);
        return created is null
            ? Fail(commandId, "invalidMeeting", "Meeting project, linked task, or fields are invalid.")
            : WorkspaceCommandResult.Succeeded(
                commandId,
                createdMeetingId: created.Id.ToString("N"));
    }

    private static WorkspaceCommandResult UpdateMeeting(
        AppState state,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!Guid.TryParse(ReadString(payload, "meetingId"), out var meetingId))
        {
            return Fail(commandId, "invalidMeetingId", "A valid meetingId is required.");
        }

        var meeting = state.Meetings?.FirstOrDefault(item => item.Id == meetingId);
        if (meeting is null)
        {
            return Fail(commandId, "meetingNotFound", "The requested meeting does not exist.");
        }

        if (!TryReadMeetingInput(payload, meeting, out var input))
        {
            return Fail(commandId, "invalidPayload", "Meeting patch is invalid.");
        }

        return new MeetingService(state).Update(meetingId, input, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "invalidMeeting", "Meeting project, linked task, or fields are invalid.");
    }

    private static WorkspaceCommandResult DeleteMeeting(
        AppState state,
        JsonElement payload,
        string commandId)
    {
        if (!Guid.TryParse(ReadString(payload, "meetingId"), out var meetingId))
        {
            return Fail(commandId, "invalidMeetingId", "A valid meetingId is required.");
        }

        return new MeetingService(state).Delete(meetingId)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "meetingNotFound", "The requested meeting does not exist.");
    }

    private static WorkspaceCommandResult ExecuteContextCommand(
        AppState state,
        string type,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        var service = new ContextService(state);
        switch (type)
        {
            case "createContextSource":
            {
                if (!TryReadSourceInput(payload, existing: null, timestamp, out var input))
                {
                    return Fail(commandId, "invalidPayload", "Context source payload is invalid.");
                }

                var created = service.CreateSource(input, timestamp);
                return created is null
                    ? Fail(commandId, "invalidContextSource", "Context source project, links, or fields are invalid.")
                    : WorkspaceCommandResult.Succeeded(
                        commandId,
                        createdContextSourceId: created.Id.ToString("N"));
            }

            case "updateContextSource":
            {
                if (!Guid.TryParse(ReadString(payload, "sourceId"), out var sourceId))
                {
                    return Fail(commandId, "invalidContextSourceId", "A valid sourceId is required.");
                }

                var existing = state.ContextSources.FirstOrDefault(source => source.Id == sourceId);
                if (existing is null)
                {
                    return Fail(commandId, "contextSourceNotFound", "The requested context source does not exist.");
                }

                if (!TryReadSourceInput(payload, existing, timestamp, out var input))
                {
                    return Fail(commandId, "invalidPayload", "Context source patch is invalid.");
                }

                return service.UpdateSource(sourceId, input, timestamp)
                    ? WorkspaceCommandResult.Succeeded(commandId)
                    : Fail(commandId, "invalidContextSource", "Context source project, links, or fields are invalid.");
            }

            case "deleteContextSource":
            {
                if (!Guid.TryParse(ReadString(payload, "sourceId"), out var sourceId))
                {
                    return Fail(commandId, "invalidContextSourceId", "A valid sourceId is required.");
                }

                return service.DeleteSource(sourceId, timestamp)
                    ? WorkspaceCommandResult.Succeeded(commandId)
                    : Fail(commandId, "contextSourceNotFound", "The requested context source does not exist.");
            }

            case "createContextItem":
            {
                if (!TryReadItemInput(payload, existing: null, out var input))
                {
                    return Fail(commandId, "invalidPayload", "Context item payload is invalid.");
                }

                var created = service.CreateItem(input, timestamp);
                return created is null
                    ? Fail(commandId, "invalidContextItem", "Context item project, links, or fields are invalid.")
                    : WorkspaceCommandResult.Succeeded(
                        commandId,
                        createdContextItemId: created.Id.ToString("N"));
            }

            case "updateContextItem":
            {
                if (!Guid.TryParse(ReadString(payload, "itemId"), out var itemId))
                {
                    return Fail(commandId, "invalidContextItemId", "A valid itemId is required.");
                }

                var existing = state.ContextItems.FirstOrDefault(item => item.Id == itemId);
                if (existing is null)
                {
                    return Fail(commandId, "contextItemNotFound", "The requested context item does not exist.");
                }

                if (!TryReadItemInput(payload, existing, out var input))
                {
                    return Fail(commandId, "invalidPayload", "Context item patch is invalid.");
                }

                return service.UpdateItem(itemId, input, timestamp)
                    ? WorkspaceCommandResult.Succeeded(commandId)
                    : Fail(commandId, "invalidContextItem", "Context item project, links, or fields are invalid.");
            }

            case "deleteContextItem":
            {
                if (!Guid.TryParse(ReadString(payload, "itemId"), out var itemId))
                {
                    return Fail(commandId, "invalidContextItemId", "A valid itemId is required.");
                }

                return service.DeleteItem(itemId)
                    ? WorkspaceCommandResult.Succeeded(commandId)
                    : Fail(commandId, "contextItemNotFound", "The requested context item does not exist.");
            }

            default:
            {
                // Link/unlink family: {itemId|sourceId} + {taskId|meetingId}.
                var ownerIsItem = type.Contains("ContextItem");
                var ownerKey = ownerIsItem ? "itemId" : "sourceId";
                if (!Guid.TryParse(ReadString(payload, ownerKey), out var ownerId))
                {
                    return Fail(
                        commandId,
                        ownerIsItem ? "invalidContextItemId" : "invalidContextSourceId",
                        $"A valid {ownerKey} is required.");
                }

                var targetIsTask = type.EndsWith("Task", StringComparison.Ordinal);
                var targetKey = targetIsTask ? "taskId" : "meetingId";
                if (!Guid.TryParse(ReadString(payload, targetKey), out var targetId))
                {
                    return Fail(
                        commandId,
                        targetIsTask ? "invalidTaskId" : "invalidMeetingId",
                        $"A valid {targetKey} is required.");
                }

                var succeeded = type switch
                {
                    "linkContextItemToTask" => service.LinkItemToTask(ownerId, targetId, timestamp),
                    "unlinkContextItemFromTask" => service.UnlinkItemFromTask(ownerId, targetId, timestamp),
                    "linkContextItemToMeeting" => service.LinkItemToMeeting(ownerId, targetId, timestamp),
                    "unlinkContextItemFromMeeting" => service.UnlinkItemFromMeeting(ownerId, targetId, timestamp),
                    "linkSourceToTask" => service.LinkSourceToTask(ownerId, targetId, timestamp),
                    "unlinkSourceFromTask" => service.UnlinkSourceFromTask(ownerId, targetId, timestamp),
                    "linkSourceToMeeting" => service.LinkSourceToMeeting(ownerId, targetId, timestamp),
                    "unlinkSourceFromMeeting" => service.UnlinkSourceFromMeeting(ownerId, targetId, timestamp),
                    _ => false
                };
                return succeeded
                    ? WorkspaceCommandResult.Succeeded(commandId)
                    : Fail(commandId, "mutationRejected", "Context link could not be updated.");
            }
        }
    }

    private static bool TryReadSourceInput(
        JsonElement payload,
        SourceDocument? existing,
        DateTimeOffset timestamp,
        out SourceDocumentUpdate input)
    {
        input = default!;

        var projectId = existing?.ProjectId ?? Guid.Empty;
        if (payload.TryGetProperty("projectId", out var projectElement) &&
            (projectElement.ValueKind != JsonValueKind.String ||
             !Guid.TryParse(projectElement.GetString(), out projectId)))
        {
            return false;
        }

        var sourceType = existing?.SourceType ?? ContextSourceType.ManualNote;
        if (payload.TryGetProperty("sourceType", out var typeElement))
        {
            if (typeElement.ValueKind != JsonValueKind.String ||
                !TryParseSourceType(typeElement.GetString(), out sourceType))
            {
                return false;
            }
        }

        var sourceApp = existing?.SourceApp;
        if (payload.TryGetProperty("sourceApp", out var appElement))
        {
            if (appElement.ValueKind == JsonValueKind.Null)
            {
                sourceApp = null;
            }
            else if (appElement.ValueKind != JsonValueKind.String ||
                     !TryParseSourceApp(appElement.GetString(), out var parsedApp))
            {
                return false;
            }
            else
            {
                sourceApp = parsedApp;
            }
        }

        if (!TryReadPatchString(payload, "title", existing?.Title ?? string.Empty, out var title) ||
            !TryReadPatchString(payload, "body", existing?.Body ?? string.Empty, out var body) ||
            !TryReadPatchString(payload, "summary", existing?.Summary ?? string.Empty, out var summary))
        {
            return false;
        }

        // A JSON null title is not a valid clear (title is required); null
        // body/summary clear to empty via the service normalization.
        if (title is null)
        {
            return false;
        }

        var sourceDateUtc = existing?.SourceDateUtc ?? timestamp;
        if (payload.TryGetProperty("sourceDateUtc", out var dateElement))
        {
            if (dateElement.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(
                    dateElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out sourceDateUtc))
            {
                return false;
            }
        }

        if (!TryReadPatchGuidArray(payload, "linkedTaskIds", existing?.LinkedTaskIds, out var linkedTaskIds) ||
            !TryReadPatchGuidArray(payload, "linkedMeetingIds", existing?.LinkedMeetingIds, out var linkedMeetingIds))
        {
            return false;
        }

        input = new SourceDocumentUpdate(
            projectId,
            sourceType,
            sourceApp,
            title,
            body,
            summary,
            sourceDateUtc,
            linkedTaskIds,
            linkedMeetingIds);
        return true;
    }

    private static bool TryReadItemInput(
        JsonElement payload,
        ContextItem? existing,
        out ContextItemUpdate input)
    {
        input = default!;

        var projectId = existing?.ProjectId ?? Guid.Empty;
        if (payload.TryGetProperty("projectId", out var projectElement) &&
            (projectElement.ValueKind != JsonValueKind.String ||
             !Guid.TryParse(projectElement.GetString(), out projectId)))
        {
            return false;
        }

        var itemType = existing?.ItemType ?? ContextItemType.Note;
        if (payload.TryGetProperty("itemType", out var typeElement))
        {
            if (typeElement.ValueKind != JsonValueKind.String ||
                !TryParseItemType(typeElement.GetString(), out itemType))
            {
                return false;
            }
        }

        var status = existing?.Status ?? ContextItemStatus.Active;
        if (payload.TryGetProperty("status", out var statusElement))
        {
            if (statusElement.ValueKind != JsonValueKind.String ||
                !TryParseItemStatus(statusElement.GetString(), out status))
            {
                return false;
            }
        }

        if (!TryReadPatchString(payload, "title", existing?.Title ?? string.Empty, out var title) ||
            !TryReadPatchString(payload, "body", existing?.Body ?? string.Empty, out var body))
        {
            return false;
        }

        if (title is null)
        {
            return false;
        }

        if (!TryReadPatchGuidArray(payload, "sourceDocumentIds", existing?.SourceDocumentIds, out var sourceDocumentIds) ||
            !TryReadPatchGuidArray(payload, "linkedTaskIds", existing?.LinkedTaskIds, out var linkedTaskIds) ||
            !TryReadPatchGuidArray(payload, "linkedMeetingIds", existing?.LinkedMeetingIds, out var linkedMeetingIds))
        {
            return false;
        }

        input = new ContextItemUpdate(
            projectId,
            itemType,
            status,
            title,
            body,
            sourceDocumentIds,
            linkedTaskIds,
            linkedMeetingIds);
        return true;
    }

    private static bool TryReadPatchGuidArray(
        JsonElement payload,
        string propertyName,
        IReadOnlyList<Guid>? existing,
        out List<Guid> values)
    {
        if (!payload.TryGetProperty(propertyName, out _))
        {
            values = existing?.ToList() ?? new List<Guid>();
            return true;
        }

        return TryReadGuidArray(payload, propertyName, out values);
    }

    private static bool TryParseSourceType(string? value, out ContextSourceType parsed)
    {
        parsed = value switch
        {
            "meetingSummary" => ContextSourceType.MeetingSummary,
            "meetingTranscript" => ContextSourceType.MeetingTranscript,
            "chatSummary" => ContextSourceType.ChatSummary,
            "manualNote" => ContextSourceType.ManualNote,
            "clientRequest" => ContextSourceType.ClientRequest,
            "documentSummary" => ContextSourceType.DocumentSummary,
            "statusUpdate" => ContextSourceType.StatusUpdate,
            "telegramCapture" => ContextSourceType.TelegramCapture,
            "other" => ContextSourceType.Other,
            _ => (ContextSourceType)(-1)
        };
        return Enum.IsDefined(parsed);
    }

    private static bool TryParseSourceApp(string? value, out ContextSourceApp parsed)
    {
        parsed = value switch
        {
            "chatgpt" => ContextSourceApp.ChatGpt,
            "claude" => ContextSourceApp.Claude,
            "codex" => ContextSourceApp.Codex,
            "telegram" => ContextSourceApp.Telegram,
            "manual" => ContextSourceApp.Manual,
            "other" => ContextSourceApp.Other,
            _ => (ContextSourceApp)(-1)
        };
        return Enum.IsDefined(parsed);
    }

    private static bool TryParseItemType(string? value, out ContextItemType parsed)
    {
        parsed = value switch
        {
            "decision" => ContextItemType.Decision,
            "requirement" => ContextItemType.Requirement,
            "constraint" => ContextItemType.Constraint,
            "blocker" => ContextItemType.Blocker,
            "openQuestion" => ContextItemType.OpenQuestion,
            "actionItem" => ContextItemType.ActionItem,
            "projectFact" => ContextItemType.ProjectFact,
            "risk" => ContextItemType.Risk,
            "note" => ContextItemType.Note,
            _ => (ContextItemType)(-1)
        };
        return Enum.IsDefined(parsed);
    }

    private static bool TryParseItemStatus(string? value, out ContextItemStatus parsed)
    {
        parsed = value switch
        {
            "active" => ContextItemStatus.Active,
            "resolved" => ContextItemStatus.Resolved,
            "deprecated" => ContextItemStatus.Deprecated,
            "superseded" => ContextItemStatus.Superseded,
            _ => (ContextItemStatus)(-1)
        };
        return Enum.IsDefined(parsed);
    }

    private static bool TryReadMeetingInput(
        JsonElement payload,
        MeetingItem? existing,
        out MeetingUpdate input)
    {
        input = default!;
        var projectId = existing?.ProjectId ?? Guid.Empty;
        if (payload.TryGetProperty("projectId", out var projectElement) &&
            (projectElement.ValueKind != JsonValueKind.String ||
             !Guid.TryParse(projectElement.GetString(), out projectId)))
        {
            return false;
        }

        var title = existing?.Title;
        if (payload.TryGetProperty("title", out var titleElement))
        {
            if (titleElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            title = titleElement.GetString();
        }

        var startsAtUtc = existing?.StartsAtUtc ?? default;
        if (payload.TryGetProperty("startsAtUtc", out var startsElement) &&
            (startsElement.ValueKind != JsonValueKind.String ||
             !DateTimeOffset.TryParse(
                 startsElement.GetString(),
                 CultureInfo.InvariantCulture,
                 DateTimeStyles.RoundtripKind,
                 out startsAtUtc)))
        {
            return false;
        }

        var durationMinutes = existing?.DurationMinutes ?? MeetingItem.DefaultDurationMinutes;
        if (payload.TryGetProperty("durationMinutes", out var durationElement) &&
            (durationElement.ValueKind != JsonValueKind.Number ||
             !durationElement.TryGetInt32(out durationMinutes)))
        {
            return false;
        }

        if (!TryReadPatchString(payload, "notes", existing?.Notes, out var notes) ||
            !TryReadPatchString(payload, "location", existing?.Location, out var location) ||
            !TryReadPatchString(payload, "link", existing?.Link, out var link) ||
            !TryReadPatchGuid(payload, "linkedTaskId", existing?.LinkedTaskId, out var linkedTaskId) ||
            projectId == Guid.Empty || string.IsNullOrWhiteSpace(title) || startsAtUtc == default)
        {
            return false;
        }

        input = new MeetingUpdate(
            projectId,
            title,
            notes,
            startsAtUtc,
            durationMinutes,
            location,
            link,
            linkedTaskId);
        return true;
    }

    private static bool TryReadPatchString(
        JsonElement payload,
        string propertyName,
        string? existing,
        out string? value)
    {
        value = existing;
        if (!payload.TryGetProperty(propertyName, out var element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            value = null;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return true;
    }

    private static bool TryReadPatchGuid(
        JsonElement payload,
        string propertyName,
        Guid? existing,
        out Guid? value)
    {
        value = existing;
        if (!payload.TryGetProperty(propertyName, out var element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            value = null;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(element.GetString(), out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadGuidArray(
        JsonElement parent,
        string propertyName,
        out List<Guid> values)
    {
        values = new List<Guid>();
        if (!parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array ||
            property.GetArrayLength() > 100)
        {
            return false;
        }

        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(item.GetString(), out var value))
            {
                return false;
            }

            values.Add(value);
        }

        return true;
    }

    private static bool TryReadOptionalGuid(
        JsonElement parent,
        string propertyName,
        out Guid? value)
    {
        value = null;
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(property.GetString(), out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadOptionalString(
        JsonElement parent,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is null || value.Length <= 256;
    }

    private static string? ReadString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static WorkspaceCommandResult Fail(
        string commandId,
        string errorCode,
        string errorMessage) =>
        WorkspaceCommandResult.Failed(commandId, errorCode, errorMessage);
}

public sealed class WorkspaceCommandDispatcher
{
    private readonly AppState _state;
    private readonly Action _saveState;
    private readonly Action _stateChanged;

    public WorkspaceCommandDispatcher(
        AppState state,
        Action saveState,
        Action stateChanged)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _saveState = saveState ?? throw new ArgumentNullException(nameof(saveState));
        _stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
    }

    public WorkspaceCommandResult Dispatch(
        string json,
        DateTimeOffset? now = null)
    {
        var result = WorkspaceCommandProcessor.Execute(_state, json, now);
        if (!result.Success)
        {
            return result;
        }

        _saveState();
        if (AffectsTaskPresentation(json))
        {
            _stateChanged();
        }

        return result;
    }

    private static bool AffectsTaskPresentation(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReadCommandType(document.RootElement) != "updateWorkspaceContext";
    }

    private static string? ReadCommandType(JsonElement root) =>
        root.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;
}
