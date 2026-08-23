using System;
using System.Collections.Generic;
using Shared.Core.Events;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public interface IContextMenuCommand : IUiCommand
    {
        public string GetDisplayName(TreeNode node);
        public bool IsEnabled(TreeNode node);
        public void Execute(TreeNode node);

        public string GetDisplayName(IReadOnlyList<TreeNode> nodes) =>
            GetDisplayName(nodes[0]);

        public bool IsEnabled(IReadOnlyList<TreeNode> nodes) =>
            nodes.Count == 1 && IsEnabled(nodes[0]);

        public void Execute(IReadOnlyList<TreeNode> nodes)
        {
            if (nodes.Count != 1)
            {
                throw new InvalidOperationException(
                    "This command only supports one tree node.");
            }
            Execute(nodes[0]);
        }
    }
}
