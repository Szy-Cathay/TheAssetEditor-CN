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
public class VfxParticleCapacityTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [Test]
    public void HighCountEarlyEmitter_DoesNotSuppressTheTwoFiniteLateLayers()
    {
        // R47 Terry pack 6aef8ba6cb6502e88d9a368a12d7f6a4157797c149de392c9c9ab88839d161aa.
        // Fixed zero direction/depth/motion: 266 frames, both late red and blue layers visible.
        var plan = Composition((12000, 4000, 0, 3), (1, 1, 2.5f, .01f), (1, 1, 3.5f, .01f));
        var simulation = new TotalWarVfxSimulation(plan);
        simulation.Seek(3.75f, Matrix.Identity);
        var frame = simulation.Frame(Camera);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters[0].MaximumParticles, Is.EqualTo(12000));
            Assert.That(frame.Count(particle => particle.Layer == 0), Is.EqualTo(12000));
            Assert.That(frame.Count(particle => particle.Layer == 1), Is.EqualTo(1));
            Assert.That(frame.Count(particle => particle.Layer == 2), Is.EqualTo(1));
            Assert.That(simulation.CapacityReached, Is.False);
        });
    }

    [Test]
    public void SeveralOrdinaryEmitters_KeepTheirIndependentAuthoredLimits()
    {
        var plan = Composition((6000, 6000, 0, 1), (6000, 6000, 0, 1),
            (6000, 6000, 0, 1), (1, 1, 1.25f, .01f));
        var simulation = new TotalWarVfxSimulation(plan);
        simulation.Seek(1.5f, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.ParticleCount, Is.EqualTo(18001));
            Assert.That(simulation.Frame(Camera).Count(particle => particle.Layer == 3), Is.EqualTo(1));
            Assert.That(simulation.CapacityReached, Is.False);
        });
    }

    [Test]
    public void ExcessiveComposition_RemainsBoundedAndReportsThePreviewLimit()
    {
        var plan = Composition(Enumerable.Repeat((8192, 10000, 0f, 1f), 9).ToArray());
        var simulation = new TotalWarVfxSimulation(plan);
        simulation.Seek(1.1f, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.ParticleCount, Is.EqualTo(65536));
            Assert.That(simulation.CapacityReached, Is.True);
            Assert.That(simulation.Frame(Camera).GroupBy(particle => particle.Layer).All(group => group.Count() <= 8192), Is.True);
        });
    }

    [Test]
    public void SourceAboveSafetyLimit_IsDiagnosedWithoutEditingTheDocument()
    {
        var source = Source(100000, 1, 1); var bytes = source.Write();
        var plan = TotalWarVfxConverter.Convert(new(bytes, "vfx/capacity.xml", _ => [1]));
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters.Single().MaximumParticles, Is.EqualTo(65536));
            Assert.That(plan.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.CapacityLimited"), Is.True);
            Assert.That(source.Write(), Is.EqualTo(bytes));
        });
    }

    [TestCase(8193)]
    [TestCase(16385)]
    public void NativeSpriteBatch_RendersTheTailBeyondTheVertexBufferLimit(int count)
    {
        var particles = Enumerable.Repeat(Particle(new(100, 0, 0), Vector4.One), count).ToArray();
        particles[^1] = Particle(Vector3.Zero, new(1, 0, 0, 1));
        var colour = Render(particles);
        Assert.That(colour.X, Is.GreaterThan(.9f), "The final sprite must not be silently discarded by Effekseer's 8192-square buffer.");
    }

    [Test]
    public void NativeSpriteBatch_PreservesTransparentOrderAcrossTheBufferBoundary()
    {
        var red = Particle(Vector3.Zero, new(1, 0, 0, .5f));
        var blue = Particle(Vector3.Zero, new(0, 0, 1, .5f));
        var expected = Render([red, blue]);
        var particles = Enumerable.Repeat(Particle(new(100, 0, 0), Vector4.One), 8193).ToArray();
        particles[^2] = red; particles[^1] = blue;
        Assert.That(Vector4.Distance(Render(particles), expected), Is.LessThan(.01f));
    }

    private static VfxConversion Composition(params (int Max, int Rate, float Start, float Duration)[] definitions)
    {
        var root = VfxDocument.CreateComposition(); var sources = new Dictionary<string, byte[]>();
        for (var index = 0; index < definitions.Length; index++)
        {
            var item = definitions[index]; var source = Source(item.Max, item.Rate, item.Duration);
            var path = $"vfx/capacity_{index}.xml"; sources[path] = source.Write();
            var edit = root.AddReference(path, source); edit.Apply();
            edit.Selection.SetAttributeValue("start_time_min", item.Start); edit.Selection.SetAttributeValue("start_time_max", item.Start);
        }
        var plan = TotalWarVfxConverter.Convert(new(root.Write(), "vfx/capacity.xml", path => sources.GetValueOrDefault(path.Replace('\\', '/')) ?? [1]));
        Assert.That(plan.Emitters, Has.Count.EqualTo(definitions.Length)); return plan;
    }

    private static VfxDocument Source(int maximum, int rate, float duration)
    {
        var doc = VfxDocument.Read(VfxLibraryVisibilityTests.Source("")); var emitter = doc.Root.Descendants("emitter").Single();
        emitter.Elements().Single(item => item.Attribute("name")?.Value == "InfiniteLife").SetAttributeValue("value", "false");
        emitter.Elements().Single(item => item.Attribute("name")?.Value == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", rate);
        emitter.Elements().Single(item => item.Attribute("name")?.Value == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 8);
        emitter.Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", maximum)),
            new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", duration), new XAttribute("max", duration)));
        return doc;
    }

    private static EffekseerParticle Particle(Vector3 position, Vector4 colour) => new()
    {
        Transform = Matrix.CreateScale(.5f) * Matrix.CreateTranslation(position), Colour = colour, UV = new(0, 0, 1, 1),
    };

    private static Vector4 Render(EffekseerParticle[] particles)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Requires opt-in DX11 pixels.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, [255, 255, 255, 255], 4, 1, 1, 0));
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [EffekseerLayer.Create()], 1));
            using var target = new RenderTarget2D(device, 16, 16, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,
                Values(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up)), Values(Matrix.CreateOrthographic(4, 4, .1f, 50)), particles, particles.Length));
            device.SetRenderTarget(null); var pixels = new Vector4[256]; target.GetData(pixels); return pixels[8 * 16 + 8];
        }
        finally { graphics.Release(); }
    }

    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
