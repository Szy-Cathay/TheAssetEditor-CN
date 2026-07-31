using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Shared.Core.Misc;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class ExportToDirectoryCommand : IContextMenuCommand
    {
        private readonly Func<string?> _selectOutputDirectory;
        private readonly Action<string, byte[]> _writeAllBytes;
        private readonly Action<int> _showCompleted;
        private readonly Action<string> _showError;

        public ExportToDirectoryCommand()
            : this(
                SelectOutputDirectory,
                WriteAllBytes,
                ShowCompleted,
                ShowError)
        {
        }

        internal ExportToDirectoryCommand(
            Func<string?> selectOutputDirectory,
            Action<string, byte[]> writeAllBytes,
            Action<int> showCompleted,
            Action<string> showError)
        {
            _selectOutputDirectory = selectOutputDirectory;
            _writeAllBytes = writeAllBytes;
            _showCompleted = showCompleted;
            _showError = showError;
        }

        public string GetDisplayName(TreeNode node) => LocalizationManager.Instance.Get("ContextMenu.ExportToSystemFolder");
        public bool IsEnabled(TreeNode node) => true;

        public void Execute(TreeNode selectedNode)
        {
            var outputDirectory = _selectOutputDirectory();
            if (string.IsNullOrEmpty(outputDirectory))
                return;

            var nodeStartDir = selectedNode.NodeType == NodeType.Root
                ? string.Empty
                : Path.GetDirectoryName(selectedNode.GetFullPath());
            (TreeNode Node, string OutputPath)[] pendingWrites;
            try
            {
                pendingWrites = selectedNode
                    .GetAllChildFileNodes()
                    .Select(node =>
                    {
                        var relativePath = ComputeRelativePath(
                            node.GetFullPath(),
                            nodeStartDir);
                        return (
                            Node: node,
                            OutputPath: GetSafeOutputPath(
                                outputDirectory,
                                relativePath));
                    })
                    .ToArray();
            }
            catch (InvalidDataException exception)
            {
                _showError(exception.Message);
                return;
            }

            foreach (var pendingWrite in pendingWrites)
            {
                var bytes = pendingWrite.Node.Item!.DataSource.ReadData();
                _writeAllBytes(pendingWrite.OutputPath, bytes);
            }

            _showCompleted(pendingWrites.Length);
        }

        private static string GetSafeOutputPath(
            string outputDirectory,
            string relativePath)
        {
            try
            {
                if (Path.IsPathRooted(relativePath))
                    throw CreateUnsafePathException(relativePath);

                var outputRoot = Path.GetFullPath(outputDirectory);
                var outputPath = Path.GetFullPath(
                    Path.Combine(outputRoot, relativePath));
                var outputRootPrefix =
                    Path.EndsInDirectorySeparator(outputRoot)
                        ? outputRoot
                        : outputRoot + Path.DirectorySeparatorChar;

                if (!outputPath.StartsWith(
                        outputRootPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw CreateUnsafePathException(relativePath);
                }

                return outputPath;
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                throw CreateUnsafePathException(relativePath);
            }
        }

        private static InvalidDataException CreateUnsafePathException(
            string relativePath)
        {
            var message = LocalizationManager.Instance?.GetFormat(
                "Msg.ExportPathOutsideSelectedFolder",
                relativePath);
            return new InvalidDataException(
                message ?? "Pack file path escapes the selected folder.");
        }

        private static string ComputeRelativePath(
            string nodeFullPath,
            string? rootPath)
        {
            var relativePath = nodeFullPath;
            var rootPrefix = string.IsNullOrEmpty(rootPath)
                ? string.Empty
                : rootPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                  Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(rootPrefix) &&
                nodeFullPath.StartsWith(
                    rootPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                relativePath = nodeFullPath[rootPrefix.Length..];
            }

            return relativePath.TrimStart(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static string? SelectOutputDirectory()
        {
            using var dialog = new FolderBrowserDialog();
            return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
        }

        private static void WriteAllBytes(string path, byte[] bytes)
        {
            var outputDirectory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(outputDirectory))
                DirectoryHelper.EnsureCreated(outputDirectory);

            File.WriteAllBytes(path, bytes);
        }

        private static void ShowCompleted(int fileCounter)
        {
            MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.FilesExported", fileCounter));
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(
                message,
                LocalizationManager.Instance.Get("Msg.GeneralError"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
