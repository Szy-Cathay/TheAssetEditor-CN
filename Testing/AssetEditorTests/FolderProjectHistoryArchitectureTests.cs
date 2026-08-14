using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public sealed class FolderProjectHistoryArchitectureTests
{
    [Test]
    public void UserInterface_DoesNotExposeAdvancedGitSurface()
    {
        var root = FindSolutionRoot();
        var removedFiles = new[]
        {
            "AssetEditor/UiCommands/OpenFolderProjectVersionControlCommand.cs",
            "AssetEditor/ViewModels/FolderProjectVersionControlViewModel.cs",
            "AssetEditor/ViewModels/FolderProjectGitWorkspaceViewModel.cs",
            "AssetEditor/Views/FolderProjectVersionControl/FolderProjectVersionControlWindow.xaml",
            "AssetEditor/Views/FolderProjectVersionControl/FolderProjectGitPanelView.xaml",
        };
        var forbiddenAdvancedOperations = new[]
        {
            "StageChanges",
            "UnstageChanges",
            "CommitStaged",
            "UndoLatestCommit",
            "ResetToCommit",
            "RevertCommit",
            "EditLatestCommitChanges",
            "CompleteLatestCommitEdit",
            "GetStashes",
            "StashChanges",
            "ApplyStash",
            "PopStash",
            "DeleteStash",
            "ClearStashes",
            "GetBranches",
            "CreateBranch",
            "RenameBranch",
            "DeleteBranch",
            "SwitchBranch",
            "GetMergeState",
            "BeginMerge",
            "ResolveMergeConflict",
            "CompleteMerge",
            "AbortMerge",
            "GetIdentity",
            "SetIdentity",
        };
        var surfaceFiles = Directory
            .EnumerateFiles(
                Path.Combine(root, "AssetEditor"),
                "*.*",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                               $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                               StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains(
                               $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                               StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".xaml", StringComparison.Ordinal) ||
                           path.EndsWith("ViewModel.cs", StringComparison.Ordinal) ||
                           path.Contains(
                               $"{Path.DirectorySeparatorChar}UiCommands{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal) ||
                           path.EndsWith("Language_Cn.json", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();
        var publicSurface = string.Join(Environment.NewLine, surfaceFiles);

        NUnitAssert.Multiple(() =>
        {
            foreach (var relativePath in removedFiles)
            {
                NUnitAssert.That(
                    File.Exists(Path.Combine(root, relativePath)),
                    Is.False,
                    relativePath);
            }
            NUnitAssert.That(
                publicSurface,
                Does.Not.Contain("FolderProject.VersionControl"));
            NUnitAssert.That(
                publicSurface,
                Does.Not.Contain("FolderProject.Git"));
            NUnitAssert.That(
                publicSurface,
                Does.Not.Contain("OpenFolderProjectGitPanelEvent"));
            foreach (var operation in forbiddenAdvancedOperations)
            {
                NUnitAssert.That(
                    publicSurface,
                    Does.Not.Contain(operation),
                    operation);
            }
        });
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AssetEditor.CN.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate solution root.");
    }
}
