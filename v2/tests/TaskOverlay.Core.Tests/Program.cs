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
            ("tree manager state persistence", TreeManagerStatePersistence),
            ("tree manager state repair", TreeManagerStateRepair),
            ("tree manager panel pin isolation", TreeManagerPanelPinIsolation),
            ("tree manager status filters", TreeManagerStatusFilters),
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
            ("done resolves reminder schedule", DoneResolvesReminderSchedule),
            ("quick task capture", QuickTaskCapture),
            ("quick task reminder presets", QuickTaskReminderPresets),
            ("save/load roundtrip", SaveLoadRoundtrip),
            ("overlay mode serialization", OverlayModeSerialization),
            ("working presentation settings", WorkingPresentationSettings),
            ("utility shell geometry persistence", UtilityShellGeometryPersistence),
            ("old utility shell geometry compatibility", OldUtilityShellGeometryCompatibility),
            ("invalid utility shell geometry repair", InvalidUtilityShellGeometryRepair),
            ("single WPF instance guard", SingleWpfInstanceGuard),
            ("working panel bounds", WorkingPanelBoundsBehavior),
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
            ("utility window toggle policy", UtilityWindowTogglePolicyBehavior),
            ("user-facing overlay mode labels", UserFacingOverlayModeLabels),
            ("working mode task filtering", WorkingModeTaskFiltering),
            ("overlay panel task filtering", OverlayPanelTaskFiltering),
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
            ("backup filename generation", BackupFilenameGeneration),
            ("backup machine name sanitization", BackupMachineNameSanitization),
            ("backup retention selection", BackupRetentionSelection),
            ("backup disabled and missing state", BackupDisabledAndMissingState),
            ("backup copy and scoped retention", BackupCopyAndScopedRetention),
            ("local backup settings persistence", LocalBackupSettingsPersistence),
            ("backup metadata and latest discovery", BackupMetadataAndLatestDiscovery),
            ("backup discovery fallback and freshness", BackupDiscoveryFallbackAndFreshness),
            ("backup restore safety pair", BackupRestoreSafetyPair),
            ("backup restore rejects invalid state", BackupRestoreRejectsInvalidState),
            ("clipboard lines create multiple tasks", ClipboardLinesCreateMultipleTasks),
            ("single clipboard task collapses lines", SingleClipboardTaskCollapsesLines),
            ("clipboard task with description", ClipboardTaskWithDescription),
            ("empty clipboard text", EmptyClipboardText),
            ("workspace snapshot contract", WorkspaceSnapshotContract),
            ("workspace timeline consistency", WorkspaceTimelineConsistency),
            ("workspace orphan fallback", WorkspaceOrphanFallback),
            ("workspace state persistence and repair", WorkspaceStatePersistenceAndRepair),
            ("workspace context command persistence", WorkspaceContextCommandPersistence),
            ("workspace command status persistence", WorkspaceCommandStatusPersistence),
            ("workspace command invalid task and status", WorkspaceCommandInvalidTaskAndStatus),
            ("workspace command pin persistence", WorkspaceCommandPinPersistence),
            ("workspace command notes persistence", WorkspaceCommandNotesPersistence),
            ("workspace command title persistence", WorkspaceCommandTitlePersistence),
            ("workspace command contract rejection", WorkspaceCommandContractRejection),
            ("workspace command planned work persistence", WorkspaceCommandPlannedWorkPersistence),
            ("workspace command planned work validation", WorkspaceCommandPlannedWorkValidation),
            ("workspace planned work snapshot and migration", WorkspacePlannedWorkSnapshotAndMigration),
            ("workspace command create task persistence", WorkspaceCommandCreateTaskPersistence),
            ("workspace command create task in section", WorkspaceCommandCreateTaskInSection),
            ("workspace command create task validation", WorkspaceCommandCreateTaskValidation),
            ("workspace command waiting for persistence", WorkspaceCommandWaitingForPersistence),
            ("workspace command reminder persistence", WorkspaceCommandReminderPersistence),
            ("workspace command reminder validation", WorkspaceCommandReminderValidation),
            ("workspace command deadline persistence", WorkspaceCommandDeadlinePersistence),
            ("workspace legacy state without meetings", WorkspaceLegacyStateWithoutMeetings),
            ("workspace meeting command persistence", WorkspaceMeetingCommandPersistence),
            ("workspace meeting validation and repair", WorkspaceMeetingValidationAndRepair),
            ("workspace meeting snapshot and linked task cleanup", WorkspaceMeetingSnapshotAndLinkedTaskCleanup),
            ("workspace active now collapsed persistence", WorkspaceActiveNowCollapsedPersistence),
            ("workspace command create section persistence", WorkspaceCommandCreateSectionPersistence),
            ("workspace command create section validation", WorkspaceCommandCreateSectionValidation),
            ("workspace command rename section persistence", WorkspaceCommandRenameSectionPersistence),
            ("workspace command delete section reparents tasks", WorkspaceCommandDeleteSectionReparentsTasks),
            ("workspace command create task in created section", WorkspaceCommandCreateTaskInCreatedSection),
            ("workspace snapshot includes created section", WorkspaceSnapshotIncludesCreatedSection),
            ("workspace command create subtask persistence", WorkspaceCommandCreateSubtaskPersistence),
            ("workspace command create subtask validation", WorkspaceCommandCreateSubtaskValidation),
            ("workspace command create draft task persistence", WorkspaceCommandCreateDraftTaskPersistence),
            ("workspace command delete task persistence", WorkspaceCommandDeleteTaskPersistence),
            ("workspace command delete task reparents subtasks", WorkspaceCommandDeleteTaskReparentsSubtasks),
            ("workspace command move task to section", WorkspaceCommandMoveTaskToSectionPersistence),
            ("workspace command move task to project root", WorkspaceCommandMoveTaskToProjectRootPersistence),
            ("workspace command move task invalid target", WorkspaceCommandMoveTaskInvalidTarget),
            ("workspace command done clears reminder and deadline", WorkspaceCommandDoneClearsReminderAndDeadline),
            ("workspace command checkpoint persistence", WorkspaceCommandCheckpointPersistence),
            ("workspace command checkpoint reorder persistence", WorkspaceCommandCheckpointReorderPersistence),
            ("workspace command checkpoint validation", WorkspaceCommandCheckpointValidation),
            ("workspace checkpoint snapshot and safe load", WorkspaceCheckpointSnapshotAndSafeLoad),
            ("workspace checkpoint independent of parent done", WorkspaceCheckpointIndependentOfParentDone)
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
        Assert(service.GetNode(task.Id)?.Status == TreeNodeStatus.Focus, "Active task should use FOCUS status.");
        Assert(service.MarkStatus(task.Id, TreeNodeStatus.Wait, timestamp), "Task should become WAIT.");
        Assert(state.Tasks.Single().Status == TaskStatus.Waiting, "WAIT should use the existing Waiting status.");
        Assert(!state.Tasks.Single().InWork, "WAIT should leave FOCUS state.");
        Assert(service.SetWaitingFor(task.Id, "  Vendor reply  ", timestamp), "WAIT context should update.");
        Assert(state.Tasks.Single().WaitingFor == "Vendor reply", "WAIT context should be trimmed and retained.");
        Assert(
            service.SetDescription(task.Id, "  Delivery details  ", timestamp),
            "Task description should update through the tree service.");
        Assert(
            state.Tasks.Single().Description == "Delivery details",
            "Task description should be trimmed and retained.");
        Assert(service.GetNode(task.Id)?.Status == TreeNodeStatus.Wait, "Normalized task should report WAIT.");
        Assert(service.MarkStatus(task.Id, TreeNodeStatus.Focus, timestamp), "Task should return to FOCUS.");
        Assert(state.Tasks.Single().Status == TaskStatus.InWork, "FOCUS should use the existing InWork status.");
        Assert(service.MarkStatus(task.Id, TreeNodeStatus.Done, timestamp), "Task should become done.");
        Assert(state.Tasks.Single().Completed, "Done should retain flat Completed compatibility.");
        Assert(!state.Tasks.Single().InWork, "Done task should leave active state.");
        Assert(service.GetNode(task.Id)?.Status == TreeNodeStatus.Done, "Normalized status should be Done.");
        Assert(!service.MarkActive(task.Id, true), "Completed task cannot become active.");
        Assert(service.MarkStatus(task.Id, TreeNodeStatus.Todo), "Task should return to todo.");
        Assert(!state.Tasks.Single().Completed, "Todo should clear flat Completed state.");
        Assert(!service.MarkStatus(group.Id, TreeNodeStatus.Done), "Group status change should be rejected.");
        Assert(!service.SetDescription(group.Id, "Unsupported"), "Group notes should remain unsupported.");
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
        var waitingTask = service.CreateTask(secondGroup.Id, "Waiting task")!;
        var doneTask = service.CreateTask(secondGroup.Id, "Done task")!;
        service.MarkActive(firstTask.Id, true);
        service.MarkActive(secondTask.Id, true);
        service.MarkStatus(waitingTask.Id, TreeNodeStatus.Wait);
        service.MarkActive(doneTask.Id, true);
        service.MarkStatus(doneTask.Id, TreeNodeStatus.Done);
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
                    "Second task",
                    "Waiting task",
                    "Done task"
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
            service.GetProjection(project.Id, TreeProjection.ActiveOnly)
                .All(node => node.Status == TreeNodeStatus.Focus),
            "ActiveOnly should contain FOCUS tasks only, excluding WAIT and DONE.");
        Assert(
            service.GetProjection(project.Id, TreeProjection.ActivePlusAncestors)
                .All(node => node.Id != doneTask.Id),
            "DONE tasks must not appear in the active projection.");
        Assert(
            service.GetProjection(project.Id, TreeProjection.CurrentBranchOnly, firstGroup.Id)
                .Select(node => node.Id)
                .SequenceEqual(new[] { project.Id, firstGroup.Id, firstTask.Id, child.Id }),
            "CurrentBranchOnly should include selected subtree and ancestors.");
        Assert(state.Tasks.Count == taskCount, "Projection must not mutate state.");
    }

    private static void TreeManagerStatePersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = CreateEmptyTreeState();
            var service = new TreeStateService(state);
            var project = state.Projects.Single();
            var section = service.CreateGroup(project.Id, "Section")!;
            var task = service.CreateTask(section.Id, "Task")!;
            var subtask = service.CreateTask(task.Id, "Subtask")!;
            service.MarkStatus(task.Id, TreeNodeStatus.Wait);
            service.SetWaitingFor(task.Id, "Client response");
            service.SetPinToPanel(task.Id, true);
            state.TreeManagerSettings.SelectedProjectId = project.Id;
            state.TreeManagerSettings.SelectedNodeId = subtask.Id;
            state.TreeManagerSettings.ExpandedNodeIds = new System.Collections.Generic.List<Guid>
            {
                project.Id,
                section.Id,
                task.Id
            };
            state.TreeManagerSettings.Filter = TreeManagerFilter.ActivePlusAncestors;
            state.TreeManagerSettings.ActiveView = TreeManagerView.Status;
            state.TreeManagerSettings.StatusFilter = TreeManagerStatusFilter.Panel;
            state.TreeManagerSettings.ExpansionInitialized = true;

            var store = new AppStateStore(directory);
            store.Save(state);
            var loaded = store.Load();
            var loadedTask = loaded.Tasks.Single(item => item.Id == task.Id);

            Assert(loaded.TreeManagerSettings.SelectedProjectId == project.Id, "Selected project should restore.");
            Assert(loaded.TreeManagerSettings.SelectedNodeId == subtask.Id, "Selected node should restore.");
            Assert(
                loaded.TreeManagerSettings.ExpandedNodeIds.SequenceEqual(
                    new[] { project.Id, section.Id, task.Id }),
                "Expanded node IDs should restore in order.");
            Assert(
                loaded.TreeManagerSettings.Filter == TreeManagerFilter.ActivePlusAncestors,
                "Tree filter should restore.");
            Assert(loaded.TreeManagerSettings.ActiveView == TreeManagerView.Status, "Active view should restore.");
            Assert(
                loaded.TreeManagerSettings.StatusFilter == TreeManagerStatusFilter.Panel,
                "Status filter should restore.");
            Assert(loaded.TreeManagerSettings.ExpansionInitialized, "Expansion initialization should persist.");
            Assert(loadedTask.Status == TaskStatus.Waiting, "WAIT status should survive save/load.");
            Assert(loadedTask.WaitingFor == "Client response", "waitingFor should survive save/load.");
            Assert(loadedTask.PinToPanel, "PinToPanel should survive save/load.");
        });
    }

    private static void TreeManagerStateRepair()
    {
        var state = CreateEmptyTreeState();
        var project = state.Projects.Single();
        state.TreeManagerSettings.SelectedProjectId = Guid.NewGuid();
        state.TreeManagerSettings.SelectedNodeId = Guid.NewGuid();
        state.TreeManagerSettings.ExpandedNodeIds = new System.Collections.Generic.List<Guid>
        {
            Guid.NewGuid(),
            project.Id,
            project.Id
        };
        state.TreeManagerSettings.Filter = (TreeManagerFilter)999;
        state.TreeManagerSettings.ActiveView = (TreeManagerView)999;
        state.TreeManagerSettings.StatusFilter = (TreeManagerStatusFilter)999;

        Assert(TreeManagerStatePolicy.Normalize(state), "Invalid Tree Manager state should be repaired.");
        Assert(state.TreeManagerSettings.SelectedProjectId == project.Id, "Missing project should fall back safely.");
        Assert(state.TreeManagerSettings.SelectedNodeId == project.Id, "Missing node should fall back to project.");
        Assert(
            state.TreeManagerSettings.ExpandedNodeIds.SequenceEqual(new[] { project.Id }),
            "Unknown and duplicate expanded IDs should be removed.");
        Assert(state.TreeManagerSettings.Filter == TreeManagerFilter.All, "Invalid filter should fall back to All.");
        Assert(state.TreeManagerSettings.ActiveView == TreeManagerView.Tree, "Invalid view should fall back to Tree.");
        Assert(
            state.TreeManagerSettings.StatusFilter == TreeManagerStatusFilter.All,
            "Invalid status filter should fall back to All.");
        Assert(!TreeManagerStatePolicy.Normalize(state), "Normalized Tree Manager state should be idempotent.");
    }

    private static void TreeManagerPanelPinIsolation()
    {
        var state = CreateEmptyTreeState();
        var service = new TreeStateService(state);
        var project = state.Projects.Single();
        var todo = service.CreateTask(project.Id, "Pinned TODO")!;
        var focus = service.CreateTask(project.Id, "FOCUS")!;
        service.MarkStatus(focus.Id, TreeNodeStatus.Focus);

        Assert(service.SetPinToPanel(todo.Id, true), "Task should pin to panel.");
        Assert(state.Tasks.Single(task => task.Id == todo.Id).PinToPanel, "Pin state should update immediately.");
        Assert(!service.SetPinToPanel(project.Id, true), "Only tasks can pin to panel.");

        var working = OverlayTaskFilter.SelectForMode(
                state.Tasks,
                OverlayMode.Working,
                DateTimeOffset.Parse("2026-06-29T10:00:00Z"))
            .Select(task => task.Id)
            .ToList();
        Assert(!working.Contains(todo.Id), "Pinned TODO must not enter Working.");
        Assert(working.Contains(focus.Id), "FOCUS should continue to enter Working.");

        Assert(service.SetPinToPanel(todo.Id, false), "Task should unpin from panel.");
        Assert(!state.Tasks.Single(task => task.Id == todo.Id).PinToPanel, "Unpin should persist in state.");
    }

    private static void TreeManagerStatusFilters()
    {
        var now = DateTimeOffset.Parse("2026-06-29T10:00:00Z");
        var todo = TaskItem.Create("Todo", now);
        todo.PinToPanel = true;
        var focus = TaskItem.Create("Focus", now);
        focus.Status = TaskStatus.InWork;
        focus.InWork = true;
        var wait = TaskItem.Create("Wait", now);
        wait.Status = TaskStatus.Waiting;
        wait.WaitingFor = "Reply";
        var remind = TaskItem.Create("Remind", now);
        ReminderService.SetSchedule(remind, now.AddMinutes(-1), null, now.AddMinutes(-5));
        ReminderService.ProcessDueReminders(new[] { remind }, now);
        var done = TaskItem.Create("Done", now);
        TaskInteractionService.Complete(done, now);
        var tasks = new[] { todo, focus, wait, remind, done };

        Assert(
            TreeManagerTaskFilter.Select(tasks, TreeManagerStatusFilter.All, now).Count() == 5,
            "All filter should retain every task.");
        Assert(
            TreeManagerTaskFilter.Select(tasks, TreeManagerStatusFilter.Panel, now).Single().Id == todo.Id,
            "Panel filter should use PinToPanel only.");
        Assert(
            TreeManagerTaskFilter.Select(tasks, TreeManagerStatusFilter.Focus, now).Single().Id == focus.Id,
            "FOCUS filter should use InWork status.");
        Assert(
            TreeManagerTaskFilter.Select(tasks, TreeManagerStatusFilter.Wait, now).Single().Id == wait.Id,
            "WAIT filter should use Waiting status.");
        Assert(
            TreeManagerTaskFilter.Select(tasks, TreeManagerStatusFilter.Remind, now).Single().Id == remind.Id,
            "REMIND filter should use active reminder attention.");
        Assert(
            TreeManagerTaskFilter.Select(tasks, TreeManagerStatusFilter.Todo, now).Select(task => task.Id)
                .SequenceEqual(new[] { todo.Id, remind.Id }),
            "TODO filter should follow stored task status independently of REMIND attention.");
        Assert(
            TreeManagerTaskFilter.Select(tasks, TreeManagerStatusFilter.Done, now).Single().Id == done.Id,
            "DONE filter should include completed tasks.");
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
            Assert(taskNode?.Status == TreeNodeStatus.Focus, "Legacy InWork should map to FOCUS status.");
            Assert(loaded.Tasks.Single().Description == "preserved", "Legacy task data should remain intact.");
            Assert(loaded.WindowPlacement.CollapsedLeft == 1800, "Handle anchor should remain intact.");
            Assert(loaded.TreeManagerSettings.SelectedProjectId == projectId, "Legacy state should get a safe tree selection.");
            Assert(loaded.TreeManagerSettings.Filter == TreeManagerFilter.All, "Legacy state should default to All filter.");
            Assert(!loaded.Tasks.Single().PinToPanel, "Legacy tasks should default to not pinned to panel.");

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
        Assert(task.Status == TaskStatus.Waiting, "Snooze must not change task status.");

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
                loaded.Status == TaskStatus.Todo,
                "Notification snooze must not change task status.");
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
            cleared.Status == TaskStatus.Todo && cleared.Title == "Clear me",
            "Removing a reminder must not complete or delete the task.");
        Assert(
            cleared.RemindAtUtc is null &&
            cleared.RemindEveryMinutes is null &&
            !cleared.ReminderActive,
            "Removing a reminder should clear schedule and attention metadata.");
    }

    private static void DoneResolvesReminderSchedule()
    {
        var start = DateTimeOffset.Parse("2026-07-03T08:00:00Z");
        var repeating = TaskItem.Create("Complete repeating reminder", start);
        ReminderService.ApplyPreset(
            repeating,
            ReminderPreset.RepeatEvery2Hours,
            start,
            TimeZoneInfo.Utc);
        ReminderService.ProcessDueReminders(new[] { repeating }, start.AddHours(2));

        Assert(repeating.ReminderActive, "Repeating reminder should be active before DONE.");
        Assert(
            TaskInteractionService.Complete(repeating, start.AddHours(2).AddMinutes(1)),
            "DONE should complete an active reminder task.");
        Assert(
            repeating.Status == TaskStatus.Done && repeating.Completed,
            "DONE should persist completed task state.");
        Assert(
            repeating.RemindAtUtc is null &&
            repeating.RemindEveryMinutes is null &&
            repeating.LastReminderAtUtc is null &&
            !repeating.ReminderActive &&
            repeating.ReminderAcknowledgedAtUtc is null &&
            repeating.ReminderSnoozedUntilUtc is null,
            "DONE should resolve all reminder schedule and attention metadata.");
        Assert(
            ReminderService.ProcessDueReminders(
                new[] { repeating },
                start.AddDays(2)).Count == 0,
            "DONE should prevent future repeating reminder activations.");

        var legacyDone = TaskItem.Create("Legacy DONE reminder", start);
        legacyDone.Status = TaskStatus.Done;
        legacyDone.Completed = true;
        legacyDone.RemindAtUtc = start.AddMinutes(-1);
        legacyDone.RemindEveryMinutes = 120;
        legacyDone.ReminderActive = true;
        legacyDone.LastReminderAtUtc = start.AddMinutes(-1);

        Assert(
            ReminderService.ProcessDueReminders(new[] { legacyDone }, start).Count == 0 &&
            !ReminderAttentionService.ShouldShowNotification(legacyDone, start),
            "DONE tasks with old metadata must be ignored by reminder scanning.");
        Assert(
            TaskInteractionService.Complete(legacyDone, start),
            "Completing legacy DONE state should report reminder cleanup.");
        Assert(
            legacyDone.RemindAtUtc is null &&
            legacyDone.RemindEveryMinutes is null &&
            !legacyDone.ReminderActive,
            "Completing legacy DONE state should clear stale reminder metadata.");
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

    private static void QuickTaskReminderPresets()
    {
        var state = AppState.CreateDefault();
        var project = state.Projects.Single();
        var now = DateTimeOffset.Parse("2026-07-03T10:15:00Z");

        var cases = new[]
        {
            (ReminderPreset.In30Minutes, now.AddMinutes(30)),
            (ReminderPreset.In1Hour, now.AddHours(1)),
            (ReminderPreset.In2Hours, now.AddHours(2)),
            (ReminderPreset.TomorrowMorning, DateTimeOffset.Parse("2026-07-04T10:00:00Z"))
        };
        foreach (var (preset, expected) in cases)
        {
            var task = TaskCaptureService.CreateQuickTask(
                state,
                new QuickTaskValues(
                    $"Quick {preset}",
                    project.Id,
                    TaskStatus.Todo,
                    preset,
                    string.Empty,
                    string.Empty),
                now,
                TimeZoneInfo.Utc);

            Assert(task is not null, $"Quick Add should create a task for {preset}.");
            Assert(
                task!.RemindAtUtc == expected && task.RemindEveryMinutes is null,
                $"Quick Add should store the expected one-shot schedule for {preset}.");
        }

        var noReminder = TaskCaptureService.CreateQuickTask(
            state,
            new QuickTaskValues(
                "Quick no reminder",
                project.Id,
                TaskStatus.Todo,
                ReminderPreset.None,
                string.Empty,
                string.Empty),
            now,
            TimeZoneInfo.Utc);
        Assert(noReminder is not null, "Quick Add should create a task without reminder.");
        Assert(
            noReminder!.RemindAtUtc is null &&
            noReminder.RemindEveryMinutes is null &&
            !noReminder.ReminderActive,
            "No reminder should create no reminder metadata.");

        var customAt = now.AddDays(3).AddMinutes(17);
        var custom = TaskCaptureService.CreateQuickTask(
            state,
            new QuickTaskValues(
                "Quick custom reminder",
                project.Id,
                TaskStatus.Waiting,
                ReminderPreset.KeepCurrent,
                "Review response",
                string.Empty,
                customAt,
                1440,
                true),
            now,
            TimeZoneInfo.Utc);
        Assert(custom is not null, "Quick Add should create a custom scheduled task.");
        Assert(
            custom!.RemindAtUtc == customAt && custom.RemindEveryMinutes == 1440,
            "Quick Add should store an explicit custom schedule and repeat interval.");

        var weekly = TaskCaptureService.CreateQuickTask(
            state,
            new QuickTaskValues(
                "Quick weekly reminder",
                project.Id,
                TaskStatus.Todo,
                ReminderPreset.KeepCurrent,
                string.Empty,
                string.Empty,
                customAt,
                7 * 24 * 60,
                true),
            now,
            TimeZoneInfo.Utc);
        Assert(
            weekly is not null && weekly.RemindEveryMinutes == 10080,
            "Quick Add should store the weekly repeat interval.");

        var done = TaskCaptureService.CreateQuickTask(
            state,
            new QuickTaskValues(
                "Quick DONE",
                project.Id,
                TaskStatus.Done,
                ReminderPreset.In1Hour,
                string.Empty,
                string.Empty),
            now,
            TimeZoneInfo.Utc);
        Assert(done is not null && done.Status == TaskStatus.Done, "Quick Add should create DONE state.");
        Assert(
            done!.RemindAtUtc is null && done.RemindEveryMinutes is null,
            "DONE tasks must not retain a Quick Add reminder schedule.");
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
            TaskInteractionService.Complete(task);
            // Completing clears the due date, so set it afterward purely to
            // exercise DueAtUtc serialization roundtrip on a done task.
            task.DueAtUtc = DateTimeOffset.UtcNow.AddHours(2);
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
            state.OverlaySettings.PanelFilter = OverlayPanelFilter.Wait;
            state.OverlaySettings.WaitGroupExpanded = true;

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
                loaded.OverlaySettings.PanelFilter == OverlayPanelFilter.Wait &&
                loaded.OverlaySettings.WaitGroupExpanded == true,
                "Overlay panel filter state should survive a save/load roundtrip.");
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
            collapsedEntry is
            {
                IsActive: false,
                IsWorking: false,
                VisualBranch: OverlayVisualBranch.Collapsed,
                ShowActiveChrome: false,
                AllowFocusBadge: false
            },
            "Inactive CollapsedHandle should use an empty structural branch.");
        Assert(
            OverlaySurfacePolicy.KeepHostVisibleWhenPanelHidden(collapsedEntry),
            "Inactive CollapsedHandle should keep its empty host rendered.");

        var collapsedHover = OverlayActiveStatePolicy.Resolve(
            OverlayMode.CollapsedHandle,
            activeRequested: true);
        Assert(
            collapsedHover is
            {
                IsActive: true,
                VisualBranch: OverlayVisualBranch.Expanded,
                ShowActiveChrome: true,
                AllowFocusBadge: true
            },
            "Hover-expanded CollapsedHandle should retain the expanded presentation.");
        Assert(
            !OverlaySurfacePolicy.KeepHostVisibleWhenPanelHidden(collapsedHover),
            "Expanded CollapsedHandle should not use the empty-host policy.");
        Assert(
            OverlayActiveStatePolicy.ForModeEntry(
                OverlayModeCycle.Next(collapsedEntry.Mode)) == workingEntry &&
            OverlayActiveStatePolicy.ForModeEntry(
                OverlayModeCycle.Next(collapsedHover.Mode)) == workingEntry,
            "Inactive and hover-expanded CollapsedHandle should converge on one Working entry state.");

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

    private static void WorkingPanelBoundsBehavior()
    {
        var settings = new OverlaySettings();
        var workArea = new OverlayBounds(0, 0, 1920, 1080);
        var workingEntry = OverlayActiveStatePolicy.ForModeEntry(
            OverlayModeCycle.Next(OverlayMode.CollapsedHandle));
        var workingLayout = OverlayPanelBoundsPolicy.ResolveLayout(
            workingEntry,
            settings,
            workArea,
            workAreaMargin: 16);
        var pinnedLayout = OverlayPanelBoundsPolicy.ResolveLayout(
            OverlayActiveStatePolicy.ForModeEntry(OverlayMode.PinnedExpanded),
            settings,
            workArea,
            workAreaMargin: 16);

        Assert(
            workingLayout.PanelWidth == OverlaySettings.DefaultWorkingWindowWidth &&
            workingLayout.ContentWidth ==
            OverlaySettings.DefaultWorkingWindowWidth -
            OverlayPanelBoundsPolicy.HorizontalChrome,
            "CollapsedHandle -> Working should resolve compact bounds before reveal.");
        Assert(
            pinnedLayout.PanelWidth == 450 && pinnedLayout.ContentWidth == 420,
            "Pinned should retain its expanded panel bounds.");

        var handle = new OverlayBounds(1880, 100, 40, 20);
        var placed = OverlayPanelBoundsPolicy.PlaceWorkingPanel(
            handle,
            workingLayout.ContentWidth,
            contentHeight: 180,
            workingLayout.PanelMaxWidth,
            workArea);
        Assert(
            placed.Width == OverlaySettings.DefaultWorkingWindowWidth &&
            placed.Height == 180 + OverlayPanelBoundsPolicy.VerticalChrome,
            "Prepared Working bounds should include panel chrome exactly once.");
        Assert(
            placed.Right == handle.Right && placed.Top == handle.Bottom,
            "Right-edge Working placement should open inward from the handle.");
        Assert(
            handle == new OverlayBounds(1880, 100, 40, 20),
            "Preparing Working bounds must not move the handle anchor.");
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
                new[]
                {
                    "Ctrl+Alt+A",
                    "Ctrl+Alt+Q",
                    "Ctrl+Alt+T",
                    "Ctrl+Alt+1",
                    "Ctrl+Alt+S",
                    "Ctrl+Alt+D",
                    "Ctrl+Alt+W"
                }),
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
            bindings.Single(binding => binding.DisplayName == "Ctrl+Alt+S").Command ==
            GlobalHotkeyCommand.OpenSettings,
            "Ctrl+Alt+S should open Settings instead of clipboard capture.");
        Assert(
            bindings.Single(binding => binding.DisplayName == "Ctrl+Alt+D").Command ==
            GlobalHotkeyCommand.OpenTreeManager,
            "Ctrl+Alt+D should open Tree Manager instead of clipboard capture.");
        Assert(
            bindings.Single(binding => binding.DisplayName == "Ctrl+Alt+W").Command ==
            GlobalHotkeyCommand.ToggleWorkspace,
            "Ctrl+Alt+W should toggle Workspace.");
        Assert(
            displayNames.All(name =>
                name is not "Ctrl+Alt+M" and
                not "Ctrl+Alt+2" and
                not "Ctrl+Alt+3"),
            "Removed mode hotkeys must not be registered.");
    }

    private static void UtilityWindowTogglePolicyBehavior()
    {
        Assert(
            UtilityWindowTogglePolicy.Resolve(false, false) ==
            UtilityWindowToggleAction.ShowAndActivate,
            "A closed utility window should open.");
        Assert(
            UtilityWindowTogglePolicy.Resolve(true, false) ==
            UtilityWindowToggleAction.ShowAndActivate,
            "A background utility window should activate.");
        Assert(
            UtilityWindowTogglePolicy.Resolve(true, true) ==
            UtilityWindowToggleAction.Hide,
            "The focused target utility window should hide.");
        Assert(
            UtilityWindowTogglePolicy.Resolve(true, true, targetIsActive: false) ==
            UtilityWindowToggleAction.ShowAndActivate,
            "A utility shell hotkey should switch tabs before it can hide that target.");
    }

    private static void UtilityShellGeometryPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = store.Load();
            state.WindowPlacement.UtilityShellPlacement =
                UtilityShellGeometryPolicy.Capture(120, 80, 900, 850);

            store.Save(state);
            var loaded = store.Load();

            Assert(
                loaded.WindowPlacement.UtilityShellPlacement is
                {
                    Left: 120,
                    Top: 80,
                    Width: 900,
                    Height: 850
                },
                "The shared utility shell geometry should survive save/load.");
        });
    }

    private static void OldUtilityShellGeometryCompatibility()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            store.Save(AppState.CreateDefault());
            var root = JsonNode.Parse(File.ReadAllText(store.StatePath))!.AsObject();
            var placement = root["windowPlacement"]!.AsObject();
            placement.Remove("utilityShellPlacement");
            File.WriteAllText(
                store.StatePath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var loaded = store.Load();
            var geometry = UtilityShellGeometryPolicy.Resolve(
                loaded.WindowPlacement.UtilityShellPlacement,
                new OverlayBounds(0, 0, 1920, 1080));

            Assert(
                geometry == new ResolvedUtilityShellGeometry(620, 130, 680, 820),
                "Old state should center the shared utility shell at its safe default size.");
        });
    }

    private static void InvalidUtilityShellGeometryRepair()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = store.Load();
            state.WindowPlacement.UtilityShellPlacement =
                new UtilityShellPlacementState
            {
                Left = -5000,
                Top = 9000,
                Width = -200,
                Height = 5000
            };

            store.Save(state);
            var loaded = store.Load();
            var geometry = UtilityShellGeometryPolicy.Resolve(
                loaded.WindowPlacement.UtilityShellPlacement,
                new OverlayBounds(0, 0, 1920, 1080));

            Assert(
                geometry == new ResolvedUtilityShellGeometry(16, 16, 600, 1048),
                "Invalid shell size and position should clamp inside the work area.");
        });

        var constrained = UtilityShellGeometryPolicy.Resolve(
            saved: null,
            workArea: new OverlayBounds(0, 0, 640, 700));
        Assert(
            constrained == new ResolvedUtilityShellGeometry(16, 16, 608, 668),
            "Default shell geometry should fit a constrained work area.");
    }

    private static void SingleWpfInstanceGuard()
    {
        var mutexName = $@"Local\TaskOverlay.WpfV2.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceGuard.TryAcquire(mutexName);
        using var second = SingleInstanceGuard.TryAcquire(mutexName);

        Assert(first is not null, "The first WPF instance should acquire the guard.");
        Assert(second is null, "A second WPF instance must be rejected.");
    }

    private static void UserFacingOverlayModeLabels()
    {
        var options = OverlayModeDisplay.UserModes.ToArray();

        Assert(options.Length == 3, "Settings should expose exactly three overlay modes.");
        Assert(
            options.Select(option => option.Mode).SequenceEqual(new[]
            {
                OverlayMode.Working,
                OverlayMode.PinnedExpanded,
                OverlayMode.CollapsedHandle
            }),
            "Settings should expose only the supported user-facing overlay modes.");
        Assert(
            options.Select(option => option.Label).SequenceEqual(new[]
            {
                "Working",
                "Pinned",
                "Collapsed handle"
            }),
            "Settings overlay mode labels should use current terminology.");
        Assert(
            OverlayModeDisplay.GetLabel(OverlayMode.AutoQuestTracker) == "Working",
            "Legacy auto mode should display as Working.");
    }

    private static void WorkingModeTaskFiltering()
    {
        var now = DateTimeOffset.Parse("2026-06-27T10:00:00Z");
        var todo = TaskItem.Create("TODO", now.AddMinutes(-4));
        todo.PinToPanel = true;
        var focus = TaskItem.Create("FOCUS", now.AddMinutes(-3));
        focus.Status = TaskStatus.InWork;
        focus.InWork = true;
        var waiting = TaskItem.Create("WAIT", now.AddMinutes(-2));
        waiting.Status = TaskStatus.Waiting;
        waiting.PinToPanel = true;
        var remind = TaskItem.Create("REMIND", now.AddMinutes(-1));
        remind.RemindAtUtc = now;
        var scheduled = TaskItem.Create("Scheduled REMIND", now);
        scheduled.RemindAtUtc = now.AddHours(2);
        var done = TaskItem.Create("DONE", now);
        done.Status = TaskStatus.Done;
        done.Completed = true;
        done.RemindAtUtc = now.AddHours(1);
        var tasks = new[] { todo, focus, waiting, remind, scheduled, done };
        var sourceIds = tasks.Select(task => task.Id).ToArray();
        var ordered = ReminderAttentionService.OrderForOverlay(tasks, now).ToList();

        var working = OverlayTaskFilter
            .SelectForMode(ordered, OverlayMode.Working, now)
            .ToList();

        Assert(working.Count == 2, "Working should show only FOCUS and active REMIND tasks.");
        Assert(working.Any(task => task.Id == focus.Id), "Working should include FOCUS tasks.");
        Assert(working.Any(task => task.Id == remind.Id), "Working should preserve REMIND attention items.");
        Assert(
            working.All(task =>
                task.Id != todo.Id &&
                task.Id != waiting.Id &&
                task.Id != scheduled.Id &&
                task.Id != done.Id),
            "Working should hide normal TODO, scheduled-only REMIND, pinned WAIT, and DONE tasks.");
        Assert(
            tasks.Select(task => task.Id).SequenceEqual(sourceIds),
            "Working filtering must not mutate or reorder the source list.");

        var pinned = OverlayTaskFilter
            .SelectForMode(ordered, OverlayMode.PinnedExpanded, now)
            .ToList();
        Assert(
            pinned.Select(task => task.Id).ToHashSet().SetEquals(
                new[] { todo.Id, waiting.Id }),
            "Pinned should use PinToPanel tasks instead of restoring the full task set.");
        Assert(
            OverlayTaskPresentationPolicy.ShouldShowFocusBadge(
                focus,
                OverlayMode.PinnedExpanded),
            "Switching Working to Pinned should immediately restore the FOCUS badge.");

        var returnedToWorking = OverlayTaskFilter
            .SelectForMode(ordered, OverlayMode.Working, now)
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

    private static void OverlayPanelTaskFiltering()
    {
        var now = DateTimeOffset.Parse("2026-07-01T10:00:00Z");
        var panelTodo = TaskItem.Create("Panel TODO", now.AddMinutes(-6));
        panelTodo.PinToPanel = true;
        var focus = TaskItem.Create("FOCUS", now.AddMinutes(-5));
        focus.Status = TaskStatus.InWork;
        focus.InWork = true;
        var wait = TaskItem.Create("WAIT", now.AddMinutes(-4));
        wait.Status = TaskStatus.Waiting;
        wait.PinToPanel = true;
        var scheduledRemind = TaskItem.Create("Scheduled REMIND", now.AddMinutes(-3));
        scheduledRemind.RemindAtUtc = now.AddHours(2);
        var activeRemind = TaskItem.Create("Active REMIND", now.AddMinutes(-2));
        activeRemind.RemindAtUtc = now.AddMinutes(-1);
        var todo = TaskItem.Create("TODO", now.AddMinutes(-2));
        var done = TaskItem.Create("DONE", now.AddMinutes(-1));
        done.PinToPanel = true;
        done.RemindAtUtc = now.AddHours(1);
        TaskInteractionService.Complete(done, now);
        var tasks = new[]
        {
            panelTodo,
            focus,
            wait,
            scheduledRemind,
            activeRemind,
            todo,
            done
        };
        var sourceIds = tasks.Select(task => task.Id).ToArray();

        Assert(
            OverlayTaskFilter.SelectForPanel(tasks, OverlayPanelFilter.Panel, now)
                .Select(task => task.Id)
                .SequenceEqual(new[] { panelTodo.Id, wait.Id }),
            "Panel should include only non-DONE PinToPanel tasks.");
        Assert(
            OverlayTaskFilter.SelectForPanel(tasks, OverlayPanelFilter.Focus, now)
                .Single().Id == focus.Id,
            "FOCUS should include only focused tasks.");
        Assert(
            OverlayTaskFilter.SelectForPanel(tasks, OverlayPanelFilter.Wait, now)
                .Single().Id == wait.Id,
            "WAIT should include only waiting tasks.");
        Assert(
            OverlayTaskFilter.SelectForPanel(tasks, OverlayPanelFilter.Remind, now)
                .Select(task => task.Id)
                .SequenceEqual(new[] { activeRemind.Id, scheduledRemind.Id }),
            "REMIND should put active attention before scheduled reminder metadata and exclude DONE.");
        Assert(
            OverlayTaskFilter.SelectForPanel(tasks, OverlayPanelFilter.Todo, now)
                .Select(task => task.Id)
                .ToHashSet()
                .SetEquals(new[]
                {
                    panelTodo.Id,
                    scheduledRemind.Id,
                    activeRemind.Id,
                    todo.Id
                }),
            "TODO should follow task status and exclude DONE.");
        Assert(
            tasks.Select(task => task.Id).SequenceEqual(sourceIds),
            "Overlay panel projections must not mutate or reorder source tasks.");

        var invalid = new OverlaySettings
        {
            PanelFilter = (OverlayPanelFilter)999
        };
        Assert(invalid.NormalizePanelPresentation(), "Invalid panel filter should repair to Panel.");
        Assert(invalid.PanelFilter == OverlayPanelFilter.Panel, "Panel should be the safe fallback.");
        Assert(!invalid.NormalizePanelPresentation(), "Panel filter repair should be idempotent.");
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

    private static void BackupFilenameGeneration()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-03T09:30:00+06:00");
        var fileName = BackupService.BuildFileName(
            "Work",
            "WORKPC",
            timestamp);

        Assert(
            fileName == "TaskOverlay_Work_WORKPC_2026-07-03_09-30.json",
            "Backup filename should include space, machine, and local timestamp.");
    }

    private static void BackupMachineNameSanitization()
    {
        Assert(
            BackupService.SanitizeMachineName(" Work PC:/01 ") == "Work_PC_01",
            "Machine name should be safe for a filename.");
        Assert(
            BackupService.SanitizeMachineName("***") == "UnknownMachine",
            "An unusable machine name should use a stable fallback.");
    }

    private static void BackupRetentionSelection()
    {
        var now = DateTimeOffset.Parse("2026-07-03T10:00:00Z");
        var files = new[]
        {
            new BackupFileInfo("newest.json", now.AddHours(-1)),
            new BackupFileInfo("second.json", now.AddHours(-2)),
            new BackupFileInfo("third.json", now.AddHours(-3)),
            new BackupFileInfo("expired.json", now.AddDays(-20))
        };

        var selected = BackupService.SelectRetentionFiles(
            files,
            now,
            retentionDays: 14,
            maximumFiles: 2);
        Assert(selected.Count == 2, "Retention should select age and count overflow.");
        Assert(selected.Contains("third.json"), "Retention should enforce maximum files.");
        Assert(selected.Contains("expired.json"), "Retention should remove expired files.");
    }

    private static void BackupDisabledAndMissingState()
    {
        WithTemporaryDirectory(directory =>
        {
            var missingStatePath = Path.Combine(directory, "missing-state.json");
            var service = new BackupService(missingStatePath);
            var disabled = service.CreateBackup(
                new BackupConfiguration(false, directory, 30, 14, 100),
                requireEnabled: true,
                DateTimeOffset.UtcNow,
                "TESTPC");
            Assert(
                disabled.Outcome == BackupOutcome.SkippedDisabled,
                "Disabled automatic backup should skip before file access.");
            var schedule = new BackupSettings
            {
                Enabled = false,
                FolderPath = directory
            };
            var scheduleNow = DateTimeOffset.Parse("2026-07-03T10:00:00Z");
            Assert(
                !BackupService.IsAutomaticBackupDue(schedule, scheduleNow),
                "Disabled automatic backup should never be due.");
            schedule.Enabled = true;
            schedule.LastBackupAttemptAtUtc = scheduleNow.AddMinutes(-29);
            Assert(
                !BackupService.IsAutomaticBackupDue(schedule, scheduleNow),
                "A failed or successful attempt should throttle the next run.");
            schedule.LastBackupAttemptAtUtc = scheduleNow.AddMinutes(-30);
            Assert(
                BackupService.IsAutomaticBackupDue(schedule, scheduleNow),
                "Automatic backup should become due at the configured interval.");

            var missing = service.CreateBackup(
                new BackupConfiguration(true, directory, 30, 14, 100),
                requireEnabled: true,
                DateTimeOffset.UtcNow,
                "TESTPC");
            Assert(
                missing.Outcome == BackupOutcome.Failed &&
                missing.Message.Contains("State file is missing"),
                "Missing state should fail safely without throwing.");

            File.WriteAllText(missingStatePath, "{}");
            var unavailableFolder = service.CreateBackup(
                new BackupConfiguration(
                    true,
                    Path.Combine(directory, "unavailable"),
                    30,
                    14,
                    100),
                requireEnabled: true,
                DateTimeOffset.UtcNow,
                "TESTPC");
            Assert(
                unavailableFolder.Outcome == BackupOutcome.Failed &&
                unavailableFolder.Message.Contains("unavailable"),
                "Unavailable backup folder should fail safely without being created.");
        });
    }

    private static void BackupCopyAndScopedRetention()
    {
        WithTemporaryDirectory(directory =>
        {
            var stateDirectory = Path.Combine(directory, "state");
            var backupDirectory = Path.Combine(directory, "backups");
            Directory.CreateDirectory(stateDirectory);
            Directory.CreateDirectory(backupDirectory);
            var statePath = Path.Combine(stateDirectory, "state.json");
            const string stateJson = "{\"schemaVersion\":2,\"tasks\":[]}";
            File.WriteAllText(statePath, stateJson);

            var oldWork = Path.Combine(
                backupDirectory,
                "TaskOverlay_Work_TEST_PC_2026-06-01_10-00.json");
            var homeBackup = Path.Combine(
                backupDirectory,
                "TaskOverlay_Home_TESTPC_2026-06-01_10-00.json");
            var otherMachineBackup = Path.Combine(
                backupDirectory,
                "TaskOverlay_Work_OTHERPC_2026-06-01_10-00.json");
            var oldWorkMetadata = Path.Combine(
                backupDirectory,
                "TaskOverlay_Work_TEST_PC_2026-06-01_10-00.meta.json");
            var unrelated = Path.Combine(backupDirectory, "notes.json");
            File.WriteAllText(oldWork, "old work");
            File.WriteAllText(oldWorkMetadata, "{}");
            File.WriteAllText(homeBackup, "old home");
            File.WriteAllText(otherMachineBackup, "other machine");
            File.WriteAllText(unrelated, "unrelated");
            var expiredTimestamp = DateTime.SpecifyKind(
                new DateTime(2026, 6, 1, 10, 0, 0),
                DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(oldWork, expiredTimestamp);
            File.SetLastWriteTimeUtc(homeBackup, expiredTimestamp);
            File.SetLastWriteTimeUtc(otherMachineBackup, expiredTimestamp);

            var result = new BackupService(statePath).CreateBackup(
                new BackupConfiguration(true, backupDirectory, 30, 14, 100),
                requireEnabled: true,
                DateTimeOffset.Parse("2026-07-03T09:30:00Z"),
                "TEST_PC");

            Assert(result.Succeeded, "Backup copy should succeed in a local folder.");
            Assert(
                result.BackupPath is not null &&
                File.ReadAllText(result.BackupPath) == stateJson,
                "Backup should contain an unchanged copy of state data.");
            Assert(!File.Exists(oldWork), "Expired Work backup should be removed.");
            Assert(
                !File.Exists(oldWorkMetadata),
                "Retention should remove metadata with its expired backup.");
            Assert(File.Exists(homeBackup), "Work retention must not remove Home backups.");
            Assert(
                File.Exists(otherMachineBackup),
                "Retention must not remove another machine's Work backups.");
            Assert(File.Exists(unrelated), "Retention must not remove unrelated files.");
            Assert(
                !Directory.EnumerateFiles(backupDirectory, "*.tmp").Any(),
                "Successful backup should leave no temporary files.");
            var metadataPath = Path.Combine(
                backupDirectory,
                $"{Path.GetFileNameWithoutExtension(result.BackupPath)}.meta.json");
            Assert(File.Exists(metadataPath), "Backup should create matching metadata.");
        });
    }

    private static void LocalBackupSettingsPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new LocalSettingsStore(directory);
            var settings = new LocalAppSettings();
            settings.Backups.Enabled = true;
            settings.Backups.FolderPath = Path.Combine(directory, "Backups");
            settings.Backups.LastBackupAtUtc =
                DateTimeOffset.Parse("2026-07-03T03:30:00Z");
            settings.Backups.LastBackupAttemptAtUtc =
                DateTimeOffset.Parse("2026-07-03T03:31:00Z");
            store.Save(settings);

            var loaded = new LocalSettingsStore(directory).Load();
            Assert(
                File.Exists(Path.Combine(directory, "localSettings.json")),
                "Backup settings should use the machine-local settings file.");
            Assert(
                !File.Exists(Path.Combine(directory, "state.json")),
                "Saving backup settings must not create or modify shared state.json.");
            Assert(loaded.Backups.Enabled, "Backup enabled setting should persist.");
            Assert(
                loaded.Backups.FolderPath == settings.Backups.FolderPath,
                "Machine-local backup folder should persist outside state.json.");
            Assert(
                loaded.Backups.IntervalMinutes == 30 &&
                loaded.Backups.RetentionDays == 14 &&
                loaded.Backups.MaximumFiles == 100,
                "Backup defaults should remain stable across save/load.");
            Assert(
                loaded.Backups.LastBackupAttemptAtUtc ==
                settings.Backups.LastBackupAttemptAtUtc,
                "Backup attempt timestamp should persist for interval throttling.");
        });
    }

    private static void BackupMetadataAndLatestDiscovery()
    {
        WithTemporaryDirectory(directory =>
        {
            var statePath = Path.Combine(directory, "state.json");
            var backupDirectory = Path.Combine(directory, "backups");
            Directory.CreateDirectory(backupDirectory);
            File.WriteAllText(statePath, "{\"tasks\":[],\"projects\":[]}");
            var stateTimestamp = DateTime.SpecifyKind(
                new DateTime(2026, 7, 3, 9, 25, 0),
                DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(statePath, stateTimestamp);
            var service = new BackupService(statePath);
            var configuration = new BackupConfiguration(
                true,
                backupDirectory,
                30,
                14,
                100);

            var emptyCheck = service.CheckBackupFolder(configuration, "WORKPC");
            Assert(
                emptyCheck.Status == BackupFreshnessStatus.NoBackupsFound &&
                !Directory.EnumerateFiles(backupDirectory).Any(),
                "Freshness check must not create an automatic backup.");

            var createdAt = DateTimeOffset.Parse("2026-07-03T16:46:00Z");
            var backup = service.CreateBackup(
                configuration,
                requireEnabled: true,
                createdAt,
                "WORKPC");
            Assert(backup.Succeeded, "Backup with metadata should succeed.");
            var metadataPath = Path.Combine(
                backupDirectory,
                $"{Path.GetFileNameWithoutExtension(backup.BackupPath!)}.meta.json");
            using var metadataDocument = JsonDocument.Parse(
                File.ReadAllText(metadataPath));
            var metadata = metadataDocument.RootElement;
            Assert(
                metadata.GetProperty("sourceMachine").GetString() == "WORKPC",
                "Metadata should contain the source machine.");
            Assert(
                metadata.GetProperty("taskSpace").GetString() == "Work",
                "Metadata should contain the task space.");
            Assert(
                metadata.GetProperty("backupCreatedAtUtc").GetDateTimeOffset() ==
                createdAt,
                "Metadata should contain the UTC creation timestamp.");
            Assert(
                metadata.GetProperty("stateLastWriteTimeUtc").GetDateTimeOffset() ==
                new DateTimeOffset(stateTimestamp),
                "Metadata should contain the source state timestamp.");

            var check = service.CheckBackupFolder(configuration, "HOMEPC");
            Assert(check.LatestBackup is not null, "Latest backup should be discovered.");
            Assert(
                check.LatestBackup!.MetadataPath == metadataPath &&
                check.LatestBackup.SourceMachine == "WORKPC" &&
                check.LatestBackup.TaskSpace == "Work",
                "Discovery should prefer valid metadata values.");
            Assert(
                check.Status == BackupFreshnessStatus.UpToDate,
                "Matching state timestamps should appear up to date.");
        });
    }

    private static void BackupDiscoveryFallbackAndFreshness()
    {
        WithTemporaryDirectory(directory =>
        {
            var statePath = Path.Combine(directory, "state.json");
            var backupDirectory = Path.Combine(directory, "backups");
            Directory.CreateDirectory(backupDirectory);
            File.WriteAllText(statePath, "{\"tasks\":[],\"projects\":[]}");
            File.SetLastWriteTimeUtc(
                statePath,
                DateTime.SpecifyKind(new DateTime(2026, 7, 2, 10, 0, 0), DateTimeKind.Utc));

            var workBackup = Path.Combine(
                backupDirectory,
                "TaskOverlay_Work_REMOTE_PC_2026-07-03_12-00.json");
            File.WriteAllText(workBackup, "{\"tasks\":[],\"projects\":[]}");
            File.WriteAllText(
                Path.Combine(
                    backupDirectory,
                    "TaskOverlay_Work_REMOTE_PC_2026-07-03_12-00.meta.json"),
                "not valid json");
            File.WriteAllText(
                Path.Combine(
                    backupDirectory,
                    "TaskOverlay_Home_OTHERPC_2026-07-04_12-00.json"),
                "{\"tasks\":[],\"projects\":[]}");
            File.WriteAllText(
                Path.Combine(backupDirectory, "unrelated.json"),
                "{}");

            var service = new BackupService(statePath);
            var configuration = new BackupConfiguration(
                true,
                backupDirectory,
                30,
                14,
                100);
            var backupNewer = service.CheckBackupFolder(configuration, "LOCALPC");
            Assert(
                backupNewer.Status == BackupFreshnessStatus.BackupNewer,
                "Filename timestamp fallback should detect a newer backup.");
            Assert(
                backupNewer.LatestBackup?.SourceMachine == "REMOTE_PC",
                "Source machine should be inferred from filename when metadata is invalid.");
            Assert(
                backupNewer.LatestBackup?.BackupPath == workBackup,
                "Discovery should ignore unrelated and future Home files.");

            File.SetLastWriteTimeUtc(
                statePath,
                DateTime.SpecifyKind(new DateTime(2026, 7, 5, 10, 0, 0), DateTimeKind.Utc));
            var localNewer = service.CheckBackupFolder(configuration, "LOCALPC");
            Assert(
                localNewer.Status == BackupFreshnessStatus.LocalNewer,
                "Freshness comparison should detect newer local state.");
        });
    }

    private static void BackupRestoreSafetyPair()
    {
        WithTemporaryDirectory(directory =>
        {
            var localDirectory = Path.Combine(directory, "local");
            var remoteDirectory = Path.Combine(directory, "remote");
            var backupDirectory = Path.Combine(directory, "backups");
            Directory.CreateDirectory(localDirectory);
            Directory.CreateDirectory(remoteDirectory);
            Directory.CreateDirectory(backupDirectory);
            var localStatePath = Path.Combine(localDirectory, "state.json");
            var remoteStatePath = Path.Combine(remoteDirectory, "state.json");
            const string localJson =
                "{\"tasks\":[{\"title\":\"local\"}],\"projects\":[]}";
            const string remoteJson =
                "{\"tasks\":[{\"title\":\"remote\"}],\"projects\":[]}";
            File.WriteAllText(localStatePath, localJson);
            File.WriteAllText(remoteStatePath, remoteJson);
            File.SetLastWriteTimeUtc(
                localStatePath,
                DateTime.SpecifyKind(new DateTime(2026, 7, 3, 8, 0, 0), DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(
                remoteStatePath,
                DateTime.SpecifyKind(new DateTime(2026, 7, 3, 10, 0, 0), DateTimeKind.Utc));
            var configuration = new BackupConfiguration(
                true,
                backupDirectory,
                30,
                14,
                100);
            var remoteBackup = new BackupService(remoteStatePath).CreateBackup(
                configuration,
                requireEnabled: true,
                DateTimeOffset.Parse("2026-07-03T10:05:00Z"),
                "REMOTEPC");
            Assert(remoteBackup.Succeeded, "Remote test backup should be created.");

            var localService = new BackupService(localStatePath);
            var check = localService.CheckBackupFolder(configuration, "LOCALPC");
            Assert(
                check.Status == BackupFreshnessStatus.BackupNewer &&
                check.LatestBackup is not null,
                "Remote backup should be a restore candidate.");
            var restored = localService.RestoreLatestBackup(
                configuration,
                check.LatestBackup!,
                DateTimeOffset.Parse("2026-07-03T10:10:00Z"),
                "LOCALPC");

            Assert(restored.Succeeded, "Valid latest backup should restore.");
            Assert(
                File.ReadAllText(localStatePath) == remoteJson,
                "Restore should replace local state with selected backup data.");
            Assert(
                File.Exists(remoteBackup.BackupPath!),
                "Restore must not delete the selected backup.");
            Assert(
                restored.SafetyBackupPath is not null &&
                File.ReadAllText(restored.SafetyBackupPath) == localJson,
                "Restore should preserve current local state in a safety backup.");
            var safetyMetadataPath = Path.Combine(
                backupDirectory,
                $"{Path.GetFileNameWithoutExtension(restored.SafetyBackupPath!)}.meta.json");
            Assert(
                File.Exists(safetyMetadataPath),
                "Before-restore safety backup should include metadata.");
            var afterRestoreBackup = localService.CreateBackup(
                configuration with { MaximumFiles = 1 },
                requireEnabled: true,
                DateTimeOffset.Parse("2026-07-03T10:11:00Z"),
                "LOCALPC");
            Assert(afterRestoreBackup.Succeeded, "Backup should still work after restore.");
            Assert(
                File.Exists(restored.SafetyBackupPath),
                "Retention must not delete a before-restore safety backup.");
            Assert(
                File.Exists(remoteBackup.BackupPath!),
                "Local-machine retention must not delete the selected remote backup.");
        });
    }

    private static void BackupRestoreRejectsInvalidState()
    {
        WithTemporaryDirectory(directory =>
        {
            var statePath = Path.Combine(directory, "state.json");
            var backupDirectory = Path.Combine(directory, "backups");
            Directory.CreateDirectory(backupDirectory);
            const string localJson = "{\"tasks\":[],\"projects\":[]}";
            File.WriteAllText(statePath, localJson);
            var invalidPath = Path.Combine(
                backupDirectory,
                "TaskOverlay_Work_REMOTEPC_2026-07-03_12-00.json");
            File.WriteAllText(invalidPath, "not valid json");
            var candidate = new BackupCandidate(
                invalidPath,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "REMOTEPC",
                "Work");
            var result = new BackupService(statePath).RestoreLatestBackup(
                new BackupConfiguration(true, backupDirectory, 30, 14, 100),
                candidate,
                DateTimeOffset.UtcNow,
                "LOCALPC");

            Assert(!result.Succeeded, "Invalid JSON backup must be rejected.");
            Assert(
                File.ReadAllText(statePath) == localJson,
                "Invalid restore must not change local state.");
            Assert(
                !Directory.EnumerateFiles(backupDirectory, "*_before-restore.json").Any(),
                "Invalid restore must not create a misleading safety backup.");
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

    private static void WorkspaceSnapshotContract()
    {
        var now = DateTimeOffset.Parse("2026-07-04T09:00:00Z");
        var state = AppState.CreateDefault(now);
        state.Tasks.Clear();
        var project = state.Projects[0];
        project.ColorHex = "#22AA77";
        var group = new GroupItem
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Delivery",
            SortOrder = 2,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        state.Groups.Add(group);
        var waiting = TaskItem.Create("Await review", now, project.Id);
        waiting.GroupId = group.Id;
        waiting.Status = TaskStatus.Waiting;
        waiting.WaitingFor = "reviewer";
        waiting.PinToPanel = true;
        waiting.RemindAtUtc = now.AddMinutes(-5);
        waiting.LastReminderAtUtc = waiting.RemindAtUtc;
        waiting.ReminderActive = true;
        waiting.DueAtUtc = now.AddDays(1);
        var focus = TaskItem.Create("Current work", now.AddMinutes(1), project.Id);
        focus.Status = TaskStatus.InWork;
        state.Tasks.Add(waiting);
        state.Tasks.Add(focus);
        state.WorkspaceSettings.ActiveTab = WorkspaceTab.Status;
        state.WorkspaceSettings.SelectedProjectIds = new() { project.Id };
        state.WorkspaceSettings.SelectedTaskId = waiting.Id;
        state.WorkspaceSettings.SelectedTimelineItemId = $"remind:{waiting.Id:N}";
        state.WorkspaceSettings.Filter = WorkspaceFilter.Active;

        var snapshot = WorkspaceSnapshotFactory.Create(state, now);
        var waitingSnapshot = snapshot.Tasks.Single(task =>
            task.Id == waiting.Id.ToString("N"));

        Assert(snapshot.SchemaVersion == 1, "Workspace contract version should be 1.");
        Assert(snapshot.Mode == "readonly", "Workspace contract should be read-only.");
        Assert(
            WorkspaceSnapshotFactory.Create(
                state,
                now,
                WorkspaceSnapshotFactory.ConnectedMode).Mode == "connected",
            "Workspace write bridge should explicitly advertise connected mode.");
        Assert(snapshot.GeneratedAtUtc == now, "Workspace snapshot timestamp mismatch.");
        Assert(snapshot.Projects.Single().Color == "#22AA77", "Project color should be projected.");
        Assert(waitingSnapshot.Status == "WAIT", "Waiting status should be projected.");
        Assert(waitingSnapshot.WaitingFor == "reviewer", "Waiting metadata should be projected.");
        Assert(waitingSnapshot.PinToPanel, "Panel pin should be projected.");
        Assert(waitingSnapshot.ReminderActive, "Active reminder should be projected.");
        Assert(
            snapshot.Context is
            {
                ActiveTab: "status",
                Filter: "active"
            } &&
            snapshot.Context.SelectedProjectIds.SequenceEqual(new[] { project.Id.ToString("N") }) &&
            snapshot.Context.SelectedTaskId == waiting.Id.ToString("N") &&
            snapshot.Context.SelectedTimelineItemId == $"remind:{waiting.Id:N}",
            "Workspace snapshot should carry persisted UI context.");
        Assert(
            snapshot.ActiveNow.Select(item => item.TaskId).OrderBy(id => id).SequenceEqual(
                new[] { waiting.Id.ToString("N"), focus.Id.ToString("N") }.OrderBy(id => id)),
            "Active now should contain FOCUS and active REMIND tasks.");

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var document = JsonDocument.Parse(json);
        Assert(document.RootElement.GetProperty("schemaVersion").GetInt32() == 1,
            "Serialized workspace contract should use camelCase fields.");
        Assert(document.RootElement.GetProperty("mode").GetString() == "readonly",
            "Serialized workspace contract mode mismatch.");
    }

    private static void WorkspaceTimelineConsistency()
    {
        var now = DateTimeOffset.Parse("2026-07-04T09:00:00Z");
        var state = AppState.CreateDefault(now);
        var project = state.Projects[0];
        state.Tasks.Clear();
        var reminderTask = TaskItem.Create("Reminder task", now, project.Id);
        reminderTask.RemindAtUtc = now.AddHours(1);
        var deadlineTask = TaskItem.Create("Deadline task", now, project.Id);
        deadlineTask.DueAtUtc = now.AddDays(2);
        var plainTask = TaskItem.Create("Plain task", now, project.Id);
        state.Tasks.Add(reminderTask);
        state.Tasks.Add(deadlineTask);
        state.Tasks.Add(plainTask);

        var snapshot = WorkspaceSnapshotFactory.Create(state, now);
        Assert(snapshot.TimelineItems.Count == 2,
            "Only tasks with reminder/deadline metadata should appear in Timeline.");
        foreach (var item in snapshot.TimelineItems)
        {
            var linkedTask = snapshot.Tasks.Single(task => task.Id == item.LinkedTaskId);
            if (item.Kind == "REMIND")
            {
                Assert(linkedTask.ReminderAtUtc == item.OccursAtUtc,
                    "REMIND must use the linked task reminder timestamp.");
            }
            else if (item.Kind == "DEADLINE")
            {
                Assert(linkedTask.DeadlineAtUtc == item.OccursAtUtc,
                    "DEADLINE must use the linked task deadline timestamp.");
            }
            else
            {
                throw new InvalidOperationException("Unexpected real Timeline kind.");
            }
        }

        Assert(snapshot.TimelineItems.All(item => item.LinkedTaskId != plainTask.Id.ToString("N")),
            "Task without attention metadata must not produce a Timeline row.");
    }

    private static void WorkspaceOrphanFallback()
    {
        var now = DateTimeOffset.Parse("2026-07-04T09:00:00Z");
        var state = new AppState
        {
            Projects = new(),
            Groups = new(),
            Tasks =
            {
                new TaskItem
                {
                    Id = Guid.NewGuid(),
                    ProjectId = Guid.NewGuid(),
                    GroupId = Guid.NewGuid(),
                    Title = "Orphan task",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }
            }
        };

        var snapshot = WorkspaceSnapshotFactory.Create(state, now);
        Assert(snapshot.Projects.Count == 1, "Empty state should expose a safe Default project.");
        Assert(snapshot.Projects[0].Name == ProjectItem.DefaultName,
            "Fallback project should be Default.");
        Assert(snapshot.Tasks.Single().ProjectId == snapshot.Projects[0].Id,
            "Orphan task should resolve to the snapshot Default project.");
        Assert(snapshot.Sections.Any(section =>
                section.Id == snapshot.Tasks[0].SectionId && section.IsProjectRoot),
            "Orphan task should resolve to a visible project-root section.");
        Assert(state.Projects.Count == 0,
            "Workspace snapshot creation must not mutate source state.");
    }

    private static void WorkspaceStatePersistenceAndRepair()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = AppState.CreateDefault();
            var project = state.Projects[0];
            var task = state.Tasks[0];
            task.RemindAtUtc = DateTimeOffset.UtcNow.AddHours(1);
            state.WorkspaceSettings = new WorkspaceSettings
            {
                ActiveTab = WorkspaceTab.Timeline,
                SelectedProjectIds = new() { project.Id },
                SelectedTaskId = task.Id,
                SelectedTimelineItemId = $"remind:{task.Id:N}",
                SelectedWorkstreamId = "workstream-1",
                Filter = WorkspaceFilter.ActivePath
            };

            store.Save(state);
            var loaded = store.Load();
            Assert(
                loaded.WorkspaceSettings.ActiveTab == WorkspaceTab.Timeline &&
                loaded.WorkspaceSettings.SelectedProjectIds.SequenceEqual(new[] { project.Id }) &&
                loaded.WorkspaceSettings.SelectedTaskId == task.Id &&
                loaded.WorkspaceSettings.SelectedTimelineItemId == $"remind:{task.Id:N}" &&
                loaded.WorkspaceSettings.SelectedWorkstreamId == "workstream-1" &&
                loaded.WorkspaceSettings.Filter == WorkspaceFilter.ActivePath,
                "Workspace context should survive save/load.");

            var root = JsonNode.Parse(File.ReadAllText(store.StatePath))!.AsObject();
            root.Remove("workspaceSettings");
            File.WriteAllText(
                store.StatePath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var oldState = store.Load();
            Assert(
                oldState.WorkspaceSettings.SelectedProjectIds.Count == 1 &&
                oldState.WorkspaceSettings.ActiveTab == WorkspaceTab.Tree,
                "Old schema v2 state should receive safe Workspace defaults.");
        });

        var repairState = AppState.CreateDefault();
        repairState.WorkspaceSettings = new WorkspaceSettings
        {
            ActiveTab = (WorkspaceTab)999,
            SelectedProjectIds = new() { Guid.NewGuid(), Guid.NewGuid() },
            SelectedTaskId = Guid.NewGuid(),
            SelectedTimelineItemId = "missing-item",
            Filter = (WorkspaceFilter)999
        };
        Assert(WorkspaceStatePolicy.Normalize(repairState),
            "Invalid Workspace context should be repaired.");
        Assert(
            repairState.WorkspaceSettings.ActiveTab == WorkspaceTab.Tree &&
            repairState.WorkspaceSettings.Filter == WorkspaceFilter.All &&
            repairState.WorkspaceSettings.SelectedProjectIds.SequenceEqual(
                new[] { repairState.Projects[0].Id }) &&
            repairState.Tasks.Any(task =>
                task.Id == repairState.WorkspaceSettings.SelectedTaskId &&
                task.ProjectId == repairState.Projects[0].Id) &&
            repairState.WorkspaceSettings.SelectedTimelineItemId is null,
            "Workspace repair should fall back to valid project/task context.");
        Assert(!WorkspaceStatePolicy.Normalize(repairState),
            "Normalized Workspace context should be idempotent.");
    }

    private static void WorkspaceContextCommandPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = AppState.CreateDefault();
            var project = state.Projects[0];
            var task = state.Tasks[0];
            task.RemindAtUtc = DateTimeOffset.UtcNow.AddHours(1);
            var store = new AppStateStore(directory);
            var saves = 0;
            var refreshes = 0;
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () =>
                {
                    store.Save(state);
                    saves++;
                },
                () => refreshes++);

            var result = dispatcher.Dispatch(WorkspaceCommandJson(
                "workspace-context",
                "updateWorkspaceContext",
                new
                {
                    activeTab = "status",
                    selectedProjectIds = new[] { project.Id.ToString("N") },
                    selectedTaskId = task.Id.ToString("N"),
                    selectedTimelineItemId = $"remind:{task.Id:N}",
                    selectedWorkstreamId = (string?)null,
                    filter = "active"
                }));
            Assert(result.Success && saves == 1 && refreshes == 0,
                "Valid Workspace context should save without refreshing task presentations.");
            var loaded = store.Load().WorkspaceSettings;
            Assert(
                loaded.ActiveTab == WorkspaceTab.Status &&
                loaded.SelectedProjectIds.SequenceEqual(new[] { project.Id }) &&
                loaded.SelectedTaskId == task.Id &&
                loaded.SelectedTimelineItemId == $"remind:{task.Id:N}" &&
                loaded.Filter == WorkspaceFilter.Active,
                "Workspace context command should persist through AppStateStore.");

            var invalid = dispatcher.Dispatch(WorkspaceCommandJson(
                "workspace-context-invalid",
                "updateWorkspaceContext",
                new
                {
                    activeTab = "dashboard",
                    selectedProjectIds = new[] { project.Id.ToString("N") },
                    selectedTaskId = task.Id.ToString("N"),
                    selectedTimelineItemId = (string?)null,
                    selectedWorkstreamId = (string?)null,
                    filter = "all"
                }));
            Assert(!invalid.Success && invalid.ErrorCode == "invalidPayload" && saves == 1,
                "Invalid Workspace context should not save or mutate state.");
        });
    }

    private static void WorkspaceCommandStatusPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
            var state = AppState.CreateDefault(now);
            var task = state.Tasks[0];
            task.WaitingFor = "existing reviewer";
            var store = new AppStateStore(directory);
            var saves = 0;
            var refreshes = 0;
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () =>
                {
                    store.Save(state);
                    saves++;
                },
                () => refreshes++);

            var waitResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "status-wait",
                "updateTaskStatus",
                new { taskId = task.Id.ToString("N"), status = "WAIT" }), now);
            Assert(waitResult.Success, "WAIT command should succeed.");
            Assert(task.Status == TaskStatus.Waiting, "WAIT command should update status.");
            Assert(task.WaitingFor == "existing reviewer", "WAIT should preserve waitingFor.");

            var doneAt = now.AddMinutes(1);
            var doneResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "status-done",
                "updateTaskStatus",
                new { taskId = task.Id.ToString("N"), status = "DONE" }), doneAt);
            Assert(doneResult.Success, "DONE command should succeed.");
            Assert(task.Completed && task.CompletedAtUtc == doneAt,
                "DONE should use existing completion semantics.");

            var focusResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "status-focus",
                "updateTaskStatus",
                new { taskId = task.Id.ToString("N"), status = "FOCUS" }), now.AddMinutes(2));
            Assert(focusResult.Success, "FOCUS command should reopen the task.");
            Assert(task.Status == TaskStatus.InWork && !task.Completed && task.CompletedAtUtc is null,
                "FOCUS should use existing reopen/focus semantics.");

            var todoResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "status-todo",
                "updateTaskStatus",
                new { taskId = task.Id.ToString("N"), status = "TODO" }), now.AddMinutes(3));
            Assert(todoResult.Success && task.Status == TaskStatus.Todo,
                "TODO command should leave active work.");
            Assert(saves == 4 && refreshes == 4,
                "Every successful status command should save and refresh.");

            var reloaded = store.Load();
            Assert(reloaded.Tasks.Single(item => item.Id == task.Id).Status == TaskStatus.Todo,
                "Status command should persist through AppStateStore.");
        });
    }

    private static void WorkspaceCommandInvalidTaskAndStatus()
    {
        var now = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
        var state = AppState.CreateDefault(now);
        var task = state.Tasks[0];
        var initialStatus = task.Status;
        var saves = 0;
        var refreshes = 0;
        var dispatcher = new WorkspaceCommandDispatcher(
            state,
            () => saves++,
            () => refreshes++);

        var missingTask = dispatcher.Dispatch(WorkspaceCommandJson(
            "missing-task",
            "updateTaskStatus",
            new { taskId = Guid.NewGuid().ToString("N"), status = "FOCUS" }), now);
        Assert(!missingTask.Success && missingTask.ErrorCode == "taskNotFound",
            "Unknown task should be rejected.");

        var invalidStatus = dispatcher.Dispatch(WorkspaceCommandJson(
            "invalid-status",
            "updateTaskStatus",
            new { taskId = task.Id.ToString("N"), status = "BLOCKED" }), now);
        Assert(!invalidStatus.Success && invalidStatus.ErrorCode == "invalidStatus",
            "Unknown status should be rejected.");
        Assert(task.Status == initialStatus, "Rejected commands must not mutate task status.");
        Assert(saves == 0 && refreshes == 0, "Rejected commands must not save or refresh.");
    }

    private static void WorkspaceCommandPinPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = AppState.CreateDefault();
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () => store.Save(state),
                () => { });

            Assert(dispatcher.Dispatch(WorkspaceCommandJson(
                "pin-on",
                "updateTaskPinToPanel",
                new { taskId = task.Id.ToString("N"), pinToPanel = true })).Success,
                "Pin command should succeed.");
            Assert(dispatcher.Dispatch(WorkspaceCommandJson(
                "pin-off",
                "updateTaskPinToPanel",
                new { taskId = task.Id.ToString("N"), pinToPanel = false })).Success,
                "Unpin command should succeed.");
            Assert(!store.Load().Tasks.Single(item => item.Id == task.Id).PinToPanel,
                "Final panel pin value should persist.");
        });
    }

    private static void WorkspaceCommandNotesPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = AppState.CreateDefault();
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () => store.Save(state),
                () => { });
            var result = dispatcher.Dispatch(WorkspaceCommandJson(
                "notes",
                "updateTaskNotes",
                new { taskId = task.Id.ToString("N"), notes = "  New context  " }));

            Assert(result.Success, "Notes command should succeed.");
            Assert(store.Load().Tasks.Single(item => item.Id == task.Id).Description == "New context",
                "Notes command should persist through the domain service.");
        });
    }

    private static void WorkspaceCommandTitlePersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = AppState.CreateDefault();
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () => store.Save(state),
                () => { });
            var result = dispatcher.Dispatch(WorkspaceCommandJson(
                "title",
                "updateTaskTitle",
                new { taskId = task.Id.ToString("N"), title = "  Updated title  " }));

            Assert(result.Success, "Title command should succeed.");
            Assert(store.Load().Tasks.Single(item => item.Id == task.Id).Title == "Updated title",
                "Title command should persist through the domain service.");
        });
    }

    private static void WorkspaceCommandContractRejection()
    {
        var state = AppState.CreateDefault();
        var task = state.Tasks[0];
        var unknownType = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "unknown",
            "deleteEverything",
            new { taskId = task.Id.ToString("N") }));
        Assert(!unknownType.Success && unknownType.ErrorCode == "unknownCommandType",
            "Unknown command type should be rejected.");

        var wrongVersion = JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            commandId = "wrong-version",
            type = "updateTaskTitle",
            payload = new { taskId = task.Id.ToString("N"), title = "No change" }
        });
        var wrongVersionResult = WorkspaceCommandProcessor.Execute(state, wrongVersion);
        Assert(!wrongVersionResult.Success &&
               wrongVersionResult.ErrorCode == "unsupportedSchemaVersion",
            "Wrong command schemaVersion should be rejected.");

        var invalidPayload = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "invalid-payload",
            "updateTaskPinToPanel",
            new { taskId = task.Id.ToString("N"), pinToPanel = "true" }));
        Assert(!invalidPayload.Success && invalidPayload.ErrorCode == "invalidPayload",
            "Invalid command payload shape should be rejected.");
        Assert(task.Title != "No change", "Rejected contract must not mutate state.");
    }

    private static void WorkspaceCommandPlannedWorkPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-06T08:00:00Z");
            var state = AppState.CreateDefault(now);
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () => store.Save(state),
                () => { });

            var plannedStart = "2026-07-06T09:00:00Z";
            var setResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "plan-set",
                "updateTaskPlannedWork",
                new { taskId = task.Id.ToString("N"), plannedStartAtUtc = plannedStart, plannedDurationMinutes = 60 }),
                now);
            Assert(setResult.Success, "Setting a planned block should succeed.");

            var afterSet = store.Load().Tasks.Single(item => item.Id == task.Id);
            Assert(afterSet.PlannedStartAtUtc == DateTimeOffset.Parse(plannedStart),
                "Planned start should persist through state.json.");
            Assert(afterSet.PlannedDurationMinutes == 60,
                "Planned duration should persist through state.json.");

            var clearResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "plan-clear",
                "updateTaskPlannedWork",
                new { taskId = task.Id.ToString("N"), plannedStartAtUtc = (string?)null, plannedDurationMinutes = (int?)null }),
                now);
            Assert(clearResult.Success, "Clearing a planned block should succeed.");

            var afterClear = store.Load().Tasks.Single(item => item.Id == task.Id);
            Assert(afterClear.PlannedStartAtUtc is null && afterClear.PlannedDurationMinutes is null,
                "Clearing planned work should null both fields in state.json.");
        });
    }

    private static void WorkspaceCommandPlannedWorkValidation()
    {
        var now = DateTimeOffset.Parse("2026-07-06T08:00:00Z");
        var state = AppState.CreateDefault(now);
        var task = state.Tasks[0];

        var missingTask = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "plan-missing",
            "updateTaskPlannedWork",
            new { taskId = Guid.NewGuid().ToString("N"), plannedStartAtUtc = "2026-07-06T09:00:00Z", plannedDurationMinutes = 60 }),
            now);
        Assert(!missingTask.Success && missingTask.ErrorCode == "taskNotFound",
            "Planned work on an unknown task should be rejected.");

        var tooShort = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "plan-short",
            "updateTaskPlannedWork",
            new { taskId = task.Id.ToString("N"), plannedStartAtUtc = "2026-07-06T09:00:00Z", plannedDurationMinutes = 0 }),
            now);
        Assert(!tooShort.Success && tooShort.ErrorCode == "invalidPayload",
            "Zero duration should be rejected as unreasonable.");

        var tooLong = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "plan-long",
            "updateTaskPlannedWork",
            new { taskId = task.Id.ToString("N"), plannedStartAtUtc = "2026-07-06T09:00:00Z", plannedDurationMinutes = 5000 }),
            now);
        Assert(!tooLong.Success && tooLong.ErrorCode == "invalidPayload",
            "Excessive duration should be rejected as unreasonable.");

        var badStart = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "plan-bad-start",
            "updateTaskPlannedWork",
            new { taskId = task.Id.ToString("N"), plannedStartAtUtc = "not-a-date", plannedDurationMinutes = 60 }),
            now);
        Assert(!badStart.Success && badStart.ErrorCode == "invalidPayload",
            "Non-ISO planned start should be rejected.");

        Assert(task.PlannedStartAtUtc is null && task.PlannedDurationMinutes is null,
            "Rejected planned-work commands must not mutate the task.");
    }

    private static void WorkspacePlannedWorkSnapshotAndMigration()
    {
        var now = DateTimeOffset.Parse("2026-07-06T08:00:00Z");

        // Snapshot includes planned fields.
        var state = AppState.CreateDefault(now);
        var planned = state.Tasks[0];
        planned.PlannedStartAtUtc = now.AddHours(2);
        planned.PlannedDurationMinutes = 45;
        var snapshot = WorkspaceSnapshotFactory.Create(state, now);
        var plannedSnapshot = snapshot.Tasks.Single(item => item.Id == planned.Id.ToString("N"));
        Assert(plannedSnapshot.PlannedStartAtUtc == now.AddHours(2),
            "Snapshot should project planned start.");
        Assert(plannedSnapshot.PlannedDurationMinutes == 45,
            "Snapshot should project planned duration.");

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        Assert(json.Contains("plannedStartAtUtc") && json.Contains("plannedDurationMinutes"),
            "Serialized snapshot should carry camelCase planned fields.");

        // Old state.json without planned fields deserializes safely.
        var legacyTask = state.Tasks[1];
        var legacyJson = "{\"id\":\"" + legacyTask.Id + "\",\"title\":\"Legacy\"}";
        var deserialized = JsonSerializer.Deserialize<TaskItem>(legacyJson);
        Assert(deserialized is not null, "Legacy task JSON should deserialize.");
        Assert(deserialized!.PlannedStartAtUtc is null && deserialized.PlannedDurationMinutes is null,
            "Missing planned fields in old state must default to null without corruption.");
    }

    private static void WorkspaceCommandCreateTaskPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-06T09:00:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () => store.Save(state),
                () => { });

            var result = dispatcher.Dispatch(WorkspaceCommandJson(
                "create-1",
                "createTask",
                new { title = "  New task from Workspace  ", projectId = project.Id.ToString("N") }),
                now);

            Assert(result.Success, "createTask should succeed with a valid project.");
            Assert(!string.IsNullOrEmpty(result.CreatedTaskId), "createTask should return the new task id.");

            var createdId = Guid.Parse(result.CreatedTaskId!);
            var loaded = store.Load();
            var created = loaded.Tasks.SingleOrDefault(t => t.Id == createdId);
            Assert(created is not null, "Created task should persist to state.json.");
            Assert(created!.Title == "New task from Workspace", "Created task title should be trimmed and persisted.");
            Assert(created.ProjectId == project.Id, "Created task should be assigned to the requested project.");
            Assert(created.GroupId is null, "Created task without a section should have no group.");
            Assert(created.Status == TaskStatus.Todo, "Created task should default to TODO.");
        });
    }

    private static void WorkspaceCommandCreateTaskInSection()
    {
        var now = DateTimeOffset.Parse("2026-07-06T09:00:00Z");
        var state = AppState.CreateDefault(now);
        var project = state.Projects[0];
        var group = new GroupItem
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Backlog",
            SortOrder = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        state.Groups.Add(group);

        var result = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "create-section",
            "createTask",
            new { title = "Task in section", sectionId = $"group:{group.Id:N}" }),
            now);

        Assert(result.Success, "createTask with a section id should succeed.");
        var createdId = Guid.Parse(result.CreatedTaskId!);
        var created = state.Tasks.Single(t => t.Id == createdId);
        Assert(created.ProjectId == project.Id, "Task created via sectionId should join the section's project.");
        Assert(created.GroupId == group.Id, "Task created via sectionId should join the given group.");

        var rootResult = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "create-root",
            "createTask",
            new { title = "Task at project root", sectionId = $"project:{project.Id:N}:root" }),
            now);
        Assert(rootResult.Success, "createTask with a project-root sectionId should succeed.");
        var rootCreated = state.Tasks.Single(t => t.Id == Guid.Parse(rootResult.CreatedTaskId!));
        Assert(rootCreated.ProjectId == project.Id && rootCreated.GroupId is null,
            "Task created via project-root sectionId should join the project with no group.");
    }

    private static void WorkspaceCommandCreateSectionPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-07T09:00:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () => store.Save(state),
                () => { });

            var result = dispatcher.Dispatch(WorkspaceCommandJson(
                "section-1",
                "createSection",
                new { title = "  Workspace editing  ", projectId = project.Id.ToString("N") }),
                now);

            Assert(result.Success, "createSection should succeed with a valid project.");
            Assert(!string.IsNullOrEmpty(result.CreatedSectionId), "createSection should return the new section id.");
            Assert(
                result.CreatedSectionId!.StartsWith("group:", StringComparison.Ordinal),
                "createSection should return a snapshot-format section id.");

            var groupId = Guid.Parse(result.CreatedSectionId!["group:".Length..]);
            var loaded = store.Load();
            var created = loaded.Groups.SingleOrDefault(g => g.Id == groupId);
            Assert(created is not null, "Created section should persist to state.json.");
            Assert(created!.Name == "Workspace editing", "Created section title should be trimmed and persisted.");
            Assert(created.ProjectId == project.Id, "Created section should belong to the requested project.");
        });
    }

    private static void WorkspaceCommandCreateSectionValidation()
    {
        var now = DateTimeOffset.Parse("2026-07-07T09:00:00Z");
        var state = AppState.CreateDefault(now);
        var project = state.Projects[0];

        var missingProject = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "section-missing-project",
            "createSection",
            new { title = "Orphan stream", projectId = Guid.NewGuid().ToString("N") }),
            now);
        Assert(!missingProject.Success, "createSection with an unknown project should fail.");
        Assert(missingProject.ErrorCode == "mutationRejected", "Unknown project should be rejected as a mutation error.");

        var malformedProject = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "section-bad-project",
            "createSection",
            new { title = "Bad project id", projectId = "not-a-guid" }),
            now);
        Assert(!malformedProject.Success, "createSection with a malformed projectId should fail.");
        Assert(malformedProject.ErrorCode == "invalidPayload", "Malformed projectId should be an invalid payload.");

        var blankTitle = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "section-blank-title",
            "createSection",
            new { title = "   ", projectId = project.Id.ToString("N") }),
            now);
        Assert(!blankTitle.Success, "createSection with a blank title should fail.");
        Assert(blankTitle.ErrorCode == "invalidPayload", "Blank title should be an invalid payload.");

        var longTitle = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "section-long-title",
            "createSection",
            new { title = new string('x', 501), projectId = project.Id.ToString("N") }),
            now);
        Assert(!longTitle.Success, "createSection with an oversized title should fail.");

        Assert(state.Groups.Count == 0, "Rejected createSection commands must not add groups.");
    }

    private static void WorkspaceCommandRenameSectionPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-08T08:00:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var group = new GroupItem
            {
                ProjectId = project.Id,
                Name = "Old section",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            state.Groups.Add(group);
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });

            var result = dispatcher.Dispatch(WorkspaceCommandJson(
                "rename-section", "renameSection",
                new { sectionId = $"group:{group.Id:N}", title = "  Renamed section  " }), now);

            Assert(result.Success, "renameSection should succeed for an editable group section.");
            Assert(store.Load().Groups.Single(g => g.Id == group.Id).Name == "Renamed section",
                "Renamed section should persist through state.json.");

            var rootResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "rename-root", "renameSection",
                new { sectionId = $"project:{project.Id:N}:root", title = "Not allowed" }), now);
            Assert(!rootResult.Success && rootResult.ErrorCode == "invalidPayload",
                "Synthetic project root must not be renameable.");
        });
    }

    private static void WorkspaceCommandDeleteSectionReparentsTasks()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-08T08:15:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var group = new GroupItem
            {
                ProjectId = project.Id,
                Name = "Temporary section",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            state.Groups.Add(group);
            var task = TaskItem.Create("Keep me", now, project.Id);
            task.GroupId = group.Id;
            state.Tasks.Add(task);
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });

            var result = dispatcher.Dispatch(WorkspaceCommandJson(
                "delete-section", "deleteSection",
                new { sectionId = $"group:{group.Id:N}" }), now);

            Assert(result.Success, "deleteSection should succeed for an editable group section.");
            var loaded = store.Load();
            Assert(loaded.Groups.All(g => g.Id != group.Id), "Deleted section should stay deleted after reload.");
            var keptTask = loaded.Tasks.Single(t => t.Id == task.Id);
            Assert(keptTask.ProjectId == project.Id && keptTask.GroupId is null,
                "Tasks from a deleted section should move safely to the project root.");
        });
    }

    private static void WorkspaceCommandCreateTaskInCreatedSection()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-07T09:30:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () => store.Save(state),
                () => { });

            var sectionResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "ws-section",
                "createSection",
                new { title = "Calendar planning", projectId = project.Id.ToString("N") }),
                now);
            Assert(sectionResult.Success, "createSection should succeed before creating a task inside it.");

            var taskResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "ws-task",
                "createTask",
                new { title = "Plan week grid", projectId = project.Id.ToString("N"), sectionId = sectionResult.CreatedSectionId }),
                now);
            Assert(taskResult.Success, "createTask inside the created section should succeed.");

            var groupId = Guid.Parse(sectionResult.CreatedSectionId!["group:".Length..]);
            var loaded = store.Load();
            var created = loaded.Tasks.Single(t => t.Id == Guid.Parse(taskResult.CreatedTaskId!));
            Assert(created.GroupId == groupId, "Task created inside a new section should join that section.");
            Assert(created.ProjectId == project.Id, "Task created inside a new section should join the section's project.");
        });
    }

    private static void WorkspaceSnapshotIncludesCreatedSection()
    {
        var now = DateTimeOffset.Parse("2026-07-07T10:00:00Z");
        var state = AppState.CreateDefault(now);
        var project = state.Projects[0];

        var sectionResult = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "snap-section",
            "createSection",
            new { title = "Reliability / QA", projectId = project.Id.ToString("N") }),
            now);
        Assert(sectionResult.Success, "createSection should succeed for the snapshot test.");

        var taskResult = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "snap-task",
            "createTask",
            new { title = "Add regression test", sectionId = sectionResult.CreatedSectionId }),
            now);
        Assert(taskResult.Success, "createTask should succeed for the snapshot test.");

        var snapshot = WorkspaceSnapshotFactory.Create(state, now);
        var section = snapshot.Sections.SingleOrDefault(s => s.Id == sectionResult.CreatedSectionId);
        Assert(section is not null, "Snapshot should include the created section.");
        Assert(section!.Name == "Reliability / QA", "Snapshot section should carry the created name.");
        Assert(section.ProjectId == project.Id.ToString("N"), "Snapshot section should reference the project.");
        Assert(!section.IsProjectRoot, "A created workstream section must not be a project root.");

        var task = snapshot.Tasks.Single(t => t.Id == taskResult.CreatedTaskId);
        Assert(task.SectionId == sectionResult.CreatedSectionId,
            "Snapshot task created inside the section should carry that section id.");
    }

    private static void WorkspaceCommandCreateSubtaskPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-08T09:00:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var group = new GroupItem
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Backlog",
                SortOrder = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            state.Groups.Add(group);
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });

            var parentResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "parent-task", "createTask",
                new { title = "Parent task", sectionId = $"group:{group.Id:N}" }), now);
            Assert(parentResult.Success, "Parent task creation should succeed.");
            var parentId = Guid.Parse(parentResult.CreatedTaskId!);

            var subtaskResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "subtask", "createTask",
                new { title = "  Subtask under parent  ", parentTaskId = parentId.ToString("N") }), now);
            Assert(subtaskResult.Success, "Subtask creation with parentTaskId should succeed.");

            var subtaskId = Guid.Parse(subtaskResult.CreatedTaskId!);
            var loaded = store.Load();
            var subtask = loaded.Tasks.SingleOrDefault(t => t.Id == subtaskId);
            Assert(subtask is not null, "Created subtask should persist to state.json.");
            Assert(subtask!.Title == "Subtask under parent", "Subtask title should be trimmed and persisted.");
            Assert(subtask.ParentTaskId == parentId, "Subtask should reference the parent task.");
            Assert(subtask.ProjectId == project.Id, "Subtask should inherit the parent's project.");
            Assert(subtask.GroupId == group.Id, "Subtask should inherit the parent's section.");
        });
    }

    private static void WorkspaceCommandCreateSubtaskValidation()
    {
        var now = DateTimeOffset.Parse("2026-07-08T09:00:00Z");
        var state = AppState.CreateDefault(now);
        var initialCount = state.Tasks.Count;

        var malformed = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "subtask-bad-id", "createTask",
            new { title = "Orphan subtask", parentTaskId = "not-a-guid" }), now);
        Assert(!malformed.Success && malformed.ErrorCode == "invalidPayload",
            "Malformed parentTaskId should be an invalid payload.");

        var missingParent = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "subtask-missing-parent", "createTask",
            new { title = "Orphan subtask", parentTaskId = Guid.NewGuid().ToString("N") }), now);
        Assert(!missingParent.Success && missingParent.ErrorCode == "mutationRejected",
            "Unknown parentTaskId should be rejected as a mutation error.");

        Assert(state.Tasks.Count == initialCount, "Rejected subtask commands must not add tasks.");
    }

    private static void WorkspaceCommandCreateDraftTaskPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-08T09:10:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });

            var created = dispatcher.Dispatch(WorkspaceCommandJson(
                "draft-create", "createTask",
                new { title = "", draft = true, projectId = project.Id.ToString("N") }), now);
            Assert(created.Success, "Explicit draft task creation should accept an empty title.");

            var taskId = Guid.Parse(created.CreatedTaskId!);
            var persistedDraft = store.Load().Tasks.Single(t => t.Id == taskId);
            Assert(persistedDraft.Title == string.Empty && persistedDraft.IsDraft,
                "An empty Workspace draft should persist only while marked as a draft.");

            var renamed = dispatcher.Dispatch(WorkspaceCommandJson(
                "draft-title", "updateTaskTitle",
                new { taskId = taskId.ToString("N"), title = "Captured task" }), now);
            Assert(renamed.Success, "Giving a draft a real title should succeed.");
            var persistedTask = store.Load().Tasks.Single(t => t.Id == taskId);
            Assert(persistedTask.Title == "Captured task" && !persistedTask.IsDraft,
                "Saving a non-empty title should promote the draft to a normal task.");
        });
    }

    private static void WorkspaceCommandDeleteTaskPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-08T09:15:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });

            var created = dispatcher.Dispatch(WorkspaceCommandJson(
                "make-task", "createTask",
                new { title = "Disposable task", projectId = project.Id.ToString("N") }), now);
            Assert(created.Success, "Task creation should succeed before delete.");
            var taskId = Guid.Parse(created.CreatedTaskId!);
            Assert(store.Load().Tasks.Any(t => t.Id == taskId), "Task should exist before deletion.");

            var deleteResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "delete-task", "deleteTask", new { taskId = taskId.ToString("N") }), now);
            Assert(deleteResult.Success, "deleteTask should succeed for an existing task.");
            Assert(!store.Load().Tasks.Any(t => t.Id == taskId),
                "Deleted task should be removed from state.json.");

            var deleteMissing = dispatcher.Dispatch(WorkspaceCommandJson(
                "delete-missing", "deleteTask", new { taskId = Guid.NewGuid().ToString("N") }), now);
            Assert(!deleteMissing.Success && deleteMissing.ErrorCode == "taskNotFound",
                "Deleting a non-existent task should fail with taskNotFound.");
        });
    }

    private static void WorkspaceCommandDeleteTaskReparentsSubtasks()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-08T09:30:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });

            var parent = dispatcher.Dispatch(WorkspaceCommandJson(
                "rp-parent", "createTask",
                new { title = "Parent", projectId = project.Id.ToString("N") }), now);
            var parentId = Guid.Parse(parent.CreatedTaskId!);
            var child = dispatcher.Dispatch(WorkspaceCommandJson(
                "rp-child", "createTask",
                new { title = "Child", parentTaskId = parentId.ToString("N") }), now);
            var childId = Guid.Parse(child.CreatedTaskId!);

            var deleteParent = dispatcher.Dispatch(WorkspaceCommandJson(
                "rp-delete", "deleteTask", new { taskId = parentId.ToString("N") }), now);
            Assert(deleteParent.Success, "Deleting a parent task with subtasks should succeed.");

            var loaded = store.Load();
            Assert(!loaded.Tasks.Any(t => t.Id == parentId), "Deleted parent must be gone.");
            var reparented = loaded.Tasks.SingleOrDefault(t => t.Id == childId);
            Assert(reparented is not null, "Subtask must survive deletion of its parent (reparented, not cascaded).");
            Assert(reparented!.ParentTaskId is null, "Reparented subtask should move up to the project root.");
            Assert(reparented.ProjectId == project.Id, "Reparented subtask should stay in the project.");
        });
    }

    private static void WorkspaceCommandMoveTaskToSectionPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-08T09:45:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var group = new GroupItem
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Target section",
                SortOrder = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            state.Groups.Add(group);
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });

            var created = dispatcher.Dispatch(WorkspaceCommandJson(
                "mv-make", "createTask",
                new { title = "Movable task", projectId = project.Id.ToString("N") }), now);
            var taskId = Guid.Parse(created.CreatedTaskId!);
            Assert(store.Load().Tasks.Single(t => t.Id == taskId).GroupId is null,
                "Task should start at project root.");

            var moveResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "mv-section", "moveTask",
                new { taskId = taskId.ToString("N"), sectionId = $"group:{group.Id:N}" }), now);
            Assert(moveResult.Success, "moveTask to a section should succeed.");

            var moved = store.Load().Tasks.Single(t => t.Id == taskId);
            Assert(moved.GroupId == group.Id, "Moved task should join the target section.");
            Assert(moved.ProjectId == project.Id, "Moved task should stay in the project.");
        });
    }

    private static void WorkspaceCommandMoveTaskToProjectRootPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-08T10:00:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var group = new GroupItem
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Origin section",
                SortOrder = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            state.Groups.Add(group);
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });

            var created = dispatcher.Dispatch(WorkspaceCommandJson(
                "mvr-make", "createTask",
                new { title = "Task in section", sectionId = $"group:{group.Id:N}" }), now);
            var taskId = Guid.Parse(created.CreatedTaskId!);
            Assert(store.Load().Tasks.Single(t => t.Id == taskId).GroupId == group.Id,
                "Task should start in the section.");

            var moveResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "mvr-root", "moveTask",
                new { taskId = taskId.ToString("N"), sectionId = $"project:{project.Id:N}:root" }), now);
            Assert(moveResult.Success, "moveTask to project root should succeed.");

            var moved = store.Load().Tasks.Single(t => t.Id == taskId);
            Assert(moved.GroupId is null, "Moved task should sit at project root with no section.");
            Assert(moved.ProjectId == project.Id, "Moved task should stay in the project.");
        });
    }

    private static void WorkspaceCommandMoveTaskInvalidTarget()
    {
        var now = DateTimeOffset.Parse("2026-07-08T10:15:00Z");
        var state = AppState.CreateDefault(now);
        var project = state.Projects[0];
        var task = state.Tasks[0];

        var malformed = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "mv-bad", "moveTask",
            new { taskId = task.Id.ToString("N"), sectionId = "group:not-a-guid" }), now);
        Assert(!malformed.Success && malformed.ErrorCode == "invalidPayload",
            "Malformed section id should be an invalid payload.");

        var unknownGroup = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "mv-unknown", "moveTask",
            new { taskId = task.Id.ToString("N"), sectionId = $"group:{Guid.NewGuid():N}" }), now);
        Assert(!unknownGroup.Success && unknownGroup.ErrorCode == "mutationRejected",
            "Moving to a non-existent section should be rejected as a mutation error.");

        Assert(task.GroupId is null, "Rejected moveTask commands must not mutate the task.");
    }

    private static void WorkspaceCommandDoneClearsReminderAndDeadline()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-08T10:30:00Z");
            var state = AppState.CreateDefault(now);
            var task = state.Tasks[0];
            task.RemindAtUtc = now.AddHours(1);
            task.RemindEveryMinutes = 60;
            task.ReminderActive = true;
            task.DueAtUtc = now.AddDays(1);
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });

            var doneResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "done-clear", "updateTaskStatus",
                new { taskId = task.Id.ToString("N"), status = "DONE" }), now);
            Assert(doneResult.Success, "Marking a task DONE should succeed.");

            var loaded = store.Load().Tasks.Single(t => t.Id == task.Id);
            Assert(loaded.Status == TaskStatus.Done, "Task should be DONE after the command.");
            Assert(loaded.RemindAtUtc is null && loaded.RemindEveryMinutes is null,
                "DONE must clear the reminder schedule.");
            Assert(!loaded.ReminderActive, "DONE must clear the active reminder flag.");
            Assert(loaded.DueAtUtc is null, "DONE must clear the deadline (DueAtUtc).");
        });
    }

    private static void WorkspaceCommandCheckpointPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-09T09:00:00Z");
            var state = AppState.CreateDefault(now);
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });
            var taskId = task.Id.ToString("N");

            // Batch add (multiline paste shape) preserves input order and skips blank lines.
            var addResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-add", "addTaskCheckpoints",
                new { taskId, titles = new[] { "Collect numbers", "  ", "Check previous month", "Write summary" } }), now);
            Assert(addResult.Success, "Adding checkpoints should succeed.");
            var afterAdd = store.Load().Tasks.Single(t => t.Id == task.Id);
            Assert(afterAdd.Checkpoints is { Count: 3 }, "Three non-empty checkpoints should persist (blank line skipped).");
            Assert(afterAdd.Checkpoints![0].Title == "Collect numbers" &&
                   afterAdd.Checkpoints[1].Title == "Check previous month" &&
                   afterAdd.Checkpoints[2].Title == "Write summary",
                "Checkpoint order must match input order.");
            Assert(afterAdd.Checkpoints.Select(c => c.SortOrder).SequenceEqual(new[] { 0, 1, 2 }),
                "Checkpoint SortOrder must be renumbered 0..n-1.");
            Assert(afterAdd.Checkpoints.All(c => !c.Done && c.CompletedAtUtc is null),
                "New checkpoints start open without CompletedAtUtc.");

            // Toggle done sets CompletedAtUtc; toggling back clears it.
            var firstId = task.Checkpoints![0].Id.ToString("N");
            var toggleOn = dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-done", "toggleTaskCheckpoint", new { taskId, checkpointId = firstId, done = true }), now);
            Assert(toggleOn.Success, "Toggling a checkpoint done should succeed.");
            var afterToggle = store.Load().Tasks.Single(t => t.Id == task.Id).Checkpoints![0];
            Assert(afterToggle.Done && afterToggle.CompletedAtUtc == now,
                "Done checkpoint must persist with CompletedAtUtc set.");

            var toggleOff = dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-reopen", "toggleTaskCheckpoint", new { taskId, checkpointId = firstId, done = false }), now);
            Assert(toggleOff.Success, "Reopening a checkpoint should succeed.");
            var afterReopen = store.Load().Tasks.Single(t => t.Id == task.Id).Checkpoints![0];
            Assert(!afterReopen.Done && afterReopen.CompletedAtUtc is null,
                "Reopened checkpoint must persist with CompletedAtUtc cleared.");

            // Rename persists trimmed.
            var rename = dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-rename", "updateTaskCheckpointTitle",
                new { taskId, checkpointId = firstId, title = "  Collect all numbers  " }), now);
            Assert(rename.Success, "Renaming a checkpoint should succeed.");
            Assert(store.Load().Tasks.Single(t => t.Id == task.Id).Checkpoints![0].Title == "Collect all numbers",
                "Renamed checkpoint title should persist trimmed.");

            // Delete removes the item and renumbers the rest.
            var delete = dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-delete", "deleteTaskCheckpoint", new { taskId, checkpointId = firstId }), now);
            Assert(delete.Success, "Deleting a checkpoint should succeed.");
            var afterDelete = store.Load().Tasks.Single(t => t.Id == task.Id).Checkpoints!;
            Assert(afterDelete.Count == 2 &&
                   afterDelete[0].Title == "Check previous month" &&
                   afterDelete.Select(c => c.SortOrder).SequenceEqual(new[] { 0, 1 }),
                "Delete must persist and renumber remaining checkpoints.");

            // Unrelated task fields must be untouched by checkpoint commands.
            Assert(afterDelete[0].Id != Guid.Empty, "Persisted checkpoints keep their ids.");
            var reloaded = store.Load().Tasks.Single(t => t.Id == task.Id);
            Assert(reloaded.Status == TaskStatus.Todo && reloaded.RemindAtUtc is null && reloaded.DueAtUtc is null,
                "Checkpoint commands must not mutate status/reminder/deadline.");
        });
    }

    private static void WorkspaceCommandCheckpointReorderPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-09T09:30:00Z");
            var state = AppState.CreateDefault(now);
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });
            var taskId = task.Id.ToString("N");

            dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-seed", "addTaskCheckpoints",
                new { taskId, titles = new[] { "A", "B", "C" } }), now);
            var lastId = task.Checkpoints![2].Id.ToString("N");

            var moveUp = dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-move", "reorderTaskCheckpoint",
                new { taskId, checkpointId = lastId, targetIndex = 0 }), now);
            Assert(moveUp.Success, "Reordering a checkpoint should succeed.");
            var reordered = store.Load().Tasks.Single(t => t.Id == task.Id).Checkpoints!;
            Assert(reordered.Select(c => c.Title).SequenceEqual(new[] { "C", "A", "B" }),
                "Reorder must persist the new order.");
            Assert(reordered.Select(c => c.SortOrder).SequenceEqual(new[] { 0, 1, 2 }),
                "Reorder must renumber SortOrder 0..n-1.");

            // Out-of-range target index clamps instead of failing.
            var clamp = dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-clamp", "reorderTaskCheckpoint",
                new { taskId, checkpointId = lastId, targetIndex = 99 }), now);
            Assert(clamp.Success, "Over-large targetIndex should clamp, not fail.");
            var clamped = store.Load().Tasks.Single(t => t.Id == task.Id).Checkpoints!;
            Assert(clamped.Select(c => c.Title).SequenceEqual(new[] { "A", "B", "C" }),
                "Clamped reorder should move the checkpoint to the end.");
        });
    }

    private static void WorkspaceCommandCheckpointValidation()
    {
        var now = DateTimeOffset.Parse("2026-07-09T10:00:00Z");
        var state = AppState.CreateDefault(now);
        var task = state.Tasks[0];
        var taskId = task.Id.ToString("N");
        CheckpointService.Add(task, new[] { "Only step" }, now);
        var checkpointId = task.Checkpoints![0].Id.ToString("N");

        var unknownTask = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "cp-bad-task", "toggleTaskCheckpoint",
            new { taskId = Guid.NewGuid().ToString("N"), checkpointId, done = true }), now);
        Assert(!unknownTask.Success && unknownTask.ErrorCode == "taskNotFound",
            "Unknown taskId must be rejected.");

        var unknownCheckpoint = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "cp-bad-id", "toggleTaskCheckpoint",
            new { taskId, checkpointId = Guid.NewGuid().ToString("N"), done = true }), now);
        Assert(!unknownCheckpoint.Success && unknownCheckpoint.ErrorCode == "checkpointNotFound",
            "Unknown checkpointId must be rejected.");

        var invalidCheckpointId = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "cp-invalid-id", "deleteTaskCheckpoint",
            new { taskId, checkpointId = "not-a-guid" }), now);
        Assert(!invalidCheckpointId.Success && invalidCheckpointId.ErrorCode == "invalidCheckpointId",
            "Malformed checkpointId must be rejected.");

        var emptyBatch = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "cp-empty-batch", "addTaskCheckpoints",
            new { taskId, titles = new[] { "   ", "" } }), now);
        Assert(!emptyBatch.Success && emptyBatch.ErrorCode == "invalidPayload",
            "A batch with no non-empty titles must be rejected.");

        var emptyRename = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "cp-empty-title", "updateTaskCheckpointTitle",
            new { taskId, checkpointId, title = "   " }), now);
        Assert(!emptyRename.Success && emptyRename.ErrorCode == "invalidPayload",
            "Blank rename must be rejected.");

        var badTitles = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "cp-bad-titles", "addTaskCheckpoints",
            new { taskId, titles = "not-an-array" }), now);
        Assert(!badTitles.Success && badTitles.ErrorCode == "invalidPayload",
            "Non-array titles must be rejected.");

        var badIndex = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "cp-bad-index", "reorderTaskCheckpoint",
            new { taskId, checkpointId, targetIndex = -1 }), now);
        Assert(!badIndex.Success && badIndex.ErrorCode == "invalidPayload",
            "Negative targetIndex must be rejected.");

        Assert(task.Checkpoints.Count == 1 && task.Checkpoints[0].Title == "Only step" && !task.Checkpoints[0].Done,
            "Rejected checkpoint commands must not mutate checkpoints.");
    }

    private static void WorkspaceCheckpointSnapshotAndSafeLoad()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-09T11:00:00Z");
            var state = AppState.CreateDefault(now);
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);

            // A state saved before checkpoints existed loads with null Checkpoints
            // and must round-trip safely (null and empty both mean "no steps").
            store.Save(state);
            var legacyLoaded = store.Load();
            Assert(legacyLoaded.Tasks.All(t => t.Checkpoints is null),
                "State without checkpoints must load with null Checkpoints.");
            var legacySnapshot = WorkspaceSnapshotFactory.Create(legacyLoaded, now);
            Assert(legacySnapshot.Tasks.All(t => t.Checkpoints is { Count: 0 }),
                "Snapshot must expose an empty checkpoint list for tasks without steps.");

            // Snapshot carries checkpoints in stable order with done flags.
            CheckpointService.Add(task, new[] { "First", "Second" }, now);
            CheckpointService.Toggle(task, task.Checkpoints![1].Id, done: true, now);
            var snapshot = WorkspaceSnapshotFactory.Create(state, now);
            var snapshotTask = snapshot.Tasks.Single(t => t.Id == task.Id.ToString("N"));
            Assert(snapshotTask.Checkpoints is { Count: 2 } &&
                   snapshotTask.Checkpoints[0].Title == "First" && !snapshotTask.Checkpoints[0].Done &&
                   snapshotTask.Checkpoints[1].Title == "Second" && snapshotTask.Checkpoints[1].Done &&
                   snapshotTask.Checkpoints[1].CompletedAtUtc == now,
                "Snapshot must carry ordered checkpoints with done state and CompletedAtUtc.");

            // Repair normalizes corrupted checkpoint data: empty titles dropped,
            // stale CompletedAtUtc cleared, SortOrder renumbered.
            task.Checkpoints.Add(new CheckpointItem { Title = "   ", SortOrder = 5 });
            task.Checkpoints[0].SortOrder = 40;
            task.Checkpoints[0].CompletedAtUtc = now; // not done — stale timestamp
            var repaired = StateMigrator.RepairCurrentState(state);
            Assert(repaired, "Repair must report checkpoint normalization changes.");
            Assert(task.Checkpoints.Count == 2 &&
                   task.Checkpoints.Select(c => c.SortOrder).SequenceEqual(new[] { 0, 1 }) &&
                   task.Checkpoints.All(c => c.Done || c.CompletedAtUtc is null),
                "Repair must drop empty steps, renumber order, and clear stale CompletedAtUtc.");
        });
    }

    private static void WorkspaceCheckpointIndependentOfParentDone()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-09T12:00:00Z");
            var state = AppState.CreateDefault(now);
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });
            var taskId = task.Id.ToString("N");

            dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-mixed", "addTaskCheckpoints",
                new { taskId, titles = new[] { "Done step", "Open step" } }), now);
            dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-mixed-done", "toggleTaskCheckpoint",
                new { taskId, checkpointId = task.Checkpoints![0].Id.ToString("N"), done = true }), now);

            // Marking the parent DONE must leave checkpoint states exactly as-is
            // (unlike reminder/deadline, which DONE clears by design).
            var doneResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-parent-done", "updateTaskStatus",
                new { taskId, status = "DONE" }), now);
            Assert(doneResult.Success, "Marking the parent DONE should succeed.");
            var afterDone = store.Load().Tasks.Single(t => t.Id == task.Id);
            Assert(afterDone.Status == TaskStatus.Done, "Parent should be DONE.");
            Assert(afterDone.Checkpoints is { Count: 2 } &&
                   afterDone.Checkpoints![0].Done && !afterDone.Checkpoints[1].Done,
                "Parent DONE must not mutate checkpoint states.");

            // Reopening the parent must also preserve checkpoint states.
            var reopen = dispatcher.Dispatch(WorkspaceCommandJson(
                "cp-parent-reopen", "updateTaskStatus",
                new { taskId, status = "TODO" }), now);
            Assert(reopen.Success, "Reopening the parent should succeed.");
            var afterReopen = store.Load().Tasks.Single(t => t.Id == task.Id);
            Assert(afterReopen.Checkpoints is { Count: 2 } &&
                   afterReopen.Checkpoints![0].Done && !afterReopen.Checkpoints[1].Done,
                "Reopening the parent must not reset completed checkpoints.");
        });
    }

    private static void WorkspaceCommandCreateTaskValidation()
    {
        var now = DateTimeOffset.Parse("2026-07-06T09:00:00Z");
        var state = AppState.CreateDefault(now);
        var project = state.Projects[0];
        var initialCount = state.Tasks.Count;

        var emptyTitle = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "create-empty", "createTask", new { title = "   ", projectId = project.Id.ToString("N") }), now);
        Assert(!emptyTitle.Success && emptyTitle.ErrorCode == "invalidPayload", "Blank title should be rejected.");

        var badProject = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "create-bad-project", "createTask", new { title = "Valid title", projectId = Guid.NewGuid().ToString("N") }), now);
        Assert(!badProject.Success, "Unknown projectId should be rejected.");

        var missingLocation = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "create-missing-location", "createTask", new { title = "Valid title" }), now);
        Assert(!missingLocation.Success && missingLocation.ErrorCode == "invalidPayload",
            "createTask without projectId or sectionId should be rejected.");

        Assert(state.Tasks.Count == initialCount, "Rejected createTask commands must not add tasks.");
    }

    private static void WorkspaceCommandWaitingForPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = AppState.CreateDefault();
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () => store.Save(state),
                () => { });

            var result = dispatcher.Dispatch(WorkspaceCommandJson(
                "wait-1", "updateTaskWaitingFor", new { taskId = task.Id.ToString("N"), waitingFor = "  reply from Madina  " }));
            Assert(result.Success, "updateTaskWaitingFor should succeed.");
            Assert(store.Load().Tasks.Single(t => t.Id == task.Id).WaitingFor == "reply from Madina",
                "waitingFor should persist trimmed through state.json.");

            var clearResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "wait-clear", "updateTaskWaitingFor", new { taskId = task.Id.ToString("N"), waitingFor = "" }));
            Assert(clearResult.Success, "Clearing waitingFor should succeed.");
            Assert(store.Load().Tasks.Single(t => t.Id == task.Id).WaitingFor == "",
                "Cleared waitingFor should persist as empty string.");
        });
    }

    private static void WorkspaceCommandReminderPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-06T08:00:00Z");
            var state = AppState.CreateDefault(now);
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () => store.Save(state),
                () => { });

            var remindAt = "2026-07-07T09:30:00Z";
            var setResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "rem-set", "updateTaskReminder", new { taskId = task.Id.ToString("N"), remindAtUtc = remindAt, remindEveryMinutes = (int?)null }), now);
            Assert(setResult.Success, "Setting an explicit reminder should succeed.");
            var afterSet = store.Load().Tasks.Single(t => t.Id == task.Id);
            Assert(afterSet.RemindAtUtc == DateTimeOffset.Parse(remindAt), "Reminder time should persist through state.json.");
            Assert(!afterSet.ReminderActive, "A freshly scheduled reminder must not be active/noisy immediately.");

            var clearResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "rem-clear", "updateTaskReminder", new { taskId = task.Id.ToString("N"), remindAtUtc = (string?)null }), now);
            Assert(clearResult.Success, "Clearing a reminder should succeed.");
            Assert(store.Load().Tasks.Single(t => t.Id == task.Id).RemindAtUtc is null,
                "Cleared reminder should persist as null.");

            // Workspace Reminder editor's Repeat toggle (every2h/daily/weekly) pushes a positive
            // remindEveryMinutes alongside remindAtUtc — only null/negative repeat values were
            // previously covered here.
            var repeatRemindAt = "2026-07-07T09:00:00Z";
            var repeatResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "rem-repeat-set", "updateTaskReminder",
                new { taskId = task.Id.ToString("N"), remindAtUtc = repeatRemindAt, remindEveryMinutes = 10080 }), now);
            Assert(repeatResult.Success, "Setting a weekly-repeating reminder should succeed.");
            var afterRepeatSet = store.Load().Tasks.Single(t => t.Id == task.Id);
            Assert(afterRepeatSet.RemindAtUtc == DateTimeOffset.Parse(repeatRemindAt),
                "Repeating reminder's next occurrence should persist through state.json.");
            Assert(afterRepeatSet.RemindEveryMinutes == 10080,
                "Weekly repeat cadence (10080 minutes) should persist through state.json.");

            var repeatOffResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "rem-repeat-off", "updateTaskReminder",
                new { taskId = task.Id.ToString("N"), remindAtUtc = repeatRemindAt, remindEveryMinutes = (int?)null }), now);
            Assert(repeatOffResult.Success, "Turning repeat off while keeping the reminder should succeed.");
            var afterRepeatOff = store.Load().Tasks.Single(t => t.Id == task.Id);
            Assert(afterRepeatOff.RemindAtUtc == DateTimeOffset.Parse(repeatRemindAt),
                "Turning repeat off must not clear the reminder's own time.");
            Assert(afterRepeatOff.RemindEveryMinutes is null,
                "Turning repeat off should clear the repeat cadence.");
        });
    }

    private static void WorkspaceCommandReminderValidation()
    {
        var now = DateTimeOffset.Parse("2026-07-06T08:00:00Z");
        var state = AppState.CreateDefault(now);
        var task = state.Tasks[0];

        var badTimestamp = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "rem-bad", "updateTaskReminder", new { taskId = task.Id.ToString("N"), remindAtUtc = "not-a-date" }), now);
        Assert(!badTimestamp.Success && badTimestamp.ErrorCode == "invalidPayload", "Non-ISO reminder timestamp should be rejected.");

        var badRepeat = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "rem-bad-repeat", "updateTaskReminder", new { taskId = task.Id.ToString("N"), remindAtUtc = "2026-07-07T09:00:00Z", remindEveryMinutes = -5 }), now);
        Assert(!badRepeat.Success && badRepeat.ErrorCode == "invalidPayload", "Non-positive repeat interval should be rejected.");

        Assert(task.RemindAtUtc is null, "Rejected reminder commands must not mutate the task.");

        // DONE tasks should not accumulate active REMIND noise even if a reminder is scheduled.
        task.Status = TaskStatus.Done;
        var scheduledWhileDone = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "rem-done", "updateTaskReminder", new { taskId = task.Id.ToString("N"), remindAtUtc = "2026-07-07T09:00:00Z" }), now);
        Assert(scheduledWhileDone.Success, "Scheduling a reminder on a done task is not itself rejected.");
        Assert(task.RemindAtUtc is null, "ReminderService must clear the schedule for a DONE task.");
    }

    private static void WorkspaceCommandDeadlinePersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-06T08:00:00Z");
            var state = AppState.CreateDefault(now);
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(
                state,
                () => store.Save(state),
                () => { });

            var deadlineAt = "2026-07-10T23:59:00Z";
            var setResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "dl-set", "updateTaskDeadline", new { taskId = task.Id.ToString("N"), deadlineAtUtc = deadlineAt }), now);
            Assert(setResult.Success, "Setting a deadline should succeed.");
            Assert(store.Load().Tasks.Single(t => t.Id == task.Id).DueAtUtc == DateTimeOffset.Parse(deadlineAt),
                "Deadline should persist through state.json.");

            var clearResult = dispatcher.Dispatch(WorkspaceCommandJson(
                "dl-clear", "updateTaskDeadline", new { taskId = task.Id.ToString("N"), deadlineAtUtc = (string?)null }), now);
            Assert(clearResult.Success, "Clearing a deadline should succeed.");
            Assert(store.Load().Tasks.Single(t => t.Id == task.Id).DueAtUtc is null,
                "Cleared deadline should persist as null.");

            // Reminder must remain a separate field from Deadline.
            Assert(store.Load().Tasks.Single(t => t.Id == task.Id).RemindAtUtc is null,
                "Setting/clearing Deadline must not affect RemindAtUtc.");
        });
    }

    private static void WorkspaceLegacyStateWithoutMeetings()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = AppState.CreateDefault(DateTimeOffset.Parse("2026-07-11T08:00:00Z"));
            var store = new AppStateStore(directory);
            store.Save(state);
            var root = JsonNode.Parse(File.ReadAllText(store.StatePath))!.AsObject();
            root.Remove("meetings");
            File.WriteAllText(store.StatePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var loaded = store.Load();
            Assert(loaded.Meetings is { Count: 0 },
                "Legacy state without meetings must load as an empty meeting list.");
        });
    }

    private static void WorkspaceMeetingCommandPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = DateTimeOffset.Parse("2026-07-11T09:00:00Z");
            var state = AppState.CreateDefault(now);
            var project = state.Projects[0];
            var task = state.Tasks[0];
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });

            var create = dispatcher.Dispatch(WorkspaceCommandJson(
                "meet-create", "createMeeting", new
                {
                    projectId = project.Id.ToString("N"),
                    title = "  Project sync  ",
                    startsAtUtc = "2026-07-12T10:30:00Z",
                    durationMinutes = 30,
                    notes = "Agenda",
                    location = "Room 4",
                    link = "https://example.test/meet",
                    linkedTaskId = task.Id.ToString("N")
                }), now);
            Assert(create.Success && Guid.TryParse(create.CreatedMeetingId, out _),
                "createMeeting should return the created meeting id.");
            var meetingId = Guid.Parse(create.CreatedMeetingId!);
            var created = store.Load().Meetings.Single(meeting => meeting.Id == meetingId);
            Assert(created.Title == "Project sync" && created.DurationMinutes == 30 &&
                   created.LinkedTaskId == task.Id,
                "createMeeting should normalize and persist meeting fields.");

            var update = dispatcher.Dispatch(WorkspaceCommandJson(
                "meet-update", "updateMeeting", new
                {
                    meetingId = meetingId.ToString("N"),
                    title = "Updated sync",
                    startsAtUtc = "2026-07-13T11:00:00Z",
                    durationMinutes = 60,
                    notes = "Updated agenda",
                    location = "Online",
                    link = (string?)null,
                    linkedTaskId = (string?)null
                }), now.AddMinutes(1));
            Assert(update.Success, "updateMeeting should accept an allowed partial patch.");
            var updated = store.Load().Meetings.Single(meeting => meeting.Id == meetingId);
            Assert(updated.Title == "Updated sync" && updated.DurationMinutes == 60 &&
                   updated.ProjectId == project.Id && updated.LinkedTaskId is null && updated.Link == string.Empty,
                "updateMeeting should preserve omitted fields and persist patched fields.");

            var delete = dispatcher.Dispatch(WorkspaceCommandJson(
                "meet-delete", "deleteMeeting", new { meetingId = meetingId.ToString("N") }), now.AddMinutes(2));
            Assert(delete.Success && store.Load().Meetings.Count == 0,
                "deleteMeeting should persist removal.");
        });
    }

    private static void WorkspaceMeetingValidationAndRepair()
    {
        var now = DateTimeOffset.Parse("2026-07-11T10:00:00Z");
        var state = AppState.CreateDefault(now);
        var initialCount = state.Meetings.Count;
        var invalidProject = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "meet-bad-project", "createMeeting", new
            {
                projectId = Guid.NewGuid().ToString("N"),
                title = "Invalid",
                startsAtUtc = "2026-07-12T10:00:00Z",
                durationMinutes = 30
            }), now);
        var invalidTask = WorkspaceCommandProcessor.Execute(state, WorkspaceCommandJson(
            "meet-bad-task", "createMeeting", new
            {
                projectId = state.Projects[0].Id.ToString("N"),
                title = "Invalid link",
                startsAtUtc = "2026-07-12T10:00:00Z",
                durationMinutes = 30,
                linkedTaskId = Guid.NewGuid().ToString("N")
            }), now);
        Assert(!invalidProject.Success && !invalidTask.Success && state.Meetings.Count == initialCount,
            "Invalid project/task references must not create meetings.");

        state.Meetings.Add(new MeetingItem
        {
            ProjectId = Guid.NewGuid(),
            Title = "  Repair me  ",
            StartsAtUtc = now,
            DurationMinutes = -5,
            LinkedTaskId = Guid.NewGuid()
        });
        state.Meetings.Add(new MeetingItem { Title = "   ", StartsAtUtc = now });
        Assert(StateMigrator.RepairCurrentState(state), "Corrupt meetings should be repaired.");
        Assert(state.Meetings.Count == 1 &&
               state.Meetings[0].ProjectId == state.Projects[0].Id &&
               state.Meetings[0].Title == "Repair me" &&
               state.Meetings[0].DurationMinutes == MeetingItem.DefaultDurationMinutes &&
               state.Meetings[0].LinkedTaskId is null,
            "Repair should remove invalid drafts and normalize safe meeting fields.");
    }

    private static void WorkspaceMeetingSnapshotAndLinkedTaskCleanup()
    {
        var now = DateTimeOffset.Parse("2026-07-11T11:00:00Z");
        var state = AppState.CreateDefault(now);
        var project = state.Projects[0];
        var task = state.Tasks[0];
        var service = new MeetingService(state);
        var later = service.Create(new MeetingUpdate(
            project.Id, "Later", null, now.AddHours(2), 60, "Online", null, null), now)!;
        var earlier = service.Create(new MeetingUpdate(
            project.Id, "Earlier", "Agenda", now.AddHours(1), 30, "Room", null, task.Id), now)!;

        var snapshot = WorkspaceSnapshotFactory.Create(state, now, WorkspaceSnapshotFactory.ConnectedMode);
        Assert(snapshot.Meetings.Select(meeting => meeting.Id).SequenceEqual(
                new[] { earlier.Id.ToString("N"), later.Id.ToString("N") }) &&
               snapshot.TimelineItems.Any(item =>
                   item.Kind == "MEET" && item.LinkedMeetingId == earlier.Id.ToString("N")),
            "Snapshot should include ordered meetings and linked MEET timeline rows.");

        Assert(new TreeStateService(state).DeleteNode(task.Id, now.AddMinutes(1)),
            "Linked task deletion should succeed.");
        Assert(earlier.LinkedTaskId is null,
            "Deleting a linked task should clear the meeting link without deleting the meeting.");
    }

    private static void WorkspaceActiveNowCollapsedPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = AppState.CreateDefault(DateTimeOffset.Parse("2026-07-11T12:00:00Z"));
            var store = new AppStateStore(directory);
            var dispatcher = new WorkspaceCommandDispatcher(state, () => store.Save(state), () => { });
            var result = dispatcher.Dispatch(WorkspaceCommandJson(
                "active-now-collapse", "updateWorkspaceContext", new
                {
                    activeTab = "tree",
                    selectedProjectIds = new[] { state.Projects[0].Id.ToString("N") },
                    selectedTaskId = (string?)null,
                    selectedTimelineItemId = (string?)null,
                    selectedWorkstreamId = (string?)null,
                    filter = "all",
                    activeNowCollapsed = true
                }));
            Assert(result.Success && store.Load().WorkspaceSettings.ActiveNowCollapsed,
                "Active Now collapsed state should persist through Workspace context.");
            Assert(WorkspaceSnapshotFactory.Create(store.Load()).Context.ActiveNowCollapsed,
                "Workspace snapshot should restore Active Now collapsed state.");
        });
    }

    private static string WorkspaceCommandJson(
        string commandId,
        string type,
        object payload) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = WorkspaceCommandProcessor.CurrentSchemaVersion,
            commandId,
            type,
            payload
        });

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
