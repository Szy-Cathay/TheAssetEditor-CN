using AssetEditor.Services;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace AssetEditor.UiCommands;

public sealed class OpenFolderProjectVersionControlCommand(
    IPackFileService packFileService,
    IFolderProjectVersionControlWindowService windowService) : IUiCommand
{
    public void Execute()
    {
        if (packFileService.GetEditablePack() is not
            FolderProjectContainer project)
        {
            return;
        }

        windowService.ShowDialog(
            project.ProjectRoot,
            project.ProjectSettings.Name,
            false);
    }
}
