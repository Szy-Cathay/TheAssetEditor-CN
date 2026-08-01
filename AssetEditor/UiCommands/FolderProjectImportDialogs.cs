using System;
using System.Windows.Forms;

using AssetEditor.Views.FolderProject;

using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace AssetEditor.UiCommands;

public interface IFolderProjectImportDialogs
{
    string? SelectSourcePack(string title, string filter);
}

public sealed record FolderProjectSetupDialogResult(
    string ProjectFolder,
    string OutputFolder,
    bool EnablePackFileCorruptionDetection,
    string PrimaryBranchName = "master");

public interface IFolderProjectSetupDialogs
{
    FolderProjectSetupDialogResult? ShowSetup(
        string title,
        string description);
}

public interface IFolderProjectProgressRunner
{
    FolderProjectContainer? Run(
        string title,
        string message,
        Func<FolderProjectContainer?> operation);
}

public sealed class FolderProjectImportDialogs :
    IFolderProjectImportDialogs
{
    public string? SelectSourcePack(string title, string filter)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
        };
        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.FileName
            : null;
    }

}

public sealed class FolderProjectSetupDialogs(
    LocalizationManager localizationManager) : IFolderProjectSetupDialogs
{
    public FolderProjectSetupDialogResult? ShowSetup(
        string title,
        string description)
    {
        var window = new FolderProjectSetupWindow(
            localizationManager,
            title,
            description);
        return window.ShowDialog() == true
            ? new FolderProjectSetupDialogResult(
                window.ProjectFolder,
                window.OutputFolder,
                window.EnablePackFileCorruptionDetection,
                window.PrimaryBranchName)
            : null;
    }
}

public sealed class FolderProjectProgressRunner :
    IFolderProjectProgressRunner
{
    public FolderProjectContainer? Run(
        string title,
        string message,
        Func<FolderProjectContainer?> operation)
    {
        return new FolderProjectProgressWindow(
            title,
            message,
            operation).Run();
    }
}
