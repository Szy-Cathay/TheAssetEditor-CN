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

public sealed class ImportPackAsFolderProjectCommand(
    IPackFileService packFileService,
    IPackFileContainerLoader packFileContainerLoader,
    IFolderProjectFactory folderProjectFactory,
    ApplicationSettingsService settingsService,
    IStandardDialogs dialogs,
    LocalizationManager localizationManager,
    IFolderProjectImportDialogs? importDialogs = null,
    IFolderProjectSetupDialogs? setupDialogs = null,
    Func<string, bool>? isEmptyTarget = null,
    IFolderProjectVersionControlService? versionControlService = null,
    IFolderProjectProgressRunner? progressRunner = null) : IUiCommand
{
    private readonly IFolderProjectImportDialogs _importDialogs =
        importDialogs ?? new FolderProjectImportDialogs();
    private readonly IFolderProjectSetupDialogs _setupDialogs =
        setupDialogs ?? new FolderProjectSetupDialogs(localizationManager);
    private readonly Func<string, bool> _isEmptyTarget =
        isEmptyTarget ?? FolderProjectImportTargetValidator.IsEmptyTarget;
    private readonly IFolderProjectVersionControlService
        _versionControlService =
            versionControlService ?? new FolderProjectVersionControlService();
    private readonly IFolderProjectProgressRunner _progressRunner =
        progressRunner ?? new FolderProjectProgressRunner();

    public void Execute()
    {
        var sourcePath = _importDialogs.SelectSourcePack(
            localizationManager.Get(
                "FolderProject.Import.SelectPack"),
            localizationManager.Get("FolderProject.PackFilter"));
        if (sourcePath == null)
            return;

        var setupTitle =
            localizationManager.Get("FolderProject.Import.SetupTitle");
        var setup = _setupDialogs.ShowSetup(
            setupTitle,
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
        try
        {
            project = _progressRunner.Run(
                setupTitle,
                localizationManager.Get(
                    "FolderProject.Import.Progress"),
                reportProgress =>
                {
                    FolderProjectContainer? importedProject = null;
                    try
                    {
                        reportProgress(
                            new OperationProgressUpdate(
                                localizationManager.Get(
                                    "FolderProject.Progress.ReadPack"),
                                sourcePath));
                        var source = packFileContainerLoader.Load(
                            sourcePath);
                        if (source == null)
                            return null;

                        var game = GameInformationDatabase.GetGameById(
                            settingsService.CurrentSettings.CurrentGame);
                        importedProject = folderProjectFactory.ImportPack(
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
                            },
                            progress => reportProgress(
                                new OperationProgressUpdate(
                                    localizationManager.Get(
                                        progress.IsCompressed
                                            ? "FolderProject.Progress.ExtractCompressedFile"
                                            : "FolderProject.Progress.WriteFile"),
                                    progress.RelativePath,
                                    progress.CurrentIndex,
                                    progress.Total)));

                        reportProgress(
                            new OperationProgressUpdate(
                                localizationManager.Get(
                                    "FolderProject.Progress.InitializeGit"),
                                localizationManager.Get(
                                    "FolderProject.Progress.InitializeGitDetail")));
                        _versionControlService.Initialize(
                            projectRoot,
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
                                projectRoot));
                        return importedProject;
                    }
                    catch
                    {
                        importedProject?.Dispose();
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
                    "FolderProject.Import.Failed"));
        }
    }

}
