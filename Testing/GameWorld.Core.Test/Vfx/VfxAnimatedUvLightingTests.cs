#nullable enable
using System.Globalization;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxAnimatedUvLightingTests
{
    [TestCase(false, 0f, .01f, 0f, 0f)]
    [TestCase(false, .7f, .3f, .4f, .6f)]
    [TestCase(true, 4f, .1f, .5f, 1.2f)]
    public void CustomShader_PreservesTheSourceLightingFieldsWithoutInventingAWeatherConsumer(
        bool simple, float emissive, float small, float large, float average)
    {
        var source = Source(simple, emissive, small, large, average);
        var conversion = TotalWarVfxConverter.Convert(new(source, "DIAG/animated-uv-lighting.xml",
            path => path.EndsWith(".material", StringComparison.Ordinal) ? VfxAnimatedUvSpriteTests.Material()
                : VfxMaterialCompositingTests.Texture(255, 255)));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(",", conversion.Diagnostics));
            Assert.That(conversion.Emitters[0].Render.LightingMode, Is.Zero);
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.CustomShaderLightingUnused"), Is.True);
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.False);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void OriginalCustomShaderIntrinsic_IsIndependentOfOrdinaryWeatherUniforms(int mode)
    {
        RequireGpu();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var unlit = Render(graphics.GraphicsDevice, mode, false);
            var weather = Render(graphics.GraphicsDevice, mode, true);
            // Independent native PS arithmetic: (white + white + zero detail) * .1 * alpha * (1+alpha).
            var expected = .2f * .6f * 1.6f;
            Assert.Multiple(() =>
            {
                Assert.That(unlit[32 * 64 + 32].X, Is.EqualTo(expected).Within(.00002f));
                Assert.That(weather, Is.EqualTo(unlit), "The shipped custom PS has no sun or ambient resource.");
            });
        }
        finally { graphics.Release(); }
    }

    [TestCase(-.25f)]
    [TestCase(.75f)]
    public void SoftDepth_UsesTheNativeSignedMinimumXYSize(float minimumSize)
    {
        RequireGpu();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var pixels = Render(graphics.GraphicsDevice, 0, false, minimumSize);
            // Native PS: min_sat(sizeX,sizeY), strength=1, soft=.4, depth separation=.05.
            var thickness = .4 * Math.Clamp((double)minimumSize, 0, 1) + .001;
            var opacity = .6 * Math.Clamp(.05 / thickness, 0, 1);
            var expected = .2 * opacity * (1 + opacity);
            Assert.That(pixels[32 * 64 + 32].X, Is.EqualTo(expected).Within(.0002));
        }
        finally { graphics.Release(); }
    }

    private static byte[] Source(bool simple, float emissive, float small, float large, float average)
    {
        var document = VfxDocument.Read(VfxAnimatedUvSpriteTests.Source());
        var quality = document.Root.Descendants("quality").Single();
        quality.Add(new XElement("modifier", new XAttribute("fragment", "ps_lighting"),
            new[] { ("emissive", emissive), ("small_particles", small), ("large_particles", large),
                ("directionality", .4f), ("average_size", average) }.Select(p => new XElement("property",
                    new XAttribute("name", p.Item1), new XAttribute("type", "float"),
                    new XAttribute("value", p.Item2.ToString(CultureInfo.InvariantCulture))))));
        if (simple)
            document.Root.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "material0"),
                new XElement("property", new XAttribute("name", "enable_simple_lighting"), new XAttribute("type", "bool"), new XAttribute("value", "true")))));
        return document.Write();
    }

    private static Vector4[] Render(GraphicsDevice device, int mode, bool weather, float? minimumSize = null)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [255,255,255,255], 4, 1, 1, 0));
        var material = VfxCustomSpriteMaterial.Read(VfxAnimatedUvSpriteTests.Material()).Material;
        var parameters = (float[])material.Parameters.Clone();
        foreach (var index in new[] { 30, 31, 34, 35 }) parameters[index] = 1;
        if (minimumSize.HasValue) parameters[24] = .4f;
        EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(handle, new int[6], 6, parameters, parameters.Length));
        var layer = EffekseerLayer.Create(); layer.MeshMaterialIndex = 0; layer.LightingMode = mode;
        layer.Emissive = .7f; layer.SmallParticles = .3f; layer.LargeParticles = .4f;
        layer.Directionality = .4f; layer.AverageSize = .6f;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer], 1));
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
        var view = Matrix.CreateLookAt(new(0,0,10), Vector3.Zero, Vector3.Up);
        var projection = minimumSize.HasValue ? Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2,1,.1f,50)
            : Matrix.CreateOrthographic(4,4,.1f,50);
        var particle = new EffekseerParticle { Transform = Matrix.CreateScale(minimumSize.HasValue ? 20 : 4), UV = new(0,0,1,1),
            Colour = new(1,1,1,.6f), ParticleSize = minimumSize.HasValue ? .75f : 4, SoftParticleScale = minimumSize ?? 4 };
        var lighting = weather ? new EffekseerLighting { PositiveX = new(.2f,.3f,.4f,0), NegativeX = new(.4f,.1f,.2f,0),
            PositiveY = new(.9f,.2f,.1f,0), NegativeY = new(.1f,.9f,.2f,0), PositiveZ = new(.2f,.1f,.9f,0),
            NegativeZ = new(.7f,.6f,.5f,0), SunDirection = new(0,-1,0,.5f), SunColour = new(.7f,.4f,.2f,0),
            Parameters = new(1,1,0,0) } : default;
        using var depthData = minimumSize.HasValue ? new SharpDX.DataStream(64 * 64 * sizeof(float), true, true) : null;
        if (depthData != null)
        {
            var clip = Vector4.Transform(new Vector4(0,0,-10.05f,1), projection);
            depthData.WriteRange(Enumerable.Repeat(clip.Z / clip.W, 64 * 64).ToArray()); depthData.Position = 0;
        }
        using var depthTexture = depthData == null ? null : new SharpDX.Direct3D11.Texture2D(direct, new SharpDX.Direct3D11.Texture2DDescription
        {
            Width = 64, Height = 64, MipLevels = 1, ArraySize = 1, Format = SharpDX.DXGI.Format.R32_Float,
            SampleDescription = new(1,0), Usage = SharpDX.Direct3D11.ResourceUsage.Immutable, BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource,
        }, new SharpDX.DataRectangle(depthData.DataPointer, 64 * sizeof(float)));
        using var depthView = depthTexture == null ? null : new SharpDX.Direct3D11.ShaderResourceView(direct, depthTexture);
        try { EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(view), Values(projection), [particle], 1, in lighting, depthView?.NativePointer ?? 0)); }
        finally { device.SetRenderTarget(null); }
        var result = new Vector4[64 * 64]; target.GetData(result); return result;
    }

    private static float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
        m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    private static void RequireGpu()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
    }
}
