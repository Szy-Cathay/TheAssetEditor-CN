using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using GameWorld.Core.Utility;

namespace GameWorld.Core.Vfx;

// Owns only immutable CPU upload payloads; it never reads Pack services or accesses the device.
internal sealed partial class VfxPreparedResources
{
    private static readonly Vector128<byte> BgraShuffle = Vector128.Create(
        (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);

    internal static void CopyBgraToRgba(ReadOnlySpan<byte> source, Span<byte> target, bool opaque)
    {
        if (Avx2.IsSupported) CopyBgraToRgbaAvx2(source, target, opaque);
        else if (Ssse3.IsSupported) CopyBgraToRgbaSsse3(source, target, opaque);
        else CopyBgraToRgbaScalar(source, target, opaque);
    }

    internal static void CopyBgraToRgbaAvx2(ReadOnlySpan<byte> source, Span<byte> target, bool opaque)
    {
        ValidatePixelCopy(source, target);
        if (!Avx2.IsSupported) throw new PlatformNotSupportedException("AVX2 pixel copy is unavailable.");
        var mask = Vector256.Create(BgraShuffle, BgraShuffle);
        var alpha = Vector256.Create(opaque ? 0xff000000u : 0u).AsByte();
        ref var input = ref MemoryMarshal.GetReference(source);
        ref var output = ref MemoryMarshal.GetReference(target);
        var offset = 0;
        for (; offset <= source.Length - Vector256<byte>.Count; offset += Vector256<byte>.Count)
        {
            var pixels = Vector256.LoadUnsafe(ref input, (nuint)offset);
            Avx2.Or(Avx2.Shuffle(pixels, mask), alpha).StoreUnsafe(ref output, (nuint)offset);
        }
        CopyBgraToRgbaScalar(source[offset..], target[offset..], opaque);
    }

    internal static void CopyBgraToRgbaSsse3(ReadOnlySpan<byte> source, Span<byte> target, bool opaque)
    {
        ValidatePixelCopy(source, target);
        if (!Ssse3.IsSupported) throw new PlatformNotSupportedException("SSSE3 pixel copy is unavailable.");
        var alpha = Vector128.Create(opaque ? 0xff000000u : 0u).AsByte();
        ref var input = ref MemoryMarshal.GetReference(source);
        ref var output = ref MemoryMarshal.GetReference(target);
        var offset = 0;
        for (; offset <= source.Length - Vector128<byte>.Count; offset += Vector128<byte>.Count)
        {
            var pixels = Vector128.LoadUnsafe(ref input, (nuint)offset);
            Sse2.Or(Ssse3.Shuffle(pixels, BgraShuffle), alpha).StoreUnsafe(ref output, (nuint)offset);
        }
        CopyBgraToRgbaScalar(source[offset..], target[offset..], opaque);
    }

    internal static void CopyBgraToRgbaScalar(ReadOnlySpan<byte> source, Span<byte> target, bool opaque)
    {
        ValidatePixelCopy(source, target);
        for (var offset = 0; offset < source.Length; offset += 4)
        {
            var blue = source[offset];
            var red = source[offset + 2];
            target[offset] = red; target[offset + 1] = source[offset + 1];
            target[offset + 2] = blue; target[offset + 3] = opaque ? (byte)255 : source[offset + 3];
        }
    }

    private static void ValidatePixelCopy(ReadOnlySpan<byte> source, Span<byte> target)
    {
        if (source.Length % 4 != 0 || target.Length != source.Length)
            throw new ArgumentException("Pixel copies require matching complete BGRA/RGBA pixel spans.");
    }

    internal static byte? ConstantAlpha(byte[] pixels)
    {
        var alpha = pixels[3];
        if (alpha is not (0 or 255)) return null;
        for (var index = 7; index < pixels.Length; index += 4)
            if (pixels[index] != alpha) return null;
        return alpha;
    }

    internal static (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) DecodeTexture(byte[] gameBytes, VfxTextureUsage usage)
    {
        if (usage == VfxTextureUsage.SolidWhite)
        {
            if (gameBytes.Length != 4 || gameBytes.Any(value => value != 255)) throw new InvalidOperationException("Invalid untextured VFX identity");
            return (gameBytes, 1, 1, false, 1);
        }
        if (usage == VfxTextureUsage.GradientValue)
        {
            if (gameBytes.Length != VfxPreviewGradient.Width * 4) throw new InvalidOperationException("Invalid VFX gradient lookup");
            return (gameBytes, VfxPreviewGradient.Width, 1, false, 1);
        }
        using var image = ImageLoader.LoadImageFromBytes(gameBytes, out var information);
        if (image.Format != Pfim.ImageFormat.Rgba32 || image.Width is < 1 or > 4096 || image.Height is < 1 or > 4096
            || image.Stride < image.Width * 4 || (long)image.Stride * image.Height > image.DataLen)
            throw new NotSupportedException("Unsupported decoded VFX image layout");
        var layouts = new List<(int Width, int Height, int Stride, int Offset)> { (image.Width, image.Height, image.Stride, 0) };
        var width = image.Width; var height = image.Height;
        foreach (var mip in image.MipMaps)
        {
            if (width == 1 && height == 1) throw new NotSupportedException("Invalid decoded VFX mip count");
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            if (mip.Width != width || mip.Height != height || mip.Stride < width * 4 || mip.DataOffset < 0
                || (long)mip.Stride * height > mip.DataLen || (long)mip.DataOffset + mip.DataLen > image.Data.Length)
                throw new NotSupportedException("Unsupported decoded VFX mip layout");
            layouts.Add((width, height, mip.Stride, mip.DataOffset));
        }
        var pixels = new byte[layouts.Sum(layout => checked(layout.Width * layout.Height * 4))];
        var target = 0;
        // Pfim keeps block padding in the smallest mips. Upload only each level's actual texels.
        foreach (var layout in layouts)
            for (var row = 0; row < layout.Height; ++row)
            {
                var rowBytes = layout.Width * 4;
                CopyBgraToRgba(image.Data.AsSpan(layout.Offset + row * layout.Stride, rowBytes), pixels.AsSpan(target, rowBytes),
                    usage == VfxTextureUsage.OpaqueDiffuse);
                target += rowBytes;
            }
        // ps_texture samples diffuse colour as sRGB, including legacy DDS files without a colour-space tag.
        // Numeric material slots stay linear for legacy DDS, but explicit sRGB formats retain their native sampling.
        var srgb = usage is VfxTextureUsage.Diffuse or VfxTextureUsage.OpaqueDiffuse
            || usage == VfxTextureUsage.MaterialValue && information.Header10_DxgiFormat is
                Pfim.DxgiFormat.R8G8B8A8_UNORM_SRGB or Pfim.DxgiFormat.B8G8R8A8_UNORM_SRGB
                or Pfim.DxgiFormat.B8G8R8X8_UNORM_SRGB or Pfim.DxgiFormat.BC1_UNORM_SRGB
                or Pfim.DxgiFormat.BC2_UNORM_SRGB or Pfim.DxgiFormat.BC3_UNORM_SRGB or Pfim.DxgiFormat.BC7_UNORM_SRGB;
        if (usage == VfxTextureUsage.DistortionNormal)
        {
            if (information.IsDdsImage && information.Header_PixelFormat.FourCC == Pfim.CompressionAlgorithm.D3DFMT_DXT5)
                TotalWarVfxMaterial.ConvertDxt5Normal(pixels);
            else
                throw new NotSupportedException("Unconfirmed game distortion normal encoding");
            srgb = false;
        }
        if (usage == VfxTextureUsage.ShadingNormal && (!information.IsDdsImage
            || information.Header_PixelFormat.FourCC is not (Pfim.CompressionAlgorithm.D3DFMT_DXT5 or 0)))
            throw new NotSupportedException("Unconfirmed game shading normal encoding");
        return (pixels, image.Width, image.Height, srgb, layouts.Count);
    }

}
