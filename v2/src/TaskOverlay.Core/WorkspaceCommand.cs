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
    string? CreatedTaskId = null)
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentMessageType = "commandResult";

    public static WorkspaceCommandResult Succeeded(string commandId, string? createdTaskId = null) =>
        new(
            CurrentSchemaVersion,
            CurrentMessageType,
            commandId,
            Success: true,
            ErrorCode: null,
            ErrorMessage: null,
            createdTaskId);

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

    private static WorkspaceCommandResult CreateTask(
        AppState state,
        JsonElement payload,
        string commandId,
        DateTimeOffset timestamp)
    {
        var title = ReadString(payload, "title");
        if (string.IsNullOrWhiteSpace(title) || title.Length > MaximumTitleLength)
        {
            return Fail(commandId, "invalidPayload", "Task title is required and must not be too long.");
        }

        if (!TryResolveSectionParent(payload, out var parentId))
        {
            return Fail(commandId, "invalidPayload", "A valid projectId or sectionId is required.");
        }

        var treeService = new TreeStateService(state);
        var created = treeService.CreateTask(parentId, title, timestamp);
        return created is not null
            ? WorkspaceCommandResult.Succeeded(commandId, created.Id.ToString("N"))
            : Fail(commandId, "mutationRejected", "Task could not be created in the given location.");
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

        state.WorkspaceSettings = new WorkspaceSettings
        {
            ActiveTab = activeTab.Value,
            SelectedProjectIds = selectedProjectIds,
            SelectedTaskId = selectedTaskId,
            SelectedTimelineItemId = selectedTimelineItemId,
            SelectedWorkstreamId = selectedWorkstreamId,
            Filter = filter.Value
        };
        WorkspaceStatePolicy.Normalize(state);
        return WorkspaceCommandResult.Succeeded(commandId);
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
