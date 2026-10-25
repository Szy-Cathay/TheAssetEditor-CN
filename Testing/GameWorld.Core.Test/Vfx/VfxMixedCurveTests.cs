#nullable enable
using System.IO;
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
public class VfxMixedCurveTests
{
    public static IEnumerable<TestCaseData> NativeCases()
    {
        var assembly = typeof(VfxMixedCurveTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("NativeMixedCurves.json", StringComparison.Ordinal)))!;
        using var data = JsonDocument.Parse(stream);
        foreach (var item in data.RootElement.GetProperty("Cases").EnumerateArray())
            yield return new TestCaseData(item.Clone()).SetName("NativeMixedCurve_" + item.GetProperty("Name").GetString());
    }

    [TestCaseSource(nameof(NativeCases))]
    public void MixedEndpoints_MatchIndependentDescendingNativeCohorts(JsonElement item)
    {
        var curve = new VfxPreviewCurve(item.GetProperty("Curve").GetString(), "ParticleScale", 1);
        foreach (var sample in item.GetProperty("Samples").EnumerateArray())
        {
            var phase = sample.GetProperty("Phase").GetDouble();
            var phaseError = sample.GetProperty("PhaseUncertainty").GetDouble();
            var values = Enumerable.Range(0, 9).Select(index => curve.Sample("x", phase - phaseError + index * phaseError / 4, .5f)).ToArray();
            var observed = sample.GetProperty("Scale").GetDouble();
            var pixelError = sample.GetProperty("PixelScaleUncertainty").GetDouble();
            Assert.Multiple(() =>
            {
                Assert.That(values.Min(), Is.LessThanOrEqualTo(observed + pixelError), $"Frame {sample.GetProperty("Frame")}");
                Assert.That(values.Max(), Is.GreaterThanOrEqualTo(observed - pixelError), $"Frame {sample.GetProperty("Frame")}");
            });
        }
    }

    [TestCase("curve_alpha")]
    [TestCase("ParticleScale")]
    public void MixedCurve_DoesNotDiscardItsLayerOrRewriteAuthoredData(string field)
    {
        var doc = Source(field);
        var bytes = doc.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/mixed.xml", _ => throw new InvalidOperationException("No texture was authored.")));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.InterpolationUnsupported"), Is.False);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void MixedAlpha_ProducesPixelsThroughTheActualRuntime()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var bytes = Source("curve_alpha").Write();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(bytes, "vfx/mixed-alpha.xml", _ => null));
            Assert.That(preview.EmitterCount, Is.EqualTo(1));
            preview.Seek(.4f, Matrix.Identity);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
            device.SetRenderTarget(target);
            device.Clear(Color.Black);
            preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
                Matrix.CreateOrthographic(10, 10, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
            device.SetRenderTarget(null);
            var pixels = new Color[64 * 64];
            target.GetData(pixels);
            Assert.That(pixels.Count(p => p.R > 10 && p.B > 10), Is.GreaterThan(100));
        }
        finally { graphics.Release(); }
    }

    private static VfxDocument Source(string field)
    {
        var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(new(1, 0, 1), false, 0));
        doc.Root.Descendants("modifier").Where(x => (string?)x.Attribute("fragment") is "ps_texture" or "ps_lighting").Remove();
        var axes = field == "curve_alpha" ? new[] { "alpha" } : new[] { "x", "y", "z" };
        var text = string.Join('|', axes.SelectMany(axis => new[] { "min", "max" }.Select(bound =>
            $"{axis}_{bound},8405056,0;0,0.2,linear,bezier_c,-0.2,0,0.2,0,1;1,1,sine,linear,-0.2,0,0.2,0,1")))
            + ":" + string.Join('|', axes.Select((axis, index) => $"{axis},1;{index * 2},{index * 2 + 1}"));
        var existing = doc.Root.Descendants().FirstOrDefault(e => (string?)e.Attribute("name") == field);
        if (existing != null) existing.Value = text;
        else doc.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", field), new XAttribute("quality", "0"), text));
        return doc;
    }
}
