using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

public sealed record WorkspaceSnapshot(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Mode,
    IReadOnlyList<WorkspaceProjectSnapshot> Projects,
    IReadOnlyList<WorkspaceSectionSnapshot> Sections,
    IReadOnlyList<WorkspaceTaskSnapshot> Tasks,
    IReadOnlyList<WorkspaceActiveNowSnapshot> ActiveNow,
    IReadOnlyList<WorkspaceTimelineItemSnapshot> TimelineItems);

public sealed record WorkspaceProjectSnapshot(
    string Id,
    string Name,
    string Color,
    int SortOrder);

public sealed record WorkspaceSectionSnapshot(
    string Id,
    string ProjectId,
    string Name,
    int SortOrder,
    bool IsProjectRoot);

public sealed record WorkspaceTaskSnapshot(
    string Id,
    string ProjectId,
    string SectionId,
    string? ParentId,
    string Title,
    string Description,
    string Status,
    string WaitingFor,
    bool PinToPanel,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ReminderAtUtc,
    int? ReminderEveryMinutes,
    bool ReminderActive,
    DateTimeOffset? DeadlineAtUtc);

public sealed record WorkspaceActiveNowSnapshot(
    string TaskId,
    string Kind);

public sealed record WorkspaceTimelineItemSnapshot(
    string Id,
    string Kind,
    string Title,
    string ProjectId,
    string ProjectPath,
    string LinkedTaskId,
    DateTimeOffset OccursAtUtc,
    string? Meta);

public static class WorkspaceSnapshotFactory
{
    public const int CurrentSchemaVersion = 1;
    public const string ReadOnlyMode = "readonly";

    public static WorkspaceSnapshot Create(
        AppState state,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var sourceProjects = state.Projects ?? new List<ProjectItem>();
        var sourceGroups = state.Groups ?? new List<GroupItem>();
        var sourceTasks = state.Tasks ?? new List<TaskItem>();
        var projects = sourceProjects
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.CreatedAtUtc)
            .Select(ToProjectSnapshot)
            .ToList();

        var fallbackProject = sourceProjects.FirstOrDefault(project =>
                                  string.Equals(
                                      project.Name,
                                      ProjectItem.DefaultName,
                                      StringComparison.OrdinalIgnoreCase)) ??
                              sourceProjects
                                  .OrderBy(project => project.SortOrder)
                                  .ThenBy(project => project.CreatedAtUtc)
                                  .FirstOrDefault();
        if (fallbackProject is null)
        {
            fallbackProject = new ProjectItem
            {
                Id = Guid.Empty,
                Name = ProjectItem.DefaultName,
                ColorHex = ProjectColorPalette.Default,
                CreatedAtUtc = timestamp,
                UpdatedAtUtc = timestamp
            };
            projects.Add(ToProjectSnapshot(fallbackProject));
        }

        var projectById = sourceProjects.ToDictionary(project => project.Id);
        projectById[fallbackProject.Id] = fallbackProject;
        var groupById = sourceGroups
            .GroupBy(group => group.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var taskIds = sourceTasks.Select(task => task.Id).ToHashSet();

        var sections = sourceGroups
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.CreatedAtUtc)
            .Select(group =>
            {
                var project = ResolveProject(projectById, fallbackProject, group.ProjectId);
                return new WorkspaceSectionSnapshot(
                    GroupSectionId(group.Id),
                    FormatId(project.Id),
                    group.Name,
                    group.SortOrder,
                    IsProjectRoot: false);
            })
            .ToList();

        foreach (var project in projects)
        {
            sections.Add(new WorkspaceSectionSnapshot(
                RootSectionId(project.Id),
                project.Id,
                "Project root",
                int.MinValue,
                IsProjectRoot: true));
        }

        var taskContexts = sourceTasks
            .Select(task => CreateTaskContext(
                task,
                projectById,
                fallbackProject,
                groupById,
                taskIds,
                timestamp))
            .OrderBy(context => context.Snapshot.ProjectId)
            .ThenBy(context => context.Snapshot.SectionId)
            .ThenBy(context => context.Snapshot.SortOrder)
            .ThenBy(context => context.Snapshot.CreatedAtUtc)
            .ToList();
        var tasks = taskContexts.Select(context => context.Snapshot).ToList();

        var activeNow = taskContexts
            .Where(context =>
                context.Source.Status == TaskStatus.InWork ||
                context.Snapshot.ReminderActive)
            .Select(context => new WorkspaceActiveNowSnapshot(
                context.Snapshot.Id,
                context.Snapshot.ReminderActive ? "REMIND" : "FOCUS"))
            .ToList();

