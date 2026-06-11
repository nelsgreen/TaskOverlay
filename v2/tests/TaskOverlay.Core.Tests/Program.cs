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
