using System.Collections.ObjectModel;
using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;

namespace AssetEditorTests;

[NonParallelizable]
public class PackFileTreeOperationsTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void CopyPaste_MultipleFileAndDirectory_PreservesContentsAndEmptyFolders()
    {
        RunOnWpfThread(() =>
        {
            using var projectRoot = new TemporaryDirectory();
            projectRoot.Write(@"source\a.bin", [1]);
            projectRoot.Write(@"source\nested\b.bin", [2]);
            projectRoot.CreateDirectory(@"source\empty");
            projectRoot.Write("loose.bin", [3]);
            projectRoot.Write(@"target\existing.bin", [9]);
            using var project = FolderProjectContainer.Create(
                projectRoot.Path,
                new FolderProjectSettings
                {
                    Name = "工程",
                    EmptyDirectories = [@"source\empty"],
                });
            using var harness = CreateHarness(project);
            var root = harness.ViewModel.Files.Single();
            var source = FindNode(root, "source");
            var loose = FindNode(root, "loose.bin");
            var target = FindNode(root, "target");

            harness.Operations.Copy([source, loose]);
            harness.Operations.Paste(target);
            harness.Operations.Paste(target);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    File.ReadAllBytes(projectRoot.Resolve(@"target\source\a.bin")),
                    Is.EqualTo(new byte[] { 1 }));
                NUnitAssert.That(
                    File.ReadAllBytes(projectRoot.Resolve(@"target\source\nested\b.bin")),
                    Is.EqualTo(new byte[] { 2 }));
                NUnitAssert.That(
                    Directory.Exists(projectRoot.Resolve(@"target\source\empty")),
                    Is.True);
                NUnitAssert.That(
                    File.ReadAllBytes(projectRoot.Resolve(@"target\loose.bin")),
                    Is.EqualTo(new byte[] { 3 }));
                NUnitAssert.That(
                    File.Exists(projectRoot.Resolve(@"target\source_copy\a.bin")),
                    Is.True);
                NUnitAssert.That(
                    File.Exists(projectRoot.Resolve(@"target\loose_copy.bin")),
                    Is.True);
            });
        });
    }

    [Test]
    public void Move_MultipleFileAndDirectory_UpdatesDiskAndTree()
    {
        RunOnWpfThread(() =>
        {
            using var projectRoot = new TemporaryDirectory();
            projectRoot.Write(@"from\moving.bin", [1]);
            projectRoot.Write(@"folder-to-move\nested\child.bin", [2]);
            projectRoot.CreateDirectory(@"folder-to-move\empty");
            projectRoot.Write(@"target\keep.bin", [3]);
            using var project = FolderProjectContainer.Create(
                projectRoot.Path,
                new FolderProjectSettings { Name = "工程" });
            using var harness = CreateHarness(project);
            var root = harness.ViewModel.Files.Single();
            var movingFile = FindNode(root, @"from\moving.bin");
            var movingFolder = FindNode(root, "folder-to-move");
            var target = FindNode(root, "target");

            NUnitAssert.That(
                harness.Operations.CanMove([movingFile, movingFolder], target),
                Is.True);
            harness.Operations.Move([movingFile, movingFolder], target);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    File.Exists(projectRoot.Resolve(@"from\moving.bin")),
                    Is.False);
                NUnitAssert.That(
                    File.Exists(projectRoot.Resolve(@"target\moving.bin")),
                    Is.True);
                NUnitAssert.That(
                    Directory.Exists(projectRoot.Resolve("folder-to-move")),
                    Is.False);
                NUnitAssert.That(
                    File.Exists(projectRoot.Resolve(
                        @"target\folder-to-move\nested\child.bin")),
                    Is.True);
                NUnitAssert.That(
                    Directory.Exists(projectRoot.Resolve(
                        @"target\folder-to-move\empty")),
                    Is.True);
                NUnitAssert.That(
                    project.EmptyDirectories.Any(path => path.Equals(
                        @"target\folder-to-move\empty",
                        StringComparison.OrdinalIgnoreCase)),
                    Is.True);
                NUnitAssert.That(
                    movingFile.GetFullPath(),
                    Is.EqualTo(@"target\moving.bin").IgnoreCase);
                NUnitAssert.That(
                    movingFolder.GetFullPath(),
                    Is.EqualTo(@"target\folder-to-move").IgnoreCase);
            });
        });
    }

    [Test]
    public void Delete_ParentAndChildSelected_DeletesTheTreeOnlyOnce()
    {
        RunOnWpfThread(() =>
        {
            using var projectRoot = new TemporaryDirectory();
            projectRoot.Write(@"source\nested\child.bin", [1]);
            using var project = FolderProjectContainer.Create(
                projectRoot.Path,
                new FolderProjectSettings { Name = "工程" });
            using var harness = CreateHarness(project, confirmDelete: true);
            var root = harness.ViewModel.Files.Single();
            var source = FindNode(root, "source");
            var child = FindNode(root, @"source\nested\child.bin");

            harness.Operations.Delete([source, child]);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    Directory.Exists(projectRoot.Resolve("source")),
                    Is.False);
                NUnitAssert.That(
                    project.FileList.Keys.Any(path => path.StartsWith(
                        "source\\",
                        StringComparison.OrdinalIgnoreCase)),
                    Is.False);
            });
            NUnitAssert.That(harness.GetConfirmationCount(), Is.EqualTo(1));
        });
    }

    private static void RunOnWpfThread(Action action) =>
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            action);

    private static OperationHarness CreateHarness(
        FolderProjectContainer project,
        bool confirmDelete = false)
    {
        var eventHub = new TestEventHub();
        var service = new PackFileService(eventHub);
        service.AddEditableFolderProject(project);
        var confirmationCount = 0;
        var operations = new PackFileTreeOperations(
            service,
            new PackFileTreeClipboard(),
            () =>
            {
                confirmationCount++;
                return confirmDelete;
            });
        var contextMenu = new Mock<IContextMenuBuilder>();
        contextMenu.Setup(builder => builder.Build(It.IsAny<TreeNode?>()))
            .Returns(new ObservableCollection<ContextMenuItem2>());
        contextMenu.Setup(builder => builder.Build(
                It.IsAny<IReadOnlyList<TreeNode>>()))
            .Returns(new ObservableCollection<ContextMenuItem2>());
        var viewModel = new PackFileBrowserViewModel(
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            contextMenu.Object,
            service,
            eventHub,
            true,
            false,
            operations: operations);
        return new OperationHarness(
            operations,
            viewModel,
            () => confirmationCount);
    }

    private static TreeNode FindNode(TreeNode root, string path)
    {
        TreeNode? found = null;
        root.ForeachNode(node =>
        {
            if (node.GetFullPath().Equals(
                    path,
                    StringComparison.OrdinalIgnoreCase))
            {
                found = node;
            }
        });
        return found ?? throw new InvalidOperationException(
            $"Tree node not found: {path}");
    }

    private sealed record OperationHarness(
        PackFileTreeOperations Operations,
        PackFileBrowserViewModel ViewModel,
        Func<int> GetConfirmationCount) : IDisposable
    {
        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-tree-operations-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public string Resolve(string relativePath) =>
            System.IO.Path.Combine(Path, relativePath);

        public void CreateDirectory(string relativePath) =>
            Directory.CreateDirectory(Resolve(relativePath));

        public void Write(string relativePath, byte[] bytes)
        {
            var path = Resolve(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
