#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;
using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxUntexturedColourTests
{
    [Test]
    public void AbsentTextureStage_RetainsTheNativeVertexColourWithoutResolvingAFakeResource()
    {
        var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(new(1, 0, 1), false, 0));
        doc.Root.Descendants("modifier").Where(x => (string?)x.Attribute("fragment") == "ps_texture").Remove();
        var source = doc.Write();
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/solid.xml", _ => throw new InvalidOperationException("No file was authored.")));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.False);
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void ExplicitTextureWithMissingFile_RemainsDiagnosed()
    {
        var source = VfxMaterialCompositingTests.Source(Vector3.One, false, 0);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/missing.xml", _ => null));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource" && d.Field.Replace('\\', '/') == "vfx/material.dds"), Is.True);
    }

    [Test]
    public void CustomPixelShaderWithoutTextureStage_DoesNotBecomeASolidRectangle()
    {
        var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, false, 0));
        doc.Root.Descendants("modifier").Where(x => (string?)x.Attribute("fragment") == "ps_texture").Remove();
        doc.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "ps_custom_shader")));
        var conversion = TotalWarVfxConverter.Convert(new(doc.Write(), "vfx/custom.xml", _ => null));
        Assert.That(conversion.Emitters, Is.Empty);
    }

    [Test]
    public void NativeMagentaControl_ProducesRealPixelsWithoutAResourceFile()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(new(1, 0, 1), false, 0));
        doc.Root.Descendants("modifier").Where(x => (string?)x.Attribute("fragment") is "ps_texture" or "ps_lighting").Remove();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(doc.Write(), "vfx/solid.xml", _ => throw new InvalidOperationException("No resource was authored.")));
            Assert.That(preview.EmitterCount, Is.EqualTo(1)); preview.Seek(.5f, Matrix.Identity);
            var parameters = new CommonShaderParameters(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
                Matrix.CreateOrthographic(10, 10, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            using var linearTarget = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(linearTarget); device.Clear(Color.Black); preview.Draw(parameters);
            device.SetRenderTarget(null); var linearPixels = new Vector4[64 * 64]; linearTarget.GetData(linearPixels);
            Assert.That(linearPixels.Count(pixel => pixel.X >= 250f / 255 && pixel.Z >= 250f / 255
                && pixel.Y >= 0 && pixel.Y <= 2f / 255), Is.GreaterThan(500),
                "The original near-unit magenta requirement applies to linear particle colour before display conversion.");
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black);
            preview.Draw(parameters);
            device.SetRenderTarget(null); var pixels = new Color[64 * 64]; target.GetData(pixels);
            var displayed = pixels.Where(pixel => pixel.R != 0 || pixel.G != 0 || pixel.B != 0).ToArray();
            // Native exposure 1, Burn .05, gamma and default LUT map linear magenta to 232.9885, rounded to 233.
            Assert.Multiple(() =>
            {
                Assert.That(displayed, Has.Length.GreaterThan(500));
                Assert.That(displayed.All(pixel => pixel.R == pixel.B && pixel.G <= 2 && Math.Abs(pixel.R - 233) <= 1), Is.True,
                    "The 8-bit target must apply the independent native display transfer while retaining equal red/blue and no green.");
            });
        }
        finally { graphics.Release(); }
    }
}
