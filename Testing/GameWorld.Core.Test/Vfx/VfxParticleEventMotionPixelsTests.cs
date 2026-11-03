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
public class VfxParticleEventMotionPixelsTests
{
    private const int Width = 192, Height = 96;

    [Test]
    public void NegativeReferenceChild_IsAlreadyVisibleAtParentBirthAndKeepsItsAuthoredTail()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen rendering is opt-in.");
        var parent = VfxDocument.Read(VfxParticleEventMotionTests.Parent(true, "WORLD_SPACE", true));
        var emitter = parent.Root.Descendants("emitter").Single();
        VfxParticleEventMotionTests.Set(emitter, "EmitterPosition", VfxParticleEventMotionTests.VectorCurve(Vector3.Zero));
        VfxParticleEventMotionTests.Set(emitter, "EmitterOrientation", VfxParticleEventMotionTests.VectorCurve(new(0, 0, -90)));
        VfxParticleEventMotionTests.Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 4));
        VfxParticleEventMotionTests.Set(emitter, "ParticleDeceleration", VfxLibraryVisibilityTests.Constant("x", 0));
        emitter.Add(new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", 90), new XAttribute("max", 90)));
        var child = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, false, 0));
        var childEmitter = child.Root.Descendants("emitter").Single();
        VfxParticleEventMotionTests.Set(childEmitter, "ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ONLY", true);
        VfxParticleEventMotionTests.Set(childEmitter, "EmitterPosition", VfxParticleEventMotionTests.VectorCurve(new(0, -2, 0)));
        VfxParticleEventMotionTests.Set(childEmitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 0));
        VfxParticleEventMotionTests.Set(childEmitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .25f));
        VfxParticleEventMotionTests.Set(childEmitter, "ParticleScale", VfxParticleEventMotionTests.VectorCurve(new(.5f)));
        VfxParticleEventMotionTests.Set(childEmitter, "MaximumParticles", "1", true);
        VfxParticleEventMotionTests.Set(childEmitter, "InfiniteLife", "false", true);
        foreach (var field in new[] { "StartTime", "LifeTime" })
        {
            childEmitter.Elements().Where(p => (string?)p.Attribute("name") == field).Remove();
            var value = field == "StartTime" ? .07 : .1;
            childEmitter.Add(new XElement("ranged_property", new XAttribute("name", field), new XAttribute("min", value), new XAttribute("max", value)));
        }
        var composition = VfxDocument.CreateComposition(); var reference = composition.AddReference("inner", child); reference.Apply();
        reference.Selection.SetAttributeValue("start_time_min", -.25); reference.Selection.SetAttributeValue("start_time_max", -.25);
        var parentBytes = parent.Write(); var childBytes = child.Write(); var eventBytes = composition.Write();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(new(parentBytes, "DIAG/event-negative-reference-pixels.xml", path => path.Replace('/', '\\') switch
            {
                @"vfx\child.xml" => eventBytes, @"vfx\inner.xml" => childBytes,
                _ => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                    ? VfxModelVariantPixelTests.Dds(path.EndsWith("texture-0.dds", StringComparison.OrdinalIgnoreCase) ? Color.Red : Color.Lime)
                    : VfxModelVariantTests.Resolve(path),
            }));
            var first = Render(graphics.GraphicsDevice, preview, 0);
            var later = Render(graphics.GraphicsDevice, preview, .04f);
            Assert.That(Centroid(first, green: true).X, Is.EqualTo(Width / 2.0).Within(.6));
            Assert.That(Centroid(later, green: true).X - Centroid(first, green: true).X, Is.EqualTo(4 * .04 * Width / 16).Within(.6));
            var expired = Render(graphics.GraphicsDevice, preview, .1f);
            Assert.That(expired.Count(c => c.G > 80 && c.G > c.R * 2 && c.G > c.B * 2), Is.EqualTo(0));
            Assert.That(Centroid(expired, green: false).X - Centroid(first, green: false).X, Is.EqualTo(4 * .1 * Width / 16).Within(.6));
        }
        finally { graphics.Release(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MovingRigidParent_ProducesTheNativeTrackedOrFrozenChildPixelDisplacement(bool track)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen rendering is opt-in.");
        var parent = VfxDocument.Read(VfxParticleEventMotionTests.Parent(true, "WORLD_SPACE", track));
        var emitter = parent.Root.Descendants("emitter").Single();
        VfxParticleEventMotionTests.Set(emitter, "EmitterPosition", VfxParticleEventMotionTests.VectorCurve(Vector3.Zero));
        VfxParticleEventMotionTests.Set(emitter, "EmitterOrientation", VfxParticleEventMotionTests.VectorCurve(new(0, 0, -90)));
        VfxParticleEventMotionTests.Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 4));
        VfxParticleEventMotionTests.Set(emitter, "ParticleDeceleration", VfxLibraryVisibilityTests.Constant("x", 0));
        emitter.Add(new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", 90), new XAttribute("max", 90)));
        var child = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, false, 0));
        var childEmitter = child.Root.Descendants("emitter").Single();
        VfxParticleEventMotionTests.Set(childEmitter, "ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ONLY", true);
        VfxParticleEventMotionTests.Set(childEmitter, "EmitterPosition", VfxParticleEventMotionTests.VectorCurve(new(0, -2, 0)));
        VfxParticleEventMotionTests.Set(childEmitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 0));
        VfxParticleEventMotionTests.Set(childEmitter, "ParticleScale", VfxParticleEventMotionTests.VectorCurve(new(.5f)));
        var parentBytes = parent.Write(); var childBytes = child.Write();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(parentBytes, "DIAG/event-motion-pixels.xml", path =>
                path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? childBytes
                : path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                    ? VfxModelVariantPixelTests.Dds(path.EndsWith("texture-0.dds", StringComparison.OrdinalIgnoreCase) ? Color.Red : Color.Lime)
                    : VfxModelVariantTests.Resolve(path)));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey)));
            var first = Render(device, preview, .125f);
            var second = Render(device, preview, .5f);
            var redFirst = Centroid(first, green: false); var redSecond = Centroid(second, green: false);
            var greenFirst = Centroid(first, green: true); var greenSecond = Centroid(second, green: true);
            const double pixelsPerWorldUnit = Width / 16.0;
            const double parentMovement = 4 * (.5 - .125) * pixelsPerWorldUnit;
            Assert.Multiple(() =>
            {
                Assert.That(redSecond.X - redFirst.X, Is.EqualTo(parentMovement).Within(.6),
                    "Independent constant velocity4, zero damping and orthographic projection predict18 pixels of parent movement.");
                Assert.That(greenSecond.X - greenFirst.X, Is.EqualTo(track ? parentMovement : 0).Within(.6),
                    "The green child must follow the actual moving red parent only with native position tracking enabled.");
                Assert.That(greenFirst.X, Is.EqualTo(Width / 2.0 + (track ? 4 * .125 * pixelsPerWorldUnit : 0)).Within(.6));
                Assert.That(greenSecond.X, Is.EqualTo(Width / 2.0 + (track ? 4 * .5 * pixelsPerWorldUnit : 0)).Within(.6));
                Assert.That(greenSecond.Y - greenFirst.Y, Is.EqualTo(0).Within(.6));
            });
            Assert.That(VfxDocument.Read(parentBytes).Write(), Is.EqualTo(parentBytes));
            Assert.That(VfxDocument.Read(childBytes).Write(), Is.EqualTo(childBytes));
            TestContext.Out.WriteLine($"DIAG separate red model/green child, speed4, damping0, unchanged1/120 tick. track={track}; parent pixel delta={redSecond.X - redFirst.X}; child delta={greenSecond.X - greenFirst.X}. No complete official-effect visual claim.");
        }
        finally { graphics.Release(); }
    }

    private static Color[] Render(GraphicsDevice device, EffekseerVfxPreview preview, float time)
    {
        using var target = new RenderTarget2D(device, Width, Height, false, SurfaceFormat.Color, DepthFormat.Depth24);
        preview.Seek(time, Matrix.Identity);
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        device.DepthStencilState = DepthStencilState.Default;
        device.BlendState = BlendState.Opaque;
        device.RasterizerState = RasterizerState.CullNone;
        var eye = new Vector3(0, 0, 10);
        preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
            Matrix.CreateOrthographic(16, 8, .1f, 100), eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, [],
            ViewportWidth: Width, ViewportHeight: Height));
        device.SetRenderTarget(null);
        var pixels = new Color[Width * Height]; target.GetData(pixels);
        return pixels;
    }

    private static (double X, double Y) Centroid(Color[] pixels, bool green)
    {
        var points = pixels.Select((colour, index) => (colour, index)).Where(p => green
            ? p.colour.G > 80 && p.colour.G > p.colour.R * 2 && p.colour.G > p.colour.B * 2
            : p.colour.R > 80 && p.colour.R > p.colour.G * 2 && p.colour.R > p.colour.B * 2).ToArray();
        Assert.That(points.Length, Is.GreaterThan(12), green ? "Missing green child control." : "Missing red visible-model parent control.");
        return (points.Average(p => p.index % Width + .5), points.Average(p => p.index / Width + .5));
    }
}
