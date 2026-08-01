using System;
using System.IO;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditor.UiCommands;

public sealed class CreateFolderProjectCommand(
    IPackFileService packFileService,
    IFolderProjectFactory folderProjectFactory,
    ApplicationSettingsService settingsService,
    IStandardDialogs dialogs,
    LocalizationManager localizationManager,
    IFolderProjectSetupDialogs? setupDialogs = null,
    IFolderProjectVersionControlService? versionControlService = null) : IUiCommand
{
    private readonly IFolderProjectSetupDialogs _setupDialogs =
        setupDialogs ?? new FolderProjectSetupDialogs(localizationManager);
    private readonly IFolderProjectVersionControlService
        _versionControlService =
            versionControlService ?? new FolderProjectVersionControlService();

    public void Execute()
    {
        var setup = _setupDialogs.ShowSetup(
            localizationManager.Get("FolderProject.Create.SetupTitle"),
            localizationManager.Get("FolderProject.Create.SetupDescription"));
        if (setup == null)
            return;

        var root = setup.ProjectFolder;
        if (HasProjectSettings(root))
        {
            dialogs.ShowDialogBox(
                localizationManager.Get(
                    "FolderProject.Create.AlreadyExists"),
                localizationManager.Get("FolderProject.ErrorTitle"));
            return;
        }

        var name = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(root));
        var outputPath = Path.Combine(
            setup.OutputFolder,
            name + ".pack");

        FolderProjectContainer? project = null;
        try
        {
            var game = GameInformationDatabase.GetGameById(
                settingsService.CurrentSettings.CurrentGame);
            project = folderProjectFactory.Create(
                root,
                new FolderProjectSettings
                {
                    Name = name,
                    OutputPackPath = outputPath,
                    GameVersion = game.Type,
                    PackFileVersion = game.PackFileVersion,
                    PackFileType = PackFileCAType.MOD,
                    EnablePackFileCorruptionDetection =
                        setup.EnablePackFileCorruptionDetection,
                });

            _versionControlService.Initialize(
                root,
                new FolderProjectGitIdentity(
                    localizationManager.Get(
                        "FolderProject.VersionControl.DefaultIdentityName"),
                    localizationManager.Get(
                        "FolderProject.VersionControl.DefaultIdentityEmail")),
                setup.PrimaryBranchName);

            if (packFileService.AddContainer(project, true) == null)
                project.Dispose();
        }
        catch (Exception exception)
        {
            project?.Dispose();
            dialogs.ShowExceptionWindow(
                exception,
                localizationManager.Get(
                    "FolderProject.Create.Failed"));
        }
    }

    private static bool HasProjectSettings(string root)
    {
        return File.Exists(
                   Path.Combine(root, FolderProjectSettings.CnFileName)) ||
               File.Exists(
                   Path.Combine(
                       root,
                       FolderProjectSettings.OriginalFileName)) ||
               File.Exists(
                   Path.Combine(
                       root,
                       FolderProjectSettings.LegacyFileName));
    }
}
