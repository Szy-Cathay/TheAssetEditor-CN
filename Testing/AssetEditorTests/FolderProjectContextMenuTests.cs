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
}
