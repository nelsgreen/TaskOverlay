using System;
using System.Linq;

namespace TaskOverlay.Core;

public sealed class TaskWorkSessionService
{
    public const int MinimumDurationMinutes = 5;
    public const int MaximumDurationMinutes = 24 * 60;
    public const int MaximumNoteLength = 10_000;
    public const int LegacyDefaultDurationMinutes = 60;

    private readonly AppState _state;

    public TaskWorkSessionService(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public TaskWorkSession? Create(
        Guid taskId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string? note = null,
        DateTimeOffset? now = null)
    {
        if (!_state.Tasks.Any(task => task.Id == taskId) ||
            !IsValidRange(startUtc, endUtc) ||
            !TryNormalizeNote(note, out var normalizedNote))
        {
            return null;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var session = new TaskWorkSession
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            StartUtc = startUtc.ToUniversalTime(),
            EndUtc = endUtc.ToUniversalTime(),
            Note = normalizedNote,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        _state.TaskWorkSessions.Add(session);
        return session;
    }

    public bool Update(
        Guid sessionId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string? note,
        DateTimeOffset? now = null)
    {
        var session = _state.TaskWorkSessions.FirstOrDefault(item => item.Id == sessionId);
        if (session is null ||
            !_state.Tasks.Any(task => task.Id == session.TaskId) ||
            !IsValidRange(startUtc, endUtc) ||
            !TryNormalizeNote(note, out var normalizedNote))
        {
            return false;
        }

        session.StartUtc = startUtc.ToUniversalTime();
        session.EndUtc = endUtc.ToUniversalTime();
        session.Note = normalizedNote;
        session.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        return true;
    }

    public bool Delete(Guid sessionId) =>
        _state.TaskWorkSessions.RemoveAll(session => session.Id == sessionId) == 1;

    public int DeleteForTask(Guid taskId) =>
        _state.TaskWorkSessions.RemoveAll(session => session.TaskId == taskId);

    public static bool IsValidRange(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (startUtc == default || endUtc == default || endUtc <= startUtc)
        {
            return false;
        }

        var duration = endUtc - startUtc;
        return duration >= TimeSpan.FromMinutes(MinimumDurationMinutes) &&
               duration <= TimeSpan.FromMinutes(MaximumDurationMinutes);
    }

    private static bool TryNormalizeNote(string? note, out string normalized)
    {
        normalized = note?.Trim() ?? string.Empty;
        return normalized.Length <= MaximumNoteLength;
    }
}
