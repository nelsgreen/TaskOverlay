using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

/// <summary>
/// Domain mutations for lightweight task checkpoints ("Steps" in the UI).
/// All mutations renumber SortOrder to keep list order stable and stamp
/// UpdatedAtUtc on both the checkpoint and the owning task. Checkpoint state
/// is independent of the parent task's status by design: nothing here touches
/// task status, and task status changes must not call into here.
/// </summary>
public static class CheckpointService
{
    public const int MaximumTitleLength = 500;
    public const int MaximumCheckpointsPerTask = 200;

    /// <summary>
    /// Appends one checkpoint per non-empty title, preserving input order
    /// (this is what multiline paste maps to). Returns the created items;
    /// empty result means nothing was added.
    /// </summary>
    public static IReadOnlyList<CheckpointItem> Add(
        TaskItem task,
        IEnumerable<string> titles,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(titles);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var checkpoints = task.Checkpoints ??= new List<CheckpointItem>();
        var created = new List<CheckpointItem>();
        // Append strictly after every existing item, even when stored sort
        // orders have gaps — Renumber below compacts everything to 0..n-1.
        var nextOrder = checkpoints.Count == 0
            ? 0
            : checkpoints.Max(item => item.SortOrder) + 1;
        foreach (var rawTitle in titles)
        {
            var title = NormalizeTitle(rawTitle);
            if (title is null || checkpoints.Count >= MaximumCheckpointsPerTask)
            {
                continue;
            }

            var item = new CheckpointItem
            {
                Title = title,
                SortOrder = nextOrder++,
                CreatedAtUtc = timestamp,
                UpdatedAtUtc = timestamp
            };
            checkpoints.Add(item);
            created.Add(item);
        }

        if (created.Count > 0)
        {
            Renumber(checkpoints);
            task.UpdatedAtUtc = timestamp;
        }

        return created;
    }

    public static bool Rename(
        TaskItem task,
        Guid checkpointId,
        string title,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        var normalized = NormalizeTitle(title);
        var checkpoint = Find(task, checkpointId);
        if (normalized is null || checkpoint is null)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        checkpoint.Title = normalized;
        checkpoint.UpdatedAtUtc = timestamp;
        task.UpdatedAtUtc = timestamp;
        return true;
    }

    public static bool Toggle(
        TaskItem task,
        Guid checkpointId,
        bool done,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        var checkpoint = Find(task, checkpointId);
        if (checkpoint is null)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        checkpoint.Done = done;
        checkpoint.CompletedAtUtc = done ? timestamp : null;
        checkpoint.UpdatedAtUtc = timestamp;
        task.UpdatedAtUtc = timestamp;
        return true;
    }

    public static bool Delete(
        TaskItem task,
        Guid checkpointId,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        var checkpoints = task.Checkpoints;
        var checkpoint = Find(task, checkpointId);
        if (checkpoints is null || checkpoint is null)
        {
            return false;
        }

        checkpoints.Remove(checkpoint);
        Renumber(checkpoints);
        task.UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Moves a checkpoint to targetIndex (clamped to the valid range).</summary>
    public static bool Move(
        TaskItem task,
        Guid checkpointId,
        int targetIndex,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        var checkpoints = task.Checkpoints;
        var checkpoint = Find(task, checkpointId);
        if (checkpoints is null || checkpoint is null)
        {
            return false;
        }

        var ordered = Ordered(checkpoints);
        var currentIndex = ordered.IndexOf(checkpoint);
        var clampedIndex = Math.Clamp(targetIndex, 0, ordered.Count - 1);
        if (clampedIndex == currentIndex)
        {
            return true;
        }

        ordered.RemoveAt(currentIndex);
        ordered.Insert(clampedIndex, checkpoint);
        checkpoints.Clear();
        checkpoints.AddRange(ordered);
        Renumber(checkpoints);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        checkpoint.UpdatedAtUtc = timestamp;
        task.UpdatedAtUtc = timestamp;
        return true;
    }

    /// <summary>
    /// Repairs checkpoint data loaded from disk: drops null/empty-title items,
    /// truncates over-long titles, reconciles Done/CompletedAtUtc, and renumbers
    /// SortOrder. Returns true when anything changed (state should be re-saved).
    /// </summary>
    public static bool Normalize(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var checkpoints = task.Checkpoints;
        if (checkpoints is null)
        {
            return false;
        }

        var changed = false;
        for (var index = checkpoints.Count - 1; index >= 0; index--)
        {
            var checkpoint = checkpoints[index];
            if (checkpoint is null || NormalizeTitle(checkpoint.Title) is not string title)
            {
                checkpoints.RemoveAt(index);
                changed = true;
                continue;
            }

            if (checkpoint.Title != title)
            {
                checkpoint.Title = title;
                changed = true;
            }

            if (checkpoint.Id == Guid.Empty)
            {
                checkpoint.Id = Guid.NewGuid();
                changed = true;
            }

            if (!checkpoint.Done && checkpoint.CompletedAtUtc is not null)
            {
                checkpoint.CompletedAtUtc = null;
                changed = true;
            }
        }

        var ordered = Ordered(checkpoints);
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].SortOrder != index)
            {
                changed = true;
            }
        }

        if (changed)
        {
            checkpoints.Clear();
            checkpoints.AddRange(ordered);
            Renumber(checkpoints);
        }

        return changed;
    }

    private static string? NormalizeTitle(string? title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaximumTitleLength
            ? trimmed[..MaximumTitleLength]
            : trimmed;
    }

    private static CheckpointItem? Find(TaskItem task, Guid checkpointId) =>
        checkpointId == Guid.Empty
            ? null
            : task.Checkpoints?.FirstOrDefault(item => item?.Id == checkpointId);

    private static List<CheckpointItem> Ordered(List<CheckpointItem> checkpoints) =>
        checkpoints
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedAtUtc)
            .ToList();

    private static void Renumber(List<CheckpointItem> checkpoints)
    {
        for (var index = 0; index < checkpoints.Count; index++)
        {
            checkpoints[index].SortOrder = index;
        }
    }
}
