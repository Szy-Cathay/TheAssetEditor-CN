#nullable enable
using System.Runtime.Intrinsics.X86;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxTexturePixelCopyTests
{
    [TestCase("Auto", false)]
    [TestCase("Auto", true)]
    [TestCase("Scalar", false)]
    [TestCase("Scalar", true)]
    [TestCase("Ssse3", false)]
    [TestCase("Ssse3", true)]
    [TestCase("Avx2", false)]
    [TestCase("Avx2", true)]
    public void EveryChannelAndSentinelMatchesIndependentByteOracle(string mode, bool opaque)
    {
        RequireHardware(mode);
        var random = new Random(125031);
        int[] widths = [0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33,
            63, 64, 65, 127, 128, 129, 255, 256, 257, 1023, 1024, 1025, 4095, 4096];
        foreach (var width in widths.Concat(Enumerable.Range(0, 40).Select(_ => random.Next(1, 258))))
            for (var layout = 0; layout < 6; layout++)
            {
                var height = 1 + layout;
                var sourceOffset = new[] { 0, 1, 7, 15, 31, 33 }[layout];
                var targetOffset = new[] { 0, 3, 9, 17, 29, 35 }[layout];
                var sourceStride = width * 4 + new[] { 0, 1, 3, 7, 16, 31 }[layout];
                var targetStride = width * 4 + new[] { 0, 3, 1, 13, 31, 7 }[layout];
                var source = Enumerable.Repeat((byte)0xa6, sourceOffset + sourceStride * height + 37).ToArray();
                var target = Enumerable.Repeat((byte)0x5d, targetOffset + targetStride * height + 43).ToArray();
                for (var row = 0; row < height; row++)
                    random.NextBytes(source.AsSpan(sourceOffset + row * sourceStride, width * 4));
                var original = source.ToArray();
                var expected = target.ToArray();
                // The oracle spells out the file's BGRA bytes independently of every product copy kernel.
                for (var row = 0; row < height; row++)
                    for (var pixel = 0; pixel < width; pixel++)
                    {
                        var read = sourceOffset + row * sourceStride + pixel * 4;
                        var write = targetOffset + row * targetStride + pixel * 4;
                        expected[write] = source[read + 2];
                        expected[write + 1] = source[read + 1];
                        expected[write + 2] = source[read];
                        expected[write + 3] = opaque ? byte.MaxValue : source[read + 3];
                    }
                for (var row = 0; row < height; row++)
                    Copy(mode, source.AsSpan(sourceOffset + row * sourceStride, width * 4),
                        target.AsSpan(targetOffset + row * targetStride, width * 4), opaque);
                Assert.That(target, Is.EqualTo(expected), $"{mode} opaque={opaque} width={width} layout={layout}: every texel, padding and guard byte.");
                Assert.That(source, Is.EqualTo(original), "Channel conversion must not modify any source mip byte or padding.");
            }
    }

    [TestCase("Auto")]
    [TestCase("Scalar")]
    [TestCase("Ssse3")]
    [TestCase("Avx2")]
    public void InvalidSpansFailBeforeWritingAnyDestinationByte(string mode)
    {
        RequireHardware(mode);
        foreach (var lengths in new[] { (3, 3), (4, 3), (4, 8), (0, 4) })
        {
            var source = Enumerable.Repeat((byte)0xa6, lengths.Item1).ToArray();
            var target = Enumerable.Repeat((byte)0x5d, lengths.Item2).ToArray();
            var original = target.ToArray();
            Assert.Throws<ArgumentException>(() => Copy(mode, source, target, false));
            Assert.That(target, Is.EqualTo(original));
        }
    }

    private static void RequireHardware(string mode)
    {
        if (mode == "Ssse3" && !Ssse3.IsSupported) Assert.Ignore("This CPU/runtime does not enable SSSE3; automatic scalar fallback remains covered.");
        if (mode == "Avx2" && !Avx2.IsSupported) Assert.Ignore("This CPU/runtime does not enable AVX2; automatic lower-ISA fallback remains covered.");
    }

    private static void Copy(string mode, ReadOnlySpan<byte> source, Span<byte> target, bool opaque)
    {
        switch (mode)
        {
            case "Auto": VfxPreparedResources.CopyBgraToRgba(source, target, opaque); break;
            case "Scalar": VfxPreparedResources.CopyBgraToRgbaScalar(source, target, opaque); break;
            case "Ssse3": VfxPreparedResources.CopyBgraToRgbaSsse3(source, target, opaque); break;
            case "Avx2": VfxPreparedResources.CopyBgraToRgbaAvx2(source, target, opaque); break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
