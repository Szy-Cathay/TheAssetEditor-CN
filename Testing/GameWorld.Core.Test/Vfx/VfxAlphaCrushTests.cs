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
public class VfxAlphaCrushTests
{
    [TestCase(.1f, false)]
    [TestCase(.2f, false)]
    [TestCase(.2f, true)]
    public void PremultipliedCoverage_AppliesNativeVisibilityCrush(float progress, bool sharp)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Requires opt-in DX11 pixels.");
        var source = Source(progress, sharp);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var actual = Render(graphics.GraphicsDevice, source, VfxMaterialCompositingTests.Texture(128, 128));
            var alpha = MathF.Pow((128f / 255 + .055f) / 1.055f, 2.4f);
            var crushed = Math.Clamp(alpha - progress, 0, 1) / (Math.Clamp(1 - progress, 0, 1) + .001f);
            if (sharp && crushed < progress) crushed = 0;
            Assert.That(actual.X, Is.EqualTo(crushed).Within(.004f),
                "Native ps_alpha_crush changes the covered shape after texture alpha, before soft fading.");
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void EmissionCrush_PrecedesSoftDepthFade()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Requires opt-in DX11 pixels.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, [128, 128, 128, 0], 4, 1, 1, 1));
            var layer = EffekseerLayer.Create(); layer.MaterialMode = 2; layer.Blend = 2;
            layer.SoftDistance = 2; layer.EmissionShapeWeight = 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
            var particle = new EffekseerParticle { Transform = Matrix.CreateScale(2), Colour = Vector4.One, UV = new(0, 0, 1, 1), AlphaCrush = new(.1f, 1) };
            var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
            var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 1, .01f, 25000);
            var contact = Vector4.Transform(new Vector4(0, 0, -10.5f, 1), projection);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, contact.Z / contact.W, 0);
            device.DepthStencilState = DepthStencilState.Default;
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(view), Values(projection), [particle], 1));
            device.SetRenderTarget(null);
            var pixels = new Vector4[4096]; target.GetData(pixels);
            var emission = MathF.Pow((128f / 255 + .055f) / 1.055f, 2.4f);
            var visibility = (emission - .1f) / .901f;
            Assert.That(pixels[32 * 64 + 32].X, Is.EqualTo(emission * visibility * .25f * .25f).Within(.00015f),
                "Crush uses the original emissiveness, then soft depth fades both emission and its weight.");
        }
        finally { graphics.Release(); }
    }

    private static Vector4 Render(GraphicsDevice device, byte[] source, byte[] texture)
    {
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(new(source, "vfx/crush.xml", _ => texture));
        Assert.That(preview.EmitterCount, Is.EqualTo(1));
        preview.Seek(3, Matrix.Identity);
        Assert.That(preview.ParticleCount, Is.EqualTo(1));
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
        var eye = new Vector3(0, 0, 10);
        preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
            Matrix.CreateOrthographic(20, 20, .1f, 50), eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
        device.SetRenderTarget(null);
        var pixels = new Vector4[64 * 64]; target.GetData(pixels);
        return pixels[32 * 64 + 32];
    }

    internal static byte[] Source(float progress, bool sharp)
    {
        var document = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, true, 0));
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "ps_alpha_crush"),
            new XElement("property", new XAttribute("name", "sharp_transition"), new XAttribute("value", sharp)),
            new XElement("property", new XAttribute("name", "curve_alpha_crush"), VfxLibraryVisibilityTests.Constant("progress", progress))));
        return document.Write();
    }

    [TestCase(.1f, false)]
    [TestCase(.2f, true)]
    public void EmissionOnlyTexture_CrushesItsOwnVisibility(float progress, bool sharp)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Requires opt-in DX11 pixels.");
        var document = VfxDocument.Read(Source(progress, sharp));
        document.Root.Descendants("property").Single(p => (string)p.Attribute("name") == "additive_blend_mul").SetAttributeValue("value", 1);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var actual = Render(graphics.GraphicsDevice, document.Write(), VfxMaterialCompositingTests.Texture(128, 0));
            var emission = MathF.Pow((128f / 255 + .055f) / 1.055f, 2.4f);
            var visibility = Math.Max(emission - progress, 0) / (1 - progress + .001f);
            if (sharp && visibility < progress) visibility = 0;
            Assert.That(actual.X, Is.EqualTo(emission * visibility).Within(.002f));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void Curve_UsesParticleAgeAndKeepsSeekingDeterministic()
    {
        var document = VfxDocument.Read(Source(0, true));
        document.Root.Descendants("property").Single(p => (string)p.Attribute("name") == "curve_alpha_crush").Value
            = "progress,0,1;0,0,linear,linear,0,0,0,0,1;1,1,linear,linear,0,0,0,0,1:progress,0;0,0";
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "vfx/crush.xml", _ => [1]));
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        var direct = new TotalWarVfxSimulation(conversion); direct.Seek(3, Matrix.Identity);
        var frame = direct.Frame(camera);
        Assert.That(frame, Has.Length.EqualTo(1));
        Assert.That(frame[0].AlphaCrush.X, Is.EqualTo(.5f).Within(.005f));
        Assert.That(frame[0].AlphaCrush.Y, Is.EqualTo(2));
        direct.Seek(2.5f, Matrix.Identity); direct.Advance(.5f, Matrix.Identity);
        Assert.That(direct.Frame(camera), Is.EqualTo(frame));
    }

    [Test]
    public void SameLayer_KeepsEachParticlesCrushThreshold()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Requires opt-in DX11 pixels.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, [128, 128, 128, 128], 4, 1, 1, 1));
            var layer = EffekseerLayer.Create(); layer.MaterialMode = 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
            var particles = new[] { .05f, .2f }.Select((progress, index) => new EffekseerParticle
            {
                Transform = Matrix.CreateTranslation(index * 2 - 1, 0, 0), Colour = Vector4.One, UV = new(0, 0, 1, 1),
                AlphaCrush = new(progress, 1),
            }).ToArray();
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,
                Values(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up)), Values(Matrix.CreateOrthographic(4, 4, .1f, 50)), particles, particles.Length));
            device.SetRenderTarget(null);
            var pixels = new Vector4[4096]; target.GetData(pixels);
            var alpha = MathF.Pow((128f / 255 + .055f) / 1.055f, 2.4f);
            Assert.That(pixels[32 * 64 + 16].X, Is.EqualTo((alpha - .05f) / .951f).Within(.003f));
            Assert.That(pixels[32 * 64 + 48].X, Is.EqualTo((alpha - .2f) / .801f).Within(.003f));
        }
        finally { graphics.Release(); }
    }

    private static float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
}
