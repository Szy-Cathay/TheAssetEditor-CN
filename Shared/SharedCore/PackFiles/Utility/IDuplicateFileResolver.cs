using System.Windows;
using Shared.Core.Services;

namespace Shared.Core.PackFiles.Utility
{
    public interface IDuplicateFileResolver
    {
        bool CheckForDuplicates { get; }
        bool KeepDuplicateFile(string fileName);
    }

    public class CaPackDuplicateFileResolver : IDuplicateFileResolver
    {
        public bool CheckForDuplicates => false;
        public bool KeepDuplicateFile(string fileName) => false;
    }

    public class CustomPackDuplicateFileResolver : IDuplicateFileResolver
    {
        public bool CheckForDuplicates => true;
        public bool KeepDuplicateFile(string fileName)
        {
            var res = UiMessageBoxBridge.Show(
                LocalizationManager.Instance.GetFormat(
                    "Msg.DuplicateFile",
                    fileName),
                "DuplicateFile",
                MessageBoxButton.YesNo);
            return res == MessageBoxResult.Yes;
        }
    }
}
