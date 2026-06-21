using TaskOverlay.Core;

namespace TaskOverlay.App;

internal sealed class TreeRowViewModel
{
    public TreeRowViewModel(TreeNode node, int depth)
    {
        Node = node;
        IndentWidth = depth * 14;
    }

    public TreeNode Node { get; }
    public double IndentWidth { get; }
    public string Title => Node.Title;
    public bool IsActive => Node.Active;
    public bool IsDone => Node.Status == TreeNodeStatus.Done;
    public string KindMarker => Node.Kind switch
    {
        TreeNodeKind.Project => "P",
        TreeNodeKind.Group => "G",
        _ => "T"
    };
    public string StatusMarker => IsActive ? "ACTIVE" : IsDone ? "DONE" : string.Empty;
}
