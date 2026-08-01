using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;

namespace AssetEditor.Services;

public enum FolderProjectUnsavedChangesOperation
{
    Stage,
    CommitAll,
}

public enum FolderProjectUnsavedChangesChoice
{
    Save,
    DontSave,
    Cancel,
}

public interface IFolderProjectUnsavedChangesService
{
    bool HasUnsavedChanges(
        string projectRoot,
        IReadOnlyCollection<string>? repositoryPaths);

    bool SaveUnsavedChanges(
        string projectRoot,
        IReadOnlyCollection<string>? repositoryPaths);
}

public interface IFolderProjectUnsavedChangesPrompt
{
    FolderProjectUnsavedChangesChoice Show(
        FolderProjectUnsavedChangesOperation operation);
}

public sealed class FolderProjectUnsavedChangesService(
    IPackFileService packFileService,
    IEditorManager editorManager) :
    IFolderProjectUnsavedChangesService
{
    public bool HasUnsavedChanges(
        string projectRoot,
        IReadOnlyCollection<string>? repositoryPaths)
    {
        return GetUnsavedEditors(projectRoot, repositoryPaths).Count != 0;
    }

    public bool SaveUnsavedChanges(
        string projectRoot,
        IReadOnlyCollection<string>? repositoryPaths)
    {
        foreach (var editor in GetUnsavedEditors(
                     projectRoot,
                     repositoryPaths))
        {
            if (!editor.Save())
                return false;
        }

        return true;
    }

    private IReadOnlyList<ISaveableEditor> GetUnsavedEditors(
        string projectRoot,
        IReadOnlyCollection<string>? repositoryPaths)
    {
        var project = packFileService.GetAllPackfileContainers()
            .OfType<FolderProjectContainer>()
            .FirstOrDefault(
                item => PathsEqual(item.ProjectRoot, projectRoot));
        if (project == null)
            return [];

        HashSet<string>? selectedPaths = null;
        if (repositoryPaths != null)
        {
            selectedPaths = repositoryPaths
                .Select(NormalizeRepositoryPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return editorManager.GetAllEditors()
            .OfType<IFileEditor>()
            .Where(
                editor => ReferenceEquals(
                    packFileService.GetPackFileContainer(
                        editor.CurrentFile),
                    project))
            .Where(
                editor => selectedPaths == null ||
                          selectedPaths.Contains(
                              NormalizeRepositoryPath(
                                  packFileService.GetFullPath(
                                      editor.CurrentFile,
                                      project))))
            .OfType<ISaveableEditor>()
            .Where(editor => editor.HasUnsavedChanges)
            .ToList();
    }

    private static bool PathsEqual(string first, string second)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRepositoryPath(string path)
    {
        var normalized = path.Replace('/', '\\');
        while (normalized.StartsWith(@".\", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.TrimStart('\\');
    }
}

public sealed class FolderProjectUnsavedChangesPrompt(
    LocalizationManager localization) :
    IFolderProjectUnsavedChangesPrompt
{
    public FolderProjectUnsavedChangesChoice Show(
        FolderProjectUnsavedChangesOperation operation)
    {
        var choice = FolderProjectUnsavedChangesChoice.Cancel;
        var owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(item => item.IsActive) ??
            Application.Current?.MainWindow;
        var window = new Window
        {
            Title = localization.Get(
                "FolderProject.VersionControl.Unsaved.Title"),
            Owner = owner,
            WindowStartupLocation = owner == null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
        };
        if (Application.Current?.TryFindResource("CustomWindowStyle") is
            Style style)
        {
            window.Style = style;
        }
        var buttons = new StackPanel
        {
            Margin = new Thickness(18),
            Orientation = Orientation.Horizontal,
        };
        buttons.Children.Add(CreateButton(
            operation == FolderProjectUnsavedChangesOperation.CommitAll
                ? "FolderProject.VersionControl.Unsaved.SaveAndCommit"
                : "FolderProject.VersionControl.Unsaved.Save",
            true,
            value => choice = value,
            FolderProjectUnsavedChangesChoice.Save,
            window));
        buttons.Children.Add(CreateButton(
            operation == FolderProjectUnsavedChangesOperation.CommitAll
                ? "FolderProject.VersionControl.Unsaved.DontSaveAndCommit"
                : "FolderProject.VersionControl.Unsaved.DontSave",
            false,
            value => choice = value,
            FolderProjectUnsavedChangesChoice.DontSave,
            window));
        buttons.Children.Add(CreateButton(
            "FolderProject.VersionControl.Unsaved.Cancel",
            false,
            value => choice = value,
            FolderProjectUnsavedChangesChoice.Cancel,
            window,
            true));
        window.Content = buttons;
        window.ShowDialog();
        return choice;
    }

    private Button CreateButton(
        string localizationKey,
        bool isDefault,
        Action<FolderProjectUnsavedChangesChoice> setChoice,
        FolderProjectUnsavedChangesChoice choice,
        Window window,
        bool isCancel = false)
    {
        var button = new Button
        {
            MinWidth = 96,
            Height = 30,
            Margin = new Thickness(0, 0, isCancel ? 0 : 8, 0),
            Content = localization.Get(localizationKey),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        button.Click += (_, _) =>
        {
            setChoice(choice);
            window.DialogResult = choice !=
                                  FolderProjectUnsavedChangesChoice.Cancel;
        };
        return button;
    }
}
