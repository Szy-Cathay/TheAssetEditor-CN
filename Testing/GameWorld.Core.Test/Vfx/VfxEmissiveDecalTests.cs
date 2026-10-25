using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxEmissiveDecalTests
{
    [TestCase(false, 3)]
    [TestCase(true, 4)]
    public void EmissivePass_SelectsItsOwnMaterialWithoutChangingTheDocument(bool premultiplied, int mode)
    {
        var source = Source(premultiplied, 1);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/decal.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters[0].Render.MaterialMode, Is.EqualTo(mode));
            Assert.That(conversion.Emitters[0].Render.Blend, Is.EqualTo(2));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(false, 128, 255, 1f)]
    [TestCase(true, 128, 255, 1f)]
    [TestCase(false, 255, 128, .5f)]
    [TestCase(true, 255, 128, .5f)]
    [TestCase(true, 128, 0, 1f)]
    [TestCase(true, 64, 255, .5f)]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void EmissivePass_UsesNativeLinearScaleAndOpacityWithoutDarkeningTheReceiver(bool premultiplied, int gray, int alpha, float particleAlpha)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(Source(premultiplied, particleAlpha), "vfx/decal.xml",
                _ => VfxMaterialCompositingTests.Texture((byte)gray, (byte)alpha)));
            Assert.That(preview.EmitterCount, Is.EqualTo(1));
            Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
            preview.Seek(.2f, Matrix.Identity);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            var camera = new Vector3(0, 4, 6);
            var parameters = new CommonShaderParameters(Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up),
                Matrix.CreateOrthographic(4, 4, .1f, 100), camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);
            var background = new Color(26, 51, 77, 255);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, background, 1, 0);
            preview.Draw(parameters); device.SetRenderTarget(null); var pixels = new Vector4[4096]; target.GetData(pixels);
            var texel = Linear(gray / 255f); var texAlpha = Linear(alpha / 255f);
            var vertexAlpha = MathF.Round(particleAlpha * 255) / 255;
            var opacity = Math.Clamp(texAlpha * vertexAlpha, 0, 1);
            var correction = premultiplied ? vertexAlpha : opacity;
            var covered = premultiplied ? Math.Min(texel, texAlpha) : texel;
            var emission = premultiplied ? Math.Clamp(texel - texAlpha, 0, 1) : 0;
            var emissiveness = Math.Clamp(emission * vertexAlpha, 0, 1);
            // Shipped ps_main_deferred_decal_emissive: radiance scale, premul correction and final opacity.
            var expected = 2500 * .004f * (covered * correction + emission * emissiveness) * opacity * correction;
            var centre = pixels[32 * 64 + 32]; var baseColour = background.ToVector4();
            Assert.Multiple(() =>
            {
                Assert.That(centre.X, Is.EqualTo(baseColour.X + expected).Within(Math.Max(.0001f, expected * .015f)));
                Assert.That(centre.Y, Is.EqualTo(baseColour.Y + expected).Within(Math.Max(.0001f, expected * .015f)));
                Assert.That(centre.Z, Is.EqualTo(baseColour.Z + expected).Within(Math.Max(.0001f, expected * .015f)));
            });
        }
        finally { graphics.Release(); }
    }

    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);

    private static byte[] Source(bool premultiplied, float alpha)
    {
        var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(new(.004f), premultiplied, 0, alpha: alpha));
        var effect = doc.Root.Element("vfx")!;
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
            effect.Element("emitters")!.Element("emitter")!.Add(new XElement("property", new XAttribute("name", name),
                new XAttribute("x", -90), new XAttribute("y", 0), new XAttribute("z", 0)));
        effect.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "material0"),
            new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", "DEFERRED_DECAL_EMISSIVE")))));
        return doc.Write();
    }
}
