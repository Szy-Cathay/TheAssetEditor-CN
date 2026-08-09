using DirectXTexNet;
using System.IO;
using System.Runtime.InteropServices;
using Editors.ImportExport.Importing.Importers.PngToDds.Helpers;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.Types;
using Editors.ImportExport.Common.Interfaces;

namespace Editors.ImportExport.Importing.Importers.PngToDds
{

    public class PngToDdsImporter
    {
        public static PackFile Import(
            string inputPath,
            TextureType textureType,
            GameTypeEnum gameType,
            string outFileName,
            bool convertSpecialTexture = true)
        {
            using var scratchImagePng = TexHelper.Instance.LoadFromWICFile(inputPath, WIC_FLAGS.DEFAULT_SRGB);
            return Import(scratchImagePng, textureType, gameType, outFileName, convertSpecialTexture);
        }

        public static PackFile Import(
            Stream inputStream,
            TextureType textureType,
            GameTypeEnum gameType,
            string outFileName,
            bool convertSpecialTexture = true)
        {
            using var memoryStream = new MemoryStream();
            inputStream.CopyTo(memoryStream);
            var imageBytes = memoryStream.ToArray();
            var imageHandle = GCHandle.Alloc(imageBytes, GCHandleType.Pinned);
            try
            {
                using var scratchImagePng = TexHelper.Instance.LoadFromWICMemory(
                    imageHandle.AddrOfPinnedObject(),
                    imageBytes.LongLength,
                    WIC_FLAGS.DEFAULT_SRGB);
                return Import(scratchImagePng, textureType, gameType, outFileName, convertSpecialTexture);
            }
            finally
            {
                imageHandle.Free();
            }
        }

        private static PackFile Import(
            ScratchImage scratchImagePng,
            TextureType textureType,
            GameTypeEnum gameType,
            string outFileName,
            bool convertSpecialTexture)
        {
            var processedImage = ImageProcessorFactory
                .CreateImageProcessor(textureType, convertSpecialTexture)
                .Transform(scratchImagePng);
            using (processedImage)
            using (var imageWithMips = processedImage.GenerateMipMaps(TEX_FILTER_FLAGS.DEFAULT, 0))
            {
                var ddsFormat = DDSFormatHelper.GetDDSFormat(gameType, textureType);
                using var ddsImage = imageWithMips.Compress(ddsFormat, TEX_COMPRESS_FLAGS.DEFAULT, 0.5f);
                using var ddsMemStream = ddsImage.SaveToDDSMemory(DDS_FLAGS.NONE);

                var ddsBytes = new byte[ddsMemStream.Length];
                ddsMemStream.Read(ddsBytes, 0, ddsBytes.Length);

                return new PackFile(outFileName, new MemorySource(ddsBytes));
            }
        }
    }
}
