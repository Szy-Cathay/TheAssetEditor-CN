#nullable enable
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelQuaternionSpinTests
{
    private const double Tick = 1.0 / 120;
    private const float Tolerance = .00001f;
    private static readonly Vector3 BirthAngles = new(-35, 50, 17);
    private static readonly Vector3 Spin = new(150, 220, 300);
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(0f, "CCW", 1)]
    [TestCase(.15f, "CCW", 1)]
    [TestCase(.4f, "CCW", 1)]
    [TestCase(5f, "CCW", 1)]
    [TestCase(0f, "CW", -1)]
    [TestCase(.15f, "CW", -1)]
    public void MultiAxisModels_FollowTheNativeAllAxisDecayAndQuaternionStep(float damping, string direction, int sign)
    {
        var source = Source(damping: damping, direction: direction);
        var conversion = Convert(source);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, 1, Spin * sign, Birth(), damping));
        Assert.Multiple(() =>
        {
            Assert.That(UsesNativeModelSpin(conversion.Emitters.Single()), Is.True);
            Assert.That(conversion.Emitters.Single().ComplexSpinDamping, Is.False);
            Assert.That(conversion.Emitters.Single().SpinMax, Is.EqualTo(Spin));
        });
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void WorldXModelSpin_IsDampedInsteadOfUsingTheSpriteWorldZOnlyRule()
    {
        var angular = new Vector3(180, 0, 0);
        var simulation = Simulation(Source(spin: angular, birth: Vector3.Zero, damping: 2));
        simulation.Seek(.9f, Matrix.Identity);
        var actual = simulation.Frame(Camera).Single().Transform;
        Near(actual, Oracle(.9f, 1, angular, Matrix.Identity, 2));
        var spriteWithoutXDamping = Matrix.CreateRotationX(MathHelper.ToRadians(180 * .9f));
        Assert.That(Vector3.Distance(actual.Up, spriteWithoutXDamping.Up), Is.GreaterThan(.5f));
    }

    [TestCase("WORLD_SPACE", false)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", true)]
    public void IndependentBirth_DoesNotAppendTheAbsoluteRootRotation(string space, bool followsPosition)
    {
        var simulation = Simulation(Source(space: space));
        var firstRoot = Matrix.CreateRotationX(.6f) * Matrix.CreateRotationY(-.4f);
        simulation.Seek(0, firstRoot);
        var laterRoot = Matrix.CreateRotationY(.8f) * Matrix.CreateTranslation(3, 2, 1);
        simulation.Seek(.4f, laterRoot);
        var actual = simulation.Frame(Camera).Single().Transform;
        Near(actual, Oracle(.4f, 1, Spin, Birth(), .15));
        Assert.That(actual.Translation, Is.EqualTo(followsPosition ? laterRoot.Translation : Vector3.Zero));
    }

    [Test]
    public void RelativeBirth_UsesTheComposedEmitterAndNodeBasisForOrientationAndAngularVelocity()
    {
        var source = VfxDocument.Read(Source()); var emitter = Emitter(source);
        emitter.Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS"));
        emitter.Add(new XElement("property", new XAttribute("name", "EmitterOrientation"), VectorCurve(new(0, 33, 0))));
        var root = Matrix.CreateRotationX(.6f) * Matrix.CreateRotationY(-.4f);
        var birth = Birth() * Matrix.CreateRotationY(MathHelper.ToRadians(33)) * root;
        var simulation = Simulation(source.Write()); simulation.Seek(0, root); simulation.Seek(.625f, root);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, 1, Spin, birth, .15));
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void ReferenceSpeed_UsesSourceSecondsForBothDecayAndRotation(float speed)
    {
        var child = VfxDocument.Read(Source()); var parent = VfxDocument.CreateComposition();
        var edit = parent.AddReference("model-spin-child", child); edit.Apply();
        edit.Selection.SetAttributeValue("simulation_speed_multiplier", speed);
        var converted = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/model-spin-parent.xml", path =>
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, speed, Spin, Birth(), .15));
        Assert.That(UsesNativeModelSpin(converted.Emitters.Single()), Is.True);
    }

    [Test]
    public void SeekingBackAndPartitioningAdvance_RebuildTheSameModelAngularState()
    {
        var simulation = Simulation(Source()); simulation.Seek(.637f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single();
        simulation.Seek(.11f, Matrix.Identity);
        foreach (var dt in new[] { .127f, .2f, .2f }) simulation.Advance(dt, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, first.Transform);
        Near(first.Transform, Oracle(.637f, 1, Spin, Birth(), .15));
    }

    [Test]
    public void PartialFrames_DoNotCommitTheRigidAngularState()
    {
        var simulation = Simulation(Source()); simulation.Seek(.004f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single();
        Near(first.Transform, Oracle(.004f, 1, Spin, Birth(), .15));
        for (var index = 0; index < 8; ++index) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(first));
        simulation.Seek(.007f, Matrix.Identity); simulation.Frame(Camera);
        simulation.Seek(.012f, Matrix.Identity);
        var direct = Simulation(Source()); direct.Seek(.012f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, direct.Frame(Camera).Single().Transform);
        Near(direct.Frame(Camera).Single().Transform, Oracle(.012f, 1, Spin, Birth(), .15));
    }

    [Test]
    public void FiniteEmitter_KeepsItsModelsRotatingUntilTheirOriginalLifetimeEnds()
    {
        var source = VfxDocument.Read(Source(life: .5f)); var emitter = Emitter(source);
        Set(emitter, "InfiniteLife", "false");
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", .05), new XAttribute("max", .05)));
        var simulation = Simulation(source.Write()); simulation.Seek(.4f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.4f, 1, Spin, Birth(), .15));
        simulation.Seek(.51f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    [Test]
    public void QuaternionState_DoesNotChangeNonAngularBirthRandomness()
    {
        var source = VfxDocument.Read(Source()); var emitter = Emitter(source);
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "EmissionRateCurve").Value = Curve("x", 8);
        Set(emitter, "MaximumParticles", "32");
        emitter.Add(new XElement("property", new XAttribute("name", "SpawnAreaBox"), Curve("xyz", 3)));
        var current = Convert(source.Write()); var legacy = Convert(source.Write());
        typeof(VfxPreviewEmitter).GetProperty("ModelQuaternionSpin")?.SetValue(legacy.Emitters.Single(), false);
        var simulation = new TotalWarVfxSimulation(current); var baseline = new TotalWarVfxSimulation(legacy);
        simulation.Seek(1.1f, Matrix.Identity); baseline.Seek(1.1f, Matrix.Identity);
        var actual = simulation.Frame(Camera); var expected = baseline.Frame(Camera);
        Assert.That(actual, Has.Length.EqualTo(expected.Length)); Assert.That(actual.Length, Is.GreaterThan(4));
        for (var index = 0; index < actual.Length; ++index)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual[index].Transform.Translation, Is.EqualTo(expected[index].Transform.Translation));
                Assert.That(actual[index].Colour, Is.EqualTo(expected[index].Colour));
                Assert.That(actual[index].UV, Is.EqualTo(expected[index].UV));
                Assert.That(actual[index].Time, Is.EqualTo(expected[index].Time));
                Assert.That(actual[index].AppearancePhase, Is.EqualTo(expected[index].AppearancePhase));
            });
        }
    }

    [TestCase(0f)]
    [TestCase(.4f)]
    public void ExistingPureZModelPath_RetainsItsPreviousAnalyticalConvention(float damping)
    {
        var spin = new Vector3(0, 0, 180); var conversion = Convert(Source(spin: spin, damping: damping));
        Assert.That(UsesNativeModelSpin(conversion.Emitters.Single()), Is.False);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.625f, Matrix.Identity);
        var time = damping == 0 ? .625 : (1 - Math.Exp(-damping * .625)) / damping;
        Near(simulation.Frame(Camera).Single().Transform, Birth(BirthAngles + spin * (float)time));
    }

    [TestCase("events")]
    [TestCase("unknown-axis-direction")]
    public void UncalibratedCombinations_KeepTheExistingModelPath(string combination)
    {
        var source = VfxDocument.Read(Source()); var emitter = Emitter(source);
        if (combination == "events") emitter.Add(new XElement("property_event", new XAttribute("name", "OnParticleSpawn"), new XAttribute("vfx_name", "child")));
        if (combination == "unknown-axis-direction") Set(emitter, "ParticleSpinDirectionX", "unknown");
        Assert.That(UsesNativeModelSpin(Convert(source.Write()).Emitters.Single()), Is.False);
    }

    [TestCase(-.01f)]
    [TestCase(5.01f)]
    public void NativeModelDamping_DoesNotUseTheOldNonnegativeFiveLimit(float damping)
    {
        var source = Source(damping: damping); var conversion = Convert(source);
        Assert.That(UsesNativeModelSpin(conversion.Emitters.Single()), Is.True);
        Assert.That(conversion.Emitters.Single().SpinMax, Is.EqualTo(Spin));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, 1, Spin, Birth(), damping));
    }

    [TestCase(.1f, .8f)]
    [TestCase(1.4f, .1f)]
    public void DynamicModelDamping_PreservesTheAuthoredSpinForItsNativeUpdate(float start, float end)
    {
        var source = VfxDocument.Read(Source());
        Emitter(source).Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value = Curve("x", start, end);
        var conversion = Convert(source.Write()); var emitter = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(UsesNativeModelSpin(emitter), Is.True);
            Assert.That(emitter.SpinMin, Is.EqualTo(Spin));
            Assert.That(emitter.SpinMax, Is.EqualTo(Spin));
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration" && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void ModelQuaternionRotation_ReachesTheGpuAsItsIndependentStaticPose(bool reflected)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Native offscreen rendering requires explicit opt-in.");
        const float time = .625f;
        var expected = Oracle(time, 1, Spin, Birth(), .15);
        var radians = new Vector3(MathF.Atan2(expected.M23, expected.M33), MathF.Asin(-expected.M13),
            MathF.Atan2(expected.M12, expected.M11));
        var angles = radians * (180 / MathHelper.Pi);
        Near(Birth(angles), expected);
        var source = Source(); var reference = VfxDocument.Read(source); var emitter = Emitter(reference);
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
            emitter.Elements().Single(e => (string?)e.Attribute("name") == name).ReplaceWith(VectorProperty(name, angles));
        foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" })
            emitter.Elements().Single(e => (string?)e.Attribute("name") == name).ReplaceWith(VectorProperty(name, Vector3.Zero));
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value = Curve("x", 0);
        // A constant coloured texture isolates pose transmission from texture-filter quantization.
        // The independent double pose differs by a few float ulps after the Euler round trip.
        var texture = VfxModelVariantPixelTests.Dds(new Color(70, 180, 230, 155));
        byte[]? Resolve(string path) => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? texture : VfxModelVariantTests.Resolve(path);
        VfxPreviewSnapshot Snapshot(byte[] bytes) => new(bytes, "diagnostic/model-spin-pixels.xml", Resolve);
        var converted = TotalWarVfxConverter.Convert(Snapshot(source));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1)); Assert.That(converted.Models, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(time, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, expected);
        var targetPoint = Vector3.Transform(new(.35f, .35f, 0), expected);
        var eye = targetPoint + expected.Backward * 6 + expected.Right * .4f + expected.Up * .2f;
        var camera = Camera with { View = Matrix.CreateLookAt(eye, targetPoint, expected.Up),
            Projection = Matrix.CreateOrthographic(3, 3, .1f, 100) * Matrix.CreateScale(reflected ? -1 : 1, 1, 1), CameraPosition = eye };
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            Vector4[] Render(byte[] bytes)
            {
                using var preview = new EffekseerVfxPreview(device); preview.Reload(Snapshot(bytes)); preview.Seek(time, Matrix.Identity);
                using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                preview.Draw(camera); device.SetRenderTarget(null);
                var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
            }
            var actual = Render(source); var control = Render(reference.Write());
            Assert.That(control.Count(pixel => pixel.X + pixel.Y + pixel.Z > .05f), Is.GreaterThan(100));
            Assert.That(actual.Zip(control).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThanOrEqualTo(Tolerance),
                "Full float RGBA must match the independently integrated static model pose.");
        }
        finally { graphics.Release(); }
    }

    // Native rigid CPU integration uniformly decays omega, then its normalized Euler step
    // is an exact Rodrigues turn of 2*atan(|omega|*sourceDt/2), independent of quaternion code.
    private static Matrix Oracle(float time, float sourceSpeed, Vector3 localDegrees, Matrix birth, double damping)
    {
        var rotation = Basis(birth);
        var omega = Transform(new[] { (double)localDegrees.X, localDegrees.Y, localDegrees.Z }, rotation)
            .Select(value => value * Math.PI / 180).ToArray();
        var committed = 0.0;
        void Step(double dt)
        {
            var sourceDt = dt * sourceSpeed;
            var decay = Math.Exp(-damping * sourceDt);
            for (var axis = 0; axis < 3; ++axis) omega[axis] *= decay;
            var speed = Math.Sqrt(omega.Sum(value => value * value));
            if (speed == 0) return;
            var x = omega[0] / speed; var y = omega[1] / speed; var z = omega[2] / speed;
            var angle = 2 * Math.Atan(speed * sourceDt / 2); var c = Math.Cos(angle); var s = Math.Sin(angle); var t = 1 - c;
            var delta = new[,] { { c + x*x*t, x*y*t + z*s, x*z*t - y*s },
                { x*y*t - z*s, c + y*y*t, y*z*t + x*s }, { x*z*t + y*s, y*z*t - x*s, c + z*z*t } };
            rotation = Multiply(rotation, delta);
        }
        while (committed + Tick <= time + .000001) { committed += Tick; Step(Tick); }
        if (time > committed) Step(time - committed);
        return new((float)rotation[0,0], (float)rotation[0,1], (float)rotation[0,2], 0,
            (float)rotation[1,0], (float)rotation[1,1], (float)rotation[1,2], 0,
            (float)rotation[2,0], (float)rotation[2,1], (float)rotation[2,2], 0, 0, 0, 0, 1);
    }

    private static double[,] Basis(Matrix matrix) => new[,] { { (double)matrix.M11, matrix.M12, matrix.M13 },
        { (double)matrix.M21, matrix.M22, matrix.M23 }, { (double)matrix.M31, matrix.M32, matrix.M33 } };
    private static double[] Transform(double[] vector, double[,] matrix) => Enumerable.Range(0, 3)
        .Select(column => Enumerable.Range(0, 3).Sum(row => vector[row] * matrix[row, column])).ToArray();
    private static double[,] Multiply(double[,] first, double[,] second)
    {
        var result = new double[3, 3];
        for (var row = 0; row < 3; ++row) for (var column = 0; column < 3; ++column)
            for (var k = 0; k < 3; ++k) result[row, column] += first[row, k] * second[k, column];
        return result;
    }
    private static Matrix Birth(Vector3? angles = null)
    {
        var value = angles ?? BirthAngles;
        return Matrix.CreateRotationX(MathHelper.ToRadians(value.X)) * Matrix.CreateRotationY(MathHelper.ToRadians(value.Y))
            * Matrix.CreateRotationZ(MathHelper.ToRadians(value.Z));
    }
    private static void Near(Matrix actual, Matrix expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(actual.Right, expected.Right), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Up, expected.Up), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Backward, expected.Backward), Is.LessThan(Tolerance));
        });
    }
    private static bool UsesNativeModelSpin(VfxPreviewEmitter emitter) =>
        typeof(VfxPreviewEmitter).GetProperty("ModelQuaternionSpin")?.GetValue(emitter) is true;
    private static TotalWarVfxSimulation Simulation(byte[] bytes) => new(Convert(bytes));
    private static VfxConversion Convert(byte[] bytes)
    {
        var result = TotalWarVfxConverter.Convert(new(bytes, "diagnostic/model-quaternion-spin.xml", VfxModelVariantTests.Resolve));
        Assert.That(result.Emitters, Has.Count.EqualTo(1), string.Join(';', result.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        Assert.That(result.Models, Has.Count.EqualTo(1));
        Assert.That(result.Emitters.Single().Kind, Is.EqualTo(1));
        return result;
    }
    private static XElement Emitter(VfxDocument source) => source.Root.Descendants("emitter").Single();
    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name),
        new XAttribute("value", value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value));
    private static XElement VectorProperty(string name, Vector3 value) => new("property", new XAttribute("name", name),
        new XAttribute("x", value.X), new XAttribute("y", value.Y), new XAttribute("z", value.Z));
    private static void Set(XElement emitter, string name, string value) => emitter.Elements()
        .Single(e => (string?)e.Attribute("name") == name).SetAttributeValue("value", value);
    private static string Curve(string axes, double start, double? end = null) => string.Join('|', axes.Select(axis =>
        $"{axis}_min,0,0;0,{start.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1" +
        (end.HasValue && end.Value != start ? $";1,{end.Value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1" : ""))) + ":" +
        string.Join('|', axes.Select((axis, index) => $"{axis},0;{index},{index}"));
    private static string VectorCurve(Vector3 value) => string.Join('|', new[] { ("x", value.X), ("y", value.Y), ("z", value.Z) }
        .Select(pair => $"{pair.Item1}_min,0,0;0,{pair.Item2.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":x,0;0,0|y,0;1,1|z,0;2,2";
    private static byte[] Source(string direction = "CCW", string space = "WORLD_SPACE", float life = 4, float damping = .15f,
        Vector3? spin = null, Vector3? birth = null)
    {
        var rigid = VfxDocument.Read(VfxModelVariantTests.Source(1)).Root.Descendants("property_rigid_data").Single();
        var emitter = new XElement("emitter", new XAttribute("id", "rigid-spin"), new XAttribute("type", "continuous_rigid"),
            Property("InfiniteLife", "true"), Property("MaximumParticles", 1), Property("ParticleSimulationSpace", space),
            new XElement("property", new XAttribute("name", "EmissionRateCurve"), Curve("x", .1)),
            new XElement("property", new XAttribute("name", "ParticleLifeTime"), Curve("x", life)),
            new XElement("property", new XAttribute("name", "ParticleScale"), Curve("xyz", 1)),
            VectorProperty("ParticleOrientationMin", birth ?? BirthAngles), VectorProperty("ParticleOrientationMax", birth ?? BirthAngles),
            VectorProperty("ParticleSpin_Min", spin ?? Spin), VectorProperty("ParticleSpin_Max", spin ?? Spin),
            Property("ParticleSpinDirectionX", direction), Property("ParticleSpinDirectionY", direction), Property("ParticleSpinDirectionZ", direction),
            new XElement("property", new XAttribute("name", "ParticleSpinDeceleration"), Curve("x", damping)), new XElement(rigid));
        var quality = new XElement("quality", new XAttribute("value", "0"),
            new XElement("modifier", new XAttribute("fragment", "vs_translation"), Property("restriction", 0)));
        return Encoding.UTF8.GetBytes(new XElement("vfx", new XAttribute("revision", 2), new XElement("emitters", emitter),
            new XElement("behaviour_chains", new XElement("behaviour_chain", new XAttribute("id", "rigid-spin"), quality))).ToString(SaveOptions.DisableFormatting));
    }
}
