using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Services;
using AssetEditor.ViewModels;
using AssetEditor.Views.FolderProject;
using AssetEditor.Views.FolderProjectVersionControl;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiPackGitWorkspaceGallery
{
    private static readonly string[] Variants =
    [
        "pack-tree",
        "working",
        "repository",
        "merge",
        "setup",
        "dirty-switch",
        "loading",
    ];

    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [TestCaseSource(nameof(Cases))]
    public void PackGitWorkspace_RendersRequiredThemeAndState(
        ThemeType theme,
        string variant)
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () => Render(theme, variant));
    }

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var theme in new[]
                 {
                     ThemeType.DarkTheme,
                     ThemeType.LightTheme,
                     ThemeType.HighContrastDark,
                     ThemeType.HighContrastLight,
                 })
        {
            foreach (var variant in Variants)
                yield return new TestCaseData(theme, variant);
        }
    }

    private static void Render(ThemeType theme, string variant)
    {
        var previousTheme = ThemesController.CurrentTheme;
        Window? window = null;
        PackFileBrowserViewModel? packTree = null;
        try
        {
            ThemesController.SetTheme(theme);
            if (variant == "setup")
            {
                window = new FolderProjectSetupWindow(
                    LocalizationManager.Instance,
                    Mock.Of<IStandardDialogs>(),
                    "创建文件夹工程",
                    "选择工程与输出目录。工程会使用本地 Git 记录资源变更。")
                {
                    WindowStyle = WindowStyle.None,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
            }
            else if (variant == "pack-tree")
            {
                packTree = CreatePackTree();
                window = Host(
                    new PackFileBrowserView
                    {
                        DataContext = packTree,
                        ShowTitle = true,
                    },
                    360,
                    640);
            }
            else
            {
                var workspace = CreateWorkspace(out var versionControl);
                FrameworkElement content;
                if (variant == "repository")
                {
                    SetBackingField(
                        versionControl,
                        "_selectedHistoryBranch",
                        versionControl.Branches[0]);
                    SetBackingField(
                        versionControl,
                        "_selectedCommit",
                        versionControl.History[0]);
                    var repository = new FolderProjectGitRepositoryViewModel();
                    typeof(FolderProjectGitRepositoryViewModel)
                        .GetProperty(
                            nameof(FolderProjectGitRepositoryViewModel.Workspace),
                            BindingFlags.Instance |
                            BindingFlags.Public |
                            BindingFlags.NonPublic)!
                        .SetValue(repository, workspace);
                    content = new FolderProjectGitRepositoryView
                    {
                        DataContext = repository,
                    };
                    window = Host(content, 1120, 720);
                }
                else if (variant == "merge")
                {
                    versionControl.MergePhase =
                        FolderProjectMergePhase.Conflicts;
                    versionControl.MergeSummary =
                        "feature/empire 与 master 有 2 个资源冲突";
                    versionControl.MergeMessage =
                        "合并帝国将军资源调整";
                    AddMergeConflict(versionControl, "variantmeshes/empire.xml");
                    AddMergeConflict(versionControl, "textures/general.dds");
                    window = new FolderProjectVersionControlWindow
                    {
                        Width = 820,
                        Height = 600,
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                        DataContext = versionControl,
                    };
                }
                else
                {
                    if (variant == "dirty-switch")
                    {
                        versionControl.PendingBranchName = "feature/empire";
                        versionControl.IsBranchSwitchChoiceOpen = true;
                    }
                    else if (variant == "loading")
                    {
                        versionControl.LoadingProgressStatusText =
                            "正在扫描 Git 工作树";
                        versionControl.LoadingProgressDetailText =
                            "variantmeshes/wh_variantmodels";
                        versionControl.LoadingProgressMaximum = 2100;
                        versionControl.LoadingProgressValue = 1428;
                        versionControl.LoadingProgressIsIndeterminate = false;
                        versionControl.IsStatusRefreshing = true;
                    }

                    content = new FolderProjectGitPanelView
                    {
                        DataContext = workspace,
                    };
                    window = Host(content, 430, 720);
                }
            }

            window.Show();
            window.UpdateLayout();
            if (variant is "repository" or "merge")
            {
                var grid = FindDescendants<DataGrid>(window).Single();
                var minimumWidth = variant == "merge" ? 360 : 240;
                NUnitAssert.That(
                    grid.Columns[0].ActualWidth,
                    Is.GreaterThanOrEqualTo(minimumWidth));
            }
            Capture(window, theme, variant);
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(window.ActualWidth, Is.GreaterThan(300));
                NUnitAssert.That(window.ActualHeight, Is.GreaterThan(300));
                NUnitAssert.That(window.Content, Is.Not.Null);
            });
        }
        finally
        {
            window?.Close();
            packTree?.Dispose();
            ThemesController.SetTheme(previousTheme);
        }
    }

    private static Window Host(
        FrameworkElement content,
        double width,
        double height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.SetResourceReference(
            Control.BackgroundProperty,
            "AeBrush.Canvas");
        return window;
    }

    private static PackFileBrowserViewModel CreatePackTree()
    {
        var packFileService = new Mock<IPackFileService>();
        packFileService
            .Setup(service => service.GetAllPackfileContainers())
            .Returns([]);
        var model = new PackFileBrowserViewModel(
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IContextMenuBuilder>(),
            packFileService.Object,
            null,
            showCaFiles: false,
            showFoldersOnly: false);
        var pack = new PackFileContainer("帝国将军.pack");
        var root = new TreeNode(
            "帝国将军.pack",
            NodeType.Root,
            pack,
            null)
        {
            IsMainEditabelPack = true,
            IsNodeExpanded = true,
        };
        var variants = AddNode(
            root,
            "variantmeshes",
            NodeType.Directory,
            expanded: true);
        AddNode(variants, "wh_variantmodels", NodeType.Directory, true);
        AddNode(variants, "emp_general_variant.xml", NodeType.File)
            .UnsavedChanged = true;
        AddNode(root, "animations", NodeType.Directory);
        AddNode(root, "textures", NodeType.Directory);
        AddNode(root, "audio", NodeType.Directory).IsIgnored = true;
        model.Files.Add(root);
        return model;
    }

    private static TreeNode AddNode(
        TreeNode parent,
        string name,
        NodeType type,
        bool expanded = false)
    {
        var node = new TreeNode(
            name,
            type,
            parent.FileOwner,
            parent)
        {
            IsNodeExpanded = expanded,
        };
        parent.Children.Add(node);
        return node;
    }

    private static FolderProjectGitWorkspaceViewModel CreateWorkspace(
        out FolderProjectVersionControlViewModel versionControl)
    {
        versionControl = new FolderProjectVersionControlViewModel(
            Mock.Of<IFolderProjectVersionControlService>(),
            Mock.Of<IFolderProjectGitOperationCoordinator>(),
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IFolderProjectUnsavedChangesService>(),
            Mock.Of<IFolderProjectUnsavedChangesPrompt>(),
            LocalizationManager.Instance)
        {
            ProjectRoot = @"D:\Mods\帝国将军",
            ProjectName = "帝国将军",
            IsInitialized = true,
            HasRepositorySnapshot = true,
            CurrentBranch = "master",
            PrimaryBranchName = "master",
            HeadCommitId = "77ad9f849e71",
            IsClean = false,
            HasIdentity = true,
            IdentityName = "Cathay Modder",
            IdentityEmail = "modder@example.cn",
            CommitMessage = "调整帝国将军模型与材质",
            StatusMessage = "3 项更改 · 1 项已暂存",
        };
        versionControl.Branches.Add(
            new FolderProjectBranchInfo("master", "77ad9f8", true, true));
        versionControl.Branches.Add(
            new FolderProjectBranchInfo("feature/empire", "c304c19", false));
        versionControl.Branches.Add(
            new FolderProjectBranchInfo("backup/before-audio", "904c8ee", false));
        versionControl.SelectedBranch = versionControl.Branches[1];
        versionControl.MergeSources.Add(versionControl.Branches[1]);
        versionControl.MergeTargets.Add(versionControl.Branches[0]);
        versionControl.SelectedMergeSource = versionControl.Branches[1];
        versionControl.SelectedMergeTarget = versionControl.Branches[0];
        versionControl.Stashes.Add(new FolderProjectStashInfo(
            0,
            "切换分支前的资源调整",
            DateTimeOffset.Now.AddMinutes(-18),
            ["variantmeshes/empire.xml"]));

        AddWorkingChange(
            versionControl,
            "variantmeshes/wh_variantmodels/emp_general.xml",
            FolderProjectWorkingChangeKind.Modified |
            FolderProjectWorkingChangeKind.Staged |
            FolderProjectWorkingChangeKind.Unstaged);
        AddWorkingChange(
            versionControl,
            "textures/emp_general_diffuse.dds",
            FolderProjectWorkingChangeKind.Modified |
            FolderProjectWorkingChangeKind.Unstaged);
        AddWorkingChange(
            versionControl,
            "audio/emp_general_voice.wav",
            FolderProjectWorkingChangeKind.Untracked |
            FolderProjectWorkingChangeKind.Unstaged);
        RebuildWorkingTrees(versionControl);

        var commits = new[]
        {
            new FolderProjectCommitSummary(
                "77ad9f849e71",
                "调整帝国将军模型",
                "Cathay Modder",
                "modder@example.cn",
                DateTimeOffset.Now.AddMinutes(-12),
                ["c304c19"]),
            new FolderProjectCommitSummary(
                "c304c1977be0",
                "补充中文资源",
                "Cathay Modder",
                "modder@example.cn",
                DateTimeOffset.Now.AddHours(-2),
                ["904c8ee"]),
            new FolderProjectCommitSummary(
                "904c8ee12ab4",
                "创建文件夹工程",
                "Cathay Modder",
                "modder@example.cn",
                DateTimeOffset.Now.AddDays(-1),
                []),
        };
        foreach (var commit in commits)
            versionControl.History.Add(commit);

        var commitRows = new[]
        {
            new FolderProjectCommitChangeRow(
                new FolderProjectCommitChange(
                    "variantmeshes/wh_variantmodels/emp_general.xml",
                    null,
                    FolderProjectCommitChangeKind.Modified,
                    false),
                LocalizationManager.Instance),
            new FolderProjectCommitChangeRow(
                new FolderProjectCommitChange(
                    "textures/emp_general_diffuse.dds",
                    null,
                    FolderProjectCommitChangeKind.Added,
                    true),
                LocalizationManager.Instance),
        };
        foreach (var row in commitRows)
            versionControl.CommitChanges.Add(row);
        foreach (var node in FolderProjectCommitChangeTreeNode.Build(
                     versionControl.ProjectName,
                     commitRows))
        {
            versionControl.CommitChangeTree.Add(node);
        }

        return new FolderProjectGitWorkspaceViewModel(
            versionControl,
            Mock.Of<IEditorManager>(),
            Mock.Of<IFolderProjectVersionControlWindowService>())
        {
            IsEnabled = true,
        };
    }

    private static void AddWorkingChange(
        FolderProjectVersionControlViewModel viewModel,
        string path,
        FolderProjectWorkingChangeKind kind)
    {
        var row = new FolderProjectWorkingChangeRow(
            new FolderProjectWorkingChange(path, kind),
            LocalizationManager.Instance);
        viewModel.WorkingChanges.Add(row);
        if (kind.HasFlag(FolderProjectWorkingChangeKind.Staged))
            viewModel.StagedChanges.Add(row);
        if (kind.HasFlag(FolderProjectWorkingChangeKind.Unstaged))
            viewModel.UnstagedChanges.Add(row);
    }

    private static void RebuildWorkingTrees(
        FolderProjectVersionControlViewModel viewModel)
    {
        foreach (var node in FolderProjectWorkingChangeTreeNode.Build(
                     viewModel.ProjectName,
                     viewModel.UnstagedChanges))
        {
            viewModel.UnstagedChangeTree.Add(node);
        }
        foreach (var node in FolderProjectWorkingChangeTreeNode.Build(
                     viewModel.ProjectName,
                     viewModel.StagedChanges,
                     isStagedTree: true))
        {
            viewModel.StagedChangeTree.Add(node);
        }
    }

    private static void AddMergeConflict(
        FolderProjectVersionControlViewModel viewModel,
        string path)
    {
        FolderProjectMergeSide Side(long size) => new(
            path,
            "0123456789abcdef",
            FolderProjectGitFileMode.NonExecutable,
            size,
            path.EndsWith(".dds", StringComparison.Ordinal));
        viewModel.MergeConflicts.Add(new FolderProjectMergeConflictRow(
            new FolderProjectMergeConflict(
                path,
                Side(9216),
                Side(9450),
                Side(9730)),
            LocalizationManager.Instance));
    }

    private static void SetBackingField<T>(
        object instance,
        string fieldName,
        T value)
    {
        instance.GetType()
            .GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);
    }

    private static void Capture(
        Window window,
        ThemeType theme,
        string variant)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(
                window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(
                window.ActualHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(window);
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(bitmap.PixelWidth, Is.GreaterThan(0));
            NUnitAssert.That(bitmap.PixelHeight, Is.GreaterThan(0));
        });

        var outputDirectory = Environment.GetEnvironmentVariable(
            "AE_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(
            outputDirectory,
            $"pack-git-{variant}-{theme}.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }
}
