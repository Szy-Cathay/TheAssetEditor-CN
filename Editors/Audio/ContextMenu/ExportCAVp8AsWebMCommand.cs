using System;
using System.IO;
using System.Threading.Tasks;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.External;

namespace Editors.Audio.ContextMenu
{
    public class ExportCAVp8AsWebMCommand :
        IExportCAVp8AsWebMCommand
    {
        private readonly ILogger _logger =
            Logging.Create<ExportCAVp8AsWebMCommand>();
        private readonly Func<string> _selectOutputDirectory;
        private readonly Action _loadAudio;
        private readonly Func<PackFile, PackFile?> _resolveWem;
        private readonly Func<PackFile, PackFile?, byte[]> _convert;
        private readonly Action<string, byte[], bool> _writeAllBytes;
        private readonly Func<string, bool> _fileExists;
        private readonly Func<string, bool> _confirmOverwrite;
        private readonly Action<string> _showSuccess;
        private readonly Action<string> _showError;
        private readonly Func<Action, Task> _runInBackground;

        public ExportCAVp8AsWebMCommand(
            IServiceScopeFactory scopeFactory,
            IPackFileService packFileService)
            : this(
                SelectOutputDirectory,
                () => { },
                packFile => ResolveMovieWemForExport(
                    scopeFactory,
                    packFileService,
                    packFile),
                CAVp8Exporter.ExportToWebM,
                AtomicFileWriter.WriteAllBytes,
                File.Exists,
                ConfirmOverwrite,
                ShowSuccess,
                ShowError,
                action => Task.Run(action))
        {
        }

        internal ExportCAVp8AsWebMCommand(
            Func<string> selectOutputDirectory,
            Action loadAudio,
            Func<PackFile, PackFile?> resolveWem,
            Func<PackFile, PackFile?, byte[]> convert,
            Action<string, byte[]> writeAllBytes,
            Action<string> showSuccess,
            Action<string> showError)
            : this(
                selectOutputDirectory,
                loadAudio,
                resolveWem,
                convert,
                (path, bytes, _) => writeAllBytes(path, bytes),
                _ => false,
                _ => true,
                showSuccess,
                showError,
                action => Task.Run(action))
        {
        }

        internal ExportCAVp8AsWebMCommand(
            Func<string> selectOutputDirectory,
            Action loadAudio,
            Func<PackFile, PackFile?> resolveWem,
            Func<PackFile, PackFile?, byte[]> convert,
            Action<string, byte[]> writeAllBytes,
            Action<string> showSuccess,
            Action<string> showError,
            Func<Action, Task> runInBackground)
            : this(
                selectOutputDirectory,
                loadAudio,
                resolveWem,
                convert,
                (path, bytes, _) => writeAllBytes(path, bytes),
                _ => false,
                _ => true,
                showSuccess,
                showError,
                runInBackground)
        {
        }

        internal ExportCAVp8AsWebMCommand(
            Func<string> selectOutputDirectory,
            Action loadAudio,
            Func<PackFile, PackFile?> resolveWem,
            Func<PackFile, PackFile?, byte[]> convert,
            Action<string, byte[], bool> writeAllBytes,
            Func<string, bool> fileExists,
            Func<string, bool> confirmOverwrite,
            Action<string> showSuccess,
            Action<string> showError,
            Func<Action, Task> runInBackground)
        {
            _selectOutputDirectory = selectOutputDirectory;
            _loadAudio = loadAudio;
            _resolveWem = resolveWem;
            _convert = convert;
            _writeAllBytes = writeAllBytes;
            _fileExists = fileExists;
            _confirmOverwrite = confirmOverwrite;
            _showSuccess = showSuccess;
            _showError = showError;
            _runInBackground = runInBackground;
        }

        public string GetDisplayName(TreeNode node) =>
            LocalizationManager.Instance.Get(
                "ContextMenu.ExportCAVp8AsWebM");

        public bool IsEnabled(TreeNode node) =>
            node.NodeType == NodeType.File &&
            node.Item?.Name.EndsWith(
                ".ca_vp8",
                StringComparison.OrdinalIgnoreCase) == true;

        public void Execute(TreeNode node) => _ = ExecuteAsync(node);

        private async Task ExecuteAsync(TreeNode node)
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
                    Path.ChangeExtension(packFile.Name, ".webm"));
                var overwrite = _fileExists(outputPath);
                if (overwrite && !_confirmOverwrite(outputPath))
                    return;

                PackFile? wemPackFile = null;
                await _runInBackground(() =>
                {
                    _loadAudio();
                    wemPackFile = _resolveWem(packFile);
                    var outputBytes = _convert(packFile, wemPackFile);
                    _writeAllBytes(outputPath, outputBytes, overwrite);
                });
                _showSuccess(
                    LocalizationManager.Instance.GetFormat(
                        wemPackFile == null
                            ? "CAVp8.ExportSucceededWithoutAudio"
                            : "CAVp8.ExportSucceeded",
                        outputPath));
            }
            catch (Exception exception)
            {
                _logger.Here().Error(
                    exception,
                    "Failed to export CA VP8 movie as WebM");
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

        private static PackFile? ResolveMovieWemForExport(
            IServiceScopeFactory scopeFactory,
            IPackFileService packFileService,
            PackFile movie)
        {
            using var scope = scopeFactory.CreateScope();
            var audioRepository = scope.ServiceProvider
                .GetRequiredService<IAudioRepository>();
            audioRepository.Load(
                [
                    Wh3LanguageInformation.GetLanguageAsString(
                        Wh3Language.Chinese)
                ]);
            var movieAudioResolver = scope.ServiceProvider
                .GetRequiredService<IMovieAudioResolver>();
            return movieAudioResolver.ResolveMovieWem(
                packFileService.GetFullPath(movie));
        }

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
