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
    string? CreatedMeetingId = null)
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentMessageType = "commandResult";

    public static WorkspaceCommandResult Succeeded(
        string commandId,
        string? createdTaskId = null,
        string? createdSectionId = null,
        string? createdMeetingId = null) =>
        new(
            CurrentSchemaVersion,
            CurrentMessageType,
            commandId,
            Success: true,
            ErrorCode: null,
            ErrorMessage: null,
            createdTaskId,
            createdSectionId,
            createdMeetingId);

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
    public const int MinimumPlannedDurationMinutes = 5;
    public const int MaximumPlannedDurationMinutes = 24 * 60;
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

            if (type == "renameSection")
            {
                return RenameSection(state, payload, commandId, now ?? DateTimeOffset.UtcNow);
            }

            if (type == "deleteSection")
            {
                return DeleteSection(state, payload, commandId, now ?? DateTimeOffset.UtcNow);
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
                "updateTaskPlannedWork" => UpdatePlannedWork(
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

    private static WorkspaceCommandResult UpdatePlannedWork(
        TreeStateService treeService,
        Guid taskId,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        if (!payload.TryGetProperty("plannedStartAtUtc", out var startElement))
        {
            return Fail(commandId, "invalidPayload", "plannedStartAtUtc is required (use null to clear).");
        }

        // Clearing planned work: null start (and duration is ignored).
        if (startElement.ValueKind == JsonValueKind.Null)
        {
            return treeService.SetPlannedWork(taskId, null, null, timestamp)
                ? WorkspaceCommandResult.Succeeded(commandId)
                : Fail(commandId, "mutationRejected", "Planned work could not be cleared.");
        }

        if (startElement.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                startElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var plannedStart))
        {
            return Fail(commandId, "invalidPayload", "plannedStartAtUtc must be an ISO-8601 timestamp or null.");
        }

        if (!payload.TryGetProperty("plannedDurationMinutes", out var durationElement) ||
            durationElement.ValueKind != JsonValueKind.Number ||
            !durationElement.TryGetInt32(out var durationMinutes) ||
            durationMinutes < MinimumPlannedDurationMinutes ||
            durationMinutes > MaximumPlannedDurationMinutes)
        {
            return Fail(
                commandId,
                "invalidPayload",
                $"plannedDurationMinutes must be an integer between {MinimumPlannedDurationMinutes} and {MaximumPlannedDurationMinutes}.");
        }

        return treeService.SetPlannedWork(taskId, plannedStart, durationMinutes, timestamp)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : Fail(commandId, "mutationRejected", "Planned work could not be updated.");
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

        var treeService = new TreeStateService(state);
        var created = isDraft
            ? treeService.CreateDraftTask(parentId, timestamp)
            : treeService.CreateTask(parentId, title, timestamp);
        return created is not null
            ? WorkspaceCommandResult.Succeeded(commandId, created.Id.ToString("N"))
            : Fail(commandId, "mutationRejected", "Task could not be created in the given location.");
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
