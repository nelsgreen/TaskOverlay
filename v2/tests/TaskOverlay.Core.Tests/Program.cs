using System;
using System.IO;
using System.Linq;
using TaskOverlay.Core;

namespace TaskOverlay.Core.Tests;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("default state creation", DefaultStateCreation),
            ("save/load roundtrip", SaveLoadRoundtrip),
            ("collapsed setting persistence", CollapsedSettingPersistence),
            ("old state collapsed default", OldStateCollapsedDefault),
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
            Assert(state.Tasks.All(task => task.Id != Guid.Empty), "Seed tasks need stable IDs.");
            Assert(state.Tasks.Select(task => task.Id).Distinct().Count() == 3, "Seed task IDs must be unique.");
            Assert(
                !state.OverlaySettings.CollapsedMode,
                "Collapsed mode should be disabled for new state.");
        });
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
            task.InWork = true;
            task.DueAtUtc = DateTimeOffset.UtcNow.AddHours(2);
            task.Completed = true;
            task.CompletedAtUtc = DateTimeOffset.UtcNow;
            state.WindowPlacement.Left = 123.5;
            state.WindowPlacement.Top = 456.5;

            store.Save(state);
            var loaded = new AppStateStore(directory).Load();
            var loadedTask = loaded.Tasks.Single(item => item.Id == task.Id);

            Assert(loadedTask.Description == task.Description, "Description did not roundtrip.");
            Assert(loadedTask.Priority == TaskPriority.High, "Priority did not roundtrip.");
            Assert(loadedTask.InWork, "In-work flag did not roundtrip.");
            Assert(loadedTask.Completed, "Completed flag did not roundtrip.");
            Assert(loadedTask.CompletedAtUtc is not null, "Completed timestamp did not roundtrip.");
            Assert(loadedTask.DueAtUtc is not null, "Due timestamp did not roundtrip.");
            Assert(loaded.WindowPlacement.Left == 123.5, "Window left did not roundtrip.");
            Assert(loaded.WindowPlacement.Top == 456.5, "Window top did not roundtrip.");
            Assert(File.Exists(store.BackupPath), "Overwriting state should create a backup.");
        });
    }

    private static void CollapsedSettingPersistence()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new AppStateStore(directory);
            var state = store.Load();
            state.OverlaySettings.CollapsedMode = true;

            store.Save(state);

            var json = File.ReadAllText(store.StatePath);
            var loaded = new AppStateStore(directory).Load();

            Assert(
                json.Contains("\"collapsedMode\": true"),
                "Collapsed mode should be serialized in overlay settings.");
            Assert(
                loaded.OverlaySettings.CollapsedMode,
                "Collapsed mode should survive a save/load roundtrip.");
        });
    }

    private static void OldStateCollapsedDefault()
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
                !loaded.OverlaySettings.CollapsedMode,
                "Old state files should default collapsed mode to false.");
            Assert(
                Directory.GetFiles(directory, "state.corrupt.*.json").Length == 0,
                "A missing collapsed setting should not mark old state as corrupted.");
        });
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
        Assert(
            task.CreatedAtUtc == expectedCreatedAtUtc,
            "Created task should have the expected UTC timestamp.");
        Assert(task.CompletedAtUtc is null, "Completed timestamp should be empty.");
        Assert(task.DueAtUtc is null, "Due time should be empty.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
