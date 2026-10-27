#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Shared.Core.PackFiles.Utility;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxNativeAlphaSamplerTests
{
    private const string TexturePath = @"vfx\textures\generic_cells_b_diffuse.dds";
    private const int Width = 128, Height = 96;
    private const float ScreenSize = 64, VertexAlpha = .75f;
    private (byte[] Pixels, int Width, int Height, bool Srgb, int Mips) _image;
    private static readonly Matrix View = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
    private static readonly Matrix Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), Width / (float)Height, .1f, 50);

    [OneTimeSetUp]
    public void ReadTheCompleteOfficialAtlasWithoutModResolution()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("The original game manifest is required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(row => row.Length > 1)
            .ToDictionary(row => row[0], row => long.Parse(row[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(data!, "vfx.pack");
        Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest["vfx.pack"]));
        var pack = new PackFileContainerLoader(null!).Load(path)!;
        var original = pack.FileList.Single(pair => pair.Key.Replace('/', '\\').Equals(TexturePath, StringComparison.OrdinalIgnoreCase)).Value.DataSource.ReadData();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)),
            Is.EqualTo("0f9f9d442fc80bd338c9826d25df2d0fef4820918c9a68d5fb32d6650cc989ce"));
        // DecodeTexture preserves every Pfim mip, removes block padding and changes BGRA to RGBA.
        var decode = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!;
        _image = ((byte[], int, int, bool, int))decode.Invoke(null, [original, VfxTextureUsage.Diffuse])!;
        Assert.Multiple(() =>
        {
            Assert.That(_image.Width, Is.EqualTo(1024));
            Assert.That(_image.Height, Is.EqualTo(1024));
            Assert.That(_image.Mips, Is.EqualTo(11));
            Assert.That(_image.Srgb, Is.True);
        });
        Assert.That(EffekseerNative.AecnVfxAbi(132, 148, 72, 176), Is.EqualTo(1));
        var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Single(item => item.ModuleName.Equals("AssetEditor.CN.Effekseer.dll", StringComparison.OrdinalIgnoreCase));
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName)));
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_EXPECTED_NATIVE_SHA256") is { Length: > 0 } expected)
            Assert.That(hash, Is.EqualTo(expected).IgnoreCase);
        TestContext.Out.WriteLine($"DIAG original11mip atlas, one FULL particle, actual1x linear-half target; native={hash}. Not complete-effect visual acceptance.");
    }

    [TestCase(6.4f, false)]
    [TestCase(6.6f, false)]
    [TestCase(6.4f, true)]
    [TestCase(6.6f, true)]
    public void FractionalLodPreservesNativeTrilinearRadianceAndOpacity(float lod, bool lowerRight)
    {
        // Clamp keeps the four composition taps identical while derivatives retain a fractional LOD.
        // Real atlas corners differ between mip6/7; no synthetic texels or modified mip chain are used.
        var centre = new Vector2(lowerRight ? 1.5f : -.5f);
        var span = ScreenSize * MathF.Pow(2, lod) / _image.Width;
        var actualLod = MathF.Log2(span / ScreenSize * _image.Width);
        var sampled = Sample(centre, actualLod, nearestMip: false);
        var nearest = Sample(centre, actualLod, nearestMip: true);
        var expected = Material(sampled);
        var wrongMip = Material(nearest);
        var tolerance = new Vector4(Tolerance(expected.X), Tolerance(expected.Y), Tolerance(expected.Z), Tolerance(expected.W));
        var mipGap = Math.Max(Math.Abs(expected.X - wrongMip.X), Math.Max(Math.Abs(expected.Y - wrongMip.Y), Math.Abs(expected.Z - wrongMip.Z)));
        var largestRgbTolerance = Math.Max(tolerance.X, Math.Max(tolerance.Y, tolerance.Z));
        Assert.That(Math.Abs(actualLod - lod), Is.LessThan(.00001f));
        Assert.That(mipGap, Is.GreaterThan(4 * largestRgbTolerance),
            "This exact official atlas sample must distinguish trilinear mip blending from nearest-mip sampling.");

        foreach (var offset in new[] { -.5f, .5f })
        {
            var neighbour = Sample(centre + new Vector2(offset * span / ScreenSize), actualLod, nearestMip: false);
            Assert.That(neighbour, Is.EqualTo(sampled), "The selected clamped region removes full resolve's half-texel spatial interpolation from this mip-only control.");
        }

        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = Preview(device);
            using var target = new RenderTarget2D(device, Width, Height, false, SurfaceFormat.HalfVector4,
                DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(target);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            var views = direct.ImmediateContext.OutputMerger.GetRenderTargets(1, out var depth);
            try
            {
                using var resource = views[0].Resource;
                using var texture = resource.QueryInterface<SharpDX.Direct3D11.Texture2D>();
                Assert.That(texture.Description.SampleDescription.Count, Is.EqualTo(1));
                Assert.That(texture.Description.Format, Is.EqualTo(SharpDX.DXGI.Format.R16G16B16A16_Float));
            }
            finally { foreach (var view in views) view?.Dispose(); depth?.Dispose(); }

            var worldSize = ScreenSize * 20 / (Width * Projection.M11);
            var particle = new EffekseerParticle
            {
                Layer = 2, Transform = Matrix.CreateScale(worldSize, worldSize, 1), Colour = new(1, 1, 1, VertexAlpha),
                UV = new(centre.X - span * .5f, centre.Y - span * .5f, span, span),
            };
            var lighting = new EffekseerLighting();
            EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(preview, in lighting));
            try
            {
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(preview.DangerousGetHandle(), preview,
                    Values(View), Values(Projection), [particle], 1, in lighting));
            }
            finally { EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(preview)); }
            device.SetRenderTarget(null);
            var pixels = new HalfVector4[Width * Height]; target.GetData(pixels);
            foreach (var point in new[] { new Point(Width / 2, Height / 2), new Point(Width / 2 - 1, Height / 2 - 1), new Point(Width / 2 + 1, Height / 2 + 1) })
            {
                var actual = pixels[point.Y * Width + point.X].ToVector4();
                Assert.Multiple(() =>
                {
                    Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance.X), $"R pixel{point}, LOD{actualLod:R}");
                    Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance.Y), $"G pixel{point}, LOD{actualLod:R}");
                    Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(tolerance.Z), $"B pixel{point}, LOD{actualLod:R}");
                    Assert.That(actual.W, Is.EqualTo(expected.W).Within(tolerance.W), $"Coverage pixel{point}, LOD{actualLod:R}");
                });
                var wrongError = Math.Max(Math.Abs(actual.X - wrongMip.X), Math.Max(Math.Abs(actual.Y - wrongMip.Y), Math.Abs(actual.Z - wrongMip.Z)));
                Assert.That(wrongError, Is.GreaterThan(2 * largestRgbTolerance), "Nearest mip remains a separating negative control, not merely non-black output.");
                TestContext.Out.WriteLine($"LOD{actualLod:R} corner{lowerRight} pixel{point} actual{actual} trilinear{expected} nearest{wrongMip} tolerance{tolerance}");
            }
        }
        finally { graphics.Release(); }
    }

    private EffekseerHandle Preview(GraphicsDevice device)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        try
        {
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(preview, _image.Pixels, _image.Pixels.Length,
                _image.Width, _image.Height, 1, _image.Mips));
            var coverage = EffekseerLayer.Create(); coverage.TextureIndex = 0; coverage.MaterialMode = 1; coverage.ZTest = 0;
            var emission = coverage; emission.MaterialMode = 2; emission.Blend = 2;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [coverage, emission, coverage], 3));
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigureFull(preview, [new()
            {
                LayerIndex = 2, LegacyCoverageIndex = 0, LegacyEmissionIndex = 1, Premultiplied = 1,
                CoveredColourMultiplier = 1, EmissionMultiplier = 0,
            }], 1, EffekseerHalfLayer.Bytes));
            return preview;
        }
        catch { preview.Dispose(); throw; }
    }

    private Vector4 Sample(Vector2 uv, float lod, bool nearestMip)
    {
        lod = Math.Clamp(lod, 0, _image.Mips - 1);
        if (nearestMip) return Bilinear(uv, Math.Clamp((int)MathF.Floor(lod + .5f), 0, _image.Mips - 1));
        var low = (int)MathF.Floor(lod); var high = Math.Min(low + 1, _image.Mips - 1);
        return Vector4.Lerp(Bilinear(uv, low), Bilinear(uv, high), lod - low);
    }

    private Vector4 Bilinear(Vector2 uv, int level)
    {
        var width = _image.Width; var height = _image.Height; var offset = 0;
        for (var mip = 0; mip < level; mip++)
        { offset += width * height * 4; width = Math.Max(1, width / 2); height = Math.Max(1, height / 2); }
        var x = uv.X * width - .5f; var y = uv.Y * height - .5f;
        var ix = (int)MathF.Floor(x); var iy = (int)MathF.Floor(y);
        return Vector4.Lerp(Vector4.Lerp(Texel(ix, iy), Texel(ix + 1, iy), x - ix),
            Vector4.Lerp(Texel(ix, iy + 1), Texel(ix + 1, iy + 1), x - ix), y - iy);

        Vector4 Texel(int tx, int ty)
        {
            var index = offset + (Math.Clamp(ty, 0, height - 1) * width + Math.Clamp(tx, 0, width - 1)) * 4;
            return new(Decode(_image.Pixels[index] / 255f), Decode(_image.Pixels[index + 1] / 255f),
                Decode(_image.Pixels[index + 2] / 255f), _image.Pixels[index + 3] / 255f);
        }
    }

    private static Vector4 Material(Vector4 sampled)
    {
        var alpha = Decode(sampled.W);
        var rgb = Vector3.Min(new(sampled.X, sampled.Y, sampled.Z), new(alpha)) * VertexAlpha;
        var coverage = Math.Clamp(alpha * VertexAlpha, 0, 1);
        var effective = Math.Clamp(coverage + BitConverter.Int32BitsToSingle(0x3a83126f),
            BitConverter.Int32BitsToSingle(0x3a83126f), BitConverter.Int32BitsToSingle(0x3f7fbe77));
        // Identical one-fragment neighbours cancel query transmission and absorption in K.
        return new(rgb, effective);
    }

    private static float Decode(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);

    private static float Tolerance(float value)
    {
        var half = (Half)value;
        var bits = BitConverter.HalfToUInt16Bits(half);
        var ulp = (float)BitConverter.UInt16BitsToHalf((ushort)(bits + 1)) - (float)half;
        // Colour accumulation, compose and target store each round to half; also allow SRGB conversion precision.
        return 12 * ulp + .00002f;
    }

    private static float[] Values(Matrix matrix) => [matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44];
}
