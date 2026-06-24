using TaskOverlay.Core;

namespace TaskOverlay.App;

internal sealed class TreeCardViewModel
{
    public TreeCardViewModel(TreeNode node, int depth)
    {
        Node = node;
        IndentWidth = depth * 22;
    }

    public TreeNode Node { get; }
    public double IndentWidth { get; }
    public string Title => Node.Title;
    public bool IsProject => Node.Kind == TreeNodeKind.Project;
    public bool IsGroup => Node.Kind == TreeNodeKind.Group;
    public bool IsTask => Node.Kind == TreeNodeKind.Task;
    public bool IsActive => Node.Active;
    public bool IsDone => Node.Status == TreeNodeStatus.Done;
    public bool CanActivate => IsTask && !IsActive && !IsDone;
    public string KindLabel => Node.Kind switch
    {
        TreeNodeKind.Project => "PROJECT",
        TreeNodeKind.Group => "GROUP / BRANCH",
        _ => "TASK"
    };
    public string StatusLabel => IsActive
        ? "FOCUS"
        : IsDone
            ? "COMPLETED"
            : string.Empty;
    public string CompletionAction => IsDone ? "Reopen" : "Complete";
}
