using System;
using System.IO;
using System.Windows.Forms;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

public sealed class ChangeFolderProjectOutputCommand(
    IStandardDialogs dialogs) : IContextMenuCommand
{
    public string GetDisplayName(TreeNode node)
    {
        return LocalizationManager.Instance.Get(
            "FolderProject.Context.ChangeOutput");
    }

    public bool IsEnabled(TreeNode node)
    {
        return node.NodeType == NodeType.Root &&
               node.FileOwner is FolderProjectContainer;
    }

    public void Execute(TreeNode node)
    {
        if (node.FileOwner is not FolderProjectContainer project)
            return;

        var currentPath = project.ProjectSettings.OutputPackPath;
        using var dialog = new SaveFileDialog
        {
            Title = LocalizationManager.Instance.Get(
                "FolderProject.SelectOutputPack"),
            Filter = LocalizationManager.Instance.Get(
                "FolderProject.PackFilter"),
            DefaultExt = "pack",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(currentPath)
                ? project.Name + ".pack"
                : Path.GetFileName(currentPath),
            InitialDirectory = string.IsNullOrWhiteSpace(currentPath)
                ? Path.GetDirectoryName(project.ProjectRoot)
                : Path.GetDirectoryName(currentPath),
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            FolderProjectPathPolicy.EnsureOutputOutsideProject(
                project.ProjectRoot,
                dialog.FileName);
            project.ProjectSettings.OutputPackPath =
                Path.GetFullPath(dialog.FileName);
            project.SaveSettings();
            node.UnsavedChanged = true;
        }
        catch (Exception exception)
        {
            dialogs.ShowExceptionWindow(
                exception,
                LocalizationManager.Instance.Get(
                    "FolderProject.Output.Failed"));
        }
    }
}
