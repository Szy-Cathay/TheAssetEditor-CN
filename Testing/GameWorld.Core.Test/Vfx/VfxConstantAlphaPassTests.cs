using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxConstantAlphaPassTests
{
    private static readonly Vector3 Eye = new(0, 0, 10);
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(Eye, Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(6, 6, .1f, 100), Eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(0, 0f)]
    [TestCase(0, .25f)]
    [TestCase(0, 1f)]
    [TestCase(0, 3f)]
    [TestCase(0, 1000f)]
    [TestCase(255, 0f)]
    [TestCase(255, .25f)]
    [TestCase(255, 1f)]
    [TestCase(255, 3f)]
    [TestCase(255, 1000f)]
    public void ConstantAlphaTexture_KeepsAllRgbaChannelsAndSubmitsOneUsefulPass(int textureAlpha, float particleAlpha)
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var texture = VfxMaterialCompositingTests.Texture(128, (byte)textureAlpha);
            var source = VfxMaterialCompositingTests.Source(Vector3.One, true, 1, alpha: particleAlpha);
            var expected = RenderOriginalPair(device, source, texture, (byte)textureAlpha);
            using var preview = new EffekseerVfxPreview(device); preview.Reload(new(source, "vfx/constant-alpha.xml", _ => texture));
            var actual = Render(device, preview);
            Assert.Multiple(() =>
            {
                Assert.That(actual.Zip(expected).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThan(.00001f),
                    "Removing an identity operation must preserve RGB and the native MAX alpha operation, including faint and high raw alpha.");
                Assert.That(preview.DrawCallCount, Is.EqualTo(particleAlpha == 0 ? 0 : 1));
            });
        }
        finally { graphics.Release(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PartialOrMixedTextureAlpha_KeepsBothContributingPasses(bool mixed)
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var texture = VfxMaterialCompositingTests.Texture(200, 128);
            if (mixed) { texture[131] = 0; texture[135] = 255; }
            var source = VfxMaterialCompositingTests.Source(Vector3.One, true, 1);
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice); preview.Reload(new(source, "vfx/mixed-alpha.xml", _ => texture));
            Render(graphics.GraphicsDevice, preview);
            Assert.That(preview.DrawCallCount, Is.EqualTo(2));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void GradientMapping_KeepsBothPassesEvenForOpaqueSourceTexture()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, true, 1));
            var rgb = string.Join('|', new[] { "r", "g", "b" }.Select(c => VfxLibraryVisibilityTests.Constant(c, 1).Split(':')[0]))
                + ":r,0;0,0|g,0;1,1|b,0;2,2";
            doc.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "ps_gradient"),
                new XElement("property", new XAttribute("name", "curve_colour"), rgb)));
            var bytes = doc.Write(); var texture = VfxMaterialCompositingTests.Texture(200, 255);
            var plan = TotalWarVfxConverter.Convert(new(bytes, "vfx/gradient.xml", _ => texture));
            Assert.That(plan.Emitters.Single().Render.GradientIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(plan.Emitters.Single().Render.EmissionShapeWeight, Is.Zero,
                "The plain texture calibration does not change the gradient emission path.");
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice); preview.Reload(new(bytes, "vfx/gradient.xml", _ => texture));
            Render(graphics.GraphicsDevice, preview); Assert.That(preview.DrawCallCount, Is.EqualTo(2));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void AliasedTexture_UsesTheDecodedAlphaClassificationForBothLogicalSlots()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, true, 1, emitters: 2));
            doc.Root.Descendants("texture_property").Last().SetAttributeValue("path", "vfx/alias.dds");
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(new(doc.Write(), "vfx/alias.xml", _ => VfxMaterialCompositingTests.Texture(128, 255)));
            Render(graphics.GraphicsDevice, preview);
            Assert.Multiple(() => { Assert.That(preview.EmitterCount, Is.EqualTo(2)); Assert.That(preview.DrawCallCount, Is.LessThanOrEqualTo(2)); });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void ReloadedTexture_DoesNotReuseThePreviousAlphaClassification()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice); var source = VfxMaterialCompositingTests.Source(Vector3.One, true, 1);
            var calls = new List<int>();
            foreach (var alpha in new byte[] { 255, 128, 0 })
            {
                preview.Reload(new(source, "vfx/reload.xml", _ => VfxMaterialCompositingTests.Texture(128, alpha)));
                Render(graphics.GraphicsDevice, preview); calls.Add(preview.DrawCallCount);
            }
            Assert.That(calls, Is.EqualTo(new[] { 1, 2, 1 }));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void HighCountOpaqueTexture_IsBatchedInsteadOfSubmittingEachParticleTwice()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, true, 1, rate: 10000));
            doc.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 12000)));
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(new(doc.Write(), "vfx/high-count.xml", _ => VfxMaterialCompositingTests.Texture(128, 255)));
            Render(graphics.GraphicsDevice, preview, 1.3f);
            Assert.Multiple(() => { Assert.That(preview.ParticleCount, Is.EqualTo(12000)); Assert.That(preview.DrawCallCount, Is.LessThanOrEqualTo(2)); });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void ZeroAlphaWithEmissionDisabled_RemainsAValidEmptyColourPreview()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(new(VfxMaterialCompositingTests.Source(Vector3.One, true, 0), "vfx/no-emission.xml", _ => VfxMaterialCompositingTests.Texture(255, 0)));
            Render(graphics.GraphicsDevice, preview); Assert.That(preview.EmitterCount, Is.EqualTo(1));
            Assert.That(preview.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
        }
        finally { graphics.Release(); }
    }

    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(255, false)]
    [TestCase(255, true)]
    public void MergedPass_KeepsEachParticlesOwnColourAndRawAlphaPrecision(int textureAlpha, bool varyingColour)
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, true, 1, rate: 5));
            var emitter = doc.Root.Descendants("emitter").Single();
            string Vector(float value) => string.Join('|', new[] { "x", "y", "z" }.Select(c => VfxLibraryVisibilityTests.Constant(c, value).Split(':')[0]))
                + ":x,0;0,0|y,0;1,1|z,0;2,2";
            emitter.Elements().Single(e => e.Attribute("name")?.Value == "ParticleScale").Value = Vector(.5f);
            var position = "x,0,0;0,-1,linear,linear,0,0,0,0,1;0.2,1,linear,linear,0,0,0,0,1|"
                + string.Join('|', new[] { "y", "z" }.Select(c => VfxLibraryVisibilityTests.Constant(c, 0).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
            emitter.Add(new XElement("property", new XAttribute("name", "EmitterPosition"), position),
                new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), VfxLibraryVisibilityTests.Constant("x", 0)));
            var fragment = doc.Root.Descendants("modifier").Single(m => m.Attribute("fragment")?.Value == (varyingColour ? "vs_colour" : "vs_vertex_alpha"));
            var curve = fragment.Element("property")!;
            if (varyingColour)
            {
                var parts = curve.Value.Split(':'); var channels = parts[0].Split('|');
                channels[^1] = "brightness,0,0;0,1000,linear,linear,0,0,0,0,1;1,1,linear,linear,0,0,0,0,1";
                curve.Value = string.Join('|', channels) + ":" + parts[1];
            }
            else curve.Value = "alpha,0,0;0,1.948,linear,linear,0,0,0,0,1;0.125,1.0001,linear,linear,0,0,0,0,1:alpha,0;0,0";
            var source = doc.Write(); var texture = VfxMaterialCompositingTests.Texture(128, (byte)textureAlpha);
            var expected = RenderOriginalPair(graphics.GraphicsDevice, source, texture, (byte)textureAlpha, .25f);
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice); preview.Reload(new(source, "vfx/precision.xml", _ => texture));
            var actual = Render(graphics.GraphicsDevice, preview, .25f);
            Assert.Multiple(() =>
            {
                Assert.That(preview.ParticleCount, Is.EqualTo(2));
                Assert.That(actual.Zip(expected).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThan(.00001f),
                    "Batching must not quantize weaker RGB or raw alpha against a neighbouring particle's normalization scale.");
            });
        }
        finally { graphics.Release(); }
    }

    private static Vector4[] Render(GraphicsDevice device, EffekseerVfxPreview preview, float time = 0)
    {
        preview.Seek(time, Matrix.Identity);
        using var target = new RenderTarget2D(device, 32, 32, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(new Color(.2f, .3f, .4f, .1f)); device.DepthStencilState = DepthStencilState.Default;
        preview.Draw(Camera); device.SetRenderTarget(null); var pixels = new Vector4[1024]; target.GetData(pixels); return pixels;
    }

    private static Vector4[] RenderOriginalPair(GraphicsDevice device, byte[] source, byte[] texture, byte alpha, float time = 0)
    {
        var plan = TotalWarVfxConverter.Convert(new(source, "vfx/pair.xml", _ => texture)); var simulation = new TotalWarVfxSimulation(plan); simulation.Seek(time, Matrix.Identity);
        var direct = (SharpDX.Direct3D11.Device)device.Handle; using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [128, 128, 128, alpha], 4, 1, 1, 1));
        var coverage = plan.Emitters[0].Render; coverage.MaterialMode = 1; coverage.Blend = 1;
        var emission = coverage; emission.MaterialMode = 2; emission.Blend = 2;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [coverage, emission], 2));
        var frame = simulation.Frame(Camera); var particles = frame.SelectMany(particle =>
        {
            var covered = particle; covered.Layer = 0; var glow = particle; glow.Layer = 1; return new[] { covered, glow };
        }).ToArray();
        using var target = new RenderTarget2D(device, 32, 32, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(new Color(.2f, .3f, .4f, .1f)); device.DepthStencilState = DepthStencilState.Default;
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(Camera.View), Values(Camera.Projection), particles, particles.Length));
        device.SetRenderTarget(null); var pixels = new Vector4[1024]; target.GetData(pixels); return pixels;
    }

    private static void RequireGpu() { if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in."); }
    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
