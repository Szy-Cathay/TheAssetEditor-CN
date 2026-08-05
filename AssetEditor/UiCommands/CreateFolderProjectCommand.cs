using System;
using System.IO;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;

namespace AssetEditor.UiCommands;

public sealed class CreateFolderProjectCommand(
    IPackFileService packFileService,
    IFolderProjectFactory folderProjectFactory,
    ApplicationSettingsService settingsService,
    IStandardDialogs dialogs,
    LocalizationManager localizationManager,
    IFolderProjectSetupDialogs? setupDialogs = null,
    IFolderProjectVersionControlService? versionControlService = null,
    IFolderProjectProgressRunner? progressRunner = null) : IUiCommand
{
    private readonly IFolderProjectSetupDialogs _setupDialogs =
        setupDialogs ?? new FolderProjectSetupDialogs(
            localizationManager,
            dialogs);
    private readonly IFolderProjectVersionControlService
        _versionControlService =
            versionControlService ?? new FolderProjectVersionControlService();
    private readonly IFolderProjectProgressRunner _progressRunner =
        progressRunner ?? new FolderProjectProgressRunner();

    public void Execute()
    {
        var setupTitle =
            localizationManager.Get("FolderProject.Create.SetupTitle");
        var setup = _setupDialogs.ShowSetup(
            setupTitle,
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
            project = _progressRunner.Run(
                setupTitle,
                localizationManager.Get(
                    "FolderProject.Create.Progress"),
                reportProgress =>
                {
                    FolderProjectContainer? createdProject = null;
                    try
                    {
                        reportProgress(
                            new OperationProgressUpdate(
                                localizationManager.Get(
                                    "FolderProject.Progress.CreateProject"),
                                root));
                        var game = GameInformationDatabase.GetGameById(
                            settingsService.CurrentSettings.CurrentGame);
                        createdProject = folderProjectFactory.Create(
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

                        reportProgress(
                            new OperationProgressUpdate(
                                localizationManager.Get(
                                    "FolderProject.Progress.InitializeGit"),
                                localizationManager.Get(
                                    "FolderProject.Progress.InitializeGitDetail")));
                        _versionControlService.Initialize(
                            root,
                            new FolderProjectGitIdentity(
                                localizationManager.Get(
                                    "FolderProject.VersionControl.DefaultIdentityName"),
                                localizationManager.Get(
                                    "FolderProject.VersionControl.DefaultIdentityEmail")),
                            setup.PrimaryBranchName,
                            progress => reportProgress(
                                FolderProjectVersionControlProgressAdapter
                                    .ToOperationProgress(
                                        progress,
                                        localizationManager)));
                        reportProgress(
                            new OperationProgressUpdate(
                                localizationManager.Get(
                                    "FolderProject.Progress.OpenProject"),
                                root));
                        return createdProject;
                    }
                    catch
                    {
                        createdProject?.Dispose();
                        throw;
                    }
                });

            if (project == null)
                return;

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
