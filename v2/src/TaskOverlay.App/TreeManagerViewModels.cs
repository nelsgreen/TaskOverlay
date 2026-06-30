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

    public override string ToString() => Name;
}

internal sealed class TreeNodeRowViewModel
{
    private static readonly Brush TodoBrush = CreateBrush("#FF9AA4B2");
    private static readonly Brush FocusBrush = CreateBrush("#FF45D58A");
    private static readonly Brush WaitBrush = CreateBrush("#FF51B5DB");
    private static readonly Brush RemindBrush = CreateBrush("#FFF1B94E");
    private static readonly Brush DoneBrush = CreateBrush("#FF737B87");
    private static readonly Brush PinnedBrush = CreateBrush("#FFA79AF4");
    private static readonly Brush UnpinnedBrush = CreateBrush("#FF69727C");
    private static readonly Brush PinnedBackgroundBrush = CreateBrush("#FF302A48");

    public TreeNodeRowViewModel(
        TreeNode node,
        int depth,
        int childCount,
        bool isExpanded,
        bool isReminder,
        bool canMoveUp,
        bool canMoveDown,
        string waitingFor,
        bool isPinnedToPanel)
    {
        Node = node;
        Depth = Math.Max(0, depth);
        ChildCount = Math.Max(0, childCount);
        IsExpanded = isExpanded;
        IsReminder = isReminder;
        CanMoveUp = canMoveUp;
        CanMoveDown = canMoveDown;
        WaitingFor = waitingFor;
        IsPinnedToPanel = isPinnedToPanel;
    }

    public TreeNode Node { get; }
    public Guid Id => Node.Id;
    public int Depth { get; }
    public double IndentWidth => Depth * 22;
    public Visibility GuideVisibility => Depth > 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string Title => Node.Title;
    public int ChildCount { get; }
    public bool HasChildren => ChildCount > 0;
    public bool IsExpanded { get; }
    public string ExpandGlyph => IsExpanded ? "\u25BE" : "\u25B8";
    public Visibility ExpandVisibility => HasChildren
        ? Visibility.Visible
        : Visibility.Hidden;
    public bool IsTask => Node.Kind == TreeNodeKind.Task;
    public bool IsSection => Node.Kind == TreeNodeKind.Group;
    public Visibility TaskVisibility => IsTask
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility SectionVisibility => IsSection
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string SectionSummary => ChildCount == 1
        ? "1 item"
        : $"{ChildCount} items";
    public bool IsDone => Node.Status == TreeNodeStatus.Done;
    public bool IsReminder { get; }
    public bool CanMoveUp { get; }
    public bool CanMoveDown { get; }
    public string WaitingFor { get; }
    public bool IsPinnedToPanel { get; }
    public string PinToolTip => IsPinnedToPanel ? "Remove from panel" : "Pin to panel";
    public Brush PinBrush => IsPinnedToPanel ? PinnedBrush : UnpinnedBrush;
    public Brush PinFill => IsPinnedToPanel ? PinnedBrush : Brushes.Transparent;
    public Brush PinStroke => IsPinnedToPanel ? PinnedBrush : UnpinnedBrush;
    public Brush PinBackground => IsPinnedToPanel ? PinnedBackgroundBrush : Brushes.Transparent;
    public Visibility PanelBadgeVisibility => IsPinnedToPanel
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string WaitingForLabel => Node.Status == TreeNodeStatus.Wait &&
                                     !string.IsNullOrWhiteSpace(WaitingFor)
        ? $"Waiting for {WaitingFor}"
        : string.Empty;
    public Visibility WaitingForVisibility => string.IsNullOrEmpty(WaitingForLabel)
        ? Visibility.Collapsed
        : Visibility.Visible;
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

internal sealed class TreeStatusRowViewModel
{
    private static readonly Brush TodoBrush = CreateBrush("#FF9AA4B2");
    private static readonly Brush FocusBrush = CreateBrush("#FF45D58A");
    private static readonly Brush WaitBrush = CreateBrush("#FF51B5DB");
    private static readonly Brush RemindBrush = CreateBrush("#FFF1B94E");
    private static readonly Brush DoneBrush = CreateBrush("#FF737B87");
    private static readonly Brush PinnedBrush = CreateBrush("#FFA79AF4");
    private static readonly Brush UnpinnedBrush = CreateBrush("#FF69727C");
    private static readonly Brush PinnedBackgroundBrush = CreateBrush("#FF302A48");

    public TreeStatusRowViewModel(
        TaskItem task,
        string contextPath,
        bool isReminder,
        string reminderLabel)
    {
        Task = task;
        ContextPath = contextPath;
        IsReminder = isReminder;
        ReminderLabel = reminderLabel;
    }

    public TaskItem Task { get; }
    public Guid Id => Task.Id;
    public string Title => Task.Title;
    public string ContextPath { get; }
    public bool IsReminder { get; }
    public bool IsDone => Task.Status == TaskStatus.Done;
    public bool IsPinnedToPanel => Task.PinToPanel;
    public string PinToolTip => IsPinnedToPanel ? "Remove from panel" : "Pin to panel";
    public Brush PinBrush => IsPinnedToPanel ? PinnedBrush : UnpinnedBrush;
    public Brush PinFill => IsPinnedToPanel ? PinnedBrush : Brushes.Transparent;
    public Brush PinStroke => IsPinnedToPanel ? PinnedBrush : UnpinnedBrush;
    public Brush PinBackground => IsPinnedToPanel ? PinnedBackgroundBrush : Brushes.Transparent;
    public Visibility PanelBadgeVisibility => IsPinnedToPanel
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string StatusLabel => IsReminder
        ? "REMIND"
        : Task.Status switch
        {
            TaskStatus.InWork => "FOCUS",
            TaskStatus.Waiting => "WAIT",
            TaskStatus.Done => "DONE",
            _ => "TODO"
        };
    public Brush StatusBrush => IsReminder
        ? RemindBrush
        : Task.Status switch
        {
            TaskStatus.InWork => FocusBrush,
            TaskStatus.Waiting => WaitBrush,
            TaskStatus.Done => DoneBrush,
            _ => TodoBrush
        };
    public string WaitingForLabel => Task.Status == TaskStatus.Waiting &&
                                     !string.IsNullOrWhiteSpace(Task.WaitingFor)
        ? $"Waiting for: {Task.WaitingFor}"
        : string.Empty;
    public string ReminderLabel { get; }
    public Visibility WaitingForVisibility => string.IsNullOrEmpty(WaitingForLabel)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public Visibility ReminderVisibility => string.IsNullOrEmpty(ReminderLabel)
        ? Visibility.Collapsed
        : Visibility.Visible;

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

internal sealed record TreeLocationOption(Guid? Id, string Label)
{
    public override string ToString() => Label;
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
