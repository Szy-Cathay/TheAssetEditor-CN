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
            bool convertSpecialTexture = true,
            float alphaThreshold = 0.5f)
        {
            using var scratchImagePng = TexHelper.Instance.LoadFromWICFile(
                inputPath,
                GetWicFlags(textureType));
            return Import(
                scratchImagePng,
                textureType,
                gameType,
                outFileName,
                convertSpecialTexture,
                alphaThreshold);
        }

        public static PackFile Import(
            Stream inputStream,
            TextureType textureType,
            GameTypeEnum gameType,
            string outFileName,
            bool convertSpecialTexture = true,
            float alphaThreshold = 0.5f)
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
                    GetWicFlags(textureType));
                return Import(
                    scratchImagePng,
                    textureType,
                    gameType,
                    outFileName,
                    convertSpecialTexture,
                    alphaThreshold);
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
            bool convertSpecialTexture,
            float alphaThreshold)
        {
            var processedImage = ImageProcessorFactory
                .CreateImageProcessor(textureType, convertSpecialTexture)
                .Transform(scratchImagePng);
            using (processedImage)
            using (var imageWithMips = processedImage.GenerateMipMaps(TEX_FILTER_FLAGS.DEFAULT, 0))
            {
                var ddsFormat = DDSFormatHelper.GetDDSFormat(gameType, textureType);
                using var ddsImage = imageWithMips.Compress(
                    ddsFormat,
                    TEX_COMPRESS_FLAGS.DEFAULT,
                    alphaThreshold);
                var ddsFlags = gameType == GameTypeEnum.Warhammer3
                    ? DDS_FLAGS.FORCE_DX10_EXT
                    : DDS_FLAGS.NONE;
                using var ddsMemStream = ddsImage.SaveToDDSMemory(ddsFlags);

                var ddsBytes = new byte[ddsMemStream.Length];
                ddsMemStream.Read(ddsBytes, 0, ddsBytes.Length);

                return new PackFile(outFileName, new MemorySource(ddsBytes));
            }
        }

        private static WIC_FLAGS GetWicFlags(TextureType textureType) =>
            textureType is TextureType.BaseColour or TextureType.Diffuse or TextureType.Specular
                ? WIC_FLAGS.DEFAULT_SRGB
                : WIC_FLAGS.IGNORE_SRGB;
    }
}
