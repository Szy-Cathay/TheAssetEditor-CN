using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public sealed class PasteTreeNodesCommand(
        PackFileTreeOperations operations) : IContextMenuCommand
    {
        public string GetDisplayName(TreeNode node) =>
            LocalizationManager.Instance.Get("General.Paste");

        public bool IsEnabled(TreeNode node) => operations.CanPaste(node);

        public void Execute(TreeNode node) => operations.Paste(node);
    }
}
