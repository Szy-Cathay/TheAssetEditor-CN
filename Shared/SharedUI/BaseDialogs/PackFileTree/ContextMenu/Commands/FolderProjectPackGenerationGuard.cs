using System;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

public interface IFolderProjectPackGenerationGuard
{
    bool CanGenerate(PackFileContainer container);
}

public sealed class FolderProjectPackGenerationGuard(
    IFolderProjectHistoryService historyService,
    IStandardDialogs dialogs) : IFolderProjectPackGenerationGuard
{
    public bool CanGenerate(PackFileContainer container)
    {
        if (container is not FolderProjectContainer project)
            return true;

        FolderProjectHistoryStatus status;
        try
        {
            status = historyService.GetDisplayStatus(project.ProjectRoot);
        }
        catch (Exception)
        {
            dialogs.ShowDialogBox(
                LocalizationManager.Instance.Get(
                    "FolderProject.GeneratePack.StatusCheckFailed"),
                LocalizationManager.Instance.Get(
                    "FolderProject.GeneratePack.Title"));
            return false;
        }

        if (status.IsClean)
            return true;

        return dialogs.ShowYesNoBox(
                LocalizationManager.Instance.GetFormat(
                    "FolderProject.GeneratePack.UnrecordedChanges",
                    project.ProjectSettings.Name,
                    status.UnrecordedChanges.Count),
                LocalizationManager.Instance.Get(
                    "FolderProject.GeneratePack.Title")) ==
            ShowMessageBoxResult.OK;
    }
}
