using System;
using System.IO;
using System.Windows.Forms;
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
    LocalizationManager localizationManager) : IUiCommand
{
    public void Execute()
    {
        using var folderDialog = new FolderBrowserDialog
        {
            Description = localizationManager.Get(
                "FolderProject.Create.SelectFolder"),
            UseDescriptionForTitle = true,
        };
        if (folderDialog.ShowDialog() != DialogResult.OK)
            return;

        var root = folderDialog.SelectedPath;
        if (HasProjectSettings(root))
        {
            dialogs.ShowDialogBox(
                localizationManager.Get(
                    "FolderProject.Create.AlreadyExists"),
                localizationManager.Get("FolderProject.ErrorTitle"));
            return;
        }

        var outputPath = SelectOutputPath(root);
        if (outputPath == null)
            return;

        FolderProjectContainer? project = null;
        try
        {
            var game = GameInformationDatabase.GetGameById(
                settingsService.CurrentSettings.CurrentGame);
            project = folderProjectFactory.Create(
                root,
                new FolderProjectSettings
                {
                    Name = Path.GetFileName(
                        Path.TrimEndingDirectorySeparator(root)),
                    OutputPackPath = outputPath,
                    GameVersion = game.Type,
                    PackFileVersion = game.PackFileVersion,
                    PackFileType = PackFileCAType.MOD,
                    EnablePackFileCorruptionDetection = false,
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
                    "FolderProject.Create.Failed"));
        }
    }

    private string? SelectOutputPath(string root)
    {
        using var dialog = new SaveFileDialog
        {
            Title = localizationManager.Get(
                "FolderProject.SelectOutputPack"),
            Filter = localizationManager.Get("FolderProject.PackFilter"),
            DefaultExt = "pack",
            AddExtension = true,
            InitialDirectory = Path.GetDirectoryName(
                Path.TrimEndingDirectorySeparator(root)),
            FileName =
                Path.GetFileName(
                    Path.TrimEndingDirectorySeparator(root)) +
                ".pack",
        };
        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.FileName
            : null;
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
