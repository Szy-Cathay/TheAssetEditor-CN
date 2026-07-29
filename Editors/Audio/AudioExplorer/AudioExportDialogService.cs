using System.Windows.Forms;
using Editors.Audio.Shared.Utilities;
using Shared.Core.Services;

namespace Editors.Audio.AudioExplorer
{
    public interface IAudioExportDialogService
    {
        string SelectOutputFile(
            string suggestedFileName,
            AudioExportFormat format);
        string SelectOutputFolder();
    }

    public class AudioExportDialogService : IAudioExportDialogService
    {
        public string SelectOutputFile(
            string suggestedFileName,
            AudioExportFormat format)
        {
            var extension = format == AudioExportFormat.Wav
                ? ".wav"
                : ".wem";
            var filterKey = format == AudioExportFormat.Wav
                ? "AudioExplorer.WavFilter"
                : "AudioExplorer.WemFilter";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = extension,
                FileName = suggestedFileName,
                Filter = LocalizationManager.Instance.Get(filterKey),
                OverwritePrompt = true
            };

            return dialog.ShowDialog() == true
                ? dialog.FileName
                : null;
        }

        public string SelectOutputFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = LocalizationManager.Instance.Get(
                    "AudioExplorer.SelectExportFolder"),
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            return dialog.ShowDialog() == DialogResult.OK
                ? dialog.SelectedPath
                : null;
        }
    }
}
