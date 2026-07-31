using System.Reflection;
using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

namespace AssetEditorTests
{
    [TestClass]
    public class PackFileExportCommandTests
    {
        private static readonly string OutputRoot = Path.Combine(Path.GetTempPath(), "AssetEditorExportTests");

        [TestMethod]
        public void Execute_RootNode_ExportsMultipleFilesAcrossTwoDirectoryLevels()
        {
            var root = CreateRoot();
            var models = AddDirectory(root, "models");
            var units = AddDirectory(models, "units");
            AddFile(units, "first.bin", [1, 2]);
            AddFile(units, "second.bin", [3, 4]);
            var writes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var completedCounts = new List<int>();
            var command = CreateCommand(
                () => OutputRoot,
                (path, data) => writes.Add(path, data),
                completedCounts.Add);

            command.Execute(root);

            Assert.AreEqual(2, writes.Count);
            CollectionAssert.AreEqual(
                new byte[] { 1, 2 },
                writes[Path.Combine(OutputRoot, "models", "units", "first.bin")]);
            CollectionAssert.AreEqual(
                new byte[] { 3, 4 },
                writes[Path.Combine(OutputRoot, "models", "units", "second.bin")]);
            CollectionAssert.AreEqual(new[] { 2 }, completedCounts);
        }

        [TestMethod]
        public void Execute_DirectoryNode_ExportsItsFilesRelativeToParent()
        {
            var root = CreateRoot();
            var assets = AddDirectory(root, "assets");
            var models = AddDirectory(assets, "models");
            AddFile(models, "unit.bin", [5]);
            var writes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var command = CreateCommand(
                () => OutputRoot,
                (path, data) => writes.Add(path, data),
                _ => { });

            command.Execute(models);

            Assert.AreEqual(1, writes.Count);
            CollectionAssert.AreEqual(
                new byte[] { 5 },
                writes[Path.Combine(OutputRoot, "models", "unit.bin")]);
        }

        [TestMethod]
        public void Execute_FileNode_ExportsOnlyTheSelectedFile()
        {
            var root = CreateRoot();
            var assets = AddDirectory(root, "assets");
            var models = AddDirectory(assets, "models");
            var selectedFile = AddFile(models, "selected.bin", [6]);
            AddFile(models, "other.bin", [7]);
            var writes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var completedCounts = new List<int>();
            var command = CreateCommand(
                () => OutputRoot,
                (path, data) => writes.Add(path, data),
                completedCounts.Add);

            command.Execute(selectedFile);

            Assert.AreEqual(1, writes.Count);
            CollectionAssert.AreEqual(
                new byte[] { 6 },
                writes[Path.Combine(OutputRoot, "selected.bin")]);
            CollectionAssert.AreEqual(new[] { 1 }, completedCounts);
        }

        [TestMethod]
        public void Execute_RepeatedDirectorySegment_RemovesOnlyTheLeadingParentPath()
        {
            var root = CreateRoot();
            var outerData = AddDirectory(root, "data");
            var innerData = AddDirectory(outerData, "data");
            AddFile(innerData, "unit.mesh", [8]);
            var writes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var command = CreateCommand(
                () => OutputRoot,
                (path, data) => writes.Add(path, data),
                _ => { });

            command.Execute(innerData);

            Assert.AreEqual(1, writes.Count);
            CollectionAssert.AreEqual(
                new byte[] { 8 },
                writes[Path.Combine(OutputRoot, "data", "unit.mesh")]);
        }

        [TestMethod]
        public void Execute_CancelledFolderSelection_DoesNotWriteFiles()
        {
            var root = CreateRoot();
            AddFile(root, "file.bin", [9]);
            var writes = new List<string>();
            var completedCounts = new List<int>();
            var command = CreateCommand(
                () => null,
                (path, _) => writes.Add(path),
                completedCounts.Add);

            command.Execute(root);

            Assert.AreEqual(0, writes.Count);
            Assert.AreEqual(0, completedCounts.Count);
        }

        [TestMethod]
        public void Execute_PathTraversal_RejectsBeforeWritingAnyFiles()
        {
            var root = CreateRoot();
            AddFile(root, "valid.bin", [1]);
            var traversalDirectory = AddDirectory(root, "..");
            AddFile(traversalDirectory, "outside.bin", [2]);
            var writes = new List<string>();
            var completedCounts = new List<int>();
            var errors = new List<string>();
            var command = CreateCommand(
                () => OutputRoot,
                (path, _) => writes.Add(path),
                completedCounts.Add,
                errors.Add);

            command.Execute(root);

            Assert.AreEqual(0, writes.Count);
            Assert.AreEqual(0, completedCounts.Count);
            Assert.AreEqual(1, errors.Count);
        }

        [TestMethod]
        public void Execute_RootedPath_RejectsBeforeWritingAnyFiles()
        {
            var root = CreateRoot();
            AddFile(root, @"C:\outside.bin", [1]);
            var writes = new List<string>();
            var completedCounts = new List<int>();
            var errors = new List<string>();
            var command = CreateCommand(
                () => OutputRoot,
                (path, _) => writes.Add(path),
                completedCounts.Add,
                errors.Add);

            command.Execute(root);

            Assert.AreEqual(0, writes.Count);
            Assert.AreEqual(0, completedCounts.Count);
            Assert.AreEqual(1, errors.Count);
        }

        [TestMethod]
        public void Execute_DriveRootDestination_KeepsFileInsideDriveRoot()
        {
            var root = CreateRoot();
            AddFile(root, "file.bin", [1]);
            var driveRoot = Path.GetPathRoot(OutputRoot)!;
            var writes = new List<string>();
            var command = CreateCommand(
                () => driveRoot,
                (path, _) => writes.Add(path),
                _ => { });

            command.Execute(root);

            CollectionAssert.AreEqual(
                new[] { Path.Combine(driveRoot, "file.bin") },
                writes);
        }

        private static ExportToDirectoryCommand CreateCommand(
            Func<string?> selectOutputDirectory,
            Action<string, byte[]> writeAllBytes,
            Action<int> showCompleted,
            Action<string>? showError = null)
        {
            var constructor = typeof(ExportToDirectoryCommand).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(Func<string>),
                    typeof(Action<string, byte[]>),
                    typeof(Action<int>),
                    typeof(Action<string>)
                ],
                modifiers: null);

            Assert.IsNotNull(constructor, "Export command must expose an internal injectable constructor.");
            return (ExportToDirectoryCommand)constructor.Invoke(
                [
                    selectOutputDirectory,
                    writeAllBytes,
                    showCompleted,
                    showError ?? (_ => { })
                ]);
        }

        private static TreeNode CreateRoot()
        {
            return new TreeNode("test.pack", NodeType.Root, new PackFileContainer("test.pack"), null);
        }

        private static TreeNode AddDirectory(TreeNode parent, string name)
        {
            var node = new TreeNode(name, NodeType.Directory, parent.FileOwner, parent);
            parent.Children.Add(node);
            return node;
        }

        private static TreeNode AddFile(TreeNode parent, string name, byte[] data)
        {
            var packFile = new PackFile(name, new MemorySource(data));
            var node = new TreeNode(name, NodeType.File, parent.FileOwner, parent, packFile);
            parent.Children.Add(node);
            return node;
        }
    }
}
