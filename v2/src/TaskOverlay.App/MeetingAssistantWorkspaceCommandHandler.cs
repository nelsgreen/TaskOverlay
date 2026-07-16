using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TaskOverlay.Core;
using CoreTaskStatus = TaskOverlay.Core.TaskStatus;

namespace TaskOverlay.App;

public sealed class MeetingAssistantWorkspaceCommandHandler
{
    private static readonly HashSet<string> CommandTypes = new(StringComparer.Ordinal)
    {
        "startMeetingRecording",
        "startEmergencyRecording",
        "stopMeetingRecording",
        "transcribeMeetingRecording",
        "analyzeMeetingRecording",
        "cancelMeetingProcessing",
        "setMeetingRecordingPolicy",
        "setMeetingRecordingFormat",
        "setMeetingRecordingLocalOnly",
        "deleteMeetingRecording",
        "linkMeetingRecording",
        "createMeetingFromRecording",
        "openMeetingRecordingFolder",
        "openMeetingLink",
        "applyMeetingProposedActions",
        "rejectMeetingProposedAction"
    };

    private readonly MeetingAssistantCoordinator _coordinator;

    public MeetingAssistantWorkspaceCommandHandler(MeetingAssistantCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public async Task<WorkspaceCommandResult?> TryHandleAsync(
        string json,
        CancellationToken cancellationToken = default)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            var type = ReadString(root, "type");
            if (!CommandTypes.Contains(type))
            {
                return null;
            }

            var commandId = ReadString(root, "commandId");
            if (commandId.Length == 0)
            {
                return WorkspaceCommandResult.Failed(
                    string.Empty,
                    "invalidCommandId",
                    "Meeting Assistant command id is required.");
            }

            var payload = root.TryGetProperty("payload", out var payloadValue) &&
                          payloadValue.ValueKind == JsonValueKind.Object
                ? payloadValue
                : default;
            try
            {
                return type switch
                {
                    "startMeetingRecording" => await StartMeetingAsync(
                        commandId,
                        payload,
                        cancellationToken),
                    "startEmergencyRecording" => ToCommandResult(
                        commandId,
                        await _coordinator.StartEmergencyAsync(cancellationToken)),
                    "stopMeetingRecording" => await WithRecordingAsync(
                        commandId,
                        payload,
                        id => _coordinator.StopAsync(id, cancellationToken: cancellationToken)),
                    "transcribeMeetingRecording" => await WithRecordingAsync(
                        commandId,
                        payload,
                        id => _coordinator.TranscribeAsync(
                            id,
                            ReadBoolean(payload, "acceptUploadDisclosure"),
                            cancellationToken)),
                    "analyzeMeetingRecording" => await WithRecordingAsync(
                        commandId,
                        payload,
                        id => _coordinator.AnalyzeAsync(id, cancellationToken)),
                    "cancelMeetingProcessing" => WithRecording(
                        commandId,
                        payload,
                        _coordinator.CancelProcessing,
                        "Processing operation is not active."),
                    "setMeetingRecordingPolicy" => SetPolicy(commandId, payload),
                    "setMeetingRecordingFormat" => SetRecordingFormat(commandId, payload),
                    "setMeetingRecordingLocalOnly" => WithRecording(
                        commandId,
                        payload,
                        id => _coordinator.SetKeepLocalOnly(
                            id,
                            ReadBoolean(payload, "keepLocalOnly")),
                        "Recording local-only policy could not be updated."),
                    "deleteMeetingRecording" => WithRecording(
                        commandId,
                        payload,
                        _coordinator.DeleteRecording,
                        "Recording could not be deleted."),
                    "linkMeetingRecording" => LinkRecording(commandId, payload),
                    "createMeetingFromRecording" => CreateMeeting(commandId, payload),
                    "openMeetingRecordingFolder" => WithRecording(
                        commandId,
                        payload,
                        _coordinator.OpenRecordingFolder,
                        "Recording folder is unavailable."),
                    "openMeetingLink" => OpenMeetingLink(commandId, payload),
                    "applyMeetingProposedActions" => ApplyActions(commandId, payload),
                    "rejectMeetingProposedAction" => RejectAction(commandId, payload),
                    _ => null
                };
            }
            catch (Exception ex) when (
                ex is ArgumentException or
                InvalidOperationException or
                JsonException)
            {
                return WorkspaceCommandResult.Failed(
                    commandId,
                    "meetingAssistantCommandFailed",
                    ProviderErrorRedactor.Redact(ex.Message));
            }
        }
    }

    private async Task<WorkspaceCommandResult> StartMeetingAsync(
        string commandId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (!TryReadGuid(payload, "meetingId", out var meetingId))
        {
            return Invalid(commandId, "A valid meetingId is required.");
        }

        return ToCommandResult(
            commandId,
            await _coordinator.StartMeetingAsync(
                meetingId,
                automatic: false,
                cancellationToken));
    }

    private async Task<WorkspaceCommandResult> WithRecordingAsync(
        string commandId,
        JsonElement payload,
        Func<Guid, Task<MeetingAssistantOperationResult>> operation)
    {
        if (!TryReadGuid(payload, "recordingId", out var recordingId))
        {
            return Invalid(commandId, "A valid recordingId is required.");
        }

        return ToCommandResult(commandId, await operation(recordingId));
    }

    private static WorkspaceCommandResult WithRecording(
        string commandId,
        JsonElement payload,
        Func<Guid, bool> operation,
        string error)
    {
        if (!TryReadGuid(payload, "recordingId", out var recordingId))
        {
            return Invalid(commandId, "A valid recordingId is required.");
        }

        return operation(recordingId)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : WorkspaceCommandResult.Failed(commandId, "mutationRejected", error);
    }

    private WorkspaceCommandResult SetPolicy(string commandId, JsonElement payload)
    {
        if (!TryReadGuid(payload, "meetingId", out var meetingId) ||
            !Enum.TryParse<MeetingRecordingPolicy>(
                ReadString(payload, "policy"),
                ignoreCase: true,
                out var policy))
        {
            return Invalid(commandId, "A valid meetingId and recording policy are required.");
        }

        return _coordinator.SetMeetingPolicy(meetingId, policy)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : WorkspaceCommandResult.Failed(
                commandId,
                "mutationRejected",
                "MEET recording policy could not be updated.");
    }

    private WorkspaceCommandResult SetRecordingFormat(string commandId, JsonElement payload)
    {
        if (!Enum.TryParse<MeetingRecordingFormat>(
                ReadString(payload, "format"),
                ignoreCase: true,
                out var format))
        {
            return Invalid(commandId, "A valid meeting recording format is required.");
        }

        return _coordinator.SetRecordingFormat(format)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : WorkspaceCommandResult.Failed(
                commandId,
                "mutationRejected",
                "Recording format cannot change while a recording is active.");
    }

    private WorkspaceCommandResult LinkRecording(string commandId, JsonElement payload)
    {
        if (!TryReadGuid(payload, "recordingId", out var recordingId) ||
            !TryReadGuid(payload, "meetingId", out var meetingId))
        {
            return Invalid(commandId, "A valid recordingId and meetingId are required.");
        }

        return _coordinator.LinkToMeeting(recordingId, meetingId)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : WorkspaceCommandResult.Failed(
                commandId,
                "mutationRejected",
                "Recording could not be linked to the selected MEET.");
    }

    private WorkspaceCommandResult CreateMeeting(string commandId, JsonElement payload)
    {
        if (!TryReadGuid(payload, "recordingId", out var recordingId) ||
            !TryReadGuid(payload, "projectId", out var projectId))
        {
            return Invalid(commandId, "A valid recordingId and projectId are required.");
        }

        var title = ReadString(payload, "title");
        var result = _coordinator.CreateMeetingFromRecording(
            recordingId,
            projectId,
            title);
        return result.Success
            ? WorkspaceCommandResult.Succeeded(
                commandId,
                createdMeetingId: result.MeetingId?.ToString("N"))
            : WorkspaceCommandResult.Failed(
                commandId,
                "mutationRejected",
                result.Error ?? "MEET could not be created from recording.");
    }

    private WorkspaceCommandResult OpenMeetingLink(string commandId, JsonElement payload)
    {
        if (!TryReadGuid(payload, "meetingId", out var meetingId))
        {
            return Invalid(commandId, "A valid meetingId is required.");
        }

        return _coordinator.OpenMeetingLink(meetingId)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : WorkspaceCommandResult.Failed(
                commandId,
                "linkUnavailable",
                "MEET link is missing or invalid.");
    }

    private WorkspaceCommandResult ApplyActions(string commandId, JsonElement payload)
    {
        if (!TryReadGuid(payload, "analysisId", out var analysisId) ||
            !payload.TryGetProperty("actionIds", out var idsValue) ||
            idsValue.ValueKind != JsonValueKind.Array)
        {
            return Invalid(commandId, "A valid analysisId and actionIds array are required.");
        }

        var actionIds = idsValue.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String &&
                             Guid.TryParse(value.GetString(), out var id)
                ? id
                : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var overrides = ReadOverrides(payload);
        var result = _coordinator.ApplyProposedActions(analysisId, actionIds, overrides);
        return result.Success
            ? WorkspaceCommandResult.Succeeded(commandId)
            : WorkspaceCommandResult.Failed(
                commandId,
                "partialApply",
                result.Error ?? "Proposed actions could not be applied.");
    }

    private WorkspaceCommandResult RejectAction(string commandId, JsonElement payload)
    {
        if (!TryReadGuid(payload, "analysisId", out var analysisId) ||
            !TryReadGuid(payload, "actionId", out var actionId))
        {
            return Invalid(commandId, "A valid analysisId and actionId are required.");
        }

        return _coordinator.RejectProposedAction(analysisId, actionId)
            ? WorkspaceCommandResult.Succeeded(commandId)
            : WorkspaceCommandResult.Failed(
                commandId,
                "mutationRejected",
                "Proposed action could not be rejected.");
    }

    private static IReadOnlyList<ProposedActionOverride> ReadOverrides(JsonElement payload)
    {
        if (!payload.TryGetProperty("overrides", out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ProposedActionOverride>();
        }

        var result = new List<ProposedActionOverride>();
        foreach (var value in values.EnumerateArray())
        {
            if (!TryReadGuid(value, "actionId", out var actionId))
            {
                continue;
            }

            TryReadNullableGuid(value, "projectId", out var projectId);
            var status = ReadString(value, "status") switch
            {
                "FOCUS" => CoreTaskStatus.InWork,
                "WAIT" => CoreTaskStatus.Waiting,
                "DONE" => CoreTaskStatus.Done,
                "TODO" => CoreTaskStatus.Todo,
                _ => (CoreTaskStatus?)null
            };
            result.Add(new ProposedActionOverride(
                actionId,
                NullIfEmpty(ReadString(value, "title")),
                projectId,
                status,
                NullIfEmpty(ReadString(value, "waitingFor")),
                ReadTimestamp(value, "deadlineAtUtc"),
                ReadTimestamp(value, "reminderAtUtc")));
        }

        return result;
    }

    private static WorkspaceCommandResult ToCommandResult(
        string commandId,
        MeetingAssistantOperationResult result) =>
        result.Success
            ? WorkspaceCommandResult.Succeeded(
                commandId,
                createdMeetingId: result.MeetingId?.ToString("N"))
            : WorkspaceCommandResult.Failed(
                commandId,
                "meetingAssistantOperationFailed",
                result.Error ?? "Meeting Assistant operation failed.");

    private static WorkspaceCommandResult Invalid(string commandId, string message) =>
        WorkspaceCommandResult.Failed(commandId, "invalidPayload", message);

    private static string ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static bool TryReadGuid(JsonElement element, string name, out Guid value) =>
        Guid.TryParse(ReadString(element, name), out value) && value != Guid.Empty;

    private static bool TryReadNullableGuid(
        JsonElement element,
        string name,
        out Guid? value)
    {
        var raw = ReadString(element, name);
        if (raw.Length == 0)
        {
            value = null;
            return true;
        }

        if (Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty)
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string name)
    {
        var raw = ReadString(element, name);
        return DateTimeOffset.TryParse(raw, out var timestamp)
            ? timestamp.ToUniversalTime()
            : null;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
