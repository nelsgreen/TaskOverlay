using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaskOverlay.Core;

namespace TaskOverlay.Core.Tests;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("default state creation", DefaultStateCreation),
            ("project and group models", ProjectAndGroupModels),
            ("v1 to v2 state migration", V1ToV2StateMigration),
            ("state migration idempotence", StateMigrationIdempotence),
            ("migration persisted on load", MigrationPersistedOnLoad),
            ("v2 tree serialization roundtrip", V2TreeSerializationRoundtrip),
            ("project agnostic task behavior", ProjectAgnosticTaskBehavior),
            ("orphan project fallback", OrphanProjectFallback),
            ("project service create", ProjectServiceCreate),
            ("project service rename", ProjectServiceRename),
            ("project service delete", ProjectServiceDelete),
            ("project service default safety", ProjectServiceDefaultSafety),
            ("project service group create and rename", ProjectServiceGroupCreateAndRename),
            ("project service group delete", ProjectServiceGroupDelete),
            ("project service task assignment", ProjectServiceTaskAssignment),
            ("project service orphan safety", ProjectServiceOrphanSafety),
            ("tree service constructors are side effect free", TreeServiceConstructorsAreSideEffectFree),
            ("tree node creation", TreeNodeCreation),
            ("tree rename and safe delete", TreeRenameAndSafeDelete),
            ("tree delete atomic failure", TreeDeleteAtomicFailure),
            ("tree move and cycle guards", TreeMoveAndCycleGuards),
            ("tree orphan fallback", TreeOrphanFallback),
            ("tree missing default recovery", TreeMissingDefaultRecovery),
            ("tree cycle repair", TreeCycleRepair),
            ("save repair diagnostics", SaveRepairDiagnostics),
            ("tree sibling ordering", TreeSiblingOrdering),
            ("tree active and status", TreeActiveAndStatus),
            ("tree navigation", TreeNavigation),
            ("tree projections", TreeProjections),
            ("tree legacy flat state compatibility", TreeLegacyFlatStateCompatibility),
            ("daily MVP project seeding", DailyMvpProjectSeeding),
            ("old attention state compatibility", OldAttentionStateCompatibility),
            ("waiting status persistence", WaitingStatusPersistence),
            ("project color persistence", ProjectColorPersistence),
            ("reminder scheduling", ReminderScheduling),
            ("reminder snooze and still waiting", ReminderSnoozeAndStillWaiting),
            ("reminder focus transition", ReminderFocusTransition),
            ("reminder notification snooze persistence", ReminderNotificationSnoozePersistence),
            ("reminder attention ordering", ReminderAttentionOrdering),
            ("done and clear remove reminder attention", DoneAndClearRemoveReminderAttention),
            ("quick task capture", QuickTaskCapture),
            ("save/load roundtrip", SaveLoadRoundtrip),
            ("overlay mode serialization", OverlayModeSerialization),
            ("working presentation settings", WorkingPresentationSettings),
            ("old collapsed mode migration", OldCollapsedModeMigration),
            ("old pinned mode migration", OldPinnedModeMigration),
            ("old overlay mode default", OldOverlayModeDefault),
            ("legacy auto mode fallback", LegacyAutoModeFallback),
            ("overlay collapse guard", OverlayCollapseGuardBehavior),
            ("working activation policy", WorkingActivationPolicyBehavior),
            ("pointer click versus drag threshold", PointerClickVersusDragThreshold),
            ("overlay mode click cycle", OverlayModeClickCycle),
            ("overlay mode shortcut policy", OverlayModeShortcutPolicyBehavior),
            ("global hotkey bindings", GlobalHotkeyBindingBehavior),
            ("working mode task filtering", WorkingModeTaskFiltering),
            ("working mode focus badge", WorkingModeFocusBadge),
            ("handle surface ownership across modes", HandleSurfaceOwnershipAcrossModes),
            ("single task in-work mode", SingleTaskInWorkMode),
            ("multiple tasks in-work mode", MultipleTasksInWorkMode),
            ("task edit values", TaskEditValuesUpdate),
            ("task delete", TaskDelete),
            ("task completion", TaskCompletion),
            ("in-work setting serialization", InWorkSettingSerialization),
            ("old state in-work default", OldStateInWorkDefault),
            ("window placement negative monitor clamp", WindowPlacementNegativeMonitorClamp),
            ("window placement edge snap", WindowPlacementEdgeSnap),
            ("window placement off-screen correction", WindowPlacementOffScreenCorrection),
            ("collapsed panel opens inward", CollapsedPanelOpensInward),
            ("corrupted state backup", CorruptedStateBackup),
            ("crash log contents", CrashLogContents),
            ("diagnostic callback isolation", DiagnosticCallbackIsolation),
            ("clipboard lines create multiple tasks", ClipboardLinesCreateMultipleTasks),
            ("single clipboard task collapses lines", SingleClipboardTaskCollapsesLines),
            ("clipboard task with description", ClipboardTaskWithDescription),
            ("empty clipboard text", EmptyClipboardText)
        };

        foreach (var test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine($"PASS: {test.Name}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {test.Name}");
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        return 0;
    }

    private static void DefaultStateCreation()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = store.Load();

            Assert(File.Exists(store.StatePath), "Load should create state.json.");
            Assert(state.SchemaVersion == AppState.CurrentSchemaVersion, "Schema version mismatch.");
            Assert(state.Tasks.Count == 3, "Expected three seed tasks.");
            Assert(state.Projects.Count == 1, "Fresh state should contain one Default project.");
            Assert(state.Groups.Count == 0, "Fresh state should not contain groups.");
            Assert(
                state.Projects[0].Name == ProjectItem.DefaultName,
                "Fresh state should create the Default project.");
            Assert(
                state.Tasks.All(task => task.ProjectId == state.Projects[0].Id),
                "Seed tasks should belong to the Default project.");
            Assert(state.Tasks.All(task => task.Id != Guid.Empty), "Seed tasks need stable IDs.");
            Assert(state.Tasks.Select(task => task.Id).Distinct().Count() == 3, "Seed task IDs must be unique.");
            Assert(
                state.OverlaySettings.OverlayMode == OverlayMode.Working,
                "New state should use Working mode.");
        });
    }

    private static void ProjectAndGroupModels()
    {
        var projectId = Guid.NewGuid();
        var createdAtUtc = DateTimeOffset.Parse("2026-06-20T09:15:00Z");
        var project = new ProjectItem
        {
            Id = projectId,
            Name = "Project Alpha",
            SortOrder = 7,
            CreatedAtUtc = createdAtUtc
        };
        var group = new GroupItem
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = "Group One",
            SortOrder = 3,
            CreatedAtUtc = createdAtUtc
        };
        var task = new TaskItem();

        Assert(project.Id == projectId, "Project ID should be assignable.");
        Assert(project.Name == "Project Alpha", "Project name should be assignable.");
        Assert(project.SortOrder == 7, "Project sort order should be assignable.");
        Assert(project.CreatedAtUtc == createdAtUtc, "Project creation time should be assignable.");
        Assert(group.ProjectId == project.Id, "Group should reference its project.");
        Assert(task.ProjectId is null, "New task ProjectId should be nullable and unset.");
        Assert(task.GroupId is null, "New task GroupId should be nullable and unset.");
    }

    private static void V1ToV2StateMigration()
    {
        var createdAtUtc = DateTimeOffset.Parse("2025-01-02T03:04:05Z");
        var completedAtUtc = DateTimeOffset.Parse("2025-01-03T03:04:05Z");
        var dueAtUtc = DateTimeOffset.Parse("2025-01-04T03:04:05Z");
        var taskId = Guid.NewGuid();
        var state = new AppState
        {
            SchemaVersion = 1,
            CreatedAtUtc = createdAtUtc,
            Tasks =
            {
                new TaskItem
                {
                    Id = taskId,
                    Title = "Preserve me",
                    Description = "Existing description",
                    Completed = true,
                    Priority = TaskPriority.Critical,
                    InWork = true,
                    DescriptionExpanded = true,
                    CreatedAtUtc = createdAtUtc,
                    CompletedAtUtc = completedAtUtc,
                    DueAtUtc = dueAtUtc,
                    ProjectId = Guid.NewGuid(),
                    GroupId = Guid.NewGuid()
                }
            },
            OverlaySettings = new OverlaySettings
            {
                ActiveToPassiveDelayMilliseconds = 875,
                AlwaysOnTop = false,
                OverlayMode = OverlayMode.PinnedExpanded,
                InWorkMode = InWorkMode.SingleTask
            },
            WindowPlacement = new WindowPlacement
            {
                Left = -200,
                Top = 42,
                CollapsedLeft = 1800,
                CollapsedTop = 12
            }
        };
        var settings = state.OverlaySettings;
        var placement = state.WindowPlacement;

        var migrated = StateMigrator.Migrate(state);
        var defaultProject = migrated.Projects.Single(project =>
            project.Name == ProjectItem.DefaultName);
        var task = migrated.Tasks.Single();

        Assert(ReferenceEquals(migrated, state), "Migration should update the loaded state.");
        Assert(migrated.SchemaVersion == 2, "Migration should advance schemaVersion to 2.");
        Assert(migrated.Projects.Count == 1, "Migration should create exactly one Default project.");
        Assert(task.ProjectId == defaultProject.Id, "Existing tasks should join the Default project.");
        Assert(task.GroupId is null, "Migrated v1 tasks should not belong to a group.");
        Assert(task.Id == taskId, "Migration should preserve task ID.");
        Assert(task.Title == "Preserve me", "Migration should preserve task title.");
        Assert(task.Description == "Existing description", "Migration should preserve description.");
        Assert(task.Completed, "Migration should preserve completed state.");
        Assert(task.Priority == TaskPriority.Critical, "Migration should preserve priority.");
        Assert(task.InWork, "Migration should preserve in-work state.");
        Assert(task.DescriptionExpanded, "Migration should preserve description expansion.");
        Assert(task.CreatedAtUtc == createdAtUtc, "Migration should preserve creation time.");
        Assert(task.CompletedAtUtc == completedAtUtc, "Migration should preserve completion time.");
        Assert(task.DueAtUtc == dueAtUtc, "Migration should preserve due time.");
        Assert(ReferenceEquals(migrated.OverlaySettings, settings), "Migration should preserve settings.");
        Assert(ReferenceEquals(migrated.WindowPlacement, placement), "Migration should preserve placement.");
        Assert(settings.ActiveToPassiveDelayMilliseconds == 875, "Migration should preserve delay.");
        Assert(!settings.AlwaysOnTop, "Migration should preserve topmost setting.");
        Assert(placement.Left == -200 && placement.CollapsedLeft == 1800, "Migration should preserve coordinates.");
    }

    private static void StateMigrationIdempotence()
    {
        var state = new AppState { SchemaVersion = 1 };
        state.Tasks.Add(TaskItem.Create("Existing task"));

        StateMigrator.Migrate(state);
        var projectId = state.Projects.Single().Id;
        StateMigrator.Migrate(state);

        Assert(state.Projects.Count == 1, "Repeated migration must not duplicate Default.");
        Assert(state.Projects[0].Id == projectId, "Repeated migration must preserve Default ID.");
        Assert(state.Tasks[0].ProjectId == projectId, "Repeated migration must preserve assignment.");
    }

    private static void MigrationPersistedOnLoad()
    {
        WithTemporaryDirectory(directory =>
        {
            const string v1Json =
                """
                {
                  "schemaVersion": 1,
                  "tasks": [{
                    "id": "a5bc557e-6f52-468c-b23c-86f2626a448e",
                    "title": "Migrated task",
                    "description": "unchanged",
                    "completed": false,
                    "priority": "high",
                    "inWork": true,
                    "descriptionExpanded": true,
                    "createdAtUtc": "2026-06-01T08:30:00+00:00"
                  }],
                  "overlaySettings": { "alwaysOnTop": true },
                  "windowPlacement": { "left": 100, "top": 200 },
                  "createdAtUtc": "2026-06-01T08:30:00+00:00",
                  "updatedAtUtc": "2026-06-01T08:30:00+00:00"
                }
                """;
            Directory.CreateDirectory(directory);
            var store = new AppStateStore(directory);
            File.WriteAllText(store.StatePath, v1Json);

            var loaded = store.Load();
            var rewrittenJson = File.ReadAllText(store.StatePath);
            var backupJson = File.ReadAllText(store.BackupPath);

            Assert(loaded.SchemaVersion == 2, "Loaded v1 state should migrate to schemaVersion 2.");
            Assert(loaded.Tasks[0].ProjectId == loaded.Projects.Single().Id, "Loaded task should be assigned.");
            Assert(rewrittenJson.Contains("\"schemaVersion\": 2"), "Migrated state should be persisted.");
            Assert(backupJson.Contains("\"schemaVersion\": 1"), "The original v1 state should be backed up.");
        });
    }

    private static void V2TreeSerializationRoundtrip()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = AppState.CreateDefault();
            var project = new ProjectItem
            {
                Name = "Release",
                SortOrder = 2,
                CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T08:00:00Z")
            };
            var group = new GroupItem
            {
                ProjectId = project.Id,
                Name = "QA",
                SortOrder = 4,
                CreatedAtUtc = project.CreatedAtUtc
            };
            var task = TaskItem.Create("Verify build", project.CreatedAtUtc, project.Id);
            task.GroupId = group.Id;
            state.Projects.Add(project);
            state.Groups.Add(group);
            state.Tasks.Add(task);

            var store = new AppStateStore(directory);
            store.Save(state);
            var loaded = store.Load();

            Assert(loaded.SchemaVersion == 2, "Roundtrip should retain schemaVersion 2.");
            Assert(loaded.Projects.Single(item => item.Id == project.Id).Name == "Release", "Project should roundtrip.");
            Assert(loaded.Groups.Single(item => item.Id == group.Id).ProjectId == project.Id, "Group should roundtrip.");
            var loadedTask = loaded.Tasks.Single(item => item.Id == task.Id);
            Assert(loadedTask.ProjectId == project.Id, "Task ProjectId should roundtrip.");
            Assert(loadedTask.GroupId == group.Id, "Task GroupId should roundtrip.");
        });
    }

    private static void ProjectAgnosticTaskBehavior()
    {
        var state = AppState.CreateDefault();
        var secondProject = new ProjectItem { Name = "Second", SortOrder = 1 };
        var crossProjectTask = TaskItem.Create("Cross-project", projectId: secondProject.Id);
        state.Projects.Add(secondProject);
        state.Tasks.Add(crossProjectTask);
        state.Tasks[0].InWork = true;

        TaskInteractionService.SetInWorkMode(state, InWorkMode.SingleTask);
        TaskInteractionService.SetInWork(state, crossProjectTask, true);
        var activeTasks = state.Tasks.Where(task => !task.Completed).ToList();

        Assert(!state.Tasks[0].InWork, "Single-task mode should clear tasks in other projects.");
        Assert(crossProjectTask.InWork, "Task activation should not be filtered by project.");
        Assert(activeTasks.Contains(state.Tasks[0]), "Active subset should include Default tasks.");
        Assert(activeTasks.Contains(crossProjectTask), "Active subset should include other projects.");
    }

    private static void OrphanProjectFallback()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = AppState.CreateDefault();
            var orphanProjectId = Guid.NewGuid();
            var orphanTask = TaskItem.Create("Orphan", projectId: orphanProjectId);
            var nullProjectTask = TaskItem.Create("Unassigned");
            state.Tasks.Add(orphanTask);
            state.Tasks.Add(nullProjectTask);

            var store = new AppStateStore(directory);
            store.Save(state);
            var loaded = store.Load();
            var defaultProject = loaded.Projects.Single(project => project.Name == ProjectItem.DefaultName);
            var loadedOrphan = loaded.Tasks.Single(task => task.Id == orphanTask.Id);
            var loadedUnassigned = loaded.Tasks.Single(task => task.Id == nullProjectTask.Id);

            Assert(loadedOrphan.ProjectId == defaultProject.Id, "Load should repair an orphan reference.");
            Assert(loadedUnassigned.ProjectId == defaultProject.Id, "Load should assign null reference to Default.");
            Assert(
                ProjectReferenceResolver.ResolveProject(loaded, loadedOrphan)?.Id == defaultProject.Id,
                "Orphan references should safely resolve to Default.");
            Assert(
                ProjectReferenceResolver.ResolveProject(loaded, loadedUnassigned)?.Id == defaultProject.Id,
                "Null references should safely resolve to Default.");
        });
    }

    private static void ProjectServiceCreate()
    {
        var state = AppState.CreateDefault();
        state.Projects.Add(new ProjectItem { Name = "Existing", SortOrder = 4 });
        var service = new ProjectService(state);
        var createdAtUtc = DateTimeOffset.Parse("2026-06-20T10:30:00Z");

        var project = service.CreateProject("  Project Alpha  ", createdAtUtc);
        var projectCount = state.Projects.Count;

        Assert(project is not null, "A valid project should be created.");
        Assert(project!.Id != Guid.Empty, "Created project should have an ID.");
        Assert(project.Name == "Project Alpha", "Created project name should be trimmed.");
        Assert(project.SortOrder == 5, "Created project should use the next sort order.");
        Assert(project.CreatedAtUtc == createdAtUtc, "Created project should use the supplied timestamp.");
        Assert(service.CreateProject("   ") is null, "Whitespace project names should be rejected safely.");
        Assert(service.CreateProject(ProjectItem.DefaultName) is null, "Duplicate Default should be rejected.");
        Assert(state.Projects.Count == projectCount, "Rejected creates must not change project state.");
    }

    private static void ProjectServiceRename()
    {
        var state = AppState.CreateDefault();
        var project = new ProjectItem { Name = "Before" };
        state.Projects.Add(project);
        var service = new ProjectService(state);

        Assert(service.RenameProject(project.Id, "  After  "), "Existing project should be renamed.");
        Assert(project.Name == "After", "Renamed project should use a trimmed name.");
        Assert(!service.RenameProject(project.Id, "  "), "Empty project names should be rejected.");
        Assert(project.Name == "After", "Rejected rename should preserve the existing name.");
        Assert(!service.RenameProject(Guid.NewGuid(), "Missing"), "Missing project should fail safely.");
    }

    private static void ProjectServiceDelete()
    {
        var state = AppState.CreateDefault();
        var defaultProject = state.Projects.Single();
        var project = new ProjectItem { Name = "Delete me", SortOrder = 1 };
        var group = new GroupItem { ProjectId = project.Id, Name = "Delete group" };
        var assignedTask = TaskItem.Create("Assigned", projectId: project.Id);
        assignedTask.GroupId = group.Id;
        var crossReferencedTask = TaskItem.Create("Cross-reference", projectId: defaultProject.Id);
        crossReferencedTask.GroupId = group.Id;
        state.Projects.Add(project);
        state.Groups.Add(group);
        state.Tasks.Add(assignedTask);
        state.Tasks.Add(crossReferencedTask);
        var taskCount = state.Tasks.Count;
        var service = new ProjectService(state);

        Assert(service.DeleteProject(project.Id), "Existing project should be deleted.");
        Assert(state.Tasks.Count == taskCount, "Deleting a project must not delete tasks.");
        Assert(state.Projects.All(item => item.Id != project.Id), "Deleted project should be removed.");
        Assert(state.Groups.All(item => item.ProjectId != project.Id), "Project groups should be removed.");
        Assert(assignedTask.ProjectId == defaultProject.Id, "Project tasks should move to Default.");
        Assert(assignedTask.GroupId is null, "Reassigned task group should be cleared.");
        Assert(crossReferencedTask.ProjectId == defaultProject.Id, "Unrelated project assignment should remain.");
        Assert(crossReferencedTask.GroupId is null, "References to deleted groups should be cleared.");
        Assert(!service.DeleteProject(Guid.NewGuid()), "Missing project deletion should fail safely.");
    }

    private static void ProjectServiceDefaultSafety()
    {
        var state = AppState.CreateDefault();
        var defaultProject = state.Projects.Single();
        var service = new ProjectService(state);

        Assert(!service.DeleteProject(defaultProject.Id), "The only Default project must not be deleted.");
        Assert(!service.RenameProject(defaultProject.Id, "Renamed"), "The only Default must not be renamed away.");
        Assert(state.Projects.Single().Id == defaultProject.Id, "Default safety should preserve the project.");

        var alternateDefault = ProjectItem.CreateDefault();
        state.Projects.Add(alternateDefault);
        var task = TaskItem.Create("Legacy duplicate", projectId: defaultProject.Id);
        state.Tasks.Add(task);

        Assert(service.DeleteProject(defaultProject.Id), "A Default may be removed when another safe Default exists.");
        Assert(task.ProjectId == alternateDefault.Id, "Tasks should move to the remaining Default.");
        Assert(state.Projects.Count(item => item.Name == ProjectItem.DefaultName) == 1, "One Default should remain.");
    }

    private static void ProjectServiceGroupCreateAndRename()
    {
        var state = AppState.CreateDefault();
        var project = state.Projects.Single();
        var otherProject = new ProjectItem { Name = "Other", SortOrder = 1 };
        state.Projects.Add(otherProject);
        state.Groups.Add(new GroupItem { ProjectId = project.Id, Name = "Existing", SortOrder = 3 });
        state.Groups.Add(new GroupItem { ProjectId = otherProject.Id, Name = "Other", SortOrder = 20 });
        var service = new ProjectService(state);
        var createdAtUtc = DateTimeOffset.Parse("2026-06-20T11:00:00Z");

        var group = service.CreateGroup(project.Id, "  Planning  ", createdAtUtc);

        Assert(group is not null, "Group should be created under an existing project.");
        Assert(group!.Id != Guid.Empty, "Created group should have an ID.");
        Assert(group.ProjectId == project.Id, "Created group should reference its project.");
        Assert(group.Name == "Planning", "Created group name should be trimmed.");
        Assert(group.SortOrder == 4, "Group sort order should be scoped to its project.");
        Assert(group.CreatedAtUtc == createdAtUtc, "Created group should use the supplied timestamp.");
        Assert(service.CreateGroup(Guid.NewGuid(), "Missing") is null, "Missing project should fail safely.");
        Assert(service.CreateGroup(project.Id, "  ") is null, "Empty group name should be rejected.");
        Assert(service.RenameGroup(group.Id, "  Ready  "), "Existing group should be renamed.");
        Assert(group.Name == "Ready", "Renamed group should use a trimmed name.");
        Assert(!service.RenameGroup(group.Id, " "), "Empty group rename should be rejected.");
        Assert(!service.RenameGroup(Guid.NewGuid(), "Missing"), "Missing group should fail safely.");
    }

    private static void ProjectServiceGroupDelete()
    {
        var state = AppState.CreateDefault();
        var project = state.Projects.Single();
        var group = new GroupItem { ProjectId = project.Id, Name = "Group" };
        var task = TaskItem.Create("Grouped", projectId: project.Id);
        task.GroupId = group.Id;
        state.Groups.Add(group);
        state.Tasks.Add(task);
        var service = new ProjectService(state);

        Assert(service.DeleteGroup(group.Id), "Existing group should be deleted.");
        Assert(state.Tasks.Contains(task), "Deleting a group must not delete its tasks.");
        Assert(task.GroupId is null, "Deleting a group should clear task GroupId.");
        Assert(task.ProjectId == project.Id, "Deleting a group should preserve task ProjectId.");
        Assert(!service.DeleteGroup(Guid.NewGuid()), "Missing group deletion should fail safely.");
    }

    private static void ProjectServiceTaskAssignment()
    {
        var state = AppState.CreateDefault();
        var firstProject = state.Projects.Single();
        var secondProject = new ProjectItem { Name = "Second", SortOrder = 1 };
        var firstGroup = new GroupItem { ProjectId = firstProject.Id, Name = "First group" };
        var secondGroup = new GroupItem { ProjectId = secondProject.Id, Name = "Second group" };
        var task = TaskItem.Create("Assign me", projectId: firstProject.Id);
        task.GroupId = firstGroup.Id;
        var ancestor = TaskItem.Create("Ancestor", projectId: firstProject.Id);
        ancestor.GroupId = firstGroup.Id;
        var parent = TaskItem.Create("Parent", projectId: firstProject.Id);
        parent.GroupId = firstGroup.Id;
        parent.ParentTaskId = ancestor.Id;
        var child = TaskItem.Create("Child", projectId: firstProject.Id);
        child.GroupId = firstGroup.Id;
        child.ParentTaskId = parent.Id;
        state.Projects.Add(secondProject);
        state.Groups.Add(firstGroup);
        state.Groups.Add(secondGroup);
        state.Tasks.Add(task);
        state.Tasks.Add(ancestor);
        state.Tasks.Add(parent);
        state.Tasks.Add(child);
        var service = new ProjectService(state);

        Assert(service.AssignTaskToProject(task.Id, secondProject.Id), "Task should move to an existing project.");
        Assert(task.ProjectId == secondProject.Id, "Project assignment should update ProjectId.");
        Assert(task.GroupId is null, "Moving across projects should clear an invalid group.");
        Assert(service.AssignTaskToGroup(task.Id, secondGroup.Id), "Task should join a group in its project.");
        Assert(task.GroupId == secondGroup.Id, "Group assignment should set GroupId.");
        Assert(service.AssignTaskToProject(task.Id, secondProject.Id), "Same-project assignment should succeed.");
        Assert(task.GroupId is null, "Assigning to project should move task to project root.");
        Assert(service.AssignTaskToGroup(task.Id, firstGroup.Id), "Task should move to an existing group.");
        Assert(task.GroupId == firstGroup.Id, "Group assignment should update GroupId.");
        Assert(task.ProjectId == firstProject.Id, "Group assignment should also update ProjectId.");
        Assert(service.ClearTaskGroup(task.Id), "Existing task group should clear.");
        Assert(task.GroupId is null, "ClearTaskGroup should clear only GroupId.");
        Assert(task.ParentTaskId is null, "ClearTaskGroup should move task out of a task parent.");
        Assert(task.ProjectId == firstProject.Id, "ClearTaskGroup should preserve ProjectId.");
        Assert(!service.AssignTaskToProject(Guid.NewGuid(), secondProject.Id), "Missing task should fail safely.");
        Assert(!service.AssignTaskToProject(task.Id, Guid.NewGuid()), "Missing project should fail safely.");
        Assert(!service.AssignTaskToGroup(task.Id, Guid.NewGuid()), "Missing group should fail safely.");
        Assert(!service.ClearTaskGroup(Guid.NewGuid()), "Missing task clear should fail safely.");
        Assert(service.AssignTaskToGroup(parent.Id, secondGroup.Id), "ProjectService should use tree-aware move.");
        Assert(parent.ParentTaskId is null, "Moving parent to group should clear its ParentTaskId.");
        Assert(child.ParentTaskId == parent.Id, "Descendant should remain attached to moved parent.");
        Assert(child.ProjectId == secondProject.Id, "Descendant ProjectId should cascade.");
        Assert(child.GroupId == secondGroup.Id, "Descendant GroupId should cascade.");
        Assert(service.ClearTaskGroup(parent.Id), "Clearing parent group should move branch to project root.");
        Assert(parent.GroupId is null && parent.ParentTaskId is null, "Cleared parent should be project-root task.");
        Assert(child.GroupId is null && child.ProjectId == secondProject.Id, "Clear should cascade to descendants.");
    }

    private static void ProjectServiceOrphanSafety()
    {
        var state = AppState.CreateDefault();
        var defaultProject = state.Projects.Single();
        var task = TaskItem.Create("Orphan", projectId: Guid.NewGuid());
        task.GroupId = Guid.NewGuid();
        var orphanGroup = new GroupItem
        {
            ProjectId = Guid.NewGuid(),
            Name = "Orphan group"
        };
        state.Tasks.Add(task);
        state.Groups.Add(orphanGroup);
        Assert(StateMigrator.RepairCurrentState(state), "Corrupt fixture should require explicit repair.");
        var service = new ProjectService(state);

        Assert(orphanGroup.ProjectId == defaultProject.Id, "Orphan group should repair to Default.");
        Assert(task.ProjectId == defaultProject.Id, "Orphan task should repair to Default.");
        Assert(task.GroupId is null, "Missing group reference should be cleared.");
        Assert(
            ProjectReferenceResolver.ResolveProject(state, task)?.Id == defaultProject.Id,
            "Orphan project references should resolve to Default.");
        Assert(service.AssignTaskToGroup(task.Id, orphanGroup.Id), "Repaired group assignment should succeed safely.");
        Assert(task.GroupId == orphanGroup.Id, "Repaired group should become the task parent.");
        Assert(service.AssignTaskToProject(task.Id, defaultProject.Id), "Orphan task should move to Default safely.");
        Assert(task.ProjectId == defaultProject.Id, "Safe assignment should repair orphan ProjectId.");
        Assert(task.GroupId is null, "Safe assignment should clear orphan GroupId.");
    }

    private static void TreeServiceConstructorsAreSideEffectFree()
    {
        var orphanProjectId = Guid.NewGuid();
        var task = TaskItem.Create("Orphan", projectId: orphanProjectId);
        var state = new AppState
        {
            Projects = new System.Collections.Generic.List<ProjectItem>(),
            Tasks = { task }
        };

        _ = new ProjectService(state);
        _ = new TreeStateService(state);

        Assert(state.Projects.Count == 0, "Service construction must not create Default project.");
        Assert(task.ProjectId == orphanProjectId, "Service construction must not rewrite task assignment.");
        Assert(StateMigrator.RepairCurrentState(state), "Explicit boundary repair should normalize state.");
        Assert(state.Projects.Single().Name == ProjectItem.DefaultName, "Explicit repair should create Default.");
        Assert(task.ProjectId == state.Projects.Single().Id, "Explicit repair should reassign orphan task.");
    }

    private static void TreeNodeCreation()
    {
        var state = CreateEmptyTreeState();
        var service = new TreeStateService(state);
        var timestamp = DateTimeOffset.Parse("2026-06-21T08:00:00Z");

        var project = service.CreateProject("  Product  ", timestamp);
        var group = service.CreateGroup(project!.Id, "  Planning  ", timestamp);
        var projectTask = service.CreateTask(project.Id, "Project task", timestamp);
        var groupTask = service.CreateTask(group!.Id, "Group task", timestamp);
        var subtask = service.CreateTask(groupTask!.Id, "Subtask", timestamp);

        Assert(project.Kind == TreeNodeKind.Project, "Created project should have Project kind.");
        Assert(project.ParentId is null, "Project should be a root node.");
        Assert(project.Title == "Product", "Project title should be trimmed.");
        Assert(group.Kind == TreeNodeKind.Group, "Created group should have Group kind.");
        Assert(group.ParentId == project.Id, "Group should be parented by its project.");
        Assert(projectTask?.ParentId == project.Id, "Direct task should be parented by project.");
        Assert(groupTask.Kind == TreeNodeKind.Task, "Created task should have Task kind.");
        Assert(groupTask.ParentId == group.Id, "Group task should be parented by group.");
        Assert(subtask?.ParentId == groupTask.Id, "Subtask should be parented by task.");
        Assert(subtask?.CreatedAtUtc == timestamp, "Creation timestamp should be retained.");
        Assert(subtask?.UpdatedAtUtc == timestamp, "Update timestamp should initialize at creation.");
        Assert(service.CreateGroup(Guid.NewGuid(), "Orphan") is null, "Missing project should reject group.");
        Assert(service.CreateTask(Guid.NewGuid(), "Orphan") is null, "Missing parent should reject task.");
        Assert(service.CreateTask(project.Id, "  ") is null, "Empty task title should be rejected.");
    }

    private static void TreeRenameAndSafeDelete()
    {
        var state = CreateEmptyTreeState();
        var service = new TreeStateService(state);
        var defaultProject = state.Projects.Single();
        var project = service.CreateProject("Temporary")!;
        var group = service.CreateGroup(project.Id, "Group")!;
        var parentTask = service.CreateTask(group.Id, "Parent")!;
        var childTask = service.CreateTask(parentTask.Id, "Child")!;
        var taskCount = state.Tasks.Count;

        Assert(service.RenameNode(parentTask.Id, "  Renamed  "), "Existing node should be renamed.");
        Assert(service.GetNode(parentTask.Id)?.Title == "Renamed", "Renamed title should be trimmed.");
        Assert(!service.RenameNode(parentTask.Id, " "), "Empty rename should be rejected.");
        Assert(!service.RenameNode(Guid.NewGuid(), "Missing"), "Missing node rename should fail safely.");
        Assert(service.DeleteNode(parentTask.Id), "Task parent should be deleted safely.");
        Assert(service.GetNode(childTask.Id)?.ParentId == group.Id, "Child should reparent to deleted task parent.");
        Assert(state.Tasks.Count == taskCount - 1, "Only the selected task should be deleted.");
        Assert(service.DeleteNode(project.Id), "Non-default project should be deleted.");
        Assert(state.Tasks.Count == taskCount - 1, "Deleting project must preserve remaining tasks.");
        Assert(state.Tasks.Single().ProjectId == defaultProject.Id, "Project tasks should move to Default.");
        Assert(state.Groups.All(item => item.ProjectId != project.Id), "Deleted project groups should be removed.");
        Assert(!service.DeleteNode(defaultProject.Id), "Default project must remain safe.");
    }

    private static void TreeDeleteAtomicFailure()
    {
        var state = CreateEmptyTreeState();
        var service = new TreeStateService(state);
        var project = state.Projects.Single();
        var parent = service.CreateTask(project.Id, "Parent")!;
        var firstChild = service.CreateTask(parent.Id, "First child")!;
        var secondChild = service.CreateTask(parent.Id, "Second child")!;
        var firstBefore = state.Tasks.Single(task => task.Id == firstChild.Id);
        var secondBefore = state.Tasks.Single(task => task.Id == secondChild.Id);
        var firstAssignment = (firstBefore.ProjectId, firstBefore.GroupId, firstBefore.ParentTaskId);
        var secondAssignment = (secondBefore.ProjectId, secondBefore.GroupId, secondBefore.ParentTaskId);

        state.Projects.Clear();
        state.Tasks.Single(task => task.Id == parent.Id).ProjectId = Guid.NewGuid();

        Assert(!service.DeleteNode(parent.Id), "Delete should fail when replacement parent cannot be planned.");
        Assert(service.GetNode(parent.Id) is not null, "Failed delete must preserve parent task.");
        Assert(
            (firstBefore.ProjectId, firstBefore.GroupId, firstBefore.ParentTaskId) == firstAssignment,
            "Failed delete must not partially reparent first child.");
        Assert(
            (secondBefore.ProjectId, secondBefore.GroupId, secondBefore.ParentTaskId) == secondAssignment,
            "Failed delete must not partially reparent later children.");
    }

    private static void TreeMoveAndCycleGuards()
    {
        var state = CreateEmptyTreeState();
        var service = new TreeStateService(state);
        var firstProject = state.Projects.Single();
        var secondProject = service.CreateProject("Second")!;
        var firstGroup = service.CreateGroup(firstProject.Id, "First group")!;
        var secondGroup = service.CreateGroup(secondProject.Id, "Second group")!;
        var parentTask = service.CreateTask(firstGroup.Id, "Parent")!;
        var childTask = service.CreateTask(parentTask.Id, "Child")!;

        Assert(!service.MoveNode(parentTask.Id, parentTask.Id), "Node cannot move under itself.");
        Assert(!service.MoveNode(parentTask.Id, childTask.Id), "Node cannot move under its descendant.");
        Assert(!service.MoveNode(childTask.Id, Guid.NewGuid()), "Missing parent should be rejected.");
        Assert(!service.MoveNode(firstGroup.Id, parentTask.Id), "Group can only move under project.");
        Assert(service.MoveNode(parentTask.Id, secondGroup.Id), "Task branch should move between groups.");
        var movedParent = state.Tasks.Single(task => task.Id == parentTask.Id);
        var inheritedChild = state.Tasks.Single(task => task.Id == childTask.Id);
        Assert(movedParent.ProjectId == secondProject.Id, "Moved parent should update ProjectId.");
        Assert(inheritedChild.ProjectId == secondProject.Id, "Descendant should inherit moved ProjectId.");
        Assert(inheritedChild.GroupId == secondGroup.Id, "Descendant should inherit moved GroupId.");
        Assert(service.MoveNode(parentTask.Id, firstGroup.Id), "Task branch should move back for cycle checks.");
        Assert(service.MoveNode(childTask.Id, secondGroup.Id), "Task should move between branches.");
        var movedTask = state.Tasks.Single(task => task.Id == childTask.Id);
        Assert(movedTask.ParentTaskId is null, "Moving to group should clear ParentTaskId.");
        Assert(movedTask.GroupId == secondGroup.Id, "Moving to group should update GroupId.");
        Assert(movedTask.ProjectId == secondProject.Id, "Moving to group should update ProjectId.");
        Assert(service.MoveNode(firstGroup.Id, secondProject.Id), "Group should move between projects.");
        Assert(state.Groups.Single(item => item.Id == firstGroup.Id).ProjectId == secondProject.Id, "Group project should update.");
        Assert(state.Tasks.Single(task => task.Id == parentTask.Id).ProjectId == secondProject.Id, "Group tasks should follow project.");
    }

    private static void TreeOrphanFallback()
    {
        var state = CreateEmptyTreeState();
        var defaultProject = state.Projects.Single();
        var orphanTask = TaskItem.Create("Orphan", projectId: Guid.NewGuid());
        orphanTask.GroupId = Guid.NewGuid();
        orphanTask.ParentTaskId = Guid.NewGuid();
        state.Tasks.Add(orphanTask);
        Assert(StateMigrator.RepairCurrentState(state), "Orphan fixture should require explicit repair.");
        var service = new TreeStateService(state);

        Assert(service.GetNode(orphanTask.Id)?.ParentId == defaultProject.Id, "Orphan task should resolve under Default.");
        Assert(
            service.GetChildren(defaultProject.Id).Any(node => node.Id == orphanTask.Id),
            "Orphan fallback should prevent an unreachable task.");
        Assert(
            service.GetProjection(defaultProject.Id, TreeProjection.AllInProject)
                .Any(node => node.Id == orphanTask.Id),
            "Projection should include safely resolved orphan task.");
        Assert(!service.MoveNode(orphanTask.Id, Guid.NewGuid()), "Move to orphan parent should be rejected.");
    }

    private static void TreeMissingDefaultRecovery()
    {
        WithTemporaryDirectory(directory =>
        {
            var otherProjectId = Guid.NewGuid();
            var orphanGroupId = Guid.NewGuid();
            var orphanTaskId = Guid.NewGuid();
            var handEditedJson =
                $$"""
                {
                  "schemaVersion": 2,
                  "tasks": [{
                    "id": "{{orphanTaskId}}",
                    "title": "Preserved task",
                    "description": "preserved",
                    "completed": false,
                    "priority": "normal",
                    "inWork": false,
                    "createdAtUtc": "2026-06-01T08:30:00+00:00",
                    "projectId": "{{Guid.NewGuid()}}",
                    "groupId": "{{Guid.NewGuid()}}"
                  }],
                  "projects": [{
                    "id": "{{otherProjectId}}",
                    "name": "Other",
                    "sortOrder": 0,
                    "active": true,
                    "createdAtUtc": "2026-06-01T08:30:00+00:00"
                  }],
                  "groups": [{
                    "id": "{{orphanGroupId}}",
                    "projectId": "{{Guid.NewGuid()}}",
                    "name": "Orphan group",
                    "sortOrder": 0,
                    "active": true,
                    "createdAtUtc": "2026-06-01T08:30:00+00:00"
                  }],
                  "overlaySettings": { "alwaysOnTop": true },
                  "windowPlacement": { "collapsedLeft": 1800, "collapsedTop": 12 },
                  "createdAtUtc": "2026-06-01T08:30:00+00:00",
                  "updatedAtUtc": "2026-06-01T08:30:00+00:00"
                }
                """;
            var store = new AppStateStore(directory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(store.StatePath, handEditedJson);
            var loaded = store.Load();
            var defaultProject = loaded.Projects.Single(project =>
                project.Name == ProjectItem.DefaultName);
            var loadedTask = loaded.Tasks.Single(task => task.Id == orphanTaskId);
            var loadedGroup = loaded.Groups.Single(group => group.Id == orphanGroupId);
            var rootNodes = new TreeStateService(loaded).GetChildren(parentId: null);

            Assert(loaded.Tasks.Count == 1, "Default recovery must not delete user tasks.");
            Assert(loadedGroup.ProjectId == defaultProject.Id, "Orphan group should move to Default.");
            Assert(loadedTask.ProjectId == defaultProject.Id, "Orphan task should move to Default.");
            Assert(loadedTask.GroupId is null, "Missing group reference should clear.");
            Assert(rootNodes.All(node => node.Kind == TreeNodeKind.Project), "Tasks must not become root siblings.");
            Assert(loaded.WindowPlacement.CollapsedLeft == 1800, "Default recovery must preserve handle anchor.");
            Assert(File.Exists(store.BackupPath), "Normalized load should preserve the hand-edited state as backup.");
            Assert(!File.ReadAllText(store.StatePath).Contains("\"active\":"), "Project/group Active should not persist.");
        });
    }

    private static void TreeCycleRepair()
    {
        var state = CreateEmptyTreeState();
        var project = state.Projects.Single();
        var first = TaskItem.Create("First", projectId: project.Id);
        var second = TaskItem.Create("Second", projectId: project.Id);
        first.ParentTaskId = second.Id;
        second.ParentTaskId = first.Id;
        state.Tasks.Add(first);
        state.Tasks.Add(second);

        Assert(StateMigrator.RepairCurrentState(state), "Cycle repair should report a state change.");
        Assert(
            !(first.ParentTaskId == second.Id && second.ParentTaskId == first.Id),
            "Repair should break the cyclic parent relationship.");
        Assert(state.Tasks.Count == 2, "Cycle repair must preserve both tasks.");
        var service = new TreeStateService(state);
        Assert(service.GetProjectRoot(first.Id)?.Id == project.Id, "First repaired task should remain reachable.");
        Assert(service.GetProjectRoot(second.Id)?.Id == project.Id, "Second repaired task should remain reachable.");
    }

    private static void SaveRepairDiagnostics()
    {
        WithTemporaryDirectory(directory =>
        {
            var diagnostics = new System.Collections.Generic.List<string>();
            var store = new AppStateStore(
                directory,
                (message, exception) => diagnostics.Add(message));
            var state = AppState.CreateDefault();

            store.Save(state);
            Assert(
                diagnostics.All(message => message != "State normalized before save."),
                "Healthy save should not report normalization.");

            state.Projects.Clear();
            state.Tasks[0].ProjectId = Guid.NewGuid();
            store.Save(state);
            Assert(
                diagnostics.Count(message => message == "State normalized before save.") == 1,
                "Repairing save should report normalization once.");
            Assert(state.Projects.Any(project => project.Name == ProjectItem.DefaultName), "Save repair should restore Default.");

            store.Save(state);
            Assert(
                diagnostics.Count(message => message == "State normalized before save.") == 1,
                "Subsequent healthy save should not repeat normalization log.");
        });
    }

    private static void TreeSiblingOrdering()
    {
        var state = CreateEmptyTreeState();
        var service = new TreeStateService(state);
        var project = state.Projects.Single();
        var first = service.CreateGroup(project.Id, "First")!;
        var second = service.CreateTask(project.Id, "Second")!;
        var third = service.CreateGroup(project.Id, "Third")!;

        Assert(
            service.GetChildren(project.Id).Select(node => node.Title).SequenceEqual(new[] { "First", "Second", "Third" }),
            "Creation order should be shared across sibling kinds.");
        Assert(service.ReorderNode(third.Id, 0), "Sibling should reorder.");
        var reordered = service.GetChildren(project.Id);
        Assert(
            reordered.Select(node => node.Title).SequenceEqual(new[] { "Third", "First", "Second" }),
            "Sibling query should follow updated order.");
        Assert(reordered.Select(node => node.SortOrder).SequenceEqual(new[] { 0, 1, 2 }), "Sort orders should normalize.");
        Assert(service.ReorderNode(first.Id, 99), "Out-of-range reorder should clamp safely.");
        Assert(service.GetChildren(project.Id).Last().Id == first.Id, "Clamped reorder should move node last.");
    }

    private static void TreeActiveAndStatus()
    {
        var state = CreateEmptyTreeState();
        var service = new TreeStateService(state);
        var project = state.Projects.Single();
        var group = service.CreateGroup(project.Id, "Group")!;
        var task = service.CreateTask(group.Id, "Task")!;
        var timestamp = DateTimeOffset.Parse("2026-06-21T09:00:00Z");

        Assert(!service.MarkActive(group.Id, true, timestamp), "Group active state is intentionally unsupported.");
        Assert(service.GetNode(group.Id)?.Active == false, "Group should remain inactive in normalized view.");
        Assert(service.MarkActive(task.Id, true, timestamp), "Task should become active.");
        Assert(state.Tasks.Single().InWork, "Task active should retain flat InWork compatibility.");
        Assert(service.GetNode(task.Id)?.Active == true, "Normalized task should report active.");
        Assert(service.MarkStatus(task.Id, TreeNodeStatus.Done, timestamp), "Task should become done.");
        Assert(state.Tasks.Single().Completed, "Done should retain flat Completed compatibility.");
        Assert(!state.Tasks.Single().InWork, "Done task should leave active state.");
        Assert(service.GetNode(task.Id)?.Status == TreeNodeStatus.Done, "Normalized status should be Done.");
        Assert(!service.MarkActive(task.Id, true), "Completed task cannot become active.");
        Assert(service.MarkStatus(task.Id, TreeNodeStatus.Todo), "Task should return to todo.");
        Assert(!state.Tasks.Single().Completed, "Todo should clear flat Completed state.");
        Assert(!service.MarkStatus(group.Id, TreeNodeStatus.Done), "Group status change should be rejected.");
    }

    private static void TreeNavigation()
    {
        var state = CreateEmptyTreeState();
        var service = new TreeStateService(state);
        var project = state.Projects.Single();
        var group = service.CreateGroup(project.Id, "Group")!;
        var parent = service.CreateTask(group.Id, "Parent")!;
        var child = service.CreateTask(parent.Id, "Child")!;
        var grandchild = service.CreateTask(child.Id, "Grandchild")!;

        Assert(
            service.GetChildren(parent.Id).Select(node => node.Id).SequenceEqual(new[] { child.Id }),
            "GetChildren should return direct children only.");
        Assert(
            service.GetAncestors(grandchild.Id).Select(node => node.Id).SequenceEqual(
                new[] { project.Id, group.Id, parent.Id, child.Id }),
            "Ancestors should be ordered root to direct parent.");
        Assert(
            service.GetDescendants(parent.Id).Select(node => node.Id).SequenceEqual(
                new[] { child.Id, grandchild.Id }),
            "Descendants should use pre-order traversal.");
        Assert(service.GetProjectRoot(grandchild.Id)?.Id == project.Id, "Project root should resolve.");
        Assert(
            service.GetCurrentBranch(child.Id).Select(node => node.Id).SequenceEqual(
                new[] { project.Id, group.Id, parent.Id, child.Id, grandchild.Id }),
            "Current branch should include context and selected subtree.");
    }

    private static void TreeProjections()
    {
        var state = CreateEmptyTreeState();
        var service = new TreeStateService(state);
        var project = state.Projects.Single();
        var firstGroup = service.CreateGroup(project.Id, "First group")!;
        var firstTask = service.CreateTask(firstGroup.Id, "First task")!;
        var child = service.CreateTask(firstTask.Id, "Child")!;
        var secondGroup = service.CreateGroup(project.Id, "Second group")!;
        var secondTask = service.CreateTask(secondGroup.Id, "Second task")!;
        service.MarkActive(firstTask.Id, true);
        service.MarkActive(secondTask.Id, true);
        var taskCount = state.Tasks.Count;

        Assert(
            service.GetProjection(project.Id, TreeProjection.AllInProject)
                .Select(node => node.Title)
                .SequenceEqual(new[]
                {
                    ProjectItem.DefaultName,
                    "First group",
                    "First task",
                    "Child",
                    "Second group",
                    "Second task"
                }),
            "AllInProject should return project pre-order.");
        Assert(
            service.GetProjection(project.Id, TreeProjection.ActiveOnly)
                .Select(node => node.Id)
                .SequenceEqual(new[] { firstTask.Id, secondTask.Id }),
            "ActiveOnly should return active tasks only in tree order.");
        Assert(
            service.GetProjection(project.Id, TreeProjection.ActivePlusAncestors)
                .Select(node => node.Id)
                .SequenceEqual(new[]
                {
                    project.Id,
                    firstGroup.Id,
                    firstTask.Id,
                    secondGroup.Id,
                    secondTask.Id
                }),
            "ActivePlusAncestors should add context without inactive descendants.");
        Assert(
            service.GetProjection(project.Id, TreeProjection.CurrentBranchOnly, firstGroup.Id)
                .Select(node => node.Id)
                .SequenceEqual(new[] { project.Id, firstGroup.Id, firstTask.Id, child.Id }),
            "CurrentBranchOnly should include selected subtree and ancestors.");
        Assert(state.Tasks.Count == taskCount, "Projection must not mutate state.");
    }

    private static void TreeLegacyFlatStateCompatibility()
    {
        WithTemporaryDirectory(directory =>
        {
            var projectId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var oldFlatJson =
                $$"""
                {
                  "schemaVersion": 2,
                  "tasks": [{
                    "id": "{{taskId}}",
                    "title": "Legacy flat task",
                    "description": "preserved",
                    "completed": false,
                    "priority": "normal",
                    "inWork": true,
                    "createdAtUtc": "2026-06-01T08:30:00+00:00",
                    "projectId": "{{projectId}}",
                    "groupId": null
                  }],
                  "projects": [{
                    "id": "{{projectId}}",
                    "name": "Default",
                    "sortOrder": 0,
                    "createdAtUtc": "2026-06-01T08:30:00+00:00"
                  }],
                  "groups": [],
                  "overlaySettings": { "alwaysOnTop": true },
                  "windowPlacement": { "collapsedLeft": 1800, "collapsedTop": 12 },
                  "createdAtUtc": "2026-06-01T08:30:00+00:00",
                  "updatedAtUtc": "2026-06-01T08:30:00+00:00"
                }
                """;
            var store = new AppStateStore(directory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(store.StatePath, oldFlatJson);

            var loaded = store.Load();
            var service = new TreeStateService(loaded);
            var taskNode = service.GetNode(taskId);

            Assert(loaded.SchemaVersion == 2, "Additive tree fields should not require schema migration.");
            Assert(taskNode?.ParentId == projectId, "Legacy flat task should resolve under its project.");
            Assert(taskNode?.Active == true, "Legacy InWork should map to Active.");
            Assert(taskNode?.Status == TreeNodeStatus.Todo, "Legacy Completed should map to Status.");
            Assert(loaded.Tasks.Single().Description == "preserved", "Legacy task data should remain intact.");
            Assert(loaded.WindowPlacement.CollapsedLeft == 1800, "Handle anchor should remain intact.");

            var nested = service.CreateTask(taskId, "Nested")!;
            store.Save(loaded);
            var reloaded = new AppStateStore(directory).Load();
            var reloadedNested = new TreeStateService(reloaded).GetNode(nested.Id);
            Assert(reloadedNested?.ParentId == taskId, "Tree parent should survive save/load roundtrip.");
        });
    }

    private static void DailyMvpProjectSeeding()
    {
        var state = AppState.CreateDefault();

        Assert(MvpProjectSeeder.EnsureSeedProjects(state), "First seed pass should change state.");
        Assert(!MvpProjectSeeder.EnsureSeedProjects(state), "Seed pass should be idempotent.");
        foreach (var definition in ProjectColorPalette.MvpProjects)
        {
            var project = state.Projects.Single(item => item.Name == definition.Name);
            Assert(project.ColorHex == definition.ColorHex, $"{definition.Name} should use its MVP color.");
        }

        Assert(
            TaskCaptureService.ResolvePreferredProject(state)?.Name == "Personal",
            "Personal should become the initial quick-capture project.");
    }

    private static void OldAttentionStateCompatibility()
    {
        WithTemporaryDirectory(directory =>
        {
            var projectId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var oldStateJson =
                $$"""
                {
                  "schemaVersion": 2,
                  "tasks": [{
                    "id": "{{taskId}}",
                    "title": "Legacy active task",
                    "description": "",
                    "completed": false,
                    "priority": "normal",
                    "inWork": true,
                    "createdAtUtc": "2026-06-01T08:30:00+00:00",
                    "projectId": "{{projectId}}"
                  }],
                  "projects": [{
                    "id": "{{projectId}}",
                    "name": "Default",
                    "sortOrder": 0,
                    "createdAtUtc": "2026-06-01T08:30:00+00:00"
                  }],
                  "groups": [],
                  "overlaySettings": { "alwaysOnTop": true },
                  "windowPlacement": {},
                  "createdAtUtc": "2026-06-01T08:30:00+00:00",
                  "updatedAtUtc": "2026-06-01T08:30:00+00:00"
                }
                """;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "state.json"), oldStateJson);

            var loaded = new AppStateStore(directory).Load();
            var task = loaded.Tasks.Single();
            var project = loaded.Projects.Single();

            Assert(task.Status == TaskStatus.InWork, "Legacy InWork should migrate to InWork status.");
            Assert(ProjectColorPalette.IsValid(project.ColorHex), "Missing project color should be repaired.");
            Assert(task.RemindAtUtc is null, "Old tasks should default to no reminder.");
            Assert(task.WaitingFor == string.Empty, "Old tasks should default waitingFor to empty.");
        });
    }

    private static void WaitingStatusPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = store.Load();
            var task = state.Tasks[0];
            TaskInteractionService.SetStatus(state, task, TaskStatus.Waiting);
            task.WaitingFor = "Madina";
            task.RemindAtUtc = DateTimeOffset.Parse("2026-06-22T12:00:00Z");
            task.RemindEveryMinutes = 120;

            store.Save(state);
            var loaded = new AppStateStore(directory).Load().Tasks.Single(item => item.Id == task.Id);

            Assert(loaded.Status == TaskStatus.Waiting, "Waiting status should persist.");
            Assert(loaded.WaitingFor == "Madina", "waitingFor should persist.");
            Assert(loaded.RemindEveryMinutes == 120, "Repeat interval should persist.");
        });
    }

    private static void ProjectColorPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = store.Load();
            state.Projects[0].ColorHex = "#123ABC";

            store.Save(state);
            var loaded = new AppStateStore(directory).Load();

            Assert(loaded.Projects[0].ColorHex == "#123ABC", "Project color should persist.");
        });
    }

    private static void ReminderScheduling()
    {
        var task = TaskItem.Create("Follow up");
        var start = DateTimeOffset.Parse("2026-06-22T08:00:00Z");
        ReminderService.ApplyPreset(
            task,
            ReminderPreset.RepeatEvery2Hours,
            start,
            TimeZoneInfo.Utc);

        var due = ReminderService.ProcessDueReminders(
            new[] { task },
            start.AddHours(2));

        Assert(due.Count == 1, "Reminder should activate once at its scheduled time.");
        Assert(task.ReminderActive, "REMIND attention should remain visibly active.");
        Assert(task.LastReminderAtUtc == start.AddHours(2), "Last reminder time should be recorded.");
        Assert(task.RemindAtUtc == start.AddHours(4), "Repeating reminder should schedule the next occurrence.");
        Assert(
            ReminderService.ProcessDueReminders(new[] { task }, start.AddHours(3)).Count == 0,
            "Active reminder should not fire repeatedly before it is handled.");
    }

    private static void ReminderSnoozeAndStillWaiting()
    {
        var task = TaskItem.Create("Waiting response");
        var now = DateTimeOffset.Parse("2026-06-22T10:00:00Z");
        task.Status = TaskStatus.Waiting;
        task.ReminderActive = true;

        Assert(ReminderService.Snooze(task, 30, now), "Snooze should succeed.");
        Assert(task.RemindAtUtc == now.AddMinutes(30), "Snooze should set the requested time.");
        Assert(!task.ReminderActive, "Snooze should dismiss the REMIND badge.");

        task.RemindEveryMinutes = null;
        Assert(ReminderService.MarkStillWaiting(task, now), "Still waiting should succeed.");
        Assert(task.Status == TaskStatus.Waiting, "Still waiting should retain Waiting status.");
        Assert(task.RemindAtUtc == now.AddHours(2), "Still waiting should default to a two-hour follow-up.");
    }

    private static void ReminderFocusTransition()
    {
        var state = AppState.CreateDefault();
        state.Tasks.Clear();
        var oneShot = TaskItem.Create("One-shot reminder");
        var oneShotTime = DateTimeOffset.Parse("2026-06-22T07:00:00Z");
        oneShot.RemindAtUtc = oneShotTime;
        state.Tasks.Add(oneShot);
        ReminderService.ProcessDueReminders(state.Tasks, oneShotTime);

        Assert(
            ReminderAttentionService.Focus(state, oneShot, oneShotTime.AddMinutes(1)),
            "Focus should handle a one-shot reminder.");
        Assert(
            ReminderService.IsDue(oneShot, oneShotTime.AddMinutes(1)),
            "The overdue timestamp remains scheduled until the reminder is removed.");
        Assert(
            !ReminderAttentionService.ShouldShowNotification(oneShot, oneShotTime.AddMinutes(1)),
            "A handled overdue timestamp must not recreate REMIND attention.");

        var task = TaskItem.Create("Recurring follow-up");
        state.Tasks.Add(task);
        var start = DateTimeOffset.Parse("2026-06-22T08:00:00Z");
        ReminderService.ApplyPreset(
            task,
            ReminderPreset.RepeatEvery2Hours,
            start,
            TimeZoneInfo.Utc);
        ReminderService.ProcessDueReminders(new[] { task }, start.AddHours(2));

        Assert(
            ReminderAttentionService.ShouldShowNotification(task, start.AddHours(2)),
            "A new reminder occurrence should show attention.");
        Assert(
            ReminderAttentionService.Focus(state, task, start.AddHours(2).AddMinutes(1)),
            "Focus should handle the current reminder occurrence.");
        Assert(
            task.Status == TaskStatus.InWork && task.InWork,
            "Focus should move the task into the internal focused state.");
        Assert(
            !ReminderAttentionService.ShouldShowNotification(task, start.AddHours(2).AddMinutes(1)),
            "Focus should dismiss the current reminder occurrence.");
        Assert(
            task.RemindAtUtc == start.AddHours(4),
            "Focus must preserve the reminder schedule.");

        ReminderService.ProcessDueReminders(new[] { task }, start.AddHours(4));
        Assert(
            ReminderAttentionService.ShouldShowNotification(task, start.AddHours(4)),
            "A later reminder occurrence should notify again.");
    }

    private static void ReminderNotificationSnoozePersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-06-22T10:00:00Z");
            var state = AppState.CreateDefault(now);
            state.Tasks.Clear();
            var task = TaskItem.Create("Persist notification snooze", now, state.Projects[0].Id);
            task.RemindAtUtc = now;
            state.Tasks.Add(task);
            ReminderService.ProcessDueReminders(state.Tasks, now);
            var reminderSchedule = task.RemindAtUtc;

            Assert(
                ReminderAttentionService.SnoozeNotification(task, 30, now),
                "Notification snooze should succeed for a task with REMIND attention.");
            Assert(
                task.RemindAtUtc == reminderSchedule,
                "Notification snooze must not change the reminder schedule.");

            var store = new AppStateStore(directory);
            store.Save(state);
            var loaded = new AppStateStore(directory).Load().Tasks.Single();

            Assert(
                loaded.ReminderSnoozedUntilUtc == now.AddMinutes(30),
                "Notification snooze should survive restart.");
            Assert(
                !ReminderAttentionService.ShouldShowNotification(loaded, now.AddMinutes(29)),
                "The notification should remain hidden during the snooze.");
            Assert(
                ReminderAttentionService.ShouldShowNotification(loaded, now.AddMinutes(31)),
                "The notification should return after the snooze expires.");

            Assert(
                ReminderAttentionService.SnoozeNotification(loaded, 60, now.AddMinutes(31)),
                "A 60-minute notification snooze should succeed.");
            Assert(
                loaded.ReminderSnoozedUntilUtc == now.AddMinutes(91),
                "The 60-minute action should use the requested duration.");
            Assert(
                !ReminderAttentionService.ShouldShowNotification(loaded, now.AddMinutes(90)),
                "The notification should remain hidden during a 60-minute snooze.");
        });
    }

    private static void ReminderAttentionOrdering()
    {
        var now = DateTimeOffset.Parse("2026-06-22T12:00:00Z");
        var normal = TaskItem.Create("Normal", now.AddMinutes(-3));
        normal.SortOrder = 0;
        var focused = TaskItem.Create("Focused", now.AddMinutes(-2));
        focused.Status = TaskStatus.InWork;
        focused.InWork = true;
        focused.SortOrder = 1;
        var due = TaskItem.Create("Due", now.AddMinutes(-1));
        due.RemindAtUtc = now;
        due.SortOrder = 2;

        var ordered = ReminderAttentionService
            .OrderForOverlay(new[] { normal, focused, due }, now)
            .ToList();

        Assert(ordered[0].Id == due.Id, "REMIND tasks should remain first in the overlay.");
        Assert(ordered[1].Id == focused.Id, "Focused tasks should remain ahead of normal tasks.");
    }

    private static void DoneAndClearRemoveReminderAttention()
    {
        var now = DateTimeOffset.Parse("2026-06-22T14:00:00Z");
        var completed = TaskItem.Create("Complete me", now);
        completed.RemindAtUtc = now;
        ReminderService.ProcessDueReminders(new[] { completed }, now);
        TaskInteractionService.Complete(completed, now);
        Assert(
            !ReminderAttentionService.ShouldShowNotification(completed, now),
            "Completing a task should remove its reminder notification.");

        var cleared = TaskItem.Create("Clear me", now);
        cleared.RemindAtUtc = now;
        ReminderService.ProcessDueReminders(new[] { cleared }, now);
        ReminderService.ApplyPreset(cleared, ReminderPreset.None, now);
        Assert(
            !ReminderAttentionService.ShouldShowNotification(cleared, now),
            "Removing a reminder should dismiss its notification.");
        Assert(
            cleared.Status != TaskStatus.Done && cleared.Title == "Clear me",
            "Removing a reminder must not complete or delete the task.");
    }

    private static void QuickTaskCapture()
    {
        var state = AppState.CreateDefault();
        MvpProjectSeeder.EnsureSeedProjects(state);
        var kazChess = state.Projects.Single(project => project.Name == "KazChess");
        var now = DateTimeOffset.Parse("2026-06-22T10:00:00Z");

        var task = TaskCaptureService.CreateQuickTask(
            state,
            new QuickTaskValues(
                "Wait for contract reply",
                kazChess.Id,
                TaskStatus.Waiting,
                ReminderPreset.RepeatEvery2Hours,
                "Madina",
                "Contract follow-up"),
            now,
            TimeZoneInfo.Utc);

        Assert(task is not null, "Quick capture should create a task.");
        Assert(task!.ProjectId == kazChess.Id, "Quick task should use the selected project.");
        Assert(task.Status == TaskStatus.Waiting, "Quick task should use Waiting status.");
        Assert(task.WaitingFor == "Madina", "Quick task should store waitingFor.");
        Assert(task.RemindAtUtc == now.AddHours(2), "Repeat preset should schedule first reminder.");
        Assert(task.RemindEveryMinutes == 120, "Repeat preset should store interval.");
    }

    private static void SaveLoadRoundtrip()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = store.Load();
            var task = state.Tasks[0];

            task.Description = "Stored description";
            task.Priority = TaskPriority.High;
            task.DueAtUtc = DateTimeOffset.UtcNow.AddHours(2);
            TaskInteractionService.Complete(task);
            state.WindowPlacement.Left = 123.5;
            state.WindowPlacement.Top = 456.5;

            store.Save(state);
            var loaded = new AppStateStore(directory).Load();
            var loadedTask = loaded.Tasks.Single(item => item.Id == task.Id);

            Assert(loadedTask.Description == task.Description, "Description did not roundtrip.");
            Assert(loadedTask.Priority == TaskPriority.High, "Priority did not roundtrip.");
            Assert(!loadedTask.InWork, "Done task should not remain in work.");
            Assert(loadedTask.Completed, "Completed flag did not roundtrip.");
            Assert(loadedTask.Status == TaskStatus.Done, "Done status did not roundtrip.");
            Assert(loadedTask.CompletedAtUtc is not null, "Completed timestamp did not roundtrip.");
            Assert(loadedTask.DueAtUtc is not null, "Due timestamp did not roundtrip.");
            Assert(loaded.WindowPlacement.Left == 123.5, "Window left did not roundtrip.");
            Assert(loaded.WindowPlacement.Top == 456.5, "Window top did not roundtrip.");
            Assert(File.Exists(store.BackupPath), "Overwriting state should create a backup.");
        });
    }

    private static void OverlayModeSerialization()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = store.Load();
            state.OverlaySettings.OverlayMode = OverlayMode.Working;

            store.Save(state);

            var json = File.ReadAllText(store.StatePath);
            var loaded = new AppStateStore(directory).Load();

            Assert(
                json.Contains("\"overlayMode\": \"working\""),
                "The unified overlay mode should be serialized.");
            Assert(
                loaded.OverlaySettings.OverlayMode == OverlayMode.Working,
                "Overlay mode should survive a save/load roundtrip.");
            Assert(
                !json.Contains("\"collapsedMode\"") &&
                !json.Contains("\"pinnedActiveMode\""),
                "Legacy mode flags should not be written after normalization.");
        });
    }

    private static void WorkingPresentationSettings()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = store.Load();
            state.OverlaySettings.WorkingIdleFontSize = 17.5;
            state.OverlaySettings.WorkingActiveFontSize = 21;
            state.OverlaySettings.WorkingWindowWidth = 360;
            state.OverlaySettings.WorkingWindowHeight = 280;

            store.Save(state);
            var loaded = new AppStateStore(directory).Load();

            Assert(
                loaded.OverlaySettings.WorkingIdleFontSize == 17.5,
                "Working idle font size should survive save/load.");
            Assert(
                loaded.OverlaySettings.WorkingActiveFontSize == 21,
                "Working active font size should survive save/load.");
            Assert(
                loaded.OverlaySettings.WorkingWindowWidth == 360,
                "Working window width should survive save/load.");
            Assert(
                loaded.OverlaySettings.WorkingWindowHeight == 280,
                "Working window height should survive save/load.");
        });

        var invalid = new OverlaySettings
        {
            WorkingIdleFontSize = 1,
            WorkingActiveFontSize = 100,
            WorkingWindowWidth = 5000,
            WorkingWindowHeight = -10
        };
        Assert(
            invalid.NormalizeWorkingPresentation(),
            "Out-of-range Working presentation values should be normalized.");
        Assert(
            invalid.WorkingIdleFontSize == OverlaySettings.MinimumWorkingFontSize,
            "Working idle font size should clamp to its minimum.");
        Assert(
            invalid.WorkingActiveFontSize == OverlaySettings.MaximumWorkingFontSize,
            "Working active font size should clamp to its maximum.");
        Assert(
            invalid.WorkingWindowWidth == OverlaySettings.MaximumWorkingWindowWidth,
            "Working width should clamp to its maximum.");
        Assert(
            invalid.WorkingWindowHeight == OverlaySettings.MinimumWorkingWindowHeight,
            "Working height should clamp to its minimum.");
        Assert(
            !invalid.NormalizeWorkingPresentation(),
            "Normalized Working presentation values should be idempotent.");

        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            store.Save(AppState.CreateDefault());
            var root = JsonNode.Parse(File.ReadAllText(store.StatePath))!.AsObject();
            var overlaySettings = root["overlaySettings"]!.AsObject();
            overlaySettings.Remove("workingIdleFontSize");
            overlaySettings.Remove("workingActiveFontSize");
            overlaySettings["workingFontSize"] = 18;
            File.WriteAllText(
                store.StatePath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var loaded = store.Load();
            var normalizedJson = File.ReadAllText(store.StatePath);

            Assert(
                loaded.OverlaySettings.WorkingIdleFontSize == 18,
                "Legacy Working font size should migrate to idle font size.");
            Assert(
                loaded.OverlaySettings.WorkingActiveFontSize ==
                OverlaySettings.DefaultWorkingActiveFontSize,
                "Legacy Working font migration should retain the active default.");
            Assert(
                !normalizedJson.Contains("\"workingFontSize\""),
                "Legacy Working font setting should not be rewritten after migration.");
        });
    }

    private static void OldCollapsedModeMigration()
    {
        WithTemporaryDirectory(directory =>
        {
            const string oldStateJson =
                """
                {
                  "schemaVersion": 1,
                  "tasks": [],
                  "overlaySettings": {
                    "activeToPassiveDelayMilliseconds": 500,
                    "alwaysOnTop": true,
                    "collapsedMode": true,
                    "pinnedActiveMode": false
                  },
                  "windowPlacement": {
                    "left": null,
                    "top": null
                  },
                  "createdAtUtc": "2026-06-11T08:30:00+00:00",
                  "updatedAtUtc": "2026-06-11T08:30:00+00:00"
                }
                """;

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "state.json"), oldStateJson);

            var loaded = new AppStateStore(directory).Load();

            Assert(
                loaded.OverlaySettings.OverlayMode == OverlayMode.CollapsedHandle,
                "Old collapsed state should migrate to CollapsedHandle.");
            Assert(
                Directory.GetFiles(directory, "state.corrupt.*.json").Length == 0,
                "A missing collapsed setting should not mark old state as corrupted.");
        });
    }

    private static void OldPinnedModeMigration()
    {
        WithTemporaryDirectory(directory =>
        {
            const string oldStateJson =
                """
                {
                  "schemaVersion": 1,
                  "tasks": [],
                  "overlaySettings": {
                    "activeToPassiveDelayMilliseconds": 500,
                    "alwaysOnTop": true,
                    "collapsedMode": true,
                    "pinnedActiveMode": true
                  },
                  "windowPlacement": {
                    "left": 100,
                    "top": 100,
                    "collapsedLeft": 1800,
                    "collapsedTop": 200
                  },
                  "createdAtUtc": "2026-06-11T08:30:00+00:00",
                  "updatedAtUtc": "2026-06-11T08:30:00+00:00"
                }
                """;

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "state.json"), oldStateJson);
            var loaded = new AppStateStore(directory).Load();

            Assert(
                loaded.OverlaySettings.OverlayMode == OverlayMode.PinnedExpanded,
                "Legacy pinned state should take precedence and migrate to PinnedExpanded.");
            Assert(
                loaded.WindowPlacement.CollapsedLeft == 1800,
                "Mode migration must preserve the collapsed anchor.");
        });
    }

    private static void OldOverlayModeDefault()
    {
        WithTemporaryDirectory(directory =>
        {
            const string oldStateJson =
                """
                {
                  "schemaVersion": 1,
                  "tasks": [],
                  "overlaySettings": {
                    "activeToPassiveDelayMilliseconds": 500,
                    "alwaysOnTop": true
                  },
                  "windowPlacement": {
                    "left": null,
                    "top": null
                  },
                  "createdAtUtc": "2026-06-11T08:30:00+00:00",
                  "updatedAtUtc": "2026-06-11T08:30:00+00:00"
                }
                """;

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "state.json"), oldStateJson);

            var loaded = new AppStateStore(directory).Load();

            Assert(
                loaded.OverlaySettings.OverlayMode == OverlayMode.Working,
                "Old state without mode flags should migrate to Working.");
            Assert(
                loaded.OverlaySettings.WorkingIdleFontSize ==
                OverlaySettings.DefaultWorkingIdleFontSize,
                "Old state should load the default Working idle font size.");
            Assert(
                loaded.OverlaySettings.WorkingActiveFontSize ==
                OverlaySettings.DefaultWorkingActiveFontSize,
                "Old state should load the default Working active font size.");
            Assert(
                loaded.OverlaySettings.WorkingWindowWidth ==
                OverlaySettings.DefaultWorkingWindowWidth,
                "Old state should load the default Working window width.");
            Assert(
                loaded.OverlaySettings.WorkingWindowHeight ==
                OverlaySettings.DefaultWorkingWindowHeight,
                "Old state should load the default Working window height.");
        });
    }

    private static void LegacyAutoModeFallback()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            store.Save(AppState.CreateDefault());
            var legacyJson = File.ReadAllText(store.StatePath)
                .Replace("\"overlayMode\": \"working\"", "\"overlayMode\": \"autoQuestTracker\"");
            Assert(
                legacyJson.Contains("\"overlayMode\": \"autoQuestTracker\""),
                "Test setup should contain the legacy mode value.");
            File.WriteAllText(store.StatePath, legacyJson);

            var loaded = new AppStateStore(directory).Load();
            var normalizedJson = File.ReadAllText(store.StatePath);

            Assert(
                loaded.OverlaySettings.OverlayMode == OverlayMode.Working,
                "Legacy AutoQuestTracker state should fall back to Working.");
            Assert(
                normalizedJson.Contains("\"overlayMode\": \"working\"") &&
                !normalizedJson.Contains("autoQuestTracker"),
                "Legacy AutoQuestTracker fallback should be persisted.");
        });
    }

    private static void OverlayCollapseGuardBehavior()
    {
        var idle = new OverlayInteractionState(
            OverlayMode: OverlayMode.Working,
            TaskDetailsOpen: false,
            ContextMenuOpen: false,
            SettingsOpen: false,
            ModalDialogOpen: false,
            Dragging: false);

        Assert(
            OverlayCollapseGuard.CanCollapse(idle),
            "Idle overlay should be allowed to collapse.");

        var dueTask = TaskItem.Create("Due while idle");
        dueTask.ReminderActive = true;
        Assert(ReminderService.IsDue(dueTask), "Test task should be due.");
        Assert(
            OverlayCollapseGuard.CanCollapse(idle),
            "A REMIND task must not block Working mode idle transition.");
        Assert(
            OverlayCollapseGuard.CanCollapse(idle with { SettingsOpen = true }),
            "Settings should not block Working from returning to idle.");
        Assert(
            OverlayCollapseGuard.CanCollapse(
                idle with { OverlayMode = OverlayMode.CollapsedHandle }),
            "A REMIND task must not block CollapsedHandle collapse.");
        Assert(
            !OverlayCollapseGuard.CanCollapse(
                idle with
                {
                    OverlayMode = OverlayMode.CollapsedHandle,
                    SettingsOpen = true
                }),
            "Settings should preserve the existing CollapsedHandle interaction guard.");

        var blockers = new[]
        {
            idle with { OverlayMode = OverlayMode.PinnedExpanded },
            idle with { TaskDetailsOpen = true },
            idle with { ContextMenuOpen = true },
            idle with { ModalDialogOpen = true },
            idle with { Dragging = true }
        };

        Assert(
            blockers.All(state => !OverlayCollapseGuard.CanCollapse(state)),
            "Every active interaction should prevent overlay collapse.");
    }

    private static void WorkingActivationPolicyBehavior()
    {
        var workingEntry = OverlayActiveStatePolicy.ForModeEntry(OverlayMode.Working);
        Assert(
            workingEntry is
            {
                IsActive: false,
                IsWorking: true,
                VisualBranch: OverlayVisualBranch.Working,
                ShowActiveChrome: false,
                ShowDescriptions: false,
                AllowFocusBadge: false,
                UseCompactLayout: true
            },
            "Working entry should expose a complete idle presentation before rendering.");

        var focusedTask = TaskItem.Create("Focused transition task");
        focusedTask.Status = TaskStatus.InWork;
        focusedTask.InWork = true;
        Assert(
            !OverlayTaskPresentationPolicy.ShouldShowFocusBadge(
                focusedTask,
                workingEntry),
            "Working entry should suppress FOCUS before rows are exposed.");

        var workingHover = OverlayActiveStatePolicy.Resolve(
            OverlayMode.Working,
            activeRequested: true);
        Assert(
            workingHover is
            {
                IsActive: true,
                IsWorking: true,
                VisualBranch: OverlayVisualBranch.Working,
                ShowActiveChrome: false,
                ShowDescriptions: true,
                AllowFocusBadge: false,
                UseCompactLayout: true
            },
            "Working hover should activate without adding expanded-only visuals.");
        Assert(
            !OverlayTaskPresentationPolicy.ShouldShowFocusBadge(
                focusedTask,
                workingHover),
            "Active Working should never expose a FOCUS chip.");

        var pinnedEntry = OverlayActiveStatePolicy.ForModeEntry(
            OverlayMode.PinnedExpanded);
        Assert(
            pinnedEntry is
            {
                IsActive: true,
                IsWorking: false,
                VisualBranch: OverlayVisualBranch.Expanded,
                ShowActiveChrome: true,
                ShowDescriptions: true,
                AllowFocusBadge: true,
                UseCompactLayout: false
            },
            "Switching to Pinned should restore its complete active presentation.");
        Assert(
            OverlayTaskPresentationPolicy.ShouldShowFocusBadge(
                focusedTask,
                pinnedEntry),
            "Pinned entry should immediately restore FOCUS chips.");

        Assert(
            workingEntry.VisualBranch != pinnedEntry.VisualBranch,
            "Working and Pinned should use distinct visual branches.");
        Assert(
            !workingEntry.ShowActiveChrome && !workingHover.ShowActiveChrome,
            "Working should never request the expanded active header.");

        var collapsedEntry = OverlayActiveStatePolicy.ForModeEntry(
            OverlayMode.CollapsedHandle);
        Assert(
            !collapsedEntry.IsActive && !collapsedEntry.IsWorking,
            "Switching to CollapsedHandle should not render active immediately.");

        Assert(
            !OverlayActiveStatePolicy.WhileSettingsOpen(
                OverlayMode.Working,
                pointerInside: false),
            "Opening Settings away from Working should preserve idle presentation.");
        Assert(
            OverlayActiveStatePolicy.WhileSettingsOpen(
                OverlayMode.Working,
                pointerInside: true),
            "Working should still activate on real hover while Settings is open.");
        Assert(
            !OverlayActiveStatePolicy.WhileSettingsOpen(
                OverlayMode.Working,
                pointerInside: false),
            "Working should return to idle after pointer leave while Settings is open.");
        Assert(
            OverlayActiveStatePolicy.WhileSettingsOpen(
                OverlayMode.PinnedExpanded,
                pointerInside: false),
            "Settings should preserve Pinned active behavior.");
        Assert(
            OverlayActiveStatePolicy.WhileSettingsOpen(
                OverlayMode.CollapsedHandle,
                pointerInside: false),
            "Settings should preserve the existing CollapsedHandle active interaction.");
    }

    private static void PointerClickVersusDragThreshold()
    {
        Assert(
            !PointerDragGesture.HasExceededThreshold(10, 10, 13, 14),
            "Movement below five DIPs should remain a click.");
        Assert(
            PointerDragGesture.HasExceededThreshold(10, 10, 15, 10),
            "Horizontal movement at the threshold should start dragging.");
        Assert(
            PointerDragGesture.HasExceededThreshold(10, 10, 10, 4),
            "Vertical movement beyond the threshold should start dragging.");
    }

    private static void WindowPlacementNegativeMonitorClamp()
    {
        var workArea = new OverlayBounds(-1920, -200, 1920, 1040);
        var window = new OverlayBounds(-2200, -350, 520, 600);

        var corrected = WindowPlacementGeometry.ClampToWorkArea(window, workArea);

        Assert(corrected.Left == -1920, "Window should clamp to a negative left edge.");
        Assert(corrected.Top == -200, "Window should clamp to a negative top edge.");
        Assert(corrected.Right <= workArea.Right, "Window should remain inside the work area.");
        Assert(corrected.Bottom <= workArea.Bottom, "Window should remain inside the work area.");
    }

    private static void OverlayModeClickCycle()
    {
        Assert(
            OverlayModeCycle.Next(OverlayMode.Working) ==
            OverlayMode.PinnedExpanded,
            "Working should cycle to Pinned.");
        Assert(
            OverlayModeCycle.Next(OverlayMode.PinnedExpanded) ==
            OverlayMode.CollapsedHandle,
            "Pinned should cycle to collapsed handle.");
        Assert(
            OverlayModeCycle.Next(OverlayMode.CollapsedHandle) ==
            OverlayMode.Working,
            "Collapsed handle should cycle to Working.");
        Assert(
            OverlayModeCycle.Next(OverlayMode.AutoQuestTracker) ==
            OverlayMode.Working,
            "Legacy AutoQuestTracker should cycle into Working.");

        var mouseTarget = OverlayModeCycle.Next(OverlayMode.CollapsedHandle);
        var hotkeyTarget = OverlayModeShortcutPolicy.Resolve(
            OverlayMode.CollapsedHandle,
            OverlayModeShortcut.Cycle).Mode;
        Assert(
            mouseTarget == hotkeyTarget &&
            OverlayActiveStatePolicy.ForModeEntry(mouseTarget).VisualBranch ==
            OverlayVisualBranch.Working,
            "Mouse and Ctrl+Alt+1 cycles should resolve the same Working entry policy.");
    }

    private static void OverlayModeShortcutPolicyBehavior()
    {
        var cycle = OverlayModeShortcutPolicy.Resolve(
            OverlayMode.Working,
            OverlayModeShortcut.Cycle);
        Assert(
            cycle.Mode == OverlayMode.PinnedExpanded,
            "Ctrl+Alt+1 should cycle Working to Pinned.");

        cycle = OverlayModeShortcutPolicy.Resolve(
            cycle.Mode,
            OverlayModeShortcut.Cycle);
        Assert(
            cycle.Mode ==
            OverlayMode.CollapsedHandle,
            "Ctrl+Alt+1 should cycle Pinned to collapsed handle.");
        cycle = OverlayModeShortcutPolicy.Resolve(
            cycle.Mode,
            OverlayModeShortcut.Cycle);
        Assert(
            cycle.Mode == OverlayMode.Working,
            "Ctrl+Alt+1 should cycle collapsed handle to Working.");

        foreach (var mode in new[] { OverlayMode.Working, OverlayMode.PinnedExpanded })
        {
            var collapse = OverlayModeShortcutPolicy.Resolve(
                mode,
                OverlayModeShortcut.CollapseOrToggle);
            Assert(
                collapse.Mode == OverlayMode.CollapsedHandle &&
                collapse.EnsureVisible &&
                !collapse.ToggleVisibility,
                "Ctrl+Alt+T should collapse and reveal non-collapsed modes.");
        }

        var toggle = OverlayModeShortcutPolicy.Resolve(
            OverlayMode.CollapsedHandle,
            OverlayModeShortcut.CollapseOrToggle);
        Assert(
            toggle.Mode == OverlayMode.CollapsedHandle &&
            toggle.ToggleVisibility &&
            !toggle.EnsureVisible,
            "Ctrl+Alt+T should retain visibility toggle behavior in collapsed mode.");
    }

    private static void GlobalHotkeyBindingBehavior()
    {
        var bindings = GlobalHotkeyBindings.All;
        var displayNames = bindings.Select(binding => binding.DisplayName).ToArray();

        Assert(
            displayNames.SequenceEqual(
                new[] { "Ctrl+Alt+A", "Ctrl+Alt+Q", "Ctrl+Alt+T", "Ctrl+Alt+1" }),
            "Only the final fixed hotkey set should be registered.");
        Assert(
            bindings.Single(binding => binding.DisplayName == "Ctrl+Alt+A").Command ==
            GlobalHotkeyCommand.CreateTaskWithDescription,
            "Ctrl+Alt+A should create one clipboard task with description.");
        Assert(
            bindings.Single(binding => binding.DisplayName == "Ctrl+Alt+Q").Command ==
            GlobalHotkeyCommand.QuickAddTask,
            "Ctrl+Alt+Q should retain Quick Add.");
        Assert(
            bindings.Single(binding => binding.DisplayName == "Ctrl+Alt+T").Command ==
            GlobalHotkeyCommand.CollapseOrToggleOverlay,
            "Ctrl+Alt+T should retain collapse/toggle behavior.");
        Assert(
            bindings.Single(binding => binding.DisplayName == "Ctrl+Alt+1").Command ==
            GlobalHotkeyCommand.CycleOverlayMode,
            "Ctrl+Alt+1 should cycle overlay modes.");
        Assert(
            displayNames.All(name =>
                name is not "Ctrl+Alt+M" and
                not "Ctrl+Alt+2" and
                not "Ctrl+Alt+3" and
                not "Ctrl+Alt+D" and
                not "Ctrl+Alt+S"),
            "Removed mode and clipboard hotkeys must not be registered.");
    }

    private static void WorkingModeTaskFiltering()
    {
        var now = DateTimeOffset.Parse("2026-06-27T10:00:00Z");
        var todo = TaskItem.Create("TODO", now.AddMinutes(-4));
        var focus = TaskItem.Create("FOCUS", now.AddMinutes(-3));
        focus.Status = TaskStatus.InWork;
        focus.InWork = true;
        var waiting = TaskItem.Create("WAIT", now.AddMinutes(-2));
        waiting.Status = TaskStatus.Waiting;
        var remind = TaskItem.Create("REMIND", now.AddMinutes(-1));
        remind.RemindAtUtc = now;
        var done = TaskItem.Create("DONE", now);
        done.Status = TaskStatus.Done;
        done.Completed = true;
        var tasks = new[] { todo, focus, waiting, remind, done };
        var sourceIds = tasks.Select(task => task.Id).ToArray();
        var ordered = ReminderAttentionService.OrderForOverlay(tasks, now).ToList();

        var working = OverlayTaskFilter
            .SelectForMode(ordered, OverlayMode.Working, now)
            .ToList();

        Assert(working.Count == 2, "Working should show only FOCUS and active REMIND tasks.");
        Assert(working.Any(task => task.Id == focus.Id), "Working should include FOCUS tasks.");
        Assert(working.Any(task => task.Id == remind.Id), "Working should preserve REMIND attention items.");
        Assert(working.All(task => task.Id != todo.Id && task.Id != waiting.Id && task.Id != done.Id),
            "Working should hide normal TODO, WAIT, and DONE tasks.");
        Assert(
            tasks.Select(task => task.Id).SequenceEqual(sourceIds),
            "Working filtering must not mutate or reorder the source list.");

        var pinned = OverlayTaskFilter
            .SelectForMode(ordered, OverlayMode.PinnedExpanded, now)
            .ToList();
        Assert(
            pinned.Count == ordered.Count,
            "Switching Working to Pinned should immediately restore the full task set.");
        Assert(
            OverlayTaskPresentationPolicy.ShouldShowFocusBadge(
                focus,
                OverlayMode.PinnedExpanded),
            "Switching Working to Pinned should immediately restore the FOCUS badge.");

        var returnedToWorking = OverlayTaskFilter
            .SelectForMode(pinned, OverlayMode.Working, now)
            .ToList();
        Assert(
            returnedToWorking.Count == 2 &&
            !OverlayTaskPresentationPolicy.ShouldShowFocusBadge(
                focus,
                OverlayMode.Working),
            "Switching Pinned to Working should immediately restore filtering and badge suppression.");

        var noFocus = OverlayTaskFilter.SelectForMode(
            new[] { todo, waiting },
            OverlayMode.Working,
            now);
        Assert(!noFocus.Any(), "Working should return an empty projection without FOCUS or REMIND tasks.");
    }

    private static void WorkingModeFocusBadge()
    {
        var now = DateTimeOffset.Parse("2026-06-27T10:00:00Z");
        var focus = TaskItem.Create("Focused task", now);
        focus.Status = TaskStatus.InWork;
        focus.InWork = true;
        focus.RemindAtUtc = now;
        var settings = new OverlaySettings
        {
            WorkingIdleFontSize = 15,
            WorkingActiveFontSize = 20
        };

        Assert(
            !OverlayTaskPresentationPolicy.ShouldShowFocusBadge(
                focus,
                OverlayMode.Working),
            "Working should hide its redundant FOCUS badge.");
        Assert(
            OverlayTaskPresentationPolicy.ShouldShowFocusBadge(
                focus,
                OverlayMode.PinnedExpanded),
            "Pinned should preserve the FOCUS badge.");
        Assert(
            OverlayTaskPresentationPolicy.ShouldShowFocusBadge(
                focus,
                OverlayMode.CollapsedHandle),
            "Collapsed expansion should preserve the FOCUS badge.");
        Assert(
            ReminderAttentionService.ShouldShowNotification(focus, now),
            "Hiding FOCUS in Working must not suppress active REMIND attention.");
        Assert(
            OverlayTaskPresentationPolicy.GetWorkingFontSize(
                settings,
                activeMode: false) == 15,
            "Idle Working should use its idle font size.");
        Assert(
            OverlayTaskPresentationPolicy.GetWorkingFontSize(
                settings,
                activeMode: true) == 20,
            "Active Working should use its active font size.");
    }

    private static void HandleSurfaceOwnershipAcrossModes()
    {
        var handle = new OverlayBounds(1872, 0, 48, 20);
        var modes = new[]
        {
            OverlayMode.Working,
            OverlayMode.CollapsedHandle,
            OverlayMode.PinnedExpanded,
            OverlayMode.Working,
            OverlayMode.CollapsedHandle
        };

        foreach (var mode in modes)
        {
            Assert(
                OverlaySurfacePolicy.UseHandleWindowForMode(
                    mode,
                    hasCollapsedAnchor: true),
                $"{mode} should retain HandleWindow when an anchor exists.");

            var panel = PanelLayoutService.PlacePanel(
                handle,
                panelWidth: 450,
                panelHeight: 600,
                new OverlayBounds(0, 0, 1920, 1080));
            Assert(panel.Right == 1920, $"{mode} panel should open inward.");
            Assert(handle.Left == 1872, $"{mode} must not mutate the handle anchor.");
            Assert(handle.Top == 0, $"{mode} must not move the handle downward.");
        }

        Assert(
            !OverlaySurfacePolicy.UseHandleWindowForMode(
                OverlayMode.Working,
                hasCollapsedAnchor: false),
            "Working should retain its fallback surface before an anchor exists.");
    }

    private static void SingleTaskInWorkMode()
    {
        var state = AppState.CreateDefault();
        state.Tasks[0].InWork = true;
        state.Tasks[2].InWork = true;

        TaskInteractionService.SetInWorkMode(state, InWorkMode.SingleTask);
        TaskInteractionService.SetInWork(state, state.Tasks[1], true);
        TaskInteractionService.ActivateFromClick(state, state.Tasks[1]);

        Assert(!state.Tasks[0].InWork, "SingleTask mode should clear other tasks.");
        Assert(state.Tasks[1].InWork, "Selected task should be marked in work.");
        Assert(!state.Tasks[2].InWork, "Unselected tasks should remain clear.");
    }

    private static void MultipleTasksInWorkMode()
    {
        var state = AppState.CreateDefault();
        state.OverlaySettings.InWorkMode = InWorkMode.MultipleTasks;

        TaskInteractionService.ActivateFromClick(state, state.Tasks[0]);
        TaskInteractionService.ActivateFromClick(state, state.Tasks[1]);

        Assert(state.Tasks[0].InWork, "MultipleTasks mode should keep the first task focused.");
        Assert(state.Tasks[1].InWork, "MultipleTasks mode should allow another focused task.");

        TaskInteractionService.ActivateFromClick(state, state.Tasks[0]);
        Assert(!state.Tasks[0].InWork, "Clicking a focused task should toggle it off.");
        Assert(state.Tasks[1].InWork, "Toggling one task should not change another.");
    }

    private static void TaskEditValuesUpdate()
    {
        var state = AppState.CreateDefault();
        var task = state.Tasks[0];

        TaskInteractionService.Update(
            state,
            task,
            new TaskEditValues(
                "  Updated title  ",
                "  Updated description  ",
                InWork: true,
                Completed: false));

        Assert(task.Title == "Updated title", "Edited title should be trimmed and stored.");
        Assert(
            task.Description == "Updated description",
            "Edited description should be trimmed and stored.");
        Assert(task.InWork, "Editor should update in-work state.");
        Assert(!task.Completed, "Editor should preserve active state.");
    }

    private static void TaskDelete()
    {
        var state = AppState.CreateDefault();
        var task = state.Tasks[1];

        var deleted = TaskInteractionService.Delete(state, task);

        Assert(deleted, "Existing task should be deleted.");
        Assert(state.Tasks.All(item => item.Id != task.Id), "Deleted task should leave state.");
    }

    private static void TaskCompletion()
    {
        var task = TaskItem.Create("Complete me");
        task.InWork = true;
        var completedAt = DateTimeOffset.Parse("2026-06-12T08:30:00Z");

        var completed = TaskInteractionService.Complete(task, completedAt);

        Assert(completed, "Active task should complete.");
        Assert(task.Completed, "Completed flag should be set.");
        Assert(!task.InWork, "Completed task should leave in-work state.");
        Assert(task.CompletedAtUtc == completedAt, "Completion timestamp should be stored.");
    }

    private static void InWorkSettingSerialization()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = store.Load();
            state.OverlaySettings.InWorkMode = InWorkMode.SingleTask;
            state.Tasks[0].DescriptionExpanded = true;
            state.WindowPlacement.CollapsedLeft = -42.5;
            state.WindowPlacement.CollapsedTop = 18.25;

            store.Save(state);
            var loaded = new AppStateStore(directory).Load();

            Assert(
                loaded.OverlaySettings.InWorkMode == InWorkMode.SingleTask,
                "In-work mode should survive serialization.");
            Assert(
                loaded.Tasks[0].DescriptionExpanded,
                "Description expansion should survive serialization.");
            Assert(
                loaded.WindowPlacement.CollapsedLeft == -42.5,
                "Collapsed left anchor should survive serialization.");
            Assert(
                loaded.WindowPlacement.CollapsedTop == 18.25,
                "Collapsed top anchor should survive serialization.");
        });
    }

    private static void OldStateInWorkDefault()
    {
        WithTemporaryDirectory(directory =>
        {
            const string oldStateJson =
                """
                {
                  "schemaVersion": 1,
                  "tasks": [{
                    "id": "e9718783-bc52-4c19-b39e-7a595d379ba8",
                    "title": "Old task",
                    "description": "",
                    "completed": false,
                    "priority": "normal",
                    "inWork": false,
                    "createdAtUtc": "2026-06-11T08:30:00+00:00",
                    "completedAtUtc": null,
                    "dueAtUtc": null
                  }],
                  "overlaySettings": {
                    "activeToPassiveDelayMilliseconds": 500,
                    "alwaysOnTop": true,
                    "collapsedMode": false
                  },
                  "windowPlacement": {
                    "left": 100,
                    "top": 100
                  },
                  "createdAtUtc": "2026-06-11T08:30:00+00:00",
                  "updatedAtUtc": "2026-06-11T08:30:00+00:00"
                }
                """;

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "state.json"), oldStateJson);

            var loaded = new AppStateStore(directory).Load();

            Assert(
                loaded.OverlaySettings.InWorkMode == InWorkMode.MultipleTasks,
                "Old state should default to MultipleTasks mode.");
            Assert(
                !loaded.Tasks[0].DescriptionExpanded,
                "Old tasks should default descriptions to collapsed.");
            Assert(
                loaded.WindowPlacement.CollapsedLeft is null,
                "Old placement should leave collapsed anchor unset.");
        });
    }

    private static void WindowPlacementEdgeSnap()
    {
        var workArea = new OverlayBounds(100, 50, 1200, 800);
        var nearRightBottom = new OverlayBounds(887, 637, 400, 200);

        var snapped = WindowPlacementGeometry.SnapToWorkArea(
            nearRightBottom,
            workArea,
            threshold: 16);

        Assert(snapped.Right == workArea.Right, "Window should snap to the right edge.");
        Assert(snapped.Bottom == workArea.Bottom, "Window should snap to the bottom edge.");
    }

    private static void WindowPlacementOffScreenCorrection()
    {
        var workArea = new OverlayBounds(1920, 0, 1280, 720);
        var offScreen = new OverlayBounds(5000, 2000, 1600, 900);

        var corrected = WindowPlacementGeometry.ClampToWorkArea(offScreen, workArea);

        Assert(corrected.Left == workArea.Left, "Oversized window should anchor to the work-area left.");
        Assert(corrected.Top == workArea.Top, "Oversized window should anchor to the work-area top.");
        Assert(corrected.Width == workArea.Width, "Oversized width should be constrained.");
        Assert(corrected.Height == workArea.Height, "Oversized height should be constrained.");
        Assert(
            WindowPlacementGeometry.Intersects(corrected, workArea),
            "Corrected window should intersect the current monitor work area.");
    }

    private static void CollapsedPanelOpensInward()
    {
        var workArea = new OverlayBounds(0, 0, 1920, 1080);
        var handle = new OverlayBounds(1872, 160, 48, 20);

        var panel = PanelLayoutService.PlacePanel(
            handle,
            panelWidth: 450,
            panelHeight: 600,
            workArea);

        Assert(panel.Right == workArea.Right, "Right-edge panel should open inward.");
        Assert(panel.Left < handle.Left, "Panel should extend left of the handle.");
        Assert(panel.Top == handle.Bottom, "Panel should open below the handle when it fits.");
        Assert(handle.Left == 1872, "Panel placement must not mutate the handle anchor.");
    }

    private static void CorruptedStateBackup()
    {
        WithTemporaryDirectory(directory =>
        {
            var diagnostics = new System.Collections.Generic.List<string>();
            var store = new AppStateStore(
                directory,
                (message, exception) => diagnostics.Add(message));
            store.Load();
            File.WriteAllText(store.StatePath, "{ definitely not valid json");

            var recovered = store.Load();
            var corruptBackups = Directory.GetFiles(directory, "state.corrupt.*.json");

            Assert(recovered.Tasks.Count == 3, "Corrupted state should load seed tasks.");
            Assert(corruptBackups.Length == 1, "Corrupted state should be preserved once.");
            Assert(
                File.ReadAllText(corruptBackups[0]).Contains("definitely not valid json"),
                "Corrupted backup should contain the original data.");
            Assert(
                diagnostics.Any(message => message.Contains("State load failed")),
                "Corrupted state recovery should be logged.");

            var reloaded = store.Load();
            Assert(reloaded.Tasks.Count == 3, "Recovered state.json should be valid.");
        });
    }

    private static void CrashLogContents()
    {
        WithTemporaryDirectory(directory =>
        {
            var diagnostics = new AppDiagnostics(directory);
            var exception = new InvalidOperationException(
                "outer failure",
                new IOException("inner failure"));

            var path = diagnostics.LogCrash(
                "test source",
                exception,
                "OverlayMode=active");

            Assert(path is not null && File.Exists(path), "Crash log should be created.");

            var contents = File.ReadAllText(path!);
            Assert(contents.Contains("test source"), "Crash source should be logged.");
            Assert(contents.Contains("InvalidOperationException"), "Exception type should be logged.");
            Assert(contents.Contains("outer failure"), "Exception message should be logged.");
            Assert(contents.Contains("IOException"), "Inner exception type should be logged.");
            Assert(contents.Contains("inner failure"), "Inner exception message should be logged.");
            Assert(contents.Contains("StatePath:"), "State path should be logged.");
            Assert(contents.Contains("OverlayMode=active"), "Runtime context should be logged.");
        });
    }

    private static void DiagnosticCallbackIsolation()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(
                directory,
                (_, _) => throw new InvalidOperationException("logger failed"));

            var state = store.Load();
            store.Save(state);

            Assert(File.Exists(store.StatePath), "Logger failures must not break storage.");
        });
    }

    private static void ClipboardLinesCreateMultipleTasks()
    {
        var now = DateTimeOffset.Parse("2026-06-11T08:30:00Z");
        var tasks = ClipboardTaskFactory.CreateFromLines(
            "  First task  \r\n\r\n Second task\n\t\nThird task ",
            now);

        Assert(tasks.Count == 3, "Each non-empty line should create one task.");
        Assert(tasks[0].Title == "First task", "First title should be trimmed.");
        Assert(tasks[1].Title == "Second task", "Second title should be trimmed.");
        Assert(tasks[2].Title == "Third task", "Third title should be trimmed.");
        Assert(
            tasks.Select(task => task.Id).Distinct().Count() == 3,
            "Created tasks should have unique stable IDs.");
        Assert(
            tasks.All(task => task.Description == string.Empty),
            "Line-created tasks should have empty descriptions.");
        foreach (var task in tasks)
        {
            AssertClipboardTaskDefaults(task, now);
        }
    }

    private static void SingleClipboardTaskCollapsesLines()
    {
        var task = ClipboardTaskFactory.CreateSingle(
            "  Prepare release \r\n\r\n notes for QA  ");

        Assert(task is not null, "Non-empty clipboard should create one task.");
        Assert(
            task!.Title == "Prepare release notes for QA",
            "Single-task mode should collapse non-empty lines into one title.");
        Assert(task.Description == string.Empty, "Single-task description should be empty.");
        AssertClipboardTaskDefaults(task, task.CreatedAtUtc);
    }

    private static void ClipboardTaskWithDescription()
    {
        const string clipboardText =
            "\r\n  Prepare release notes  \r\n\r\nFirst paragraph\r\n\r\nSecond paragraph\r\n  ";

        var parsed = ClipboardTaskFactory.ParseWithDescription(clipboardText);
        var task = ClipboardTaskFactory.CreateWithDescription(clipboardText);

        Assert(parsed is not null, "Multi-line text should parse.");
        Assert(parsed!.Value.Title == "Prepare release notes", "First non-empty line should be the title.");
        Assert(
            parsed.Value.Description == "First paragraph\n\nSecond paragraph",
            "Description should preserve internal line breaks.");
        Assert(task?.Description == parsed.Value.Description, "Task should use the parsed description.");
        AssertClipboardTaskDefaults(task!, task!.CreatedAtUtc);
    }

    private static void EmptyClipboardText()
    {
        Assert(
            ClipboardTaskFactory.CreateFromLines(null).Count == 0,
            "Null clipboard should create no line tasks.");
        Assert(
            ClipboardTaskFactory.CreateFromLines(" \r\n\t").Count == 0,
            "Whitespace lines should be ignored.");
        Assert(
            ClipboardTaskFactory.CreateSingle(string.Empty) is null,
            "Empty clipboard should create no single task.");
        Assert(
            ClipboardTaskFactory.CreateWithDescription(" \r\n\t\r\n ") is null,
            "Whitespace-only clipboard should create no described task.");
    }

    private static AppState CreateEmptyTreeState()
    {
        var state = AppState.CreateDefault(
            DateTimeOffset.Parse("2026-06-21T07:00:00Z"));
        state.Tasks.Clear();
        return state;
    }

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "TaskOverlayV2Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            test(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertClipboardTaskDefaults(
        TaskItem task,
        DateTimeOffset expectedCreatedAtUtc)
    {
        Assert(task.Id != Guid.Empty, "Created task should have a stable ID.");
        Assert(!task.Completed, "Created task should be active.");
        Assert(task.Priority == TaskPriority.Normal, "Created task priority should be normal.");
        Assert(!task.InWork, "Created task should not be in work.");
        Assert(task.Status == TaskStatus.Todo, "Created task status should be Todo.");
        Assert(
            task.CreatedAtUtc == expectedCreatedAtUtc,
            "Created task should have the expected UTC timestamp.");
        Assert(task.CompletedAtUtc is null, "Completed timestamp should be empty.");
        Assert(task.DueAtUtc is null, "Due time should be empty.");
        Assert(task.RemindAtUtc is null, "Reminder time should be empty.");
        Assert(task.RemindEveryMinutes is null, "Reminder repeat should be empty.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
