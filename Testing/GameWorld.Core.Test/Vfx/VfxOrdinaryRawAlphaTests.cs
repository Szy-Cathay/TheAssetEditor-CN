using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxOrdinaryRawAlphaTests
{
    private static readonly Vector4 Background = new(.2f, .4f, .6f, 1);
    private const int Width = 64;
    private const float FaintAlpha = .0015f;

    [TestCase(3f, 1f)]
    [TestCase(1f, 3f)]
    [TestCase(FaintAlpha, 1f)]
    public void NonpremultipliedSprite_PreservesAlphaUntilTextureCoverage(float initial, float curve)
    {
        var source = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="alpha" type="continuous_mesh">
            <property name="InfiniteLife" value="true"/><property name="MaximumParticles" value="1"/>
            <property name="ParticleLifeTime">{Constant("x", 5)}</property>
            <property name="ParticleInitialAlpha">{Constant("x", initial)}</property>
            </emitter></emitters><behaviour_chains><behaviour_chain id="alpha"><quality value="0">
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/smoke.dds"/>
            <property name="pre_multiplied_alpha" value="false"/></modifier>
            <modifier fragment="vs_vertex_alpha"><property name="curve_alpha">{Constant("alpha", curve)}</property></modifier>
            </quality></behaviour_chain></behaviour_chains></vfx>
            """);
        var texture = OfficialTexture();
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/ordinary-alpha-diagnostic.xml", _ => texture));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
            Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        // Isolate raw opacity; the ordinary default gate is asserted below.
        Assert.That(simulation.Frame(camera with { VfxLowOpacityCulling = -1 }).Single().Colour.W, Is.EqualTo(initial * curve).Within(.000001f),
            "Native process_texture_colour saturates texture alpha times particle alpha, including nonpremultiplied sprites.");
        if (initial * curve <= .01f) Assert.That(simulation.Frame(camera), Is.Empty);
    }

    [TestCase(2f)]
    [TestCase(3f)]
    [TestCase(1000f)]
    public void OrdinaryTexture_SaturatesAfterMultiplyingTheOriginalTextureAlpha(float alpha)
    {
        RequireGpu();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var sample = SampleTexture(graphics.GraphicsDevice, opaque: false);
            var pixels = Render(graphics.GraphicsDevice, sample, [alpha], false, false);
            AssertNativePixel(pixels[32 * Width + 32], sample, alpha);
        }
        finally { graphics.Release(); }
    }

    [TestCase(.6f)]
    [TestCase(.8f)]
    [TestCase(1f)]
    public void UnitRangeFractionalTexel_UsesTheNativeAlphaTransfer(float alpha)
    {
        RequireGpu();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var sample = SampleTexture(graphics.GraphicsDevice, opaque: false);
            var pixels = Render(graphics.GraphicsDevice, sample, [alpha], false, false);
            AssertNativePixel(pixels[32 * Width + 32], sample, alpha);
        }
        finally { graphics.Release(); }
    }

    [TestCase(3f, false, false)]
    [TestCase(3f, true, false)]
    [TestCase(3f, false, true)]
    [TestCase(3f, true, true)]
    [TestCase(1000f, false, false)]
    [TestCase(1000f, true, false)]
    [TestCase(1000f, false, true)]
    [TestCase(1000f, true, true)]
    public void OrdinaryTexture_KeepsFaintAndStrongParticlesInEitherOrder(float strong, bool reverse, bool separateLayers)
    {
        RequireGpu();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var sample = SampleTexture(graphics.GraphicsDevice, opaque: false);
            var pixels = Render(graphics.GraphicsDevice, sample, [FaintAlpha, strong], reverse, separateLayers);
            AssertNativePixel(pixels[32 * Width + 16], sample, FaintAlpha);
            AssertNativePixel(pixels[32 * Width + 48], sample, strong);
            Assert.That(Vector4.Distance(pixels[32 * Width + 16], Background), Is.GreaterThan(.00002f),
                "The original fractional-alpha texel contributes below half a vertex-alpha byte.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(0f)]
    [TestCase(.6f)]
    [TestCase(.8f)]
    [TestCase(1f)]
    public void OrdinaryOpaqueTexel_KeepsTheExistingUnitRangePath(float alpha)
    {
        RequireGpu();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var sample = SampleTexture(graphics.GraphicsDevice, opaque: true);
            var pixels = Render(graphics.GraphicsDevice, sample, [alpha], false, false);
            AssertNativePixel(pixels[32 * Width + 32], sample, alpha);
        }
        finally { graphics.Release(); }
    }

    [TestCase(.25f, false)]
    [TestCase(.25f, true)]
    [TestCase(-.25f, false)]
    public void NonpremultipliedCrush_ReachesEachParticle(float progress, bool sharp)
    {
        var source = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="crush" type="continuous_mesh">
            <property name="InfiniteLife" value="true"/><property name="MaximumParticles" value="1"/>
            <property name="ParticleLifeTime">{Constant("x", 5)}</property>
            <property name="ParticleInitialAlpha">{Constant("x", 3)}</property>
            </emitter></emitters><behaviour_chains><behaviour_chain id="crush"><quality value="0">
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/smoke.dds"/>
            <property name="pre_multiplied_alpha" value="false"/></modifier>
            <modifier fragment="ps_alpha_crush"><property name="sharp_transition" value="{sharp.ToString().ToLowerInvariant()}"/>
            <property name="curve_alpha_crush">{Constant("progress", progress)}</property></modifier>
            </quality></behaviour_chain></behaviour_chains></vfx>
            """);
        var texture = OfficialTexture();
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/ordinary-crush-diagnostic.xml", _ => texture));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].AlphaCrush, Is.Not.Null,
            "Shipped ps_alpha_crush consumes processed texture opacity regardless of the premultiplied flag.");
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
            Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        var particle = simulation.Frame(camera).Single();
        Assert.That(particle.AlphaCrush.X, Is.EqualTo(progress).Within(.000001f));
        Assert.That(particle.AlphaCrush.Y, Is.EqualTo(sharp ? 2 : 1));
    }

    [TestCase(3f, .1f, false, false)]
    [TestCase(1000f, .25f, false, false)]
    [TestCase(3f, .1f, false, true)]
    [TestCase(3f, .25f, true, false)]
    [TestCase(1000f, .25f, true, false)]
    [TestCase(0f, -.25f, false, false)]
    [TestCase(0f, -.25f, true, false)]
    [TestCase(0f, 0f, false, false)]
    [TestCase(0f, .25f, false, false)]
    public void NonpremultipliedCrush_UsesProcessedOpacityAndPreservesTheNativeZeroRgbBranch(
        float rawAlpha, float progress, bool sharp, bool gradient)
    {
        RequireGpu();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var sample = SampleTexture(graphics.GraphicsDevice, opaque: false);
            var crush = new Vector2(progress, sharp ? 2 : 1);
            var pixels = Render(graphics.GraphicsDevice, sample, [rawAlpha], false, false, crush, gradient);
            AssertNativePixel(pixels[32 * Width + 32], sample, rawAlpha, crush);
        }
        finally { graphics.Release(); }
    }

    private static void AssertNativePixel(Vector4 actual, TextureSample sample, float rawAlpha, Vector2? crush = null)
    {
        // Shipped process_texture_colour: straight colour, then saturate(textureAlpha * vertexAlpha).
        var opacity = Math.Clamp(Linear(sample.Rgba[3]) * rawAlpha, 0, 1);
        var colour = sample.ReferenceRgb;
        if (crush is { } settings)
        {
            // Independent ps_alpha_crush plus its native straight-colour output ratio.
            if (opacity == 0) colour = Vector3.Zero;
            opacity = Math.Clamp(opacity - settings.X, 0, 1) / (Math.Clamp(1 - settings.X, 0, 1) + .001f);
            if (settings.Y > 1 && opacity < settings.X) opacity = 0;
        }
        var expected = new Vector4(colour * opacity
            + new Vector3(Background.X, Background.Y, Background.Z) * (1 - opacity), 1);
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(.00001f), "Native nonpremultiplied red coverage.");
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(.00001f), "Native nonpremultiplied green coverage.");
        Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(.00001f), "Native nonpremultiplied blue coverage.");
        Assert.That(actual.W, Is.EqualTo(expected.W).Within(.00001f), "An opaque scene remains opaque after coverage.");
    }

    private static Vector4[] Render(GraphicsDevice device, TextureSample sample, float[] alphas, bool reverse, bool separateLayers,
        Vector2? crush = null, bool gradient = false)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(preview, sample.Pixels, sample.Pixels.Length,
            sample.Width, sample.Height, sample.Srgb ? 1 : 0, sample.MipLevels));
        var layer = EffekseerLayer.Create();
        layer.TextureIndex = 0; layer.MaterialMode = 0; layer.Blend = 1;
        if (gradient)
        {
            // Constant gradient control retains this same original texel RGB; alpha mapping stays disabled.
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, sample.Rgba, 4, 1, 1, sample.Srgb ? 1 : 0));
            layer.GradientIndex = 1; layer.GradientScale = 1; layer.GradientAlphaMapping = 0;
        }
        var layers = Enumerable.Repeat(layer, separateLayers ? alphas.Length : 1).ToArray();
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, layers, layers.Length));
        var particles = alphas.Select((alpha, index) => new EffekseerParticle
        {
            Layer = separateLayers ? index : 0,
            Transform = Matrix.CreateScale(.75f) * Matrix.CreateTranslation(alphas.Length == 1 ? 0 : index * 2 - 1, 0, 0),
            Colour = new(1, 1, 1, alpha), UV = sample.Uv,
            AlphaCrush = crush ?? Vector2.Zero,
        }).ToArray();
        if (reverse) Array.Reverse(particles);
        using var target = new RenderTarget2D(device, Width, Width, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target);
        device.Clear(new Color(Background)); device.DepthStencilState = DepthStencilState.Default;
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,
            Values(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up)),
            Values(Matrix.CreateOrthographic(4, 4, .1f, 50)), particles, particles.Length));
        device.SetRenderTarget(null);
        var pixels = new Vector4[Width * Width]; target.GetData(pixels);
        return pixels;
    }

    private static TextureSample SampleTexture(GraphicsDevice device, bool opaque)
    {
        var decode = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!;
        var image = ((byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels))decode.Invoke(null,
            [OfficialTexture(), VfxTextureUsage.Diffuse])!;
        var best = -1;
        var contrast = -1f;
        for (var index = 0; index < image.Width * image.Height; index++)
        {
            var offset = index * 4;
            var alpha = image.Pixels[offset + 3];
            if (alpha is < 88 or > 128) continue;
            var rgb = new Vector3(Linear(image.Pixels[offset]), Linear(image.Pixels[offset + 1]), Linear(image.Pixels[offset + 2]));
            var distance = Vector3.DistanceSquared(rgb, new(Background.X, Background.Y, Background.Z));
            if (distance <= contrast) continue;
            contrast = distance; best = index;
        }
        Assert.That(best, Is.GreaterThanOrEqualTo(0), "The unchanged official smoke texture must contain the required fractional-alpha texel.");
        var originalRgba = image.Pixels.Skip(best * 4).Take(4).ToArray();
        var rgba = (byte[])originalRgba.Clone();
        var pixels = image.Pixels;
        if (opaque)
        {
            // Single-variable diagnostic: preserve the original source and RGB, change only this mip0 texel's alpha.
            pixels = (byte[])image.Pixels.Clone();
            pixels[best * 4 + 3] = 255; rgba[3] = 255;
        }
        TestContext.Progress.WriteLine($"DIAG: official smoke DDS remains unchanged; original RGBA={string.Join(',', originalRgba)}, uploaded RGBA={string.Join(',', rgba)}, mip0 texel={best}. "
            + (opaque ? "Only that decoded texel alpha is changed to255 for an opaque control; all RGB and other mip bytes remain original. "
                : "Unchanged decoded texel and complete original mip chain. ")
            + "Controlled straight-alpha mode and particle values; not complete-effect visual acceptance.");
        var referenceRgb = HardwareRgbReference(device, rgba);
        return new(pixels, image.Width, image.Height, image.Srgb, image.MipLevels, rgba, referenceRgb,
            new((best % image.Width + .5f) / image.Width, (best / image.Width + .5f) / image.Height, 0, 0));
    }

    private static Vector3 HardwareRgbReference(GraphicsDevice device, byte[] rgba)
    {
        // Independent MonoGame sampling measures hardware sRGB precision, without any VFX material.
        using var texture = new Texture2D(device, 1, 1, false, SurfaceFormat.ColorSRgb);
        texture.SetData(rgba);
        using var target = new RenderTarget2D(device, 1, 1, false, SurfaceFormat.Vector4, DepthFormat.None);
        using var sprites = new SpriteBatch(device);
        device.SetRenderTarget(target); device.Clear(Color.Transparent);
        sprites.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        sprites.Draw(texture, new Rectangle(0, 0, 1, 1), Color.White);
        sprites.End(); device.SetRenderTarget(null);
        var sampled = new Vector4[1]; target.GetData(sampled);
        Assert.That(sampled[0].W, Is.EqualTo(rgba[3] / 255f).Within(.000001f),
            "The independent sRGB reference must leave alpha encoded; native alpha transfer is calculated separately.");
        TestContext.Progress.WriteLine($"DIAG: independent MonoGame ColorSRgb/Opaque/PointClamp RGB={sampled[0].X},{sampled[0].Y},{sampled[0].Z}; no VFX shader reference.");
        return new(sampled[0].X, sampled[0].Y, sampled[0].Z);
    }

    private static byte[] OfficialTexture()
    {
        var assembly = typeof(VfxOrdinaryRawAlphaTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("OfficialSiegeSmokeDiffuse.dds", StringComparison.Ordinal)))!;
        using var output = new MemoryStream(); stream.CopyTo(output);
        var bytes = output.ToArray();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Is.EqualTo("18346cc4e31f527c4abe1d92c287cd50591aa3e154e67b5815b44497e9aa4a70"));
        return bytes;
    }

    private static float Linear(byte value)
    {
        var encoded = value / 255f;
        return encoded <= .04045f ? encoded / 12.92f : MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
    }

    private static void RequireGpu()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Requires opt-in DX11 offscreen pixels; no window or desktop input.");
    }

    private static string Constant(string axis, float value) => $"{axis},0,1;0,{value.ToString(System.Globalization.CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1:{axis},0;0,0";
    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
    private sealed record TextureSample(byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels, byte[] Rgba,
        Vector3 ReferenceRgb, Vector4 Uv);
}
