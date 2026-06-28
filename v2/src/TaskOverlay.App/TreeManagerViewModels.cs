using System;
using System.Windows;
using System.Windows.Media;
using TaskOverlay.Core;

namespace TaskOverlay.App;

internal sealed class TreeProjectViewModel
{
    public TreeProjectViewModel(ProjectItem project, int activeCount)
    {
        Project = project;
        ActiveCount = activeCount;
    }

    public ProjectItem Project { get; }
    public Guid Id => Project.Id;
    public string Name => Project.Name;
    public int ActiveCount { get; }
    public Visibility ActiveCountVisibility => ActiveCount > 0
        ? Visibility.Visible
        : Visibility.Collapsed;
}

internal sealed class TreeNodeRowViewModel
{
    private static readonly Brush TodoBrush = CreateBrush("#FF9AA4B2");
    private static readonly Brush FocusBrush = CreateBrush("#FF45D58A");
    private static readonly Brush WaitBrush = CreateBrush("#FF51B5DB");
    private static readonly Brush RemindBrush = CreateBrush("#FFF1B94E");
    private static readonly Brush DoneBrush = CreateBrush("#FF737B87");

    public TreeNodeRowViewModel(
        TreeNode node,
        TreeNodeKind? parentKind,
        int depth,
        bool hasChildren,
        bool isExpanded,
        bool isReminder,
        bool canMoveUp,
        bool canMoveDown,
        string waitingFor)
    {
        Node = node;
        ParentKind = parentKind;
        Depth = Math.Max(0, depth);
        HasChildren = hasChildren;
        IsExpanded = isExpanded;
        IsReminder = isReminder;
        CanMoveUp = canMoveUp;
        CanMoveDown = canMoveDown;
        WaitingFor = waitingFor;
    }

    public TreeNode Node { get; }
    public TreeNodeKind? ParentKind { get; }
    public Guid Id => Node.Id;
    public int Depth { get; }
    public Thickness IndentMargin => new(Depth * 18, 0, 0, 0);
    public string Title => Node.Title;
    public bool HasChildren { get; }
    public bool IsExpanded { get; }
    public string ExpandGlyph => IsExpanded ? "−" : "+";
    public Visibility ExpandVisibility => HasChildren
        ? Visibility.Visible
        : Visibility.Hidden;
    public bool IsTask => Node.Kind == TreeNodeKind.Task;
    public bool IsSection => Node.Kind == TreeNodeKind.Group;
    public bool IsDone => Node.Status == TreeNodeStatus.Done;
    public bool IsReminder { get; }
    public bool CanMoveUp { get; }
    public bool CanMoveDown { get; }
    public string WaitingFor { get; }
    public string WaitingForLabel => Node.Status == TreeNodeStatus.Wait &&
                                     !string.IsNullOrWhiteSpace(WaitingFor)
        ? $"Waiting for {WaitingFor}"
        : string.Empty;
    public Visibility WaitingForVisibility => string.IsNullOrEmpty(WaitingForLabel)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public string KindLabel => Node.Kind switch
    {
        TreeNodeKind.Group => "SECTION",
        TreeNodeKind.Task when ParentKind == TreeNodeKind.Task => "SUBTASK",
        TreeNodeKind.Task => "TASK",
        _ => "PROJECT"
    };
    public string StatusLabel => IsReminder
        ? "REMIND"
        : Node.Status switch
        {
            TreeNodeStatus.Focus => "FOCUS",
            TreeNodeStatus.Wait => "WAIT",
            TreeNodeStatus.Done => "DONE",
            _ => "TODO"
        };
    public Brush StatusBrush => IsReminder
        ? RemindBrush
        : Node.Status switch
        {
            TreeNodeStatus.Focus => FocusBrush,
            TreeNodeStatus.Wait => WaitBrush,
            TreeNodeStatus.Done => DoneBrush,
            _ => TodoBrush
        };

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

internal sealed class TreeActiveTaskViewModel
{
    public TreeActiveTaskViewModel(
        TaskItem task,
        string projectName,
        string statusLabel,
        Brush statusBrush)
    {
        Task = task;
        ProjectName = projectName;
        StatusLabel = statusLabel;
        StatusBrush = statusBrush;
    }

    public TaskItem Task { get; }
    public string Title => Task.Title;
    public string ProjectName { get; }
    public string StatusLabel { get; }
    public Brush StatusBrush { get; }
}
