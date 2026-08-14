using AssetEditor.Events;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace AssetEditor.UiCommands;

public sealed class OpenFolderProjectHistoryCommand(
    IPackFileService packFileService,
    IEventHub eventHub) : IUiCommand
{
    public void Execute()
    {
        if (packFileService.GetEditablePack() is not FolderProjectContainer)
            return;

        eventHub.Publish(new OpenFolderProjectHistoryPanelEvent());
    }
}
