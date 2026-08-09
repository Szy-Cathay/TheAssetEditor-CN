using System;
using System.Runtime.InteropServices;
using DirectXTexNet;

namespace Editors.ImportExport.Common.Interfaces
{
    public class BlenderToWH3MaterialMapProcessor : IImageProcessor
    {
        public ScratchImage Transform(ScratchImage scratchImage)
        {
            var format = scratchImage.GetMetadata().Format;
            if (format is not DXGI_FORMAT.B8G8R8A8_UNORM and
                not DXGI_FORMAT.B8G8R8A8_UNORM_SRGB and
                not DXGI_FORMAT.R8G8B8A8_UNORM and
                not DXGI_FORMAT.R8G8B8A8_UNORM_SRGB)
            {
                throw new Exception($"Error: image format is {scratchImage.GetMetadata().Format}  should be uncompressed RGBA8 (BC_B8G8R8A8_UNORM)");
            }

            var isBgra = format is DXGI_FORMAT.B8G8R8A8_UNORM or
                DXGI_FORMAT.B8G8R8A8_UNORM_SRGB;
            var redOffset = isBgra ? 2 : 0;
            var blueOffset = isBgra ? 0 : 2;
            
            var copyScratchImage = scratchImage.CreateImageCopy(0, false, CP_FLAGS.NONE);
            var srcImage = copyScratchImage.GetImage(0, 0, 0);
            byte[] rgbaBytes = new byte[srcImage.SlicePitch];
            Marshal.Copy(srcImage.Pixels, rgbaBytes, 0, (int)srcImage.SlicePitch);

            for (int index = 0; index < srcImage.SlicePitch; index += 4)
            {
                var g = rgbaBytes[index + 1];
                var b = rgbaBytes[index + blueOffset];
                                
                rgbaBytes[index + blueOffset] = 0;
                rgbaBytes[index + 1] = g;
                rgbaBytes[index + redOffset] = b;
                rgbaBytes[index + 3] = 255;
                
            }

            Marshal.Copy(rgbaBytes, 0, srcImage.Pixels, (int)srcImage.SlicePitch);
            return copyScratchImage;
        }
    }
}