        var timelineItems = taskContexts
            .SelectMany(CreateTimelineItems)
            .OrderBy(item => item.OccursAtUtc)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        return new WorkspaceSnapshot(
            CurrentSchemaVersion,
            timestamp,
            ReadOnlyMode,
            projects,
            sections,
            tasks,
            activeNow,
            timelineItems);
    }

    private static WorkspaceProjectSnapshot ToProjectSnapshot(ProjectItem project) =>
        new(
            FormatId(project.Id),
            string.IsNullOrWhiteSpace(project.Name)
                ? ProjectItem.DefaultName
                : project.Name,
            string.IsNullOrWhiteSpace(project.ColorHex)
                ? ProjectColorPalette.Default
                : project.ColorHex,
            project.SortOrder);

    private static TaskContext CreateTaskContext(
        TaskItem task,
        IReadOnlyDictionary<Guid, ProjectItem> projectById,
        ProjectItem fallbackProject,
        IReadOnlyDictionary<Guid, GroupItem> groupById,
        IReadOnlySet<Guid> taskIds,
        DateTimeOffset timestamp)
    {
        var project = ResolveProject(projectById, fallbackProject, task.ProjectId);
        var group = task.GroupId is Guid groupId &&
                    groupById.TryGetValue(groupId, out var referencedGroup) &&
                    referencedGroup.ProjectId == project.Id
            ? referencedGroup
            : null;
        var reminderAt = task.ReminderSnoozedUntilUtc ??
                         task.RemindAtUtc ??
                         (task.ReminderActive ? task.LastReminderAtUtc : null);
        var reminderActive = reminderAt is not null &&
                             ReminderAttentionService.ShouldShowNotification(task, timestamp);
        var projectId = FormatId(project.Id);
        var snapshot = new WorkspaceTaskSnapshot(
            FormatId(task.Id),
            projectId,
            group is null ? RootSectionId(projectId) : GroupSectionId(group.Id),
            task.ParentTaskId is Guid parentId &&
            parentId != task.Id &&
            taskIds.Contains(parentId)
                ? FormatId(parentId)
                : null,
            task.Title,
            task.Description,
            ToStatus(task.Status),
            task.WaitingFor,
            task.PinToPanel,
            task.SortOrder,
            task.CreatedAtUtc,
            task.UpdatedAtUtc,
            reminderAt,
            task.RemindEveryMinutes,
            reminderActive,
            task.DueAtUtc);
        var projectPath = group is null
            ? project.Name
            : $"{project.Name} / {group.Name}";
        return new TaskContext(task, snapshot, projectPath);
    }

    private static IEnumerable<WorkspaceTimelineItemSnapshot> CreateTimelineItems(
        TaskContext context)
    {
        if (context.Snapshot.ReminderAtUtc is DateTimeOffset reminderAt)
        {
            yield return new WorkspaceTimelineItemSnapshot(
                $"remind:{context.Snapshot.Id}",
                "REMIND",
                context.Snapshot.Title,
                context.Snapshot.ProjectId,
                context.ProjectPath,
                context.Snapshot.Id,
                reminderAt,
                context.Snapshot.ReminderEveryMinutes is > 0
                    ? $"Repeats every {context.Snapshot.ReminderEveryMinutes}m"
                    : "Task reminder");
        }

        if (context.Snapshot.DeadlineAtUtc is DateTimeOffset deadlineAt)
        {
            yield return new WorkspaceTimelineItemSnapshot(
                $"deadline:{context.Snapshot.Id}",
                "DEADLINE",
                context.Snapshot.Title,
                context.Snapshot.ProjectId,
                context.ProjectPath,
                context.Snapshot.Id,
                deadlineAt,
                "Task deadline");
        }
    }

    private static ProjectItem ResolveProject(
        IReadOnlyDictionary<Guid, ProjectItem> projectById,
        ProjectItem fallbackProject,
        Guid? projectId) =>
        projectId is Guid id && projectById.TryGetValue(id, out var project)
            ? project
            : fallbackProject;

    private static string ToStatus(TaskStatus status) => status switch
    {
        TaskStatus.InWork => "FOCUS",
        TaskStatus.Waiting => "WAIT",
        TaskStatus.Done => "DONE",
        _ => "TODO"
    };

    private static string RootSectionId(string projectId) =>
        $"project:{projectId}:root";

    private static string GroupSectionId(Guid groupId) =>
        $"group:{FormatId(groupId)}";

    private static string FormatId(Guid id) => id.ToString("N");

    private sealed record TaskContext(
        TaskItem Source,
        WorkspaceTaskSnapshot Snapshot,
        string ProjectPath);
}
