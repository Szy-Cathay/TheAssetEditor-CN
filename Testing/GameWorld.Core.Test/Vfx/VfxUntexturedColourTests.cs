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
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black);
            preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up), Matrix.CreateOrthographic(10, 10, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
            device.SetRenderTarget(null); var pixels = new Color[64 * 64]; target.GetData(pixels);
            Assert.That(pixels.Count(p => p.R >= 250 && p.B >= 250 && p.G <= 2), Is.GreaterThan(500));
        }
        finally { graphics.Release(); }
    }
}
