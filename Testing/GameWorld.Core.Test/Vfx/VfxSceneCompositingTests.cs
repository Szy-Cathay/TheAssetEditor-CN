#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxSceneCompositingTests
{
    private const string TexturePath = @"vfx\textures\wh_fire_loop_a_grayscale_diffuse.dds";
    private readonly VfxOfficialDistortionPixelTests _official = new();
    private (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) _image;
    private Vector4 _emissionUv, _coverageUv;

    [OneTimeSetUp]
    public void LoadOnlyTheOriginalOfficialDiffuse()
    {
        _official.LoadCurrentOfficialResourcesOnly();
        var read = typeof(VfxOfficialDistortionPixelTests).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var bytes = (byte[]?)read.Invoke(_official, [TexturePath]) ?? throw new FileNotFoundException(TexturePath);
        var decode = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!;
        _image = ((byte[], int, int, bool, int))decode.Invoke(null, [bytes, VfxTextureUsage.Diffuse])!;
        var emission = -1; var coverage = -1; var maximumEmission = 0f; var maximumAlpha = 0;
        for (var pixel = 0; pixel < _image.Width * _image.Height; pixel++)
        {
            var offset = pixel * 4;
            var r = _image.Pixels[offset]; var g = _image.Pixels[offset + 1];
            var b = _image.Pixels[offset + 2]; var a = _image.Pixels[offset + 3];
            var emitted = Linear(r) - Linear(a);
            if (r == g && g == b && emitted > maximumEmission) { maximumEmission = emitted; emission = pixel; }
            if (a > maximumAlpha) { maximumAlpha = a; coverage = pixel; }
        }
        Assert.Multiple(() =>
        {
            Assert.That(_image.MipLevels, Is.GreaterThan(1), "The original DDS mip chain must remain intact.");
            Assert.That(maximumEmission, Is.EqualTo(1), "The original grayscale fire atlas has a white, zero-alpha emissive texel.");
            Assert.That(Linear((byte)maximumAlpha), Is.GreaterThan(.5f),
                "An original atlas texel and particle alpha2 must provide opaque coverage without altering the DDS.");
        });
        _emissionUv = ConstantUv(emission);
        _coverageUv = ConstantUv(coverage);
        object Texel(int index) => new
        {
            X = index % _image.Width, Y = index / _image.Width,
            Rgba = _image.Pixels.Skip(index * 4).Take(4).Select(value => (int)value).ToArray(),
        };
        var origins = typeof(VfxOfficialDistortionPixelTests).GetField("_origins", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_official);
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new
        {
            Boundary = "DIAG constant UV samples from the full unmodified official atlas; tint/alpha controls only. No synthetic texture, crop, pixel edit or complete-effect acceptance claim.",
            TexturePath, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), _image.Width, _image.Height,
            _image.MipLevels, _image.Srgb, Emission = Texel(emission), Coverage = Texel(coverage), Origins = origins,
        }));
    }

    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(4, false)]
    [TestCase(4, true)]
    public void SceneScope_PreservesHdrColourAcrossIndependentEffects(int samples, bool reverse)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var first = Preview(device);
            using var second = Preview(device);
            var a = Particle(new(0, 0, 100, 1));
            var b = Particle(new(100, 0, 0, 1));
            if (reverse) (a, b) = (b, a);
            var combined = Render(device, first, second, [a, b], samples, false, false);
            var shared = Render(device, first, second, [a, b], samples, true, true);
            Assert.Multiple(() =>
            {
                Assert.That(combined[2080].R, Is.EqualTo(255));
                Assert.That(combined[2080].B, Is.EqualTo(255), "Both HDR channels must remain present before the final transfer.");
                Assert.That(MaximumDifference(combined, shared), Is.LessThanOrEqualTo(1),
                    "Splitting the same additive input into independent Effect handles must not truncate one channel first.");
            });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void SceneScope_BloomsOnlyTheFinalVisibleComposition()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var first = Preview(device);
            using var second = Preview(device);
            var bright = Particle(new(8, 8, 8, 1));
            var cover = Particle(new(0, 0, 0, 2)); cover.Layer = 1; cover.UV = _coverageUv;
            var combined = Render(device, first, second, [bright, cover], 0, false, false, .1f);
            var shared = Render(device, first, second, [bright, cover], 0, true, true, .1f);
            Assert.Multiple(() =>
            {
                Assert.That(combined.All(pixel => pixel.R == 0 && pixel.G == 0 && pixel.B == 0), Is.True,
                    "The opaque dark particle hides the light source before bloom extraction.");
                Assert.That(MaximumDifference(combined, shared), Is.LessThanOrEqualTo(1),
                    "A covered light source must not leave an earlier Effect's independently resolved halo.");
            });
        }
        finally { graphics.Release(); }
    }

    [TestCase(0)]
    [TestCase(4)]
    public void SceneScope_EmptyGroupPreservesDisplayPixelsAndViewport(int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var owner = Preview(device);
            using var target = new RenderTarget2D(device, 80, 72, false, SurfaceFormat.Color, DepthFormat.Depth24,
                samples, RenderTargetUsage.PreserveContents);
            var background = new Color(51, 82, 140, 191);
            var viewport = new Viewport(8, 6, 64, 56);
            device.SetRenderTarget(target); device.Clear(background); device.Viewport = viewport;
            var nativeTarget = NativeTarget(device);
            Assert.That(nativeTarget, Is.Not.EqualTo(nint.Zero));
            var lighting = Lighting();
            EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(owner, in lighting));
            EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(owner));
            Assert.Multiple(() =>
            {
                Assert.That(device.Viewport, Is.EqualTo(viewport));
                Assert.That(device.GetRenderTargets()[0].RenderTarget, Is.SameAs(target));
                Assert.That(NativeTarget(device), Is.EqualTo(nativeTarget));
            });
            device.SetRenderTarget(null);
            var pixels = new Color[80 * 72]; target.GetData(pixels);
            Assert.That(pixels.All(value => value == background), Is.True,
                "An empty group must not expose decoded linear colour or re-map an already displayed background.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void SceneScope_EndRestoresTargetAfterFailedEffectDraw()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var owner = Preview(device);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24,
                0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(target); device.Clear(Color.Black);
            var nativeTarget = NativeTarget(device);
            var lighting = Lighting();
            var invalid = Particle(Vector4.One); invalid.Layer = -1;
            EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(owner, in lighting));
            try
            {
                Assert.That(EffekseerNative.AecnVfxDrawInScene(owner.DangerousGetHandle(), owner, Values(View), Values(Projection), [invalid], 1, in lighting), Is.Zero);
            }
            finally { EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(owner)); }
            Assert.That(device.GetRenderTargets()[0].RenderTarget, Is.SameAs(target));
            Assert.That(NativeTarget(device), Is.EqualTo(nativeTarget));
            var valid = Particle(new(.5f, .5f, .5f, 1));
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(owner, Values(View), Values(Projection), [valid], 1, in lighting));
            device.SetRenderTarget(null);
            var pixels = new Color[4096]; target.GetData(pixels);
            Assert.That((int)pixels[2080].R, Is.EqualTo(157).Within(1),
                "Later effects must render into the restored display target after a failed draw.");
        }
        finally { graphics.Release(); }
    }

    private static readonly Matrix View = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
    private static readonly Matrix Projection = Matrix.CreateOrthographic(20, 20, .1f, 50);
    private static EffekseerLighting Lighting() => new() { Display = new(1, 1, .05f, 0), Bloom = new(4, 0, 0, 0) };

    private EffekseerHandle Preview(GraphicsDevice device)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        Assert.That(handle.IsInvalid, Is.False);
        // Keep all original texels and mips; constant UVs isolate only the scene composition boundary.
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, _image.Pixels, _image.Pixels.Length,
            _image.Width, _image.Height, _image.Srgb ? 1 : 0, _image.MipLevels));
        var layer = EffekseerLayer.Create(); layer.Blend = 2; layer.ZTest = 0;
        layer.MaterialMode = EffekseerLayer.PreciseEmissionMode;
        var cover = EffekseerLayer.Create(); cover.ZTest = 0;
        cover.MaterialMode = EffekseerLayer.PreciseCoverageMode;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer, cover], 2));
        return handle;
    }

    private static Color[] Render(GraphicsDevice device, EffekseerHandle first, EffekseerHandle second,
        EffekseerParticle[] particles, int samples, bool split, bool shared, float bloomStrength = 0)
    {
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24,
            samples, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        var lighting = Lighting(); lighting.Bloom.Y = bloomStrength;
        if (shared) EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(first, in lighting));
        try
        {
            if (split)
            {
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(), first, Values(View), Values(Projection), [particles[0]], 1, in lighting));
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(), second, Values(View), Values(Projection), [particles[1]], 1, in lighting));
            }
            else EffekseerNative.Check(EffekseerNative.AecnVfxDraw(first, Values(View), Values(Projection), particles, particles.Length, in lighting));
        }
        finally { if (shared) EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(first)); }
        device.SetRenderTarget(null);
        var pixels = new Color[4096]; target.GetData(pixels); return pixels;
    }

    private EffekseerParticle Particle(Vector4 colour) => new()
    {
        Transform = Matrix.CreateScale(10), Colour = colour, UV = _emissionUv,
    };

    private Vector4 ConstantUv(int pixel) => new((pixel % _image.Width + .5f) / _image.Width,
        (pixel / _image.Width + .5f) / _image.Height, 0, 0);
    private static float Linear(byte value)
    {
        var c = value / 255f;
        return c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
    }

    private static nint NativeTarget(GraphicsDevice device)
    {
        var context = ((SharpDX.Direct3D11.Device)device.Handle).ImmediateContext;
        var targets = context.OutputMerger.GetRenderTargets(1, out var depth);
        try { return targets[0]?.NativePointer ?? nint.Zero; }
        finally
        {
            foreach (var target in targets) target?.Dispose();
            depth?.Dispose();
        }
    }
    private static int MaximumDifference(Color[] a, Color[] b) => a.Zip(b).Max(pair =>
        Math.Max(Math.Abs(pair.First.R - pair.Second.R), Math.Max(Math.Abs(pair.First.G - pair.Second.G), Math.Abs(pair.First.B - pair.Second.B))));
    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
