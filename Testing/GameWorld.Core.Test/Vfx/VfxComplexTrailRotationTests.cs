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

public class VfxComplexTrailRotationTests
{
    private const double Tick = 1.0 / 120;
    private const float Tolerance = .00001f;
    private static readonly Vector3 BirthAngles = new(15, -30, 17);
    private static readonly Vector3 LocalSpin = new(80, 120, 180);
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(.3f, "CCW", 1)]
    [TestCase(1f, "CW", -1)]
    [TestCase(4f, "CCW", 1)]
    public void ConstantDamping_WithTiltedMultiAxisSpin_DecaysOnlyWorldZ(float damping, string direction, int sign)
    {
        var bytes = Source(dampingStart: damping, dampingEnd: damping, direction: direction);
        var conversion = Convert(bytes);
        Assert.That(conversion.Emitters.Single().SpinMax, Is.EqualTo(LocalSpin));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.75f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.75f, 4, 1, LocalSpin * sign, Birth(), damping, damping));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase(.4f, .4f)]
    [TestCase(1.3f, 1.3f)]
    public void OrdinaryModels_FollowTheirOwnNativeUniformDamping(float start, float end)
    {
        var source = VfxDocument.Read(Source(dampingStart: start, dampingEnd: end));
        var modelDefinition = VfxDocument.Read(VfxModelVariantTests.Source(1));
        Emitter(source).SetAttributeValue("type", "continuous_rigid");
        Emitter(source).Add(new XElement(modelDefinition.Root.Descendants("property_rigid_data").Single()));
        Set(source.Root.Descendants("modifier").Single(m => (string?)m.Attribute("fragment") == "vs_translation"), "restriction", "0");
        var conversion = TotalWarVfxConverter.Convert(new(source.Write(), "diagnostic/legacy-model-spin.xml", VfxModelVariantTests.Resolve));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Models, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters.Single().Kind, Is.EqualTo(1));
        Assert.That(conversion.Emitters.Single().ComplexSpinDamping, Is.False);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.75f, Matrix.Identity);
        // The rigid CPU path damps XYZ together, independently of the sprite's world-Z-only rule.
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.75f, 4, 1, LocalSpin, Birth(), start, end, uniformDamping: true));
    }

    [Test]
    public void ConstantDamping_PreservesWorldXAngularVelocity()
    {
        var source = Source(spin: new(180, 0, 0), birth: Vector3.Zero, dampingStart: 2, dampingEnd: 2);
        var simulation = Simulation(source); simulation.Seek(.9f, Matrix.Identity);
        var actual = simulation.Frame(Camera).Single().Transform;
        Near(actual, Oracle(.9f, 4, 1, new(180, 0, 0), Matrix.Identity, 2, 2));
        var oldAngle = Math.PI * (1 - Math.Exp(-2 * .9)) / 2;
        Assert.That(Vector3.Distance(actual.Up, Matrix.CreateRotationX((float)oldAngle).Up), Is.GreaterThan(.5f));
    }

    [TestCase(0f, .8f)]
    [TestCase(1.4f, .1f)]
    public void DynamicDamping_WithTiltedMultiAxisSpin_RemainsExplicitlyUnsupported(float start, float end)
    {
        var conversion = Convert(Source(dampingStart: start, dampingEnd: end));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Single().SpinMin, Is.EqualTo(Vector3.Zero));
            Assert.That(conversion.Emitters.Single().SpinMax, Is.EqualTo(Vector3.Zero));
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration" && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
        });
    }

    [Test]
    public void TiltedLocalSpin_BecomesWorldXYZBeforeWorldZDamping()
    {
        var birth = new Vector3(45, 0, 0); var spin = new Vector3(90, 0, 180);
        var simulation = Simulation(Source(spin: spin, birth: birth)); simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, 4, 1, spin, Birth(birth), .4, .4));
    }

    [TestCase("WORLD_SPACE", false)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", true)]
    public void IndependentComplexSpin_DoesNotInheritLaterRootRotation(string space, bool followsPosition)
    {
        var simulation = Simulation(Source(space: space)); simulation.Seek(0, Matrix.Identity);
        var root = Matrix.CreateRotationX(.6f) * Matrix.CreateRotationY(-.4f) * Matrix.CreateTranslation(3, 2, 1);
        simulation.Seek(.4f, root);
        var actual = simulation.Frame(Camera).Single().Transform;
        Near(actual, Oracle(.4f, 4, 1, LocalSpin, Birth(), .4, .4));
        Assert.That(actual.Translation, Is.EqualTo(followsPosition ? root.Translation : Vector3.Zero));
    }

    [Test]
    public void RelativeBirth_TransformsLocalAngularVelocityWithTheCompleteBirthBasis()
    {
        var document = VfxDocument.Read(Source()); var emitter = Emitter(document);
        emitter.Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS"));
        emitter.Add(new XElement("property", new XAttribute("name", "EmitterOrientation"), VectorCurve(new(0, 33, 0))));
        var root = Matrix.CreateRotationX(.6f) * Matrix.CreateRotationY(-.4f);
        var birth = Birth() * Matrix.CreateRotationY(MathHelper.ToRadians(33)) * root;
        var simulation = Simulation(document.Write()); simulation.Seek(0, root); simulation.Seek(.625f, root);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, 4, 1, LocalSpin, birth, .4, .4));
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void ReferenceSpeed_UsesSourceSecondsForComplexAngularState(float speed)
    {
        var child = VfxDocument.Read(Source()); var parent = VfxDocument.CreateComposition();
        var reference = parent.AddReference("complex-spin-child", child); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/root.xml", _ => child.Write()));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, 4, speed, LocalSpin, Birth(), .4, .4));
    }

    [Test]
    public void SeekingBackAndPartitionedAdvance_RestoreTheSameAngularState()
    {
        var simulation = Simulation(Source()); simulation.Seek(.637f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single();
        simulation.Seek(.11f, Matrix.Identity);
        foreach (var dt in new[] { .127f, .2f, .2f }) simulation.Advance(dt, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, first.Transform);
        Near(first.Transform, Oracle(.637f, 4, 1, LocalSpin, Birth(), .4, .4));
    }

    [Test]
    public void PartialFrames_DoNotCommitAngularStateOrChangeTheNextTick()
    {
        var simulation = Simulation(Source()); simulation.Seek(.004f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single();
        Near(first.Transform, Oracle(.004f, 4, 1, LocalSpin, Birth(), .4, .4));
        for (var index = 0; index < 8; ++index) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(first));
        simulation.Seek(.007f, Matrix.Identity); simulation.Frame(Camera);
        simulation.Seek(.012f, Matrix.Identity);
        var direct = Simulation(Source()); direct.Seek(.012f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, direct.Frame(Camera).Single().Transform);
        Near(direct.Frame(Camera).Single().Transform, Oracle(.012f, 4, 1, LocalSpin, Birth(), .4, .4));
    }

    [Test]
    public void FiniteEmitter_DoesNotResetSpinningParticlesBeforeTheirOwnDeath()
    {
        var document = VfxDocument.Read(Source(life: .5f)); var emitter = Emitter(document);
        Set(emitter, "InfiniteLife", "false");
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", .1), new XAttribute("max", .1)));
        var simulation = Simulation(document.Write()); simulation.Seek(.4f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.4f, .5f, 1, LocalSpin, Birth(), .4, .4));
        simulation.Seek(.51f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    [TestCase(.4f)]
    [TestCase(2f)]
    public void NewAngularPath_DoesNotShiftNonAngularBirthRandomness(float damping)
    {
        var source = VfxDocument.Read(Source(dampingStart: damping, dampingEnd: damping));
        var emitter = Emitter(source);
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "EmissionRateCurve").Value = Curve("x", 8);
        Set(emitter, "MaximumParticles", "32");
        emitter.Add(new XElement("property", new XAttribute("name", "SpawnAreaBox"), Curve("xyz", 3)));
        source.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_cell_texture"),
            Property("number_of_cells_x", 2), Property("number_of_cells_y", 2), Property("randomize_cell", "true")));
        var reference = VfxDocument.Read(source.Write());
        Emitter(reference).Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value = Curve("x", 0);
        var actual = Simulation(source.Write()); actual.Seek(1.1f, Matrix.Identity);
        var baseline = Simulation(reference.Write()); baseline.Seek(1.1f, Matrix.Identity);
        var frame = actual.Frame(Camera); var expected = baseline.Frame(Camera);
        Assert.That(frame, Has.Length.EqualTo(expected.Length)); Assert.That(frame.Length, Is.GreaterThan(4));
        for (var index = 0; index < frame.Length; ++index)
        {
            Assert.Multiple(() =>
            {
                Assert.That(frame[index].Transform.Translation, Is.EqualTo(expected[index].Transform.Translation));
                Assert.That(frame[index].Colour, Is.EqualTo(expected[index].Colour));
                Assert.That(frame[index].UV, Is.EqualTo(expected[index].UV));
                Assert.That(frame[index].Time, Is.EqualTo(expected[index].Time));
                Assert.That(frame[index].AppearancePhase, Is.EqualTo(expected[index].AppearancePhase));
            });
        }
    }

    [TestCase("full-local")]
    [TestCase("relative")]
    [TestCase("terrain-relative")]
    [TestCase("events")]
    [TestCase("bezier")]
    [TestCase("negative")]
    [TestCase("too-large")]
    public void UnmappedComplexDynamicCombinations_KeepTheirDiagnosticAndZeroSpin(string combination)
    {
        var document = VfxDocument.Read(Source(dampingStart: .2f, dampingEnd: .8f)); var emitter = Emitter(document);
        if (combination == "full-local") Set(emitter, "ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ROTATION");
        if (combination == "relative") emitter.Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS"));
        if (combination == "terrain-relative") emitter.Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_TERRAIN_NORMAL"));
        if (combination == "events") emitter.Add(new XElement("property_event", new XAttribute("name", "OnParticleSpawn"), new XAttribute("vfx_name", "child")));
        if (combination == "bezier") emitter.Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value =
            "x_min,8405056,0;0,0.2,linear,bezier_c,-0.2,0,0.2,0,1;1,1,sine,linear,-0.2,0,0.2,0,1:x,0;0,0";
        if (combination == "negative") emitter.Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value = Curve("x", -.01, .8);
        if (combination == "too-large") emitter.Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value = Curve("x", 0, 5.01);
        var converted = Convert(document.Write());
        Assert.Multiple(() =>
        {
            Assert.That(converted.Emitters.Single().SpinMin, Is.EqualTo(Vector3.Zero));
            Assert.That(converted.Emitters.Single().SpinMax, Is.EqualTo(Vector3.Zero));
            Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration" && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
        });
    }

    [Test]
    public void ExistingUndampedMultiAxisPath_KeepsItsContinuousRotation()
    {
        var simulation = Simulation(Source(dampingStart: 0, dampingEnd: 0)); simulation.Seek(.625f, Matrix.Identity);
        var magnitude = LocalSpin.Length();
        var expected = Matrix.CreateFromAxisAngle(LocalSpin / magnitude, MathHelper.ToRadians(magnitude * .625f)) * Birth();
        Near(simulation.Frame(Camera).Single().Transform, expected);
    }

    [Test]
    public void ExistingConstantPureZPath_KeepsItsAnalyticalConvention()
    {
        var simulation = Simulation(Source(spin: new(0, 0, 180), dampingStart: .4f, dampingEnd: .4f)); simulation.Seek(.625f, Matrix.Identity);
        var angle = 180 * (1 - Math.Exp(-.4 * .625)) / .4;
        Near(simulation.Frame(Camera).Single().Transform, Matrix.CreateRotationZ(MathHelper.ToRadians((float)angle)) * Birth());
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void ComplexSpriteRotation_ReachesTheGpuAsTheIndependentStaticPose(bool reflected)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Native offscreen rendering requires explicit opt-in.");
        const float time = .75f;
        var expected = Oracle(time, 4, 1, LocalSpin, Birth(), .4, .4);
        var radians = new Vector3(MathF.Atan2(expected.M23, expected.M33), MathF.Asin(-expected.M13),
            MathF.Atan2(expected.M12, expected.M11));
        Near(Birth(radians * (180 / MathHelper.Pi)), expected);
        var actualSource = VfxDocument.Read(Source());
        var referenceSource = VfxDocument.Read(actualSource.Write()); var referenceEmitter = Emitter(referenceSource);
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" }) SetVector(referenceEmitter, name, radians * (180 / MathHelper.Pi));
        foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" }) SetVector(referenceEmitter, name, Vector3.Zero);
        referenceEmitter.Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value = Curve("x", 0);
        var texture = VfxModelVariantPixelTests.Dds(Color.White);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++)
        {
            var colour = x < 2 ? y < 2 ? Color.Red : Color.Blue : y < 2 ? Color.Lime : Color.Yellow;
            var offset = 128 + (y * 4 + x) * 4;
            texture[offset] = colour.R; texture[offset + 1] = colour.G; texture[offset + 2] = colour.B;
        }
        byte[]? Resolve(string path) => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? texture : VfxModelVariantTests.Resolve(path);
        VfxPreviewSnapshot Snapshot(byte[] bytes) => new(bytes, "diagnostic/complex-trail-rotation.xml", Resolve);
        var conversion = TotalWarVfxConverter.Convert(Snapshot(actualSource.Write()));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        Assert.That(conversion.Emitters.Single().Kind, Is.EqualTo(0));
        Assert.That(conversion.Models, Is.Empty);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(time, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, expected);
        var targetPoint = Vector3.Zero;
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
            var actual = Render(actualSource.Write()); var reference = Render(referenceSource.Write());
            Assert.That(reference.Count(p => p.X + p.Y + p.Z > .05f), Is.GreaterThan(100), "The independent textured pose must be visibly rendered.");
            Assert.That(actual.Zip(reference).Max(p => Vector4.Distance(p.First, p.Second)), Is.LessThanOrEqualTo(Tolerance),
                "The full float RGBA image must agree with the independently integrated static pose.");
        }
        finally { graphics.Release(); }
    }

    // A normalized quaternion Euler step is an exact Rodrigues turn of 2*atan(|omega|*dt/2).
    // Double-precision matrices provide an oracle independent of the product's quaternion code.
    private static Matrix Oracle(float time, float life, float sourceSpeed, Vector3 localDegrees,
        Matrix birth, double dampingStart, double dampingEnd, bool uniformDamping = false)
    {
        var rotation = Basis(birth); var omega = Transform(new[] { (double)localDegrees.X, localDegrees.Y, localDegrees.Z }, rotation)
            .Select(value => value * Math.PI / 180).ToArray();
        var committed = 0.0;
        void Step(double target, double dt)
        {
            var sourceDt = dt * sourceSpeed;
            var phase = Math.Clamp(target * sourceSpeed / (life + .000001), 0, 1);
            var decay = Math.Exp(-(dampingStart + (dampingEnd - dampingStart) * phase) * sourceDt);
            if (uniformDamping) { omega[0] *= decay; omega[1] *= decay; }
            omega[2] *= decay;
            var speed = Math.Sqrt(omega.Sum(value => value * value));
            if (speed == 0) return;
            var x = omega[0] / speed; var y = omega[1] / speed; var z = omega[2] / speed;
            var angle = 2 * Math.Atan(speed * sourceDt / 2); var c = Math.Cos(angle); var s = Math.Sin(angle); var t = 1 - c;
            var delta = new[,] { { c + x*x*t, x*y*t + z*s, x*z*t - y*s },
                { x*y*t - z*s, c + y*y*t, y*z*t + x*s }, { x*z*t + y*s, y*z*t - x*s, c + z*z*t } };
            rotation = Multiply(rotation, delta);
        }
        while (committed + Tick <= time + .000001) { committed += Tick; Step(committed, Tick); }
        if (time > committed) Step(time, time - committed);
        return new((float)rotation[0,0], (float)rotation[0,1], (float)rotation[0,2], 0,
            (float)rotation[1,0], (float)rotation[1,1], (float)rotation[1,2], 0,
            (float)rotation[2,0], (float)rotation[2,1], (float)rotation[2,2], 0, 0, 0, 0, 1);
    }

    private static double[,] Basis(Matrix m) => new[,] { { (double)m.M11, m.M12, m.M13 }, { (double)m.M21, m.M22, m.M23 }, { (double)m.M31, m.M32, m.M33 } };
    private static double[] Transform(double[] v, double[,] m) => Enumerable.Range(0, 3).Select(c => Enumerable.Range(0, 3).Sum(r => v[r] * m[r,c])).ToArray();
    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var result = new double[3,3];
        for (var r = 0; r < 3; r++) for (var c = 0; c < 3; c++) for (var k = 0; k < 3; k++) result[r,c] += a[r,k] * b[k,c];
        return result;
    }
    private static Matrix Birth(Vector3? angles = null)
    {
        var v = angles ?? BirthAngles;
        return Matrix.CreateRotationX(MathHelper.ToRadians(v.X)) * Matrix.CreateRotationY(MathHelper.ToRadians(v.Y)) * Matrix.CreateRotationZ(MathHelper.ToRadians(v.Z));
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
    private static TotalWarVfxSimulation Simulation(byte[] bytes) => new(Convert(bytes));
    private static VfxConversion Convert(byte[] bytes)
    {
        var result = TotalWarVfxConverter.Convert(new(bytes, "vfx/complex-trail-spin.xml", _ => [1]));
        Assert.That(result.Emitters, Has.Count.EqualTo(1), string.Join(';', result.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        return result;
    }
    private static XElement Emitter(VfxDocument doc) => doc.Root.Descendants("emitter").Single();
    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name),
        new XAttribute("value", value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value));
    private static void Set(XElement emitter, string name, string value) => emitter.Elements().Single(e => (string?)e.Attribute("name") == name).SetAttributeValue("value", value);
    private static XElement VectorProperty(string name, Vector3 value) => new("property", new XAttribute("name", name), new XAttribute("x", value.X), new XAttribute("y", value.Y), new XAttribute("z", value.Z));
    private static void SetVector(XElement emitter, string name, Vector3 value) => emitter.Elements().Single(e => (string?)e.Attribute("name") == name).ReplaceWith(VectorProperty(name, value));
    private static string Curve(string axes, double start, double? end = null) => string.Join('|', axes.Select(axis =>
        $"{axis}_min,0,0;0,{start.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1" +
        (end.HasValue && end.Value != start ? $";1,{end.Value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1" : ""))) + ":" +
        string.Join('|', axes.Select((axis, index) => $"{axis},0;{index},{index}"));
    private static string VectorCurve(Vector3 v) => string.Join('|', new[] { ("x", v.X), ("y", v.Y), ("z", v.Z) }
        .Select(pair => $"{pair.Item1}_min,0,0;0,{pair.Item2.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":x,0;0,0|y,0;1,1|z,0;2,2";
    private static byte[] Source(string direction = "CCW", string space = "WORLD_SPACE", float life = 4,
        float dampingStart = .4f, float dampingEnd = .4f, Vector3? spin = null, Vector3? birth = null)
    {
        var emitter = new XElement("emitter", new XAttribute("id", "complex-spin"), new XAttribute("type", "continuous_mesh"),
            Property("InfiniteLife", "true"), Property("MaximumParticles", 1), Property("ParticleSimulationSpace", space),
            new XElement("property", new XAttribute("name", "EmissionRateCurve"), Curve("x", .1)),
            new XElement("property", new XAttribute("name", "ParticleLifeTime"), Curve("x", life)),
            new XElement("property", new XAttribute("name", "ParticleScale"), Curve("xyz", 1)),
            VectorProperty("ParticleOrientationMin", birth ?? BirthAngles), VectorProperty("ParticleOrientationMax", birth ?? BirthAngles),
            VectorProperty("ParticleSpin_Min", spin ?? LocalSpin), VectorProperty("ParticleSpin_Max", spin ?? LocalSpin),
            Property("ParticleSpinDirectionX", direction), Property("ParticleSpinDirectionY", direction), Property("ParticleSpinDirectionZ", direction),
            new XElement("property", new XAttribute("name", "ParticleSpinDeceleration"), Curve("x", dampingStart, dampingEnd)));
        var quality = new XElement("quality", new XAttribute("value", "0"),
            new XElement("modifier", new XAttribute("fragment", "vs_translation"), Property("restriction", 3)),
            new XElement("modifier", new XAttribute("fragment", "ps_texture"), new XElement("texture_property", new XAttribute("name", "diffuse"), new XAttribute("path", "vfx/test.dds"))));
        return Encoding.UTF8.GetBytes(new XElement("vfx", new XAttribute("revision", 2), new XElement("emitters", emitter),
            new XElement("behaviour_chains", new XElement("behaviour_chain", new XAttribute("id", "complex-spin"), quality))).ToString(SaveOptions.DisableFormatting));
    }
}
