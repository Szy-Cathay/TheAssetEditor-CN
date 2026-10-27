using System.Text.Json;
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
public class VfxRegularDecalCoverageTests
{
    [TestCase(true, 0, 1f, -1f, false, 1f)]
    [TestCase(true, 128, .5f, -1f, false, 16f)]
    [TestCase(false, 128, .5f, -1f, false, 1f)]
    [TestCase(true, 128, 2f, -1f, false, 1f)]
    [TestCase(true, 128, .0015f, -1f, false, 1f)]
    [TestCase(true, 128, 1f, .1f, false, 1f)]
    [TestCase(true, 128, 1f, .15f, true, 1f)]
    public void RegularDecal_MatchesNativeDiffuseOutputWithoutResidualEmission(
        bool premultiplied, int alpha, float vertexAlpha, float progress, bool sharp, float redTint)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 to opt into native offscreen rendering.");

        var source = Source(premultiplied, vertexAlpha, progress, sharp, new(redTint, 1, 1));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(source, "vfx/regular-decal.xml",
                _ => VfxMaterialCompositingTests.Texture(192, (byte)alpha)));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics));
            preview.Seek(.2f, Matrix.Identity);
            Assert.That(preview.ParticleCount, Is.EqualTo(1));
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            var background = new Color(26, 51, 77, 255);
            device.SetRenderTarget(target);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, background, 1, 0);
            device.DepthStencilState = DepthStencilState.Default;
            preview.Draw(Camera());
            device.SetRenderTarget(null);
            var pixels = new Vector4[4096];
            target.GetData(pixels);

            // Shipped ps_main_deferred_decal output1 and its ONE / INV_SRC_ALPHA RGB blend.
            // The aligned flat receiver makes the native receiver-angle factor exactly one.
            var textureAlpha = Linear(alpha / 255f);
            var diffuse = Linear(192 / 255f);
            var oldOpacity = Math.Clamp(textureAlpha * vertexAlpha, 0, 1);
            var crushed = oldOpacity;
            if (progress >= 0)
            {
                crushed = Math.Clamp(oldOpacity - progress, 0, 1) / (Math.Clamp(1 - progress, 0, 1) + .001f);
                if (sharp && crushed < progress) crushed = 0;
            }
            var correction = premultiplied ? Math.Clamp(vertexAlpha, 0, 1) : oldOpacity;
            var baseDiffuse = premultiplied ? Math.Min(diffuse, textureAlpha) : diffuse;
            var nativeRgb = new Vector3(redTint, 1, 1) * baseDiffuse
                * (oldOpacity != 0 ? crushed / oldOpacity : 1) * correction;
            nativeRgb = Vector3.Clamp(nativeRgb, Vector3.Zero, Vector3.One);
            var nativeAlpha = Math.Clamp(crushed * correction, 0, 1);
            var backgroundRgb = new Vector3(background.R / 255f, background.G / 255f, background.B / 255f);
            var expected = nativeRgb + backgroundRgb * (1 - nativeAlpha);
            var tolerance = vertexAlpha < .01f ? .000002f : .00015f;
            var centre = pixels[32 * 64 + 32];
            Assert.Multiple(() =>
            {
                Assert.That(centre.X, Is.EqualTo(expected.X).Within(tolerance));
                Assert.That(centre.Y, Is.EqualTo(expected.Y).Within(tolerance));
                Assert.That(centre.Z, Is.EqualTo(expected.Z).Within(tolerance));
                Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
            });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void RegularDecal_TransfersCrushParametersWithoutEditingTheSource()
    {
        var source = Source(true, 1, .125f, true, Vector3.One);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/regular-decal.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].AlphaCrush, Is.Not.Null);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.2f, Matrix.Identity);
        var particle = simulation.Frame(Camera()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(particle.AlphaCrush.X, Is.EqualTo(.125f).Within(.000001f));
            Assert.That(particle.AlphaCrush.Y, Is.EqualTo(2));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(0f, true)]
    [TestCase(.05f, true)]
    [TestCase(.1f, true)]
    [TestCase(.2f, true)]
    [TestCase(-.2f, true)]
    [TestCase(.4f, true)]
    [TestCase(1f, true)]
    [TestCase(.2f, false)]
    public void RegularDecal_UsesNativeReceiverAngleBeforeOutputSaturation(float signedCosine, bool premultiplied)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 to opt into native offscreen rendering.");

        var angle = -MathHelper.ToDegrees(MathF.Asin(signedCosine));
        var source = Source(premultiplied, 1, -1, false, new(16, 1, 1), angle);
        // Exact white isolates the non-PM angle rule from amplified hardware sRGB approximation.
        var gray = premultiplied ? (byte)192 : (byte)255;
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(source, "vfx/regular-angle.xml", _ => VfxMaterialCompositingTests.Texture(gray, 128)));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics));
            preview.Seek(.2f, Matrix.Identity);
            Assert.That(preview.ParticleCount, Is.EqualTo(1));
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            var background = new Color(26, 51, 77, 255);
            device.SetRenderTarget(target);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, background, 1, 0);
            device.DepthStencilState = DepthStencilState.Default;
            preview.Draw(Camera());
            device.SetRenderTarget(null);
            var pixels = new Vector4[4096];
            target.GetData(pixels);

            // Native PS normalizes projector Z, takes abs(dot(Z, receiverNormal)), then smoothsteps.
            // The explicit flat receiver has exact world normal Up; its angle is set independently.
            var u = Math.Min(Math.Abs(signedCosine) * 2.5f, 1);
            var factor = u * u * (3 - 2 * u);
            if (factor < .1f) factor = 0;
            var textureAlpha = Linear(128 / 255f);
            var oldOpacity = textureAlpha;
            var correction = premultiplied ? 1 : oldOpacity;
            var diffuse = premultiplied ? Math.Min(Linear(gray / 255f), textureAlpha) : Linear(gray / 255f);
            var nativeRgb = Vector3.Clamp(new Vector3(16, 1, 1) * diffuse * correction * factor, Vector3.Zero, Vector3.One);
            var nativeAlpha = Math.Clamp(oldOpacity * correction * factor, 0, 1);
            var expected = nativeRgb + new Vector3(background.R / 255f, background.G / 255f, background.B / 255f) * (1 - nativeAlpha);
            var centre = pixels[32 * 64 + 32];
            Assert.Multiple(() =>
            {
                Assert.That(centre.X, Is.EqualTo(expected.X).Within(.00015f));
                Assert.That(centre.Y, Is.EqualTo(expected.Y).Within(.00015f));
                Assert.That(centre.Z, Is.EqualTo(expected.Z).Within(.00015f));
                Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
            });
        }
        finally { graphics.Release(); }
    }

    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);

    private static CommonShaderParameters Camera()
    {
        var eye = new Vector3(0, 4, 6);
        return new(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up), Matrix.CreateOrthographic(4, 4, .1f, 100),
            eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);
    }

    private static byte[] Source(bool premultiplied, float alpha, float progress, bool sharp, Vector3 colour, float angle = -90)
    {
        // Single-variable diagnostic source, not an edited official acceptance sample.
        var document = VfxDocument.Read(VfxMaterialCompositingTests.Source(colour, premultiplied, 1, alpha: alpha));
        var effect = document.Root.Element("vfx")!;
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
            effect.Element("emitters")!.Element("emitter")!.Add(new XElement("property", new XAttribute("name", name),
                new XAttribute("x", angle), new XAttribute("y", 0), new XAttribute("z", 0)));
        effect.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "material0"),
            new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", "DEFERRED_DECAL")))));
        if (progress >= 0)
            effect.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "ps_alpha_crush"),
                new XElement("property", new XAttribute("name", "sharp_transition"), new XAttribute("value", sharp)),
                new XElement("property", new XAttribute("name", "curve_alpha_crush"), VfxLibraryVisibilityTests.Constant("progress", progress))));
        return document.Write();
    }
}
