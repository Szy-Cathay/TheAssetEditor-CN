using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

public sealed class ToggleFolderProjectCorruptionDetectionCommand :
    IContextMenuCommand
{
    public string GetDisplayName(TreeNode node)
    {
        return node.FileOwner is FolderProjectContainer project &&
               project.IsPackFileCorruptionDetectionEnabled()
            ? LocalizationManager.Instance.Get(
                "FolderProject.Context.DisableCorruptionDetection")
            : LocalizationManager.Instance.Get(
                "FolderProject.Context.EnableCorruptionDetection");
    }

    public bool IsEnabled(TreeNode node)
    {
        return node.NodeType == NodeType.Root &&
               node.FileOwner is FolderProjectContainer;
    }

    public void Execute(TreeNode node)
    {
        if (node.NodeType != NodeType.Root ||
            node.FileOwner is not FolderProjectContainer project)
        {
            return;
        }

        project.SetPackFileCorruptionDetectionEnabled(
            !project.IsPackFileCorruptionDetectionEnabled());
        node.UnsavedChanged = true;
    }
}
