using Shared.Core.PackFiles;

namespace Shared.Core.Misc
{
    public static class SaveUtility
    {
        public static bool IsFilenameUnique(IPackFileService pfs, string path)
        {
            var editablePack = pfs.GetEditablePack();
            if (editablePack == null)
                throw new Exception("Can not check if filename is unique if no out packfile is selected");

            var file = pfs.FindFile(path, pfs.GetEditablePack());
            return file == null;
        }

        public static string EnsureEnding(string text, string ending)
        {
            text = text.ToLower();
            var hasCorrectEnding = text.EndsWith(ending);
            if (!hasCorrectEnding)
            {
                text = Path.GetFileNameWithoutExtension(text);
                text = text + ending;
            }

            return text;
        }

    }
}
