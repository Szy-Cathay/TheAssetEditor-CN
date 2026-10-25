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
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxProjectionHandednessTests
{
    [TestCase(0, "untouched")]
    [TestCase(0, "90_degrees_cw")]
    [TestCase(0, "90_degrees_ccw")]
    [TestCase(1, "untouched")]
    [TestCase(1, "90_degrees_cw")]
    [TestCase(1, "90_degrees_ccw")]
    public void ReflectedProjection_PreservesTheBillboardTextureRotationAndOffsetPivot(int restriction, string uvRotation)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var document = VfxDocument.Read(VfxTranslationOrientationTests.Source(restriction, new(0, 0, 30)));
        var quality = document.Root.Descendants("quality").Single();
        quality.Add(new XElement("modifier", new XAttribute("fragment", "vs_flip_uv"),
            new XElement("property", new XAttribute("name", "flip_u"), new XAttribute("value", 1)),
            new XElement("property", new XAttribute("name", "flip_v"), new XAttribute("value", 0)),
            new XElement("property", new XAttribute("name", "uv_orientation"), new XAttribute("value", uvRotation))));
        var translation = quality.Elements("modifier").Single(x => (string?)x.Attribute("fragment") == "vs_translation");
        translation.Add(new XElement("property", new XAttribute("name", "pivot_x"), new XAttribute("value", .2f)),
            new XElement("property", new XAttribute("name", "pivot_y"), new XAttribute("value", .7f)));
        var bytes = document.Write(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            var assembly = typeof(VfxProjectionHandednessTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("NativeUvAsymmetric.dds", StringComparison.Ordinal)))!;
            using var memory = new MemoryStream(); stream.CopyTo(memory); var texture = memory.ToArray();
            preview.Reload(new(bytes, "vfx/projection.xml", _ => texture)); Assert.That(preview.EmitterCount, Is.EqualTo(1));
            using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var camera = new Vector3(0, 0, 10); var view = Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up);
            var projection = Matrix.CreateOrthographic(12, 12, .1f, 100);
            var images = new List<Color[]>();
            foreach (var reflected in new[] { false, true })
            {
                preview.Seek(.2f, Matrix.Identity);
                var parameters = new CommonShaderParameters(view, projection * Matrix.CreateScale(reflected ? -1 : 1, 1, 1),
                    camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
                device.SetRenderTarget(target); device.Clear(Color.Black); preview.Draw(parameters); device.SetRenderTarget(null);
                var pixels = new Color[128 * 128]; target.GetData(pixels); images.Add(pixels);
            }
            Assert.That(images[0].Count(p => p.R > 20 || p.G > 20 || p.B > 20), Is.GreaterThan(100));
            Assert.That(images[0].Zip(images[1]).Max(p => Math.Max(Math.Abs(p.First.R - p.Second.R),
                Math.Max(Math.Abs(p.First.G - p.Second.G), Math.Abs(p.First.B - p.Second.B)))), Is.LessThanOrEqualTo(1));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        }
        finally { graphics.Release(); }
    }
}
