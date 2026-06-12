using TaskOverlay.Core;

namespace TaskOverlay.App;

internal sealed class TaskRowViewModel
{
    public TaskRowViewModel(TaskItem task, bool activeMode)
    {
        Task = task;
        ShowDescription =
            activeMode &&
            !string.IsNullOrWhiteSpace(task.Description) &&
            (task.DescriptionExpanded || task.InWork);
    }

    public TaskItem Task { get; }
    public string Title => Task.Title;
    public string Description => Task.Description;
    public bool InWork => Task.InWork;
    public bool ShowDescription { get; }
}
