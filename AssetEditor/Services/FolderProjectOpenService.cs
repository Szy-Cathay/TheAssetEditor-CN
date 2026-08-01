using System;
using System.IO;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditor.Services;

public interface IFolderProjectOpenService
{
    void Open(string projectRoot);
}

public sealed class FolderProjectOpenService(
    IPackFileService packFileService,
    IFolderProjectFactory folderProjectFactory,
    IFolderProjectVersionControlService versionControlService,
    IFolderProjectVersionControlWindowService versionControlWindowService,
    ApplicationSettingsService settingsService,
    IStandardDialogs dialogs,
    LocalizationManager localizationManager) : IFolderProjectOpenService
{
    private readonly ILogger _logger =
        Logging.Create<FolderProjectOpenService>();

    public void Open(string projectRoot)
    {
        FolderProjectContainer? project = null;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(projectRoot));
            var hasCnSettings = File.Exists(
                Path.Combine(root, FolderProjectSettings.CnFileName));
            if (!hasCnSettings &&
                !File.Exists(
                    Path.Combine(
                        root,
                        FolderProjectSettings.OriginalFileName)) &&
                !File.Exists(
                    Path.Combine(
                        root,
                        FolderProjectSettings.LegacyFileName)))
            {
                RemoveBrokenRecentProject(root);
                dialogs.ShowDialogBox(
                    localizationManager.Get(
                        "FolderProject.Open.SettingsMissing"),
                    localizationManager.Get("FolderProject.ErrorTitle"));
                return;
            }

            FolderProjectMergeState mergeState;
            try
            {
                mergeState = versionControlService.GetMergeState(root);
            }
            catch (FolderProjectVersionControlException exception)
                when (exception.Code ==
                      FolderProjectVersionControlError
                          .RepositoryNotInitialized)
            {
                dialogs.ShowDialogBox(
                    localizationManager.Get(
                        "FolderProject.Open.VersionControlRequired"),
                    localizationManager.Get("FolderProject.ErrorTitle"));
                versionControlWindowService.ShowDialog(
                    root,
                    GetProjectName(root),
                    true);
                return;
            }
            if (mergeState != null &&
                mergeState.Phase != FolderProjectMergePhase.None)
            {
                versionControlWindowService.ShowDialog(
                    root,
                    GetProjectName(root),
                    true);
                return;
            }

            project = folderProjectFactory.Open(root);
            if (project.ProjectSettings.GameVersion == null)
            {
                project.ProjectSettings.GameVersion =
                    settingsService.CurrentSettings.CurrentGame;
            }

            if (!hasCnSettings)
            {
                var game = GameInformationDatabase.GetGameById(
                    project.ProjectSettings.GameVersion.Value);
                project.ProjectSettings.PackFileVersion =
                    game.PackFileVersion;
            }
            project.SaveSettings();

            if (packFileService.AddContainer(project, true) == null)
                project.Dispose();
        }
        catch (Exception exception)
        {
            project?.Dispose();
            RemoveBrokenRecentProject(projectRoot);
            _logger.Error(
                exception,
                "Opening the folder project failed.");
            dialogs.ShowDialogBox(
                localizationManager.Get("FolderProject.Open.Failed"),
                localizationManager.Get("FolderProject.ErrorTitle"));
        }
    }

    private static string GetProjectName(string projectRoot)
    {
        try
        {
            var name = FolderProjectSettings.Load(projectRoot).Name;
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }

        return Path.GetFileName(projectRoot);
    }

    private void RemoveBrokenRecentProject(string projectRoot)
    {
        if (settingsService.RemoveRecentlyOpenedFolderProject(projectRoot))
            settingsService.Save();
    }
}
