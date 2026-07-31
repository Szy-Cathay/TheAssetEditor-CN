using System;
using System.Linq;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

public sealed class ToggleFolderProjectIgnoreCommand :
    IContextMenuCommand
{
    public string GetDisplayName(TreeNode node)
    {
        return IsExplicitlyIgnored(node)
            ? LocalizationManager.Instance.Get(
                "FolderProject.Context.Include")
            : LocalizationManager.Instance.Get(
                "FolderProject.Context.Ignore");
    }

    public bool IsEnabled(TreeNode node)
    {
        if (node.FileOwner is not FolderProjectContainer project ||
            node.NodeType == NodeType.Root)
        {
            return false;
        }

        return !project.IsIgnored(node.GetFullPath()) ||
               IsExplicitlyIgnored(node);
    }

    public void Execute(TreeNode node)
    {
        if (node.FileOwner is not FolderProjectContainer project ||
            node.NodeType == NodeType.Root)
        {
            return;
        }

        var path = node.GetFullPath();
        project.SetIgnored(path, !IsExplicitlyIgnored(node));
        node.ForeachNode(
            child =>
                child.IsIgnored =
                    project.IsIgnored(child.GetFullPath()));

        var root = node;
        while (root.Parent != null)
            root = root.Parent;
        root.UnsavedChanged = true;
    }

    private static bool IsExplicitlyIgnored(TreeNode node)
    {
        if (node.FileOwner is not FolderProjectContainer project ||
            node.NodeType == NodeType.Root)
        {
            return false;
        }

        var path = FolderProjectPathPolicy.NormalizeRelativePath(
            node.GetFullPath());
        return project.ProjectSettings.IgnoredPaths.Any(
            ignored => string.Equals(
                ignored,
                path,
                StringComparison.OrdinalIgnoreCase));
    }
}
