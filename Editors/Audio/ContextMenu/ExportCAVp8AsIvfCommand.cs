using System;
using System.IO;
using Editors.Audio.Shared.Utilities;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.External;

namespace Editors.Audio.ContextMenu
{
    public class ExportCAVp8AsIvfCommand :
        IExportCAVp8AsIvfCommand
    {
        private readonly ILogger _logger =
            Logging.Create<ExportCAVp8AsIvfCommand>();
        private readonly Func<string> _selectOutputDirectory;
        private readonly Func<PackFile, byte[]> _convert;
        private readonly Action<string, byte[], bool> _writeAllBytes;
        private readonly Func<string, bool> _fileExists;
        private readonly Func<string, bool> _confirmOverwrite;
        private readonly Action<string> _showSuccess;
        private readonly Action<string> _showError;

        public ExportCAVp8AsIvfCommand()
            : this(
                SelectOutputDirectory,
                CAVp8Exporter.ExportToIvf,
                AtomicFileWriter.WriteAllBytes,
                File.Exists,
                ConfirmOverwrite,
                ShowSuccess,
                ShowError)
        {
        }

        internal ExportCAVp8AsIvfCommand(
            Func<string> selectOutputDirectory,
            Func<PackFile, byte[]> convert,
            Action<string, byte[]> writeAllBytes,
            Action<string> showSuccess,
            Action<string> showError)
            : this(
                selectOutputDirectory,
                convert,
                (path, bytes, _) => writeAllBytes(path, bytes),
                _ => false,
                _ => true,
                showSuccess,
                showError)
        {
        }

        internal ExportCAVp8AsIvfCommand(
            Func<string> selectOutputDirectory,
            Func<PackFile, byte[]> convert,
            Action<string, byte[], bool> writeAllBytes,
            Func<string, bool> fileExists,
            Func<string, bool> confirmOverwrite,
            Action<string> showSuccess,
            Action<string> showError)
        {
            _selectOutputDirectory = selectOutputDirectory;
            _convert = convert;
            _writeAllBytes = writeAllBytes;
            _fileExists = fileExists;
            _confirmOverwrite = confirmOverwrite;
            _showSuccess = showSuccess;
            _showError = showError;
        }

        public string GetDisplayName(TreeNode node) =>
            LocalizationManager.Instance.Get(
                "ContextMenu.ExportCAVp8AsIvf");

        public bool IsEnabled(TreeNode node) =>
            node.NodeType == NodeType.File &&
            node.Item?.Name.EndsWith(
                ".ca_vp8",
                StringComparison.OrdinalIgnoreCase) == true;

        public void Execute(TreeNode node)
        {
            var packFile = node.Item;
            if (packFile == null || !IsEnabled(node))
                return;

            try
            {
                var outputDirectory = _selectOutputDirectory();
                if (string.IsNullOrWhiteSpace(outputDirectory))
                    return;

                var outputPath = Path.Combine(
                    outputDirectory,
                    Path.ChangeExtension(packFile.Name, ".ivf"));
                var overwrite = _fileExists(outputPath);
                if (overwrite && !_confirmOverwrite(outputPath))
                    return;

                var outputBytes = _convert(packFile);
                _writeAllBytes(outputPath, outputBytes, overwrite);
                _showSuccess(
                    LocalizationManager.Instance.GetFormat(
                        "CAVp8.ExportSucceeded",
                        outputPath));
            }
            catch (Exception exception)
            {
                _logger.Here().Error(
                    exception,
                    "Failed to export CA VP8 movie as IVF");
                _showError(
                    LocalizationManager.Instance.Get(
                        "CAVp8.ExportFailed"));
            }
        }

        private static string SelectOutputDirectory()
        {
            using var dialog =
                new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LocalizationManager.Instance.Get(
                    "CAVp8.SelectOutputFolder"),
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true,
            };

            return dialog.ShowDialog() ==
                System.Windows.Forms.DialogResult.OK
                ? dialog.SelectedPath
                : null;
        }

        private static bool ConfirmOverwrite(string outputPath) =>
            MessageBox.Show(
                LocalizationManager.Instance.GetFormat(
                    "CAVp8.ConfirmOverwrite",
                    outputPath),
                LocalizationManager.Instance.Get(
                    "CAVp8.ExportTitle"),
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) ==
            System.Windows.MessageBoxResult.Yes;

        private static void ShowSuccess(string message) =>
            MessageBox.Show(
                message,
                LocalizationManager.Instance.Get(
                    "CAVp8.ExportTitle"));

        private static void ShowError(string message) =>
            MessageBox.Show(
                message,
                LocalizationManager.Instance.Get(
                    "CAVp8.ExportTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
    }
}
