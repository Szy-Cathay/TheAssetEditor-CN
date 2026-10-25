#nullable enable
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;
using Color = Microsoft.Xna.Framework.Color;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxFaintAlphaTests
{
    private const float FaintAlpha = .0015f;
    private const float Brightness = 16;
    private (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) _image;
    private readonly Dictionary<int, (Vector4 Uv, Vector3 Colour)> _samples = [];

    [OneTimeSetUp]
    public void LoadUnchangedOfficialTexture()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Requires explicit opt-in DX11 offscreen pixels.");
        var originals = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_FAINT_ALPHA_ORIGINALS");
        if (string.IsNullOrWhiteSpace(originals))
            Assert.Ignore("Requires exported official poison-trail originals, with manifest provenance retained beside them.");
        var path = Path.Combine(originals!, "vfx/textures/wh_fire_loop_a_grayscale_diffuse.dds");
        var bytes = File.ReadAllBytes(path);
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Is.EqualTo("047a461d879c9a64a653f0fb6a3584eaa8354c557951f894dcd08b985d2bacc6"),
            "Use the unchanged manifest-derived official DDS, never a generated or brightened replacement.");
        var decode = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!;
        _image = ((byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels))decode.Invoke(null, [bytes, VfxTextureUsage.Diffuse])!;
        foreach (var mode in new[] { 1, 2 })
        {
            var best = Vector3.Zero; var bestIndex = -1;
            for (var index = 0; index < _image.Width * _image.Height; index++)
            {
                var offset = index * 4;
                var rgb = new Vector3(Linear(_image.Pixels[offset]), Linear(_image.Pixels[offset + 1]), Linear(_image.Pixels[offset + 2]));
                var alpha = Linear(_image.Pixels[offset + 3]);
                var contribution = mode == 1 ? Vector3.Min(rgb, new(alpha)) : Vector3.Max(rgb - new Vector3(alpha), Vector3.Zero);
                if (contribution.LengthSquared() <= best.LengthSquared()) continue;
                best = contribution; bestIndex = index;
            }
            Assert.That(bestIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(best.Length(), Is.GreaterThan(.05f), "The original texture must contain this pass's own observable contribution.");
            _samples.Add(mode, (new((bestIndex % _image.Width + .5f) / _image.Width,
                (bestIndex / _image.Width + .5f) / _image.Height, 0, 0), best));
        }
        TestContext.Progress.WriteLine("DIAG: unchanged official DDS, fixed native sprite quad and fixed original texel; controlled particle RGB/alpha only. Float target, no tone mapping. Not complete-effect visual acceptance.");
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(EffekseerLayer.PreciseCoverageMode)]
    [TestCase(EffekseerLayer.PreciseEmissionMode)]
    public void PositiveSubByteAlpha_PreservesItsFloatContribution(int mode)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var empty = Render(graphics.GraphicsDevice, mode, [0]);
            var faint = Render(graphics.GraphicsDevice, mode, [FaintAlpha]);
            AssertPixel(faint[2080], mode, FaintAlpha);
            Assert.That(Vector4.Distance(empty[2080], faint[2080]), Is.GreaterThan(.0001f),
                "A positive official-texture contribution must survive below 0.5/255 particle alpha.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ZeroAlpha_RemainsAnIdentityDraw(int mode)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var empty = Render(graphics.GraphicsDevice, mode, []);
            var zero = Render(graphics.GraphicsDevice, mode, [0]);
            Assert.That(empty.Zip(zero).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThan(.000001f));
        }
        finally { graphics.Release(); }
    }

    [TestCase(1, .25f, false)]
    [TestCase(1, .25f, true)]
    [TestCase(2, .25f, false)]
    [TestCase(2, .25f, true)]
    [TestCase(1, 1000f, false)]
    [TestCase(1, 1000f, true)]
    [TestCase(2, 1000f, false)]
    [TestCase(2, 1000f, true)]
    public void SameLayer_PreservesWeakAndStrongParticlesInEitherSubmissionOrder(int mode, float strong, bool reverse)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            float[] alphas = reverse ? [strong, FaintAlpha] : [FaintAlpha, strong];
            var pixels = Render(graphics.GraphicsDevice, mode, alphas);
            AssertPixel(pixels[32 * 64 + 16], mode, alphas[0]);
            AssertPixel(pixels[32 * 64 + 48], mode, alphas[1]);
        }
        finally { graphics.Release(); }
    }

    private void AssertPixel(Vector4 actual, int materialMode, float alpha)
    {
        var mode = materialMode == EffekseerLayer.PreciseCoverageMode ? 1
            : materialMode == EffekseerLayer.PreciseEmissionMode ? 2 : materialMode;
        var expected = _samples[mode].Colour * Brightness * Math.Min(alpha, 1);
        var difference = Vector3.Distance(new(actual.X, actual.Y, actual.Z), expected);
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { MaterialMode = materialMode, Alpha = alpha,
            Expected = new[] { expected.X, expected.Y, expected.Z }, Actual = new[] { actual.X, actual.Y, actual.Z }, Difference = difference }));
        Assert.That(difference, Is.LessThan(Math.Max(.00002f, expected.Length() * .001f)),
            "Reconstruct the authored float alpha after byte encoding, without increasing texture or particle intensity.");
    }

    private Vector4[] Render(GraphicsDevice device, int materialMode, float[] alphas)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(preview, _image.Pixels, _image.Pixels.Length,
            _image.Width, _image.Height, _image.Srgb ? 1 : 0, _image.MipLevels));
        var mode = materialMode == EffekseerLayer.PreciseCoverageMode ? 1
            : materialMode == EffekseerLayer.PreciseEmissionMode ? 2 : materialMode;
        var layer = EffekseerLayer.Create(); layer.MaterialMode = materialMode; layer.Blend = mode == 1 ? 1 : 2;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
        var particles = alphas.Select((alpha, index) => new EffekseerParticle
        {
            Transform = Matrix.CreateScale(.75f) * Matrix.CreateTranslation(alphas.Length == 1 ? 0 : index * 2 - 1, 0, 0),
            Colour = new(Brightness, Brightness, Brightness, alpha), UV = _samples[mode].Uv,
        }).ToArray();
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Transparent); device.DepthStencilState = DepthStencilState.Default;
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,
            Values(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up)), Values(Matrix.CreateOrthographic(4, 4, .1f, 50)), particles, particles.Length));
        device.SetRenderTarget(null);
        var pixels = new Vector4[4096]; target.GetData(pixels); return pixels;
    }

    private static float Linear(byte value) => value <= 10 ? value / 255f / 12.92f : MathF.Pow((value / 255f + .055f) / 1.055f, 2.4f);
    private static float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
        m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
}
