#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxParticleSpinTests
{
    [TestCase("CW", -45)]
    [TestCase("CCW", 45)]
    public void NativeNinetyDegreeControl_RotatesByAuthoredSeconds(string direction, float expectedDegrees)
    {
        var source = Source(3, new(90, 0, 0), new(0, 0, 90), direction);
        var actual = Frame(source, .5f);
        var expected = Matrix.CreateRotationZ(MathHelper.ToRadians(expectedDegrees)) * Matrix.CreateRotationX(MathHelper.PiOver2);
        Assert.That(Vector3.Distance(Vector3.Normalize(actual.Up), expected.Up), Is.LessThan(.00001f));
        Assert.That(Vector3.Normalize(actual.Backward).Y, Is.EqualTo(-1).Within(.00001f), "Spin must stay in the native horizontal slash plane.");
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void RealSlashSpin_TravelsOneHundredSixtyDegreesDuringItsPointTwoSecondLife()
    {
        var source = Source(3, new(90, 0, 0), new(0, 0, 800), "CW");
        var early = Frame(source, .02f); var late = Frame(source, .19f);
        var expected = Matrix.CreateRotationZ(MathHelper.ToRadians(-800 * .19f)) * Matrix.CreateRotationX(MathHelper.PiOver2);
        Assert.That(Vector3.Distance(Vector3.Normalize(late.Up), expected.Up), Is.LessThan(.00001f));
        Assert.That(Vector3.Dot(Vector3.Normalize(early.Up), Vector3.Normalize(late.Up)), Is.LessThan(-.5f));
    }

    [Test]
    public void SeekingBackAndReplaying_PreservesTheParticleSpin()
    {
        var conversion = Convert(Source(3, new(90, 0, 0), new(0, 0, 800), "CW"));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.4f, Matrix.Identity); var first = simulation.Frame(Camera()).Single().Transform;
        simulation.Seek(.1f, Matrix.Identity); simulation.Advance(.3f, Matrix.Identity);
        var replay = simulation.Frame(Camera()).Single().Transform;
        Assert.That(Vector3.Distance(first.Up, replay.Up), Is.LessThan(.0001f));
    }

    [TestCase(.5f)]
    [TestCase(1f)]
    [TestCase(5f)]
    public void ConstantAngularDamping_ReducesTravelWithoutChangingLifetime(float damping)
    {
        var doc = VfxDocument.Read(Source(3, new(90, 0, 0), new(0, 0, 180), "CW"));
        doc.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "ParticleSpinDeceleration"), new XAttribute("value", damping)));
        var expectedDegrees = -180 * (1 - MathF.Exp(-damping)) / damping;
        var expected = Matrix.CreateRotationZ(MathHelper.ToRadians(expectedDegrees)) * Matrix.CreateRotationX(MathHelper.PiOver2);
        var actual = Frame(doc.Write(), 1);
        Assert.That(Vector3.Distance(Vector3.Normalize(actual.Up), expected.Up), Is.LessThan(.00001f));
    }

    [Test]
    public void ReferenceSpeedAndLifetimeBias_ApplyToTheSourceSpinClock()
    {
        var source = Source(3, new(90, 0, 0), new(0, 0, 800), "CW");
        var composition = VfxDocument.CreateComposition();
        var reference = composition.AddReference("vfx/spin-child.xml", VfxDocument.Read(source)); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", 2);
        foreach (var emitter in reference.Selection.Elements("emitter"))
        {
            emitter.SetAttributeValue("particle_lifetime_bias_min", .25f);
            emitter.SetAttributeValue("particle_lifetime_bias_max", .25f);
        }
        var conversion = TotalWarVfxConverter.Convert(new(composition.Write(), "vfx/spin-root.xml", _ => source));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.1f, Matrix.Identity);
        var frame = simulation.Frame(Camera()).Single().Transform;
        var expected = Matrix.CreateRotationZ(MathHelper.ToRadians(-160)) * Matrix.CreateRotationX(MathHelper.PiOver2);
        Assert.That(Vector3.Distance(Vector3.Normalize(frame.Up), expected.Up), Is.LessThan(.00001f));
    }

    [Test]
    public void MissingDirection_RetainsOneRandomDirectionPerBirth()
    {
        var source = Source(3, new(90, 0, 0), new(0, 0, 90), null);
        var early = Vector3.Normalize(Frame(source, .25f).Up); var late = Vector3.Normalize(Frame(source, .5f).Up);
        Assert.That(MathF.Abs(early.X), Is.EqualTo(MathF.Sin(MathHelper.PiOver4 / 2)).Within(.00001f));
        Assert.That(Math.Sign(early.X), Is.EqualTo(Math.Sign(late.X)));
    }

    [TestCase("CW", -1)]
    [TestCase("CCW", 1)]
    public void SpatialSingleAxisSpin_RespectsNativeDirectionOnEveryAxis(string direction, float sign)
    {
        for (var axis = 0; axis < 3; ++axis)
        {
            var spin = axis == 0 ? new Vector3(90, 0, 0) : axis == 1 ? new(0, 90, 0) : new(0, 0, 90);
            var doc = VfxDocument.Read(Source(3, Vector3.Zero, spin, null));
            doc.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "ParticleSpinDirection" + "XYZ"[axis]), new XAttribute("value", direction)));
            var frame = Frame(doc.Write(), .5f);
            var expected = Matrix.CreateFromYawPitchRoll(MathHelper.ToRadians(spin.Y * .5f * sign), MathHelper.ToRadians(spin.X * .5f * sign), MathHelper.ToRadians(spin.Z * .5f * sign));
            Assert.That(Vector3.Distance(Vector3.Normalize(frame.Right), expected.Right), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(Vector3.Normalize(frame.Up), expected.Up), Is.LessThan(.00001f));
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void SpinTransform_ReachesTheNativeRendererWithTheCalibratedPlane(int restriction)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var assembly = typeof(VfxParticleSpinTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("NativeUvAsymmetric.dds", StringComparison.Ordinal)))!;
            using var memory = new MemoryStream(); stream.CopyTo(memory); var texture = memory.ToArray();
            var device = graphics.GraphicsDevice; using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var camera = new Vector3(0, 8, -6);
            var parameters = new CommonShaderParameters(Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up), Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), 1, .1f, 100) * Matrix.CreateScale(-1, 1, 1), camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            Color[] Render(byte[] source)
            {
                using var preview = new EffekseerVfxPreview(device); preview.Reload(new(source, "vfx/spin.xml", _ => texture)); preview.Seek(.5f, Matrix.Identity);
                device.SetRenderTarget(target); device.Clear(Color.Black); preview.Draw(parameters); device.SetRenderTarget(null);
                var pixels = new Color[128 * 128]; target.GetData(pixels); return pixels;
            }
            var tilt = restriction == 0 ? 45 : restriction == 1 ? 30 : 90;
            var spun = Render(Source(restriction, new(tilt, 0, 0), new(0, 0, 90), "CW"));
            // Local spin precedes the birth tilt; extract equivalent native XYZ angles for a static reference.
            var expected = Matrix.CreateRotationZ(-MathHelper.PiOver4) * Matrix.CreateRotationX(MathHelper.ToRadians(tilt));
            var angles = new Vector3(MathF.Atan2(expected.M23, expected.M33), MathF.Asin(-expected.M13),
                MathF.Atan2(expected.M12, expected.M11));
            var reconstructed = Matrix.CreateRotationX(angles.X) * Matrix.CreateRotationY(angles.Y) * Matrix.CreateRotationZ(angles.Z);
            Assert.That(Vector3.Distance(reconstructed.Right, expected.Right), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(reconstructed.Up, expected.Up), Is.LessThan(.00001f));
            var reference = Render(VfxTranslationOrientationTests.Source(restriction, angles * (180 / MathHelper.Pi)));
            Assert.That(spun.Count(p => p.R > 10 || p.B > 10), Is.GreaterThan(100));
            Assert.That(spun.Zip(reference).Max(p => Math.Max(Math.Abs(p.First.R - p.Second.R), Math.Max(Math.Abs(p.First.G - p.Second.G), Math.Abs(p.First.B - p.Second.B)))), Is.LessThanOrEqualTo(1));
        }
        finally { graphics.Release(); }
    }

    internal static byte[] Source(int restriction, Vector3 orientation, Vector3 spin, string? direction)
    {
        var doc = VfxDocument.Read(VfxTranslationOrientationTests.Source(restriction, orientation));
        var emitter = doc.Root.Descendants("emitter").Single();
        foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" })
            emitter.Add(new XElement("property", new XAttribute("name", name), new XAttribute("x", spin.X), new XAttribute("y", spin.Y), new XAttribute("z", spin.Z)));
        if (direction != null) emitter.Add(new XElement("property", new XAttribute("name", "ParticleSpinDirectionZ"), new XAttribute("value", direction)));
        return doc.Write();
    }

    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(new(source, "vfx/spin.xml", _ => [1]));
    private static Matrix Frame(byte[] source, float seconds)
    {
        var conversion = Convert(source); Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(seconds, Matrix.Identity);
        return simulation.Frame(Camera()).Single().Transform;
    }
    private static CommonShaderParameters Camera() => new(Matrix.Identity, Matrix.Identity, new(0, 3, -10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
}
