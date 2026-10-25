#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxDefaultTextureEmissionTests
{
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void MissingLightingStage_PreservesTextureEmissionWithoutTurningCoverageAdditive(bool premultiplied, float emission)
    {
        var source = Source(premultiplied, null);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/default-emission.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].AdditiveMultiplier, Is.EqualTo(emission));
        if (!premultiplied) Assert.That(conversion.Emitters[0].Render.Blend, Is.EqualTo(1));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void ExplicitLightingMultiplier_RetainsItsAuthoredValue(float emission)
    {
        var conversion = TotalWarVfxConverter.Convert(new(Source(true, emission), "vfx/explicit-emission.xml", _ => [1]));
        Assert.That(conversion.Emitters.Single().AdditiveMultiplier, Is.EqualTo(emission));
    }

    [TestCase(128)]
    [TestCase(255)]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void ZeroAlphaTexture_DefaultEmissionMatchesExplicitOneAndExplicitZeroStaysOff(int gray)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            var camera = new Vector3(0, 0, 10);
            var parameters = new CommonShaderParameters(Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up),
                Matrix.CreateOrthographic(6, 6, .1f, 100), camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            var pixels = new List<Vector4>();
            foreach (var multiplier in new float?[] { null, 0, 1 })
            {
                preview.Reload(new(Source(true, multiplier), "vfx/emission.xml", _ => VfxMaterialCompositingTests.Texture((byte)gray, 0)));
                Assert.That(preview.EmitterCount, Is.EqualTo(1)); preview.Seek(.2f, Matrix.Identity);
                device.SetRenderTarget(target); device.Clear(Color.Black); preview.Draw(parameters); device.SetRenderTarget(null);
                var image = new Vector4[4096]; target.GetData(image); pixels.Add(image[32 * 64 + 32]);
            }
            Assert.Multiple(() =>
            {
                var value = gray / 255f;
                var linear = value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
                Assert.That(pixels[0].X, Is.EqualTo(linear * linear).Within(.001f),
                    "Native default emission retains the texture-luminance shape, including with no lighting modifier.");
                Assert.That(pixels[0].X, Is.EqualTo(pixels[2].X).Within(.00001f));
                Assert.That(pixels[1].X, Is.Zero.Within(.00001f));
            });
        }
        finally { graphics.Release(); }
    }

    private static byte[] Source(bool premultiplied, float? emission)
    {
        var document = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, premultiplied, emission ?? 1));
        if (emission == null) document.Root.Descendants("modifier").Where(x => (string?)x.Attribute("fragment") == "ps_lighting").Remove();
        return document.Write();
    }
}
