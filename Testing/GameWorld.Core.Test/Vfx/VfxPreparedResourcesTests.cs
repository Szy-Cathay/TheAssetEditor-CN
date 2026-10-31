#nullable enable
using System.Diagnostics;
using System.Reflection;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxPreparedResourcesTests
{
    [TestCase("Diffuse", 0)]
    [TestCase("OpaqueDiffuse", 0)]
    [TestCase("MaterialValue", 0)]
    [TestCase("ShadingNormal", 0)]
    [TestCase("Diffuse", 1)]
    [TestCase("MaterialValue", 1)]
    [TestCase("MaterialValue", 2)]
    [TestCase("Diffuse", 3)]
    [TestCase("DistortionNormal", 3)]
    [TestCase("ShadingNormal", 3)]
    public void PreparedOriginalMipPayloadExactlyMatchesTheFrozenOwnerDecoder(string usageName, int encoding)
    {
        var usage = Enum.Parse<VfxTextureUsage>(usageName);
        var source = Dds(encoding); var original = source.ToArray();
        var expected = LegacyDecode(source, usage);
        var prepared = VfxPreparedResources.Prepare(Conversion((source, usage)));
        var image = prepared.Textures[0].GetImage();
        Assert.Multiple(() =>
        {
            Assert.That(image.Pixels, Is.EqualTo(expected.Pixels), "All original mips, channel order and normal conversion must match byte for byte.");
            Assert.That((image.Width, image.Height, image.Srgb, image.MipLevels),
                Is.EqualTo((expected.Width, expected.Height, expected.Srgb, expected.MipLevels)));
            Assert.That(image.MipLevels, Is.EqualTo(4));
            Assert.That(source, Is.EqualTo(original));
        });
        if (usage == VfxTextureUsage.OpaqueDiffuse)
            Assert.That(Enumerable.Range(0, image.Pixels.Length / 4).All(index => image.Pixels[index * 4 + 3] == 255), Is.True);
    }

    [Test]
    public void UsageAndCompleteShaKeepAliasesDistinctAndAlphaChecksEveryMip()
    {
        var source = Dds(0);
        var conversion = Conversion((source, VfxTextureUsage.Diffuse), (source.ToArray(), VfxTextureUsage.Diffuse),
            (source.ToArray(), VfxTextureUsage.OpaqueDiffuse));
        var resources = VfxPreparedResources.Prepare(conversion);
        Assert.Multiple(() =>
        {
            Assert.That(resources.Textures[1], Is.SameAs(resources.Textures[0]));
            Assert.That(resources.Textures[2], Is.Not.SameAs(resources.Textures[0]));
            Assert.That(resources.Textures[0].GetImage().ConstantAlpha, Is.Null,
                "The top mip is opaque but later source mips carry fractional alpha.");
            Assert.That(resources.Textures[2].GetImage().ConstantAlpha, Is.EqualTo(255));
            Assert.That(resources.Textures, Has.Length.EqualTo(3), "Logical GPU slots cannot be collapsed by CPU aliasing.");
        });
    }

    [TestCase(2)]
    [TestCase(3)]
    public void PreparedMaskAtlasKeepsOriginalRectanglesGuttersAndRepeatedMaterialAliases(int count)
    {
        var conversion = Conversion(([1], VfxTextureUsage.MaterialValue), ([2], VfxTextureUsage.MaterialValue),
            ([3], VfxTextureUsage.MaterialValue));
        var slots = count == 2 ? new[] { 0, 0, 0, 0, 1, 0, 0 } : new[] { 0, 0, 1, 0, 2, 0, 0, 0, 0, 0 };
        var parameters = new float[count == 2 ? 36 : 60];
        conversion.MeshMaterials.Add(new(slots, parameters));
        conversion.MeshMaterials.Add(new(slots.ToArray(), parameters.ToArray()));
        var pixels = Enumerable.Range(1, 3).Select(value => Enumerable.Range(0, value * 2 * 4)
            .Select(index => (byte)(value * 17 + index * 9)).ToArray()).ToArray();
        var decodeCount = 0;
        var prepared = VfxPreparedResources.Prepare(conversion, decode: (bytes, _) =>
        {
            decodeCount++; var value = bytes[0]; return (pixels[value - 1], (int)value, 2, false, 1);
        });
        var expected = count == 2 ? VfxMaterialMaskAtlas.Pack(pixels[0], 1, 2, pixels[1], 2, 2)
            : VfxMaterialMaskAtlas.PackThree((pixels[0], 1, 2), (pixels[1], 2, 2), (pixels[2], 3, 2));
        var actual = prepared.Masks[0]!.GetAtlas();
        Assert.Multiple(() =>
        {
            Assert.That(actual.Pixels, Is.EqualTo(expected.Pixels));
            Assert.That(actual.Rectangles.Select(BitConverter.SingleToInt32Bits),
                Is.EqualTo(expected.Rectangles.Select(BitConverter.SingleToInt32Bits)));
            Assert.That((actual.Width, actual.Height), Is.EqualTo((expected.Width, expected.Height)));
            Assert.That(prepared.Masks[1], Is.SameAs(prepared.Masks[0]));
            Assert.That(decodeCount, Is.EqualTo(3));
            Assert.That(conversion.MeshMaterials[0].Parameters, Is.EqualTo(parameters));
            Assert.That(conversion.MeshMaterials[0].TextureIndexes, Is.EqualTo(slots));
        });
    }

    [Test]
    public void TextureFailuresStayInTheirOriginalSlotsAndDoNotSuppressValidPayloads()
    {
        var valid = Dds(0);
        var conversion = Conversion(([1, 2, 3], VfxTextureUsage.Diffuse), (valid, VfxTextureUsage.Diffuse),
            (valid, VfxTextureUsage.DistortionNormal), ([255, 255, 255, 0], VfxTextureUsage.SolidWhite));
        var prepared = VfxPreparedResources.Prepare(conversion);
        for (var index = 0; index < conversion.Textures.Count; index++)
        {
            Exception? expectedFailure = null;
            try { _ = LegacyDecode(conversion.Textures[index], conversion.TextureUsages[index]); }
            catch (Exception exception) { expectedFailure = exception; }
            if (expectedFailure == null) Assert.That(prepared.Textures[index].Failure, Is.Null);
            else
            {
                var actual = Assert.Catch(() => prepared.Textures[index].GetImage());
                Assert.That(actual!.GetType(), Is.EqualTo(expectedFailure.GetType()));
                Assert.That(actual.Message, Is.EqualTo(expectedFailure.Message));
            }
        }
        Assert.That(prepared.Textures[1].GetImage().MipLevels, Is.EqualTo(4));
        Assert.That(conversion.Textures, Has.Count.EqualTo(4));
    }

    [Test]
    public void MixedColourSpacesAndMissingMasksRetainOriginalMaterialFailureKinds()
    {
        var conversion = Conversion(([1], VfxTextureUsage.MaterialValue), ([2], VfxTextureUsage.MaterialValue),
            ([3], VfxTextureUsage.MaterialValue));
        conversion.MeshMaterials.Add(new([0, 0, 1, 0, 2, 0, 0, 0, 0, 0], new float[60]));
        var colourSpaces = VfxPreparedResources.Prepare(conversion, decode: (bytes, _) => ([0, 0, 0, 255], 1, 1, bytes[0] == 2, 1));
        Assert.Throws<NotSupportedException>(() => colourSpaces.Masks[0]!.GetAtlas());
        var missing = VfxPreparedResources.Prepare(conversion, decode: (bytes, _) => bytes[0] == 2
            ? throw new NotSupportedException("DIAG texture encoding") : ([0, 0, 0, 255], 1, 1, false, 1));
        Assert.Throws<KeyNotFoundException>(() => missing.Masks[0]!.GetAtlas());
        Assert.That(conversion.MeshMaterials, Has.Count.EqualTo(1));
    }

    [Test]
    public void WorkerPreparesColdPixelsAndIdentityReuseDefersEveryDecode()
    {
        var owner = Environment.CurrentManagedThreadId;
        var bytes = VfxLibraryVisibilityTests.Source("");
        var texture = Dds(0);
        var snapshot = new VfxPreviewSnapshot(bytes, "vfx/cold-resource-DIAG.xml", _ => texture);
        using var cold = new VfxInteractiveConversion(snapshot, true);
        var first = Take(cold);
        Assert.That(first.Resources, Is.Not.Null);
        Assert.That(first.Resources!.PreparationThreadId, Is.Not.EqualTo(owner));
        Assert.That(first.Resources.Textures.Any(texture => texture.GetImage().MipLevels == 4), Is.True);
        using var reused = new VfxInteractiveConversion(snapshot, true, first.ResourcePaths, first.ResourceIdentity);
        var second = Take(reused);
        Assert.That(second.ResourceIdentity, Is.EqualTo(first.ResourceIdentity));
        Assert.That(second.Resources, Is.Null, "An unchanged resource identity must preserve the handle-reuse path without decoding again.");
    }

    [Test]
    public async Task DeferredPreparationUsesTheSameBoundedWorkerPoolAndCancellationReturnsItsPermit()
    {
        var workers = (SemaphoreSlim)typeof(VfxInteractiveConversion).GetField("Workers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.That(workers.CurrentCount, Is.EqualTo(2));
        await workers.WaitAsync(); await workers.WaitAsync();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var pending = VfxInteractiveConversion.PrepareResourcesAsync(Conversion((Dds(0), VfxTextureUsage.Diffuse)), cancellation.Token);
            Assert.That(pending.IsCompleted, Is.False);
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        }
        finally { workers.Release(2); }
        Assert.That(workers.CurrentCount, Is.EqualTo(2));
        var prepared = await VfxInteractiveConversion.PrepareResourcesAsync(Conversion((Dds(0), VfxTextureUsage.Diffuse)), CancellationToken.None);
        Assert.That(prepared.Textures[0].GetImage().MipLevels, Is.EqualTo(4));
        Assert.That(prepared.PreparationThreadId, Is.GreaterThan(0));
    }

    [Test]
    public void CancelledPreparationAndOutOfMemoryAreNeverConvertedToTextureFallbacks()
    {
        var conversion = Conversion((Dds(0), VfxTextureUsage.Diffuse));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => VfxPreparedResources.Prepare(conversion, cancellation.Token));
        Assert.Throws<OutOfMemoryException>(() => VfxPreparedResources.Prepare(conversion,
            decode: (_, _) => throw new OutOfMemoryException("DIAG allocation failure")));
    }

    private static VfxPreparedConversion Take(VfxInteractiveConversion helper)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started).TotalSeconds < 10)
        {
            if (helper.TryTake(out var result)) return result!;
            Thread.Sleep(1);
        }
        throw new TimeoutException("Small deterministic CPU resource preparation did not complete.");
    }

    private static VfxConversion Conversion(params (byte[] Bytes, VfxTextureUsage Usage)[] textures)
    {
        var conversion = new VfxConversion();
        foreach (var (bytes, usage) in textures)
        {
            conversion.Textures.Add(bytes); conversion.TextureUsages.Add(usage); conversion.TexturePaths.Add("DIAG/texture.dds");
        }
        return conversion;
    }

    private static byte[] Dds(int encoding)
    {
        const int width = 8, height = 4, mips = 4;
        var lengths = Enumerable.Range(0, mips).Select(mip => encoding == 3
            ? Math.Max(1, ((width >> mip) + 3) / 4) * Math.Max(1, ((height >> mip) + 3) / 4) * 16
            : Math.Max(1, width >> mip) * Math.Max(1, height >> mip) * 4).ToArray();
        var header = encoding is 1 or 2 ? 148 : 128;
        var bytes = new byte[header + lengths.Sum()];
        void UInt(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        UInt(0, 0x20534444); UInt(4, 124); UInt(8, 0x2100F); UInt(12, height); UInt(16, width); UInt(20, width * 4); UInt(28, mips);
        UInt(76, 32); UInt(80, encoding == 0 ? 0x41u : 4u); UInt(88, 32);
        UInt(92, 255); UInt(96, 65280); UInt(100, 16711680); UInt(104, 4278190080); UInt(108, 0x401008);
        if (encoding is 1 or 2) { UInt(84, 0x30315844); UInt(128, encoding == 1 ? 29u : 28u); UInt(132, 3); UInt(140, 1); }
        else if (encoding == 3) UInt(84, 0x35545844);
        var target = header;
        for (var mip = 0; mip < mips; mip++)
        {
            if (encoding == 3)
            {
                for (var block = 0; block < lengths[mip]; block += 16)
                {
                    bytes[target + block] = 255; bytes[target + block + 1] = 0;
                    bytes[target + block + 8] = 0xE0; bytes[target + block + 9] = 0x07;
                    bytes[target + block + 10] = 0x1F; bytes[target + block + 11] = 0;
                    bytes[target + block + 12] = 0xE4; bytes[target + block + 13] = 0xE4;
                }
            }
            else
                for (var index = 0; index < lengths[mip]; index += 4)
                {
                    bytes[target + index] = (byte)(7 + mip * 23); bytes[target + index + 1] = (byte)(31 + index);
                    bytes[target + index + 2] = (byte)(111 + mip); bytes[target + index + 3] = mip == 0 ? (byte)255 : (byte)(mip * 31);
                }
            target += lengths[mip];
        }
        return bytes;
    }
    // Frozen pre-migration owner decoder: byte-for-byte compatibility oracle for CPU relocation.
    private static (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) LegacyDecode(byte[] gameBytes, VfxTextureUsage usage)
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
                for (var column = 0; column < layout.Width; ++column)
                {
                    var source = layout.Offset + row * layout.Stride + column * 4;
                    pixels[target] = image.Data[source + 2]; pixels[target + 1] = image.Data[source + 1];
                    pixels[target + 2] = image.Data[source]; pixels[target + 3] = usage == VfxTextureUsage.OpaqueDiffuse ? (byte)255 : image.Data[source + 3];
                    target += 4;
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
