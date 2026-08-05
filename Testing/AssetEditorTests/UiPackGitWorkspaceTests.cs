using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AssetEditor.ViewModels;
using NUnit.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class UiPackGitWorkspaceTests
{
    private static readonly Regex LegacyThemeResource = new(
        @"\{DynamicResource\s+(?:ABrush\.|Button\.|TextBox\.|App\.Border|Markdown\.Hyperlink)",
        RegexOptions.CultureInvariant);

    [Test]
    public void GitWorkspace_UsesSemanticThemeResourcesAndSharedControls()
    {
        var root = FindSolutionRoot();
        var paths = new[]
        {
            Path.Combine(
                root,
                "AssetEditor",
                "Views",
                "FolderProjectVersionControl",
                "FolderProjectGitStyles.xaml"),
            Path.Combine(
                root,
                "AssetEditor",
                "Views",
                "FolderProjectVersionControl",
                "FolderProjectGitPanelView.xaml"),
            Path.Combine(
                root,
                "AssetEditor",
                "Views",
                "FolderProjectVersionControl",
                "FolderProjectGitRepositoryView.xaml"),
            Path.Combine(
                root,
                "AssetEditor",
                "Views",
                "FolderProjectVersionControl",
                "FolderProjectVersionControlWindow.xaml"),
        };
        var sources = paths.Select(File.ReadAllText).ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                sources,
                Has.All.Contains("AeBrush."));
            NUnitAssert.That(
                sources,
                Has.None.Matches<string>(source =>
                    LegacyThemeResource.IsMatch(source)));
            NUnitAssert.That(
                sources,
                Has.None.Contains("#66000000"));
            NUnitAssert.That(
                sources,
                Has.None.Contains("#88000000"));
            NUnitAssert.That(
                sources[0],
                Does.Contain("AeButton.Quiet"));
            NUnitAssert.That(
                sources[0],
                Does.Contain("AeTree.View"));
            NUnitAssert.That(
                sources[0],
                Does.Contain("AeList.View"));
            NUnitAssert.That(
                sources[0],
                Does.Contain("AeTable.Grid"));
        });
    }

    [Test]
    public void PackTree_UsesSemanticStatesWithoutHardcodedRedOrDarkOverlay()
    {
        var path = Path.Combine(
            FindSolutionRoot(),
            "Shared",
            "SharedUI",
            "BaseDialogs",
            "PackFileTree",
            "PackFileBrowserView.xaml");
        var source = File.ReadAllText(path);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source, Does.Contain("AeTree.View"));
            NUnitAssert.That(source, Does.Contain("AeButton.Icon"));
            NUnitAssert.That(source, Does.Contain("AeMenu.Context"));
            NUnitAssert.That(source, Does.Contain("AeProgress.Bar"));
            NUnitAssert.That(source, Does.Contain("AeBrush.Danger"));
            NUnitAssert.That(source, Does.Not.Contain("Value=\"Red\""));
            NUnitAssert.That(source, Does.Not.Contain("#B0202020"));
            NUnitAssert.That(LegacyThemeResource.IsMatch(source), Is.False);
        });
    }

    [Test]
    public void FolderProjectSetup_UsesSharedInputsButtonsAndStandardDialogs()
    {
        var root = FindSolutionRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "AssetEditor",
            "Views",
            "FolderProject",
            "FolderProjectSetupWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(
            root,
            "AssetEditor",
            "Views",
            "FolderProject",
            "FolderProjectSetupWindow.xaml.cs"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(xaml, Does.Contain("AeInput.TextBox"));
            NUnitAssert.That(xaml, Does.Contain("AeInput.CheckBox"));
            NUnitAssert.That(xaml, Does.Contain("AeButton.Primary"));
            NUnitAssert.That(xaml, Does.Contain("AeButton.Secondary"));
            NUnitAssert.That(xaml, Does.Contain("AeBrush."));
            NUnitAssert.That(code, Does.Contain("IStandardDialogs"));
            NUnitAssert.That(code, Does.Not.Contain("MessageBox.Show"));
        });
    }

    [Test]
    public void GitTrees_PreserveVirtualizationAndSeparateIndexBindings()
    {
        var root = FindSolutionRoot();
        var panel = XDocument.Load(Path.Combine(
            root,
            "AssetEditor",
            "Views",
            "FolderProjectVersionControl",
            "FolderProjectGitPanelView.xaml"));
        var styles = XDocument.Load(Path.Combine(
            root,
            "AssetEditor",
            "Views",
            "FolderProjectVersionControl",
            "FolderProjectGitStyles.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var trees = panel.Descendants(presentation + "TreeView")
            .ToDictionary(
                element => element.Attribute(xaml + "Name")?.Value ?? "");
        var treeStyle = styles.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "GitChangeTreeStyle");
        var setters = treeStyle.Elements(presentation + "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value ?? "",
                element => element.Attribute("Value")?.Value ?? "");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                trees["UnstagedChangesTree"].Attribute("ItemsSource")?.Value,
                Is.EqualTo("{Binding VersionControl.UnstagedChangeTree}"));
            NUnitAssert.That(
                trees["StagedChangesTree"].Attribute("ItemsSource")?.Value,
                Is.EqualTo("{Binding VersionControl.StagedChangeTree}"));
            NUnitAssert.That(
                setters["VirtualizingPanel.IsVirtualizing"],
                Is.EqualTo("True"));
            NUnitAssert.That(
                setters["VirtualizingPanel.VirtualizationMode"],
                Is.EqualTo("Recycling"));
            NUnitAssert.That(
                setters["ScrollViewer.CanContentScroll"],
                Is.EqualTo("True"));
        });
    }

    [Test]
    public void RealRepository_PreservesGitLayersAndReportsStageTimings()
    {
        using var project = new TemporaryDirectory();
        const int fileCount = 480;
        const int changedFileCount = 180;
        const int stagedFileCount = 90;
        var service = new FolderProjectVersionControlService();
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        var identity = new FolderProjectGitIdentity(
            "AssetEditor.CN UI 测试",
            "ui-test@asseteditor.cn");
        var paths = Enumerable.Range(0, fileCount)
            .Select(index =>
                $"resources/group-{index % 12:D2}/item-{index:D4}.txt")
            .ToArray();
        foreach (var path in paths)
            Write(project.Path, path, "initial");

        var timings = new Dictionary<string, long>();
        var stopwatch = Stopwatch.StartNew();
        service.Initialize(project.Path, identity, "main");
        timings["initializeMs"] = stopwatch.ElapsedMilliseconds;

        foreach (var path in paths[..changedFileCount])
            Write(project.Path, path, $"changed::{path}");

        stopwatch.Restart();
        var initialStatus = service.GetStatus(project.Path);
        timings["statusScanMs"] = stopwatch.ElapsedMilliseconds;
        var stagedPaths = paths[..stagedFileCount];
        service.StageChanges(project.Path, stagedPaths);
        service.UnstageChanges(project.Path, stagedPaths[..10]);
        var splitPath = stagedPaths[10];
        Write(project.Path, splitPath, $"changed-again::{splitPath}");
        var splitStatus = service.GetStatus(project.Path);

        stopwatch.Restart();
        var workingRows = splitStatus.Changes
            .Select(change => new FolderProjectWorkingChangeRow(
                change,
                localization))
            .ToArray();
        var unstagedTree = FolderProjectWorkingChangeTreeNode.Build(
            project.Path,
            workingRows.Where(row => row.Source.Kind.HasFlag(
                FolderProjectWorkingChangeKind.Unstaged)));
        var stagedTree = FolderProjectWorkingChangeTreeNode.Build(
            project.Path,
            workingRows.Where(row => row.Source.Kind.HasFlag(
                FolderProjectWorkingChangeKind.Staged)),
            isStagedTree: true);
        timings["treeProjectionMs"] = stopwatch.ElapsedMilliseconds;

        var splitChange = splitStatus.Changes.Single(change =>
            change.RepositoryPath == splitPath);
        var commit = service.CommitStaged(project.Path, "提交暂存资源");
        stopwatch.Restart();
        var commitChanges = service.GetCommitChanges(
            project.Path,
            commit.Id);
        timings["commitDiffMs"] = stopwatch.ElapsedMilliseconds;
        service.CreateBranch(project.Path, "feature/ui-phase-5");
        var branch = service.SwitchBranch(
            project.Path,
            "feature/ui-phase-5",
            FolderProjectBranchSwitchMode.StashChanges,
            "切换前保存 UI 验证状态");
        var stashes = service.GetStashes(project.Path);
        service.PopStash(project.Path, stashes.Single().Index);
        var restoredStatus = service.GetStatus(project.Path);

        NUnit.Framework.TestContext.Progress.WriteLine(JsonSerializer.Serialize(new
        {
            FileCount = fileCount,
            ChangedFileCount = changedFileCount,
            StagedFileCount = stagedFileCount,
            Timings = timings,
            InitialStatusCount = initialStatus.Changes.Count,
            SplitKind = splitChange.Kind.ToString(),
            UnstagedTreeCount = unstagedTree.Count,
            StagedTreeCount = stagedTree.Count,
            CommitChangeCount = commitChanges.Count,
            Branch = branch,
            RestoredStatusIsClean = restoredStatus.IsClean,
            RemainingStashCount = service.GetStashes(project.Path).Count,
        }));
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                initialStatus.Changes,
                Has.Count.EqualTo(changedFileCount));
            NUnitAssert.That(
                splitChange.Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Staged),
                Is.True,
                "The staged layer must remain visible after another disk edit.");
            NUnitAssert.That(
                splitChange.Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Unstaged),
                Is.True,
                "The post-stage disk edit must remain visible separately.");
            NUnitAssert.That(unstagedTree.Count, Is.EqualTo(1));
            NUnitAssert.That(stagedTree.Count, Is.EqualTo(1));
            NUnitAssert.That(
                commitChanges,
                Has.Count.EqualTo(stagedFileCount - 10));
            NUnitAssert.That(branch.Name, Is.EqualTo("feature/ui-phase-5"));
            NUnitAssert.That(
                branch.IsCurrent,
                Is.True,
                "The switched branch must be reported as current.");
            NUnitAssert.That(
                restoredStatus.IsClean,
                Is.False,
                "Popping the stash must restore working-tree changes.");
            NUnitAssert.That(service.GetStashes(project.Path), Is.Empty);
        });
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the solution root.");
    }

    private static void Write(
        string projectRoot,
        string repositoryPath,
        string contents)
    {
        var fullPath = Path.Combine(
            projectRoot,
            repositoryPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-ui-pack-git-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (!Directory.Exists(Path))
                return;

            foreach (var file in Directory.EnumerateFiles(
                         Path,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Path, recursive: true);
        }
    }
}
