using System;
using System.IO;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common;

namespace AssetEditor.UiCommands;

public sealed class ImportPackAsFolderProjectCommand(
    IPackFileService packFileService,
    IPackFileContainerLoader packFileContainerLoader,
    IFolderProjectFactory folderProjectFactory,
    ApplicationSettingsService settingsService,
    IStandardDialogs dialogs,
    LocalizationManager localizationManager,
    IFolderProjectImportDialogs? importDialogs = null,
    IFolderProjectSetupDialogs? setupDialogs = null,
    Func<string, bool>? isEmptyTarget = null) : IUiCommand
{
    private readonly IFolderProjectImportDialogs _importDialogs =
        importDialogs ?? new FolderProjectImportDialogs();
    private readonly IFolderProjectSetupDialogs _setupDialogs =
        setupDialogs ?? new FolderProjectSetupDialogs(localizationManager);
    private readonly Func<string, bool> _isEmptyTarget =
        isEmptyTarget ?? FolderProjectImportTargetValidator.IsEmptyTarget;

    public void Execute()
    {
        var sourcePath = _importDialogs.SelectSourcePack(
            localizationManager.Get(
                "FolderProject.Import.SelectPack"),
            localizationManager.Get("FolderProject.PackFilter"));
        if (sourcePath == null)
            return;

        var setup = _setupDialogs.ShowSetup(
            localizationManager.Get("FolderProject.Import.SetupTitle"),
            localizationManager.Get("FolderProject.Import.SetupDescription"));
        if (setup == null)
            return;

        var projectRoot = setup.ProjectFolder;
        var isEmptyTarget = false;
        try
        {
            isEmptyTarget = _isEmptyTarget(projectRoot);
        }
        catch (UnauthorizedAccessException)
        {
            isEmptyTarget = false;
        }

        if (!isEmptyTarget)
        {
            dialogs.ShowDialogBox(
                localizationManager.Get(
                    "FolderProject.Import.TargetNotEmpty"),
                localizationManager.Get("FolderProject.ErrorTitle"));
            return;
        }

        var projectName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(projectRoot));
        var outputPath = Path.Combine(
            setup.OutputFolder,
            projectName + ".pack");

        FolderProjectContainer? project = null;
        using (new WaitCursor())
        {
            try
            {
                var source = packFileContainerLoader.Load(
                    sourcePath);
                if (source == null)
                    return;

                var game = GameInformationDatabase.GetGameById(
                    settingsService.CurrentSettings.CurrentGame);
                project = folderProjectFactory.ImportPack(
                    source,
                    projectRoot,
                    new FolderProjectSettings
                    {
                        Name = projectName,
                        OutputPackPath = outputPath,
                        GameVersion = game.Type,
                        PackFileVersion = source.Header.Version,
                        PackFileType = source.Header.PackFileType,
                        EnablePackFileCorruptionDetection =
                            setup.EnablePackFileCorruptionDetection,
                    });

                if (packFileService.AddContainer(project, true) == null)
                    project.Dispose();
            }
            catch (Exception exception)
            {
                project?.Dispose();
                dialogs.ShowExceptionWindow(
                    exception,
                    localizationManager.Get(
                        "FolderProject.Import.Failed"));
            }
        }
    }

}
