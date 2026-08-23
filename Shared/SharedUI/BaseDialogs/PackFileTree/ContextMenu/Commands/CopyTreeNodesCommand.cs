using System.Collections.Generic;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public sealed class CopyTreeNodesCommand(
        PackFileTreeOperations operations) : IContextMenuCommand
    {
        public string GetDisplayName(TreeNode node) =>
            LocalizationManager.Instance.Get("General.Copy");

        public bool IsEnabled(TreeNode node) =>
            operations.CanCopy([node]);

        public void Execute(TreeNode node) => operations.Copy([node]);

        public bool IsEnabled(IReadOnlyList<TreeNode> nodes) =>
            operations.CanCopy(nodes);

        public void Execute(IReadOnlyList<TreeNode> nodes) =>
            operations.Copy(nodes);
    }
}
