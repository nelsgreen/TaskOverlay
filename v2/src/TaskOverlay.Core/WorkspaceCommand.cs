using System;
using System.Linq;
using System.Text.Json;

namespace TaskOverlay.Core;

public sealed record WorkspaceCommandResult(
    int SchemaVersion,
    string MessageType,
    string CommandId,
    bool Success,
    string? ErrorCode,
    string? ErrorMessage)
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentMessageType = "commandResult";

    public static WorkspaceCommandResult Succeeded(string commandId) =>
        new(
            CurrentSchemaVersion,
            CurrentMessageType,
            commandId,
            Success: true,
            ErrorCode: null,
            ErrorMessage: null);

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
        _stateChanged();
        return result;
    }
}
