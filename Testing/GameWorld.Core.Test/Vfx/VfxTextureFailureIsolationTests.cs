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
public class VfxTextureFailureIsolationTests
{
    [Test]
    public void UndecodableTexture_DisablesOnlyItsLayerAndCanRecoverOnReload()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var effect = document.Root.Element("vfx")!;
        var emitter = new XElement(effect.Element("emitters")!.Element("emitter")!); emitter.SetAttributeValue("id", "bad");
        effect.Element("emitters")!.Add(emitter);
        var chain = new XElement(effect.Element("behaviour_chains")!.Element("behaviour_chain")!); chain.SetAttributeValue("id", "bad");
        chain.Descendants("texture_property").Single().SetAttributeValue("path", "vfx/broken.dds"); effect.Element("behaviour_chains")!.Add(chain);
        var bytes = document.Write(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            var snapshot = new VfxPreviewSnapshot(bytes, "vfx/isolated.xml", path => path.EndsWith("broken.dds") ? [1,2,3] : VfxModelVariantPixelTests.Dds(Color.Red));
            preview.Reload(snapshot);
            Assert.That(preview.EmitterCount, Is.EqualTo(1));
            Assert.That(preview.TotalEmitterCount, Is.EqualTo(2));
            Assert.That(preview.Diagnostics.Any(d => d.Layer == "bad" && d.ReasonKey == "Vfx.Preview.LayerTextureUnavailable"), Is.True);
            Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
            preview.Seek(.2f, Matrix.Identity);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black);
            var cameraPosition = new Vector3(0,0,10);
            preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(cameraPosition,Vector3.Zero,Vector3.Up),
                Matrix.CreateOrthographic(2,2,.1f,100),cameraPosition,Vector3.Zero,0,0,0,1,Vector3.One,[]));
            device.SetRenderTarget(null); var pixels = new Color[4096]; target.GetData(pixels);
            Assert.That(pixels.Count(pixel => pixel.R > 100 && pixel.G < 30), Is.GreaterThan(100));
            preview.Reload(snapshot with { Resolve = _ => VfxModelVariantPixelTests.Dds(Color.Lime) });
            Assert.That(preview.EmitterCount, Is.EqualTo(2));
            Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.LayerTextureUnavailable"), Is.False);
            Assert.That(document.Write(), Is.EqualTo(bytes));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void InvalidTextureInAnyModelChoice_DisablesThatWholeEmitterWithoutReportingTheRuntimeMissing()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(new(VfxModelVariantTests.Source(2), "vfx/variants.xml", path =>
                path.EndsWith("texture-1.dds") ? [1,2,3] : path.EndsWith(".dds") ? VfxModelVariantPixelTests.Dds(Color.Red) : VfxModelVariantTests.Resolve(path)));
            Assert.That(preview.EmitterCount, Is.EqualTo(0));
            Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.LayerTextureUnavailable"), Is.True);
            Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
        }
        finally { graphics.Release(); }
    }
}
