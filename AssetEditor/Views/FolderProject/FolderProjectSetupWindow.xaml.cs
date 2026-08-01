using System;
using System.IO;
using System.Windows;

using CommonControls;

using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace AssetEditor.Views.FolderProject;

public partial class FolderProjectSetupWindow : Window
{
    private readonly LocalizationManager _localizationManager;

    public string ProjectFolder { get; private set; } = "";
    public string OutputFolder { get; private set; } = "";
    public bool EnablePackFileCorruptionDetection =>
        EnablePackFileCorruptionDetectionCheckBox.IsChecked == true;
    public string PrimaryBranchName =>
        PrimaryBranchNameTextBox.Text.Trim();

    public FolderProjectSetupWindow(
        LocalizationManager localizationManager,
        string title,
        string description)
    {
        _localizationManager = localizationManager;
        InitializeComponent();
        DarkTitleBarHelper.Enable(this);
        Title = title;
        DescriptionTextBlock.Text = description;
        PrimaryBranchNameTextBox.Text = "master";
        if (Application.Current?.MainWindow is { } owner &&
            !ReferenceEquals(owner, this))
        {
            Owner = owner;
        }
    }

    private void BrowseProjectFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = GetText(
                "FolderProject.Setup.SelectProjectFolder"),
            UseDescriptionForTitle = true,
            SelectedPath = ProjectFolder,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        if (HasProjectSettings(dialog.SelectedPath))
        {
            MessageBox.Show(
                GetText("FolderProject.Create.AlreadyExists"),
                GetText("FolderProject.ErrorTitle"));
            return;
        }

        ProjectFolder = dialog.SelectedPath;
        ProjectFolderTextBox.Text = ProjectFolder;
        OutputFolder = Directory.GetParent(ProjectFolder)?.FullName ??
                       ProjectFolder;
        OutputFolderTextBox.Text = OutputFolder;
    }

    private void BrowseOutputFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = GetText(
                "FolderProject.Setup.SelectOutputFolder"),
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(OutputFolder)
                ? ProjectFolder
                : OutputFolder,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        OutputFolder = dialog.SelectedPath;
        OutputFolderTextBox.Text = OutputFolder;
        if (IsSameFolder(ProjectFolder, OutputFolder))
        {
            MessageBox.Show(
                GetText("FolderProject.Setup.OutputSameAsProject"),
                GetText("FolderProject.Setup.WarningTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectFolder))
        {
            MessageBox.Show(
                GetText("FolderProject.Setup.NoProjectFolder"),
                GetText("FolderProject.ErrorTitle"));
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            MessageBox.Show(
                GetText("FolderProject.Setup.NoOutputFolder"),
                GetText("FolderProject.ErrorTitle"));
            return;
        }

        if (!FolderProjectGitRepository.IsValidBranchName(
                PrimaryBranchName))
        {
            MessageBox.Show(
                GetText("FolderProject.Setup.InvalidPrimaryBranch"),
                GetText("FolderProject.ErrorTitle"));
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private string GetText(string key)
    {
        return _localizationManager.Get(key);
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

    private static bool IsSameFolder(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) ||
            string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        var firstPath = Path.GetFullPath(first)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var secondPath = Path.GetFullPath(second)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(
            firstPath,
            secondPath,
            StringComparison.OrdinalIgnoreCase);
    }
}
