using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxModelAnimationPixelTests
{
    [TestCase(1, 4)]
    [TestCase(1, 2)]
    [TestCase(2, 4)]
    public void ActualRenderer_UsesParticlePhaseAndAnimationSpeed(float speed, float life)
        => WithPreview((device, preview) =>
        {
            Load(preview, VfxModelAnimationTests.Source(speed, life));
            var first = Frame(device, preview, 0);
            var quarter = Frame(device, preview, life / speed * .25f);
            var half = Frame(device, preview, life / speed * .5f);
            Assert.That(Centre(quarter) - Centre(first), Is.EqualTo(20).Within(1));
            Assert.That(Centre(half) - Centre(first), Is.EqualTo(40).Within(1));
            Assert.That(Frame(device, preview, life / speed * .25f), Is.EqualTo(quarter), "Backward seek must restore the same baked pose.");
        });

    [Test]
    public void ZeroSpeed_KeepsTheFirstSkinnedPose()
        => WithPreview((device, preview) =>
        {
            Load(preview, VfxModelAnimationTests.Source(0));
            Assert.That(Frame(device, preview, 2), Is.EqualTo(Frame(device, preview, 0)));
        });

    [Test]
    public void DelayedBirth_DoesNotStartTheModelAtGlobalPlaybackTime()
        => WithPreview((device, preview) =>
        {
            Load(preview, VfxModelAnimationTests.Source());
            var normal = Frame(device, preview, 1);
            var document = VfxDocument.Read(VfxModelAnimationTests.Source());
            document.Root.Descendants("emitter").Single().Add(new System.Xml.Linq.XElement("ranged_property",
                new System.Xml.Linq.XAttribute("name", "StartTime"), new System.Xml.Linq.XAttribute("min", 1), new System.Xml.Linq.XAttribute("max", 1)));
            Load(preview, document.Write());
            Assert.That(Frame(device, preview, 2), Is.EqualTo(normal));
        });

    [Test]
    public void LoopingTwice_ReturnsToTheFirstPoseMidwayThroughParticleLife()
        => WithPreview((device, preview) =>
        {
            Load(preview, VfxModelAnimationTests.Source(2));
            Assert.That(Frame(device, preview, 2), Is.EqualTo(Frame(device, preview, 0)));
        });

    private static void WithPreview(Action<GraphicsDevice, EffekseerVfxPreview> test)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try { using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice); test(graphics.GraphicsDevice, preview); }
        finally { graphics.Release(); }
    }

    private static void Load(EffekseerVfxPreview preview, byte[] source)
    {
        preview.Reload(new(source, "vfx/animated.xml", VfxModelAnimationTests.Resolve));
        Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey + "/" + d.Field)));
    }

    private static Color[] Frame(GraphicsDevice device, EffekseerVfxPreview preview, float time)
    {
        preview.Seek(time, Matrix.Identity);
        using var target = new RenderTarget2D(device, 240, 120, false, SurfaceFormat.Color, DepthFormat.Depth24);
        var position = new Vector3(3, 1.5f, 10);
        var camera = new CommonShaderParameters(Matrix.CreateLookAt(position, new(3, 1.5f, 0), Vector3.Up),
            Matrix.CreateOrthographic(6, 3, .1f, 100), position, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        preview.Draw(camera); device.SetRenderTarget(null);
        var pixels = new Color[240 * 120]; target.GetData(pixels);
        return pixels;
    }

    private static double Centre(Color[] pixels)
    {
        var visible = Enumerable.Range(0, pixels.Length).Where(i => pixels[i].R > 100 && pixels[i].G > 100 && pixels[i].B > 100).ToArray();
        Assert.That(visible.Length, Is.GreaterThan(100), "The animated model must produce an actual visible triangle.");
        return visible.Average(i => i % 240);
    }
}
