using System;
using System.Windows.Forms;
using AssetEditor.Services;
using Shared.Core.Events;
using Shared.Core.Services;

namespace AssetEditor.UiCommands;

public sealed class OpenFolderProjectCommand(
    IFolderProjectOpenService folderProjectOpenService,
    LocalizationManager localizationManager,
    Func<string?>? selectFolder = null) : IUiCommand
{
    private readonly Func<string?> _selectFolder =
        selectFolder ?? (() => SelectFolder(localizationManager));

    public void Execute()
    {
        var projectRoot = _selectFolder();
        if (projectRoot != null)
            folderProjectOpenService.Open(projectRoot);
    }

    private static string? SelectFolder(
        LocalizationManager localizationManager)
    {
        using var folderDialog = new FolderBrowserDialog
        {
            Description = localizationManager.Get(
                "FolderProject.Open.SelectFolder"),
            UseDescriptionForTitle = true,
        };
        return folderDialog.ShowDialog() == DialogResult.OK
            ? folderDialog.SelectedPath
            : null;
    }
}
