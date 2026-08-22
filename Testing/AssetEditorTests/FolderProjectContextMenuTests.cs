using AssetEditor.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class FolderProjectContextMenuTests
{
    [NUnit.Framework.Test]
    public void FolderProjectRootRename_PersistsProjectDisplayName()
    {
        new LocalizationManager().LoadLanguage();
        using var projectRoot = new TemporaryDirectory();
        var outputPackPath = Path.Combine(
            Path.GetTempPath(),
            $"ae-folder-project-output-{Guid.NewGuid():N}.pack");
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings
            {
                Name = "旧工程名",
                OutputPackPath = outputPackPath,
            });
        var root = new TreeNode(
            project.Name,
            NodeType.Root,
            project,
            null);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowTextInputDialog(
                "重命名工程",
                "旧工程名"))
            .Returns(new TextInputDialogResult(true, "  新工程名  "));
        var command = new OnRenameNodeCommand(
            Mock.Of<IPackFileService>(),
            dialogs.Object);

        command.Execute(root);

        var savedSettings = FolderProjectSettings.Load(projectRoot.Path);
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.Name, NUnit.Framework.Is.EqualTo("新工程名"));
            NUnitAssert.That(
                project.Name,
                NUnit.Framework.Is.EqualTo("新工程名"));
            NUnitAssert.That(
                project.ProjectSettings.Name,
                NUnit.Framework.Is.EqualTo("新工程名"));
            NUnitAssert.That(
                savedSettings.Name,
                NUnit.Framework.Is.EqualTo("新工程名"));
            NUnitAssert.That(
                project.ProjectRoot,
                NUnit.Framework.Is.EqualTo(projectRoot.Path));
            NUnitAssert.That(
                project.ProjectSettings.OutputPackPath,
                NUnit.Framework.Is.EqualTo(outputPackPath));
            NUnitAssert.That(
                savedSettings.OutputPackPath,
                NUnit.Framework.Is.EqualTo(outputPackPath));
            NUnitAssert.That(
                root.UnsavedChanged,
                NUnit.Framework.Is.True);
        });
    }

    [NUnit.Framework.Test]
    public void FolderProjectRootRename_SaveFailurePreservesOldName()
    {
        new LocalizationManager().LoadLanguage();
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "旧工程名" });
        var root = new TreeNode(
            project.Name,
            NodeType.Root,
            project,
            null);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowTextInputDialog(
                "重命名工程",
                "旧工程名"))
            .Returns(new TextInputDialogResult(true, "新工程名"));
        var command = new OnRenameNodeCommand(
            Mock.Of<IPackFileService>(),
            dialogs.Object);
        var settingsPath = Path.Combine(
            projectRoot.Path,
            FolderProjectSettings.CnFileName);

        using (File.Open(
                   settingsPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.None))
        {
            NUnitAssert.DoesNotThrow(() => command.Execute(root));
        }

        var savedSettings = FolderProjectSettings.Load(projectRoot.Path);
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.Name, NUnit.Framework.Is.EqualTo("旧工程名"));
            NUnitAssert.That(
                project.Name,
                NUnit.Framework.Is.EqualTo("旧工程名"));
            NUnitAssert.That(
                project.ProjectSettings.Name,
                NUnit.Framework.Is.EqualTo("旧工程名"));
            NUnitAssert.That(
                savedSettings.Name,
                NUnit.Framework.Is.EqualTo("旧工程名"));
            NUnitAssert.That(
                root.UnsavedChanged,
                NUnit.Framework.Is.False);
        });
        dialogs.Verify(item => item.ShowExceptionWindow(
            It.IsAny<Exception>(),
            "无法重命名工程。请检查工程目录权限或配置文件是否正被占用。"),
            Times.Once);
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Apartment(ApartmentState.STA)]
    public void CopyFromReferencePack_TargetsActiveFolderProject()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        var reference = new PackFileContainer("参考 Pack");
        var file = PackFile.CreateFromBytes("file.bin", [1]);
        reference.FileList["file.bin"] = file;
        var root = new TreeNode(
            reference.Name,
            NodeType.Root,
            reference,
            null);
        var fileNode = new TreeNode(
            file.Name,
            NodeType.File,
            reference,
            root,
            file);
        root.Children.Add(fileNode);
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(item => item.GetEditablePack())
            .Returns(project);
        var command = new CopyToFolderProjectCommand(
            packFileService.Object,
            Mock.Of<IStandardDialogs>());

        command.Execute(fileNode);

        packFileService.Verify(item =>
            item.CopyFileFromOtherPackFile(
                reference,
                "file.bin",
                project),
            Times.Once);
    }

    [NUnit.Framework.Test]
    public void ToggleIgnore_PersistsAndUpdatesSelectedSubtree()
    {
        var projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"ae-folder-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(
            Path.Combine(projectRoot, "folder"));
        File.WriteAllBytes(
            Path.Combine(projectRoot, "folder", "file.bin"),
            [1]);

        try
        {
            using var project = FolderProjectContainer.Create(
                projectRoot,
                new FolderProjectSettings { Name = "工程" });
            var root = new TreeNode(
                project.Name,
                NodeType.Root,
                project,
                null);
            var folder = new TreeNode(
                "folder",
                NodeType.Directory,
                project,
                root);
            var file = new TreeNode(
                "file.bin",
                NodeType.File,
                project,
                folder,
                project.FileList[@"folder\file.bin"]);
            root.Children.Add(folder);
            folder.Children.Add(file);
            var command = new ToggleFolderProjectIgnoreCommand();

            command.Execute(folder);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    project.ProjectSettings.IgnoredPaths,
                    NUnit.Framework.Does.Contain("folder"));
                NUnitAssert.That(root.UnsavedChanged, NUnit.Framework.Is.True);
                NUnitAssert.That(folder.IsIgnored, NUnit.Framework.Is.True);
                NUnitAssert.That(file.IsIgnored, NUnit.Framework.Is.True);
            });

            command.Execute(folder);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    project.ProjectSettings.IgnoredPaths,
                    NUnit.Framework.Does.Not.Contain("folder"));
                NUnitAssert.That(folder.IsIgnored, NUnit.Framework.Is.False);
                NUnitAssert.That(file.IsIgnored, NUnit.Framework.Is.False);
            });
        }
        finally
        {
            Directory.Delete(projectRoot, true);
        }
    }

    [NUnit.Framework.Test]
    public void ExternalFolderProjectUpdate_MarksGeneratedPackAsOutOfDate()
    {
        var projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"ae-folder-dirty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllBytes(
            Path.Combine(projectRoot, "file.bin"),
            [1]);

        try
        {
            using var project = FolderProjectContainer.Create(
                projectRoot,
                new FolderProjectSettings { Name = "工程" });
            var eventHub = new TestEventHub();
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.GetAllPackfileContainers())
                .Returns([project]);
            packFileService
                .Setup(service => service.GetEditablePack())
                .Returns(project);
            var contextMenuBuilder = new Mock<IContextMenuBuilder>();
            using var viewModel = new PackFileBrowserViewModel(
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                contextMenuBuilder.Object,
                packFileService.Object,
                eventHub,
                true,
                false);

            NUnitAssert.That(
                viewModel.Files.Single().UnsavedChanged,
                NUnit.Framework.Is.False);

            eventHub.Publish(
                new PackFileContainerFilesUpdatedEvent(
                    project,
                    [project.FileList["file.bin"]]));

            NUnitAssert.That(
                viewModel.Files.Single().UnsavedChanged,
                NUnit.Framework.Is.True);
        }
        finally
        {
            Directory.Delete(projectRoot, true);
        }
    }

    [NUnit.Framework.Test]
    public void ToggleCorruptionDetection_PersistsAndMarksGeneratedPackOutOfDate()
    {
        new LocalizationManager().LoadLanguage();
        var projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"ae-folder-corruption-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try
        {
            using var project = FolderProjectContainer.Create(
                projectRoot,
                new FolderProjectSettings { Name = "工程" });
            var root = new TreeNode(
                project.Name,
                NodeType.Root,
                project,
                null);
            var command =
                new ToggleFolderProjectCorruptionDetectionCommand();

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    project.ProjectSettings
                        .EnablePackFileCorruptionDetection,
                    NUnit.Framework.Is.False);
                NUnitAssert.That(
                    command.IsEnabled(root),
                    NUnit.Framework.Is.True);
                NUnitAssert.That(
                    command.GetDisplayName(root),
                    NUnit.Framework.Is.EqualTo(
                        "启用 Pack 保存损坏检测"));
            });

            command.Execute(root);

            var reopened = FolderProjectSettings.Load(projectRoot);
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    project.ProjectSettings
                        .EnablePackFileCorruptionDetection,
                    NUnit.Framework.Is.True);
                NUnitAssert.That(
                    reopened.EnablePackFileCorruptionDetection,
                    NUnit.Framework.Is.True);
                NUnitAssert.That(
                    root.UnsavedChanged,
                    NUnit.Framework.Is.True);
                NUnitAssert.That(
                    command.GetDisplayName(root),
                    NUnit.Framework.Is.EqualTo(
                        "关闭 Pack 保存损坏检测"));
            });

            root.UnsavedChanged = false;
            command.Execute(root);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    project.ProjectSettings
                        .EnablePackFileCorruptionDetection,
                    NUnit.Framework.Is.False);
                NUnitAssert.That(
                    FolderProjectSettings.Load(projectRoot)
                        .EnablePackFileCorruptionDetection,
                    NUnit.Framework.Is.False);
                NUnitAssert.That(
                    root.UnsavedChanged,
                    NUnit.Framework.Is.True);
            });
        }
        finally
        {
            Directory.Delete(projectRoot, true);
        }
    }

    [NUnit.Framework.Test]
    public void CorruptionDetectionCommand_IsAvailableOnlyOnFolderProjectRoot()
    {
        var serviceProvider =
            new DependencyInjectionConfig(false).Build(true);
        var projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"ae-folder-corruption-menu-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<LocalizationManager>()
                .LoadLanguage();
            var command = services.GetRequiredService<
                ToggleFolderProjectCorruptionDetectionCommand>();

            using var project = FolderProjectContainer.Create(
                projectRoot,
                new FolderProjectSettings { Name = "工程" });
            services.GetRequiredService<IPackFileService>()
                .AddEditableFolderProject(project);
            var folderRoot = new TreeNode(
                project.Name,
                NodeType.Root,
                project,
                null);
            var folderFile = new TreeNode(
                "file.bin",
                NodeType.File,
                project,
                folderRoot,
                PackFile.CreateFromBytes("file.bin", [1]));
            var ordinaryPack = new PackFileContainer("普通 Pack")
            {
                Header = new PFHeader(
                    PackFileVersionConverter.ToString(
                        PackFileVersion.PFH5),
                    PackFileCAType.MOD),
            };
            var ordinaryRoot = new TreeNode(
                ordinaryPack.Name,
                NodeType.Root,
                ordinaryPack,
                null);
            var menuBuilder = services
                .GetServices<IContextMenuBuilder>()
                .Single(
                    builder =>
                        builder.Type ==
                        ContextMenuType.MainApplication);
            var displayName = command.GetDisplayName(folderRoot);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    ContainsMenuItem(
                        menuBuilder.Build(folderRoot),
                        displayName),
                    NUnit.Framework.Is.True);
                NUnitAssert.That(
                    ContainsMenuItem(
                        menuBuilder.Build(folderFile),
                        displayName),
                    NUnit.Framework.Is.False);
                NUnitAssert.That(
                    ContainsMenuItem(
                        menuBuilder.Build(ordinaryRoot),
                        displayName),
                    NUnit.Framework.Is.False);
            });
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
            Directory.Delete(projectRoot, true);
        }
    }

    [NUnit.Framework.Test]
    public void FolderProjectContextMenu_DoesNotOfferSaveProjectFolder()
    {
        var serviceProvider =
            new DependencyInjectionConfig(false).Build(true);
        var projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"ae-folder-save-menu-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<LocalizationManager>()
                .LoadLanguage();
            using var project = FolderProjectContainer.Create(
                projectRoot,
                new FolderProjectSettings { Name = "工程" });
            var folderRoot = new TreeNode(
                project.Name,
                NodeType.Root,
                project,
                null);
            var folder = new TreeNode(
                "子目录",
                NodeType.Directory,
                project,
                folderRoot);
            var ordinaryPack = new PackFileContainer("普通 Pack")
            {
                Header = new PFHeader(
                    PackFileVersionConverter.ToString(
                        PackFileVersion.PFH5),
                    PackFileCAType.MOD),
            };
            var ordinaryRoot = new TreeNode(
                ordinaryPack.Name,
                NodeType.Root,
                ordinaryPack,
                null);
            var menuBuilder = services
                .GetServices<IContextMenuBuilder>()
                .Single(
                    builder =>
                        builder.Type ==
                        ContextMenuType.MainApplication);
            const string displayName = "保存工程文件夹";

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    ContainsMenuItem(
                        menuBuilder.Build(folderRoot),
                        displayName),
                    NUnit.Framework.Is.False);
                NUnitAssert.That(
                    ContainsMenuItem(
                        menuBuilder.Build(folder),
                        displayName),
                    NUnit.Framework.Is.False);
                NUnitAssert.That(
                    ContainsMenuItem(
                        menuBuilder.Build(ordinaryRoot),
                        displayName),
                    NUnit.Framework.Is.False);
            });
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
            Directory.Delete(projectRoot, true);
        }
    }

    [NUnit.Framework.Test]
    public void InactiveFolderProject_CanBecomeCurrentFromContextMenu()
    {
        var serviceProvider =
            new DependencyInjectionConfig(false).Build(true);
        using var firstRoot = new TemporaryDirectory();
        using var secondRoot = new TemporaryDirectory();
        IPackFileService? packFileService = null;
        FolderProjectContainer? firstProject = null;
        FolderProjectContainer? secondProject = null;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<LocalizationManager>()
                .LoadLanguage();
            packFileService =
                services.GetRequiredService<IPackFileService>();
            packFileService.EnforceGameFilesMustBeLoaded = false;
            firstProject = FolderProjectContainer.Create(
                firstRoot.Path,
                new FolderProjectSettings { Name = "工程一" });
            secondProject = FolderProjectContainer.Create(
                secondRoot.Path,
                new FolderProjectSettings { Name = "工程二" });
            packFileService.AddEditableFolderProject(firstProject);
            packFileService.AddEditableFolderProject(secondProject);
            var firstProjectRoot = new TreeNode(
                firstProject.Name,
                NodeType.Root,
                firstProject,
                null);
            var menuBuilder = services
                .GetServices<IContextMenuBuilder>()
                .Single(builder =>
                    builder.Type == ContextMenuType.MainApplication);

            var menuItem = FindMenuItem(
                menuBuilder.Build(firstProjectRoot),
                "设为可编辑 Pack");

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    packFileService.GetEditablePack(),
                    NUnit.Framework.Is.SameAs(secondProject));
                NUnitAssert.That(
                    menuItem,
                    NUnit.Framework.Is.Not.Null);
                NUnitAssert.That(
                    menuItem?.Command?.CanExecute(null),
                    NUnit.Framework.Is.True);
            });

            menuItem!.Command!.Execute(null);

            NUnitAssert.That(
                packFileService.GetEditablePack(),
                NUnit.Framework.Is.SameAs(firstProject));
        }
        finally
        {
            if (packFileService != null)
            {
                if (secondProject != null)
                    packFileService.UnloadPackContainer(secondProject);
                if (firstProject != null)
                    packFileService.UnloadPackContainer(firstProject);
            }
            else
            {
                secondProject?.Dispose();
                firstProject?.Dispose();
            }

            (serviceProvider as IDisposable)?.Dispose();
        }
    }

    [NUnit.Framework.Test]
    public void PackTreeContextMenus_ContainNoEnglishSectionNames()
    {
        var serviceProvider = new DependencyInjectionConfig(false).Build(true);
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<LocalizationManager>().LoadLanguage();
        var pack = new PackFileContainer("测试 Pack")
        {
            Header = new PFHeader(
                PackFileVersionConverter.ToString(PackFileVersion.PFH5),
                PackFileCAType.MOD),
        };
        var root = new TreeNode(
            pack.Name,
            NodeType.Root,
            pack,
            null);
        var file = new TreeNode(
            "file.bin",
            NodeType.File,
            pack,
            root,
            PackFile.CreateFromBytes("file.bin", [1]));
        root.Children.Add(file);
        var menuBuilder = services
            .GetServices<IContextMenuBuilder>()
            .Single(
                builder =>
                    builder.Type == ContextMenuType.MainApplication);

        var names = FlattenMenuNames(menuBuilder.Build(root))
            .Concat(FlattenMenuNames(menuBuilder.Build(file)))
            .ToList();

        NUnitAssert.That(
            names,
            NUnit.Framework.Does.Not.Contain("Open")
                .And.Not.Contain("Import")
                .And.Not.Contain("Create"));
    }

    [NUnit.Framework.Test]
    public void ReferencePackContextMenu_IsReadOnlyAndExplainsMissingProject()
    {
        var serviceProvider =
            new DependencyInjectionConfig(false).Build(true);
        try
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<LocalizationManager>()
                .LoadLanguage();
            var packFileService =
                services.GetRequiredService<IPackFileService>();
            packFileService.EnforceGameFilesMustBeLoaded = false;
            var reference = new PackFileContainer("参考 Pack")
            {
                Header = new PFHeader(
                    PackFileVersionConverter.ToString(
                        PackFileVersion.PFH5),
                    PackFileCAType.MOD),
                SystemFilePath = @"D:\packs\reference.pack",
            };
            packFileService.AddReferencePack(reference);
            var root = new TreeNode(
                reference.Name,
                NodeType.Root,
                reference,
                null);
            var file = new TreeNode(
                "file.bin",
                NodeType.File,
                reference,
                root,
                PackFile.CreateFromBytes("file.bin", [1]));
            root.Children.Add(file);
            var menuBuilder = services
                .GetServices<IContextMenuBuilder>()
                .Single(builder =>
                    builder.Type == ContextMenuType.MainApplication);

            var rootItems = menuBuilder.Build(root);
            var fileItems = menuBuilder.Build(file);
            var names = FlattenMenuNames(rootItems)
                .Concat(FlattenMenuNames(fileItems))
                .ToList();
            var copyItem = FindMenuItem(
                fileItems,
                "复制到当前工程（请先新建或打开工程）");

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    copyItem,
                    NUnit.Framework.Is.Not.Null);
                NUnitAssert.That(
                    copyItem?.Command?.CanExecute(null),
                    NUnit.Framework.Is.False);
                NUnitAssert.That(
                    names,
                    NUnit.Framework.Does.Not.Contain("设为可编辑 Pack")
                        .And.Not.Contain("保存")
                        .And.Not.Contain("另存为")
                        .And.Not.Contain("导入文件")
                        .And.Not.Contain("导入目录")
                        .And.Not.Contain("重命名")
                        .And.Not.Contain("删除")
                        .And.Not.Contain("复制"));
            });
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
        }
    }

    private static bool ContainsMenuItem(
        IEnumerable<ContextMenuItem2?> items,
        string displayName)
    {
        return items.Any(
            item =>
                item != null &&
                (item.Name == displayName ||
                 ContainsMenuItem(
                     item.ContextMenu,
                     displayName)));
    }

    private static ContextMenuItem2? FindMenuItem(
        IEnumerable<ContextMenuItem2?> items,
        string displayName)
    {
        foreach (var item in items)
        {
            if (item == null)
                continue;
            if (item.Name == displayName)
                return item;
            var child = FindMenuItem(item.ContextMenu, displayName);
            if (child != null)
                return child;
        }

        return null;
    }

    private static IEnumerable<string> FlattenMenuNames(
        IEnumerable<ContextMenuItem2?> items)
    {
        foreach (var item in items)
        {
            if (item == null)
                continue;

            yield return item.Name;
            foreach (var name in FlattenMenuNames(item.ContextMenu))
                yield return name;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-reference-copy-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
