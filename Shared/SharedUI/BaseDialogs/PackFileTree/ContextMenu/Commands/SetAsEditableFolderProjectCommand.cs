using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

public sealed class SetAsEditableFolderProjectCommand(
    IPackFileService packFileService) : IContextMenuCommand
{
    public string GetDisplayName(TreeNode node) =>
        LocalizationManager.Instance.Get(
            "ContextMenu.SetAsEditablePack");

    public bool IsEnabled(TreeNode node) =>
        node.NodeType == NodeType.Root &&
        node.FileOwner is FolderProjectContainer &&
        !ReferenceEquals(
            packFileService.GetEditablePack(),
            node.FileOwner);

    public void Execute(TreeNode node)
    {
        if (node.FileOwner is FolderProjectContainer project)
            packFileService.TryActivateFolderProject(project.ProjectRoot);
    }
}
