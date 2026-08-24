using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace GameWorld.Core.Utility
{
    public static class TexturePathResolver
    {
        private static readonly IReadOnlyDictionary<string, string> KnownAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    @"rigidmodels\buildings\textures\flatnormal.dds",
                    @"commontextures\flatnormal.dds"
                },
                {
                    @"rigidmodels\buildings\textures\test_black.dds",
                    @"commontextures\test_black.dds"
                },
            };

        public static PackFile? FindTextureFile(
            IPackFileService packFileService,
            string path,
            out string resolvedPath)
        {
            resolvedPath = path;
            var exactFile = packFileService.FindFile(path);
            if (exactFile != null)
                return exactFile;

            var normalizedPath = path.Replace('/', '\\');
            if (!KnownAliases.TryGetValue(normalizedPath, out var aliasPath))
                return null;

            var aliasFile = packFileService.FindFile(aliasPath);
            if (aliasFile == null)
                return null;

            resolvedPath = aliasPath;
            return aliasFile;
        }
    }
}
