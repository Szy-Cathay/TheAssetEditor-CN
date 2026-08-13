using System;
using System.IO;
using System.Threading;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;

namespace AssetEditor.UiCommands;

public enum FolderProjectImportOutcome
{
    Imported,
    Cancelled,
    Failed,
}

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
    IFolderProjectHistoryService? historyService = null,
    IFolderProjectProgressRunner? progressRunner = null) : IUiCommand
{
    private readonly IFolderProjectImportDialogs _importDialogs =
        importDialogs ?? new FolderProjectImportDialogs();
    private readonly IFolderProjectSetupDialogs _setupDialogs =
        setupDialogs ?? new FolderProjectSetupDialogs(
            localizationManager,
            dialogs);
    private readonly Func<string, bool> _isEmptyTarget =
        isEmptyTarget ?? FolderProjectImportTargetValidator.IsEmptyTarget;
    private readonly IFolderProjectHistoryService _historyService =
        historyService ?? new FolderProjectHistoryService(
            localizationManager);
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

        Execute(sourcePath);
    }

    public FolderProjectImportOutcome Execute(string sourcePath)
    {
        return Execute(sourcePath, CancellationToken.None);
    }

    public FolderProjectImportOutcome Execute(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return FolderProjectImportOutcome.Failed;
        if (cancellationToken.IsCancellationRequested)
            return FolderProjectImportOutcome.Cancelled;

        var setupTitle =
            localizationManager.Get("FolderProject.Import.SetupTitle");
        var setup = _setupDialogs.ShowSetup(
            setupTitle,
            localizationManager.Get("FolderProject.Import.SetupDescription"));
        if (setup == null)
            return FolderProjectImportOutcome.Cancelled;

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
            return FolderProjectImportOutcome.Failed;
        }

        var projectName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(projectRoot));
        var outputPath = Path.Combine(
            setup.OutputFolder,
            projectName + ".pack");

        FolderProjectContainer? project = null;
        try
        {
            var progressResult = _progressRunner.RunCancelable(
                setupTitle,
                localizationManager.Get(
                    "FolderProject.Import.Progress"),
                cancellationToken,
                (reportProgress, operationCancellationToken) =>
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
                        {
                            throw new InvalidDataException(
                                "The source Pack could not be loaded.");
                        }

                        var game = GameInformationDatabase.GetGameById(
                            settingsService.CurrentSettings.CurrentGame);
                        importedProject = folderProjectFactory.ImportPack(
                            source,
                            projectRoot,
                            new FolderProjectSettings
                            {
                                Name = projectName,
                                SourcePackPath = Path.GetFullPath(sourcePath),
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
                                    progress.Total)),
                            operationCancellationToken);
                        operationCancellationToken
                            .ThrowIfCancellationRequested();
                        reportProgress(
                            new OperationProgressUpdate(
                                localizationManager.Get(
                                    "FolderProject.Progress.InitializeHistory"),
                                localizationManager.Get(
                                    "FolderProject.Progress.InitializeHistoryDetail")));
                        _historyService.Initialize(
                            projectRoot,
                            progress =>
                            {
                                operationCancellationToken
                                    .ThrowIfCancellationRequested();
                                reportProgress(
                                    FolderProjectHistoryProgressAdapter
                                        .ToOperationProgress(
                                            progress,
                                            localizationManager));
                            });
                        operationCancellationToken
                            .ThrowIfCancellationRequested();
                        reportProgress(
                            new OperationProgressUpdate(
                                localizationManager.Get(
                                    "FolderProject.Progress.OpenProject"),
                                projectRoot));
                        return importedProject;
                    }
                    catch (Exception failure)
                    {
                        importedProject?.Dispose();
                        try
                        {
                            RollbackImportTarget(projectRoot);
                        }
                        catch (Exception rollbackFailure)
                        {
                            throw new AggregateException(
                                "Folder-project import failed and rollback was incomplete.",
                                failure,
                                rollbackFailure);
                        }
                        throw;
                    }
                });
            project = progressResult.Project;

            if (progressResult.Cancelled)
                return FolderProjectImportOutcome.Cancelled;

            if (project == null)
            {
                dialogs.ShowDialogBox(
                    localizationManager.Get(
                        "FolderProject.Import.Failed"),
                    localizationManager.Get(
                        "FolderProject.ErrorTitle"));
                return FolderProjectImportOutcome.Failed;
            }

            if (packFileService.AddEditableFolderProject(project) == null)
            {
                project.Dispose();
                RollbackImportTarget(projectRoot);
                dialogs.ShowDialogBox(
                    localizationManager.Get(
                        "FolderProject.Import.Failed"),
                    localizationManager.Get(
                        "FolderProject.ErrorTitle"));
                return FolderProjectImportOutcome.Failed;
            }

            return FolderProjectImportOutcome.Imported;
        }
        catch (Exception exception)
        {
            project?.Dispose();
            Exception failure = exception;
            if (project != null)
            {
                try
                {
                    RollbackImportTarget(projectRoot);
                }
                catch (Exception rollbackFailure)
                {
                    failure = new AggregateException(
                        "Folder-project import failed and rollback was incomplete.",
                        exception,
                        rollbackFailure);
                }
            }
            dialogs.ShowExceptionWindow(
                failure,
                localizationManager.Get(
                    "FolderProject.Import.Failed"));
            return FolderProjectImportOutcome.Failed;
        }
    }

    private static void RollbackImportTarget(string projectRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        if (!File.Exists(Path.Combine(
                root,
                FolderProjectSettings.CnFileName)))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(root, true);
        Directory.CreateDirectory(root);
    }

}
