
using System.IO;

namespace Editors.ImportExport.Misc
{
    public static class FileExtensionHelper
    {
        public static bool IsGltfFile(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".glb", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDdsFile(string fileName)
        {
            var isDdsFile = fileName.EndsWith(".dds", StringComparison.InvariantCultureIgnoreCase);
            return isDdsFile;
        }

        public static bool IsDdsMaterialFile(string fileName)
        {
            var isDdsFile = IsDdsFile(fileName);
            var isMaterialFile = fileName.EndsWith("material", StringComparison.InvariantCultureIgnoreCase);
            return isDdsFile && isMaterialFile;
        }

        public static bool IsRmvFile(string fileName)
        {
            var isRmv = fileName.EndsWith(".rigid_model_v2", StringComparison.InvariantCultureIgnoreCase);
            return isRmv;
        }

        public static bool IsWsModelFile(string fileName)
        {
            var isRmv = fileName.EndsWith(".wsmodel", StringComparison.InvariantCultureIgnoreCase);
            return isRmv;
        }
    }
}
